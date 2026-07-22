using System.Collections.Concurrent;
using System.Threading;
using NAudio.Wave;

namespace DAW.Audio;

/// <summary>
/// Immutable snapshot of one channel's amplitude envelope, swapped atomically
/// by reference so the mixer thread never sees a torn read while the UI
/// thread edits knobs. Mirrors <see cref="DAW.MVVM.Models.Sequencer.ChannelModel"/>'s
/// Env* properties.
/// </summary>
public sealed record EnvelopeSettings(
    float Delay, float Attack, float Hold, float Decay, float Sustain, float Release,
    float AttackTension, float ReleaseTension)
{
    public static readonly EnvelopeSettings Default = new(0f, 0.001f, 0f, 0.30f, 1f, 0.05f, 0f, 0f);

    /// <summary>Fallback fade used to choke a voice that has no envelope of its own — short enough to be click-free, long enough to sound intentional.</summary>
    public static readonly EnvelopeSettings DefaultChoke = new(0f, 0f, 0f, 0f, 1f, 0.008f, 0f, 0f);
}

internal enum EnvStage { Delay, Attack, Hold, Decay, Sustain, Release, Done }

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
///   • Voice cutting ("Group"): CutGroup/CutByGroup/CutSelf let one channel's
///     trigger choke another's (or its own) still-ringing voice — e.g. a
///     closed hi-hat silencing an open hi-hat — using a short envelope-driven
///     fade instead of a hard, clicky cut.
///   • Optional per-channel ADSR amplitude envelope; its Release also becomes
///     the fade used whenever the voice is choked.
///
/// Threading:
///   • Trigger()/ChokeActiveVoices() are called from the pattern clock thread
///     (or UI thread for previews) and only enqueue into a lock-free queue.
///   • Read() runs on the mixer thread, drains the queue and renders.
///   • Volume/Pan/Mute/CutGroup/CutByGroup/CutSelf/Envelope are written by the
///     UI thread and read by the mixer thread as single 32-bit/reference
///     values (atomic on .NET) — never mutated in place across threads.
/// </summary>
public sealed class ChannelRackBusProvider : ISampleProvider
{
    private sealed class Voice
    {
        public required float[] Data;
        public required float   Gain;
        public required float   Rate;   // 1.0 = original pitch
        public double FramePos;         // fractional frame position for rate playback

        public required bool              EnvelopeEnabled;
        public required EnvelopeSettings  Env;
        public EnvStage Stage;
        public double   StageT;          // seconds elapsed within the current stage
        public float    CurGain;         // last computed envelope gain (0..1)
        public float    ReleaseStartGain;

        /// <summary>Fast flat-gain path is only valid while sitting in Sustain — everything else (Delay/Attack/Hold/Decay/Release) needs per-frame gain.</summary>
        public bool NeedsPerFrameGain => Stage != EnvStage.Sustain;
    }

    private readonly ConcurrentQueue<Voice> _pending = new();
    private readonly List<Voice>            _active  = new(16);

    // Strip parameters — written on the UI thread, read on the mixer thread.
    private float _volume = 0.8f;
    private float _pan    = 0.0f;
    private volatile bool _muted;

    // Voice-cutting ("Group") parameters.
    private volatile int  _cutGroup;
    private volatile int  _cutByGroup;
    private volatile bool _cutSelf;

    // Envelope — reference swap only, never mutated in place from the UI thread.
    private volatile bool _envelopeEnabled;
    private EnvelopeSettings _envelope = EnvelopeSettings.Default;

    public WaveFormat WaveFormat { get; }

    public ChannelRackBusProvider(WaveFormat mixFormat) => WaveFormat = mixFormat;

    /// <summary>Strip fader (0.0 … 1.0+).</summary>
    public float Volume { get => _volume; set => Interlocked.Exchange(ref _volume, value); }

    /// <summary>Strip pan (−1 left … +1 right), equal-power law.</summary>
    public float Pan { get => _pan; set => Interlocked.Exchange(ref _pan, value); }

    /// <summary>Strip mute. Voices keep advancing so tails stay in time.</summary>
    public bool Muted { get => _muted; set => _muted = value; }

    /// <summary>Choke-group this bus's own voices belong to (0 = None). Registers with <see cref="ChokeGroupRegistry"/>.</summary>
    public int CutGroup
    {
        get => _cutGroup;
        set
        {
            int old = _cutGroup;
            if (old == value) return;
            _cutGroup = value;
            ChokeGroupRegistry.SetGroup(this, old, value);
        }
    }

