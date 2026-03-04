using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using DAW.Commands;
using DAW.Models;
using DAW.Views;
using DAW.Services;
using DAW.Input;
using DAW.Audio.Effects;
using Microsoft.Win32;

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
    private bool _isPlaying;
    private string _statusMessage = "Bereit";
    private double _masterVolume = 0.8;
    private double _masterPan = 0.0;
    private double _bpm = 140.0;
    private TimeSpan _currentPosition = TimeSpan.Zero;
    private int _trackCounter;
    private MasterEffectSlot? _selectedEffectSlot;

    public MainViewModel()
    {
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
        
        // Initialize File Menu
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
        // Initialize commands
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
        
        // Initialize master effect slots (10 slots)
        for (int i = 1; i <= 10; i++)
        {
            MasterEffectSlots.Add(new MasterEffectSlot(i));
        }
        
        // Project commands
        NewProjectCommand = new AsyncRelayCommand(CreateNewProjectAsync);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, () => EnhancedProjectService.CurrentProject != null);
        SaveProjectAsCommand = new AsyncRelayCommand(SaveProjectAsAsync, () => EnhancedProjectService.CurrentProject != null);
        
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
                }
            }
            UpdateAllTrackVolumes();
        };

        // Must be created after Tracks is initialised
        ArrangementVm    = new ArrangementViewModel(this);
        AudioBrowserVm   = new AudioBrowserViewModel();
        AudioBrowserVm.FileRequestedForPlaylist += (_, path) => AddFilesAsTrack([path]);
        
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
    /// Global toolbar ViewModel.
    /// </summary>
    public GlobalToolbarViewModel GlobalToolbar { get; private set; }

    public ObservableCollection<Track> Tracks { get; } = [];

    /// <summary>
    /// File Menu ViewModel for dynamic menu generation
    /// </summary>
    public FileMenuViewModel FileMenuViewModel { get; private set; }
    
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

    // Master Effects
    public ObservableCollection<MasterEffectSlot> MasterEffectSlots { get; } = [];
    
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
    /// Plays all tracks simultaneously.
    /// </summary>
    private void PlayAll()
    {
        var hasSolo = Tracks.Any(t => t.IsSolo);
        
        foreach (var track in Tracks)
        {
            if (!string.IsNullOrEmpty(track.FilePath))
            {
                track.UpdatePlayerVolume(MasterVolume, hasSolo);
                track.Play();
            }
        }
        
        IsPlaying = true;
        StatusMessage = $"▶ Spielt {Tracks.Count(t => t.IsPlaying)} Track(s)";
    }

    /// <summary>
    /// Pauses all tracks.
    /// </summary>
    private void PauseAll()
    {
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
        foreach (var track in Tracks)
        {
            track.Stop();
        }
        
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
            }
        }
        
        StatusMessage = $"✓ {filePaths.Length} Track(s) hinzugefügt";
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

        SelectedTrack.Stop();
        SelectedTrack.Dispose();
        
        var trackToRemove = SelectedTrack;
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
                Filter = "DAW Projekt (*.dawproj)|*.dawproj|Alle Dateien (*.*)|*.*",
                DefaultExt = ".dawproj",
                InitialDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "DAW Projects")
            };
            
            // Ensure directory exists
            if (!Directory.Exists(openFileDialog.InitialDirectory))
            {
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            if (openFileDialog.ShowDialog() == true)
            {
                var project = await EnhancedProjectService.OpenProjectAsync(openFileDialog.FileName);
                await EnhancedProjectService.ImportProjectState(project, this);
                StatusMessage = $"✓ Projekt geöffnet: {project.ProjectName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Fehler beim Öffnen: {ex.Message}";
        }
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
            
            // Export current state to project
            var project = EnhancedProjectService.ExportCurrentState(this);
            project.FilePath = EnhancedProjectService.CurrentProjectPath;
            EnhancedProjectService.CurrentProject = project;
            
            // Save to existing path
            var jsonContent = System.Text.Json.JsonSerializer.Serialize(project, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(EnhancedProjectService.CurrentProjectPath, jsonContent);
            
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
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Projekt speichern unter",
                Filter = "DAW Projekt (*.dawproj)|*.dawproj",
                DefaultExt = ".dawproj",
                FileName = EnhancedProjectService.CurrentProject?.ProjectName ?? "Neues Projekt",
                InitialDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "DAW Projects")
            };
            
            // Ensure directory exists
            if (!Directory.Exists(saveFileDialog.InitialDirectory))
            {
                Directory.CreateDirectory(saveFileDialog.InitialDirectory);
            }

            if (saveFileDialog.ShowDialog() == true)
            {
                var project = EnhancedProjectService.ExportCurrentState(this);
                project.ProjectName = Path.GetFileNameWithoutExtension(saveFileDialog.FileName);
                project.FilePath = saveFileDialog.FileName;
                
                // Update service state
                EnhancedProjectService.CurrentProject = project;
                EnhancedProjectService.CurrentProjectPath = saveFileDialog.FileName;
                
                // Save to the selected path
                var jsonContent = System.Text.Json.JsonSerializer.Serialize(project, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                await File.WriteAllTextAsync(saveFileDialog.FileName, jsonContent);
                
                StatusMessage = $"✓ Projekt gespeichert: {project.ProjectName}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Fehler beim Speichern unter: {ex.Message}";
        }
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
