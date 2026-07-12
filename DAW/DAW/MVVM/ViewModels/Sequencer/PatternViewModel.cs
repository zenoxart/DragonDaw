using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using DAW.Audio;
using DAW.Commands;
using DAW.MVVM.Models.PianoRoll;
using DAW.MVVM.Models.Sequencer;

namespace DAW.MVVM.ViewModels.Sequencer;

/// <summary>
/// ViewModel for the Channel Rack / Step Sequencer.
///
/// New features:
///   • SelectedChannel — tracks which row is currently "open" in the Piano Roll
///   • NavigateToPianoRollRequested — event raised by right-click context menu
///   • AddToPlaylistCommand — places the active pattern as a synced clip in the Arrangement
///   • Drop-into-rack support — DropSampleOnChannel()
/// </summary>
public class PatternViewModel : INotifyPropertyChanged
{
    // ── Constants ─────────────────────────────────────────────────────────────
    public const int PPQ = 96;

    // ── Patterns ──────────────────────────────────────────────────────────────
    public ObservableCollection<PatternModel>     AllPatterns { get; } = [];
    public ObservableCollection<ChannelViewModel> Channels   { get; } = [];

    private PatternModel? _activePattern;
    public PatternModel? ActivePattern
    {
        get => _activePattern;
        set { if (!SetField(ref _activePattern, value)) return; RebuildChannelViewModels(); OnPropertyChanged(nameof(StepCount)); }
    }

    /// <summary>
    /// Forwards to ActivePattern.StepCount. Bound to the STEPS ComboBox in the toolbar.
    /// </summary>
    public int StepCount
    {
        get => _activePattern?.StepCount ?? 16;
        set { if (_activePattern != null) _activePattern.StepCount = value; OnPropertyChanged(); }
    }

    /// <summary>Available step-count options shown in the STEPS ComboBox.</summary>
    public static IReadOnlyList<int> StepCountOptions { get; } = [8, 16, 32, 64, 128, 256];

    // ── Selected channel (Piano Roll target) ──────────────────────────────────

