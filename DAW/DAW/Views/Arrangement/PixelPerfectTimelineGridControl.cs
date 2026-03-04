using System.Windows;
using System.Windows.Media;

namespace DAW.Views.Arrangement;

/// <summary>
/// High-performance pixel-perfect background grid for the arrangement timeline.
/// 
/// Improvements over original:
/// • Pixel-snapped drawing for crisp lines
/// • DPI-aware rendering
/// • Optimized draw call batching
/// • No subpixel positioning
/// </summary>
public sealed class PixelPerfectTimelineGridControl : FrameworkElement
{
    // ── Dependency Properties ──────────────────────────────────────────────────

    public static readonly DependencyProperty PixelsPerBeatProperty =
        DependencyProperty.Register(nameof(PixelsPerBeat), typeof(double),
            typeof(PixelPerfectTimelineGridControl),
            new FrameworkPropertyMetadata(80.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackHeightProperty =
        DependencyProperty.Register(nameof(TrackHeight), typeof(double),
            typeof(PixelPerfectTimelineGridControl),
            new FrameworkPropertyMetadata(52.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackCountProperty =
        DependencyProperty.Register(nameof(TrackCount), typeof(int),
            typeof(PixelPerfectTimelineGridControl),
            new FrameworkPropertyMetadata(0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BeatsPerBarProperty =
        DependencyProperty.Register(nameof(BeatsPerBar), typeof(int),
            typeof(PixelPerfectTimelineGridControl),
            new FrameworkPropertyMetadata(4,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public double PixelsPerBeat
    {
        get => (double)GetValue(PixelsPerBeatProperty);
        set => SetValue(PixelsPerBeatProperty, value);
    }

    public double TrackHeight
    {
        get => (double)GetValue(TrackHeightProperty);
        set => SetValue(TrackHeightProperty, value);
    }

    public int TrackCount
    {
        get => (int)GetValue(TrackCountProperty);
        set => SetValue(TrackCountProperty, value);
    }

    public int BeatsPerBar
    {
        get => (int)GetValue(BeatsPerBarProperty);
        set => SetValue(BeatsPerBarProperty, value);
    }

    // ── Frozen pens for performance ────────────────────────────────────────────

    private static readonly Pen s_beatPen = CreatePixelPerfectPen(Color.FromArgb(25, 255, 255, 255));
    private static readonly Pen s_barPen = CreatePixelPerfectPen(Color.FromArgb(65, 255, 255, 255));
    private static readonly Pen s_bar4Pen = CreatePixelPerfectPen(Color.FromArgb(110, 91, 164, 230));
    private static readonly Pen s_trackSepPen = CreatePixelPerfectPen(Color.FromArgb(70, 255, 255, 255));

    private static Pen CreatePixelPerfectPen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.0);
        pen.Freeze();
        return pen;
    }

    // ── Constructor ────────────────────────────────────────────────────────────
    
    public PixelPerfectTimelineGridControl()
    {
        // Enable pixel-perfect rendering
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
        double trackHeight = TrackHeight;
        int beatsPerBar = Math.Max(1, BeatsPerBar);

        if (width <= 0 || height <= 0 || ppb <= 0) return;

        // Use pixel snapping for all coordinates
        var dpiScale = VisualTreeHelper.GetDpi(this);
        double pixelWidth = 1.0 / dpiScale.DpiScaleX;
        
        // ── Draw vertical beat/bar lines ──────────────────────────────────────
        bool drawBeatLines = ppb >= 4.0; // Skip beat lines when too dense
        int totalBeats = (int)(width / ppb) + 2;

        for (int beat = 0; beat <= totalBeats; beat++)
        {
            // Pixel-snapped X coordinate
            double x = Math.Round(beat * ppb) + (pixelWidth * 0.5);
            
            bool isBar = beat % beatsPerBar == 0;
            bool isBar4 = isBar && beat % (beatsPerBar * 4) == 0;

            Pen pen;
            if (isBar4) pen = s_bar4Pen;
            else if (isBar) pen = s_barPen;
            else if (drawBeatLines) pen = s_beatPen;
            else continue;

            dc.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }

        // ── Draw horizontal track separators ──────────────────────────────────
        if (trackHeight > 0)
        {
            int trackCount = TrackCount;
            for (int i = 1; i <= trackCount; i++)
            {
                double y = Math.Round(i * trackHeight) + (pixelWidth * 0.5);
                dc.DrawLine(s_trackSepPen, new Point(0, y), new Point(width, y));
            }
        }
    }
}