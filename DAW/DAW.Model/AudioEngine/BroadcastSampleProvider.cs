using NAudio.Wave;

namespace DAW.Audio;

/// <summary>
/// Fans out one <see cref="ISampleProvider"/> to N independent consumers that all
/// receive <b>identical, synchronised</b> sample data.
///
/// <para>
/// Without this, two sub-mixers holding the same <see cref="ISampleProvider"/> reference
/// each call <c>Read</c> independently: the first gets samples 0–1023, the second gets
/// 1024–2047 — a full-buffer desync (≈ 23 ms at 44 100 Hz).
/// </para>
///
/// <para>
/// The implementation uses a copy-on-write cache: the first consumer to read in each
/// audio cycle pulls fresh samples from the source; every subsequent consumer in that
/// same cycle receives the cached buffer.  NAudio's audio thread reads all mixer inputs
/// sequentially within a single callback, so the "first / rest" ordering is stable.
/// </para>
/// </summary>
public sealed class BroadcastSampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _consumerCount;

    // Shared cache — written once by the first consumer, read by the rest
    private float[] _cache = [];
    private int     _cachedSamples;
    private int     _consumersServed;

    private readonly object _lock = new();

    private BroadcastSampleProvider(ISampleProvider source, int consumerCount)
    {
        _source        = source;
        _consumerCount = consumerCount;
    }

    /// <summary>
    /// Splits <paramref name="source"/> into <paramref name="count"/> consumers that each
    /// receive the same samples every audio cycle.
    /// If <paramref name="count"/> is 1 the original provider is returned unchanged.
    /// </summary>
    public static ISampleProvider[] Split(ISampleProvider source, int count)
    {
        if (count <= 1) return [source];

        var broadcaster = new BroadcastSampleProvider(source, count);
        var consumers   = new ISampleProvider[count];
        for (int i = 0; i < count; i++)
            consumers[i] = new Consumer(broadcaster);
        return consumers;
    }

    // Called by each Consumer.Read — thread-safe but expected to run single-threaded
    // (NAudio audio callback).
    private int ReadInternal(float[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            if (_consumersServed == 0)
            {
                // First consumer this cycle: fetch from the real source
                if (_cache.Length < count)
                    _cache = new float[count];

                _cachedSamples = _source.Read(_cache, 0, count);
            }

            int toCopy = Math.Min(count, _cachedSamples);
            if (toCopy > 0)
                Array.Copy(_cache, 0, buffer, offset, toCopy);

            if (++_consumersServed >= _consumerCount)
                _consumersServed = 0; // reset for the next audio cycle

            return toCopy;
        }
    }

    // ── Inner consumer ────────────────────────────────────────────────────────
    private sealed class Consumer(BroadcastSampleProvider parent) : ISampleProvider
    {
        public WaveFormat WaveFormat => parent._source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
            => parent.ReadInternal(buffer, offset, count);
    }
}
