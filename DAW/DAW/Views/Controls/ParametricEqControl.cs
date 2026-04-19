using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;

namespace DAW.Views.Controls;

/// <summary>
/// A custom WPF control that renders a parametric EQ visualizer inspired by FL Studio's Parametric EQ 2.
/// Features: frequency response curve, real-time FFT spectrum, draggable band nodes.
/// </summary>
public sealed class ParametricEqControl : FrameworkElement
{
    // ── Dependency Properties ──────────────────────────────────────────────

    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(EqualizerEffect), typeof(ParametricEqControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnEffectChanged));

    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(ParametricEqControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public EqualizerEffect? Effect
    {
        get => (EqualizerEffect?)GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    /// <summary>When true, hides labels and node decorations for inline mixer use.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    // ── Constants ──────────────────────────────────────────────────────────

    private const double MinFreq = 20.0;
    private const double MaxFreq = 20000.0;
    private const double MinDb = -18.0;
    private const double MaxDb = 18.0;
    private const double FreqResponsePoints = 512;

    // Band colors (FL Studio inspired – warm tones per band)
    private static readonly Color[] BandColors =
    [
        Color.FromRgb(0xE0, 0x40, 0x40), // 1 Red
        Color.FromRgb(0xE0, 0x90, 0x30), // 2 Orange
        Color.FromRgb(0xE0, 0xD0, 0x30), // 3 Yellow
        Color.FromRgb(0x50, 0xC8, 0x50), // 4 Green
        Color.FromRgb(0x40, 0xA0, 0xE0), // 5 Blue
        Color.FromRgb(0x80, 0x60, 0xE0), // 6 Purple
        Color.FromRgb(0xE0, 0x50, 0xB0), // 7 Pink
    ];

    // ── Brushes & Pens (cached) ───────────────────────────────────────────

    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x14));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 0.5);
    private static readonly Pen GridPenMajor = new(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 0.5);
    private static readonly Pen ZeroLinePen = new(new SolidColorBrush(Color.FromArgb(80, 0x5B, 0xA4, 0xE6)), 1.0);
    private static readonly Pen CurvePen = new(new SolidColorBrush(Color.FromArgb(200, 0xFF, 0xFF, 0xFF)), 1.5);
    private static readonly Brush CurveFillBrush;
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromArgb(120, 0xCC, 0xCC, 0xCC));
    private static readonly Brush SpectrumBrush;
    private static readonly Pen SpectrumStrokePen;

    private const int NodeRadius = 6;
    private const int NodeRadiusCompact = 4;

    static ParametricEqControl()
    {
        // Freeze shared brushes/pens for cross-thread safety
        BackgroundBrush.Freeze();
        GridPen.Freeze();
        GridPenMajor.Freeze();
        ZeroLinePen.Freeze();
        CurvePen.Freeze();
        LabelBrush.Freeze();

        var curveFill = new LinearGradientBrush(
            Color.FromArgb(60, 0x5B, 0xA4, 0xE6),
            Color.FromArgb(5, 0x5B, 0xA4, 0xE6),
            new Point(0, 0), new Point(0, 1));
        curveFill.Freeze();
        CurveFillBrush = curveFill;

        var specBrush = new LinearGradientBrush(
            Color.FromArgb(90, 0x2E, 0xCC, 0x40),
            Color.FromArgb(15, 0x2E, 0xCC, 0x40),
            new Point(0, 0), new Point(0, 1));
        specBrush.Freeze();
        SpectrumBrush = specBrush;

        var specStroke = new Pen(new SolidColorBrush(Color.FromArgb(120, 0x2E, 0xCC, 0x40)), 1.0);
        specStroke.Freeze();
        SpectrumStrokePen = specStroke;
    }

    // ── State ─────────────────────────────────────────────────────────────

    private int _dragBandIndex = -1;
    private Point _dragStart;
    private double _dragStartFreq;
    private double _dragStartGain;
    private readonly DispatcherTimer _refreshTimer;

    public ParametricEqControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = true;

        // Refresh at ~30 fps for spectrum animation
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _refreshTimer.Tick += (_, _) => InvalidateVisual();

        Loaded += (_, _) => _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();
    }

    // ── Effect change handling ─────────────────────────────────────────────

    private static void OnEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (ParametricEqControl)d;
        if (e.OldValue is EqualizerEffect oldEq)
        {
            foreach (var band in oldEq.Bands)
                band.PropertyChanged -= ctrl.OnBandChanged;
        }
        if (e.NewValue is EqualizerEffect newEq)
        {
            foreach (var band in newEq.Bands)
                band.PropertyChanged += ctrl.OnBandChanged;
        }
        ctrl.InvalidateVisual();
    }

    private void OnBandChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    // ── Coordinate mapping ────────────────────────────────────────────────

    private double FreqToX(double freq, double width)
    {
        double logMin = Math.Log10(MinFreq);
        double logMax = Math.Log10(MaxFreq);
        return (Math.Log10(Math.Clamp(freq, MinFreq, MaxFreq)) - logMin) / (logMax - logMin) * width;
    }

    private double XToFreq(double x, double width)
    {
        double logMin = Math.Log10(MinFreq);
        double logMax = Math.Log10(MaxFreq);
        double logFreq = logMin + (x / width) * (logMax - logMin);
        return Math.Clamp(Math.Pow(10, logFreq), MinFreq, MaxFreq);
    }

    private double DbToY(double db, double height)
    {
        return height * (1.0 - (db - MinDb) / (MaxDb - MinDb));
    }

    private double YToDb(double y, double height)
    {
        return MinDb + (1.0 - y / height) * (MaxDb - MinDb);
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 10) return;

        // Background
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

        DrawGrid(dc, w, h);
        DrawSpectrum(dc, w, h);
        DrawCurve(dc, w, h);
        DrawNodes(dc, w, h);
    }

    private void DrawGrid(DrawingContext dc, double w, double h)
    {
        bool compact = IsCompact;

        // Frequency grid lines (logarithmic)
        double[] freqLines = [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000];
        double[] freqMajor = [100, 1000, 10000];
        foreach (var f in freqLines)
        {
            double x = FreqToX(f, w);
            var pen = freqMajor.Contains(f) ? GridPenMajor : GridPen;
            dc.DrawLine(pen, new Point(x, 0), new Point(x, h));

            if (!compact && h > 100)
            {
                string label = f >= 1000 ? $"{f / 1000}k" : $"{f}";
                var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, LabelTypeface, 9, LabelBrush, 1.0);
                dc.DrawText(text, new Point(x - text.Width / 2, h - text.Height - 1));
            }
        }

        // dB grid lines
        double[] dbLines = [-18, -12, -6, 0, 6, 12, 18];
        foreach (var db in dbLines)
        {
            double y = DbToY(db, h);
            var pen = db == 0 ? ZeroLinePen : GridPen;
            dc.DrawLine(pen, new Point(0, y), new Point(w, y));

            if (!compact && w > 120)
            {
                var text = new FormattedText($"{db:+0;-0;0}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, LabelTypeface, 8, LabelBrush, 1.0);
                dc.DrawText(text, new Point(2, y - text.Height / 2));
            }
        }
    }

    // Offset added to raw FFT dB so that typical audio (-60..-20 dB) maps into the visible ±18 dB range.
    private const double SpectrumDbBoost = 50.0;

    private void DrawSpectrum(DrawingContext dc, double w, double h)
    {
        var eq = Effect;
        if (eq == null) return;

        var spectrumData = eq.SpectrumData;
        if (spectrumData == null || spectrumData.Length == 0) return;

        int specLen = spectrumData.Length;
        double sampleRate = eq.LastSampleRate > 0 ? eq.LastSampleRate : 44100;

        // Build filled area geometry
        var geometry = new StreamGeometry();
        // Build stroke-only polyline
        var strokeGeom = new StreamGeometry();
        using var ctx = geometry.Open();
        using var sctx = strokeGeom.Open();
        bool started = false;
        double prevX = 0;

        for (int i = 1; i < specLen; i++)
        {
            double freq = (double)i / specLen * (sampleRate / 2.0);
            if (freq < MinFreq || freq > MaxFreq) continue;

            double magnitude = spectrumData[i];
            // Convert to dB with display boost so normal-level audio is visible
            double db = magnitude > 1e-10 ? 20.0 * Math.Log10(magnitude) + SpectrumDbBoost : MinDb;
            db = Math.Clamp(db, MinDb, MaxDb);

            double x = FreqToX(freq, w);
            double y = DbToY(db, h);

            // Skip if too close to previous point (performance)
            if (started && Math.Abs(x - prevX) < 1.5) continue;

            if (!started)
            {
                ctx.BeginFigure(new Point(x, h), true, true);
                ctx.LineTo(new Point(x, y), false, false);
                sctx.BeginFigure(new Point(x, y), false, false);
                started = true;
            }
            else
            {
                ctx.LineTo(new Point(x, y), true, true);
                sctx.LineTo(new Point(x, y), true, true);
            }
            prevX = x;
        }

        if (started)
        {
            ctx.LineTo(new Point(prevX, h), false, false);
        }

        geometry.Freeze();
        strokeGeom.Freeze();
        dc.DrawGeometry(SpectrumBrush, null, geometry);
        dc.DrawGeometry(null, SpectrumStrokePen, strokeGeom);
    }

    private void DrawCurve(DrawingContext dc, double w, double h)
    {
        var eq = Effect;
        if (eq == null) return;

        double sampleRate = eq.LastSampleRate > 0 ? eq.LastSampleRate : 44100;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            bool first = true;
            double firstX = 0;

            for (int i = 0; i <= FreqResponsePoints; i++)
            {
                double t = i / FreqResponsePoints;
                double logFreq = Math.Log10(MinFreq) + t * (Math.Log10(MaxFreq) - Math.Log10(MinFreq));
                double freq = Math.Pow(10, logFreq);

                double totalDb = ComputeResponseAt(eq, freq, sampleRate);
                totalDb = Math.Clamp(totalDb, MinDb - 3, MaxDb + 3);

                double x = FreqToX(freq, w);
                double y = DbToY(totalDb, h);

                if (first)
                {
                    ctx.BeginFigure(new Point(x, y), true, true);
                    firstX = x;
                    first = false;
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, true);
                }
            }

            // Close the fill shape along the bottom
            double lastX = FreqToX(MaxFreq, w);
            double zeroY = DbToY(0, h);
            ctx.LineTo(new Point(lastX, zeroY), false, false);
            ctx.LineTo(new Point(firstX, zeroY), false, false);
        }

        geometry.Freeze();

        // Fill under the curve
        dc.DrawGeometry(CurveFillBrush, null, geometry);

        // Draw the curve stroke (just the top part)
        var strokeGeometry = new StreamGeometry();
        using (var ctx = strokeGeometry.Open())
        {
            bool first = true;
            for (int i = 0; i <= FreqResponsePoints; i++)
            {
                double t = i / FreqResponsePoints;
                double logFreq = Math.Log10(MinFreq) + t * (Math.Log10(MaxFreq) - Math.Log10(MinFreq));
                double freq = Math.Pow(10, logFreq);

                double totalDb = ComputeResponseAt(eq, freq, sampleRate);
                totalDb = Math.Clamp(totalDb, MinDb - 3, MaxDb + 3);

                double x = FreqToX(freq, w);
                double y = DbToY(totalDb, h);

                if (first) { ctx.BeginFigure(new Point(x, y), false, false); first = false; }
                else ctx.LineTo(new Point(x, y), true, true);
            }
        }
        strokeGeometry.Freeze();
        dc.DrawGeometry(null, CurvePen, strokeGeometry);
    }

    private void DrawNodes(DrawingContext dc, double w, double h)
    {
        var eq = Effect;
        if (eq == null) return;

        bool compact = IsCompact;
        int radius = compact ? NodeRadiusCompact : NodeRadius;

        for (int i = 0; i < EqualizerEffect.BandCount; i++)
        {
            var band = eq.Bands[i];
            if (!band.IsEnabled) continue;

            double x = FreqToX(band.Frequency, w);
            double y = DbToY(band.Gain, h);
            var color = BandColors[i];

            // Outer glow
            if (!compact)
            {
                var glowBrush = new RadialGradientBrush(
                    Color.FromArgb(60, color.R, color.G, color.B),
                    Colors.Transparent)
                {
                    RadiusX = 1, RadiusY = 1
                };
                dc.DrawEllipse(glowBrush, null, new Point(x, y), radius * 2.5, radius * 2.5);
            }

            // Node circle
            var fillBrush = new SolidColorBrush(color);
            var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1.5);
            dc.DrawEllipse(fillBrush, borderPen, new Point(x, y), radius, radius);

            // Band number label
            if (!compact)
            {
                var text = new FormattedText($"{band.Number}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, LabelTypeface, 8,
                    new SolidColorBrush(Colors.White), 1.0);
                dc.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
            }
        }
    }

    // ── Frequency response calculation ────────────────────────────────────

    /// <summary>Compute the combined magnitude response in dB at a given frequency.</summary>
    private static double ComputeResponseAt(EqualizerEffect eq, double freq, double sampleRate)
    {
        double totalDb = 0;
        for (int b = 0; b < EqualizerEffect.BandCount; b++)
        {
            var band = eq.Bands[b];
            if (!band.IsEnabled) continue;
            if (band.Mode == EqBandMode.Peaking && Math.Abs(band.Gain) < 0.01) continue;

            totalDb += ComputeBandResponseDb(band, freq, sampleRate);
        }
        return totalDb;
    }

    /// <summary>Compute a single band's magnitude response in dB using the biquad transfer function.</summary>
    private static double ComputeBandResponseDb(EqBand band, double freq, double sampleRate)
    {
        double gainDb = band.Gain;
        double centerFreq = band.Frequency;
        double q = band.Q;

        double omega = 2.0 * Math.PI * centerFreq / sampleRate;
        double sinOmega = Math.Sin(omega);
        double cosOmega = Math.Cos(omega);
        double alpha = sinOmega / (2.0 * q);
        double A = Math.Pow(10, gainDb / 40.0);

        double b0, b1, b2, a0, a1, a2;

        switch (band.Mode)
        {
            case EqBandMode.Peaking:
                b0 = 1 + alpha * A; b1 = -2 * cosOmega; b2 = 1 - alpha * A;
                a0 = 1 + alpha / A; a1 = -2 * cosOmega; a2 = 1 - alpha / A;
                break;
            case EqBandMode.LowShelf:
            {
                var sa = Math.Sqrt(A);
                b0 = A * ((A + 1) - (A - 1) * cosOmega + 2 * sa * alpha);
                b1 = 2 * A * ((A - 1) - (A + 1) * cosOmega);
                b2 = A * ((A + 1) - (A - 1) * cosOmega - 2 * sa * alpha);
                a0 = (A + 1) + (A - 1) * cosOmega + 2 * sa * alpha;
                a1 = -2 * ((A - 1) + (A + 1) * cosOmega);
                a2 = (A + 1) + (A - 1) * cosOmega - 2 * sa * alpha;
                break;
            }
            case EqBandMode.HighShelf:
            {
                var sa = Math.Sqrt(A);
                b0 = A * ((A + 1) + (A - 1) * cosOmega + 2 * sa * alpha);
                b1 = -2 * A * ((A - 1) + (A + 1) * cosOmega);
                b2 = A * ((A + 1) + (A - 1) * cosOmega - 2 * sa * alpha);
                a0 = (A + 1) - (A - 1) * cosOmega + 2 * sa * alpha;
                a1 = 2 * ((A - 1) - (A + 1) * cosOmega);
                a2 = (A + 1) - (A - 1) * cosOmega - 2 * sa * alpha;
                break;
            }
            case EqBandMode.LowCut:
                b0 = (1 + cosOmega) / 2; b1 = -(1 + cosOmega); b2 = (1 + cosOmega) / 2;
                a0 = 1 + alpha; a1 = -2 * cosOmega; a2 = 1 - alpha;
                break;
            case EqBandMode.HighCut:
                b0 = (1 - cosOmega) / 2; b1 = 1 - cosOmega; b2 = (1 - cosOmega) / 2;
                a0 = 1 + alpha; a1 = -2 * cosOmega; a2 = 1 - alpha;
                break;
            case EqBandMode.Notch:
                b0 = 1; b1 = -2 * cosOmega; b2 = 1;
                a0 = 1 + alpha; a1 = -2 * cosOmega; a2 = 1 - alpha;
                break;
            case EqBandMode.BandPass:
                b0 = alpha; b1 = 0; b2 = -alpha;
                a0 = 1 + alpha; a1 = -2 * cosOmega; a2 = 1 - alpha;
                break;
            default:
                return 0;
        }

        // Normalize
        b0 /= a0; b1 /= a0; b2 /= a0;
        a1 /= a0; a2 /= a0;

        // Evaluate H(z) at z = e^(jω) where ω = 2π·freq/sampleRate
        double w = 2.0 * Math.PI * freq / sampleRate;
        double cosW = Math.Cos(w);
        double cos2W = Math.Cos(2 * w);
        double sinW = Math.Sin(w);
        double sin2W = Math.Sin(2 * w);

        double numReal = b0 + b1 * cosW + b2 * cos2W;
        double numImag = -(b1 * sinW + b2 * sin2W);
        double denReal = 1 + a1 * cosW + a2 * cos2W;
        double denImag = -(a1 * sinW + a2 * sin2W);

        double numMag = numReal * numReal + numImag * numImag;
        double denMag = denReal * denReal + denImag * denImag;

        if (denMag < 1e-20) return 0;
        double magSq = numMag / denMag;
        return 10.0 * Math.Log10(Math.Max(magSq, 1e-20));
    }

    // ── Mouse interaction ─────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var eq = Effect;
        if (eq == null) return;

        var pos = e.GetPosition(this);
        _dragBandIndex = HitTestBand(pos);

        if (_dragBandIndex >= 0)
        {
            var band = eq.Bands[_dragBandIndex];
            _dragStart = pos;
            _dragStartFreq = band.Frequency;
            _dragStartGain = band.Gain;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragBandIndex < 0 || Effect == null) return;

        var pos = e.GetPosition(this);
        var band = Effect.Bands[_dragBandIndex];

        double newFreq = XToFreq(pos.X, ActualWidth);
        double newGain = YToDb(pos.Y, ActualHeight);

        band.Frequency = newFreq;
        band.Gain = newGain;

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragBandIndex >= 0)
        {
            _dragBandIndex = -1;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var eq = Effect;
        if (eq == null) return;

        var pos = e.GetPosition(this);
        int bandIndex = HitTestBand(pos);
        if (bandIndex < 0)
        {
            // If not directly on a node, find nearest band
            bandIndex = FindNearestBand(pos);
        }
        if (bandIndex < 0) return;

        var band = eq.Bands[bandIndex];
        double delta = e.Delta > 0 ? 0.1 : -0.1;
        // Scale delta based on current Q (logarithmic feel)
        double newQ = band.Q * (1.0 + delta);
        band.Q = Math.Clamp(newQ, 0.05, 30);
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var eq = Effect;
        if (eq == null) return;

        var pos = e.GetPosition(this);
        int bandIndex = HitTestBand(pos);
        if (bandIndex < 0)
            bandIndex = FindNearestBand(pos);
        if (bandIndex < 0) return;

        var band = eq.Bands[bandIndex];
        ShowBandContextMenu(band);
        e.Handled = true;
    }

    private void ShowBandContextMenu(EqBand band)
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x36, 0x3D)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9)),
        };

        // Header
        var header = new System.Windows.Controls.MenuItem
        {
            Header = $"Band {band.Number}",
            IsEnabled = false,
            FontWeight = FontWeights.Bold
        };
        menu.Items.Add(header);
        menu.Items.Add(new System.Windows.Controls.Separator());

        // Enable/Disable toggle
        var enableItem = new System.Windows.Controls.MenuItem
        {
            Header = band.IsEnabled ? "✓ Enabled" : "  Disabled",
            FontWeight = band.IsEnabled ? FontWeights.SemiBold : FontWeights.Normal
        };
        enableItem.Click += (_, _) => band.IsEnabled = !band.IsEnabled;
        menu.Items.Add(enableItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Mode options
        foreach (EqBandMode mode in Enum.GetValues<EqBandMode>())
        {
            var modeItem = new System.Windows.Controls.MenuItem
            {
                Header = (band.Mode == mode ? "● " : "   ") + mode.ToString(),
                Tag = mode,
                FontWeight = band.Mode == mode ? FontWeights.SemiBold : FontWeights.Normal
            };
            var capturedMode = mode;
            modeItem.Click += (_, _) => band.Mode = capturedMode;
            menu.Items.Add(modeItem);
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Reset band
        var resetItem = new System.Windows.Controls.MenuItem { Header = "Reset Band" };
        resetItem.Click += (_, _) =>
        {
            band.Gain = 0;
            band.Q = 1.0;
            band.IsEnabled = true;
        };
        menu.Items.Add(resetItem);

        menu.IsOpen = true;
        ContextMenu = menu;
    }

    private int HitTestBand(Point pos)
    {
        var eq = Effect;
        if (eq == null) return -1;

        double hitRadius = IsCompact ? 8 : 12;
        for (int i = 0; i < EqualizerEffect.BandCount; i++)
        {
            var band = eq.Bands[i];
            double bx = FreqToX(band.Frequency, ActualWidth);
            double by = DbToY(band.Gain, ActualHeight);
            double dist = Math.Sqrt((pos.X - bx) * (pos.X - bx) + (pos.Y - by) * (pos.Y - by));
            if (dist <= hitRadius) return i;
        }
        return -1;
    }

    private int FindNearestBand(Point pos)
    {
        var eq = Effect;
        if (eq == null) return -1;

        int nearest = -1;
        double minDist = 50; // Max distance to consider
        for (int i = 0; i < EqualizerEffect.BandCount; i++)
        {
            var band = eq.Bands[i];
            double bx = FreqToX(band.Frequency, ActualWidth);
            double by = DbToY(band.Gain, ActualHeight);
            double dist = Math.Sqrt((pos.X - bx) * (pos.X - bx) + (pos.Y - by) * (pos.Y - by));
            if (dist < minDist)
            {
                minDist = dist;
                nearest = i;
            }
        }
        return nearest;
    }
}
