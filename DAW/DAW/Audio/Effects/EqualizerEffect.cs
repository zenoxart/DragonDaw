namespace DAW.Audio.Effects;

/// <summary>
/// 3-Band Parametric EQ effect.
/// </summary>
public class EqualizerEffect : AudioEffect
{
    // Low band
    private double _lowGain;
    private double _lowFrequency = 100;
    
    // Mid band
    private double _midGain;
    private double _midFrequency = 1000;
    private double _midQ = 1.0;
    
    // High band
    private double _highGain;
    private double _highFrequency = 8000;
    
    // Filter state variables (per channel)
    private readonly double[] _lowState = new double[4];
    private readonly double[] _midState = new double[4];
    private readonly double[] _highState = new double[4];

    public EqualizerEffect()
    {
        Name = "EQ";
    }

    public override string EffectType => "Equalizer";
    public override string Icon => "📊";

    #region Low Band
    /// <summary>Low band gain in dB (-12 to +12)</summary>
    public double LowGain
    {
        get => _lowGain;
        set => SetField(ref _lowGain, Math.Clamp(value, -12, 12));
    }

    /// <summary>Low band frequency in Hz (20-500)</summary>
    public double LowFrequency
    {
        get => _lowFrequency;
        set => SetField(ref _lowFrequency, Math.Clamp(value, 20, 500));
    }
    #endregion

    #region Mid Band
    /// <summary>Mid band gain in dB (-12 to +12)</summary>
    public double MidGain
    {
        get => _midGain;
        set => SetField(ref _midGain, Math.Clamp(value, -12, 12));
    }

    /// <summary>Mid band frequency in Hz (200-8000)</summary>
    public double MidFrequency
    {
        get => _midFrequency;
        set => SetField(ref _midFrequency, Math.Clamp(value, 200, 8000));
    }

    /// <summary>Mid band Q factor (0.1-10)</summary>
    public double MidQ
    {
        get => _midQ;
        set => SetField(ref _midQ, Math.Clamp(value, 0.1, 10));
    }
    #endregion

    #region High Band
    /// <summary>High band gain in dB (-12 to +12)</summary>
    public double HighGain
    {
        get => _highGain;
        set => SetField(ref _highGain, Math.Clamp(value, -12, 12));
    }

    /// <summary>High band frequency in Hz (2000-20000)</summary>
    public double HighFrequency
    {
        get => _highFrequency;
        set => SetField(ref _highFrequency, Math.Clamp(value, 2000, 20000));
    }
    #endregion

    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        if (!IsEnabled) return;

        int endIndex = offset + count;
        for (int i = offset; i < endIndex; i += channels)
        {
            for (int ch = 0; ch < channels && (i + ch) < endIndex; ch++)
            {
                double sample = buffer[i + ch];
                
                // Apply low shelf
                sample = ProcessBiquad(sample, sampleRate, LowFrequency, LowGain, 0.707, 
                    BiquadType.LowShelf, _lowState, ch * 2);
                
                // Apply parametric mid
                sample = ProcessBiquad(sample, sampleRate, MidFrequency, MidGain, MidQ, 
                    BiquadType.Peaking, _midState, ch * 2);
                
                // Apply high shelf
                sample = ProcessBiquad(sample, sampleRate, HighFrequency, HighGain, 0.707, 
                    BiquadType.HighShelf, _highState, ch * 2);
                
                buffer[i + ch] = (float)Math.Clamp(sample, -1.0, 1.0);
            }
        }
    }

    private enum BiquadType { LowShelf, HighShelf, Peaking }

    private static double ProcessBiquad(double input, int sampleRate, double freq, double gainDb, 
        double q, BiquadType type, double[] state, int stateOffset)
    {
        if (Math.Abs(gainDb) < 0.1) return input;

        double omega = 2.0 * Math.PI * freq / sampleRate;
        double sinOmega = Math.Sin(omega);
        double cosOmega = Math.Cos(omega);
        double A = Math.Pow(10, gainDb / 40.0);
        double alpha = sinOmega / (2.0 * q);

        double b0, b1, b2, a0, a1, a2;

        switch (type)
        {
            case BiquadType.LowShelf:
                double sqrtALow = Math.Sqrt(A);
                b0 = A * ((A + 1) - (A - 1) * cosOmega + 2 * sqrtALow * alpha);
                b1 = 2 * A * ((A - 1) - (A + 1) * cosOmega);
                b2 = A * ((A + 1) - (A - 1) * cosOmega - 2 * sqrtALow * alpha);
                a0 = (A + 1) + (A - 1) * cosOmega + 2 * sqrtALow * alpha;
                a1 = -2 * ((A - 1) + (A + 1) * cosOmega);
                a2 = (A + 1) + (A - 1) * cosOmega - 2 * sqrtALow * alpha;
                break;

            case BiquadType.HighShelf:
                double sqrtAHigh = Math.Sqrt(A);
                b0 = A * ((A + 1) + (A - 1) * cosOmega + 2 * sqrtAHigh * alpha);
                b1 = -2 * A * ((A - 1) + (A + 1) * cosOmega);
                b2 = A * ((A + 1) + (A - 1) * cosOmega - 2 * sqrtAHigh * alpha);
                a0 = (A + 1) - (A - 1) * cosOmega + 2 * sqrtAHigh * alpha;
                a1 = 2 * ((A - 1) - (A + 1) * cosOmega);
                a2 = (A + 1) - (A - 1) * cosOmega - 2 * sqrtAHigh * alpha;
                break;

            case BiquadType.Peaking:
            default:
                b0 = 1 + alpha * A;
                b1 = -2 * cosOmega;
                b2 = 1 - alpha * A;
                a0 = 1 + alpha / A;
                a1 = -2 * cosOmega;
                a2 = 1 - alpha / A;
                break;
        }

        // Normalize
        b0 /= a0; b1 /= a0; b2 /= a0;
        a1 /= a0; a2 /= a0;

        // Apply filter (Direct Form II)
        double w = input - a1 * state[stateOffset] - a2 * state[stateOffset + 1];
        double output = b0 * w + b1 * state[stateOffset] + b2 * state[stateOffset + 1];
        
        state[stateOffset + 1] = state[stateOffset];
        state[stateOffset] = w;

        return output;
    }

    public override void Reset()
    {
        Array.Clear(_lowState);
        Array.Clear(_midState);
        Array.Clear(_highState);
    }
}
