using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DAW.MVVM.Views.Arrangement;

/// <summary>
/// Pixel-perfect scrollable ruler that displays bar numbers and beat tick-marks.
/// Reads theme colors from Application.Current.Resources for dynamic theme support.
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

    private static readonly Typeface s_face = new("Segoe UI");

    public PixelPerfectRulerControl()
    {
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    private static Brush GetBrush(string key) =>
        Application.Current?.Resources[key] as SolidColorBrush ?? Brushes.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        var renderSize = RenderSize;
        double width = renderSize.Width;
        double height = renderSize.Height;
        double ppb = PixelsPerBeat;
        int bpb = Math.Max(1, BeatsPerBar);

        if (ppb <= 0 || width <= 0) return;

        var dpiScale = VisualTreeHelper.GetDpi(this);
        double pixelWidth = 1.0 / dpiScale.DpiScaleX;

        // Theme-aware colors
        var bgBrush = GetBrush("PanelBg");
        var accentBrush = GetBrush("AccentLightBrush");
        var textSecBrush = GetBrush("TextSecondary");
        var barPen = new Pen(accentBrush, 1.0);
        var beatPen = new Pen(textSecBrush, 1.0) { DashStyle = null };
        beatPen.Brush = textSecBrush.Clone();
        ((SolidColorBrush)beatPen.Brush).Opacity = 0.4;

        dc.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

        double pixelsPerBar = ppb * bpb;

        int labelStep = 1;
        if      (pixelsPerBar < 8)   labelStep = 32;
        else if (pixelsPerBar < 16)  labelStep = 16;
        else if (pixelsPerBar < 32)  labelStep = 8;
        else if (pixelsPerBar < 64)  labelStep = 4;
        else if (pixelsPerBar < 128) labelStep = 2;

        int totalBars = (int)(width / pixelsPerBar) + 2;

        for (int bar = 0; bar < totalBars; bar++)
        {
            double barX = Math.Round(bar * pixelsPerBar) + (pixelWidth * 0.5);

            dc.DrawLine(barPen, new Point(barX, height * 0.25), new Point(barX, height));

            if (bar % labelStep == 0)
            {
                var ft = new FormattedText(
                    (bar + 1).ToString(),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    s_face,
                    10.0,
                    accentBrush,
                    dpiScale.PixelsPerDip);

                double textX = Math.Round(barX - ft.Width * 0.5);
                double textY = Math.Round(2.0);
                dc.DrawText(ft, new Point(textX, textY));
            }

            if (ppb >= 8.0)
            {
                for (int beat = 1; beat < bpb; beat++)
                {
                    double beatX = Math.Round(barX + beat * ppb) + (pixelWidth * 0.5);
                    dc.DrawLine(beatPen, new Point(beatX, height * 0.5), new Point(beatX, height));
                }
            }
        }
    }
}