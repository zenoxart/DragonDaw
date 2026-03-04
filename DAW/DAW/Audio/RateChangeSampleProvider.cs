using NAudio.Wave;

namespace DAW.Audio;

/// <summary>
/// Provides real-time rate change (resampling) for an audio stream via linear interpolation.
/// The rate is updated lock-free through a volatile float — safe to write from the UI thread
/// and read from the audio thread simultaneously.
/// <para>
/// Rate &gt; 1 → reads more source samples per output frame → higher pitch / faster playback.<br/>
/// Rate &lt; 1 → reads fewer source samples per output frame → lower pitch / slower playback.
/// </para>
/// </summary>
public sealed class RateChangeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;

    // Volatile: UI thread writes, audio thread reads — no lock needed.
    private volatile float _rate = 1.0f;

    // Fractional read position carried across Read() calls for seamless interpolation.
    private double _fractionalOffset;
    private float[] _sourceBuffer = Array.Empty<float>();

    public RateChangeSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>
    /// Playback rate multiplier.
    /// 1.0 = normal speed, 2.0 = double speed (one octave up), 0.5 = half speed (one octave down).
    /// Thread-safe: written via volatile field, never locked in the audio thread.
    /// </summary>
    public float Rate
    {
        get => _rate;
        set => _rate = Math.Clamp(value, 0.05f, 8.0f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        float rate = _rate;   // single volatile read — consistent for this block

        // Fast path: no resampling needed
        if (Math.Abs(rate - 1.0f) < 0.0005f)
        {
            _fractionalOffset = 0.0;
            return _source.Read(buffer, offset, count);
        }

        int outputFrames = count / _channels;

        // Number of source frames required:
        //   ceil(outputFrames * rate + _fractionalOffset) + 1 (extra frame for the upper interpolation sample)
        int srcFramesNeeded = (int)Math.Ceiling(outputFrames * rate + _fractionalOffset) + 1;
        int srcSamplesNeeded = srcFramesNeeded * _channels;

        if (_sourceBuffer.Length < srcSamplesNeeded)
            _sourceBuffer = new float[srcSamplesNeeded + _channels * 4];   // slight over-alloc to reduce resize frequency

        int srcSamplesRead = _source.Read(_sourceBuffer, 0, srcSamplesNeeded);
        int srcFramesRead = srcSamplesRead / _channels;

        if (srcFramesRead < 2) return 0;   // need at least two frames for interpolation

        int framesProduced = 0;
        double pos = _fractionalOffset;

        for (int i = 0; i < outputFrames; i++)
        {
            int srcFrame = (int)pos;
            if (srcFrame + 1 >= srcFramesRead) break;

            float frac = (float)(pos - srcFrame);

            for (int ch = 0; ch < _channels; ch++)
            {
                float a = _sourceBuffer[srcFrame * _channels + ch];
                float b = _sourceBuffer[(srcFrame + 1) * _channels + ch];
                buffer[offset + i * _channels + ch] = a + frac * (b - a);
            }

            pos += rate;
            framesProduced++;
        }

        // Keep only the fractional part — the integer part was consumed from the source buffer.
        _fractionalOffset = pos - Math.Floor(pos);

        return framesProduced * _channels;
    }
}
