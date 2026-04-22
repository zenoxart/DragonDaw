using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using DAW.Commands;
using DAW.Models;
using DAW.Views;
using DAW.Services;
using DAW.Input;
using DAW.Audio;
using DAW.Audio.Effects;
using Microsoft.Win32;
using NAudio.Wave;

namespace DAW.ViewModels;

/// <summary>
/// Main ViewModel for the DAW application with multi-track playback support.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private static readonly Color[] TrackColors = 
    [
        Color.FromRgb(255, 152, 0),   // FL Orange
        Color.FromRgb(76, 175, 80),   // Green
        Color.FromRgb(33, 150, 243),  // Blue
        Color.FromRgb(156, 39, 176),  // Purple
        Color.FromRgb(244, 67, 54),   // Red
        Color.FromRgb(0, 188, 212),   // Cyan
        Color.FromRgb(255, 235, 59),  // Yellow
        Color.FromRgb(233, 30, 99),   // Pink
    ];
    
    private Track? _selectedTrack;
    private bool _isAudioBrowserVisible = true;
    private bool _isPlaying;
    private string _statusMessage = "Bereit";
    private double _masterVolume = 0.8;
    private double _masterPan = 0.0;
    private double _masterMeterLeft;
    private double _masterMeterRight;
    private DispatcherTimer? _masterMeterTimer;
    private double _bpm = 140.0;
    private TimeSpan _currentPosition = TimeSpan.Zero;
    private int _trackCounter;
    private MasterEffectSlot? _selectedEffectSlot;

    /// <summary>
    /// Maps each routing-target track to its live bus fader so Volume/Mute/Solo
    /// changes on Channel B are immediately reflected in the send signal level.
    /// </summary>
    private readonly Dictionary<Track, NAudio.Wave.SampleProviders.VolumeSampleProvider> _busFaders = new();

    /// <summary>
    /// Maps each routing-target track to its live pan provider so Pan changes
    /// on Channel B are immediately reflected in the bus signal.
    /// </summary>
    private readonly Dictionary<Track, DAW.Audio.VolumePanSampleProvider> _busPanners = new();

    public MainViewModel()
    {
        // Initialize centralized audio mix engine (single output device for all tracks)
        MixEngine = new AudioMixEngine();
        
        // Master meter timer (~20 fps)
        _masterMeterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _masterMeterTimer.Tick += MasterMeterTimer_Tick;
        _masterMeterTimer.Start();
        
        // Initialize global application state
        GlobalState = new GlobalApplicationState();
        AudioEngine = new AudioEngineService();
        TransportService = new TransportService(GlobalState, AudioEngine);
        ToolStateService = new ToolStateService(GlobalState);
        
        // Initialize global toolbar
        GlobalToolbar = new GlobalToolbarViewModel(GlobalState, TransportService, ToolStateService);
        
        // Initialize services
        var fileSystemService = new FileSystemService();
        var settingsService = new SettingsService();
        var projectService = new ProjectService(fileSystemService, settingsService);
        
        // Initialize enhanced project service
        EnhancedProjectService = new EnhancedProjectService(fileSystemService, settingsService);
        
        // Initialize commands (MUST be before FileMenuViewModel which reads them)
        PlayAllCommand = new RelayCommand(PlayAll);
        PauseAllCommand = new RelayCommand(PauseAll, () => IsPlaying);
        StopAllCommand = new RelayCommand(StopAll);
        AddTrackCommand = new RelayCommand(AddTrack);
        RemoveTrackCommand = new RelayCommand(RemoveTrack, () => SelectedTrack is not null);
        AnalyzeCommand = new RelayCommand(Analyze, () => SelectedTrack is not null);
        OpenSamplerCommand = new RelayCommand<Track>(t => OpenSampler(t), _ => SelectedTrack is not null);
        
        // Master effects commands
        AddMasterEffectCommand = new RelayCommand<MasterEffectSlot>(AddMasterEffect);
        RemoveMasterEffectCommand = new RelayCommand<MasterEffectSlot>(RemoveMasterEffect, s => s?.HasEffect == true);
        OpenMasterEffectCommand = new RelayCommand<MasterEffectSlot>(OpenMasterEffect, s => s?.HasEffect == true);
        
        // Export command
        ExportCommand = new RelayCommand(OpenExportWindow, () => Tracks.Count > 0);
        
        // Options command
        OpenOptionsCommand = new RelayCommand(OpenOptionsWindow);
        
        // Project commands
        NewProjectCommand = new AsyncRelayCommand(CreateNewProjectAsync);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync);
        SaveProjectAsCommand = new AsyncRelayCommand(SaveProjectAsAsync);
        
        // Initialize File Menu (after commands are ready)
        FileMenuViewModel = new FileMenuViewModel(projectService, fileSystemService, settingsService, this);

        // Sync BPM between global state and local state
        GlobalState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GlobalApplicationState.BPM))
                OnPropertyChanged(nameof(BPM));
        };

        // Update global state when local BPM changes
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BPM))
                GlobalState.BPM = BPM;
        };
        
        // Initialize keyboard shortcuts - temporär auskommentiert für Debugging
        // KeyboardShortcutManager = new KeyboardShortcutManager(FileMenuViewModel);

        // Initialize master effect slots (10 slots) wired to the audio engine
        for (int i = 1; i <= 10; i++)
        {
            var slot = new MasterEffectSlot(i);
            slot.SetOwnerChain(MixEngine.MasterEffectChain);
            MasterEffectSlots.Add(slot);
        }
        
        // Subscribe to project events
        EnhancedProjectService.ProjectLoaded += OnProjectLoaded;
        EnhancedProjectService.ProjectSaved += OnProjectSaved;
        EnhancedProjectService.UnsavedChangesChanged += OnUnsavedChangesChanged;
        
        // Subscribe to collection changes
        Tracks.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (Track newTrack in e.NewItems)
                {
                    newTrack.PropertyChanged += OnTrackPropertyChanged;
                    
                    // Auto-create mixer channel for any track added to the collection
                    // (whether loaded from project or added manually)
                    if (!MixerChannels.Any(mc => mc.SourceTrack == newTrack))
                    {
                        CreateMixerChannelForTrack(newTrack);
                    }
                }
            }
            UpdateAllTrackVolumes();
        };

        // Must be created after Tracks is initialised
        ArrangementVm    = new ArrangementViewModel(this);
        EditMenuViewModel = new EditMenuViewModel(this);
        AudioBrowserVm   = new AudioBrowserViewModel();
        AudioBrowserVm.FileRequestedForPlaylist += (_, path) => AddFilesAsTrack([path]);
        
        // View menu
        ToggleAudioBrowserCommand = new RelayCommand(() => IsAudioBrowserVisible = !IsAudioBrowserVisible);
        BuildAnsichtMenu();

        // Subscribe to mixer channel routing changes so the audio graph
        // is rebuilt automatically when sends are added/removed during playback
        SubscribeToMixerChannelRouting();

        // No default tracks - start with empty project
        _trackCounter = 0;
    }
    
    /// <summary>
    /// Initializes some default tracks for testing purposes.
    /// </summary>
    private void InitializeDefaultTracks()
    {
        // Add empty tracks for drag and drop testing
        for (int i = 1; i <= 8; i++)
        {
            var track = new Track
            {
                TrackNumber = i,
                Title = $"Track {i}",
                Artist = "Empty",
                ChannelColor = TrackColors[(i - 1) % TrackColors.Length],
                Volume = 0.8,
                Pan = 0.0,
                FilePath = "" // Empty track
            };
            Tracks.Add(track);
        }
        
        _trackCounter = 8; // Update counter
    }

    /// <summary>
    /// Adds a new empty track to the arrangement.
    /// </summary>
    public void AddEmptyTrack()
    {
        _trackCounter++;
        
        var track = new Track
        {
            TrackNumber = _trackCounter,
            Title = $"Track {_trackCounter}",
            Artist = "Empty",
            ChannelColor = TrackColors[(_trackCounter - 1) % TrackColors.Length],
            Volume = 0.8,
            Pan = 0.0,
            FilePath = "" // Empty track
        };
        
        Tracks.Add(track);
        SelectedTrack = track; // Auto-select the new track
        // MixerChannel creation happens automatically via Tracks.CollectionChanged
    }

    /// <summary>
    /// Enhanced project service for JSON-based project management.
    /// </summary>
    public EnhancedProjectService EnhancedProjectService { get; private set; }

    /// <summary>
    /// Global application state for transport and tools.
    /// </summary>
    public GlobalApplicationState GlobalState { get; private set; }
    
    /// <summary>
    /// Transport service for audio engine communication.
    /// </summary>
    public TransportService TransportService { get; private set; }
    
    /// <summary>
    /// Tool state service for edit tool management.
    /// </summary>
    public ToolStateService ToolStateService { get; private set; }
    
    /// <summary>
    /// Audio engine service (mock implementation).
    /// </summary>
    public AudioEngineService AudioEngine { get; private set; }
    
    /// <summary>
    /// Centralized audio mix engine — single output device for all tracks.
    /// </summary>
    public AudioMixEngine MixEngine { get; }
    
    /// <summary>
    /// Global toolbar ViewModel.
    /// </summary>
    public GlobalToolbarViewModel GlobalToolbar { get; private set; }

    public ObservableCollection<Track> Tracks { get; } = [];

    /// <summary>
    /// File Menu ViewModel for dynamic menu generation
    /// </summary>
    public FileMenuViewModel FileMenuViewModel { get; private set; }
    
    /// <summary>
    /// Edit Menu ViewModel for undo/redo/clipboard operations
    /// </summary>
    public EditMenuViewModel EditMenuViewModel { get; private set; } = null!;
    
    /// <summary>
    /// Keyboard shortcut manager for handling input gestures
    /// </summary>
    public KeyboardShortcutManager? KeyboardShortcutManager { get; private set; }

    /// <summary>
    /// The FL Studio-style Arrangement / Playlist ViewModel.
    /// Syncs automatically with <see cref="Tracks"/>.
    /// </summary>
    public ArrangementViewModel ArrangementVm { get; private set; } = null!;

    /// <summary>
    /// Audio Browser ViewModel.  Double-clicking a file calls
    /// <see cref="AddFilesAsTrack"/> to load it into the Playlist.
    /// </summary>
    public AudioBrowserViewModel AudioBrowserVm { get; private set; } = null!;

    public bool IsAudioBrowserVisible
    {
        get => _isAudioBrowserVisible;
        set => SetField(ref _isAudioBrowserVisible, value);
    }

    public ICommand ToggleAudioBrowserCommand { get; private set; } = null!;
    public ObservableCollection<MenuItemViewModel> AnsichtMenuItems { get; } = [];

    private void BuildAnsichtMenu()
    {
        AnsichtMenuItems.Add(new MenuItemViewModel(new MenuItemModel
        {
            Header = "Audio Browser",
            Command = ToggleAudioBrowserCommand,
            InputGestureText = "Ctrl+B"
        }));
    }

    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            if (SetField(ref _selectedTrack, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetField(ref _isPlaying, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }
    
    public double BPM
    {
        get => _bpm;
        set => SetField(ref _bpm, Math.Clamp(value, 20, 300));
    }
    
    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set => SetField(ref _currentPosition, value);
    }

    // Master Channel Properties
    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            if (SetField(ref _masterVolume, Math.Clamp(value, 0.0, 1.0)))
            {
                OnPropertyChanged(nameof(MasterVolumeDisplay));
                UpdateAllTrackVolumes();
            }
        }
    }

    public double MasterPan
    {
        get => _masterPan;
        set => SetField(ref _masterPan, Math.Clamp(value, -1.0, 1.0));
    }

    public string MasterVolumeDisplay => MasterVolume > 0 
        ? $"{20 * Math.Log10(MasterVolume):F1} dB" 
        : "-∞ dB";

    /// <summary>Master output peak level for the left channel (linear 0..1+).</summary>
    public double MasterMeterLeft
    {
        get => _masterMeterLeft;
        private set => SetField(ref _masterMeterLeft, value);
    }

    /// <summary>Master output peak level for the right channel (linear 0..1+).</summary>
    public double MasterMeterRight
    {
        get => _masterMeterRight;
        private set => SetField(ref _masterMeterRight, value);
    }

    private void MasterMeterTimer_Tick(object? sender, EventArgs e)
    {
        var meter = MixEngine.MasterMeter;
        double peakL = meter.PeakLeft;
        double peakR = meter.PeakRight;
        meter.ResetPeaks();

        const double release = 0.75;
        MasterMeterLeft  = peakL >= _masterMeterLeft  ? peakL : _masterMeterLeft  * release;
        MasterMeterRight = peakR >= _masterMeterRight ? peakR : _masterMeterRight * release;
    }

    // Master Effects
    public ObservableCollection<MasterEffectSlot> MasterEffectSlots { get; } = [];
    
    // ── Mixer Routing System ────────────────────────────────────────────

    /// <summary>
    /// Mixer channels for routing system (FL Studio style).
    /// Channel 0 is reserved for Master.
    /// Channels 1+ can be empty or linked to tracks.
    /// </summary>
    public ObservableCollection<Models.Mixer.MixerChannel> MixerChannels { get; } = [];

    private Models.Mixer.MixerChannel? _selectedMixerChannel;

    /// <summary>
    /// Currently selected mixer channel (for routing visualization).
    /// </summary>
    public Models.Mixer.MixerChannel? SelectedMixerChannel
    {
        get => _selectedMixerChannel;
        set
        {
            if (_selectedMixerChannel != null)
                _selectedMixerChannel.IsSelected = false;
            if (SetField(ref _selectedMixerChannel, value) && value != null)
                value.IsSelected = true;
        }
    }

    /// <summary>
    /// Creates a new empty mixer channel.
    /// </summary>
    public void AddEmptyMixerChannel()
    {
        int nextNumber = MixerChannels.Count > 0 
            ? MixerChannels.Max(c => c.ChannelNumber) + 1 
            : 1;
        
        var channel = new Models.Mixer.MixerChannel(nextNumber)
        {
            Color = TrackColors[(nextNumber - 1) % TrackColors.Length]
        };
        
        MixerChannels.Add(channel);
        StatusMessage = $"✓ Mixer Channel {nextNumber} erstellt";
    }

    /// <summary>
    /// Removes an empty mixer channel.
    /// </summary>
    public void RemoveMixerChannel(Models.Mixer.MixerChannel channel)
    {
        if (channel.SourceTrack != null)
        {
            StatusMessage = "✗ Kann Channel mit Track nicht löschen";
            return;
        }

        // Remove any sends to this channel from other channels
        foreach (var otherChannel in MixerChannels)
        {
            otherChannel.SendTargets.Remove(channel.ChannelNumber);
        }

        MixerChannels.Remove(channel);
        if (SelectedMixerChannel == channel)
            SelectedMixerChannel = null;
            
        StatusMessage = $"✓ Mixer Channel {channel.ChannelNumber} entfernt";
    }

    /// <summary>
    /// Toggles routing between two channels.
    /// </summary>
    public void ToggleChannelRouting(Models.Mixer.MixerChannel source, Models.Mixer.MixerChannel target)
    {
        if (source == target)
        {
            StatusMessage = "✗ Kann Channel nicht zu sich selbst routen";
            return;
        }

        // Only check for cycles when we are about to ADD a send (not remove)
        if (!source.SendTargets.Contains(target.ChannelNumber))
        {
            if (WouldCreateCycle(source, target))
            {
                StatusMessage = $"✗ Loop verhindert: {target.Name} → … → {source.Name} existiert bereits";
                return;
            }
        }

        source.ToggleSend(target.ChannelNumber);

        if (source.SendTargets.Contains(target.ChannelNumber))
            StatusMessage = $"✓ {source.Name} → {target.Name}";
        else
            StatusMessage = $"✗ {source.Name} ⊗ {target.Name}";
    }

    /// <summary>
    /// Returns true if adding a send from <paramref name="source"/> to
    /// <paramref name="target"/> would create a routing cycle.
    ///
    /// A cycle exists when <paramref name="source"/> is reachable from
    /// <paramref name="target"/> through the current send graph — i.e.
    /// target already (directly or indirectly) feeds back into source.
    ///
    /// Uses iterative BFS on the directed send graph.
    /// </summary>
    public bool WouldCreateCycle(Models.Mixer.MixerChannel source, Models.Mixer.MixerChannel target)
    {
        // Self-loop is always a cycle
        if (source == target) return true;

        // BFS: starting from `target`, follow all SendTargets.
        // If we reach `source`, adding source→target would close the loop.
        var visited = new HashSet<int>();
        var queue   = new Queue<int>();
        queue.Enqueue(target.ChannelNumber);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            // If we've reached the source channel, a cycle would be created
            if (current == source.ChannelNumber) return true;

            var currentChannel = MixerChannels.FirstOrDefault(c => c.ChannelNumber == current);
            if (currentChannel == null) continue;

            foreach (var nextNum in currentChannel.SendTargets)
                if (!visited.Contains(nextNum))
                    queue.Enqueue(nextNum);
        }

        return false;
    }

    /// <summary>
    /// Moves a mixer channel to a new position in the display order.
    /// </summary>
    public void MoveMixerChannel(Models.Mixer.MixerChannel channel, int newIndex)
    {
        int oldIndex = MixerChannels.IndexOf(channel);
        if (oldIndex < 0 || oldIndex == newIndex) return;
        newIndex = Math.Clamp(newIndex, 0, MixerChannels.Count - 1);
        MixerChannels.Move(oldIndex, newIndex);
        StatusMessage = $"↔ {channel.Name} nach Position {newIndex + 1}";
    }
    
    /// <summary>
    /// Available effect types for selection.
    /// </summary>
    public (string Type, string Name, string Icon)[] AvailableEffects => EffectFactory.AvailableEffects;
    
    /// <summary>
    /// Currently selected effect slot for editing.
    /// </summary>
    public MasterEffectSlot? SelectedEffectSlot
    {
        get => _selectedEffectSlot;
        set => SetField(ref _selectedEffectSlot, value);
    }

    // Commands
    public ICommand PlayAllCommand { get; }
    public ICommand PauseAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand AddTrackCommand { get; }
    public ICommand RemoveTrackCommand { get; }
    public ICommand AnalyzeCommand { get; }
    public ICommand OpenSamplerCommand { get; private set; } = null!;
    public ICommand AddMasterEffectCommand { get; private set; } = null!;
    public ICommand RemoveMasterEffectCommand { get; private set; } = null!;
    public ICommand OpenMasterEffectCommand { get; private set; } = null!;
    public ICommand ExportCommand { get; private set; } = null!;
    public ICommand OpenOptionsCommand { get; private set; } = null!;
    public ICommand NewProjectCommand { get; private set; } = null!;
    public ICommand OpenProjectCommand { get; private set; } = null!;
    public ICommand SaveProjectCommand { get; private set; } = null!;
    public ICommand SaveProjectAsCommand { get; private set; } = null!;
    
    /// <summary>
    /// Opens the Sampler/Clip Editor for a track.
    /// </summary>
    public void OpenSampler(Track? track = null)
    {
        var targetTrack = track ?? SelectedTrack;
        if (targetTrack == null) return;
        
        var mainWindow = System.Windows.Application.Current?.MainWindow;
        Views.SamplerWindow.ShowForTrack(targetTrack, mainWindow);
    }

    #region Master Effects Methods

    /// <summary>
    /// Adds an effect to a slot. Shows a context menu to select the effect type.
    /// </summary>
    private void AddMasterEffect(MasterEffectSlot? slot)
    {
        if (slot == null) return;
        
        SelectedEffectSlot = slot;
        // The actual effect selection is done via context menu in XAML
    }
    
    /// <summary>
    /// Sets the effect type for the selected slot.
    /// </summary>
    public void SetEffectType(MasterEffectSlot slot, string effectType)
    {
        var effect = EffectFactory.Create(effectType);
        if (effect != null)
        {
            slot.Effect = effect;
            slot.IsExpanded = true;
            StatusMessage = $"✓ {effect.Name} zu Slot {slot.SlotNumber} hinzugefügt";
        }
    }
    
    /// <summary>
    /// Removes an effect from a slot.
    /// </summary>
    private void RemoveMasterEffect(MasterEffectSlot? slot)
    {
        if (slot?.Effect == null) return;
        
        var effectName = slot.Effect.Name;
        slot.Effect = null;
        slot.IsExpanded = false;
        StatusMessage = $"✓ {effectName} aus Slot {slot.SlotNumber} entfernt";
    }
    
    /// <summary>
    /// Opens/expands an effect for editing.
    /// </summary>
    private void OpenMasterEffect(MasterEffectSlot? slot)
    {
        if (slot?.Effect == null) return;
        
        slot.IsExpanded = !slot.IsExpanded;
    }

    #endregion

    /// <summary>
    /// Opens a save dialog, then shows the export window which renders immediately.
    /// </summary>
    private void OpenExportWindow()
    {
        // Stop playback first
        if (IsPlaying) StopAll();
        
        // Ask the user where to export first
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Audio exportieren",
            Filter = "WAV Audio|*.wav|MP3 Audio|*.mp3|FLAC Audio|*.flac|All Files|*.*",
            DefaultExt = ".wav",
            FileName = "export.wav"
        };

        if (dlg.ShowDialog() != true)
            return;

        var exportWindow = new Views.ExportWindow(
            Tracks.ToList().AsReadOnly(),
            MixEngine.MasterEffectChain,
            MasterVolume,
            dlg.FileName)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
        };
        exportWindow.ShowDialog();
    }

    /// <summary>
    /// Opens the Options / Settings window.
    /// </summary>
    private void OpenOptionsWindow()
    {
        var optionsWindow = new Views.OptionsWindow(this)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
        };
        optionsWindow.ShowDialog();
    }

    /// <summary>
    /// Plays all tracks simultaneously, honouring the mixer routing graph.
    /// Channels with SendTargets route their audio into the target channel's sub-mixer;
    /// channels with no sends go straight to the master bus.
    /// </summary>
    private void PlayAll()
    {
        var hasSolo    = Tracks.Any(t => t.IsSolo);
        var playable   = Tracks.Where(t => !string.IsNullOrEmpty(t.FilePath)).ToList();
        var startBeat  = ArrangementVm.PlayheadBeat;
        var startTime  = TimeSpan.FromSeconds(startBeat * 60.0 / BPM);

        // ── Phase 1: pre-load, seek, volume ──────────────────────────────
        foreach (var track in playable)
        {
            track.TargetMixFormat = MixEngine.MixFormat;
            track.EnsureLoaded();
            track.SetPosition(startTime);
            track.UpdatePlayerVolume(MasterVolume, hasSolo);
        }

        // ── Phase 2: build routing graph ─────────────────────────────────
        RebuildRoutingGraph();

        // ── Phase 3: start playback ───────────────────────────────────────
        MixEngine.Play();
        foreach (var track in playable)
            track.Play();

        IsPlaying      = true;
        CurrentPosition = startTime;
        StatusMessage  = $"▶ Spielt {playable.Count} Track(s)";
    }

    /// <summary>
    /// Rebuilds the NAudio routing graph from the current MixerChannel send targets.
    /// 
    /// Signal flow when Channel A sends to Channel B:
    ///   Track A → sub-mixer[B]
    ///   Track B → sub-mixer[B]  (at MasterVolume only; Track.Volume applied as bus fader)
    ///   sub-mixer[B] → VolumeSampleProvider(B.Volume) → MeteringSampleProvider → master
    ///
    /// This means:
    ///   • Channel B's fader now controls the COMBINED level of A + B. ✓
    ///   • Channel B's meter shows the combined bus signal.           ✓
    ///   • Muting B silences A's signal too.                         ✓
    /// </summary>
    private void RebuildRoutingGraph()
    {
        bool hasSolo = Tracks.Any(t => t.IsSolo);

        // ── Clean up previous routing state ──────────────────────────────────
        foreach (var ch in MixerChannels)
            ch.SourceTrack?.SetBusMeter(null);
        _busFaders.Clear();
        _busPanners.Clear();
        MixEngine.RemoveAllInputs();

        // ── Identify bus channels (those that receive at least one send) ──────
        var allTargetNums = MixerChannels
            .SelectMany(c => c.SendTargets)
            .ToHashSet();

        var subMixers = new Dictionary<int, NAudio.Wave.SampleProviders.MixingSampleProvider>();
        foreach (var targetNum in allTargetNums)
            subMixers[targetNum] = new NAudio.Wave.SampleProviders.MixingSampleProvider(MixEngine.MixFormat)
                { ReadFully = true };

        // ── Phase 1 — Fan-out with BroadcastSampleProvider ───────────────────
        // Problem without this: two sub-mixers both hold the same ISampleProvider
        // reference.  NAudio reads inputs sequentially, so sub-mixer B reads
        // samples 0–N and sub-mixer C then reads N+1–2N from the SAME stream —
        // a full buffer-length desync (≈ 23 ms at 44 100 Hz).
        //
        // BroadcastSampleProvider caches the result of the first Read and hands
        // identical data to every subsequent consumer in the same audio cycle.
        foreach (var channel in MixerChannels)
        {
            var track = channel.SourceTrack;
            if (track?.Output == null) continue;

            var adapted    = MixEngine.AdaptFormat(track.Output);
            var targetNums = channel.SendTargets.ToList();

            if (targetNums.Count > 0)
            {
                // Split the stream: each target sub-mixer gets a synchronised consumer
                var consumers = DAW.Audio.BroadcastSampleProvider.Split(adapted, targetNums.Count);
                for (int i = 0; i < targetNums.Count; i++)
                    if (subMixers.TryGetValue(targetNums[i], out var sm))
                        sm.AddMixerInput(consumers[i]);
            }
            else
            {
                // No sends: own audio goes straight to master
                // Apply channel pan before reaching master
                ISampleProvider toMaster = adapted;
                if (Math.Abs(channel.Pan) > 0.001)
                {
                    var panProvider = new DAW.Audio.VolumePanSampleProvider(adapted);
                    panProvider.Pan = (float)channel.Pan;
                    toMaster = panProvider;
                }
                MixEngine.AddInput(toMaster);
            }
        }

        // ── Phase 2 — Plugin Delay Compensation (PDC) ────────────────────────
        // For each sub-mixer, find the maximum EffectChain.TotalLatencySamples
        // across all contributing sources and prepend DelayLineProviders on
        // shorter paths so every input arrives at the mix point in time.
        //
        // All built-in effects currently return LatencySamples = 0 (sample-accurate
        // processing), so this loop is a no-op today but fires automatically as
        // soon as any effect (e.g. look-ahead compressor) reports non-zero latency.
        foreach (var (channelNum, subMixer) in subMixers)
        {
            var senderLatencies = MixerChannels
                .Where(c => c.SendTargets.Contains(channelNum) && c.SourceTrack?.Output != null)
                .Select(c => (channel: c, latency: c.SourceTrack!.EffectChain.TotalLatencySamples))
                .ToList();

            int maxLatency = senderLatencies.Count > 0 ? senderLatencies.Max(s => s.latency) : 0;
            if (maxLatency == 0) continue;

            // Rebuild this sub-mixer's inputs with compensation delays
            // (clear + re-add because NAudio's Sources list is internal)
            foreach (var (senderCh, latency) in senderLatencies)
            {
                int compensation = maxLatency - latency;
                if (compensation <= 0) continue;

                var adapted     = MixEngine.AdaptFormat(senderCh.SourceTrack!.Output);
                var compensated = (NAudio.Wave.ISampleProvider)
                    new DAW.Audio.DelayLineProvider(adapted, compensation);

                subMixer.AddMixerInput(compensated);
            }
        }

        // ── Phase 3 — Connect buses to master via reactive bus fader + pan + meter ──
        foreach (var (channelNum, subMixer) in subMixers)
        {
            var targetCh  = MixerChannels.FirstOrDefault(c => c.ChannelNumber == channelNum);
            var targetTrk = targetCh?.SourceTrack;

            bool muted = targetTrk != null &&
                         (targetTrk.IsMuted || !targetTrk.IsEnabled || (hasSolo && !targetTrk.IsSolo));
            float busVol = muted ? 0f : (float)(targetTrk?.Volume ?? 1.0);
            float busPan = (float)(targetCh?.Pan ?? 0.0);

            // Use VolumePanSampleProvider so both volume AND pan are reactive on the bus
            var busPanner = new DAW.Audio.VolumePanSampleProvider(subMixer)
            {
                Volume = busVol,
                Pan    = busPan
            };
            if (targetTrk != null)
            {
                _busFaders[targetTrk]  = new NAudio.Wave.SampleProviders.VolumeSampleProvider(subMixer) { Volume = busVol }; // kept for UpdateBusFaderForTrack compat
                _busPanners[targetTrk] = busPanner;
            }

            // Replace busFader with busPanner in the signal chain
            var busMeter = new DAW.Audio.MeteringSampleProvider(busPanner);
            targetTrk?.SetBusMeter(busMeter);

            if (targetCh?.SendTargets.Count > 0)
            {
                foreach (var nextNum in targetCh.SendTargets)
                {
                    if (subMixers.TryGetValue(nextNum, out var nextSm))
                        nextSm.AddMixerInput(busMeter);
                    else
                        MixEngine.AddInput(busMeter);
                }
            }
            else
            {
                MixEngine.AddInput(busMeter);
            }
        }
    }

    /// <summary>
    /// Reactively updates the send bus fader volume AND pan when a routing-target
    /// track's Volume, IsMuted, IsSolo, or the mixer channel's Pan changes.
    /// </summary>
    private void UpdateBusFaderForTrack(Track track)
    {
        var hasSolo = Tracks.Any(t => t.IsSolo);
        bool muted  = track.IsMuted || !track.IsEnabled || (hasSolo && !track.IsSolo);
        float vol   = muted ? 0f : (float)track.Volume;

        // Update legacy fader (kept for API compat)
        if (_busFaders.TryGetValue(track, out var fader))
            fader.Volume = vol;

        // Update the pan-capable bus panner (used in the actual signal chain)
        if (_busPanners.TryGetValue(track, out var panner))
        {
            panner.Volume = vol;
            // Sync pan from the mixer channel
            var mixCh = MixerChannels.FirstOrDefault(c => c.SourceTrack == track);
            if (mixCh != null)
                panner.Pan = (float)mixCh.Pan;
        }
    }

    /// <summary>
    /// Reactively updates the bus panner when a MixerChannel's Pan property changes.
    /// </summary>
    private void UpdateBusPanForChannel(Models.Mixer.MixerChannel channel)
    {
        var track = channel.SourceTrack;
        if (track == null) return;
        if (_busPanners.TryGetValue(track, out var panner))
            panner.Pan = (float)channel.Pan;
    }

    /// <summary>
    /// Subscribes to SendTargets changes on every mixer channel so routing changes
    /// during playback are applied immediately without stopping the audio device.
    /// </summary>
    private void SubscribeToMixerChannelRouting()
    {
        foreach (var channel in MixerChannels)
            SubscribeChannelRouting(channel);

        MixerChannels.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (Models.Mixer.MixerChannel ch in e.NewItems)
                    SubscribeChannelRouting(ch);
        };
    }

    private void SubscribeChannelRouting(Models.Mixer.MixerChannel channel)
    {
        channel.SendTargets.CollectionChanged += (_, _) =>
        {
            if (IsPlaying)
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(RebuildRoutingGraph);
        };

        // Reactively apply pan changes to the live bus panner without rebuilding
        channel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Models.Mixer.MixerChannel.Pan))
                UpdateBusPanForChannel(channel);
        };
    }

    /// <summary>
    /// Pauses all tracks.
    /// </summary>
    private void PauseAll()
    {
        MixEngine.Pause();
        
        foreach (var track in Tracks)
        {
            track.Pause();
        }
        
        IsPlaying = false;
        StatusMessage = "⏸ Pausiert";
    }

    /// <summary>
    /// Stops all tracks and resets position.
    /// </summary>
    private void StopAll()
    {
        MixEngine.Stop();
        MixEngine.RemoveAllInputs();

        // Clear bus-meter overrides, bus faders, and bus panners
        foreach (var ch in MixerChannels)
            ch.SourceTrack?.SetBusMeter(null);
        _busFaders.Clear();
        _busPanners.Clear();

        foreach (var track in Tracks)
            track.Stop();

        CurrentPosition = TimeSpan.Zero;
        IsPlaying = false;
        StatusMessage = "⏹ Gestoppt";
    }

    private void AddTrack()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Audio Dateien|*.mp3;*.wav;*.wma;*.m4a;*.flac;*.ogg|Alle Dateien|*.*",
            Multiselect = true,
            Title = "Audio-Dateien hinzufügen"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            AddFilesAsTrack(openFileDialog.FileNames);
        }
    }
    
    /// <summary>
    /// Adds files as tracks - used by both dialog and drag & drop.
    /// </summary>
    public void AddFilesAsTrack(string[] filePaths)
    {
        foreach (var filePath in filePaths)
        {
            var track = CreateTrackFromFile(filePath);
            if (track != null)
            {
                Tracks.Add(track);
                // MixerChannel creation happens automatically via Tracks.CollectionChanged
            }
        }
        
        StatusMessage = $"✓ {filePaths.Length} Track(s) hinzugefügt";
    }
    
    /// <summary>
    /// Creates a mixer channel for a track.
    /// </summary>
    private void CreateMixerChannelForTrack(Track track)
    {
        int channelNumber = MixerChannels.Count > 0 
            ? MixerChannels.Max(c => c.ChannelNumber) + 1 
            : 1;
        
        var channel = new Models.Mixer.MixerChannel(channelNumber)
        {
            SourceTrack = track,
            Name = track.Title,
            Color = track.ChannelColor,
            Volume = track.Volume,
            Pan = track.Pan
        };
        
        MixerChannels.Add(channel);
    }
    
    /// <summary>
    /// Creates a track from a file path.
    /// </summary>
    private Track? CreateTrackFromFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return null;
        
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is not (".mp3" or ".wav" or ".wma" or ".m4a" or ".flac" or ".ogg"))
            return null;
            
        _trackCounter++;
        
        return new Track
        {
            TrackNumber = _trackCounter,
            FilePath = filePath,
            Title = System.IO.Path.GetFileNameWithoutExtension(filePath),
            Artist = "Unbekannt",
            ChannelColor = TrackColors[(_trackCounter - 1) % TrackColors.Length]
        };
    }

    private void RemoveTrack()
    {
        if (SelectedTrack is null) return;

        // Disconnect from mix bus before disposing
        if (SelectedTrack.Output is not null)
            MixEngine.RemoveInput(SelectedTrack.Output);
        
        SelectedTrack.Stop();
        SelectedTrack.Dispose();
        
        var trackToRemove = SelectedTrack;
        
        // Remove associated mixer channel
        var mixerChannel = MixerChannels.FirstOrDefault(mc => mc.SourceTrack == trackToRemove);
        if (mixerChannel != null)
        {
            // Remove any sends to/from this channel
            foreach (var otherChannel in MixerChannels)
            {
                otherChannel.SendTargets.Remove(mixerChannel.ChannelNumber);
            }
            MixerChannels.Remove(mixerChannel);
        }
        
        SelectedTrack = null;
        Tracks.Remove(trackToRemove);
        
        // Renumber tracks
        for (int i = 0; i < Tracks.Count; i++)
        {
            Tracks[i].TrackNumber = i + 1;
        }
        
        StatusMessage = "Track entfernt";
    }

    private async void Analyze()
    {
        if (SelectedTrack is null) return;

        StatusMessage = $"🔬 Analysiere: {SelectedTrack.Title}...";

        // Placeholder for AI analysis
        await Task.Delay(1500);

        SelectedTrack.IsAnalyzed = true;
        SelectedTrack.AnalysisResult = $"BPM: {Random.Shared.Next(80, 180)} | Key: {GetRandomKey()} | Genre: Electronic";
        StatusMessage = $"✓ Analyse abgeschlossen: {SelectedTrack.Title}";
    }
    
    private static string GetRandomKey()
    {
        string[] keys = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        string[] modes = ["Dur", "Moll"];
        return $"{keys[Random.Shared.Next(keys.Length)]}-{modes[Random.Shared.Next(modes.Length)]}";
    }

    private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Track track) return;

        switch (e.PropertyName)
        {
            case nameof(Track.Volume):
            case nameof(Track.IsMuted):
            case nameof(Track.IsSolo):
                UpdateAllTrackVolumes();
                UpdateBusFaderForTrack(track); // keep bus fader in sync for routing targets
                break;
        }
    }

    /// <summary>
    /// Updates volume for all tracks considering solo/mute state.
    /// </summary>
    private void UpdateAllTrackVolumes()
    {
        var hasSolo = Tracks.Any(t => t.IsSolo);
        
        foreach (var track in Tracks)
        {
            track.UpdatePlayerVolume(MasterVolume, hasSolo);
        }
    }

    #region Project Commands

    private async Task CreateNewProjectAsync()
    {
        try
        {
            var project = await EnhancedProjectService.CreateNewProjectAsync();
            StatusMessage = $"✓ Neues Projekt erstellt: {project.ProjectName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Fehler beim Erstellen: {ex.Message}";
        }
    }

    private async Task OpenProjectAsync()
    {
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Projekt öffnen",
                Filter = "Dragon Projekt (*.dragon)|*.dragon|Alle Dateien (*.*)|*.*",
                DefaultExt = ".dragon",
                InitialDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "DAW Projects")
            };
            
            if (!Directory.Exists(openFileDialog.InitialDirectory))
            {
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            if (openFileDialog.ShowDialog() == true)
            {
                var project = await EnhancedProjectService.OpenProjectAsync(openFileDialog.FileName);
                
                // Resolve file paths: prefer Sounds/ subfolder next to the .dragon file
                var projectDir = Path.GetDirectoryName(openFileDialog.FileName)!;
                var soundsDir = Path.Combine(projectDir, "Sounds");
                
                foreach (var track in project.Tracks)
                {
                    track.FilePath = ResolveAudioPath(track.FilePath, soundsDir);
                    foreach (var clip in track.Clips)
                    {
                        if (!string.IsNullOrEmpty(clip.SourceFilePath))
                            clip.SourceFilePath = ResolveAudioPath(clip.SourceFilePath, soundsDir);
                    }
                }
                
                await EnhancedProjectService.ImportProjectState(project, this);
                StatusMessage = $"✓ Projekt geöffnet: {project.ProjectName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Fehler beim Öffnen: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Resolves an audio file path: first checks if the file exists as-is (absolute),
    /// then looks in the Sounds subfolder by filename.
    /// </summary>
    private static string ResolveAudioPath(string filePath, string soundsDir)
    {
        if (string.IsNullOrEmpty(filePath)) return filePath;
        
        // If the absolute path still works, use it
        if (File.Exists(filePath)) return filePath;
        
        // Try finding the file in the project's Sounds folder
        var fileName = Path.GetFileName(filePath);
        var localPath = Path.Combine(soundsDir, fileName);
        if (File.Exists(localPath)) return localPath;
        
        return filePath; // fallback — file may be missing
    }

    private async Task SaveProjectAsync()
    {
        try
        {
            // If no project path exists, use Save As
            if (string.IsNullOrEmpty(EnhancedProjectService.CurrentProjectPath))
            {
                await SaveProjectAsAsync();
                return;
            }
            
            var project = EnhancedProjectService.ExportCurrentState(this);
            var dragonPath = EnhancedProjectService.CurrentProjectPath;
            var projectDir = Path.GetDirectoryName(dragonPath)!;
            var soundsDir = Path.Combine(projectDir, "Sounds");
            
            // Copy any new audio files into the Sounds folder
            Directory.CreateDirectory(soundsDir);
            CopyAudioFilesToSoundsFolder(project, soundsDir);
            
            // Update file paths in the project to be relative (just filename)
            MakePathsRelative(project);
            
            project.FilePath = dragonPath;
            EnhancedProjectService.CurrentProject = project;
            
            var jsonContent = System.Text.Json.JsonSerializer.Serialize(project, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(dragonPath, jsonContent);
            
            StatusMessage = $"✓ Projekt gespeichert: {project.ProjectName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Fehler beim Speichern: {ex.Message}";
        }
    }

    private async Task SaveProjectAsAsync()
    {
        try
        {
            // Use native folder picker to choose a parent directory
            var defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "DAW Projects");
            
            if (!Directory.Exists(defaultDir))
                Directory.CreateDirectory(defaultDir);
            
            var selectedFolder = FolderPicker.ShowDialog("Ordner für das Projekt wählen", defaultDir);
            if (string.IsNullOrEmpty(selectedFolder))
                return;
            
            // The selected folder IS the project folder — use its name as the project name
            var safeName = SanitizeFileName(Path.GetFileName(selectedFolder));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Projekt";
            
            // Project folder structure:
            //   <selected_folder>/
            //     Sounds/
            //     <ProjectName>.dragon
            var projectDir = selectedFolder;
            var soundsDir = Path.Combine(projectDir, "Sounds");
            var dragonPath = Path.Combine(projectDir, $"{safeName}.dragon");
            
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(soundsDir);
            
            var project = EnhancedProjectService.ExportCurrentState(this);
            project.ProjectName = safeName;
            
            // Copy all audio files into the Sounds subfolder
            CopyAudioFilesToSoundsFolder(project, soundsDir);
            
            // Update file paths in the project to relative (filename only)
            MakePathsRelative(project);
            
            project.FilePath = dragonPath;
            EnhancedProjectService.CurrentProject = project;
            EnhancedProjectService.CurrentProjectPath = dragonPath;
            
            var jsonContent = System.Text.Json.JsonSerializer.Serialize(project, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(dragonPath, jsonContent);
            
            StatusMessage = $"✓ Projekt gespeichert: {safeName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Fehler beim Speichern unter: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Copies all referenced audio files into the project's Sounds folder.
    /// </summary>
    private static void CopyAudioFilesToSoundsFolder(DawProject project, string soundsDir)
    {
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var track in project.Tracks)
        {
            CopySingleFile(track.FilePath, soundsDir, copied);
            foreach (var clip in track.Clips)
                CopySingleFile(clip.SourceFilePath, soundsDir, copied);
        }
    }
    
    private static void CopySingleFile(string? filePath, string soundsDir, HashSet<string> copied)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
        
        var fileName = Path.GetFileName(filePath);
        if (!copied.Add(fileName)) return; // already copied
        
        var dest = Path.Combine(soundsDir, fileName);
        if (!File.Exists(dest))
            File.Copy(filePath, dest);
    }
    
    /// <summary>
    /// Converts all absolute audio file paths in the project to just the filename
    /// (relative to the Sounds subfolder).
    /// </summary>
    private static void MakePathsRelative(DawProject project)
    {
        foreach (var track in project.Tracks)
        {
            if (!string.IsNullOrEmpty(track.FilePath))
                track.FilePath = Path.GetFileName(track.FilePath);
            
            foreach (var clip in track.Clips)
            {
                if (!string.IsNullOrEmpty(clip.SourceFilePath))
                    clip.SourceFilePath = Path.GetFileName(clip.SourceFilePath);
            }
        }
        
        foreach (var fileRef in project.Files)
        {
            fileRef.RelativePath = Path.GetFileName(fileRef.OriginalPath);
        }
    }
    
    /// <summary>
    /// Sanitizes a string for use as a folder/file name.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Projekt" : sanitized.Trim();
    }

    #endregion

    #region Project Event Handlers

    private async void OnProjectLoaded(object? sender, ProjectLoadedEventArgs e)
    {
        StatusMessage = $"Projekt geladen: {e.Project.ProjectName}";
        OnPropertyChanged(nameof(CurrentPosition)); // Update UI
        
        // Mark any further changes as requiring save
        PropertyChanged += (_, _) => EnhancedProjectService.MarkAsModified();
        
        // Update command availability
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void OnProjectSaved(object? sender, ProjectSavedEventArgs e)
    {
        StatusMessage = $"Projekt gespeichert: {Path.GetFileNameWithoutExtension(e.FilePath)}";
    }

    private void OnUnsavedChangesChanged(object? sender, bool hasUnsavedChanges)
    {
        // Update window title or status
        var status = hasUnsavedChanges ? "Ungespeicherte Änderungen" : "Alle Änderungen gespeichert";
        StatusMessage = status;
        
        // Update command availability
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
