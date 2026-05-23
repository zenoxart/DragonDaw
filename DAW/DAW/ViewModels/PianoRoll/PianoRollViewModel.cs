using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DAW.Commands;
using DAW.Models.PianoRoll;
using DAW.ViewModels.Sequencer;

namespace DAW.ViewModels.PianoRoll;

public enum PianoRollTool  { Draw, Paint, Select, Slice, Mute }
public enum SnapMode       { None = 0, Bar = 384, Beat = 96, Half = 48, Quarter = 24, Eighth = 12 }

/// <summary>
/// ViewModel for the Piano Roll.
///
/// New in this version:
///   • AvailableChannels — bound to the channel-selector ComboBox in the toolbar
///   • SelectedChannel   — changing it loads notes from that channel's PianoRollNotes
///   • OpenChannel()     — called by MainViewModel after a right-click "Open in Piano Roll"
/// </summary>
public class PianoRollViewModel : INotifyPropertyChanged
{
    public const int    PPQ           = 96;
    public const int    TotalPitches  = 128;
    public const double DefaultCellW  = 32.0;
    public const double DefaultCellH  = 14.0;

    // ── Channel selector ──────────────────────────────────────────────────────

    private ObservableCollection<ChannelViewModel> _availableChannels = [];
    private ChannelViewModel? _selectedChannel;

    /// <summary>
    /// Channels from the currently active pattern.
    /// Bound to the ComboBox at the top of the Piano Roll view.
    /// Updated whenever the user switches patterns or opens a new channel.
    /// </summary>
    public ObservableCollection<ChannelViewModel> AvailableChannels => _availableChannels;

