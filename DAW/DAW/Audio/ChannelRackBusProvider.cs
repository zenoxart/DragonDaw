using System.Collections.Concurrent;
using System.Threading;
using NAudio.Wave;

namespace DAW.Audio;

/// <summary>
/// Polyphonic audio bus for ONE Channel-Rack channel.
///
/// Purpose: route Channel-Rack playback through a real mixer strip instead of
/// straight into the master. One bus is created per rack channel that has a
/// sample loaded; the bus is wrapped in an EffectSampleProvider carrying the
/// strip's EffectChain and added to the master mixer:
///
///     rack channel trigger → ChannelRackBusProvider (voices, volume/pan/mute)
///                          → EffectSampleProvider (strip insert FX)
///                          → master mixer
///
/// Features:
///   • Polyphonic (overlapping hits ring out naturally)
///   • Per-voice playback rate for Piano-Roll pitch (2^(semitones/12),
///     linear interpolation per stereo frame)
///   • Strip parameters (Volume / Pan / Mute) applied live, equal-power pan
///
/// Threading:
///   • Trigger() is called from the pattern clock thread (or UI thread for
///     previews) and only enqueues into a lock-free queue.
///   • Read() runs on the mixer thread, drains the queue and renders.
///   • Volume/Pan/Mute are written by the UI thread and read by the mixer
///     thread as single 32-bit values (atomic on .NET).
/// </summary>
public sealed class ChannelRackBusProvider : ISampleProvider
{
    private sealed class Voice
    {
        public required float[] Data;
        public required float   Gain;
        public required float   Rate;   // 1.0 = original pitch
        public double FramePos;         // fractional frame position for rate playback
    }

    private readonly ConcurrentQueue<Voice> _pending = new();
    private readonly List<Voice>            _active  = new(16);

    // Strip parameters — written on the UI thread, read on the mixer thread.
    private float _volume = 0.8f;
    private float _pan    = 0.0f;
    private volatile bool _muted;

    public WaveFormat WaveFormat { get; }

    public ChannelRackBusProvider(WaveFormat mixFormat) => WaveFormat = mixFormat;

    /// <summary>Strip fader (0.0 … 1.0+).</summary>
    public float Volume { get => _volume; set => Interlocked.Exchange(ref _volume, value); }

    /// <summary>Strip pan (−1 left … +1 right), equal-power law.</summary>
    public float Pan { get => _pan; set => Interlocked.Exchange(ref _pan, value); }

    /// <summary>Strip mute. Voices keep advancing so tails stay in time.</summary>
    public bool Muted { get => _muted; set => _muted = value; }

    /// <summary>
    /// Enqueues a hit. Safe from any thread; the voice becomes audible with
    /// the next mixer read (same latency as the previous direct-to-master path).
    /// </summary>
    public void Trigger(PreloadedSample sample, float velocity = 1.0f, int semitones = 0)
    {
        _pending.Enqueue(new Voice
        {
            Data = sample.Data,
            Gain = sample.Volume * velocity,
            Rate = semitones == 0 ? 1.0f : (float)Math.Pow(2.0, semitones / 12.0),
        });
    }

    /// <summary>Silences the bus immediately (transport stop).</summary>
    public void KillAll()
    {
        while (_pending.TryDequeue(out _)) { }
        _killRequested = true;
    }
    private volatile bool _killRequested;

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);

        if (_killRequested) { _active.Clear(); _killRequested = false; }
        while (_pending.TryDequeue(out var v)) _active.Add(v);

        if (_active.Count == 0) return count;   // idle — silence, ReadFully style

        int channels = WaveFormat.Channels;
        int frames   = count / channels;

        // Equal-power pan gains, folded together with the strip fader.
        float vol   = _muted ? 0f : _volume;
        double a    = (Math.Clamp(_pan, -1f, 1f) + 1.0) * Math.PI / 4.0;
        float gainL = vol * (float)Math.Cos(a);
        float gainR = vol * (float)Math.Sin(a);

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var voice       = _active[i];
            var data        = voice.Data;
            int totalFrames = data.Length / channels;

            for (int f = 0; f < frames; f++)
            {
                int    i0   = (int)voice.FramePos;
                if (i0 >= totalFrames - 1) { voice.FramePos = totalFrames; break; }
                float  frac = (float)(voice.FramePos - i0);
                int    s0   = i0 * channels;
                int    s1   = s0 + channels;
                int    dst  = offset + f * channels;

                if (channels == 2)
                {
                    float l = data[s0]     + (data[s1]     - data[s0])     * frac;
                    float r = data[s0 + 1] + (data[s1 + 1] - data[s0 + 1]) * frac;
                    buffer[dst]     += l * voice.Gain * gainL;
                    buffer[dst + 1] += r * voice.Gain * gainR;
                }
                else
                {
                    float m = data[s0] + (data[s1] - data[s0]) * frac;
                    buffer[dst] += m * voice.Gain * vol;
                }

                voice.FramePos += voice.Rate;
            }

            if (voice.FramePos >= totalFrames - 1)
                _active.RemoveAt(i);            // sample finished
        }

        return count;
    }
}
