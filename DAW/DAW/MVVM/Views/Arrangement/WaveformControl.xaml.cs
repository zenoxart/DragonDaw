using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DAW.MVVM.Views.Arrangement;

/// <summary>
/// A control that renders an audio waveform using optimized Canvas drawing.
/// </summary>
public partial class WaveformControl : UserControl
{
    public static readonly DependencyProperty WaveformDataProperty =
        DependencyProperty.Register(nameof(WaveformData), typeof(double[]), typeof(WaveformControl),
            new PropertyMetadata(null, OnWaveformDataChanged));

    public static readonly DependencyProperty WaveformColorProperty =
        DependencyProperty.Register(nameof(WaveformColor), typeof(Brush), typeof(WaveformControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), OnWaveformColorChanged));

    public WaveformControl()
    {
        InitializeComponent();
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    public double[]? WaveformData
    {
        get => (double[]?)GetValue(WaveformDataProperty);
        set => SetValue(WaveformDataProperty, value);
    }

    public Brush WaveformColor
    {
        get => (Brush)GetValue(WaveformColorProperty);
        set => SetValue(WaveformColorProperty, value);
    }

    private static void OnWaveformDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl control)
            control.RenderWaveform();
    }

    private static void OnWaveformColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl control)
            control.RenderWaveform();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RenderWaveform();
    }

    private void RenderWaveform()
    {
        WaveformCanvas.Children.Clear();

        if (WaveformData == null || WaveformData.Length == 0 || 
            ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var width = ActualWidth;
        var height = ActualHeight;
        var data = WaveformData;
        var centerY = height / 2;

        // Create a single path for the entire waveform for better performance
        var waveformPath = new Path
        {
            Fill = WaveformColor,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };

        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(0, centerY)
        };

        // Calculate pixel-per-sample ratio for proper scaling
        var pixelsPerSample = width / data.Length;
        var previousUpperY = centerY;
        var previousLowerY = centerY;

        // Generate upper envelope
        for (int i = 0; i < data.Length; i++)
        {
            var x = i * pixelsPerSample;
            var amplitude = Math.Min(1.0, Math.Max(0.0, data[i]));
            var scaledAmplitude = amplitude * centerY * 0.85; // 85% of available height for headroom
            
            var upperY = centerY - scaledAmplitude;
            
            // Smooth line connections for better visual quality
            if (i == 0)
            {
                figure.Segments.Add(new LineSegment(new Point(x, upperY), true));
            }
            else
            {
                // Add slight smoothing for visual appeal
                var controlPointX = x - pixelsPerSample / 2;
                var controlPointY = (previousUpperY + upperY) / 2;
                
                if (pixelsPerSample > 2) // Use curves for wide samples
                {
                    figure.Segments.Add(new QuadraticBezierSegment(
                        new Point(controlPointX, controlPointY),
                        new Point(x, upperY), true));
                }
                else
                {
                    figure.Segments.Add(new LineSegment(new Point(x, upperY), true));
                }
            }
            
            previousUpperY = upperY;
        }

        // Connect to the end of the timeline
        figure.Segments.Add(new LineSegment(new Point(width, centerY), true));

        // Generate lower envelope (mirror of upper)
        for (int i = data.Length - 1; i >= 0; i--)
        {
            var x = i * pixelsPerSample;
            var amplitude = Math.Min(1.0, Math.Max(0.0, data[i]));
            var scaledAmplitude = amplitude * centerY * 0.85;
            
            var lowerY = centerY + scaledAmplitude;
            figure.Segments.Add(new LineSegment(new Point(x, lowerY), true));
        }

        // Close the path
        figure.IsClosed = true;
        geometry.Figures.Add(figure);
        waveformPath.Data = geometry;

        WaveformCanvas.Children.Add(waveformPath);

        // Add center line for reference
        var centerLine = new Rectangle
        {
            Width = width,
            Height = 0.5,
            Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            UseLayoutRounding = true,
            SnapsToDevicePixels = true
        };
        
        Canvas.SetLeft(centerLine, 0);
        Canvas.SetTop(centerLine, centerY);
        WaveformCanvas.Children.Add(centerLine);
    }
}