using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects;

/// <summary>Meter display mode for the compressor gauge.</summary>
public enum CompressorMeterMode { GainReduction, Input, Output }

/// <summary>
/// FET-style compressor inspired by the Universal Audio 1176.
/// Optimized hot path: precomputed coefficients, no per-sample allocations,
/// minimal dB↔linear conversions via fast approximations.
/// </summary>
public class CompressorEffect : AudioEffect
{
    // ── Backing fields ────────────────────────────────────────────────────

    private double _inputGain;
    private double _outputGain;
    private double _attack = 10;
    private double _release = 100;
    private double _ratio = 4;
    private double _threshold = -20;
    private double _makeupGain;
    private double _knee = 3;
    private CompressorMeterMode _meterMode;

    // ── Precomputed coefficients (updated on param change) ────────────────

    private double _inputLinear = 1.0;
    private double _outputLinear = 1.0;
    private double _oneMinusInvRatio = 0.75; // 1 - 1/ratio
    private double _halfKnee = 1.5;
    private double _negHalfKnee = -1.5;
    private double _kneeDiv = 1.0 / 3.0; // 1/knee
    private double _kneeFactor;             // knee/2 * (1 - 1/ratio)

    // ── Envelope state ────────────────────────────────────────────────────

    private double _envelope;

    // ── Metering (volatile for cross-thread reads) ────────────────────────

    private volatile float _grSmooth;
    private volatile float _inSmooth = -100f;
    private volatile float _outSmooth = -100f;
    private int _meterSkipCounter;

    /// <summary>1176-style selectable ratios.</summary>
    public static readonly double[] PresetRatios = [4, 8, 12, 20];

    public CompressorEffect()
    {
        Name = "1176 Compressor";
        RecalcCoefficients();
    }

    public override string EffectType => "Compressor";
    public override string Icon => "📉";

    // ── Public parameters ─────────────────────────────────────────────────

    public double InputGain
    {
        get => _inputGain;
        set { if (SetField(ref _inputGain, Math.Clamp(value, -12, 48))) RecalcCoefficients(); }
    }

    public double OutputGain
    {
        get => _outputGain;
        set { if (SetField(ref _outputGain, Math.Clamp(value, -24, 12))) RecalcCoefficients(); }
    }

    public double Attack
    {
        get => _attack;
        set => SetField(ref _attack, Math.Clamp(value, 0.02, 100));
    }

    public double Release
    {
        get => _release;
        set => SetField(ref _release, Math.Clamp(value, 10, 1200));
    }

    public double Ratio
    {
        get => _ratio;
        set { if (SetField(ref _ratio, Math.Clamp(value, 1, 20))) RecalcCoefficients(); }
    }

    public double Threshold
    {
        get => _threshold;
        set => SetField(ref _threshold, Math.Clamp(value, -60, 0));
    }

    public double MakeupGain
    {
        get => _makeupGain;
        set { if (SetField(ref _makeupGain, Math.Clamp(value, 0, 24))) RecalcCoefficients(); }
    }

    public double Knee
    {
        get => _knee;
        set { if (SetField(ref _knee, Math.Clamp(value, 0, 12))) RecalcCoefficients(); }
    }

    /// <summary>Which signal the VU meter displays.</summary>
    public CompressorMeterMode MeterMode
    {
        get => _meterMode;
        set => SetField(ref _meterMode, value);
    }

    // ── Metering (read-only) ──────────────────────────────────────────────

    public double GainReduction => _grSmooth;
    public double InputLevel => _inSmooth;
    public double OutputLevel => _outSmooth;

    // ── Precompute ────────────────────────────────────────────────────────

    private void RecalcCoefficients()
    {
        _inputLinear = DbToLinear(_inputGain);
        _outputLinear = DbToLinear(_outputGain + _makeupGain);
        _oneMinusInvRatio = 1.0 - 1.0 / Math.Max(_ratio, 1.0001);
        _halfKnee = _knee * 0.5;
        _negHalfKnee = -_halfKnee;
        _kneeDiv = _knee > 0.001 ? 1.0 / _knee : 0;
        _kneeFactor = _halfKnee * _oneMinusInvRatio;
    }

    // ── Audio processing (hot path) ───────────────────────────────────────

    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        if (!IsEnabled) return;

