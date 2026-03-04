using System.ComponentModel;
using DAW.Models;

namespace DAW.Services;

/// <summary>
/// Service for managing transport controls and audio engine communication.
/// Handles Play, Pause, Stop, Record commands and ensures thread-safe communication with the audio engine.
/// </summary>
public sealed class TransportService : INotifyPropertyChanged
{
    private readonly GlobalApplicationState _globalState;
    private readonly AudioEngineService _audioEngine;
    private readonly object _lockObject = new();
    private CancellationTokenSource? _playbackCancellation;

    public TransportService(GlobalApplicationState globalState, AudioEngineService audioEngine)
    {
        _globalState = globalState ?? throw new ArgumentNullException(nameof(globalState));
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        
        // Listen to global state changes
        _globalState.PropertyChanged += OnGlobalStateChanged;
    }

    /// <summary>
    /// Current transport state.
    /// </summary>
    public TransportState State => _globalState.TransportState;

    /// <summary>
    /// Current playback position.
    /// </summary>
    public TimeSpan CurrentPosition => _globalState.CurrentPosition;

    /// <summary>
    /// Starts playback from current position.
    /// </summary>
    public async Task PlayAsync()
    {
        lock (_lockObject)
        {
            if (_globalState.TransportState == TransportState.Playing)
                return;

            _globalState.TransportState = TransportState.Playing;
            _playbackCancellation = new CancellationTokenSource();
        }

        try
        {
            // Start audio engine on background thread
            await Task.Run(() => _audioEngine.StartPlayback(_globalState.CurrentPosition), 
                          _playbackCancellation.Token);
            
            // Start position updates
            _ = Task.Run(() => UpdatePlaybackPositionAsync(_playbackCancellation.Token), 
                        _playbackCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Playback was cancelled - this is normal
        }
        catch (Exception ex)
        {
            // Handle audio engine errors
            System.Diagnostics.Debug.WriteLine($"Playback error: {ex.Message}");
            await StopAsync();
        }
    }

    /// <summary>
    /// Pauses playback at current position.
    /// </summary>
    public async Task PauseAsync()
    {
        lock (_lockObject)
        {
            if (_globalState.TransportState != TransportState.Playing)
                return;

            _globalState.TransportState = TransportState.Paused;
            _playbackCancellation?.Cancel();
        }

        await Task.Run(() => _audioEngine.PausePlayback());
    }

    /// <summary>
    /// Stops playback and resets position to beginning.
    /// </summary>
    public async Task StopAsync()
    {
        lock (_lockObject)
        {
            if (_globalState.TransportState == TransportState.Stopped)
                return;

            _globalState.TransportState = TransportState.Stopped;
            _playbackCancellation?.Cancel();
        }

        await Task.Run(() => _audioEngine.StopPlayback());
        _globalState.CurrentPosition = TimeSpan.Zero;
    }

    /// <summary>
    /// Starts recording from current position.
    /// </summary>
    public async Task RecordAsync()
    {
        lock (_lockObject)
        {
            if (_globalState.TransportState == TransportState.Recording)
                return;

            _globalState.TransportState = TransportState.Recording;
            _playbackCancellation = new CancellationTokenSource();
        }

        try
        {
            await Task.Run(() => _audioEngine.StartRecording(_globalState.CurrentPosition, 
                                                           _globalState.IsMonitoringEnabled),
                          _playbackCancellation.Token);
            
            _ = Task.Run(() => UpdatePlaybackPositionAsync(_playbackCancellation.Token),
                        _playbackCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Recording was cancelled
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recording error: {ex.Message}");
            await StopAsync();
        }
    }

    /// <summary>
    /// Sets the playback position.
    /// </summary>
    public async Task SeekToAsync(TimeSpan position)
    {
        _globalState.CurrentPosition = position;
        
        if (_globalState.TransportState != TransportState.Stopped)
        {
            await Task.Run(() => _audioEngine.SeekTo(position));
        }
    }

    /// <summary>
    /// Sets the metronome state.
    /// </summary>
    public void SetMetronome(bool enabled)
    {
        _globalState.IsMetronomeEnabled = enabled;
        _audioEngine.SetMetronomeEnabled(enabled);
    }

    /// <summary>
    /// Sets the monitoring state.
    /// </summary>
    public void SetMonitoring(bool enabled)
    {
        _globalState.IsMonitoringEnabled = enabled;
        _audioEngine.SetMonitoringEnabled(enabled);
    }

    /// <summary>
    /// Updates the BPM and notifies the audio engine.
    /// </summary>
    public void SetBPM(double bpm)
    {
        _globalState.BPM = bpm;
        _audioEngine.SetBPM(bpm);
    }

    private async Task UpdatePlaybackPositionAsync(CancellationToken cancellationToken)
    {
        const int updateIntervalMs = 50; // 20 FPS updates
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var position = await Task.Run(() => _audioEngine.GetCurrentPosition(), cancellationToken);
                _globalState.CurrentPosition = position;
                
                await Task.Delay(updateIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void OnGlobalStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GlobalApplicationState.TransportState) ||
            e.PropertyName == nameof(GlobalApplicationState.CurrentPosition))
        {
            OnPropertyChanged(e.PropertyName);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _playbackCancellation?.Cancel();
        _playbackCancellation?.Dispose();
        _globalState.PropertyChanged -= OnGlobalStateChanged;
    }
}

/// <summary>
/// Mock audio engine service for demonstration.
/// In a real implementation, this would interface with actual audio hardware.
/// </summary>
public sealed class AudioEngineService
{
    private TimeSpan _currentPosition;
    private DateTime _playbackStartTime;
    private bool _isPlaying;

    public void StartPlayback(TimeSpan startPosition)
    {
        _currentPosition = startPosition;
        _playbackStartTime = DateTime.UtcNow;
        _isPlaying = true;
        System.Diagnostics.Debug.WriteLine($"Audio engine: Starting playback at {startPosition}");
    }

    public void PausePlayback()
    {
        _isPlaying = false;
        System.Diagnostics.Debug.WriteLine("Audio engine: Pausing playback");
    }

    public void StopPlayback()
    {
        _isPlaying = false;
        _currentPosition = TimeSpan.Zero;
        System.Diagnostics.Debug.WriteLine("Audio engine: Stopping playback");
    }

    public void StartRecording(TimeSpan startPosition, bool monitoring)
    {
        _currentPosition = startPosition;
        _playbackStartTime = DateTime.UtcNow;
        _isPlaying = true;
        System.Diagnostics.Debug.WriteLine($"Audio engine: Starting recording at {startPosition}, monitoring={monitoring}");
    }

    public void SeekTo(TimeSpan position)
    {
        _currentPosition = position;
        _playbackStartTime = DateTime.UtcNow;
        System.Diagnostics.Debug.WriteLine($"Audio engine: Seeking to {position}");
    }

    public TimeSpan GetCurrentPosition()
    {
        if (!_isPlaying)
            return _currentPosition;

        var elapsed = DateTime.UtcNow - _playbackStartTime;
        return _currentPosition + elapsed;
    }

    public void SetMetronomeEnabled(bool enabled)
    {
        System.Diagnostics.Debug.WriteLine($"Audio engine: Metronome {(enabled ? "enabled" : "disabled")}");
    }

    public void SetMonitoringEnabled(bool enabled)
    {
        System.Diagnostics.Debug.WriteLine($"Audio engine: Monitoring {(enabled ? "enabled" : "disabled")}");
    }

    public void SetBPM(double bpm)
    {
        System.Diagnostics.Debug.WriteLine($"Audio engine: BPM set to {bpm}");
    }
}