using System.Windows.Threading;
using DAW.Audio;
using DAW.MVVM.Models.Sequencer;

namespace DAW.MVVM.ViewModels;

/// <summary>
/// Connects pattern clips in the Playlist to the audio graph.
///
/// This class no longer does any timing itself. All scheduling happens
/// sample-accurately inside <see cref="PatternSequencerProvider"/>, which is
/// wired directly into the mixer and uses the audio device's own sample
/// counter as the one and only clock (see that file for the full rationale).
///
/// Responsibilities here:
///   1. Snapshot the WPF object graph (tracks → clips → patterns → steps)
///      into immutable plain data on the UI thread.
///   2. Pre-load / cache all referenced samples (decoded to mixer format).
///   3. Add the provider to the mixer on Start, remove it on Stop.
///   4. Drive the visual playhead from the provider's authoritative
///      audio position (a ~30 ms DispatcherTimer — purely cosmetic; the
///      audio does not depend on it in any way).
///
/// Note: pattern edits made WHILE the playlist is running take effect on the
/// next transport start — the snapshot is immutable by design (thread safety).
/// </summary>
internal sealed class PlaylistPatternEngine : IDisposable
{
    private readonly ArrangementViewModel                  _arrangement;
    private readonly ViewModels.Sequencer.PatternViewModel _patternVm;
    private readonly AudioMixEngine                        _mixEngine;
    private readonly double                                _bpm;
    private readonly double                                _startBeat;

    private PatternSequencerProvider? _provider;
    private DispatcherTimer?          _playheadTimer;

    private readonly Dictionary<string, PreloadedSample> _sampleCache
        = new(StringComparer.OrdinalIgnoreCase);

    private const int    StepsPerBeat    = 4;
    private const double StepLengthBeats = 1.0 / StepsPerBeat;

    public PlaylistPatternEngine(
        ArrangementViewModel arrangement,
        ViewModels.Sequencer.PatternViewModel patternVm,
        AudioMixEngine mixEngine,
        double bpm,
        double startBeat)
    {
        _arrangement = arrangement;
        _patternVm   = patternVm;
        _mixEngine   = mixEngine;
        _bpm         = Math.Max(1, bpm);   // engine guard only — UI range is 1–500
        _startBeat   = startBeat;
    }

    // ── Transport ─────────────────────────────────────────────────────────────

    public void Start()
    {
        Stop();   // idempotent restart safety

        // 1. Immutable snapshot of all pattern clips (UI thread).
        var clips = BuildSchedule();

        // 2. One provider per transport run. Created even with zero clips so
        //    the playhead is always driven by the real audio position.
        _provider = new PatternSequencerProvider(
            _mixEngine.MixFormat, _bpm, _startBeat, clips);

        // 3. Into the mixer. The device is always-on, so the provider's frame 0
        //    coincides with the next buffer the card consumes — every following
        //    hit is placed sample-accurately relative to that origin.
        _mixEngine.AddInput(_provider);

        // 4. Cosmetic playhead — reads the provider's atomic frame counter.
        _playheadTimer = new DispatcherTimer(DispatcherPriority.Render)
            { Interval = TimeSpan.FromMilliseconds(30) };
        _playheadTimer.Tick += (_, _) =>
        {
            if (_provider != null)
                _arrangement.PlayheadBeat = _provider.CurrentBeat;
        };
        _playheadTimer.Start();
    }

    public void Stop()
    {
        _playheadTimer?.Stop();
        _playheadTimer = null;

        if (_provider != null)
        {
            _mixEngine.RemoveInput(_provider);   // instant silence for patterns
            _provider = null;
        }
    }

    public void Dispose() => Stop();

    // ── Snapshot builder (UI thread) ──────────────────────────────────────────

