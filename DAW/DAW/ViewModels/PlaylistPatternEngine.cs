using System.Windows.Threading;
using DAW.Audio;
using DAW.Models.Sequencer;

namespace DAW.ViewModels;

/// <summary>
/// Drives pattern-clip audio playback in the Playlist.
///
/// When the Playlist plays, the engine ticks at step resolution (BPM × 4 steps/beat).
/// On each tick it:
///   1. Advances an internal beat cursor.
///   2. Checks every arrangement track for pattern clips (SourceFilePath is empty)
///      whose time range contains the current beat.
///   3. Matches those clips to PatternModels by name (from PatternViewModel.AllPatterns).
///   4. Calculates which step index is "current" inside that clip's loop.
///   5. Fires PlayOneShot for every active step in every channel of that pattern.
///
/// Looping: if the playhead passes the end of a pattern clip it loops back to step 0,
/// so you hear the pattern repeat for as many bars as the clip covers.
/// </summary>
internal sealed class PlaylistPatternEngine
{
    // ── Construction / config ─────────────────────────────────────────────────
    private readonly ArrangementViewModel                     _arrangement;
    private readonly ViewModels.Sequencer.PatternViewModel    _patternVm;
    private readonly AudioMixEngine                           _mixEngine;

    private double _bpm;
    private double _currentBeat;          // absolute beat in the arrangement
    private int    _stepsPerBeat  = 4;    // 16th-note resolution
    private double _stepLengthBeats => 1.0 / _stepsPerBeat;

    private readonly DispatcherTimer _timer;

    // ── State ─────────────────────────────────────────────────────────────────
    // Track the last step we fired per clip so we don't double-fire
    // Key = clip identity (StartBeat + DisplayName hash), Value = last step index fired
    private readonly Dictionary<long, int> _lastFiredStep = new();

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
        _bpm         = Math.Max(20, bpm);
        _currentBeat = startBeat;

        _timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = StepInterval()
        };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    // ── Timer tick ────────────────────────────────────────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        // Advance playhead by one step
        _currentBeat += _stepLengthBeats;

        // Walk all arrangement tracks
        foreach (var track in _arrangement.Tracks)
        {
            if (track.Model.IsMuted) continue;

            foreach (var clipVm in track.Clips)
            {
                var clip = clipVm.Model;

                // Pattern clips have no source audio file
                if (!string.IsNullOrEmpty(clip.SourceFilePath)) continue;
                if (clip.IsMuted) continue;

                double clipStart = clip.StartBeat;
                double clipEnd   = clipStart + clip.LengthInBeats;

                // Is the playhead inside this clip?
                if (_currentBeat < clipStart || _currentBeat >= clipEnd) continue;

                // Find the matching PatternModel by name
                var pattern = _patternVm.AllPatterns.FirstOrDefault(
                    p => string.Equals(p.Name, clip.DisplayName, StringComparison.OrdinalIgnoreCase));
                if (pattern == null) continue;

                // Compute beat offset inside the clip, modulo the pattern's natural length
                // Pattern length in beats = StepCount / stepsPerBeat (16th-note grid)
                double patternLengthBeats = (double)pattern.StepCount / _stepsPerBeat;
                if (patternLengthBeats <= 0) continue;

                double beatOffsetInClip   = _currentBeat - clipStart;
                double beatInPattern      = beatOffsetInClip % patternLengthBeats;
                int    stepIndex          = (int)(beatInPattern * _stepsPerBeat);
                stepIndex = Math.Clamp(stepIndex, 0, pattern.StepCount - 1);

                // Build a stable key for this clip to detect step changes
                long clipKey = HashCode.Combine(clip.StartBeat, clip.DisplayName);
                _lastFiredStep.TryGetValue(clipKey, out int lastStep);
                if (lastStep == stepIndex) continue;  // same step, already fired

                _lastFiredStep[clipKey] = stepIndex;

                // Fire all active channels at this step
                FirePatternStep(pattern, stepIndex);
            }
        }
    }

    /// <summary>
    /// Plays all active steps for the given step index across all channels.
    /// </summary>
    private void FirePatternStep(PatternModel pattern, int stepIndex)
    {
        foreach (var channel in pattern.Channels)
        {
            if (channel.IsMuted) continue;
            if (stepIndex >= channel.Steps.Count) continue;
            if (!channel.Steps[stepIndex].IsActive) continue;
            if (string.IsNullOrEmpty(channel.SamplePath)) continue;
            if (!System.IO.File.Exists(channel.SamplePath)) continue;

            _mixEngine.PlayOneShot(channel.SamplePath, channel.Steps[stepIndex].Velocity);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TimeSpan StepInterval()
    {
        // Duration of one 16th note = 60 / (BPM × stepsPerBeat)
        double stepsPerSecond = (_bpm / 60.0) * _stepsPerBeat;
        return TimeSpan.FromSeconds(1.0 / stepsPerSecond);
    }
}
