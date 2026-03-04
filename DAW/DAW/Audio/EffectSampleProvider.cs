using NAudio.Wave;
using DAW.Audio.Effects;

namespace DAW.Audio;

/// <summary>
/// Sample provider that applies an effect chain to audio.
/// Uses the thread-safe effects snapshot from EffectChain.
/// </summary>
public class EffectSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly EffectChain _effectChain;
    private readonly int _sampleRate;
    private readonly int _channels;

    public EffectSampleProvider(ISampleProvider source, EffectChain effectChain)
    {
        _source = source;
        _effectChain = effectChain;
        _sampleRate = source.WaveFormat.SampleRate;
        _channels = source.WaveFormat.Channels;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        
        if (samplesRead == 0) return 0;
        
        // Get thread-safe snapshot (no lock needed - volatile array reference)
        var effects = _effectChain.EffectsSnapshot;
        
        if (!_effectChain.IsBypassed && effects.Length > 0)
        {
            // Process through all enabled effects
            foreach (var effect in effects)
            {
                if (effect != null && effect.IsEnabled)
                {
                    try
                    {
                        effect.ProcessSamples(buffer, offset, samplesRead, _sampleRate, _channels);
                    }
                    catch
                    {
                        // Skip this effect if it fails
                    }
                }
            }

            // Sanitize all samples to prevent audio driver crashes
            for (int i = 0; i < samplesRead; i++)
            {
                int idx = offset + i;
                float sample = buffer[idx];
                
                // Check for invalid values
                if (float.IsNaN(sample) || float.IsInfinity(sample))
                {
                    buffer[idx] = 0f;
                }
                else
                {
                    // Hard clamp to valid range
                    buffer[idx] = sample > 1f ? 1f : (sample < -1f ? -1f : sample);
                }
            }
        }

        return samplesRead;
    }
}

/// <summary>
/// Sample provider that applies volume and pan.
/// </summary>
public class VolumePanSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private volatile float _volume = 1.0f;
    private volatile float _pan = 0.0f;
    private volatile float _leftGain = 1.0f;
    private volatile float _rightGain = 1.0f;

    public VolumePanSampleProvider(ISampleProvider source)
    {
        _source = source;
        UpdateGains();
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 2f);
    }

    public float Pan
    {
        get => _pan;
        set
        {
            _pan = Math.Clamp(value, -1f, 1f);
            UpdateGains();
        }
    }

    private void UpdateGains()
    {
        // Constant power panning
        float angle = (_pan + 1) * 0.25f * MathF.PI;
        _leftGain = MathF.Cos(angle);
        _rightGain = MathF.Sin(angle);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        float vol = _volume;
        float leftG = _leftGain;
        float rightG = _rightGain;

        if (WaveFormat.Channels == 2)
        {
            for (int i = 0; i < samplesRead; i += 2)
            {
                buffer[offset + i] *= vol * leftG;
                buffer[offset + i + 1] *= vol * rightG;
            }
        }
        else
        {
            for (int i = 0; i < samplesRead; i++)
            {
                buffer[offset + i] *= vol;
            }
        }

        return samplesRead;
    }
}
