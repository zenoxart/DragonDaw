using NAudio.Wave;

namespace DAW.Audio;

/// <summary>
/// A pass-through <see cref="ISampleProvider"/> that captures peak levels
/// per channel for real-time metering.  Thread-safe: the audio thread writes
/// peaks via <see cref="Read"/> while the UI thread reads via
/// <see cref="PeakLeft"/>/<see cref="PeakRight"/> and <see cref="ResetPeaks"/>.
/// </summary>
public sealed class MeteringSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _peakLeft;
    private float _peakRight;

    public MeteringSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Current peak level for the left (or mono) channel (0..1+).</summary>
    public float PeakLeft => Volatile.Read(ref _peakLeft);

    /// <summary>Current peak level for the right channel (0..1+).</summary>
    public float PeakRight => Volatile.Read(ref _peakRight);

    /// <summary>
    /// Resets peaks to zero.  Call from the UI thread after reading the values
    /// so the next measurement window starts fresh.
    /// </summary>
    public void ResetPeaks()
    {
        Volatile.Write(ref _peakLeft, 0f);
        Volatile.Write(ref _peakRight, 0f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        float maxL = 0f;
        float maxR = 0f;

        if (WaveFormat.Channels == 2)
        {
            for (int i = 0; i < samplesRead; i += 2)
            {
                float absL = MathF.Abs(buffer[offset + i]);
                float absR = MathF.Abs(buffer[offset + i + 1]);
                if (absL > maxL) maxL = absL;
                if (absR > maxR) maxR = absR;
            }
        }
        else
        {
            for (int i = 0; i < samplesRead; i++)
            {
                float abs = MathF.Abs(buffer[offset + i]);
                if (abs > maxL) maxL = abs;
            }
            maxR = maxL;
        }

        // Keep the highest peak across successive reads until the UI resets
        float currentL = Volatile.Read(ref _peakLeft);
        if (maxL > currentL) Volatile.Write(ref _peakLeft, maxL);

        float currentR = Volatile.Read(ref _peakRight);
        if (maxR > currentR) Volatile.Write(ref _peakRight, maxR);

        return samplesRead;
    }
}
