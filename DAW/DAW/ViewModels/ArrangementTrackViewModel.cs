using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using DAW.Commands;
using DAW.Models;
using DAW.Services;

namespace DAW.ViewModels;

/// <summary>
/// ViewModel for a single track row in the arrangement view.
/// Wraps the existing <see cref="Track"/> model and owns its clip collection.
/// </summary>
public sealed class ArrangementTrackViewModel : INotifyPropertyChanged
{
    private readonly Track _track;
    private readonly ArrangementViewModel _arrangement;
    private bool _isActive;

    public ArrangementTrackViewModel(Track track, ArrangementViewModel arrangement)
    {
        _track = track;
        _arrangement = arrangement;
        _track.PropertyChanged += OnTrackPropertyChanged;

        Clips = [];

        ToggleMuteCommand = new RelayCommand(() =>
        {
            _track.IsMuted = !_track.IsMuted;
            OnPropertyChanged(nameof(IsMuted));
        });

        ToggleSoloCommand = new RelayCommand(() =>
        {
            _track.IsSolo = !_track.IsSolo;
            OnPropertyChanged(nameof(IsSolo));
        });

        DeleteTrackCommand = new RelayCommand(() =>
        {
            _arrangement.RemoveTrack(this);
        });
    }

    public Track Model => _track;
    public ObservableCollection<ArrangementClipViewModel> Clips { get; }

    public string Name    => _track.Title;
    public bool   IsMuted => _track.IsMuted;
    public bool   IsSolo  => _track.IsSolo;
    public Color  Color   => _track.ChannelColor;

    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleSoloCommand { get; }
    public ICommand DeleteTrackCommand { get; }

    // ── Clip management ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new clip at the given beat position (already snapped by the caller).
    /// </summary>
    public void AddClipAtBeat(double beat)
    {
        var clip = new ArrangementClip
        {
            DisplayName    = _track.Title,
            StartBeat      = beat,
            LengthInBeats  = 4.0,
            Color          = _track.ChannelColor,
            SourceFilePath = _track.FilePath
        };
        Clips.Add(new ArrangementClipViewModel(clip, _arrangement));
    }

    /// <summary>
    /// Creates a new audio clip from a dragged file at the given beat position.
    /// Automatically analyzes the audio file to determine duration and waveform.
    /// Precisely calculates the beat length based on actual audio duration and current BPM.
    /// </summary>
    public async Task AddAudioClipAtBeatAsync(double beat, string audioFilePath, double bpm = 120.0)
    {
        var audioAnalysis = new AudioAnalysisService();
        
        try
        {
            var analysisResult = await audioAnalysis.AnalyzeAudioFileAsync(audioFilePath);
            
            // Precise conversion: Duration in seconds -> beats
            // Formula: beats = (duration_seconds * BPM) / 60
            // This ensures the clip length exactly matches the audio length
            var durationInSeconds = analysisResult.Duration.TotalSeconds;
            var lengthInBeats = (durationInSeconds * bpm) / 60.0;
            
            // Minimum length is 1/16 note (0.25 beats in 4/4 time)
            lengthInBeats = Math.Max(0.25, lengthInBeats);
            
            var clip = new ArrangementClip
            {
                DisplayName = System.IO.Path.GetFileNameWithoutExtension(audioFilePath),
                StartBeat = beat,
                LengthInBeats = lengthInBeats,
                Color = GenerateColorFromFileName(audioFilePath),
                SourceFilePath = audioFilePath,
                WaveformData = analysisResult.WaveformData,
                AudioDuration = analysisResult.Duration
            };
            
            Clips.Add(new ArrangementClipViewModel(clip, _arrangement));
            
            System.Diagnostics.Debug.WriteLine($"Added audio clip: {analysisResult.FileName}");
            System.Diagnostics.Debug.WriteLine($"Duration: {durationInSeconds:F2}s -> {lengthInBeats:F2} beats at {bpm} BPM");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Audio analysis failed for {audioFilePath}: {ex.Message}");
            
            // Fallback: Create a clip with estimated length based on filename
            var estimatedBeats = EstimateBeatsFromFilename(audioFilePath, bpm);
            
            var fallbackClip = new ArrangementClip
            {
                DisplayName = System.IO.Path.GetFileNameWithoutExtension(audioFilePath),
                StartBeat = beat,
                LengthInBeats = estimatedBeats,
                Color = GenerateColorFromFileName(audioFilePath),
                SourceFilePath = audioFilePath
            };
            
            Clips.Add(new ArrangementClipViewModel(fallbackClip, _arrangement));
        }
    }

    /// <summary>
    /// Fallback method to estimate clip length from filename patterns.
    /// </summary>
    private static double EstimateBeatsFromFilename(string filePath, double bpm)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath).ToLower();
        
        // Look for time signatures or length hints in filename
        if (fileName.Contains("1bar") || fileName.Contains("1_bar")) return 4.0;
        if (fileName.Contains("2bar") || fileName.Contains("2_bar")) return 8.0;
        if (fileName.Contains("4bar") || fileName.Contains("4_bar")) return 16.0;
        if (fileName.Contains("8bar") || fileName.Contains("8_bar")) return 32.0;
        
        if (fileName.Contains("kick") || fileName.Contains("snare") || fileName.Contains("hihat")) return 0.25; // Short percussion
        if (fileName.Contains("loop") && bpm >= 140) return 16.0; // Typical EDM loop
        if (fileName.Contains("loop") && bpm < 100) return 32.0; // Slower loop
        if (fileName.Contains("vocal") || fileName.Contains("voice")) return 16.0; // Typical vocal phrase
        
        // Default: 4 beats (1 bar in 4/4 time)
        return 4.0;
    }

    /// <summary>
    /// Generates a color based on the filename for visual variety.
    /// </summary>
    private static Color GenerateColorFromFileName(string filePath)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        var hash = fileName.GetHashCode();
        var random = new Random(Math.Abs(hash));
        
        // Generate pleasing colors in the blue/green/purple range
        var hue = random.Next(180, 300); // Blue to purple
        var saturation = 0.7 + (random.NextDouble() * 0.3); // 70-100% saturation
        var value = 0.5 + (random.NextDouble() * 0.3); // 50-80% brightness
        
        return HsvToRgb(hue, saturation, value);
    }

    /// <summary>
    /// Converts HSV to RGB color.
    /// </summary>
    private static Color HsvToRgb(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        var m = v - c;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }

    /// <summary>Removes a clip from this track's lane.</summary>
    public void RemoveClip(ArrangementClipViewModel clipVm) => Clips.Remove(clipVm);

    /// <summary>Called by ArrangementViewModel after a zoom change.</summary>
    public void NotifyClipPixelsChanged()
    {
        foreach (var clip in Clips)
            clip.NotifyPixelsChanged();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Track.Title):        OnPropertyChanged(nameof(Name));     break;
            case nameof(Track.IsMuted):      OnPropertyChanged(nameof(IsMuted));  break;
            case nameof(Track.IsSolo):       OnPropertyChanged(nameof(IsSolo));   break;
            case nameof(Track.ChannelColor): OnPropertyChanged(nameof(Color));    break;
            case nameof(Track.IsPlaying):    IsActive = _track.IsPlaying;         break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
