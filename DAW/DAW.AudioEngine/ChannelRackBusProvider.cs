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
///   • Amplitude envelope (Delay→Attack→Hold→Decay→Sustain→Release) shaping
///     each voice, optional per channel (<see cref="EnvelopeEnabled"/>)
///   • Voice cutting ("Group", FL-Studio style): <see cref="CutSelf"/> chokes
///     this channel's own previous voice on retrigger, and <see cref="CutGroup"/>
///     / <see cref="CutByGroup"/> (coordinated via <see cref="CutGroupRegistry"/>)
///     let one channel's trigger choke another channel entirely — e.g. a closed
///     hi-hat cutting an open hi-hat. Choking always fades through the Release
///     stage (using Release/ReleaseTension, even with the envelope otherwise
///     disabled) instead of a hard, clicky cut.
///
/// Threading:
///   • Trigger() is called from the pattern clock thread (or UI thread for
///     previews) and only enqueues into a lock-free queue.
///   • Read() runs on the mixer thread, drains the queue and renders; it is
///     the ONLY place that reads or mutates <c>_active</c>, so choke requests
///     raised from other threads (self-choke-before-play, cross-channel Cut
///     groups) are marshalled in as lightweight flags/counters rather than
///     touching voice state directly.
///   • Volume/Pan/Mute/CutByGroup/CutSelf/EnvelopeEnabled are written by the
///     UI thread and read by the mixer thread as single, independently-benign
///     fields (matches the rest of this engine's lock-free style).
/// </summary>
public sealed class ChannelRackBusProvider : ISampleProvider
{
    private enum EnvStage { Delay, Attack, Hold, Decay, Sustain, Release, Done }

    private sealed class Voice
    {
        public required float[] Data;
        public required float   Gain;
        public required float   Rate;   // 1.0 = original pitch
        public double FramePos;         // fractional frame position for rate playback

        // Envelope state — captured at trigger time so a live toggle/edit of
        // the channel's envelope never retroactively changes a ringing voice.
        public bool              UsesEnvelope;
        public EnvelopeSettings  Env;
        public EnvStage          Stage;
        public double            EnvTime;            // seconds elapsed in the current stage
        public float             ReleaseStartLevel = 1f;
        public bool              ChokeSelfBeforePlay; // CutSelf: choke this bus's own ringing voice(s) first
    }

    private readonly ConcurrentQueue<Voice> _pending = new();
    private readonly List<Voice>            _active  = new(16);

    // Strip parameters — written on the UI thread, read on the mixer thread.
    private float _volume = 0.8f;
    private float _pan    = 0.0f;
    private volatile bool _muted;

    // ── Voice cutting ("Group") ──────────────────────────────────────────────
    private int            _cutGroup;      // the group THIS channel's own voice belongs to
    private volatile int    _cutByGroup;    // the group THIS channel's trigger chokes
    private volatile bool   _cutSelf;
    private int             _chokeRequestCount; // Interlocked; >0 = choke all active voices before next render

    // ── Envelope ──────────────────────────────────────────────────────────────
    private volatile bool    _envelopeEnabled;
    private EnvelopeSettings _envelope = EnvelopeSettings.Default;

    public WaveFormat WaveFormat { get; }

    public ChannelRackBusProvider(WaveFormat mixFormat)
    {
        WaveFormat = mixFormat;
        CutGroupRegistry.Register(this);
    }

    /// <summary>Strip fader (0.0 … 1.0+).</summary>
    public float Volume { get => _volume; set => Interlocked.Exchange(ref _volume, value); }

    /// <summary>Strip pan (−1 left … +1 right), equal-power law.</summary>
    public float Pan { get => _pan; set => Interlocked.Exchange(ref _pan, value); }

    /// <summary>Strip mute. Voices keep advancing so tails stay in time.</summary>
    public bool Muted { get => _muted; set => _muted = value; }

    /// <summary>The choke-group this channel's own ringing voice belongs to (0 = None).</summary>
    public int CutGroup
    {
        get => _cutGroup;
        set
        {
            int old = _cutGroup;
            _cutGroup = value;
            if (old != value) CutGroupRegistry.UpdateCutGroup(this, old, value);
        }
    }

