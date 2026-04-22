using System.Collections.Concurrent;
using System.Diagnostics;

namespace DAW.Services;

/// <summary>
/// Application-wide log sink. Captures Debug/Trace output and explicit log calls.
/// The OptionsWindow Debug tab reads from this buffer.
/// </summary>
public sealed class AppLogger
{
    public static AppLogger Instance { get; } = new();

    private readonly ConcurrentQueue<string> _lines = new();
    private const int MaxLines = 2000;

    /// <summary>Raised on the calling thread whenever a new line is added.</summary>
    public event Action<string>? LineAdded;

    private AppLogger()
    {
        // Install a global Trace listener so Debug.WriteLine is captured automatically.
        Trace.Listeners.Add(new DelegateTraceListener(msg => Log("TRACE", msg)));
    }

    public void Log(string category, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}";
        _lines.Enqueue(line);
        while (_lines.Count > MaxLines) _lines.TryDequeue(out _);
        LineAdded?.Invoke(line);
    }

    public void Info(string msg) => Log("INFO", msg);
    public void Warn(string msg) => Log("WARN", msg);
    public void Error(string msg) => Log("ERROR", msg);

    /// <summary>Returns a snapshot of all buffered log lines.</summary>
    public IReadOnlyList<string> GetLines() => [.. _lines];

    public void Clear() => _lines.Clear();

    private sealed class DelegateTraceListener(Action<string> handler) : TraceListener
    {
        public override void Write(string? message) { if (message is not null) handler(message); }
        public override void WriteLine(string? message) { if (message is not null) handler(message); }
    }
}