    private List<PatternSequencerProvider.ClipSchedule> BuildSchedule()
    {
        var result       = new List<PatternSequencerProvider.ClipSchedule>();
        var channelCache = new Dictionary<PatternModel,
                               PatternSequencerProvider.ChannelSchedule[]?>();

        foreach (var track in _arrangement.Tracks)
        {
            if (track.Model.IsMuted) continue;

            foreach (var clipVm in track.Clips)
            {
                var clip = clipVm.Model;

                // Pattern clips = clips without a source audio file.
                if (!string.IsNullOrEmpty(clip.SourceFilePath)) continue;
                if (clip.IsMuted) continue;

                var pattern = FindPatternByName(clip.DisplayName);
                if (pattern == null || pattern.StepCount <= 0) continue;

                if (!channelCache.TryGetValue(pattern, out var channels))
                {
                    channels = SnapshotChannels(pattern);
                    channelCache[pattern] = channels;
                }
                if (channels == null) continue;   // nothing audible in pattern

                result.Add(new PatternSequencerProvider.ClipSchedule
                {
                    StartBeat = clip.StartBeat,
                    EndBeat   = clip.StartBeat + clip.LengthInBeats,
                    StepCount = pattern.StepCount,
                    Channels  = channels,
                });
            }
        }

        return result;
    }

    private PatternSequencerProvider.ChannelSchedule[]? SnapshotChannels(PatternModel pattern)
    {
        bool anySolo = pattern.Channels.Any(c => c.IsSolo);
        var  list    = new List<PatternSequencerProvider.ChannelSchedule>();

        foreach (var ch in pattern.Channels)
        {
            if (ch.IsMuted) continue;
            if (anySolo && !ch.IsSolo) continue;
            if (string.IsNullOrEmpty(ch.SamplePath)) continue;

            if (!_sampleCache.TryGetValue(ch.SamplePath, out var sample))
            {
                var loaded = _mixEngine.Preload(ch.SamplePath);
                if (loaded == null) continue;
                sample = loaded;
                _sampleCache[ch.SamplePath] = sample;
            }

            int n        = Math.Min(pattern.StepCount, ch.Steps.Count);
            var active   = new bool[pattern.StepCount];
            var velocity = new float[pattern.StepCount];
            var rate     = new float[pattern.StepCount];
            Array.Fill(rate, 1.0f);
            for (int i = 0; i < n; i++)
            {
                active[i]   = ch.Steps[i].IsActive;
                velocity[i] = ch.Steps[i].Velocity * ch.Volume;
            }

            // Piano-Roll aware triggering: when the channel has notes, they
            // define the schedule — a step triggers only where a note STARTS
            // (at that note's pitch); steps merely covered by a longer note
            // sustain and must NOT retrigger (the "long note machine-gun" bug).
            var notes = ch.PianoRollNotes;
            if (notes.Count > 0)
            {
                const int stepTicks = 24;   // PPQ 96 / 4 steps per beat
                for (int i = 0; i < pattern.StepCount; i++)
                {
                    int  t0 = i * stepTicks, t1 = t0 + stepTicks;
                    bool trig = false; float vel = 0f, rt = 1f;

                    foreach (var note in notes)
                    {
                        if (note.IsMuted) continue;
                        if (note.StartTick >= t1 || note.EndTick <= t0) continue;
                        if (note.StartTick >= t0)   // note starts in this step
                        {
                            trig = true;
                            vel  = note.Velocity * ch.Volume;
                            rt   = (float)Math.Pow(2.0, (note.Pitch - 60) / 12.0);
                            break;
                        }
                        // covered by an earlier note → sustaining, keep trig=false
                    }

                    active[i]   = trig;
                    velocity[i] = vel;
                    rate[i]     = rt;
                }
            }

            list.Add(new PatternSequencerProvider.ChannelSchedule
            {
                Channel      = ch,
                Sample       = sample,
                StepActive   = active,
                StepVelocity = velocity,
                StepRate     = rate,
            });
        }

        return list.Count > 0 ? list.ToArray() : null;
    }

    private PatternModel? FindPatternByName(string name)
    {
        foreach (var p in _patternVm.AllPatterns)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }
}
