using System.Threading;
using NAudio.Wave;
using DAW.MVVM.Models.Sequencer;

namespace DAW.Audio;

/// <summary>
/// Sample-accurate pattern sequencer for the Playlist.
///
/// ── Architecture ─────────────────────────────────────────────────────────────
/// This provider IS the clock. It sits inside the mixer graph and renders
/// pattern hits at exact sample offsets within each audio buffer:
///
///     StepVoicePool (Channel Rack)  ─┐
///     Track readers (audio clips)   ─┼─→ MixingSampleProvider → FX → WaveOut
///     PatternSequencerProvider      ─┘        ▲
///        └─ counts frames, schedules hits ────┘ (device clock = only clock)
///
/// Why not trigger-based (previous design, still used by the Channel Rack)?
///   • A triggered voice can only start at the NEXT mixer read — hits get
///     quantised to buffer boundaries (e.g. 100 ms grid instead of the
///     107.14 ms step grid at 140 BPM). The groove is structurally broken
///     no matter how precise the trigger thread is.
///   • A wall-clock thread (Stopwatch/SpinWait) inevitably drifts against
///     the sound card's crystal. Two clocks = guaranteed desync over time.
///
/// This provider has neither problem:
///   • _framePos advances exactly with the samples the device consumes —
///     it cannot drift from the audio by definition.
///   • Each Read() computes which step boundaries fall inside the block
///     [blockStart, blockEnd) and starts those hits at their precise frame
///     offset inside the buffer. Worst-case error: ±0.5 samples (~11 µs).
///
/// Threading: the snapshot (clips/steps/samples) is immutable after
/// construction. Read() is called only by the mixer thread; the UI reads
/// nothing but the atomic frame counter (CurrentBeat for the playhead).
/// </summary>
public sealed class PatternSequencerProvider : ISampleProvider
{
    // ── Immutable schedule (built on the UI thread, never mutated after) ─────

    public sealed class ChannelSchedule
    {
        public required ChannelModel    Channel;         // routes this channel's audio to its own mixer strip
        public required PreloadedSample Sample;
        public required bool[]          StepActive;
        public required float[]         StepVelocity;   // velocity × channel volume
        public required float[]         StepRate;       // 1.0 = original pitch; from Piano-Roll notes
    }

    public sealed class ClipSchedule
    {
        public required double            StartBeat;
        public required double            EndBeat;
        public required int               StepCount;
        public required ChannelSchedule[] Channels;

        // Mutable render-state — touched ONLY by the mixer thread.
        internal long NextStepK;       // absolute step counter incl. loops
        internal bool Finished;
    }

    // ── Voices (mixer thread only) ────────────────────────────────────────────

    private sealed class Voice
    {
        public required ChannelModel Channel; // routes rendered samples to this channel's buffer
        public required float[] Data;
        public required float   Volume;
        public required float   Rate;   // playback rate: 2^(semitones/12)
        public double FramePos;         // fractional frame position (rate playback)
        public int    StartFrame;       // frame offset inside the CURRENT block (then 0)
    }

    private readonly List<Voice> _voices = new(64);

    // ── Timing ────────────────────────────────────────────────────────────────

    private readonly double _startBeat;
    private readonly double _framesPerBeat;
    private const    int    StepsPerBeat    = 4;        // 16th-note grid
    private const    double StepLengthBeats = 1.0 / StepsPerBeat;

    private long _framePos;        // frames rendered since transport start

    private readonly ClipSchedule[]   _clips;
    private readonly ChannelModel[]   _allChannels; // distinct channels across all clips, for the per-channel handoff

    public WaveFormat WaveFormat { get; }

    // ── Construction ──────────────────────────────────────────────────────────

    public PatternSequencerProvider(
        WaveFormat mixFormat, double bpm, double startBeat,
        IReadOnlyList<ClipSchedule> clips)
    {
        WaveFormat     = mixFormat;
        _startBeat     = startBeat;
        _framesPerBeat = mixFormat.SampleRate * 60.0 / Math.Max(1, bpm);   // guard: UI range is 1–500
        _clips         = clips.ToArray();
        _allChannels   = _clips.SelectMany(c => c.Channels).Select(c => c.Channel).Distinct().ToArray();

        // Initialise each clip's step cursor for a transport starting at startBeat:
        // first k whose event beat (clipStart + k·stepLen) is >= startBeat.
        foreach (var clip in _clips)
        {
            double beatsBeforeStart = _startBeat - clip.StartBeat;
            clip.NextStepK = beatsBeforeStart <= 0
                ? 0
                : (long)Math.Ceiling(beatsBeforeStart / StepLengthBeats - 1e-9);
            clip.Finished = StepBeat(clip, clip.NextStepK) >= clip.EndBeat - 1e-9;
        }
    }

