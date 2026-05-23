using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects;

/// <summary>
/// Valhalla Delay–inspired stereo delay effect.
///
/// PARAMETERS (MVP)
/// ────────────────
/// Timing
///   DelayTime      – base delay in milliseconds (1–2000 ms)
///   Ratio          – right-channel time as fraction of left (0.25–4.0)
///                    1.0 = equal; 0.5 = R half as long as L (typical dotted)
///   TempoSync      – snap DelayTime to musical grid (future: BPM-driven)
///
/// Feedback
///   Feedback       – main feedback amount 0..0.95 (% echoes)
///   CrossFeedback  – how much L feeds into R delay and vice versa (0..0.95)
///                    at 1.0 this is pure ping-pong
///
/// Tone (applied inside the feedback path)
///   LowCut         – high-pass cutoff in Hz (20..2000)  — roll off mud
///   HighCut        – low-pass cutoff in Hz  (1000..20000) — tame harshness
///
/// Modulation (chorus-style LFO on the delay time)
///   ModRate        – LFO frequency in Hz (0.01–10)
///   ModDepth       – LFO depth as fraction of DelayTime (0..0.5)
///   ModShape       – 0 = Sine, 1 = Triangle, 2 = Random (S&amp;H)
///
/// Mode
///   DelayMode      – 0 Normal, 1 PingPong, 2 Reverse, 3 Tape
///
/// Mix
///   DryLevel       – dry signal gain (0..1)
///   WetLevel       – wet signal gain (0..1)
/// </summary>
public class DelayEffect : AudioEffect
{
    // ── Parameter backing fields ──────────────────────────────────────────

    private double _delayTime    = 250;
    private double _ratio        = 1.0;
    private double _feedback     = 0.40;
    private double _crossFeedback = 0.0;
    private double _lowCut       = 20;
    private double _highCut      = 20000;
    private double _modRate      = 0.5;
    private double _modDepth     = 0.0;
    private int    _modShape     = 0;
    private int    _delayMode    = 0;
    private double _wetLevel     = 0.50;
    private double _dryLevel     = 1.00;

    // ── Internal DSP state ────────────────────────────────────────────────

    // Delay buffers (stereo)
    private float[] _bufL = [];
    private float[] _bufR = [];
    private int _writeIdx;
    private int _lastSampleRate;
    private int _lastBufSize;

    // Tone filters (1-pole IIR, per channel)
    // Low-cut (HP): y[n] = a * (y[n-1] + x[n] - x[n-1])
    // High-cut (LP): y[n] = y[n-1] + b * (x[n] - y[n-1])
    private float _hpStateL, _hpStateR, _hpPrevL, _hpPrevR;
    private float _lpStateL, _lpStateR;

    // LFO
    private double _lfoPhase;
    private double _lfoSampleHold;
    private double _prevRandom;
    private double _nextRandom;
    private double _lfoHoldTimer;

    public DelayEffect()
    {
        Name = "Delay";
    }

    public override string EffectType => "Delay";
    public override string Icon => "🔁";

    // ── Public parameters ─────────────────────────────────────────────────

    /// <summary>Base delay time in milliseconds for the left channel (1–2000).</summary>
    public double DelayTime
    {
        get => _delayTime;
        set { if (SetField(ref _delayTime, Math.Clamp(value, 1, 2000))) InvalidateBuffer(); }
    }

    /// <summary>
    /// Right/Left delay ratio. 1.0 = equal, 0.5 = R is half as long, 2.0 = R is twice as long.
    /// Range 0.25–4.0.
    /// </summary>
    public double Ratio
    {
        get => _ratio;
        set => SetField(ref _ratio, Math.Clamp(value, 0.25, 4.0));
    }

    /// <summary>Main feedback (0–0.95).</summary>
    public double Feedback
    {
        get => _feedback;
        set => SetField(ref _feedback, Math.Clamp(value, 0, 0.95));
    }

    /// <summary>Cross feedback: L feeds R and R feeds L (0–0.95). At 0.95 this becomes ping-pong.</summary>
    public double CrossFeedback
    {
        get => _crossFeedback;
        set => SetField(ref _crossFeedback, Math.Clamp(value, 0, 0.95));
    }

    /// <summary>High-pass cutoff (Hz) applied in the feedback path. 20–2000 Hz.</summary>
    public double LowCut
    {
        get => _lowCut;
        set => SetField(ref _lowCut, Math.Clamp(value, 20, 2000));
    }

