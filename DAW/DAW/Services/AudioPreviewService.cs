using System.IO;
using NAudio.Wave;

namespace DAW.Services;

/// <summary>
/// Event args for AudioPreviewService state changes.
/// </summary>
public sealed class PreviewStateEventArgs : EventArgs
{
    public PreviewStateEventArgs(string? filePath, bool isPlaying)
    {
        FilePath  = filePath;
        IsPlaying = isPlaying;
    }

    public string? FilePath  { get; }
    public bool    IsPlaying { get; }
}

/// <summary>
/// Singleton service for non-blocking audio preview.
///
/// Design decisions
/// ─────────────────
/// • WaveOutEvent + AudioFileReader are created inside Task.Run so that
///   the COM-init and file I/O never block the UI thread.
/// • A SemaphoreSlim(1,1) gate serialises concurrent preview requests:
///   if the user clicks quickly through many files, each new request
///   first disposes the previous playback before starting a new one.
/// • CancellationToken support lets the ViewModel cancel an in-flight
///   preview load when the user navigates away.
/// • StateChanged is fired from a background thread; subscribers that
///   touch UI must marshal to the dispatcher themselves.
/// </summary>
public sealed class AudioPreviewService : IDisposable
{
    private static readonly Lazy<AudioPreviewService> _lazy =
        new(() => new AudioPreviewService());

    public static AudioPreviewService Instance => _lazy.Value;

    // ── Playback state ─────────────────────────────────────────────────────
    private WaveOutEvent?   _waveOut;
    private AudioFileReader? _reader;
    private string?          _currentFile;
    private bool             _disposed;

    // Gate: only one preview operation at a time
    private readonly SemaphoreSlim _gate = new(1, 1);

    // ── Public surface ─────────────────────────────────────────────────────
    public event EventHandler<PreviewStateEventArgs>? StateChanged;

    public bool    IsPlaying    => _waveOut?.PlaybackState == PlaybackState.Playing;
    public string? CurrentFile  => _currentFile;
    public float   Volume       { get; set; } = 0.8f;

    private AudioPreviewService() { }

    /// <summary>
    /// Starts playback of <paramref name="filePath"/> asynchronously.
    /// Any currently playing preview is stopped first.
    /// </summary>
    public async Task PreviewAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath) || _disposed) return;

        await _gate.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                DisposePlayback();

                _reader  = new AudioFileReader(filePath) { Volume = Volume };
                _waveOut = new WaveOutEvent();
                _waveOut.PlaybackStopped += OnPlaybackStopped;
                _waveOut.Init(_reader);
                _waveOut.Play();
                _currentFile = filePath;
            }, ct);

            StateChanged?.Invoke(this, new PreviewStateEventArgs(filePath, true));
        }
        catch (OperationCanceledException) { /* expected during rapid navigation */ }
        catch { DisposePlayback(); }
        finally { _gate.Release(); }
    }

    /// <summary>Stops the current preview immediately.</summary>
    public void Stop() => _waveOut?.Stop(); // triggers OnPlaybackStopped

    // ── Private helpers ────────────────────────────────────────────────────

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        var file = _currentFile;
        DisposePlayback();
        StateChanged?.Invoke(this, new PreviewStateEventArgs(file, false));
    }

    private void DisposePlayback()
    {
        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }
        _reader?.Dispose();
        _reader      = null;
        _currentFile = null;
    }

    public void Dispose()
    {
        _disposed = true;
        DisposePlayback();
        _gate.Dispose();
    }
}