    /// <summary>
    /// The channel whose PianoRollNotes are loaded into the editor.
    /// Switching channels persists current notes back and loads the new ones.
    /// </summary>
    public ChannelViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (_selectedChannel == value) return;
            // Persist edits back to the outgoing channel
            PersistNotes(_selectedChannel);
            UnsubscribeStepChanges();
            SetField(ref _selectedChannel, value);
            OnPropertyChanged(nameof(ChannelDisplayName));
            LoadNotes(value);
            SubscribeStepChanges(value);
        }
    }

    /// <summary>Short display name shown next to the channel ComboBox.</summary>
    public string ChannelDisplayName => _selectedChannel?.Name ?? "—";

    // Track the channel we subscribed step-change events on so we can unsubscribe.
    private ChannelViewModel? _subscribedStepsChannel;

    // Prevent feedback loop: when SyncNotesToSteps writes to steps, OnStepChanged must not re-fire.
    private bool _suppressStepSync;

    /// <summary>
    /// Called by MainViewModel when the user right-clicks a channel and picks
    /// "Open in Piano Roll", or when the active pattern changes in the rack.
    /// </summary>
    public void OpenChannel(ChannelViewModel? channel,
                            ObservableCollection<ChannelViewModel> allChannels)
    {
        PersistNotes(_selectedChannel);
        UnsubscribeStepChanges();
        _availableChannels = allChannels;
        OnPropertyChanged(nameof(AvailableChannels));
        // Assign without going through the setter to avoid double-persist
        _selectedChannel = channel;
        OnPropertyChanged(nameof(SelectedChannel));
        OnPropertyChanged(nameof(ChannelDisplayName));
        LoadNotes(channel);
        SubscribeStepChanges(channel);
    }

    private void SubscribeStepChanges(ChannelViewModel? ch)
    {
        if (ch == null) return;
        _subscribedStepsChannel = ch;
        foreach (var step in ch.Steps)
            step.PropertyChanged += OnStepChanged;
    }

    private void UnsubscribeStepChanges()
    {
        if (_subscribedStepsChannel == null) return;
        foreach (var step in _subscribedStepsChannel.Steps)
            step.PropertyChanged -= OnStepChanged;
        _subscribedStepsChannel = null;
        UnsubscribeNoteChanges();
    }

    private void OnStepChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressStepSync) return;
        if (e.PropertyName != nameof(ViewModels.Sequencer.StepViewModel.IsActive) &&
            e.PropertyName != nameof(ViewModels.Sequencer.StepViewModel.Velocity)) return;

        // Only refresh when the notes were synthesized from steps
        // (i.e. the user has not drawn custom Piano Roll notes yet).
        var ch = _subscribedStepsChannel;
        if (ch == null) return;

        // Check if all current notes match synthesized-step fingerprints
        bool hasSynthesized = Notes.Count > 0 && Notes.All(n => n.Pitch == 60);
        bool hasCustom      = Notes.Any(n => n.Pitch != 60);
        if (hasCustom) return;   // user has custom notes — don't overwrite

        ch.Model.PianoRollNotes.Clear();
        LoadNotes(ch);
    }

    // Tracks individual note property subscriptions so we can remove them when the channel changes.
    private readonly List<PianoRollNote> _subscribedNotes = [];

    private void UnsubscribeNoteChanges()
    {
        foreach (var n in _subscribedNotes)
            n.PropertyChanged -= OnNotePropertyChanged;
        _subscribedNotes.Clear();
    }

    private void SubscribeNoteChanges()
    {
        foreach (var n in Notes)
        {
            n.PropertyChanged += OnNotePropertyChanged;
            _subscribedNotes.Add(n);
        }
    }

    private void OnNotePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PianoRollNote.StartTick)
                           or nameof(PianoRollNote.Length)
                           or nameof(PianoRollNote.Velocity)
                           or nameof(PianoRollNote.IsMuted))
            SyncNotesToSteps(_selectedChannel);
    }

    private void LoadNotes(ChannelViewModel? ch)
    {
        UnsubscribeNoteChanges();
        Notes.Clear();
        if (ch == null) return;
        foreach (var n in ch.Model.PianoRollNotes)
            Notes.Add(n);

        // If the channel has no piano-roll notes but has active steps, synthesize
        // notes from those steps so they are immediately visible in the Piano Roll.
        if (Notes.Count == 0 && ch.Steps.Count > 0)
        {
            const int stepTicks  = PPQ / 4;   // 16th-note grid: 24 ticks per step
            const int defaultPitch = 60;       // C5 — matches the step-sequencer default

            for (int i = 0; i < ch.Steps.Count; i++)
            {
                var step = ch.Steps[i];
                if (!step.IsActive) continue;

                var note = new Models.PianoRoll.PianoRollNote
                {
                    Pitch     = defaultPitch,
                    StartTick = i * stepTicks,
                    Length    = stepTicks,
                    Velocity  = step.Velocity
                };
                Notes.Add(note);
                ch.Model.PianoRollNotes.Add(note);
            }
            SubscribeNoteChanges();
            return;
        }

        // If channel has no notes yet, seed with demo
        if (Notes.Count == 0 && ch.Model.SamplePath.Length == 0)
            SeedDemoNotes();

        SubscribeNoteChanges();
    }

    private void PersistNotes(ChannelViewModel? ch)
    {
        if (ch == null) return;
        ch.Model.PianoRollNotes.Clear();
        foreach (var n in Notes)
            ch.Model.PianoRollNotes.Add(n);
        SyncNotesToSteps(ch);
    }

    /// <summary>
    /// Reflects the current Piano Roll notes back into the channel rack step grid.
    /// A step is active when at least one note covers it; the step's velocity is
    /// taken from the loudest note that lands on it.
    /// Steps that no note covers are deactivated.
    /// </summary>
    private void SyncNotesToSteps(ChannelViewModel? ch)
    {
        if (ch == null) return;
        const int stepTicks = PPQ / 4;   // 16th-note = 24 ticks

        for (int i = 0; i < ch.Steps.Count; i++)
        {
            int stepStart = i * stepTicks;
            int stepEnd   = stepStart + stepTicks;

            // Find any note that overlaps this step
            var hit = Notes.Where(n => n.StartTick < stepEnd && n.EndTick > stepStart)
                           .OrderByDescending(n => n.Velocity)
                           .FirstOrDefault();

            // Suppress OnStepChanged re-entrancy while we write
            var step = ch.Steps[i].Model;
            _suppressStepSync = true;
            try
            {
                step.IsActive = hit != null;
                if (hit != null) step.Velocity = hit.Velocity;
            }
            finally { _suppressStepSync = false; }
        }
    }

    /// <summary>
    /// Raised when the user presses a key on the piano keyboard.
    /// Subscribers (MainViewModel) should play the channel's sample as a preview.
    /// The int argument is the MIDI pitch (0–127).
    /// </summary>
    public event Action<int>? NotePreviewStarted;

    /// <summary>Raised when the user releases a piano key.</summary>
    public event Action<int>? NotePreviewStopped;

    internal void FirePreviewStart(int pitch) => NotePreviewStarted?.Invoke(pitch);
    internal void FirePreviewStop(int pitch)  => NotePreviewStopped?.Invoke(pitch);

    // ── Notes ─────────────────────────────────────────────────────────────────

    public ObservableCollection<PianoRollNote> Notes { get; } = [];

    // ── View Transform ────────────────────────────────────────────────────────

    private double _zoomX  = 1.0;
    private double _zoomY  = 1.0;
    private double _scrollX = 0.0;
    private double _scrollY = 0.0;

    public double ZoomX   { get => _zoomX;   set => SetField(ref _zoomX,   Math.Clamp(value, 0.1, 10.0)); }
    public double ZoomY   { get => _zoomY;   set => SetField(ref _zoomY,   Math.Clamp(value, 0.5,  3.0)); }
    public double ScrollX { get => _scrollX; set => SetField(ref _scrollX, Math.Max(0, value)); }
    public double ScrollY { get => _scrollY; set => SetField(ref _scrollY, Math.Max(0, value)); }

    public double TickWidth  => (DefaultCellW * _zoomX) / PPQ;
    public double RowHeight  => DefaultCellH  * _zoomY;

    // ── Tool / Snap ───────────────────────────────────────────────────────────

    private PianoRollTool _activeTool = PianoRollTool.Draw;
    private SnapMode      _snapMode   = SnapMode.Quarter;

    public PianoRollTool ActiveTool { get => _activeTool; set => SetField(ref _activeTool, value); }
    public SnapMode      SnapMode   { get => _snapMode;   set => SetField(ref _snapMode,   value); }
    public int SnapTicks => (int)_snapMode == 0 ? 1 : (int)_snapMode;

    public int SnapTick(int raw)
    {
        if (SnapTicks <= 1) return raw;
        return (raw / SnapTicks) * SnapTicks;
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    private int  _playheadTick = 0;
    private bool _isPlaying    = false;

    public int  PlayheadTick { get => _playheadTick; set => SetField(ref _playheadTick, value); }
    public bool IsPlaying    { get => _isPlaying;    set => SetField(ref _isPlaying,    value); }

    // ── Ghost Notes ───────────────────────────────────────────────────────────

    public ObservableCollection<PianoRollNote> GhostNotes { get; } = [];
    private bool _showGhostNotes = true;
    public  bool ShowGhostNotes  { get => _showGhostNotes; set => SetField(ref _showGhostNotes, value); }

    // ── Velocity Editor ───────────────────────────────────────────────────────

    public enum VelocityParam { Velocity, Pan, Release }
    private VelocityParam _velocityEditorParam = VelocityParam.Velocity;
    public  VelocityParam  VelocityEditorParam { get => _velocityEditorParam; set => SetField(ref _velocityEditorParam, value); }

    // ── Scale ─────────────────────────────────────────────────────────────────

    private int  _rootNote  = 0;
    private bool _showScale = false;
    public int  RootNote  { get => _rootNote;  set => SetField(ref _rootNote,  value % 12); }
    public bool ShowScale { get => _showScale; set => SetField(ref _showScale, value); }

    // ── Note CRUD ─────────────────────────────────────────────────────────────

    public PianoRollNote AddNote(int pitch, int startTick, int length, float velocity = 0.8f)
    {
        var n = new PianoRollNote
        {
            Pitch     = pitch,
            StartTick = SnapTick(startTick),
            Length    = Math.Max(SnapTicks, length),
            Velocity  = velocity
        };
        n.PropertyChanged += OnNotePropertyChanged;
        _subscribedNotes.Add(n);
        Notes.Add(n);
        PersistNotes(_selectedChannel);
        return n;
    }

    public void DeleteNote(PianoRollNote n)
    {
        Notes.Remove(n);
        PersistNotes(_selectedChannel);
    }

    /// <summary>
    /// Splits <paramref name="note"/> at <paramref name="splitTick"/> into two adjacent notes.
    /// The original note is shortened; a new note covers the remainder.
    /// Does nothing when the split position is not strictly inside the note.
    /// </summary>
    public void CutNote(PianoRollNote note, int splitTick)
    {
        splitTick = SnapTick(splitTick);
        if (splitTick <= note.StartTick || splitTick >= note.EndTick) return;

        int originalEnd = note.EndTick;
        note.Length = splitTick - note.StartTick;

        var tail = new PianoRollNote
        {
            Pitch     = note.Pitch,
            StartTick = splitTick,
            Length    = originalEnd - splitTick,
            Velocity  = note.Velocity
        };
        Notes.Add(tail);
        PersistNotes(_selectedChannel);
    }

    public void DeleteSelected()
    {
        foreach (var n in Notes.Where(n => n.IsSelected).ToList()) Notes.Remove(n);
        PersistNotes(_selectedChannel);
    }

    public void SelectAll()    { foreach (var n in Notes) n.IsSelected = true; }
    public void DeselectAll()  { foreach (var n in Notes) n.IsSelected = false; }

    public void Quantize()
    {
        var targets = Notes.Any(n => n.IsSelected)
            ? Notes.Where(n => n.IsSelected).ToList()
            : Notes.ToList();
        foreach (var n in targets) n.StartTick = SnapTick(n.StartTick);
        PersistNotes(_selectedChannel);
    }

    // ── Coordinates ───────────────────────────────────────────────────────────

    public int    XToTick  (double x) => (int)((x + ScrollX) / TickWidth);
    public double TickToX  (int tick) => tick * TickWidth - ScrollX;
    public int    YToPitch (double y) => 127 - (int)((y + ScrollY) / RowHeight);
    public double PitchToY (int pitch) => (127 - pitch) * RowHeight - ScrollY;

    // ── Commands ──────────────────────────────────────────────────────────────

    public ICommand SetToolDrawCommand      { get; }
    public ICommand SetToolSelectCommand    { get; }
    public ICommand SetToolSliceCommand     { get; }
    public ICommand SetToolMuteCommand      { get; }
    public ICommand QuantizeCommand         { get; }
    public ICommand SelectAllCommand        { get; }
    public ICommand DeleteSelectedCommand   { get; }
    public ICommand ToggleGhostNotesCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public PianoRollViewModel()
    {
        SetToolDrawCommand      = new RelayCommand(() => ActiveTool = PianoRollTool.Draw);
        SetToolSelectCommand    = new RelayCommand(() => ActiveTool = PianoRollTool.Select);
        SetToolSliceCommand     = new RelayCommand(() => ActiveTool = PianoRollTool.Slice);
        SetToolMuteCommand      = new RelayCommand(() => ActiveTool = PianoRollTool.Mute);
        QuantizeCommand         = new RelayCommand(Quantize);
        SelectAllCommand        = new RelayCommand(SelectAll);
        DeleteSelectedCommand   = new RelayCommand(DeleteSelected);
        ToggleGhostNotesCommand = new RelayCommand(() => ShowGhostNotes = !ShowGhostNotes);
    }

    private void SeedDemoNotes()
    {
        int[] melody = [60, 62, 64, 65, 67, 65, 64, 62, 60];
        for (int i = 0; i < melody.Length; i++)
        {
            Notes.Add(new PianoRollNote
            {
                Pitch = melody[i], StartTick = i * PPQ, Length = PPQ - 4, Velocity = 0.8f
            });
        }
    }

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
