using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.MVVM.ViewModels.PianoRoll;

namespace DAW.MVVM.Views.PianoRoll;

/// <summary>
/// Vertical piano keyboard sidebar for the Piano Roll.
/// Renders 128 MIDI notes top-to-bottom with black/white key colouring,
/// octave labels, and hover/press highlighting.
/// Clicking a key fires a preview note (future MIDI out extension).
/// </summary>
public sealed class PianoKeyboardControl : FrameworkElement
{
    public const double KeyboardWidth = 52;

    private static readonly Color ColWhite  = Color.FromRgb(0xE8, 0xEC, 0xF4);
    private static readonly Color ColBlack  = Color.FromRgb(0x1C, 0x20, 0x2C);
    private static readonly Color ColHover  = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color ColBorder = Color.FromRgb(0x44, 0x4C, 0x5A);
    private static readonly Typeface Tf     = new("Segoe UI");

    private readonly DrawingVisual _visual  = new();
    private int _hoverPitch   = -1;
    private int _pressedPitch = -1;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(PianoRollViewModel),
            typeof(PianoKeyboardControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnViewModelChanged));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (PianoKeyboardControl)d;
        if (e.OldValue is PianoRollViewModel old)
            old.PropertyChanged -= c.OnVmPropertyChanged;
        if (e.NewValue is PianoRollViewModel nvm)
            nvm.PropertyChanged += c.OnVmPropertyChanged;
        c.Render();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PianoRollViewModel.ScrollY)
                           or nameof(PianoRollViewModel.ZoomY))
            Dispatcher.InvokeAsync(Render, DispatcherPriority.Render);
    }

    public PianoRollViewModel? ViewModel
    {
        get => (PianoRollViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public PianoKeyboardControl()
    {
        var vc = new VisualCollection(this);
        vc.Add(_visual);
        Width       = KeyboardWidth;
        ClipToBounds = true;
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;

    private void Render()
    {
        using var dc = _visual.RenderOpen();
        var vm = ViewModel;
        double rh = vm?.RowHeight ?? 14;

        // Keyboard background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x1A)),
            null, new Rect(0, 0, KeyboardWidth, ActualHeight));

        var borderPen  = new Pen(new SolidColorBrush(ColBorder), 0.5);
        var hoverBrush = new SolidColorBrush(ColHover);

        for (int pitch = 127; pitch >= 0; pitch--)
        {
            double y = vm?.PitchToY(pitch) ?? (127 - pitch) * rh;
            if (y + rh < 0 || y > ActualHeight) continue;

            bool isBlack = new[] { 1, 3, 6, 8, 10 }.Contains(pitch % 12);
            bool isC     = pitch % 12 == 0;
            bool isHover = pitch == _hoverPitch;

            Color fill = isHover ? ColHover : (isBlack ? ColBlack : ColWhite);
            double keyW = isBlack ? KeyboardWidth * 0.62 : KeyboardWidth - 1;

            dc.DrawRectangle(new SolidColorBrush(fill), borderPen,
                new Rect(1, y, keyW, rh - 0.5));

            // Octave label on C notes
            if (isC && rh >= 9)
            {
                int octave = pitch / 12 - 1;
                var ft = new FormattedText($"C{octave}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Tf, 7.5,
                    isHover ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x66, 0x70, 0x88)),
                    1.0);
                dc.DrawText(ft, new Point(KeyboardWidth - ft.Width - 3, y + (rh - ft.Height) / 2));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var vm = ViewModel;
        if (vm == null) return;
        int pitch = vm.YToPitch(e.GetPosition(this).Y);
        if (pitch != _hoverPitch)
        {
            // Slide: stop old preview, start new one
            if (_pressedPitch >= 0 && e.LeftButton == MouseButtonState.Pressed && pitch != _pressedPitch)
            {
                vm.FirePreviewStop(_pressedPitch);
                _pressedPitch = pitch;
                vm.FirePreviewStart(pitch);
            }
            _hoverPitch = pitch;
            Render();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_pressedPitch >= 0)
        {
            ViewModel?.FirePreviewStop(_pressedPitch);
            _pressedPitch = -1;
        }
        _hoverPitch = -1;
        Render();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var vm = ViewModel;
        if (vm == null) return;
        _pressedPitch = vm.YToPitch(e.GetPosition(this).Y);
        vm.FirePreviewStart(_pressedPitch);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_pressedPitch >= 0)
        {
            ViewModel?.FirePreviewStop(_pressedPitch);
            _pressedPitch = -1;
        }
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Render();
    }
}
