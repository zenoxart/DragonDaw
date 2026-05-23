using System.Runtime.CompilerServices;
using NAudio.Wave;

namespace DAW.Audio;

/// <summary>
/// Pass-through provider that captures per-channel peak levels for metering.
///
/// PERFORMANCE
/// ───────────
/// • Volatile.Read/Write are called only TWICE per buffer (once per channel)
///   instead of once per sample.  The inner loop accumulates into local variables.
/// </summary>
public sealed class MeteringSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _peakLeft;
    private float _peakRight;

    public MeteringSampleProvider(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float PeakLeft  => Volatile.Read(ref _peakLeft);
    public float PeakRight => Volatile.Read(ref _peakRight);

    public void ResetPeaks()
    {
        Volatile.Write(ref _peakLeft,  0f);
        Volatile.Write(ref _peakRight, 0f);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        // Accumulate peaks into locals — no memory barriers in the hot loop
        float maxL = 0f, maxR = 0f;
        int end = offset + samplesRead;

        if (WaveFormat.Channels == 2)
        {
            for (int i = offset; i < end; i += 2)
            {
                float absL = MathF.Abs(buffer[i]);
                float absR = MathF.Abs(buffer[i + 1]);
                if (absL > maxL) maxL = absL;
                if (absR > maxR) maxR = absR;
            }
        }
        else
        {
            for (int i = offset; i < end; i++)
            {
                float abs = MathF.Abs(buffer[i]);
                if (abs > maxL) maxL = abs;
            }
            maxR = maxL;
        }

        // Two atomic writes per buffer instead of two per sample
        float curL = Volatile.Read(ref _peakLeft);
        if (maxL > curL) Volatile.Write(ref _peakLeft,  maxL);

        float curR = Volatile.Read(ref _peakRight);
        if (maxR > curR) Volatile.Write(ref _peakRight, maxR);

        return samplesRead;
    }
}
