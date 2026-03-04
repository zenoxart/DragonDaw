using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DAW.Views.Arrangement;

/// <summary>
/// Pixel-perfect scrollable ruler that displays bar numbers and beat tick-marks.
/// 
/// Improvements:
/// • Pixel-snapped rendering for crisp lines
/// • DPI-aware coordinate calculations
/// • UseLayoutRounding for consistency
/// </summary>
public sealed class PixelPerfectRulerControl : FrameworkElement
{
    public static readonly DependencyProperty PixelsPerBeatProperty =
        DependencyProperty.Register(nameof(PixelsPerBeat), typeof(double),
            typeof(PixelPerfectRulerControl),
            new FrameworkPropertyMetadata(80.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BeatsPerBarProperty =
        DependencyProperty.Register(nameof(BeatsPerBar), typeof(int),
            typeof(PixelPerfectRulerControl),
            new FrameworkPropertyMetadata(4,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public double PixelsPerBeat
    {
        get => (double)GetValue(PixelsPerBeatProperty);
        set => SetValue(PixelsPerBeatProperty, value);
    }

    public int BeatsPerBar
    {
        get => (int)GetValue(BeatsPerBarProperty);
        set => SetValue(BeatsPerBarProperty, value);
    }

    // ── Frozen resources ───────────────────────────────────────────────────────

    private static readonly Brush s_bgBrush  = MakeFrozenBrush(Color.FromRgb(22, 27, 34));
    private static readonly Pen   s_barPen   = MakePen(Color.FromRgb(91, 164, 230), 1.0);
    private static readonly Pen   s_beatPen  = MakePen(Color.FromArgb(100, 136, 153, 170), 1.0);
    private static readonly Brush s_barLabel = MakeFrozenBrush(Color.FromRgb(91, 164, 230));
    private static readonly Typeface s_face  = new("Segoe UI");

    private static Brush MakeFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen MakePen(Color c, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(c), thickness);
        pen.Freeze();
        return pen;
    }

    // ── Constructor ────────────────────────────────────────────────────────────
    
    public PixelPerfectRulerControl()
    {
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var renderSize = RenderSize;
        double width = renderSize.Width;
        double height = renderSize.Height;
        double ppb = PixelsPerBeat;
        int bpb = Math.Max(1, BeatsPerBar);

        if (ppb <= 0 || width <= 0) return;

        // DPI-aware pixel snapping
        var dpiScale = VisualTreeHelper.GetDpi(this);
        double pixelWidth = 1.0 / dpiScale.DpiScaleX;

        dc.DrawRectangle(s_bgBrush, null, new Rect(0, 0, width, height));

        double pixelsPerBar = ppb * bpb;

        // Auto-thin bar labels to prevent overlap
        int labelStep = 1;
        if      (pixelsPerBar < 8)   labelStep = 32;
        else if (pixelsPerBar < 16)  labelStep = 16;
        else if (pixelsPerBar < 32)  labelStep = 8;
        else if (pixelsPerBar < 64)  labelStep = 4;
        else if (pixelsPerBar < 128) labelStep = 2;

        int totalBars = (int)(width / pixelsPerBar) + 2;

        for (int bar = 0; bar < totalBars; bar++)
        {
            // Pixel-snapped bar position
            double barX = Math.Round(bar * pixelsPerBar) + (pixelWidth * 0.5);

            // Major bar tick
            dc.DrawLine(s_barPen, new Point(barX, height * 0.25), new Point(barX, height));

            // Bar number (1-based)
            if (bar % labelStep == 0)
            {
                var ft = new FormattedText(
                    (bar + 1).ToString(),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    s_face,
                    10.0,
                    s_barLabel,
                    dpiScale.PixelsPerDip);

                double textX = Math.Round(barX - ft.Width * 0.5);
                double textY = Math.Round(2.0);
                
                dc.DrawText(ft, new Point(textX, textY));
            }

            // Beat ticks within this bar (if space permits)
            if (ppb >= 8.0)
            {
                for (int beat = 1; beat < bpb; beat++)
                {
                    double beatX = Math.Round(barX + beat * ppb) + (pixelWidth * 0.5);
                    dc.DrawLine(s_beatPen, new Point(beatX, height * 0.5), new Point(beatX, height));
                }
            }
        }
    }
}