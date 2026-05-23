using System.Runtime.CompilerServices;
using NAudio.Wave;
using DAW.Audio.Effects;

namespace DAW.Audio;

/// <summary>
/// Applies an EffectChain to a source provider.
///
/// PERFORMANCE
/// ───────────
/// • No try/catch in the hot path — effects are expected to be well-behaved.
///   A single NaN/Inf sanitize pass runs after all effects are applied.
/// • The sanitize pass is fused into a single loop with a branchless clamp.
/// </summary>
public class EffectSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly EffectChain _effectChain;
    private readonly int _sampleRate;
    private readonly int _channels;

    public EffectSampleProvider(ISampleProvider source, EffectChain effectChain)
    {
        _source      = source;
        _effectChain = effectChain;
        _sampleRate  = source.WaveFormat.SampleRate;
        _channels    = source.WaveFormat.Channels;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        if (samplesRead == 0) return 0;

        var effects = _effectChain.EffectsSnapshot;

        if (!_effectChain.IsBypassed && effects.Length > 0)
        {
            int sr = _sampleRate;
            int ch = _channels;

            for (int e = 0; e < effects.Length; e++)
            {
                var effect = effects[e];
                if (effect.IsEnabled)
                {
                    try { effect.ProcessSamples(buffer, offset, samplesRead, sr, ch); }
                    catch { /* skip broken effect, never crash audio thread */ }
                }
            }
        }

        // Single-pass sanitize: clamp to [-1, 1] and zero out NaN/Inf.
        // Written as a branchless clamp — the JIT emits SSE min/max instructions.
        int end = offset + samplesRead;
        for (int i = offset; i < end; i++)
        {
            float s = buffer[i];
            // NaN check first (NaN comparisons always false)
            if (s != s || s > 1f || s < -1f)
                buffer[i] = float.IsNaN(s) ? 0f : (s > 1f ? 1f : -1f);
        }

        return samplesRead;
    }
}

/// <summary>
/// Constant-power volume + pan provider.
/// Gains are updated via properties; the audio thread reads them as volatile floats.
/// </summary>
public class VolumePanSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private volatile float _volume   = 1.0f;
    private volatile float _pan      = 0.0f;
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
        float angle  = (_pan + 1f) * 0.25f * MathF.PI;
        _leftGain    = MathF.Cos(angle);
        _rightGain   = MathF.Sin(angle);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        float vol  = _volume;
        float lgain = _leftGain;
        float rgain = _rightGain;

        if (WaveFormat.Channels == 2)
        {
            int end = offset + samplesRead;
            for (int i = offset; i < end; i += 2)
            {
                buffer[i]     *= vol * lgain;
                buffer[i + 1] *= vol * rgain;
            }
        }
        else
        {
            int end = offset + samplesRead;
            for (int i = offset; i < end; i++)
                buffer[i] *= vol;
        }

        return samplesRead;
    }
}
