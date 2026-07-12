using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DAW.MVVM.Models;

namespace DAW.Controls;

/// <summary>
/// Custom control for visualizing and editing audio waveforms.
/// Supports zoom, scroll, selection, and marker display.
/// </summary>
public class WaveformEditor : Control
{
    static WaveformEditor()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(WaveformEditor),
            new FrameworkPropertyMetadata(typeof(WaveformEditor)));
    }
    
    public WaveformEditor()
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22));
    }
    
    #region Dependency Properties
    
    public static readonly DependencyProperty WaveformPeaksProperty =
        DependencyProperty.Register(nameof(WaveformPeaks), typeof(float[]), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    
    public float[]? WaveformPeaks
    {
        get => (float[]?)GetValue(WaveformPeaksProperty);
        set => SetValue(WaveformPeaksProperty, value);
    }
    
    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register(nameof(ZoomLevel), typeof(double), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    
    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }
    
    public static readonly DependencyProperty ScrollPositionProperty =
        DependencyProperty.Register(nameof(ScrollPosition), typeof(double), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    
    public double ScrollPosition
    {
        get => (double)GetValue(ScrollPositionProperty);
        set => SetValue(ScrollPositionProperty, value);
    }
    
    public static readonly DependencyProperty PlayheadPositionProperty =
        DependencyProperty.Register(nameof(PlayheadPosition), typeof(double), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    
    public double PlayheadPosition
    {
        get => (double)GetValue(PlayheadPositionProperty);
        set => SetValue(PlayheadPositionProperty, value);
    }
    
    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.Register(nameof(SelectionStart), typeof(double), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    
    public double SelectionStart
    {
        get => (double)GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }
    
    public static readonly DependencyProperty SelectionEndProperty =
        DependencyProperty.Register(nameof(SelectionEnd), typeof(double), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    
    public double SelectionEnd
    {
        get => (double)GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }
    
    public static readonly DependencyProperty LoopStartProperty =
        DependencyProperty.Register(nameof(LoopStart), typeof(double), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    
    public double LoopStart
    {
        get => (double)GetValue(LoopStartProperty);
        set => SetValue(LoopStartProperty, value);
    }
    
    public static readonly DependencyProperty LoopEndProperty =
        DependencyProperty.Register(nameof(LoopEnd), typeof(double), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    
    public double LoopEnd
    {
        get => (double)GetValue(LoopEndProperty);
        set => SetValue(LoopEndProperty, value);
    }
    
    public static readonly DependencyProperty LoopEnabledProperty =
        DependencyProperty.Register(nameof(LoopEnabled), typeof(bool), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    
    public bool LoopEnabled
    {
        get => (bool)GetValue(LoopEnabledProperty);
        set => SetValue(LoopEnabledProperty, value);
    }
    
    public static readonly DependencyProperty WaveformColorProperty =
        DependencyProperty.Register(nameof(WaveformColor), typeof(Color), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(Color.FromRgb(0x26, 0x61, 0x9C), FrameworkPropertyMetadataOptions.AffectsRender));
    
    public Color WaveformColor
    {
        get => (Color)GetValue(WaveformColorProperty);
        set => SetValue(WaveformColorProperty, value);
    }
    
    public static readonly DependencyProperty SelectionColorProperty =
        DependencyProperty.Register(nameof(SelectionColor), typeof(Color), typeof(WaveformEditor),
            new FrameworkPropertyMetadata(Color.FromArgb(0x40, 0x5B, 0xA4, 0xE6), FrameworkPropertyMetadataOptions.AffectsRender));
    
    public Color SelectionColor
    {
        get => (Color)GetValue(SelectionColorProperty);
        set => SetValue(SelectionColorProperty, value);
    }
    
    #endregion
    
    #region Rendering
    
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        
        // Background
        dc.DrawRectangle(Background, null, bounds);
        
        // Draw center line
        var centerLinePen = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), 1);
        dc.DrawLine(centerLinePen, new Point(0, ActualHeight / 2), new Point(ActualWidth, ActualHeight / 2));
        
        // Draw selection
        if (SelectionEnd > SelectionStart)
        {
            DrawSelection(dc);
        }
        
        // Draw loop region
        if (LoopEnabled)
        {
            DrawLoopRegion(dc);
        }
        
        // Draw waveform
        if (WaveformPeaks != null && WaveformPeaks.Length > 0)
        {
            DrawWaveform(dc);
        }
        else
        {
            DrawPlaceholder(dc);
        }
        
        // Draw playhead
        DrawPlayhead(dc);
    }
    
    private void DrawWaveform(DrawingContext dc)
    {
        var peaks = WaveformPeaks!;
        double width = ActualWidth;
        double height = ActualHeight;
        double centerY = height / 2;
        
        // Calculate visible range based on zoom and scroll
        double visibleRatio = 1.0 / ZoomLevel;
        int startIndex = (int)(ScrollPosition * peaks.Length);
        int visiblePeaks = (int)(peaks.Length * visibleRatio);
        int endIndex = Math.Min(startIndex + visiblePeaks, peaks.Length);
        
        if (visiblePeaks == 0) return;
        
        double pixelsPerPeak = width / visiblePeaks;
        
        var waveformBrush = new SolidColorBrush(WaveformColor);
        var geometry = new StreamGeometry();
        
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, centerY), true, true);
            
            // Upper half
            for (int i = 0; i < visiblePeaks && (startIndex + i) < endIndex; i++)
            {
                float peak = peaks[startIndex + i];
                double x = i * pixelsPerPeak;
                double y = centerY - (peak * centerY * 0.9);
                ctx.LineTo(new Point(x, y), true, false);
            }
            
            // Lower half (mirror)
            for (int i = visiblePeaks - 1; i >= 0 && (startIndex + i) < endIndex; i--)
            {
                float peak = peaks[Math.Min(startIndex + i, peaks.Length - 1)];
                double x = i * pixelsPerPeak;
                double y = centerY + (peak * centerY * 0.9);
                ctx.LineTo(new Point(x, y), true, false);
            }
        }
        
        geometry.Freeze();
        dc.DrawGeometry(waveformBrush, null, geometry);
    }
    
    private void DrawSelection(DrawingContext dc)
    {
        double startX = PositionToX(SelectionStart);
        double endX = PositionToX(SelectionEnd);
        
        var selectionBrush = new SolidColorBrush(SelectionColor);
        dc.DrawRectangle(selectionBrush, null, new Rect(startX, 0, endX - startX, ActualHeight));
    }
    
    private void DrawLoopRegion(DrawingContext dc)
    {
        double startX = PositionToX(LoopStart);
        double endX = PositionToX(LoopEnd);
        
        var loopBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xA5, 0x00));
        var loopPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)), 2);
        
        dc.DrawRectangle(loopBrush, null, new Rect(startX, 0, endX - startX, ActualHeight));
        dc.DrawLine(loopPen, new Point(startX, 0), new Point(startX, ActualHeight));
        dc.DrawLine(loopPen, new Point(endX, 0), new Point(endX, ActualHeight));
    }
    
    private void DrawPlayhead(DrawingContext dc)
    {
        double x = PositionToX(PlayheadPosition);
        var playheadPen = new Pen(new SolidColorBrush(Colors.White), 2);
        
        dc.DrawLine(playheadPen, new Point(x, 0), new Point(x, ActualHeight));
        
        // Playhead triangle
        var triangleGeometry = new StreamGeometry();
        using (var ctx = triangleGeometry.Open())
        {
            ctx.BeginFigure(new Point(x - 6, 0), true, true);
            ctx.LineTo(new Point(x + 6, 0), true, false);
            ctx.LineTo(new Point(x, 10), true, false);
        }
        triangleGeometry.Freeze();
        dc.DrawGeometry(Brushes.White, null, triangleGeometry);
    }
    
    private void DrawPlaceholder(DrawingContext dc)
    {
        var text = new FormattedText(
            "Keine Waveform geladen",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            14,
            new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        
        double x = (ActualWidth - text.Width) / 2;
        double y = (ActualHeight - text.Height) / 2;
        dc.DrawText(text, new Point(x, y));
    }
    
    private double PositionToX(double position)
    {
        double visibleRatio = 1.0 / ZoomLevel;
        double relativePosition = (position - ScrollPosition) / visibleRatio;
        return relativePosition * ActualWidth;
    }
    
    private double XToPosition(double x)
    {
        double visibleRatio = 1.0 / ZoomLevel;
        double relativePosition = x / ActualWidth;
        return ScrollPosition + (relativePosition * visibleRatio);
    }
    
    #endregion
    
    #region Mouse Interaction
    
    private bool _isDragging;
    private Point _dragStart;
    private DragMode _dragMode;
    
    private enum DragMode
    {
        None,
        Selection,
        Playhead,
        LoopStart,
        LoopEnd,
        Pan
    }
    
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        
        _dragStart = e.GetPosition(this);
        double position = XToPosition(_dragStart.X);
        
        // Check if clicking on loop markers
        if (LoopEnabled)
        {
            double loopStartX = PositionToX(LoopStart);
            double loopEndX = PositionToX(LoopEnd);
            
            if (Math.Abs(_dragStart.X - loopStartX) < 8)
            {
                _dragMode = DragMode.LoopStart;
                _isDragging = true;
                CaptureMouse();
                return;
            }
            if (Math.Abs(_dragStart.X - loopEndX) < 8)
            {
                _dragMode = DragMode.LoopEnd;
                _isDragging = true;
                CaptureMouse();
                return;
            }
        }
        
        // Start selection
        _dragMode = DragMode.Selection;
        _isDragging = true;
        SelectionStart = position;
        SelectionEnd = position;
        CaptureMouse();
    }
    
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        
        if (!_isDragging) return;
        
        var currentPos = e.GetPosition(this);
        double position = Math.Clamp(XToPosition(currentPos.X), 0, 1);
        
        switch (_dragMode)
        {
            case DragMode.Selection:
                double startPos = XToPosition(_dragStart.X);
                if (position < startPos)
                {
                    SelectionStart = position;
                    SelectionEnd = startPos;
                }
                else
                {
                    SelectionStart = startPos;
                    SelectionEnd = position;
                }
                break;
                
            case DragMode.LoopStart:
                LoopStart = Math.Min(position, LoopEnd - 0.001);
                break;
                
            case DragMode.LoopEnd:
                LoopEnd = Math.Max(position, LoopStart + 0.001);
                break;
        }
        
        InvalidateVisual();
    }
    
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        
        if (_isDragging)
        {
            _isDragging = false;
            _dragMode = DragMode.None;
            ReleaseMouseCapture();
        }
    }
    
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        
        // Zoom with Ctrl+Wheel
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double zoomFactor = e.Delta > 0 ? 1.2 : 1 / 1.2;
            
            // Zoom centered on mouse position
            var mousePos = e.GetPosition(this);
            double mouseRatio = mousePos.X / ActualWidth;
            double oldPosition = XToPosition(mousePos.X);
            
            ZoomLevel = Math.Clamp(ZoomLevel * zoomFactor, 0.1, 100);
            
            // Adjust scroll to keep mouse position stable
            double visibleRatio = 1.0 / ZoomLevel;
            ScrollPosition = Math.Clamp(oldPosition - (mouseRatio * visibleRatio), 0, 1 - visibleRatio);
            
            InvalidateVisual();
            e.Handled = true;
        }
        // Horizontal scroll with Shift+Wheel
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            double scrollDelta = e.Delta > 0 ? -0.05 : 0.05;
            double visibleRatio = 1.0 / ZoomLevel;
            ScrollPosition = Math.Clamp(ScrollPosition + scrollDelta, 0, 1 - visibleRatio);
            InvalidateVisual();
            e.Handled = true;
        }
    }
    
    #endregion
}
