namespace DAW.Audio.Effects;

/// <summary>
/// Gain/Volume control effect with optional soft clipping.
/// </summary>
public class GainEffect : AudioEffect
{
    private double _gain;
    private bool _softClip;

    public GainEffect()
    {
        Name = "Gain";
    }

    public override string EffectType => "Gain";
    public override string Icon => "🔊";

    /// <summary>Gain in dB (-24 to +24)</summary>
    public double Gain
    {
        get => _gain;
        set => SetField(ref _gain, Math.Clamp(value, -24, 24));
    }

    /// <summary>Enable soft clipping (saturation)</summary>
    public bool SoftClip
    {
        get => _softClip;
        set => SetField(ref _softClip, value);
    }

    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        if (!IsEnabled) return;
        
        double gainLinear = Math.Pow(10, Gain / 20.0);

        int endIndex = offset + count;
        for (int i = offset; i < endIndex; i++)
        {
            double sample = buffer[i] * gainLinear;
            
            if (SoftClip)
            {
                // Soft clipping using tanh
                sample = Math.Tanh(sample);
            }
            else
            {
                // Hard clipping
                sample = Math.Clamp(sample, -1.0, 1.0);
            }
            
            buffer[i] = (float)sample;
        }
    }
}
