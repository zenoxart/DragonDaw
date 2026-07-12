using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.MVVM.Models;

/// <summary>
/// Global application state for transport and tool management.
/// </summary>
public sealed class GlobalApplicationState : INotifyPropertyChanged
{
    private TransportState _transportState = TransportState.Stopped;
    private EditTool _activeTool = EditTool.Select;
    private bool _isMonitoringEnabled = true;
    private bool _isMetronomeEnabled = false;
    private double _bpm = 140.0;
    private TimeSpan _currentPosition = TimeSpan.Zero;
    private double _masterVolume = 0.8;

    /// <summary>
    /// Current transport state (Playing, Paused, Stopped, Recording).
    /// </summary>
    public TransportState TransportState
    {
        get => _transportState;
        set => SetField(ref _transportState, value);
    }

    /// <summary>
    /// Currently active edit tool. Only one tool can be active at a time.
    /// </summary>
    public EditTool ActiveTool
    {
        get => _activeTool;
        set => SetField(ref _activeTool, value);
    }

    /// <summary>
    /// Whether audio monitoring is enabled for input recording.
    /// </summary>
    public bool IsMonitoringEnabled
    {
        get => _isMonitoringEnabled;
        set => SetField(ref _isMonitoringEnabled, value);
    }

    /// <summary>
    /// Whether the metronome click is enabled during playback/recording.
    /// </summary>
    public bool IsMetronomeEnabled
    {
        get => _isMetronomeEnabled;
        set => SetField(ref _isMetronomeEnabled, value);
    }

    /// <summary>
    /// Current BPM (Beats Per Minute).
    /// </summary>
    public double BPM
    {
        get => _bpm;
        set => SetField(ref _bpm, Math.Clamp(value, 1, 500));
    }

    /// <summary>
    /// Current playback position.
    /// </summary>
    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set => SetField(ref _currentPosition, value);
    }

    /// <summary>
    /// Master volume level (0.0 to 1.0).
    /// </summary>
    public double MasterVolume
    {
        get => _masterVolume;
        set => SetField(ref _masterVolume, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>
    /// Convenience properties for UI binding.
    /// </summary>
    public bool IsPlaying => _transportState == TransportState.Playing;
    public bool IsPaused => _transportState == TransportState.Paused;
    public bool IsStopped => _transportState == TransportState.Stopped;
    public bool IsRecording => _transportState == TransportState.Recording;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        
        // Notify related properties for UI binding
        if (propertyName == nameof(TransportState))
        {
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsStopped));
            OnPropertyChanged(nameof(IsRecording));
        }
        
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Transport states for the audio engine.
/// </summary>
public enum TransportState
{
    Stopped,
    Playing,
    Paused,
    Recording
}

/// <summary>
/// Available edit tools that affect user interaction in Piano Roll and Playlist.
/// Only one tool can be active at a time.
/// </summary>
public enum EditTool
{
    /// <summary>Default selection tool for moving and selecting objects.</summary>
    Select,
    
    /// <summary>Drawing tool for creating new notes/clips.</summary>
    Draw,
    
    /// <summary>Paint tool for quickly adding multiple items.</summary>
    Paint,
    
    /// <summary>Slice tool for cutting clips/notes.</summary>
    Slice,
    
    /// <summary>Resize tool for changing object lengths.</summary>
    Resize,
    
    /// <summary>Zoom tool for area-based zooming.</summary>
    Zoom
}