    /// <summary>The choke-group this channel's trigger silences (0 = None).</summary>
    public int CutByGroup { get => _cutByGroup; set => _cutByGroup = value; }

    /// <summary>When true, retriggering this channel chokes its own still-ringing voice first.</summary>
    public bool CutSelf { get => _cutSelf; set => _cutSelf = value; }

    /// <summary>Whether triggered voices are shaped by the Delay/Attack/Hold/Decay/Sustain envelope.</summary>
    public bool EnvelopeEnabled { get => _envelopeEnabled; set => _envelopeEnabled = value; }

    /// <summary>Updates the envelope shape used by voices triggered from now on.</summary>
    public void SetEnvelope(EnvelopeSettings settings) => _envelope = settings;

    /// <summary>
    /// Requested by <see cref="CutGroupRegistry"/> when another channel's trigger
    /// cuts this bus's group. Thread-safe; the fade is applied on the mixer
    /// thread at the next <see cref="Read"/>.
    /// </summary>
    internal void RequestChoke() => Interlocked.Increment(ref _chokeRequestCount);

    /// <summary>
    /// Enqueues a hit. Safe from any thread; the voice becomes audible with
    /// the next mixer read (same latency as the previous direct-to-master path).
    /// </summary>
    public void Trigger(PreloadedSample sample, float velocity = 1.0f, int semitones = 0)
    {
        _pending.Enqueue(new Voice
        {
            Data   = sample.Data,
            Gain   = sample.Volume * velocity,
            Rate   = semitones == 0 ? 1.0f : (float)Math.Pow(2.0, semitones / 12.0),
            UsesEnvelope         = _envelopeEnabled,
            Env                  = _envelope,
            Stage                = EnvStage.Delay,
            ChokeSelfBeforePlay  = _cutSelf,
        });

        if (_cutByGroup > 0) CutGroupRegistry.Choke(_cutByGroup, this);
    }

    /// <summary>Silences the bus immediately (transport stop) — a hard cut, no fade.</summary>
    public void KillAll()
    {
        while (_pending.TryDequeue(out _)) { }
        _killRequested = true;
    }
    private volatile bool _killRequested;

