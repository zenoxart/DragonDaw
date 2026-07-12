using System.Windows;
using System.Windows.Media;

namespace DAW.MVVM.Views.Arrangement;

/// <summary>
/// High-performance background grid for the arrangement timeline.
///
/// Rendering strategy: single <see cref="DrawingContext"/> pass — no UIElement overhead
/// per line. Redraws only when a dependency property changes (zoom, track count, etc.).
///
/// Draws:
///   • Vertical lines at every beat, bar, and 4-bar boundary
///   • Horizontal track-separator lines
/// </summary>
public sealed class TimelineGridControl : FrameworkElement
{
    // ── Dependency Properties ──────────────────────────────────────────────────

    public static readonly DependencyProperty PixelsPerBeatProperty =
        DependencyProperty.Register(nameof(PixelsPerBeat), typeof(double),
            typeof(TimelineGridControl),
            new FrameworkPropertyMetadata(80.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackHeightProperty =
        DependencyProperty.Register(nameof(TrackHeight), typeof(double),
            typeof(TimelineGridControl),
            new FrameworkPropertyMetadata(52.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackCountProperty =
        DependencyProperty.Register(nameof(TrackCount), typeof(int),
            typeof(TimelineGridControl),
            new FrameworkPropertyMetadata(0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BeatsPerBarProperty =
        DependencyProperty.Register(nameof(BeatsPerBar), typeof(int),
            typeof(TimelineGridControl),
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

    // ── Frozen pens (created once, never re-allocated) ─────────────────────────

    private static readonly Pen s_beatPen    = MakePen(Color.FromArgb(25,  255, 255, 255), 1.0);
    private static readonly Pen s_barPen     = MakePen(Color.FromArgb(65,  255, 255, 255), 1.0);
    private static readonly Pen s_bar4Pen    = MakePen(Color.FromArgb(110,  91, 164, 230), 1.0);
    private static readonly Pen s_trackSepPen = MakePen(Color.FromArgb(70, 255, 255, 255), 1.0);

    private static Pen MakePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        double w   = ActualWidth;
        double h   = ActualHeight;
        double ppb = PixelsPerBeat;
        double th  = TrackHeight;
        int    bpb = Math.Max(1, BeatsPerBar);

        if (w <= 0 || h <= 0 || ppb <= 0) return;

        // ── Vertical beat / bar lines ────────────────────────────────────────
        // Skip individual beat lines when they would be less than 4 px apart.
        bool drawBeatLines = ppb >= 4.0;
        int  totalBeats    = (int)(w / ppb) + 2;

        for (int beat = 0; beat <= totalBeats; beat++)
        {
            // Snap to pixel centre for crisp 1-px lines.
            double x = Math.Floor(beat * ppb) + 0.5;

            bool isBar  = beat % bpb == 0;
            bool isBar4 = isBar && beat % (bpb * 4) == 0;

            Pen pen;
            if      (isBar4)         pen = s_bar4Pen;
            else if (isBar)          pen = s_barPen;
            else if (drawBeatLines)  pen = s_beatPen;
            else                     continue;

            dc.DrawLine(pen, new Point(x, 0), new Point(x, h));
        }

        // ── Horizontal track separators ──────────────────────────────────────
        if (th > 0)
        {
            int tc = TrackCount;
            for (int i = 1; i <= tc; i++)
            {
                double y = Math.Floor(i * th) + 0.5;
                dc.DrawLine(s_trackSepPen, new Point(0, y), new Point(w, y));
            }
        }
    }
}