    /// <summary>Low-pass cutoff (Hz) applied in the feedback path. 1000–20000 Hz.</summary>
    public double HighCut
    {
        get => _highCut;
        set => SetField(ref _highCut, Math.Clamp(value, 1000, 20000));
    }

    /// <summary>LFO rate in Hz (0.01–10).</summary>
    public double ModRate
    {
        get => _modRate;
        set => SetField(ref _modRate, Math.Clamp(value, 0.01, 10));
    }

    /// <summary>LFO depth as fraction of DelayTime (0–0.50).</summary>
    public double ModDepth
    {
        get => _modDepth;
        set => SetField(ref _modDepth, Math.Clamp(value, 0, 0.50));
    }

    /// <summary>LFO waveform: 0 = Sine, 1 = Triangle, 2 = S&amp;H Random.</summary>
    public int ModShape
    {
        get => _modShape;
        set => SetField(ref _modShape, Math.Clamp(value, 0, 2));
    }

    /// <summary>Delay algorithm: 0 = Normal, 1 = PingPong, 2 = Reverse, 3 = Tape.</summary>
    public int DelayMode
    {
        get => _delayMode;
        set => SetField(ref _delayMode, Math.Clamp(value, 0, 3));
    }

    /// <summary>Wet (echo) output level (0–1).</summary>
    public double WetLevel
    {
        get => _wetLevel;
        set => SetField(ref _wetLevel, Math.Clamp(value, 0, 1));
    }

    /// <summary>Dry (direct) output level (0–1).</summary>
    public double DryLevel
    {
        get => _dryLevel;
        set => SetField(ref _dryLevel, Math.Clamp(value, 0, 1));
    }

    // Legacy compat
    public bool PingPong
    {
        get => _delayMode == 1;
        set => DelayMode = value ? 1 : 0;
    }

    // ── DSP ───────────────────────────────────────────────────────────────

    private void InvalidateBuffer()
    {
        _lastBufSize = 0; // forces reallocate on next ProcessSamples
    }

    private void EnsureBuffer(int sampleRate)
    {
        // Buffer must hold at least 4× max delay to accommodate modulation + ratio
        int needed = (int)(sampleRate * 2000.0 / 1000.0 * 4) + 8;
        if (_bufL.Length >= needed && _lastSampleRate == sampleRate) return;

        _bufL = new float[needed];
        _bufR = new float[needed];
        _writeIdx = 0;
        _lastSampleRate = sampleRate;
        _lastBufSize = needed;

        // Reset filter states
        _hpStateL = _hpStateR = _hpPrevL = _hpPrevR = 0;
        _lpStateL = _lpStateR = 0;
    }

    private float ApplyToneFilters(float sample, ref float hpState, ref float hpPrev,
        ref float lpState, float hpCoeff, float lpCoeff)
    {
        // 1-pole high-pass
        float hpOut = hpCoeff * (hpState + sample - hpPrev);
        hpPrev  = sample;
        hpState = hpOut;

        // 1-pole low-pass on the HP output
        lpState += lpCoeff * (hpOut - lpState);
        return lpState;
    }

    private double LfoValue(double phase)
    {
        return _modShape switch
        {
            1 => 1.0 - 2.0 * Math.Abs(2.0 * (phase - Math.Floor(phase + 0.5))), // triangle
            2 => _lfoSampleHold, // S&H — updated separately
            _ => Math.Sin(2.0 * Math.PI * phase) // sine
        };
    }

    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        if (!IsEnabled) return;
        EnsureBuffer(sampleRate);

        int bufLen = _bufL.Length;
        double invSr = 1.0 / sampleRate;
        double lfoInc = _modRate * invSr;

        // Precompute filter coefficients
        float hpCoeff = (float)(1.0 / (1.0 + 2.0 * Math.PI * _lowCut  * invSr));
        float lpCoeff = (float)(2.0 * Math.PI * _highCut * invSr / (1.0 + 2.0 * Math.PI * _highCut * invSr));

        float fb  = (float)_feedback;
        float xfb = (float)_crossFeedback;
        float wet = (float)_wetLevel;
        float dry = (float)_dryLevel;

        double baseTimeL = _delayTime;
        double baseTimeR = _delayTime * _ratio;
        double maxModMs   = baseTimeL * _modDepth;