    /// <summary>Choke-group this bus's trigger silences (0 = None).</summary>
    public int CutByGroup { get => _cutByGroup; set => _cutByGroup = value; }

    /// <summary>When true, triggering this bus chokes its own still-ringing voice(s).</summary>
    public bool CutSelf { get => _cutSelf; set => _cutSelf = value; }

    /// <summary>Enables ADSR shaping (and envelope-driven choke fades) for future triggers.</summary>
    public bool EnvelopeEnabled { get => _envelopeEnabled; set => _envelopeEnabled = value; }

    /// <summary>Replaces the envelope snapshot used by future triggers. Atomic reference swap.</summary>
    public void SetEnvelope(EnvelopeSettings env) => Volatile.Write(ref _envelope, env);

    /// <summary>
    /// Enqueues a hit. Safe from any thread; the voice becomes audible with
    /// the next mixer read (same latency as the previous direct-to-master path).
    /// </summary>
    public void Trigger(PreloadedSample sample, float velocity = 1.0f, int semitones = 0)
    {
        bool envOn = _envelopeEnabled;
        var  env   = Volatile.Read(ref _envelope);

        if (_cutSelf) ChokeActiveVoices();
        if (_cutByGroup != 0) ChokeGroupRegistry.Choke(_cutByGroup);

        _pending.Enqueue(new Voice
        {
            Data   = sample.Data,
            Gain   = sample.Volume * velocity,
            Rate   = semitones == 0 ? 1.0f : (float)Math.Pow(2.0, semitones / 12.0),
            EnvelopeEnabled = envOn,
            Env             = envOn ? env : EnvelopeSettings.DefaultChoke,
            Stage           = envOn ? EnvStage.Delay : EnvStage.Sustain,
            CurGain         = envOn ? 0f : 1f,
        });
    }

    /// <summary>
    /// Smoothly fades out every voice currently playing on this bus instead of
    /// hard-cutting it — used for self-choke and as the target of a group cut.
    /// Safe from any thread; takes effect on the next mixer read.
    /// </summary>
    public void ChokeActiveVoices()
    {
        // We can't safely enumerate/mutate `_active` off the mixer thread, so
        // just mark a "choke everything currently active" request; the mixer
        // thread applies it to whatever is actually in `_active` right now.
        _chokeAllRequested = true;
    }
    private volatile bool _chokeAllRequested;

    /// <summary>Silences the bus immediately (transport stop) — hard cut, no fade.</summary>
    public void KillAll()
    {
        while (_pending.TryDequeue(out _)) { }
        _killRequested = true;
    }
    private volatile bool _killRequested;