        double attackCoef = Math.Exp(-1.0 / (sampleRate * _attack / 1000.0));
        double releaseCoef = Math.Exp(-1.0 / (sampleRate * _release / 1000.0));
        double inLin = _inputLinear;
        double outLin = _outputLinear;
        double threshold = _threshold;
        double omInvR = _oneMinusInvRatio;
        double hKnee = _halfKnee;
        double nhKnee = _negHalfKnee;
        double kDiv = _kneeDiv;
        double kFactor = _kneeFactor;
        double env = _envelope;

        float peakIn = 0f, peakOut = 0f;
        double maxGr = 0;

        int end = offset + count;
        for (int i = offset; i < end; i += channels)
        {
            // Apply input gain + peak detect (fused loop)
            float peak = 0f;
            int limit = Math.Min(i + channels, end);
            for (int j = i; j < limit; j++)
            {
                float s = buffer[j] * (float)inLin;
                buffer[j] = s;
                float abs = Math.Abs(s);
                if (abs > peak) peak = abs;
            }
            if (peak > peakIn) peakIn = peak;

            // dB conversion (skip tiny signals)
            if (peak < 1e-8f) { env = releaseCoef * env; continue; }
            double inputDb = FastLog10(peak) * 20.0;

            // Gain reduction with soft knee
            double overDb = inputDb - threshold;
            double grDb;
            if (overDb <= nhKnee)
            {
                grDb = 0;
            }
            else if (overDb >= hKnee || kDiv == 0)
            {
                grDb = overDb * omInvR;
            }
            else
            {
                double kneeRatio = (overDb + hKnee) * kDiv;
                grDb = kneeRatio * kneeRatio * kFactor;
            }

            // FET-style program-dependent envelope
            if (grDb > env)
            {
                double progAttack = attackCoef * (1.0 - 0.3 * Math.Min(grDb * 0.05, 1.0));
                env = progAttack * env + (1.0 - progAttack) * grDb;
            }
            else
            {
                double rel = (env - grDb > 3.0) ? releaseCoef * 0.95 : releaseCoef;
                env = rel * env + (1.0 - rel) * grDb;
            }

            if (env > maxGr) maxGr = env;

            // Apply gain reduction + output gain
            double gainLinear = FastPow10(env * -0.05) * outLin;

            for (int j = i; j < limit; j++)
            {
                float s = (float)(buffer[j] * gainLinear);
                if (s > 1f) s = 1f; else if (s < -1f) s = -1f;
                buffer[j] = s;
                float abs = Math.Abs(s);
                if (abs > peakOut) peakOut = abs;
            }
        }

        _envelope = env;

        // Update metering (smoothed, only notify UI every ~4 buffers)
        const float fall = 0.92f;
        const float smooth = 0.85f;

        _grSmooth = (float)Math.Max(maxGr, _grSmooth * fall);

        float inDb2 = peakIn > 1e-8f ? (float)(FastLog10(peakIn) * 20.0) : -100f;
        float outDb2 = peakOut > 1e-8f ? (float)(FastLog10(peakOut) * 20.0) : -100f;
        _inSmooth = inDb2 > _inSmooth ? inDb2 : _inSmooth * smooth + inDb2 * (1f - smooth);
        _outSmooth = outDb2 > _outSmooth ? outDb2 : _outSmooth * smooth + outDb2 * (1f - smooth);

        // Throttle UI notifications to every ~4 buffers (~85ms at 512 samples/48kHz)
        if (++_meterSkipCounter >= 4)
        {
            _meterSkipCounter = 0;
            OnPropertyChanged(nameof(GainReduction));
            OnPropertyChanged(nameof(InputLevel));
            OnPropertyChanged(nameof(OutputLevel));
        }
    }

    public override void Reset()
    {
        _envelope = 0;
        _grSmooth = 0;
        _inSmooth = -100f;
        _outSmooth = -100f;
    }

    // ── Fast math approximations ──────────────────────────────────────────

    /// <summary>Fast log10 approximation (accurate to ~0.1% for audio range).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double FastLog10(double x)
    {
        // Use natural log * log10(e) — faster than Math.Log10 on most runtimes
        return Math.Log(x) * 0.4342944819032518;
    }

    /// <summary>Fast 10^x approximation using exp(x * ln10).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double FastPow10(double x)
    {
        return Math.Exp(x * 2.302585092994046);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DbToLinear(double db)
    {
        return FastPow10(db * 0.05);
    }
}