        bool reverse = _delayMode == 2;
        bool tape    = _delayMode == 3;
        bool ping    = _delayMode == 1;

        int end = offset + count;
        for (int i = offset; i < end; i += channels)
        {
            // ── LFO ──
            if (_modShape == 2)
            {
                _lfoHoldTimer += lfoInc;
                if (_lfoHoldTimer >= 1.0)
                {
                    _lfoHoldTimer -= 1.0;
                    _prevRandom     = _nextRandom;
                    _nextRandom     = (Random.Shared.NextDouble() * 2.0 - 1.0);
                    _lfoSampleHold  = _prevRandom;
                }
            }

            double lfoVal  = LfoValue(_lfoPhase) * maxModMs;
            _lfoPhase += lfoInc;
            if (_lfoPhase >= 1.0) _lfoPhase -= 1.0;

            // ── Delay times in samples (with LFO) ──
            double timeL = baseTimeL + lfoVal;
            double timeR = baseTimeR - lfoVal * 0.5; // slight detuning on R for width
            int dL = Math.Clamp((int)(timeL * sampleRate / 1000.0), 1, bufLen - 1);
            int dR = Math.Clamp((int)(timeR * sampleRate / 1000.0), 1, bufLen - 1);

            // ── Read from buffer ──
            int readL = (_writeIdx - dL + bufLen) % bufLen;
            int readR = (_writeIdx - dR + bufLen) % bufLen;

            float delL, delR;
            if (reverse)
            {
                // Reverse: read from ahead of write pointer (ping-pong on reversed)
                int revL = (_writeIdx + dL) % bufLen;
                int revR = (_writeIdx + dR) % bufLen;
                delL = _bufL[revL];
                delR = _bufR[revR];
            }
            else
            {
                delL = _bufL[readL];
                delR = _bufR[readR];
            }

            float inL = buffer[i];
            float inR = channels > 1 ? buffer[i + 1] : inL;

            // ── Tone filters on feedback ──
            float filtL = ApplyToneFilters(delL, ref _hpStateL, ref _hpPrevL, ref _lpStateL, hpCoeff, lpCoeff);
            float filtR = ApplyToneFilters(delR, ref _hpStateR, ref _hpPrevR, ref _lpStateR, hpCoeff, lpCoeff);

            // ── Write to buffer (with feedback routing) ──
            if (ping)
            {
                // PingPong: L echo feeds R buffer, R echo feeds L buffer
                _bufL[_writeIdx] = Clamp(inL + filtR * fb);
                _bufR[_writeIdx] = Clamp(inR + filtL * fb);
            }
            else
            {
                // Normal + cross feedback
                _bufL[_writeIdx] = Clamp(inL + filtL * fb + filtR * xfb);
                _bufR[_writeIdx] = Clamp(inR + filtR * fb + filtL * xfb);
            }

            // Tape saturation: soft-clip the buffer signal
            if (tape)
            {
                _bufL[_writeIdx] = TapeSat(_bufL[_writeIdx]);
                _bufR[_writeIdx] = TapeSat(_bufR[_writeIdx]);
            }

            // ── Output mix ──
            buffer[i] = Clamp(inL * dry + delL * wet);
            if (channels > 1)
                buffer[i + 1] = Clamp(inR * dry + delR * wet);

            _writeIdx = (_writeIdx + 1) % bufLen;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Clamp(float v) => v > 1f ? 1f : v < -1f ? -1f : v;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float TapeSat(float x)
    {
        // Gentle soft-clip for tape character
        float ax = MathF.Abs(x);
        if (ax < 0.666f) return x;
        return MathF.Sign(x) * (0.666f + (ax - 0.666f) / (1f + ((ax - 0.666f) / 0.334f) * ((ax - 0.666f) / 0.334f)));
    }

    public static readonly string[] ModeNames = ["Normal", "Ping Pong", "Reverse", "Tape"];

    public override void Reset()
    {
        if (_bufL.Length > 0) Array.Clear(_bufL);
        if (_bufR.Length > 0) Array.Clear(_bufR);
        _writeIdx = 0;
        _hpStateL = _hpStateR = _hpPrevL = _hpPrevR = 0;
        _lpStateL = _lpStateR = 0;
        _lfoPhase = _lfoHoldTimer = 0;
    }
}
