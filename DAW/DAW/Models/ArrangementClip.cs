using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DAW.Models;

/// <summary>
/// Represents a clip instance placed on the arrangement timeline.
/// A clip has an absolute start position (in beats) and a length (in beats).
/// Multiple clips can reference the same source file at different positions.
/// </summary>
public sealed class ArrangementClip : INotifyPropertyChanged
{
    private string _displayName = "Clip";
    private double _startBeat;
    private double _lengthInBeats = 4.0;
    private string? _sourceFilePath;
    private Color _color = Color.FromRgb(38, 97, 156);
    private bool _isSelected;
    private bool _isMuted;
    private double[]? _waveformData;
    private TimeSpan? _audioDuration;

    /// <summary>Unique identifier for this clip instance.</summary>
    public string Id { get; } = Guid.NewGuid().ToString();

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    /// <summary>
    /// Start position in beats (0-based). Beat 0 = bar 1, beat 1.
    /// Beat-to-pixel: px = StartBeat × PixelsPerBeat
    /// </summary>
    public double StartBeat
    {
        get => _startBeat;
        set => SetField(ref _startBeat, Math.Max(0.0, value));
    }

    /// <summary>
    /// Length in beats. Minimum 0.25 = one 1/16-note in 4/4 time.
    /// </summary>
    public double LengthInBeats
    {
        get => _lengthInBeats;
        set => SetField(ref _lengthInBeats, Math.Max(0.25, value));
    }

    public string? SourceFilePath
    {
        get => _sourceFilePath;
        set => SetField(ref _sourceFilePath, value);
    }

    public Color Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    /// <summary>
    /// Waveform data as peak values for visualization (0.0 to 1.0 range).
    /// Null if not an audio clip or not yet analyzed.
    /// </summary>
    public double[]? WaveformData
    {
        get => _waveformData;
        set => SetField(ref _waveformData, value);
    }

    /// <summary>
    /// Duration of the source audio file. Used to calculate appropriate clip length.
    /// </summary>
    public TimeSpan? AudioDuration
    {
        get => _audioDuration;
        set => SetField(ref _audioDuration, value);
    }

    /// <summary>
    /// True if this clip represents an audio file with waveform data.
    /// </summary>
    public bool IsAudioClip => WaveformData != null || !string.IsNullOrEmpty(SourceFilePath);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
