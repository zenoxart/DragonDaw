using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using DAW.Commands;
using DAW.MVVM.Models;
using DAW.Services;
using Microsoft.Win32;
using NAudio.Wave;

namespace DAW.MVVM.ViewModels;

/// <summary>
/// ViewModel for the Sampler/Clip Editor window.
/// Manages the ClipData model and provides commands for all editor operations.
/// Follows MVVM pattern with clear separation between real-time and offline operations.
/// </summary>
public class SamplerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Track _track;
    private readonly ClipData _clipData;
    private readonly OfflineProcessingService _offlineService;
    private readonly DispatcherTimer _playheadTimer;
    
    private bool _isProcessing;
    private string _statusMessage = "Bereit";
    private double _processingProgress;
    private TimeSpan _playheadPosition;
    private double _zoomLevel = 1.0;
    private double _scrollPosition;
    private bool _isPlaying;
    private bool _disposed;
    
    // Selection state for waveform editor
    private long _selectionStart;
    private long _selectionEnd;
    private bool _hasSelection;
    
    public SamplerViewModel(Track track)
    {
        _track = track;
        _clipData = new ClipData
        {
            SourceFilePath = track.FilePath,
            DisplayName = string.IsNullOrEmpty(track.Title) ? "Neuer Clip" : track.Title,
            OriginalDuration = track.Duration,
            // Sync initial values from track
            Volume = (float)track.Volume,
            Pan = (float)track.Pan,
            IsEnabled = track.IsEnabled,
            PitchSemitones = track.PitchSemitones,
            TimeStretchRatio = track.PlaybackSpeed,
            TimeStretchMode = track.PlaybackMode
        };
        
        _offlineService = OfflineProcessingService.Instance;
        _offlineService.JobProgress += OnJobProgress;
        _offlineService.JobCompleted += OnJobCompleted;
        _offlineService.JobFailed += OnJobFailed;
        
        // Subscribe to ClipData changes to update normalized properties
        _clipData.PropertyChanged += OnClipDataPropertyChanged;
        
        // Timer for playhead position updates
        _playheadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _playheadTimer.Tick += OnPlayheadTimerTick;
        
        // Subscribe to track playing state
        _track.PropertyChanged += OnTrackPropertyChanged;
        
        // Sync initial playing state
        _isPlaying = track.IsPlaying;
        if (_isPlaying)
            _playheadTimer.Start();
        
        InitializeCommands();
        
        if (!string.IsNullOrEmpty(track.FilePath))
        {
            LoadFileInfo();
            _ = GenerateWaveformAsync();
            StatusMessage = $"Geladen: {_clipData.DisplayName}";
        }
        else
        {
            StatusMessage = "Keine Audio-Datei - Datei laden um zu beginnen";
        }
    }
    
    private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Track.IsPlaying))
        {
            IsPlaying = _track.IsPlaying;
            if (_track.IsPlaying)
                _playheadTimer.Start();
            else
                _playheadTimer.Stop();
        }
    }
    
    private void OnPlayheadTimerTick(object? sender, EventArgs e)
    {
        // Update playhead position from track
        // This would need actual implementation in Track class
        OnPropertyChanged(nameof(PlayheadNormalized));
    }
    
    private void OnClipDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Forward loop changes to normalized properties
        if (e.PropertyName == nameof(ClipData.LoopStartSamples))
            OnPropertyChanged(nameof(LoopStartNormalized));
        else if (e.PropertyName == nameof(ClipData.LoopEndSamples))
            OnPropertyChanged(nameof(LoopEndNormalized));
        else if (e.PropertyName == nameof(ClipData.TotalSamples))
        {
            OnPropertyChanged(nameof(SelectionStartNormalized));
            OnPropertyChanged(nameof(SelectionEndNormalized));
            OnPropertyChanged(nameof(LoopStartNormalized));
            OnPropertyChanged(nameof(LoopEndNormalized));
        }
        // Sync volume/pan changes to track
        else if (e.PropertyName == nameof(ClipData.Volume))
        {
            _track.Volume = _clipData.Volume;
        }
        else if (e.PropertyName == nameof(ClipData.Pan))
        {
            _track.Pan = _clipData.Pan;
        }
        else if (e.PropertyName == nameof(ClipData.IsEnabled))
        {
            _track.IsEnabled = _clipData.IsEnabled;
        }
        else if (e.PropertyName == nameof(ClipData.PitchSemitones))
        {
            _track.PitchSemitones = _clipData.PitchSemitones;
        }
        else if (e.PropertyName == nameof(ClipData.TimeStretchRatio))
        {
            _track.PlaybackSpeed = _clipData.TimeStretchRatio;
        }
        else if (e.PropertyName == nameof(ClipData.TimeStretchMode))
        {
            _track.PlaybackMode = _clipData.TimeStretchMode;
        }
    }
    
    
    
    #region Properties
    
    /// <summary>The underlying track this sampler edits.</summary>
    public Track Track => _track;
    
    /// <summary>The clip data model.</summary>
    public ClipData ClipData => _clipData;
    
    /// <summary>Window title with track name.</summary>
    public string WindowTitle => $"Sampler - {_clipData.DisplayName}";
    
    /// <summary>Whether offline processing is running.</summary>
    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetField(ref _isProcessing, value);
    }
    
    /// <summary>Current status message.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }
    
    /// <summary>Progress of current operation (0-100).</summary>
    public double ProcessingProgress
    {
        get => _processingProgress;
        set => SetField(ref _processingProgress, value);
    }
    
    /// <summary>Current playhead position.</summary>
    public TimeSpan PlayheadPosition
    {
        get => _playheadPosition;
        set
        {
            if (SetField(ref _playheadPosition, value))
            {
                OnPropertyChanged(nameof(PlayheadDisplay));
            }
        }
    }
    
    /// <summary>Playhead position display string.</summary>
    public string PlayheadDisplay => PlayheadPosition.ToString(@"mm\:ss\.fff");
    
    /// <summary>Waveform zoom level (1.0 = fit to view).</summary>
    public double ZoomLevel
    {
        get => _zoomLevel;
        set => SetField(ref _zoomLevel, Math.Clamp(value, 0.1, 100.0));
    }
    
    /// <summary>Horizontal scroll position (0-1).</summary>
    public double ScrollPosition
    {
        get => _scrollPosition;
        set => SetField(ref _scrollPosition, Math.Clamp(value, 0.0, 1.0));
    }
    
    /// <summary>Whether audio is currently playing.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }
    
    /// <summary>Selection start in samples.</summary>
    public long SelectionStart
    {
        get => _selectionStart;
        set
        {
            if (SetField(ref _selectionStart, value))
            {
                UpdateSelectionState();
            }
        }
    }
    
    /// <summary>Selection end in samples.</summary>
    public long SelectionEnd
    {
        get => _selectionEnd;
        set
        {
            if (SetField(ref _selectionEnd, value))
            {
                UpdateSelectionState();
            }
        }
    }
    
    /// <summary>Whether there is an active selection.</summary>
    public bool HasSelection
    {
        get => _hasSelection;
        private set => SetField(ref _hasSelection, value);
    }
    
    /// <summary>Available time stretch modes.</summary>
    public IReadOnlyList<TimeStretchMode> TimeStretchModes { get; } = Enum.GetValues<TimeStretchMode>();
    
    /// <summary>Available stereo modes.</summary>
    public IReadOnlyList<StereoMode> StereoModes { get; } = Enum.GetValues<StereoMode>();
    
    /// <summary>Selection start as normalized value (0-1) for waveform binding.</summary>
    public double SelectionStartNormalized
    {
        get => _clipData.TotalSamples > 0 ? (double)_selectionStart / _clipData.TotalSamples : 0;
        set
        {
            SelectionStart = (long)(value * _clipData.TotalSamples);
            OnPropertyChanged();
        }
    }
    
    /// <summary>Selection end as normalized value (0-1) for waveform binding.</summary>
    public double SelectionEndNormalized
    {
        get => _clipData.TotalSamples > 0 ? (double)_selectionEnd / _clipData.TotalSamples : 0;
        set
        {
            SelectionEnd = (long)(value * _clipData.TotalSamples);
            OnPropertyChanged();
        }
    }
    
    /// <summary>Loop start as normalized value (0-1) for waveform binding.</summary>
    public double LoopStartNormalized => _clipData.TotalSamples > 0 
        ? (double)_clipData.LoopStartSamples / _clipData.TotalSamples : 0;
    
    /// <summary>Loop end as normalized value (0-1) for waveform binding.</summary>
    public double LoopEndNormalized => _clipData.TotalSamples > 0 
        ? (double)_clipData.LoopEndSamples / _clipData.TotalSamples : 1;
    
    /// <summary>Playhead position as normalized value (0-1).</summary>
    public double PlayheadNormalized => _clipData.OriginalDuration.TotalSeconds > 0
        ? _playheadPosition.TotalSeconds / _clipData.OriginalDuration.TotalSeconds : 0;
    
    #endregion
    
    #region Commands
    
    // File Commands
    public ICommand LoadFileCommand { get; private set; } = null!;
    public ICommand RemoveFileCommand { get; private set; } = null!;
    public ICommand ReloadFileCommand { get; private set; } = null!;
    
    // Playback Commands
    public ICommand PlayCommand { get; private set; } = null!;
    public ICommand StopCommand { get; private set; } = null!;
    public ICommand PlaySelectionCommand { get; private set; } = null!;
    
    // Offline Processing Commands
    public ICommand NormalizeCommand { get; private set; } = null!;
    public ICommand RemoveDcOffsetCommand { get; private set; } = null!;
    public ICommand ReverseCommand { get; private set; } = null!;
    public ICommand InvertPolarityCommand { get; private set; } = null!;
    public ICommand ConvertStereoCommand { get; private set; } = null!;
    public ICommand TrimSilenceCommand { get; private set; } = null!;
    
    // Waveform Editor Commands
    public ICommand ZoomInCommand { get; private set; } = null!;
    public ICommand ZoomOutCommand { get; private set; } = null!;
    public ICommand ZoomToFitCommand { get; private set; } = null!;
    public ICommand ZoomToSelectionCommand { get; private set; } = null!;
    public ICommand SelectAllCommand { get; private set; } = null!;
    public ICommand ClearSelectionCommand { get; private set; } = null!;
    
    // Loop Commands
    public ICommand SetLoopFromSelectionCommand { get; private set; } = null!;
    public ICommand ClearLoopCommand { get; private set; } = null!;
    
    // AI Analysis Commands (Future)
    public ICommand AnalyzeCommand { get; private set; } = null!;
    public ICommand DetectTransientsCommand { get; private set; } = null!;
    
    private void InitializeCommands()
    {
        // File
        LoadFileCommand = new RelayCommand(LoadFile);
        RemoveFileCommand = new RelayCommand(RemoveFile, () => !string.IsNullOrEmpty(_clipData.SourceFilePath));
        ReloadFileCommand = new RelayCommand(ReloadFile, () => !string.IsNullOrEmpty(_clipData.SourceFilePath));
        
        // Playback - always enabled, methods handle missing file
        PlayCommand = new RelayCommand(TogglePlay);
        StopCommand = new RelayCommand(Stop);
        PlaySelectionCommand = new RelayCommand(PlaySelection, () => HasSelection && _track.IsLoaded);
        
        // Offline Processing - require file and not processing
        bool HasFile() => !string.IsNullOrEmpty(_clipData.SourceFilePath) && !IsProcessing;
        NormalizeCommand = new RelayCommand(async () => await NormalizeAsync(), HasFile);
        RemoveDcOffsetCommand = new RelayCommand(async () => await RemoveDcOffsetAsync(), HasFile);
        ReverseCommand = new RelayCommand(async () => await ReverseAsync(), HasFile);
        InvertPolarityCommand = new RelayCommand(async () => await InvertPolarityAsync(), HasFile);
        ConvertStereoCommand = new RelayCommand<StereoMode>(async mode => await ConvertStereoAsync(mode), _ => HasFile());
        TrimSilenceCommand = new RelayCommand(async () => await TrimSilenceAsync(), HasFile);
        
        // Waveform Editor
        ZoomInCommand = new RelayCommand(() => ZoomLevel *= 1.5);
        ZoomOutCommand = new RelayCommand(() => ZoomLevel /= 1.5);
        ZoomToFitCommand = new RelayCommand(() => ZoomLevel = 1.0);
        ZoomToSelectionCommand = new RelayCommand(ZoomToSelection, () => HasSelection);
        SelectAllCommand = new RelayCommand(SelectAll, () => _clipData.TotalSamples > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => HasSelection);
        
        // Loop
        SetLoopFromSelectionCommand = new RelayCommand(SetLoopFromSelection, () => HasSelection);
        ClearLoopCommand = new RelayCommand(ClearLoop, () => _clipData.LoopEnabled);
        
        // AI Analysis
        AnalyzeCommand = new RelayCommand(async () => await AnalyzeAsync(), HasFile);
        DetectTransientsCommand = new RelayCommand(async () => await DetectTransientsAsync(), HasFile);
    }
    
    #endregion
    
    #region File Operations
    
    private void LoadFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio Dateien|*.wav;*.mp3;*.flac;*.ogg;*.m4a|Alle Dateien|*.*",
            Title = "Audio-Datei laden"
        };
        
        if (dialog.ShowDialog() == true)
        {
            _clipData.SourceFilePath = dialog.FileName;
            _clipData.DisplayName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            
            LoadFileInfo();
            _ = GenerateWaveformAsync();
            
            OnPropertyChanged(nameof(WindowTitle));
            StatusMessage = $"Geladen: {_clipData.DisplayName}";
        }
    }
    
    private void RemoveFile()
    {
        _clipData.SourceFilePath = string.Empty;
        _clipData.WaveformPeaks = null;
        StatusMessage = "Datei entfernt";
    }
    
    private void ReloadFile()
    {
        if (!string.IsNullOrEmpty(_clipData.SourceFilePath))
        {
            LoadFileInfo();
            _ = GenerateWaveformAsync();
            StatusMessage = "Datei neu geladen";
        }
    }
    
    private void LoadFileInfo()
    {
        if (string.IsNullOrEmpty(_clipData.SourceFilePath)) return;
        
        try
        {
            using var reader = new AudioFileReader(_clipData.SourceFilePath);
            _clipData.SampleRate = reader.WaveFormat.SampleRate;
            _clipData.Channels = reader.WaveFormat.Channels;
            _clipData.BitDepth = reader.WaveFormat.BitsPerSample;
            _clipData.TotalSamples = reader.Length / sizeof(float) / reader.WaveFormat.Channels;
            _clipData.OriginalDuration = reader.TotalTime;
            _clipData.LoopEndSamples = _clipData.TotalSamples;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler: {ex.Message}";
        }
    }
    
    #endregion
    
    #region Playback
    
    private void TogglePlay()
    {
        if (!_track.IsLoaded && string.IsNullOrEmpty(_clipData.SourceFilePath))
        {
            StatusMessage = "⚠ Keine Audio-Datei geladen";
            return;
        }
        
        if (IsPlaying)
        {
            _track.Pause();
            IsPlaying = false;
            StatusMessage = "⏸ Pausiert";
        }
        else
        {
            _track.Play();
            IsPlaying = true;
            StatusMessage = "▶ Wiedergabe";
        }
    }
    
    private void Stop()
    {
        _track.Stop();
        IsPlaying = false;
        PlayheadPosition = TimeSpan.Zero;
        StatusMessage = "⏹ Gestoppt";
    }
    
    private void PlaySelection()
    {
        if (!HasSelection)
        {
            StatusMessage = "⚠ Keine Auswahl vorhanden";
            return;
        }
        
        if (_clipData.SampleRate == 0)
        {
            StatusMessage = "⚠ Keine Audio-Datei geladen";
            return;
        }
        
        var startTime = TimeSpan.FromSeconds((double)SelectionStart / _clipData.SampleRate);
        _track.SetPosition(startTime);
        _track.Play();
        IsPlaying = true;
        StatusMessage = $"▶ Spiele Auswahl ab {FormatTime(SelectionStart)}";
    }
    
    #endregion
    
    #region Offline Processing
    
    private async Task NormalizeAsync()
    {
        IsProcessing = true;
        StatusMessage = "Normalisiere...";
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.Normalize,
            TargetClip = _clipData,
            Parameters = { ["TargetPeak"] = 1.0f }
        };
        
        await _offlineService.QueueJobAsync(job);
        IsProcessing = false;
    }
    
    private async Task RemoveDcOffsetAsync()
    {
        IsProcessing = true;
        StatusMessage = "Entferne DC Offset...";
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.RemoveDcOffset,
            TargetClip = _clipData
        };
        
        await _offlineService.QueueJobAsync(job);
        IsProcessing = false;
    }
    
    private async Task ReverseAsync()
    {
        IsProcessing = true;
        StatusMessage = "Reversiere Audio...";
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.Reverse,
            TargetClip = _clipData
        };
        
        await _offlineService.QueueJobAsync(job);
        IsProcessing = false;
    }
    
    private async Task InvertPolarityAsync()
    {
        IsProcessing = true;
        StatusMessage = "Invertiere Polarität...";
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.InvertPolarity,
            TargetClip = _clipData
        };
        
        await _offlineService.QueueJobAsync(job);
        IsProcessing = false;
    }
    
    private async Task ConvertStereoAsync(StereoMode mode)
    {
        IsProcessing = true;
        StatusMessage = $"Konvertiere zu {mode}...";
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.StereoConvert,
            TargetClip = _clipData,
            Parameters = { ["Mode"] = mode }
        };
        
        await _offlineService.QueueJobAsync(job);
        IsProcessing = false;
    }
    
    private async Task TrimSilenceAsync()
    {
        IsProcessing = true;
        StatusMessage = "Trimme Stille...";
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.TrimSilence,
            TargetClip = _clipData,
            Parameters = { ["Threshold"] = 0.001f }
        };
        
        await _offlineService.QueueJobAsync(job);
        IsProcessing = false;
    }
    
    private async Task GenerateWaveformAsync()
    {
        if (string.IsNullOrEmpty(_clipData.SourceFilePath)) return;
        
        IsProcessing = true;
        StatusMessage = "Generiere Waveform...";
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.GenerateWaveform,
            TargetClip = _clipData,
            Parameters = { ["PeakCount"] = 2000 }
        };
        
        await _offlineService.QueueJobAsync(job);
        IsProcessing = false;
    }
    
    private async Task AnalyzeAsync()
    {
        IsProcessing = true;
        StatusMessage = "AI Analyse läuft...";
        
        // Placeholder for future AI analysis
        await Task.Delay(1000);
        
        _clipData.DetectedBpm = 128;
        _clipData.DetectedKey = "A Minor";
        
        IsProcessing = false;
        StatusMessage = $"BPM: {_clipData.DetectedBpm}, Key: {_clipData.DetectedKey}";
    }
    
    private async Task DetectTransientsAsync()
    {
        IsProcessing = true;
        StatusMessage = "Erkenne Transienten...";
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.DetectTransients,
            TargetClip = _clipData
        };
        
        try
        {
            await _offlineService.QueueJobAsync(job);
        }
        catch
        {
            StatusMessage = "Transient-Erkennung noch nicht implementiert";
        }
        
        IsProcessing = false;
    }
    
    #endregion
    
    #region Waveform Editor
    
    private void ZoomToSelection()
    {
        if (!HasSelection) return;
        
        double selectionRatio = (double)(SelectionEnd - SelectionStart) / _clipData.TotalSamples;
        ZoomLevel = 1.0 / selectionRatio;
        ScrollPosition = (double)SelectionStart / _clipData.TotalSamples;
    }
    
    private void SelectAll()
    {
        SelectionStart = 0;
        SelectionEnd = _clipData.TotalSamples;
    }
    
    private void ClearSelection()
    {
        SelectionStart = 0;
        SelectionEnd = 0;
    }
    
    private void UpdateSelectionState()
    {
        HasSelection = SelectionEnd > SelectionStart;
        OnPropertyChanged(nameof(SelectionStart));
        OnPropertyChanged(nameof(SelectionEnd));
        OnPropertyChanged(nameof(SelectionStartNormalized));
        OnPropertyChanged(nameof(SelectionEndNormalized));
    }
    
    #endregion
    
    #region Loop Points
    
    private void SetLoopFromSelection()
    {
        if (!HasSelection) return;
        
        _clipData.LoopStartSamples = SelectionStart;
        _clipData.LoopEndSamples = SelectionEnd;
        _clipData.LoopEnabled = true;
        
        StatusMessage = $"Loop gesetzt: {FormatTime(SelectionStart)} - {FormatTime(SelectionEnd)}";
    }
    
    private void ClearLoop()
    {
        _clipData.LoopEnabled = false;
        _clipData.LoopStartSamples = 0;
        _clipData.LoopEndSamples = _clipData.TotalSamples;
        
        StatusMessage = "Loop entfernt";
    }
    
    private string FormatTime(long samples)
    {
        if (_clipData.SampleRate == 0) return "0:00";
        var time = TimeSpan.FromSeconds((double)samples / _clipData.SampleRate);
        return time.ToString(@"m\:ss\.fff");
    }
    
    #endregion
    
    #region Event Handlers
    
    private void OnJobProgress(object? sender, OfflineProcessingJob job)
    {
        if (job.TargetClip != _clipData) return;
        
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            ProcessingProgress = job.Progress;
            StatusMessage = job.StatusMessage;
        });
    }
    
    private void OnJobCompleted(object? sender, OfflineProcessingJob job)
    {
        if (job.TargetClip != _clipData) return;
        
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            IsProcessing = false;
            ProcessingProgress = 100;
            StatusMessage = $"✓ {job.ProcessType} abgeschlossen";
        });
    }
    
    private void OnJobFailed(object? sender, OfflineProcessingJob job)
    {
        if (job.TargetClip != _clipData) return;
        
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            IsProcessing = false;
            StatusMessage = $"✗ Fehler: {job.ErrorMessage}";
        });
    }
    
    #endregion
    
    #region IDisposable
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _playheadTimer.Stop();
        _offlineService.JobProgress -= OnJobProgress;
        _offlineService.JobCompleted -= OnJobCompleted;
        _offlineService.JobFailed -= OnJobFailed;
        _clipData.PropertyChanged -= OnClipDataPropertyChanged;
        _track.PropertyChanged -= OnTrackPropertyChanged;
        
        GC.SuppressFinalize(this);
    }
    
    #endregion
    
    #region INotifyPropertyChanged
    
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
    
    #endregion
}
