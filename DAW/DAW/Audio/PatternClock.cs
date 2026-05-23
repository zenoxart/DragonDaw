using System.Diagnostics;
using System.Windows.Threading;

namespace DAW.Audio;

/// <summary>
/// High-precision step sequencer clock.
///
/// Design
/// ──────
/// • A single background thread owns the tick counter and advances it atomically.
/// • Both audio and UI callbacks receive the resolved step index so there is no
///   shared mutable state between the two threads.
/// • The background thread fires AudioTick synchronously (no extra scheduling).
/// • The UI thread update is dispatched via Dispatcher.InvokeAsync at Send priority
///   for the visual step highlight — it may lag by one frame but never accumulates
///   tick-counter desync.
/// </summary>
public sealed class PatternClock : IDisposable
{
    private Thread?       _thread;
    private volatile bool _running;
    private double        _intervalMs;
    private int           _stepCount = 16;  // updated each tick from the caller

    private readonly Dispatcher _dispatcher;
    private readonly object     _startLock = new();

    /// <summary>
    /// Fired on the background audio thread with the zero-based step index.
    /// Must be thread-safe — do not touch WPF objects.
    /// </summary>
    public event Action<int>? AudioTick;

    /// <summary>
    /// Fired on the UI thread with the zero-based step index for visual feedback.
    /// </summary>
    public event Action<int>? UiTick;

    public PatternClock(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <param name="intervalMs">Duration of one step in milliseconds.</param>
    /// <param name="stepCount">Total number of steps in the pattern.</param>
    public void Start(double intervalMs, int stepCount)
    {
        lock (_startLock)
        {
            if (_running) return;
            _intervalMs = intervalMs;
            _stepCount  = Math.Max(1, stepCount);
            _running    = true;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Priority     = ThreadPriority.AboveNormal,
                Name         = "PatternClock"
            };
            _thread.Start();
        }
    }

    public void Stop()
    {
        _running = false;
        // Do NOT join — let the background thread exit on its own
        // to avoid deadlocking if Stop() is called from the UI thread.
    }

    public void UpdateInterval(double intervalMs, int stepCount)
    {
        _intervalMs = intervalMs;
        _stepCount  = Math.Max(1, stepCount);
    }

    private void Run()
    {
        var    sw     = Stopwatch.StartNew();
        // Start one full interval in so the first tick arrives after exactly
        // one step duration — matching the beat grid from t = 0.
        double nextMs = _intervalMs;
        int    tick   = 0;

        while (_running)
        {
            double now  = sw.Elapsed.TotalMilliseconds;
            double wait = nextMs - now;

            if (wait > 1.5)
                Thread.Sleep((int)(wait - 1.5));

            while (_running && sw.Elapsed.TotalMilliseconds < nextMs)
                Thread.SpinWait(50);

            if (!_running) break;

            // Compute step BEFORE advancing nextMs so jitter in the spin-wait
            // doesn't shift the step index.
            int step = tick % _stepCount;

            // Advance schedule and counter atomically on this thread only —
            // no other thread ever writes these locals.
            nextMs += _intervalMs;
            tick++;

            // ── Audio callback (background thread, synchronous) ─────────────
            AudioTick?.Invoke(step);

            // ── UI callback (UI thread, async — purely visual) ──────────────
            int capturedStep = step;
            _dispatcher.InvokeAsync(() => UiTick?.Invoke(capturedStep), DispatcherPriority.Send);
        }
    }

    public void Dispose() => Stop();
}

