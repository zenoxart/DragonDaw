using System.Windows;
using System.Windows.Media;

namespace DAW.MVVM.Views.Arrangement;

/// <summary>
/// High-performance pixel-perfect background grid for the arrangement timeline.
/// Reads theme colors dynamically from Application.Current.Resources.
/// </summary>
public sealed class PixelPerfectTimelineGridControl : FrameworkElement
{
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

    public PixelPerfectTimelineGridControl()
    {
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    private static Color GetColor(string key)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush b)
            return b.Color;
        return Colors.Gray;
    }

    private static Pen MakePen(Color c, byte alpha)
    {
        var color = Color.FromArgb(alpha, c.R, c.G, c.B);
        return new Pen(new SolidColorBrush(color), 1.0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var renderSize = RenderSize;
        double width = renderSize.Width;
        double height = renderSize.Height;
        double ppb = PixelsPerBeat;
        double trackHeight = TrackHeight;
        int beatsPerBar = Math.Max(1, BeatsPerBar);

        if (width <= 0 || height <= 0 || ppb <= 0) return;

        var dpiScale = VisualTreeHelper.GetDpi(this);
        double pixelWidth = 1.0 / dpiScale.DpiScaleX;

        // Theme-adaptive grid lines
        var borderColor = GetColor("BorderBrush");
        var accentColor = GetColor("AccentLightBrush");
        var beatPen = MakePen(borderColor, 25);
        var barPen = MakePen(borderColor, 65);
        var bar4Pen = MakePen(accentColor, 110);
        var trackSepPen = MakePen(borderColor, 70);

        bool drawBeatLines = ppb >= 4.0;
        int totalBeats = (int)(width / ppb) + 2;

        for (int beat = 0; beat <= totalBeats; beat++)
        {
            double x = Math.Round(beat * ppb) + (pixelWidth * 0.5);

            bool isBar = beat % beatsPerBar == 0;
            bool isBar4 = isBar && beat % (beatsPerBar * 4) == 0;

            Pen pen;
            if (isBar4) pen = bar4Pen;
            else if (isBar) pen = barPen;
            else if (drawBeatLines) pen = beatPen;
            else continue;

            dc.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }

        if (trackHeight > 0)
        {
            int trackCount = TrackCount;
            for (int i = 1; i <= trackCount; i++)
            {
                double y = Math.Round(i * trackHeight) + (pixelWidth * 0.5);
                dc.DrawLine(trackSepPen, new Point(0, y), new Point(width, y));
            }
        }
    }
}