    /// <summary>Authoritative playhead position, derived from rendered frames.</summary>
    public double CurrentBeat
        => _startBeat + Interlocked.Read(ref _framePos) / _framesPerBeat;

    // ── Render ────────────────────────────────────────────────────────────────

    public int Read(float[] buffer, int offset, int count)
    {
        // This provider's own audible output is silence — the actual audio for
        // each channel is handed off per-channel below so it goes through that
        // channel's real mixer strip (Volume/Pan/Mute/FX/meter) instead of being
        // summed straight to master here.
        Array.Clear(buffer, offset, count);

        int  channels   = WaveFormat.Channels;
        int  frames     = count / channels;
        long blockStart = _framePos;
        long blockEnd   = blockStart + frames;

        ScheduleHits(blockStart, blockEnd);

        if (_allChannels.Length > 0)
        {
            var channelBufs = new Dictionary<ChannelModel, float[]>(_allChannels.Length);
            foreach (var ch in _allChannels)
                channelBufs[ch] = new float[frames * channels];

            RenderVoices(channelBufs, frames, channels);

            foreach (var ch in _allChannels)
                PatternChannelBus.Push(ch, channelBufs[ch]);
        }

        Interlocked.Exchange(ref _framePos, blockEnd);
        return count;   // always full — mixer runs with ReadFully semantics
    }

    /// <summary>
    /// Finds every step boundary falling inside [blockStart, blockEnd) and
    /// spawns voices at their exact frame offset within this block.
    /// </summary>
    private void ScheduleHits(long blockStart, long blockEnd)
    {
        foreach (var clip in _clips)
        {
            if (clip.Finished) continue;

            while (true)
            {
                double evBeat = StepBeat(clip, clip.NextStepK);
                if (evBeat >= clip.EndBeat - 1e-9) { clip.Finished = true; break; }

                long evFrame = BeatToFrame(evBeat);
                if (evFrame >= blockEnd) break;             // future block

                if (evFrame >= blockStart)                  // inside this block
                {
                    int stepIndex   = (int)(clip.NextStepK % clip.StepCount);
                    int offsetInBlk = (int)(evFrame - blockStart);

                    foreach (var ch in clip.Channels)
                    {
                        if (!ch.StepActive[stepIndex]) continue;
                        _voices.Add(new Voice
                        {
                            Channel    = ch.Channel,
                            Data       = ch.Sample.Data,
                            Volume     = ch.Sample.Volume * ch.StepVelocity[stepIndex],
                            Rate       = ch.StepRate[stepIndex],
                            FramePos   = 0,
                            StartFrame = offsetInBlk,
                        });
                    }
                }
                // evFrame < blockStart can only occur after construction-time
                // rounding at the very first block — treat as "missed by <1
                // frame" and just advance.
                clip.NextStepK++;
            }
        }
    }

    /// <summary>
    /// Additively mixes each active voice into its OWN channel's buffer (so it
    /// can be routed to that channel's mixer strip), sample-accurate. Voices
    /// play at their individual rate (Piano-Roll pitch) with linear
    /// interpolation per frame.
    /// </summary>
    private void RenderVoices(Dictionary<ChannelModel, float[]> channelBufs, int frames, int channels)
    {
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            var voice       = _voices[i];
            var data        = voice.Data;
            int totalFrames = data.Length / channels;
            var dst         = channelBufs[voice.Channel];

            int f = voice.StartFrame;              // spawn offset, 0 afterwards
            voice.StartFrame = 0;

            for (; f < frames; f++)
            {
                int i0 = (int)voice.FramePos;
                if (i0 >= totalFrames - 1) break;
                float frac = (float)(voice.FramePos - i0);
                int   s0   = i0 * channels;
                int   d0   = f * channels;

                for (int c = 0; c < channels; c++)
                {
                    float v = data[s0 + c] + (data[s0 + channels + c] - data[s0 + c]) * frac;
                    dst[d0 + c] += v * voice.Volume;
                }

                voice.FramePos += voice.Rate;
            }

            if (voice.FramePos >= totalFrames - 1)
                _voices.RemoveAt(i);               // sample finished
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double StepBeat(ClipSchedule clip, long k)
        => clip.StartBeat + k * StepLengthBeats;

    private long BeatToFrame(double beat)
        => (long)Math.Round((beat - _startBeat) * _framesPerBeat);
}
