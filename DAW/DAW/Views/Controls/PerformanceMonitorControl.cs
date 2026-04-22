using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DAW.Views.Controls;

/// <summary>
/// Compact performance and memory visualization for the toolbar.
/// Shows CPU usage mini-graph, memory usage bar, and numeric readouts.
/// Styled to match the application's dark theme.
/// </summary>
public sealed class PerformanceMonitorControl : FrameworkElement
{
    // ── App theme colors ──────────────────────────────────────────────────

    private static readonly Typeface SansTypeface = new("Segoe UI");
    private static readonly Typeface MonoTypeface = new("Consolas");

    private static Brush GetBrush(string key) =>
        Application.Current?.Resources[key] as SolidColorBrush ?? Brushes.Gray;

    private static Color GetColor(string key) =>
        (Application.Current?.Resources[key] as SolidColorBrush)?.Color ?? Colors.Gray;

    // Functional accent colors (not theme-dependent)
    private static readonly Brush CpuBrush      = F(new SolidColorBrush(Color.FromRgb(0xE6, 0x39, 0x46)));
    private static readonly Brush CpuFillBrush  = F(new SolidColorBrush(Color.FromArgb(30, 0xE6, 0x39, 0x46)));
    private static readonly Pen   CpuLinePen    = FP(Color.FromRgb(0xE6, 0x39, 0x46), 1);
    private static readonly Brush MemBrush      = F(new SolidColorBrush(Color.FromRgb(0x5B, 0xA4, 0xE6)));
    private static readonly Brush GcBrush       = F(new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x40)));

    private static T F<T>(T b) where T : Freezable { b.Freeze(); return b; }
    private static Pen FP(Color c, double t) { var p = new Pen(new SolidColorBrush(c), t); p.Freeze(); return p; }

    // ── Data ──────────────────────────────────────────────────────────────

    private const int HistorySize = 30;
    private readonly double[] _cpuHistory = new double[HistorySize];
    private readonly double[] _memHistory = new double[HistorySize];
    private int _historyIndex;

    private double _cpuPercent;
    private double _memMb;
    private double _memPeakMb;
    private int _gcGen0, _gcGen1, _gcGen2;
    private long _lastProcessorTime;
    private DateTime _lastSampleTime;
    private readonly Process _process;

    private readonly DispatcherTimer _timer;

    public PerformanceMonitorControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Width = 180;
        Height = 42;

        _process = Process.GetCurrentProcess();
        _lastProcessorTime = _process.TotalProcessorTime.Ticks;
        _lastSampleTime = DateTime.UtcNow;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(1000) };
        _timer.Tick += OnTick;
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();

        // Initialize history
        Array.Fill(_cpuHistory, 0.0);
        Array.Fill(_memHistory, 0.0);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            _process.Refresh();

            // CPU
            var now = DateTime.UtcNow;
            long currentProcessorTime = _process.TotalProcessorTime.Ticks;
            double elapsed = (now - _lastSampleTime).TotalMilliseconds;
            if (elapsed > 0)
            {
                double cpuMs = (currentProcessorTime - _lastProcessorTime) / (double)TimeSpan.TicksPerMillisecond;
                _cpuPercent = Math.Clamp(cpuMs / elapsed / Environment.ProcessorCount * 100.0, 0, 100);
            }
            _lastProcessorTime = currentProcessorTime;
            _lastSampleTime = now;

            // Memory
            _memMb = _process.WorkingSet64 / (1024.0 * 1024.0);
            if (_memMb > _memPeakMb) _memPeakMb = _memMb;

            // GC
            _gcGen0 = GC.CollectionCount(0);
            _gcGen1 = GC.CollectionCount(1);
            _gcGen2 = GC.CollectionCount(2);

            // History
            _cpuHistory[_historyIndex] = _cpuPercent;
            _memHistory[_historyIndex] = _memMb;
            _historyIndex = (_historyIndex + 1) % HistorySize;
        }
        catch
        {
            // Process may have restricted access
        }

        InvalidateVisual();
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;

        // Theme-aware colors
        var bgBrush = GetBrush("DarkBg");
        var borderColor = GetColor("ControlBg");
        var borderPen = new Pen(new SolidColorBrush(borderColor), 1);

        dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(0, 0, w, h), 3, 3);

        double graphW = w * 0.48;
        double infoX = graphW + 6;
        double infoW = w - infoX - 4;

        DrawCpuGraph(dc, 3, 3, graphW - 3, h - 6);
        DrawMemoryInfo(dc, infoX, 3, infoW, h - 6);
    }

    private void DrawCpuGraph(DrawingContext dc, double x, double y, double w, double h)
    {
        var panelBrush = GetBrush("ControlBg");
        var labelBrush = GetBrush("TextDim");
        var gridColor = GetColor("BorderBrush");
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, gridColor.R, gridColor.G, gridColor.B)), 0.5);

        dc.DrawRoundedRectangle(panelBrush, null, new Rect(x, y, w, h), 2, 2);

        // Grid lines (25%, 50%, 75%)
        for (int i = 1; i <= 3; i++)
        {
            double gy = y + h * i / 4.0;
            dc.DrawLine(gridPen, new Point(x, gy), new Point(x + w, gy));
        }

        // CPU history polyline + fill
        if (w > 4 && h > 4)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(x, y + h), true, true);
                for (int i = 0; i < HistorySize; i++)
                {
                    int idx = (_historyIndex + i) % HistorySize;
                    double px = x + (double)i / (HistorySize - 1) * w;
                    double val = Math.Clamp(_cpuHistory[idx] / 100.0, 0, 1);
                    double py = y + h - val * h;
                    ctx.LineTo(new Point(px, py), true, false);
                }
                ctx.LineTo(new Point(x + w, y + h), false, false);
            }
            geo.Freeze();
            dc.DrawGeometry(CpuFillBrush, null, geo);

            // Stroke only
            var stroke = new StreamGeometry();
            using (var ctx = stroke.Open())
            {
                bool first = true;
                for (int i = 0; i < HistorySize; i++)
                {
                    int idx = (_historyIndex + i) % HistorySize;
                    double px = x + (double)i / (HistorySize - 1) * w;
                    double val = Math.Clamp(_cpuHistory[idx] / 100.0, 0, 1);
                    double py = y + h - val * h;
                    if (first) { ctx.BeginFigure(new Point(px, py), false, false); first = false; }
                    else ctx.LineTo(new Point(px, py), true, false);
                }
            }
            stroke.Freeze();
            dc.DrawGeometry(null, CpuLinePen, stroke);
        }

        // CPU label + value
        var cpuLabel = Txt("CPU", 7, labelBrush);
        dc.DrawText(cpuLabel, new Point(x + 2, y + 1));

        var cpuVal = Txt($"{_cpuPercent:F0}%", 8, CpuBrush, FontWeights.Bold, MonoTypeface);
        dc.DrawText(cpuVal, new Point(x + w - cpuVal.Width - 2, y + 1));
    }

    private void DrawMemoryInfo(DrawingContext dc, double x, double y, double w, double h)
    {
        var labelBrush = GetBrush("TextDim");
        var memBarBg = GetBrush("ControlBg");

        // Memory bar
        double barH = 6;
        double barY = y + 14;
        dc.DrawRoundedRectangle(memBarBg, null, new Rect(x, barY, w, barH), 2, 2);

        // Estimate max for bar scale (use peak * 1.2 or 512MB min)
        double maxMem = Math.Max(_memPeakMb * 1.2, 512);
        double memFill = Math.Clamp(_memMb / maxMem, 0, 1) * (w - 2);
        if (memFill > 1)
        {
            // Color based on usage
            Brush barFill = _memMb > 1500 ? CpuBrush : _memMb > 800 ? F(new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00))) : MemBrush;
            dc.DrawRoundedRectangle(barFill, null, new Rect(x + 1, barY + 1, memFill, barH - 2), 1, 1);
        }

        // "MEM" label + value
        var memLabel = Txt("MEM", 7, labelBrush);
        dc.DrawText(memLabel, new Point(x, y + 1));

        var memVal = Txt($"{_memMb:F0}MB", 8, MemBrush, FontWeights.Bold, MonoTypeface);
        dc.DrawText(memVal, new Point(x + w - memVal.Width, y + 1));

        // GC info row
        double gcY = barY + barH + 3;
        var gcText = Txt($"GC {_gcGen0}/{_gcGen1}/{_gcGen2}", 7, GcBrush);
        dc.DrawText(gcText, new Point(x, gcY));

        // Peak
        var peakText = Txt($"Pk:{_memPeakMb:F0}", 7, labelBrush);
        dc.DrawText(peakText, new Point(x + w - peakText.Width, gcY));
    }

    // ── Text helper ───────────────────────────────────────────────────────

    private static FormattedText Txt(string text, double size, Brush brush,
        FontWeight? weight = null, Typeface? tf = null)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf ?? SansTypeface, size, brush, 1.0);
        if (weight.HasValue) ft.SetFontWeight(weight.Value);
        return ft;
    }
}