    private ChannelViewModel? _selectedChannel;
    /// <summary>
    /// The channel whose notes are currently shown in the Piano Roll.
    /// Set by the right-click context menu or the Piano Roll's own ComboBox.
    /// </summary>
    public ChannelViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set => SetField(ref _selectedChannel, value);
    }

    /// <summary>
    /// Raised when a step sequencer step fires and a channel has audio to trigger.
    /// <para>Arguments: channel, velocity (0–1), pitch (MIDI 0–127, or null when no Piano Roll
    /// note is present at this step — caller should use the channel's base/default pitch).</para>
    /// </summary>
    public event Action<ChannelViewModel, float, int?>? StepTriggered;

    /// <summary>
    /// Raised when the user selects "Open in Piano Roll" from the context menu.
    /// The MainWindow listens and switches the active tab.
    /// </summary>
    public event EventHandler<ChannelViewModel>? NavigateToPianoRollRequested;

    /// <summary>
    /// Raised when the active pattern should be placed as a new clip in the Arrangement.
    /// </summary>
    public event EventHandler<PatternModel>? AddToPlaylistRequested;

    // ── Rebuild helpers ───────────────────────────────────────────────────────

    private PatternModel? _subscribedPattern;

    private void RebuildChannelViewModels()
    {
        // Unsubscribe previous pattern's property changes
        if (_subscribedPattern != null)
            _subscribedPattern.PropertyChanged -= OnActivePatternPropertyChanged;

        var previouslySelected = _selectedChannel?.Model;
        Channels.Clear();
        if (_activePattern == null) return;

        _subscribedPattern = _activePattern;
        _activePattern.PropertyChanged += OnActivePatternPropertyChanged;

        foreach (var ch in _activePattern.Channels)
            Channels.Add(new ChannelViewModel(ch));

        // Restore selection
        SelectedChannel = previouslySelected != null
            ? Channels.FirstOrDefault(cv => cv.Model == previouslySelected)
            : Channels.FirstOrDefault();

        _activePattern.Channels.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (ChannelModel c in e.NewItems)
                    Channels.Add(new ChannelViewModel(c));
            if (e.OldItems != null)
                foreach (ChannelModel c in e.OldItems)
                {
                    var vm = Channels.FirstOrDefault(cv => cv.Model == c);
                    if (vm != null) Channels.Remove(vm);
                }
        };
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    private bool   _isPlaying    = false;
    private int    _currentTick  = 0;
    private int    _currentStep  = -1;
    private double _bpm          = 140;
    private int    _stepsPerBeat = 4;
    private readonly PatternClock _clock;

    public bool IsPlaying   { get => _isPlaying;   private set => SetField(ref _isPlaying,   value); }
    public int  CurrentStep { get => _currentStep; private set => SetField(ref _currentStep, value); }

    public double BPM
    {
        get => _bpm;
        set { if (!SetField(ref _bpm, Math.Clamp(value, 1, 500))) return; _clock.UpdateInterval(StepIntervalMs(), ActivePattern?.StepCount ?? 16); }
    }

    public double Swing
    {
        get => ActivePattern?.Swing ?? 0;
        set { if (ActivePattern != null) { ActivePattern.Swing = value; OnPropertyChanged(); } }
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public PatternViewModel(double bpm = 140)
    {
        _bpm = bpm;

        _clock = new PatternClock(Dispatcher.CurrentDispatcher);
        _clock.AudioTick += OnAudioTick;
        _clock.UiTick    += OnUiTick;

        var defaultPattern = PatternModel.CreateDefault();
        AllPatterns.Add(defaultPattern);
        ActivePattern = defaultPattern;

        PlayCommand              = new RelayCommand(Play);
        StopCommand              = new RelayCommand(Stop);
        AddChannelCommand        = new RelayCommand(AddChannel);
        RemoveChannelCommand     = new RelayCommand<ChannelViewModel>(RemoveChannel);
        AddPatternCommand        = new RelayCommand(AddPattern);
        AddToPlaylistCommand     = new RelayCommand(RequestAddToPlaylist);
        OpenInPianoRollCommand   = new RelayCommand<ChannelViewModel>(OpenInPianoRoll);
    }

    // ── Clock ─────────────────────────────────────────────────────────────────

    private double StepIntervalMs()
    {
        double stepsPerSecond = (_bpm / 60.0) * _stepsPerBeat;
        return 1000.0 / stepsPerSecond;
    }

    /// <summary>
    /// Called on the audio background thread with the resolved step index.
    /// Triggers samples for every active channel on that step.
    /// No shared mutable state is read or written here except pre-loaded audio buffers.
    /// </summary>
    private void OnAudioTick(int step)
    {
        if (ActivePattern == null) return;

        // Each step = PPQ / stepsPerBeat ticks.
        int ticksPerStep = PPQ / _stepsPerBeat;
        int stepStartTick = step * ticksPerStep;
        int stepEndTick   = stepStartTick + ticksPerStep;

        // Snapshot the collection reference — safe as long as Channels is only
        // modified on the UI thread and we treat it as read-only here.
        var channels = Channels;
        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            if (!ch.IsAudible
                || step >= ch.Steps.Count
                || !ch.Steps[step].IsActive
                || string.IsNullOrEmpty(ch.Model.SamplePath))
                continue;

            float velocity = ch.Steps[step].Velocity * ch.Volume;

            // Piano-Roll integration:
            //   • A note that STARTS inside this step's tick window triggers the
            //     sample at that note's pitch.
            //   • A note that merely COVERS the window (started earlier) is
            //     sustaining — do NOT retrigger. This is what made long notes
            //     machine-gun: SyncNotesToSteps activates every covered step,
            //     and the old code fired the sample on each of them.
            //   • No notes at all → plain step-grid behaviour (null pitch).
            int? pitch = null;
            bool sustaining = false;
            var  notes = ch.Model.PianoRollNotes;
            for (int n = 0; n < notes.Count; n++)
            {
                var note = notes[n];
                if (note.IsMuted) continue;
                if (note.StartTick < stepEndTick && note.EndTick > stepStartTick)
                {
                    if (note.StartTick >= stepStartTick)
                    {
                        pitch      = note.Pitch;   // note starts here → trigger
                        sustaining = false;
                        break;
                    }
                    sustaining = true;             // covered by an older note
                }
            }
            if (sustaining && pitch == null) continue;   // long note holding → silence

            StepTriggered?.Invoke(ch, velocity, pitch);
        }
    }

    /// <summary>Called on the UI thread — updates the visual step highlight only.</summary>
    private void OnUiTick(int step)
    {
        _currentTick = step; // keep for SyncPlay offset bookkeeping
        CurrentStep  = step;
    }

    // ── Play / Stop ───────────────────────────────────────────────────────────

    private void Play()
    {
        if (IsPlaying) return;
        _currentTick = 0;
        IsPlaying    = true;
        _clock.Start(StepIntervalMs(), ActivePattern?.StepCount ?? 16);
    }

    private void Stop()
    {
        _clock.Stop();
        IsPlaying    = false;
        CurrentStep  = -1;
        _currentTick = 0;
    }

    public void SyncPlay(int startStep = 0)
    {
        _currentTick = startStep;
        if (!IsPlaying)
        {
            IsPlaying = true;
            _clock.Start(StepIntervalMs(), ActivePattern?.StepCount ?? 16);
        }
    }

    public void SyncStop()
    {
        _clock.Stop();
        IsPlaying    = false;
        CurrentStep  = -1;
        _currentTick = 0;
    }

    /// <summary>
    /// Resets the Channel Rack to factory state: stops playback, removes all
    /// patterns (and their channels/steps/notes) and recreates the single
    /// default pattern — identical to the state right after application start.
    /// Called by MainViewModel.ResetWorkspace() when creating a new project.
    /// </summary>
    public void Reset()
    {
        SyncStop();
        _channelCounter = 0;

        // Clear active pattern first so RebuildChannelViewModels clears
        // Channels before AllPatterns is wiped (avoids stale VM references).
        ActivePattern = null;
        AllPatterns.Clear();

        var defaultPattern = PatternModel.CreateDefault();
        AllPatterns.Add(defaultPattern);
        ActivePattern = defaultPattern;
    }

    // ── Channel management ────────────────────────────────────────────────────

    private int _channelCounter = 0;

    private void AddChannel()
    {
        if (ActivePattern == null) return;
        _channelCounter++;
        var ch = new ChannelModel($"Channel {_channelCounter}", ActivePattern.StepCount);
        ActivePattern.Channels.Add(ch);
    }

    private void RemoveChannel(ChannelViewModel? vm)
    {
        if (vm == null || ActivePattern == null) return;
        ActivePattern.RemoveChannel(vm.Model);
    }

    private void AddPattern()
    {
        var p = new PatternModel { Name = $"Pattern {AllPatterns.Count + 1}" };
        p.AddChannel("Kick",   "🥁");
        p.AddChannel("Snare",  "🪘");
        p.AddChannel("Hi-Hat", "🎶");
        AllPatterns.Add(p);
        ActivePattern = p;
    }

    // ── Piano Roll navigation ─────────────────────────────────────────────────

    private void OpenInPianoRoll(ChannelViewModel? ch)
    {
        if (ch == null) return;
        SelectedChannel = ch;
        NavigateToPianoRollRequested?.Invoke(this, ch);
    }

    // ── Playlist integration ──────────────────────────────────────────────────

    private void RequestAddToPlaylist()
    {
        if (ActivePattern != null)
            AddToPlaylistRequested?.Invoke(this, ActivePattern);
    }

    // ── Drag & Drop: drop an audio file onto a channel row ────────────────────

    private void OnActivePatternPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PatternModel.StepCount))
            OnPropertyChanged(nameof(StepCount));
    }

    /// <summary>
    /// Called when the user drops a file path onto channel at index <paramref name="channelIndex"/>.
    /// Sets the SamplePath and updates the channel name to the filename.
    /// </summary>
    public void DropSampleOnChannel(int channelIndex, string filePath)
    {
        if (ActivePattern == null) return;
        if (channelIndex < 0 || channelIndex >= Channels.Count) return;

        var ch = Channels[channelIndex];
        ch.Model.SamplePath = filePath;
        ch.Model.Name       = System.IO.Path.GetFileNameWithoutExtension(filePath);

        // Auto-pick a fitting icon
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        ch.Model.PluginIcon = ext switch
        {
            ".wav" or ".aif" or ".aiff" => "🔊",
            ".mp3" or ".flac" or ".ogg" => "🎵",
            _ => "🎹"
        };
    }

    /// <summary>
    /// Adds a brand-new channel at the bottom and immediately assigns the dropped file.
    /// </summary>
    public void DropSampleAsNewChannel(string filePath)
    {
        if (ActivePattern == null) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        var ch   = new ChannelModel(name, ActivePattern.StepCount)
        {
            SamplePath = filePath,
            PluginIcon = "🔊"
        };
        ActivePattern.Channels.Add(ch);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public ICommand PlayCommand            { get; }
    public ICommand StopCommand            { get; }
    public ICommand AddChannelCommand      { get; }
    public ICommand RemoveChannelCommand   { get; }
    public ICommand AddPatternCommand      { get; }
    public ICommand AddToPlaylistCommand   { get; }
    public ICommand OpenInPianoRollCommand { get; }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
