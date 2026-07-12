using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DAW.MVVM.Views.Arrangement;

/// <summary>
/// Performance monitoring utility for arrangement view operations.
/// Helps identify bottlenecks in pixel-perfect rendering scenarios.
/// </summary>
public static class ArrangementPerformanceMonitor
{
    private static readonly Dictionary<string, Stopwatch> s_timers = new();
    private static readonly object s_lock = new();
    
    [Conditional("DEBUG")]
    public static void StartTimer([CallerMemberName] string? operation = null)
    {
        if (operation == null) return;
        
        lock (s_lock)
        {
            if (!s_timers.TryGetValue(operation, out var timer))
            {
                timer = new Stopwatch();
                s_timers[operation] = timer;
            }
            
            timer.Restart();
        }
    }
    
    [Conditional("DEBUG")]
    public static void StopTimer([CallerMemberName] string? operation = null)
    {
        if (operation == null) return;
        
        lock (s_lock)
        {
            if (s_timers.TryGetValue(operation, out var timer))
            {
                timer.Stop();
                if (timer.ElapsedMilliseconds > 16) // Frame time threshold
                {
                    System.Diagnostics.Debug.WriteLine($"PERF: {operation} took {timer.ElapsedMilliseconds}ms");
                }
            }
        }
    }
    
    [Conditional("DEBUG")]
    public static void LogClipCount(int count)
    {
        if (count > 500)
        {
            System.Diagnostics.Debug.WriteLine($"PERF: High clip count detected: {count} clips");
        }
    }
    
    [Conditional("DEBUG")]
    public static void LogScrollPerformance(double horizontalChange, double verticalChange)
    {
        if (Math.Abs(horizontalChange) > 1000 || Math.Abs(verticalChange) > 1000)
        {
            System.Diagnostics.Debug.WriteLine($"PERF: Large scroll delta: H={horizontalChange:F1}, V={verticalChange:F1}");
        }
    }
}