    /// <summary>Call when the bus is being torn down so it stops receiving group chokes.</summary>
    public void Detach() => ChokeGroupRegistry.Unregister(this, _cutGroup);

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);

        if (_killRequested) { _active.Clear(); _killRequested = false; }

        if (_chokeAllRequested)
        {
            _chokeAllRequested = false;
            foreach (var v in _active) BeginRelease(v);
        }

        while (_pending.TryDequeue(out var v)) _active.Add(v);

        if (_active.Count == 0) return count;   // idle — silence, ReadFully style

        int channels = WaveFormat.Channels;
        int frames   = count / channels;
        double dtPerFrame = 1.0 / WaveFormat.SampleRate;

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
            bool finished   = false;

            if (!voice.NeedsPerFrameGain)
            {
                // ── Fast path: flat gain (no envelope shaping active right now) ──
                for (int f = 0; f < frames; f++)
                {
                    int   i0   = (int)voice.FramePos;
                    if (i0 >= totalFrames - 1) { voice.FramePos = totalFrames; finished = true; break; }
                    float frac = (float)(voice.FramePos - i0);
                    int   s0   = i0 * channels;
                    int   s1   = s0 + channels;
                    int   dst  = offset + f * channels;

                    float envGain = voice.EnvelopeEnabled ? voice.CurGain : 1f;
                    if (channels == 2)
                    {
                        float l = data[s0]     + (data[s1]     - data[s0])     * frac;
                        float r = data[s0 + 1] + (data[s1 + 1] - data[s0 + 1]) * frac;
                        buffer[dst]     += l * voice.Gain * envGain * gainL;
                        buffer[dst + 1] += r * voice.Gain * envGain * gainR;
                    }
                    else
                    {
                        float m = data[s0] + (data[s1] - data[s0]) * frac;
                        buffer[dst] += m * voice.Gain * envGain * vol;
                    }

                    voice.FramePos += voice.Rate;
                }
            }
            else
            {
                // ── Envelope / choke path: recompute gain every frame ──
                for (int f = 0; f < frames; f++)
                {
                    int i0 = (int)voice.FramePos;
                    if (i0 >= totalFrames - 1 || voice.Stage == EnvStage.Done) { finished = true; break; }
                    float frac = (float)(voice.FramePos - i0);
                    int   s0   = i0 * channels;
                    int   s1   = s0 + channels;
                    int   dst  = offset + f * channels;

                    float envGain = AdvanceEnvelope(voice, dtPerFrame);

                    if (channels == 2)
                    {
                        float l = data[s0]     + (data[s1]     - data[s0])     * frac;
                        float r = data[s0 + 1] + (data[s1 + 1] - data[s0 + 1]) * frac;
                        buffer[dst]     += l * voice.Gain * envGain * gainL;
                        buffer[dst + 1] += r * voice.Gain * envGain * gainR;
                    }
                    else
                    {
                        float m = data[s0] + (data[s1] - data[s0]) * frac;
                        buffer[dst] += m * voice.Gain * envGain * vol;
                    }

                    voice.FramePos += voice.Rate;
                    if (voice.Stage == EnvStage.Done) { finished = true; break; }
                }
            }

            if (finished || voice.FramePos >= totalFrames - 1)
                _active.RemoveAt(i);            // sample finished, or choked out
        }

        return count;
    }

    /// <summary>Transitions a voice into its Release stage, fading from wherever its gain currently is.</summary>
    private static void BeginRelease(Voice v)
    {
        if (v.Stage == EnvStage.Done || v.Stage == EnvStage.Release) return;
        v.ReleaseStartGain = v.EnvelopeEnabled ? v.CurGain : 1f;
        v.Stage  = EnvStage.Release;
        v.StageT = 0;
        // A choked voice always needs per-frame shaping even if it started on
        // the flat-gain fast path.
        v.EnvelopeEnabled = true;
    }

    /// <summary>Advances one voice's envelope by one output frame and returns its current gain (0..1).</summary>
    private static float AdvanceEnvelope(Voice v, double dt)
    {
        var e = v.Env;
        switch (v.Stage)
        {
            case EnvStage.Delay:
                v.StageT += dt;
                if (v.StageT >= e.Delay) { v.Stage = EnvStage.Attack; v.StageT -= e.Delay; }
                v.CurGain = 0f;
                return 0f;

            case EnvStage.Attack:
                if (e.Attack <= 0.0005f) { v.Stage = EnvStage.Hold; v.StageT = 0; v.CurGain = 1f; return 1f; }
                v.StageT += dt;
                float ag = Curve((float)(v.StageT / e.Attack), e.AttackTension);
                if (v.StageT >= e.Attack) { v.Stage = EnvStage.Hold; v.StageT = 0; ag = 1f; }
                v.CurGain = ag;
                return ag;

            case EnvStage.Hold:
                v.StageT += dt;
                if (v.StageT >= e.Hold) { v.Stage = EnvStage.Decay; v.StageT = 0; }
                v.CurGain = 1f;
                return 1f;

            case EnvStage.Decay:
                if (e.Decay <= 0.0005f) { v.Stage = EnvStage.Sustain; v.StageT = 0; v.CurGain = e.Sustain; return e.Sustain; }
                v.StageT += dt;
                float dc = Curve((float)(v.StageT / e.Decay), 0f);
                float dg = 1f + (e.Sustain - 1f) * dc;
                if (v.StageT >= e.Decay) { v.Stage = EnvStage.Sustain; v.StageT = 0; dg = e.Sustain; }
                v.CurGain = dg;
                return dg;

            case EnvStage.Sustain:
                v.CurGain = e.Sustain;
                return e.Sustain;

            case EnvStage.Release:
                v.StageT += dt;
                float relTime = MathF.Max(0.004f, e.Release);
                float rg = v.ReleaseStartGain * (1f - Curve((float)(v.StageT / relTime), e.ReleaseTension));
                if (v.StageT >= relTime) { v.Stage = EnvStage.Done; rg = 0f; }
                v.CurGain = rg;
                return rg;

            default:
                return 0f;
        }
    }

    /// <summary>Bends a 0..1 progress value into a concave/convex curve. tension: -1 = logarithmic, 0 = linear, +1 = exponential.</summary>
    private static float Curve(float x, float tension)
    {
        x = Math.Clamp(x, 0f, 1f);
        tension = Math.Clamp(tension, -1f, 1f);
        if (tension > 0.001f) return MathF.Pow(x, 1f + tension * 3f);
        if (tension < -0.001f) return 1f - MathF.Pow(1f - x, 1f - tension * 3f);
        return x;
    }
}
