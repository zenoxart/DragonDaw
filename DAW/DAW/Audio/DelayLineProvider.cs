using NAudio.Wave;

namespace DAW.Audio;

/// <summary>
/// Inserts a fixed sample-accurate delay on an audio stream for latency compensation.
///
/// <para>
/// The delay line is implemented as a circular ring buffer.  On construction it is
/// pre-filled with <see cref="DelaySamples"/> zeros (silence), so output always lags
/// input by exactly <see cref="DelaySamples"/> samples with no start-up artefact.
/// </para>
///
/// <para>
/// Typical use: when two paths arriving at the same mix point have different processing
/// latencies (e.g. one path goes through a look-ahead compressor), wrap the shorter
/// path in a <see cref="DelayLineProvider"/> sized to the difference.
/// </para>
/// </summary>
public sealed class DelayLineProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _ring;
    private int _readPos;
    private int _writePos;

    /// <summary>Number of samples of delay applied to the stream.</summary>
    public int DelaySamples { get; }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <param name="source">The upstream provider to delay.</param>
    /// <param name="delaySamples">
    ///   Number of samples to delay.  Zero is a legal no-op (the provider is still
    ///   created, but adds no delay and no overhead beyond a copy).
    /// </param>
    public DelayLineProvider(ISampleProvider source, int delaySamples)
    {
        _source      = source;
        DelaySamples = Math.Max(0, delaySamples);

        // Ring buffer: must hold the delay plus at least one full read-block.
        // 8 192 samples (≈185 ms @ 44.1 kHz) is generous for any single block.
        int ringSize = DelaySamples + 8192;
        _ring     = new float[ringSize];
        _readPos  = 0;
        _writePos = DelaySamples; // pre-fill gap = silence of length DelaySamples
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        // Push fresh samples into the ring and pull delayed samples back out,
        // processing sample-by-sample so the circular arithmetic stays simple.
        for (int i = 0; i < samplesRead; i++)
        {
            float incoming = buffer[offset + i];

            // Write new sample at write position
            _ring[_writePos] = incoming;
            _writePos = (_writePos + 1) % _ring.Length;

            // Read delayed sample from read position
            buffer[offset + i] = _ring[_readPos];
            _readPos = (_readPos + 1) % _ring.Length;
        }

        return samplesRead;
    }
}
