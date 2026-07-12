using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using DAW.Commands;
using DAW.Views;
using DAW.Services;
using DAW.Input;
using DAW.Audio;
using DAW.Audio.Effects;
using Microsoft.Win32;
using NAudio.Wave;
using DAW.MVVM.Models;
using DAW.MVVM.Models.Mixer;
using DAW.MVVM.Models.Sequencer;

namespace DAW.MVVM.ViewModels;

/// <summary>
/// Main ViewModel for the DAW application.
///
/// Playback isolation:
///   • Channel Rack (PatternVm) plays step-sequencer samples independently via
///     OnPatternStepTriggered → MixEngine.PlayOneShot.  Starting/stopping the
///     Channel Rack does NOT start/stop the Playlist.
///   • Playlist (PlayAll/StopAll) plays arrangement audio tracks and fires the
///     metronome. It does NOT start/stop the Channel Rack sequencer.
///
/// Metronome:
///   • Playlist-only. A DispatcherTimer ticks every beat based on BPM.
///   • Fires a synthesised click via MixEngine.PlayOneShot when active.
///   • Enabled/disabled by GlobalState.IsMetronomeEnabled.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private static readonly Color[] TrackColors =
    [
        Color.FromRgb(255, 152,   0),
        Color.FromRgb( 76, 175,  80),
        Color.FromRgb( 33, 150, 243),
        Color.FromRgb(156,  39, 176),
        Color.FromRgb(244,  67,  54),
        Color.FromRgb(  0, 188, 212),
        Color.FromRgb(255, 235,  59),
        Color.FromRgb(233,  30,  99),
    ];

    private Track?          _selectedTrack;
    private bool            _isAudioBrowserVisible   = true;
    private bool            _isPatternBrowserVisible = true;
    private bool            _isPlaying;
    private string          _statusMessage           = "Bereit";
    private double          _masterVolume            = 0.8;
    private double          _masterPan               = 0.0;
    private double          _masterMeterLeft;
    private double          _masterMeterRight;
    private DispatcherTimer? _masterMeterTimer;
    private double          _bpm                     = 140.0;
    private TimeSpan        _currentPosition         = TimeSpan.Zero;
    private int             _trackCounter;
    private MasterEffectSlot? _selectedEffectSlot;
    private int             _activeTabIndex          = 2; // 0=Playlist 1=Mixer 2=ChannelRack 3=PianoRoll

    // ── Metronome ────────────────────────────────────────────────────────────
    private DispatcherTimer? _metronomeTimer;
    private int              _metronomeBeat = 0;  // beat counter within bar (0-based)

    // ── Playlist pattern engine ──────────────────────────────────────────────
    private PlaylistPatternEngine? _patternEngine;

    private readonly Dictionary<Track, NAudio.Wave.SampleProviders.VolumeSampleProvider> _busFaders  = new();

    // ── Channel-Rack → Mixer routing ────────────────────────────────────
    // One mixer strip + audio bus per rack channel that has a sample loaded.
    // Key: the rack ChannelModel.
    //
    // StripTrack: the MixerChannelControl UI binds everything (Title, color,
    // fader, pan, mute, meters, FX slots) to MixerChannel.SourceTrack — so each
    // rack strip gets a lightweight backing Track that exists ONLY as the
    // strip's data context (FilePath stays empty → audibly inert, and it is
    // never added to the playlist Tracks collection).
    //
    // Wrapped: outermost provider registered in the mix engine
    // (bus → strip-track EffectChain → MeteringSampleProvider).
    private sealed record RackBusEntry(
        MixerChannel Strip,
        ChannelRackBusProvider    Bus,
        Track                     StripTrack,
        NAudio.Wave.ISampleProvider Wrapped);
    private readonly Dictionary<ChannelModel, RackBusEntry> _rackBuses = new();
    private readonly Dictionary<Track, VolumePanSampleProvider>                _busPanners = new();

    public MainViewModel()
    {
        MixEngine = new AudioMixEngine();

        _masterMeterTimer = new DispatcherTimer(DispatcherPriority.Render)
            { Interval = TimeSpan.FromMilliseconds(50) };
        _masterMeterTimer.Tick += MasterMeterTimer_Tick;
        _masterMeterTimer.Start();

        GlobalState      = new GlobalApplicationState();
        AudioEngine      = new AudioEngineService();
        TransportService = new TransportService(GlobalState, AudioEngine);
        ToolStateService = new ToolStateService(GlobalState);
        GlobalToolbar    = new GlobalToolbarViewModel(GlobalState, TransportService, ToolStateService);

        var fileSystemService = new FileSystemService();
        var settingsService   = new SettingsService();
        var projectService    = new ProjectService(fileSystemService, settingsService);
        EnhancedProjectService = new EnhancedProjectService(fileSystemService, settingsService);

        PlayAllCommand   = new RelayCommand(PlayAll);
        PauseAllCommand  = new RelayCommand(PauseAll, () => IsPlaying);
        StopAllCommand   = new RelayCommand(StopAll);
        AddTrackCommand  = new RelayCommand(AddTrack);
        RemoveTrackCommand   = new RelayCommand(RemoveTrack,   () => SelectedTrack is not null);
        AnalyzeCommand       = new RelayCommand(Analyze,       () => SelectedTrack is not null);
        OpenSamplerCommand   = new RelayCommand<Track>(t => OpenSampler(t), _ => SelectedTrack is not null);

        AddMasterEffectCommand    = new RelayCommand<MasterEffectSlot>(AddMasterEffect);
        RemoveMasterEffectCommand = new RelayCommand<MasterEffectSlot>(RemoveMasterEffect, s => s?.HasEffect == true);
        OpenMasterEffectCommand   = new RelayCommand<MasterEffectSlot>(OpenMasterEffect,   s => s?.HasEffect == true);

        ExportCommand    = new RelayCommand(OpenExportWindow, () => Tracks.Count > 0);
        OpenOptionsCommand = new RelayCommand(OpenOptionsWindow);

        NewProjectCommand    = new AsyncRelayCommand(CreateNewProjectAsync);
        OpenProjectCommand   = new AsyncRelayCommand(OpenProjectAsync);
        SaveProjectCommand   = new AsyncRelayCommand(SaveProjectAsync);
        SaveProjectAsCommand = new AsyncRelayCommand(SaveProjectAsAsync);

        FileMenuViewModel = new FileMenuViewModel(projectService, fileSystemService, settingsService, this);

        GlobalState.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GlobalApplicationState.BPM))
                OnPropertyChanged(nameof(BPM));
            if (e.PropertyName == nameof(GlobalApplicationState.IsMetronomeEnabled))
                UpdateMetronomeState();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BPM))
            {
                GlobalState.BPM = BPM;
                UpdateMetronomeInterval();
            }
        };

        for (int i = 1; i <= 10; i++)
        {
            var slot = new MasterEffectSlot(i);
            slot.SetOwnerChain(MixEngine.MasterEffectChain);
            MasterEffectSlots.Add(slot);
        }

        EnhancedProjectService.ProjectLoaded          += OnProjectLoaded;
        EnhancedProjectService.ProjectSaved           += OnProjectSaved;
        EnhancedProjectService.UnsavedChangesChanged  += OnUnsavedChangesChanged;

        Tracks.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (Track newTrack in e.NewItems)
                {
                    newTrack.PropertyChanged += OnTrackPropertyChanged;
                    if (!MixerChannels.Any(mc => mc.SourceTrack == newTrack))
                        CreateMixerChannelForTrack(newTrack);
                }
            UpdateAllTrackVolumes();
        };

        ArrangementVm = new ArrangementViewModel(this);
        PatternVm     = new ViewModels.Sequencer.PatternViewModel(BPM);
        PianoRollVm   = new ViewModels.PianoRoll.PianoRollViewModel();

        // Piano key preview — play the selected channel's sample when a key is pressed
        PianoRollVm.NotePreviewStarted += OnPianoKeyPreview;

        // Channel Rack step triggers → PlayOneShot only (no playlist involvement)
        PatternVm.NavigateToPianoRollRequested += OnNavigateToPianoRoll;
        PatternVm.AddToPlaylistRequested       += OnAddPatternToPlaylist;
        PatternVm.StepTriggered                += OnPatternStepTriggered;

        // Inject audio engine into all current and future channels for zero-latency preloading
        PatternVm.Channels.CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (ViewModels.Sequencer.ChannelViewModel ch in e.NewItems)
                WireChannelAudio(ch);
        };
        foreach (var ch in PatternVm.Channels)
            WireChannelAudio(ch);

        PatternVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModels.Sequencer.PatternViewModel.ActivePattern)
                               or nameof(ViewModels.Sequencer.PatternViewModel.Channels))
                SyncPianoRollChannels();
        };
        PatternVm.Channels.CollectionChanged += (_, _) => SyncPianoRollChannels();
        SyncPianoRollChannels();
        PianoRollVm.OpenChannel(null, PatternVm.Channels);

        PatternVm.BPM = BPM;
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BPM)) PatternVm.BPM = BPM; };

        EditMenuViewModel = new EditMenuViewModel(this);
        AudioBrowserVm    = new AudioBrowserViewModel();
        AudioBrowserVm.FileRequestedForPlaylist += (_, path) => AddFilesAsTrack([path]);

        ToggleAudioBrowserCommand          = new RelayCommand(() => IsAudioBrowserVisible          = !IsAudioBrowserVisible);
        TogglePatternBrowserCommand        = new RelayCommand(() => IsPatternBrowserVisible         = !IsPatternBrowserVisible);
        BuildAnsichtMenu();
        SubscribeToMixerChannelRouting();
        _trackCounter = 0;
    }

    // ── Services / sub-VMs ───────────────────────────────────────────────────
    public EnhancedProjectService              EnhancedProjectService { get; private set; }
    public GlobalApplicationState              GlobalState            { get; private set; }
    public TransportService                    TransportService       { get; private set; }
    public ToolStateService                    ToolStateService       { get; private set; }
    public AudioEngineService                  AudioEngine            { get; private set; }
    public AudioMixEngine                      MixEngine              { get; }
    public GlobalToolbarViewModel              GlobalToolbar          { get; private set; }
    public ObservableCollection<Track>         Tracks                 { get; } = [];
    public FileMenuViewModel                   FileMenuViewModel      { get; private set; }
    public EditMenuViewModel                   EditMenuViewModel      { get; private set; } = null!;
    public KeyboardShortcutManager?            KeyboardShortcutManager { get; private set; }
    public ArrangementViewModel                ArrangementVm          { get; private set; } = null!;
    public ViewModels.Sequencer.PatternViewModel PatternVm            { get; private set; } = null!;
    public ViewModels.PianoRoll.PianoRollViewModel PianoRollVm        { get; private set; } = null!;
    public AudioBrowserViewModel               AudioBrowserVm         { get; private set; } = null!;

    // ── Visibility / UI state ────────────────────────────────────────────────

    public bool IsAudioBrowserVisible
    {
        get => _isAudioBrowserVisible;
        set => SetField(ref _isAudioBrowserVisible, value);
    }

    /// <summary>
    /// Controls the Pattern / Channel browser panel left of the Playlist timeline.
    /// Toggled by View → "Patterns &amp; Channels" and the collapse button in the panel.
    /// </summary>
    public bool IsPatternBrowserVisible
    {
        get => _isPatternBrowserVisible;
        set => SetField(ref _isPatternBrowserVisible, value);
    }

    public int ActiveTabIndex
    {
        get => _activeTabIndex;
        set => SetField(ref _activeTabIndex, value);
    }

    public ICommand ToggleAudioBrowserCommand   { get; private set; } = null!;
    public ICommand TogglePatternBrowserCommand { get; private set; } = null!;
    public ObservableCollection<MenuItemViewModel> AnsichtMenuItems { get; } = [];

    private void BuildAnsichtMenu()
    {
        AnsichtMenuItems.Add(new MenuItemViewModel(new MenuItemModel
        {
            Header = "Audio Browser",
            Command = ToggleAudioBrowserCommand,
            InputGestureText = "Ctrl+B"
        }));
        AnsichtMenuItems.Add(new MenuItemViewModel(new MenuItemModel
        {
            Header = "Patterns && Channels",
            Command = TogglePatternBrowserCommand,
            InputGestureText = "Ctrl+L"
        }));
    }

    // ── Properties ───────────────────────────────────────────────────────────

    public Track? SelectedTrack
    {
        get => _selectedTrack;
        set { if (SetField(ref _selectedTrack, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set { if (SetField(ref _isPlaying, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public double BPM
    {
        get => _bpm;
        set => SetField(ref _bpm, Math.Clamp(value, 1, 500));
    }

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set => SetField(ref _currentPosition, value);
    }

    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            if (SetField(ref _masterVolume, Math.Clamp(value, 0.0, 1.0)))
            { OnPropertyChanged(nameof(MasterVolumeDisplay)); UpdateAllTrackVolumes(); }
        }
    }

    public double MasterPan
    {
        get => _masterPan;
        set => SetField(ref _masterPan, Math.Clamp(value, -1.0, 1.0));
    }

    public string MasterVolumeDisplay => MasterVolume > 0 ? $"{20 * Math.Log10(MasterVolume):F1} dB" : "-∞ dB";

    public double MasterMeterLeft
    {
        get => _masterMeterLeft;
        private set => SetField(ref _masterMeterLeft, value);
    }

    public double MasterMeterRight
    {
        get => _masterMeterRight;
        private set => SetField(ref _masterMeterRight, value);
    }

    private void MasterMeterTimer_Tick(object? sender, EventArgs e)
    {
        var m    = MixEngine.MasterMeter;
        double l = m.PeakLeft; double r = m.PeakRight; m.ResetPeaks();
        const double rel = 0.75;
        MasterMeterLeft  = l >= _masterMeterLeft  ? l : _masterMeterLeft  * rel;
        MasterMeterRight = r >= _masterMeterRight ? r : _masterMeterRight * rel;
    }

    public ObservableCollection<MasterEffectSlot>          MasterEffectSlots { get; } = [];
    public ObservableCollection<MixerChannel> MixerChannels     { get; } = [];

    private MixerChannel? _selectedMixerChannel;
    public MixerChannel? SelectedMixerChannel
    {
        get => _selectedMixerChannel;
        set
        {
            if (_selectedMixerChannel != null) _selectedMixerChannel.IsSelected = false;
            if (SetField(ref _selectedMixerChannel, value) && value != null) value.IsSelected = true;
        }
    }

    public MasterEffectSlot? SelectedEffectSlot
    {
        get => _selectedEffectSlot;
        set => SetField(ref _selectedEffectSlot, value);
    }

    public (string Type, string Name, string Icon)[] AvailableEffects => EffectFactory.AvailableEffects;

    // ── Commands ─────────────────────────────────────────────────────────────
    public ICommand PlayAllCommand           { get; }
    public ICommand PauseAllCommand          { get; }
    public ICommand StopAllCommand           { get; }
    public ICommand AddTrackCommand          { get; }
    public ICommand RemoveTrackCommand       { get; }
    public ICommand AnalyzeCommand           { get; }
    public ICommand OpenSamplerCommand       { get; private set; } = null!;
    public ICommand AddMasterEffectCommand   { get; private set; } = null!;
    public ICommand RemoveMasterEffectCommand { get; private set; } = null!;
    public ICommand OpenMasterEffectCommand  { get; private set; } = null!;
    public ICommand ExportCommand            { get; private set; } = null!;
    public ICommand OpenOptionsCommand       { get; private set; } = null!;
    public ICommand NewProjectCommand        { get; private set; } = null!;
    public ICommand OpenProjectCommand       { get; private set; } = null!;
    public ICommand SaveProjectCommand       { get; private set; } = null!;
    public ICommand SaveProjectAsCommand     { get; private set; } = null!;

    // ── Metronome ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts or stops the metronome timer to match the current playback state
    /// and metronome toggle.  The metronome only runs when the Playlist is playing.
    /// </summary>
    private void UpdateMetronomeState()
    {
        if (GlobalState.IsMetronomeEnabled && IsPlaying)
            StartMetronome();
        else
            StopMetronome();
    }

    private void StartMetronome()
    {
        _metronomeBeat = 0;
        _metronomeTimer ??= new DispatcherTimer(DispatcherPriority.Send);
        UpdateMetronomeInterval();
        _metronomeTimer.Tick -= MetronomeTick;
        _metronomeTimer.Tick += MetronomeTick;
        if (!_metronomeTimer.IsEnabled)
            _metronomeTimer.Start();
    }

    private void StopMetronome()
    {
        if (_metronomeTimer != null)
        {
            _metronomeTimer.Stop();
            _metronomeTimer.Tick -= MetronomeTick;
        }
        _metronomeBeat = 0;
    }

    private void UpdateMetronomeInterval()
    {
        if (_metronomeTimer == null) return;
        // One beat = 60 / BPM seconds
        _metronomeTimer.Interval = TimeSpan.FromSeconds(60.0 / Math.Max(1, BPM));
    }

    private void MetronomeTick(object? sender, EventArgs e)
    {
        if (!IsPlaying || !GlobalState.IsMetronomeEnabled) { StopMetronome(); return; }

        // Beat 0 of each bar = accent (high pitch), others = normal click
        bool isAccent = (_metronomeBeat % 4) == 0;
        PlayMetronomeClick(isAccent);
        _metronomeBeat++;
    }

    /// <summary>
    /// Synthesises a short click sound and plays it one-shot through the mix engine.
    /// Accent = 1 kHz, normal = 800 Hz, duration 20 ms.
    /// </summary>
    private void PlayMetronomeClick(bool accent)
    {
        Task.Run(() =>
        {
            try
            {
                const int sampleRate  = 44100;
                const int channels    = 2;
                const int durationMs  = 20;
                int       totalFrames = sampleRate * durationMs / 1000;
                double    freq        = accent ? 1000.0 : 800.0;
                float     volume      = accent ? 0.6f   : 0.4f;

                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
                var buffer     = new float[totalFrames * channels];

                for (int i = 0; i < totalFrames; i++)
                {
                    // Simple sine with quick exponential decay
                    float sample = (float)(Math.Sin(2 * Math.PI * freq * i / sampleRate)
                                           * volume
                                           * Math.Exp(-i / (sampleRate * 0.008)));
                    buffer[i * channels]     = sample; // L
                    buffer[i * channels + 1] = sample; // R
                }

                var provider = new BufferedWaveProvider(waveFormat) { DiscardOnBufferOverflow = true };
                var bytes    = new byte[buffer.Length * sizeof(float)];
                System.Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
                provider.AddSamples(bytes, 0, bytes.Length);

                // Wrap in an ISampleProvider and add to the engine
                var sampleProvider = provider.ToSampleProvider();
                MixEngine.Play();
                MixEngine.AddInput(sampleProvider);

                // Remove after the click duration + small margin
                Task.Delay(durationMs + 10).ContinueWith(_ =>
                {
                    try { MixEngine.RemoveInput(sampleProvider); } catch { }
                });
            }
            catch { /* best-effort */ }
        });
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the Playlist (arrangement tracks).
    /// Does NOT start the Channel Rack sequencer — they are independent.
    /// </summary>
    private void PlayAll()
    {
        var hasSolo   = Tracks.Any(t => t.IsSolo);
        var playable  = Tracks.Where(t => !string.IsNullOrEmpty(t.FilePath)).ToList();
        var startBeat = ArrangementVm.PlayheadBeat;
        var startTime = TimeSpan.FromSeconds(startBeat * 60.0 / BPM);

        foreach (var track in playable)
        {
            track.TargetMixFormat = MixEngine.MixFormat;
            track.EnsureLoaded();
            track.SetPosition(startTime);
            track.UpdatePlayerVolume(MasterVolume, hasSolo);
        }

        RebuildRoutingGraph();

        // ── ORDER MATTERS ─────────────────────────────────────────────
        // The pattern engine must start AFTER RebuildRoutingGraph (which calls
        // RemoveAllInputs and would otherwise unplug the sequencer provider)
        // and BEFORE the tracks begin playing, so the provider's frame 0 is
        // wired into the mixer for the next buffer the device consumes.
        // All pattern timing happens sample-accurately inside
        // PatternSequencerProvider — no triggers, no race, no cut first hit.
        _patternEngine?.Stop();
        _patternEngine = new PlaylistPatternEngine(ArrangementVm, PatternVm, MixEngine, BPM, startBeat);
        _patternEngine.Start();

        MixEngine.Play();   // no-op with the always-on device; kept for clarity
        foreach (var track in playable) track.Play();

        IsPlaying       = true;
        CurrentPosition = startTime;
        StatusMessage   = $"▶ Playlist spielt {playable.Count} Track(s)";

        // Start metronome if enabled
        UpdateMetronomeState();

        // NOTE: PatternVm.PlayCommand is NOT called here intentionally.
        // The Channel Rack runs independently with its own play/stop buttons.
    }

    private void PauseAll()
    {
        MixEngine.Pause();
        foreach (var track in Tracks) track.Pause();
        IsPlaying     = false;
        StatusMessage = "⏸ Pausiert";
        StopMetronome();
        _patternEngine?.Stop();
    }

    private void StopAll()
    {
        MixEngine.Stop();           // device stays hot (always-on design)
        MixEngine.StopAllVoices();  // silence ringing pattern voices immediately
        MixEngine.RemoveAllInputs();

        // Channel-Rack buses live permanently in the mixer — re-register them
        // after the sweep and keep their meters wired, otherwise the rack
        // would go silent (and its strip meters dead) after the first Stop.
        foreach (var rb in _rackBuses.Values) MixEngine.AddInput(rb.Wrapped);

        foreach (var ch in MixerChannels)
        {
            if (_rackBuses.Values.Any(rb => rb.Strip == ch)) continue;   // keep rack meters
            ch.SourceTrack?.SetBusMeter(null);
        }
        _busFaders.Clear();
        _busPanners.Clear();
        foreach (var track in Tracks) track.Stop();
        CurrentPosition = TimeSpan.Zero;
        IsPlaying       = false;
        StatusMessage   = "⏹ Gestoppt";
        StopMetronome();
        _patternEngine?.Stop();
        _patternEngine = null;
    }


    // ── Routing graph ─────────────────────────────────────────────────────────

    private void RebuildRoutingGraph()
    {
        bool hasSolo = Tracks.Any(t => t.IsSolo);
        foreach (var ch in MixerChannels)
        {
            if (_rackBuses.Values.Any(rb => rb.Strip == ch)) continue;   // keep rack meters
            ch.SourceTrack?.SetBusMeter(null);
        }
        _busFaders.Clear();
        _busPanners.Clear();
        MixEngine.RemoveAllInputs();

        // Re-register the permanent Channel-Rack buses after the sweep.
        foreach (var rb in _rackBuses.Values) MixEngine.AddInput(rb.Wrapped);

        var allTargetNums = MixerChannels.SelectMany(c => c.SendTargets).ToHashSet();
        var subMixers     = new Dictionary<int, NAudio.Wave.SampleProviders.MixingSampleProvider>();
        foreach (var t in allTargetNums)
            subMixers[t] = new NAudio.Wave.SampleProviders.MixingSampleProvider(MixEngine.MixFormat) { ReadFully = true };

        foreach (var channel in MixerChannels)
        {
            var track = channel.SourceTrack;
            if (track?.Output == null) continue;
            var adapted    = MixEngine.AdaptFormat(track.Output);
            var targetNums = channel.SendTargets.ToList();
            if (targetNums.Count > 0)
            {
                var consumers = DAW.Audio.BroadcastSampleProvider.Split(adapted, targetNums.Count);
                for (int i = 0; i < targetNums.Count; i++)
                    if (subMixers.TryGetValue(targetNums[i], out var sm))
                        sm.AddMixerInput(consumers[i]);
            }
            else
            {
                ISampleProvider toMaster = adapted;
                if (Math.Abs(channel.Pan) > 0.001)
                {
                    var pan = new VolumePanSampleProvider(adapted) { Pan = (float)channel.Pan };
                    toMaster = pan;
                }
                MixEngine.AddInput(toMaster);
            }
        }

        // PDC (no-op when all latencies are 0)
        foreach (var (channelNum, subMixer) in subMixers)
        {
            var senderLats = MixerChannels
                .Where(c => c.SendTargets.Contains(channelNum) && c.SourceTrack?.Output != null)
                .Select(c => (ch: c, lat: c.SourceTrack!.EffectChain.TotalLatencySamples))
                .ToList();
            int maxLat = senderLats.Count > 0 ? senderLats.Max(s => s.lat) : 0;
            if (maxLat == 0) continue;
            foreach (var (senderCh, lat) in senderLats)
            {
                int comp = maxLat - lat; if (comp <= 0) continue;
                var ad   = MixEngine.AdaptFormat(senderCh.SourceTrack!.Output);
                subMixer.AddMixerInput(new DelayLineProvider(ad, comp));
            }
        }

        foreach (var (channelNum, subMixer) in subMixers)
        {
            var targetCh  = MixerChannels.FirstOrDefault(c => c.ChannelNumber == channelNum);
            var targetTrk = targetCh?.SourceTrack;
            bool muted    = targetTrk != null && (targetTrk.IsMuted || !targetTrk.IsEnabled || (hasSolo && !targetTrk.IsSolo));
            float busVol  = muted ? 0f : (float)(targetTrk?.Volume ?? 1.0);
            float busPan  = (float)(targetCh?.Pan ?? 0.0);
            var busPanner = new VolumePanSampleProvider(subMixer) { Volume = busVol, Pan = busPan };
            if (targetTrk != null)
            {
                _busFaders[targetTrk]  = new NAudio.Wave.SampleProviders.VolumeSampleProvider(subMixer) { Volume = busVol };
                _busPanners[targetTrk] = busPanner;
            }
            var busMeter = new MeteringSampleProvider(busPanner);
            targetTrk?.SetBusMeter(busMeter);
            if (targetCh?.SendTargets.Count > 0)
            {
                foreach (var nextNum in targetCh.SendTargets)
                {
                    if (subMixers.TryGetValue(nextNum, out var nextSm)) nextSm.AddMixerInput(busMeter);
                    else MixEngine.AddInput(busMeter);
                }
            }
            else MixEngine.AddInput(busMeter);
        }
    }

    private void UpdateBusFaderForTrack(Track track)
    {
        var hasSolo = Tracks.Any(t => t.IsSolo);
        bool muted  = track.IsMuted || !track.IsEnabled || (hasSolo && !track.IsSolo);
        float vol   = muted ? 0f : (float)track.Volume;
        if (_busFaders.TryGetValue(track,  out var fader))  fader.Volume = vol;
        if (_busPanners.TryGetValue(track, out var panner)) { panner.Volume = vol; var mc = MixerChannels.FirstOrDefault(c => c.SourceTrack == track); if (mc != null) panner.Pan = (float)mc.Pan; }
    }

    private void UpdateBusPanForChannel(MixerChannel channel)
    {
        if (channel.SourceTrack == null) return;
        if (_busPanners.TryGetValue(channel.SourceTrack, out var panner)) panner.Pan = (float)channel.Pan;
    }

    private void SubscribeToMixerChannelRouting()
    {
        foreach (var ch in MixerChannels) SubscribeChannelRouting(ch);
        MixerChannels.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null) foreach (MixerChannel ch in e.NewItems) SubscribeChannelRouting(ch);
        };
    }

    private void SubscribeChannelRouting(MixerChannel channel)
    {
        channel.SendTargets.CollectionChanged += (_, _) =>
        {
            if (IsPlaying) System.Windows.Application.Current?.Dispatcher.InvokeAsync(RebuildRoutingGraph);
        };
        channel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MixerChannel.Pan)) UpdateBusPanForChannel(channel); };
    }

    // ── Mixer channel management ──────────────────────────────────────────────

    public void AddEmptyMixerChannel()
    {
        int n = MixerChannels.Count > 0 ? MixerChannels.Max(c => c.ChannelNumber) + 1 : 1;
        MixerChannels.Add(new Models.Mixer.MixerChannel(n) { Color = TrackColors[(n - 1) % TrackColors.Length] });
        StatusMessage = $"✓ Mixer Channel {n} erstellt";
    }

    public void RemoveMixerChannel(MixerChannel channel)
    {
        // Rack strips have a backing SourceTrack (UI adapter) — they ARE
        // deletable; unplug their audio bus first, then fall through.
        var rackEntry = _rackBuses.FirstOrDefault(kv => kv.Value.Strip == channel);
        if (rackEntry.Key != null)
        {
            TearDownRackBus(rackEntry.Key);
        }
        else if (channel.SourceTrack != null)
        {
            StatusMessage = "✗ Kann Channel mit Track nicht löschen"; return;
        }

        foreach (var oc in MixerChannels) oc.SendTargets.Remove(channel.ChannelNumber);
        MixerChannels.Remove(channel);
        if (SelectedMixerChannel == channel) SelectedMixerChannel = null;
        StatusMessage = $"✓ Mixer Channel {channel.ChannelNumber} entfernt";
    }

    public void ToggleChannelRouting(MixerChannel source, MixerChannel target)
    {
        if (source == target) { StatusMessage = "✗ Kann Channel nicht zu sich selbst routen"; return; }
        if (!source.SendTargets.Contains(target.ChannelNumber) && WouldCreateCycle(source, target))
        { StatusMessage = $"✗ Loop verhindert: {target.Name} → … → {source.Name}"; return; }
        source.ToggleSend(target.ChannelNumber);
        StatusMessage = source.SendTargets.Contains(target.ChannelNumber) ? $"✓ {source.Name} → {target.Name}" : $"✗ {source.Name} ⊗ {target.Name}";
    }

    public bool WouldCreateCycle(MixerChannel source, MixerChannel target)
    {
        if (source == target) return true;
        var visited = new HashSet<int>(); var queue = new Queue<int>(); queue.Enqueue(target.ChannelNumber);
        while (queue.Count > 0) { int cur = queue.Dequeue(); if (!visited.Add(cur)) continue; if (cur == source.ChannelNumber) return true; var ch = MixerChannels.FirstOrDefault(c => c.ChannelNumber == cur); if (ch != null) foreach (var n in ch.SendTargets) if (!visited.Contains(n)) queue.Enqueue(n); }
        return false;
    }

    public void MoveMixerChannel(MixerChannel channel, int newIndex)
    {
        int old = MixerChannels.IndexOf(channel); if (old < 0 || old == newIndex) return;
        MixerChannels.Move(old, Math.Clamp(newIndex, 0, MixerChannels.Count - 1));
        StatusMessage = $"↔ {channel.Name} nach Position {newIndex + 1}";
    }

    // ── Piano Roll / navigation ───────────────────────────────────────────────

    private void OnNavigateToPianoRoll(object? sender, ViewModels.Sequencer.ChannelViewModel ch)
    {
        PianoRollVm.OpenChannel(ch, PatternVm.Channels);
        ActiveTabIndex = 3;
    }

    /// <summary>
    /// Plays the selected channel's sample pitch-shifted to match the pressed piano key.
    /// Semitone offset is calculated relative to middle C (MIDI 60).
    /// </summary>
    private void OnPianoKeyPreview(int pitch)
    {
        var ch = PianoRollVm.SelectedChannel;
        if (ch == null || string.IsNullOrEmpty(ch.Model.SamplePath)) return;

        int semitones = pitch - 60; // C5 = base pitch

        if (ch.PreloadedSample != null && _rackBuses.TryGetValue(ch.Model, out var entry))
            entry.Bus.Trigger(ch.PreloadedSample, 1.0f, semitones);   // through mixer strip
        else if (ch.PreloadedSample != null)
            MixEngine.PlayPreloadedAtPitch(ch.PreloadedSample, semitones);
        else
            MixEngine.PlayOneShot(ch.Model.SamplePath);
    }

    private void OnAddPatternToPlaylist(object? sender, PatternModel pattern)
    {
        StatusMessage = $"✓ '{pattern.Name}' zur Playlist hinzugefügt";
    }

    /// <summary>
    /// Plays a one-shot sample triggered by the step sequencer.
    /// Uses the pre-loaded in-memory buffer when available to avoid disk I/O latency.
    /// When a Piano Roll note covers the current step, the sample is pitch-shifted to
    /// match that note's MIDI pitch (relative to the base pitch C5 = MIDI 60).
    /// </summary>
    private void OnPatternStepTriggered(ViewModels.Sequencer.ChannelViewModel ch, float velocity, int? pitch)
    {
        // Preferred path: through the channel's mixer strip (volume/pan/mute + FX).
        if (ch.PreloadedSample != null && _rackBuses.TryGetValue(ch.Model, out var entry))
        {
            entry.Bus.Trigger(ch.PreloadedSample, velocity, pitch.HasValue ? pitch.Value - 60 : 0);
            return;
        }

        // Fallback: direct to master (no sample/strip yet).
        if (ch.PreloadedSample != null)
        {
            if (pitch.HasValue)
                MixEngine.PlayPreloadedAtPitch(ch.PreloadedSample, pitch.Value - 60, velocity);
            else
                MixEngine.PlayPreloaded(ch.PreloadedSample, velocity);
        }
        else
        {
            MixEngine.PlayOneShot(ch.Model.SamplePath, velocity);
        }
    }

    /// <summary>
    /// Injects the audio engine into a channel and starts background preloading.
    /// Re-preloads whenever SamplePath changes so the buffer is always current.
    /// Also creates the channel's mixer strip as soon as a sample is loaded.
    /// </summary>
    private void WireChannelAudio(ViewModels.Sequencer.ChannelViewModel ch)
    {
        ch.AudioEngine = MixEngine;
        if (!string.IsNullOrEmpty(ch.Model.SamplePath))
        {
            _ = ch.PreloadSampleAsync();
            EnsureRackMixerChannel(ch.Model);
        }

        ch.Model.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(ChannelModel.SamplePath))
            {
                await ch.PreloadSampleAsync();
                if (!string.IsNullOrEmpty(ch.Model.SamplePath))
                    EnsureRackMixerChannel(ch.Model);
            }
        };
    }

    /// <summary>
    /// Creates (once) a mixer strip + audio bus for a Channel-Rack channel:
    ///   rack triggers → bus (volume/pan/mute) → strip insert FX → meter → master.
    ///
    /// The strip is backed by a lightweight Track (SourceTrack) because the
    /// entire MixerChannelControl UI binds to it — name, color, fader, pan,
    /// mute, level meters and the FX-slot panel. Fader/pan/mute edits made in
    /// the mixer act live on the bus; the meters are fed by a
    /// MeteringSampleProvider sitting at the end of the bus chain.
    /// </summary>
    private void EnsureRackMixerChannel(ChannelModel model)
    {
        if (_rackBuses.ContainsKey(model)) return;

        int n = MixerChannels.Count > 0 ? MixerChannels.Max(c => c.ChannelNumber) + 1 : 1;

        // UI backing object — never added to the playlist Tracks collection;
        // with an empty FilePath it cannot produce audio of its own.
        var stripTrack = new Track
        {
            TrackNumber  = 0,
            Title        = model.Name,
            Artist       = "Channel Rack",
            ChannelColor = TrackColors[(n - 1) % TrackColors.Length],
            Volume       = 0.8,
            Pan          = 0.0,
            FilePath     = "",
        };

        var strip = new Models.Mixer.MixerChannel(n) { SourceTrack = stripTrack };

        var bus = new ChannelRackBusProvider(MixEngine.MixFormat)
        {
            Volume = (float)stripTrack.Volume,
            Pan    = (float)stripTrack.Pan,
            Muted  = stripTrack.IsMuted,
        };

        // Bus → strip insert FX (the FX panel edits stripTrack.EffectSlots,
        // which feed stripTrack.EffectChain) → meter → master mixer.
        var fx    = new EffectSampleProvider(bus, stripTrack.EffectChain);
        var meter = new MeteringSampleProvider(fx);
        MixEngine.AddInput(meter);

        // Level visualisation: the strip's meter bars read Track.MeterLeft/Right,
        // which a 20 fps timer fills from the injected bus meter.
        stripTrack.SetBusMeter(meter);
        stripTrack.Play();   // FilePath empty → only starts the meter timer

        // Mixer UI edits (fader/pan/mute on the strip's Track) act on the bus.
        stripTrack.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(Track.Volume):  bus.Volume = (float)stripTrack.Volume; break;
                case nameof(Track.Pan):     bus.Pan    = (float)stripTrack.Pan;    break;
                case nameof(Track.IsMuted): bus.Muted  = stripTrack.IsMuted;       break;
            }
        };

        // Strip name follows the rack channel name.
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChannelModel.Name))
            {
                stripTrack.Title = model.Name;
                strip.Name       = model.Name;
            }
        };

        MixerChannels.Add(strip);
        _rackBuses[model] = new RackBusEntry(strip, bus, stripTrack, meter);
        StatusMessage = $"✓ '{model.Name}' → Mixer Channel {n}";
    }

    /// <summary>Removes a rack bus from the audio graph (strip deletion / reset).</summary>
    private void TearDownRackBus(ChannelModel model)
    {
        if (!_rackBuses.Remove(model, out var entry)) return;
        entry.Bus.KillAll();
        MixEngine.RemoveInput(entry.Wrapped);
        entry.StripTrack.SetBusMeter(null);
        entry.StripTrack.Stop();
        entry.StripTrack.Dispose();
    }

    private void SyncPianoRollChannels()
    {
        var cur      = PianoRollVm.SelectedChannel;
        var valid    = cur != null && PatternVm.Channels.Contains(cur);
        PianoRollVm.OpenChannel(valid ? cur : null, PatternVm.Channels);
    }

    // ── Track management ──────────────────────────────────────────────────────

    public void AddEmptyTrack()
    {
        _trackCounter++;
        var track = new Track
        {
            TrackNumber  = _trackCounter,
            Title        = $"Track {_trackCounter}",
            Artist       = "Empty",
            ChannelColor = TrackColors[(_trackCounter - 1) % TrackColors.Length],
            Volume       = 0.8,
            Pan          = 0.0,
            FilePath     = ""
        };
        Tracks.Add(track);
        SelectedTrack = track;
    }

    private void AddTrack()
    {
        var dlg = new OpenFileDialog { Filter = "Audio Dateien|*.mp3;*.wav;*.wma;*.m4a;*.flac;*.ogg|Alle Dateien|*.*", Multiselect = true, Title = "Audio-Dateien hinzufügen" };
        if (dlg.ShowDialog() == true) AddFilesAsTrack(dlg.FileNames);
    }

    public void AddFilesAsTrack(string[] filePaths)
    {
        foreach (var p in filePaths)
        {
            var track = CreateTrackFromFile(p);
            if (track != null)
            {
                var local = CopyToProjectSoundsIfPossible(p);
                if (local != null && local != p) track.FilePath = local;
                Tracks.Add(track);
            }
        }
        StatusMessage = $"✓ {filePaths.Length} Track(s) hinzugefügt";
    }

    private Track? CreateTrackFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        if (Path.GetExtension(filePath).ToLowerInvariant() is not (".mp3" or ".wav" or ".wma" or ".m4a" or ".flac" or ".ogg")) return null;
        _trackCounter++;
        return new Track { TrackNumber = _trackCounter, FilePath = filePath, Title = Path.GetFileNameWithoutExtension(filePath), Artist = "Unbekannt", ChannelColor = TrackColors[(_trackCounter - 1) % TrackColors.Length] };
    }

    private string? CopyToProjectSoundsIfPossible(string src)
    {
        if (string.IsNullOrEmpty(EnhancedProjectService.CurrentProjectPath) || !File.Exists(src)) return null;
        var dir    = Path.GetDirectoryName(EnhancedProjectService.CurrentProjectPath)!;
        var sounds = Path.Combine(dir, "Sounds");
        var full   = Path.GetFullPath(src); var fullS = Path.GetFullPath(sounds) + Path.DirectorySeparatorChar;
        if (full.StartsWith(fullS, StringComparison.OrdinalIgnoreCase)) return src;
        Directory.CreateDirectory(sounds);
        var name = Path.GetFileName(src); var dest = Path.Combine(sounds, name);
        if (File.Exists(dest) && !string.Equals(full, Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        { var n = Path.GetFileNameWithoutExtension(name); var ext = Path.GetExtension(name); int c = 1; do { dest = Path.Combine(sounds, $"{n}_{c++}{ext}"); } while (File.Exists(dest)); }
        try { if (!File.Exists(dest)) File.Copy(src, dest); StatusMessage = $"✓ Sound kopiert: {Path.GetFileName(dest)}"; return dest; }
        catch { return src; }
    }

    private void CreateMixerChannelForTrack(Track track)
    {
        int n = MixerChannels.Count > 0 ? MixerChannels.Max(c => c.ChannelNumber) + 1 : 1;
        MixerChannels.Add(new Models.Mixer.MixerChannel(n) { SourceTrack = track, Name = track.Title, Color = track.ChannelColor, Volume = track.Volume, Pan = track.Pan });
    }

    private void RemoveTrack()
    {
        if (SelectedTrack is null) return;
        if (SelectedTrack.Output is not null) MixEngine.RemoveInput(SelectedTrack.Output);
        SelectedTrack.Stop(); SelectedTrack.Dispose();
        var toRemove = SelectedTrack;
        var mixCh    = MixerChannels.FirstOrDefault(mc => mc.SourceTrack == toRemove);
        if (mixCh != null) { foreach (var oc in MixerChannels) oc.SendTargets.Remove(mixCh.ChannelNumber); MixerChannels.Remove(mixCh); }
        SelectedTrack = null; Tracks.Remove(toRemove);
        for (int i = 0; i < Tracks.Count; i++) Tracks[i].TrackNumber = i + 1;
        StatusMessage = "Track entfernt";
    }

    private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Track t) return;
        if (e.PropertyName is nameof(Track.Volume) or nameof(Track.IsMuted) or nameof(Track.IsSolo))
        { UpdateAllTrackVolumes(); UpdateBusFaderForTrack(t); }
    }

    private void UpdateAllTrackVolumes()
    {
        bool hasSolo = Tracks.Any(t => t.IsSolo);
        foreach (var t in Tracks) t.UpdatePlayerVolume(MasterVolume, hasSolo);
    }

    // ── Effects ───────────────────────────────────────────────────────────────

    private void AddMasterEffect(MasterEffectSlot? slot) { if (slot == null) return; SelectedEffectSlot = slot; }

    public void SetEffectType(MasterEffectSlot slot, string effectType)
    {
        var effect = EffectFactory.Create(effectType);
        if (effect != null) { slot.Effect = effect; slot.IsExpanded = true; StatusMessage = $"✓ {effect.Name} zu Slot {slot.SlotNumber}"; }
    }

    private void RemoveMasterEffect(MasterEffectSlot? slot)
    {
        if (slot?.Effect == null) return;
        var n = slot.Effect.Name; slot.Effect = null; slot.IsExpanded = false; StatusMessage = $"✓ {n} entfernt";
    }

    private void OpenMasterEffect(MasterEffectSlot? slot) { if (slot?.Effect != null) slot.IsExpanded = !slot.IsExpanded; }

    // ── Windows ───────────────────────────────────────────────────────────────

    public void OpenSampler(Track? track = null)
    {
        var t = track ?? SelectedTrack; if (t == null) return;
        Views.SamplerWindow.ShowForTrack(t, System.Windows.Application.Current?.MainWindow);
    }

    private void OpenExportWindow()
    {
        if (IsPlaying) StopAll();
        var dlg = new SaveFileDialog { Title = "Audio exportieren", Filter = "WAV Audio|*.wav|MP3 Audio|*.mp3|FLAC Audio|*.flac|All Files|*.*", DefaultExt = ".wav", FileName = "export.wav" };
        if (dlg.ShowDialog() != true) return;
        new MVVM.Views.ExportWindow(Tracks.ToList().AsReadOnly(), MixEngine.MasterEffectChain, MasterVolume, dlg.FileName)
            { Owner = System.Windows.Application.Current?.MainWindow, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner }.ShowDialog();
    }

    private void OpenOptionsWindow()
    {
        var win = new MVVM.Views.OptionsWindow(this)
            { Owner = System.Windows.Application.Current?.MainWindow, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner };
        win.ShowDialog();
        // Refresh the audio browser default path in case the user changed it
        AudioBrowserVm.ReloadDefaultPath();
    }

    // ── Analyze ───────────────────────────────────────────────────────────────

    private async void Analyze()
    {
        if (SelectedTrack is null) return;
        StatusMessage = $"🔬 Analysiere: {SelectedTrack.Title}...";
        await Task.Delay(1500);
        SelectedTrack.IsAnalyzed     = true;
        SelectedTrack.AnalysisResult = $"BPM: {Random.Shared.Next(80, 180)} | Key: {GetRandomKey()} | Genre: Electronic";
        StatusMessage = $"✓ Analyse: {SelectedTrack.Title}";
    }

    private static string GetRandomKey()
    {
        string[] keys = ["C","C#","D","D#","E","F","F#","G","G#","A","A#","B"]; string[] modes = ["Dur","Moll"];
        return $"{keys[Random.Shared.Next(keys.Length)]}-{modes[Random.Shared.Next(modes.Length)]}";
    }

    // ── Project ───────────────────────────────────────────────────────────────

    private async Task CreateNewProjectAsync()
    {
        try
        {
            ResetWorkspace();
            var p = await EnhancedProjectService.CreateNewProjectAsync();
            StatusMessage = $"✓ Neues Projekt: {p.ProjectName}";
        }
        catch (Exception ex) { StatusMessage = $"✗ Fehler: {ex.Message}"; }
    }

    /// <summary>
    /// Returns the entire workspace to a factory-fresh state, exactly as if
    /// the application had just been started:
    ///   • Transport stopped (tracks, pattern engine, metronome, voices)
    ///   • All playlist tracks disposed and removed (incl. arrangement clips)
    ///   • All mixer channels removed (insert effects + send routing go with them)
    ///   • All master effect slots emptied; master volume/pan to defaults
    ///   • BPM, playhead, position reset
    ///   • Channel Rack: ALL patterns deleted and replaced by the single
    ///     factory default pattern ("Beat 1"), Piano-Roll notes included —
    ///     they live on the pattern channels and die with them.
    /// Called by "Datei → Neues Projekt".
    /// </summary>
    public void ResetWorkspace()
    {
        // 1. Stop everything that produces sound.
        StopAll();
        PatternVm.SyncStop();   // Channel Rack sequencer, if running

        // 2. Playlist tracks: dispose audio readers, then clear.
        //    ArrangementViewModel handles the Reset event and clears its
        //    lanes (and thereby every clip) along with this.
        SelectedTrack = null;
        foreach (var track in Tracks.ToList())
        {
            track.Stop();
            track.Dispose();
        }
        Tracks.Clear();
        _trackCounter = 0;

        // 3. Mixer: drop every channel — their insert effects live on the
        //    disposed tracks, the send routing dies with the channel objects.
        SelectedMixerChannel = null;
        foreach (var model in _rackBuses.Keys.ToList())
            TearDownRackBus(model);   // unplug rack buses from the audio graph
        MixerChannels.Clear();

        // 4. Master section: empty all effect slots (the slot setter detaches
        //    the effect from the master chain) and restore defaults.
        foreach (var slot in MasterEffectSlots)
        {
            slot.Effect     = null;
            slot.IsExpanded = false;
        }
        SelectedEffectSlot = null;
        MasterVolume       = 0.8;
        MasterPan          = 0.0;

        // 5. Transport defaults.
        BPM             = 140.0;
        CurrentPosition = TimeSpan.Zero;
        ArrangementVm.PlayheadBeat = 0;
        ArrangementVm.SelectedClip = null;

        // 6. Channel Rack: wipe ALL patterns (incl. their channels, steps and
        //    Piano-Roll notes) and recreate the factory default — identical
        //    to the state right after application start.
        PatternVm.Reset();
    }

    private async Task OpenProjectAsync()
    {
        try
        {
            var dlg = new OpenFileDialog { Title = "Projekt öffnen", Filter = "Dragon Projekt (*.dragon)|*.dragon|Alle Dateien (*.*)|*.*", DefaultExt = ".dragon" };
            if (!Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DAW Projects")))
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            else dlg.InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DAW Projects");
            if (dlg.ShowDialog() != true) return;
            var project   = await EnhancedProjectService.OpenProjectAsync(dlg.FileName);
            var soundsDir = Path.Combine(Path.GetDirectoryName(dlg.FileName)!, "Sounds");
            foreach (var t in project.Tracks) { t.FilePath = ResolveAudioPath(t.FilePath, soundsDir); foreach (var c in t.Clips) if (!string.IsNullOrEmpty(c.SourceFilePath)) c.SourceFilePath = ResolveAudioPath(c.SourceFilePath, soundsDir); }
            await EnhancedProjectService.ImportProjectState(project, this);
            StatusMessage = $"✓ Projekt geöffnet: {project.ProjectName}";
        }
        catch (Exception ex) { StatusMessage = $"✗ Fehler: {ex.Message}"; }
    }

    private static string ResolveAudioPath(string fp, string soundsDir)
    {
        if (string.IsNullOrEmpty(fp)) return fp;
        if (File.Exists(fp)) return fp;
        var local = Path.Combine(soundsDir, Path.GetFileName(fp));
        return File.Exists(local) ? local : fp;
    }

    private async Task SaveProjectAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(EnhancedProjectService.CurrentProjectPath)) { await SaveProjectAsAsync(); return; }
            var project    = EnhancedProjectService.ExportCurrentState(this);
            var dragonPath = EnhancedProjectService.CurrentProjectPath;
            var soundsDir  = Path.Combine(Path.GetDirectoryName(dragonPath)!, "Sounds");
            Directory.CreateDirectory(soundsDir);
            CopyAudioFilesToSoundsFolder(project, soundsDir);
            UpdateLiveTrackPathsToSounds(soundsDir);
            MakePathsRelative(project);
            project.FilePath = dragonPath; EnhancedProjectService.CurrentProject = project;
            await File.WriteAllTextAsync(dragonPath, System.Text.Json.JsonSerializer.Serialize(project, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            StatusMessage = $"✓ Gespeichert: {project.ProjectName}";
        }
        catch (Exception ex) { StatusMessage = $"✗ Fehler: {ex.Message}"; }
    }

    private async Task SaveProjectAsAsync()
    {
        try
        {
            var defDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DAW Projects");
            if (!Directory.Exists(defDir)) Directory.CreateDirectory(defDir);
            var folder = FolderPicker.ShowDialog("Ordner für das Projekt wählen", defDir);
            if (string.IsNullOrEmpty(folder)) return;
            var safeName = SanitizeFileName(Path.GetFileName(folder)); if (string.IsNullOrWhiteSpace(safeName)) safeName = "Projekt";
            var soundsDir  = Path.Combine(folder, "Sounds"); var dragonPath = Path.Combine(folder, $"{safeName}.dragon");
            Directory.CreateDirectory(folder); Directory.CreateDirectory(soundsDir);
            var project = EnhancedProjectService.ExportCurrentState(this); project.ProjectName = safeName;
            CopyAudioFilesToSoundsFolder(project, soundsDir); UpdateLiveTrackPathsToSounds(soundsDir); MakePathsRelative(project);
            project.FilePath = dragonPath; EnhancedProjectService.CurrentProject = project; EnhancedProjectService.CurrentProjectPath = dragonPath;
            await File.WriteAllTextAsync(dragonPath, System.Text.Json.JsonSerializer.Serialize(project, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            StatusMessage = $"✓ Gespeichert: {safeName}";
        }
        catch (Exception ex) { StatusMessage = $"✗ Fehler: {ex.Message}"; }
    }

    private static void CopyAudioFilesToSoundsFolder(DawProject project, string soundsDir)
    {
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in project.Tracks) { CopySingleFile(t.FilePath, soundsDir, copied); foreach (var c in t.Clips) CopySingleFile(c.SourceFilePath, soundsDir, copied); }
    }

    private void UpdateLiveTrackPathsToSounds(string soundsDir)
    {
        var fullS = Path.GetFullPath(soundsDir) + Path.DirectorySeparatorChar;
        foreach (var track in Tracks)
        {
            if (string.IsNullOrEmpty(track.FilePath)) continue;
            var local = Path.Combine(soundsDir, Path.GetFileName(track.FilePath));
            if (File.Exists(local) && !Path.GetFullPath(track.FilePath).StartsWith(fullS, StringComparison.OrdinalIgnoreCase)) track.FilePath = local;
            var arrTrack = ArrangementVm.Tracks.FirstOrDefault(t => t.Model == track); if (arrTrack == null) continue;
            foreach (var cv in arrTrack.Clips) { if (string.IsNullOrEmpty(cv.Model.SourceFilePath)) continue; var lc = Path.Combine(soundsDir, Path.GetFileName(cv.Model.SourceFilePath)); if (File.Exists(lc) && !Path.GetFullPath(cv.Model.SourceFilePath).StartsWith(fullS, StringComparison.OrdinalIgnoreCase)) cv.Model.SourceFilePath = lc; }
        }
    }

    private static void CopySingleFile(string? fp, string soundsDir, HashSet<string> copied)
    {
        if (string.IsNullOrEmpty(fp) || !File.Exists(fp)) return;
        var name = Path.GetFileName(fp); var fullS = Path.GetFullPath(soundsDir) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(fp).StartsWith(fullS, StringComparison.OrdinalIgnoreCase)) { copied.Add(name); return; }
        if (!copied.Add(name)) return;
        var dest = Path.Combine(soundsDir, name); if (!File.Exists(dest)) File.Copy(fp, dest);
    }

    private static void MakePathsRelative(DawProject project)
    {
        foreach (var t in project.Tracks) { if (!string.IsNullOrEmpty(t.FilePath)) t.FilePath = Path.GetFileName(t.FilePath); foreach (var c in t.Clips) if (!string.IsNullOrEmpty(c.SourceFilePath)) c.SourceFilePath = Path.GetFileName(c.SourceFilePath); }
        foreach (var f in project.Files) f.RelativePath = Path.GetFileName(f.OriginalPath);
    }

    private static string SanitizeFileName(string name)
    {
        var inv = Path.GetInvalidFileNameChars();
        var s   = new string(name.Where(c => !inv.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(s) ? "Projekt" : s.Trim();
    }

    private async void OnProjectLoaded(object? sender, ProjectLoadedEventArgs e)
    {
        StatusMessage = $"Projekt geladen: {e.Project.ProjectName}";
        OnPropertyChanged(nameof(CurrentPosition));
        PropertyChanged += (_, _) => EnhancedProjectService.MarkAsModified();
        CommandManager.InvalidateRequerySuggested();
        await Task.CompletedTask;
    }

    private void OnProjectSaved(object? sender, ProjectSavedEventArgs e)
        => StatusMessage = $"Projekt gespeichert: {Path.GetFileNameWithoutExtension(e.FilePath)}";

    private void OnUnsavedChangesChanged(object? sender, bool hasUnsaved)
    {
        StatusMessage = hasUnsaved ? "Ungespeicherte Änderungen" : "Alle Änderungen gespeichert";
        CommandManager.InvalidateRequerySuggested();
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(n); return true;
    }
}