    /// <summary>Unregisters this bus from the cut-group registry (channel/strip deleted).</summary>
    public void Detach() => CutGroupRegistry.Unregister(this);

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);

        if (_killRequested) { _active.Clear(); _killRequested = false; }

        // Choke requests raised from another channel's trigger (Cut group) —
        // always applied here so `_active` only ever has one writer thread.
        if (Interlocked.Exchange(ref _chokeRequestCount, 0) > 0)
            ChokeAllActive();

        while (_pending.TryDequeue(out var v))
        {
            if (v.ChokeSelfBeforePlay) ChokeAllActive();
            _active.Add(v);
        }

        if (_active.Count == 0) return count;   // idle — silence, ReadFully style

        int channels = WaveFormat.Channels;
        int frames   = count / channels;
        float dt     = 1f / WaveFormat.SampleRate;

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

            for (int f = 0; f < frames; f++)
            {
                int i0 = (int)voice.FramePos;
                if (i0 >= totalFrames - 1) { finished = true; break; }

                float env = AdvanceEnvelope(voice, dt);
                if (voice.Stage == EnvStage.Done) { finished = true; break; }

                float  frac = (float)(voice.FramePos - i0);
                int    s0   = i0 * channels;
                int    s1   = s0 + channels;
                int    dst  = offset + f * channels;

                if (channels == 2)
                {
                    float l = data[s0]     + (data[s1]     - data[s0])     * frac;
                    float r = data[s0 + 1] + (data[s1 + 1] - data[s0 + 1]) * frac;
                    buffer[dst]     += l * voice.Gain * env * gainL;
                    buffer[dst + 1] += r * voice.Gain * env * gainR;
                }
                else
                {
                    float m = data[s0] + (data[s1] - data[s0]) * frac;
                    buffer[dst] += m * voice.Gain * env * vol;
                }

                voice.FramePos += voice.Rate;
            }

            if (finished || voice.FramePos >= totalFrames - 1)
                _active.RemoveAt(i);            // sample finished (or envelope reached silence)
        }

        return count;
    }

    /// <summary>
    /// Forces every currently-active (non-releasing) voice on this bus into its
    /// Release stage, capturing its current level so the fade starts from
    /// wherever the voice actually is — never a hard, clicky cut. Called only
    /// from the mixer thread (start of Read(), or right before adding a new
    /// voice when CutSelf is set).
    /// </summary>
    private void ChokeAllActive()
    {
        foreach (var v in _active)
        {
            if (v.Stage is EnvStage.Release or EnvStage.Done) continue;
            v.ReleaseStartLevel = AdvanceEnvelope(v, 0f); // snapshot current level, no state advance
            v.Stage   = EnvStage.Release;
            v.EnvTime = 0;
        }
    }

    /// <summary>
    /// Advances a voice's envelope by <paramref name="dt"/> seconds and returns
    /// its current amplitude (0–1). Calling with dt = 0 re-reads the current
    /// level without advancing time or transitioning stages — used to snapshot
    /// the level at the moment a voice is choked.
    /// </summary>
    private static float AdvanceEnvelope(Voice v, float dt)
    {
        if (!v.UsesEnvelope)
        {
            // Flat gain until choked; a choke still fades via Release/ReleaseTension
            // instead of a hard cut, so envelope-disabled channels stay click-free.
            if (v.Stage != EnvStage.Release) return 1f;
            v.EnvTime += dt;
            float rel    = MathF.Max(v.Env.Release, 0.005f);
            float t      = MathF.Min((float)(v.EnvTime / rel), 1f);
            float shaped = ShapeT(t, v.Env.ReleaseTension);
            if (t >= 1f) v.Stage = EnvStage.Done;
            return v.ReleaseStartLevel * (1f - shaped);
        }

        var env = v.Env;
        switch (v.Stage)
        {
            case EnvStage.Delay:
                v.EnvTime += dt;
                if (v.EnvTime < env.Delay) return 0f;
                v.Stage = EnvStage.Attack; v.EnvTime = 0;
                goto case EnvStage.Attack;

            case EnvStage.Attack:
            {
                if (env.Attack <= 0.0005f) { v.Stage = EnvStage.Hold; v.EnvTime = 0; goto case EnvStage.Hold; }
                v.EnvTime += dt;
                float t     = MathF.Min((float)(v.EnvTime / env.Attack), 1f);
                float level = ShapeT(t, env.AttackTension);
                if (t >= 1f) { v.Stage = EnvStage.Hold; v.EnvTime = 0; }
                return level;
            }

            case EnvStage.Hold:
                v.EnvTime += dt;
                if (v.EnvTime < env.Hold) return 1f;
                v.Stage = EnvStage.Decay; v.EnvTime = 0;
                goto case EnvStage.Decay;

            case EnvStage.Decay:
            {
                if (env.Decay <= 0.0005f) { v.Stage = EnvStage.Sustain; v.EnvTime = 0; return env.Sustain; }
                v.EnvTime += dt;
                float t = MathF.Min((float)(v.EnvTime / env.Decay), 1f);
                if (t >= 1f) v.Stage = EnvStage.Sustain;
                return 1f - (1f - env.Sustain) * t; // linear ramp down to Sustain
            }

            case EnvStage.Sustain:
                return env.Sustain;

            case EnvStage.Release:
            {
                v.EnvTime += dt;
                float rel    = MathF.Max(env.Release, 0.005f);
                float t      = MathF.Min((float)(v.EnvTime / rel), 1f);
                float shaped = ShapeT(t, env.ReleaseTension);
                if (t >= 1f) v.Stage = EnvStage.Done;
                return v.ReleaseStartLevel * (1f - shaped);
            }

            default:
                return 0f;
        }
    }

    /// <summary>Bends a normalised 0–1 progress value: -1 = logarithmic, 0 = linear, +1 = exponential.</summary>
    private static float ShapeT(float t, float tension)
    {
        t = Math.Clamp(t, 0f, 1f);
        float exponent = MathF.Pow(2f, 3f * Math.Clamp(tension, -1f, 1f));
        return MathF.Pow(t, exponent);
    }
}
