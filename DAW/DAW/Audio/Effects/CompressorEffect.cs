namespace DAW.Audio.Effects;

/// <summary>
/// Dynamic Range Compressor effect.
/// </summary>
public class CompressorEffect : AudioEffect
{
    private double _threshold = -20;
    private double _ratio = 4;
    private double _attack = 10;
    private double _release = 100;
    private double _makeupGain;
    private double _knee = 6;
    
    // Envelope follower state
    private double _envelope;
    private double _gainReduction;

    public CompressorEffect()
    {
        Name = "Compressor";
    }

    public override string EffectType => "Compressor";
    public override string Icon => "📉";

    /// <summary>Threshold in dB (-60 to 0)</summary>
    public double Threshold
    {
        get => _threshold;
        set => SetField(ref _threshold, Math.Clamp(value, -60, 0));
    }

    /// <summary>Compression ratio (1:1 to 20:1)</summary>
    public double Ratio
    {
        get => _ratio;
        set => SetField(ref _ratio, Math.Clamp(value, 1, 20));
    }

    /// <summary>Attack time in ms (0.1-100)</summary>
    public double Attack
    {
        get => _attack;
        set => SetField(ref _attack, Math.Clamp(value, 0.1, 100));
    }

    /// <summary>Release time in ms (10-1000)</summary>
    public double Release
    {
        get => _release;
        set => SetField(ref _release, Math.Clamp(value, 10, 1000));
    }

    /// <summary>Makeup gain in dB (0-24)</summary>
    public double MakeupGain
    {
        get => _makeupGain;
        set => SetField(ref _makeupGain, Math.Clamp(value, 0, 24));
    }

    /// <summary>Knee width in dB (0-12)</summary>
    public double Knee
    {
        get => _knee;
        set => SetField(ref _knee, Math.Clamp(value, 0, 12));
    }
    
    /// <summary>Current gain reduction in dB (for metering)</summary>
    public double GainReduction => _gainReduction;

    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        if (!IsEnabled) return;

        double attackCoef = Math.Exp(-1.0 / (sampleRate * Attack / 1000.0));
        double releaseCoef = Math.Exp(-1.0 / (sampleRate * Release / 1000.0));
        double makeupLinear = Math.Pow(10, MakeupGain / 20.0);

        int endIndex = offset + count;
        for (int i = offset; i < endIndex; i += channels)
        {
            // Get peak across channels
            double peak = 0;
            for (int ch = 0; ch < channels && (i + ch) < endIndex; ch++)
            {
                peak = Math.Max(peak, Math.Abs(buffer[i + ch]));
            }

            // Convert to dB
            double inputDb = peak > 0 ? 20.0 * Math.Log10(peak) : -100;

            // Compute gain reduction with soft knee
            double overDb = inputDb - Threshold;
            double gainReductionDb;
            
            if (Knee > 0 && overDb > -Knee / 2 && overDb < Knee / 2)
            {
                // Soft knee region
                double kneeRatio = (overDb + Knee / 2) / Knee;
                gainReductionDb = kneeRatio * kneeRatio * Knee / 2 * (1 - 1 / Ratio);
            }
            else if (overDb > 0)
            {
                // Above threshold
                gainReductionDb = overDb * (1 - 1 / Ratio);
            }
            else
            {
                gainReductionDb = 0;
            }

            // Apply envelope follower
            double targetEnvelope = gainReductionDb;
            if (targetEnvelope > _envelope)
            {
                _envelope = attackCoef * _envelope + (1 - attackCoef) * targetEnvelope;
            }
            else
            {
                _envelope = releaseCoef * _envelope + (1 - releaseCoef) * targetEnvelope;
            }

            // Update gain reduction for metering
            _gainReduction = _envelope;

            // Apply gain reduction
            double gainLinear = Math.Pow(10, -_envelope / 20.0) * makeupLinear;

            for (int ch = 0; ch < channels && (i + ch) < endIndex; ch++)
            {
                buffer[i + ch] = (float)Math.Clamp(buffer[i + ch] * gainLinear, -1.0, 1.0);
            }
        }
        
        OnPropertyChanged(nameof(GainReduction));
    }

    public override void Reset()
    {
        _envelope = 0;
        _gainReduction = 0;
    }
}
