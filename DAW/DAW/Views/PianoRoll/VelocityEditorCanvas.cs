using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DAW.Models.PianoRoll;
using DAW.ViewModels.PianoRoll;

namespace DAW.Views.PianoRoll;

/// <summary>
/// Velocity editor bar in the lower Piano Roll panel.
/// Each active note shows a vertical bar; dragging adjusts the velocity.
/// Supports Velocity, Pan, and Release display modes.
/// </summary>
public sealed class VelocityEditorCanvas : FrameworkElement
{
    private static readonly Color ColBar     = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color ColBarSel  = Color.FromRgb(0xFF, 0xD1, 0x30);
    private static readonly Color ColBarBg   = Color.FromRgb(0x0E, 0x12, 0x1A);
    private static readonly Color ColLine    = Color.FromRgb(0x28, 0x32, 0x44);

    private readonly DrawingVisual _visual = new();

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(PianoRollViewModel),
            typeof(VelocityEditorCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnVmChanged));

    public PianoRollViewModel? ViewModel
    {
        get => (PianoRollViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnVmChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (VelocityEditorCanvas)d;
        if (e.OldValue is PianoRollViewModel old)
        { old.PropertyChanged -= c.OnVmProp; old.Notes.CollectionChanged -= c.OnNotesChanged; }
        if (e.NewValue is PianoRollViewModel nvm)
        { nvm.PropertyChanged += c.OnVmProp; nvm.Notes.CollectionChanged += c.OnNotesChanged; }
        c.Render();
    }

    private void OnVmProp(object? s, System.ComponentModel.PropertyChangedEventArgs e) => Render();
    private void OnNotesChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Render();

    public VelocityEditorCanvas()
    {
        var vc = new VisualCollection(this);
        vc.Add(_visual);
        ClipToBounds = true;
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;

    private void Render()
    {
        using var dc = _visual.RenderOpen();
        var vm = ViewModel;
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(new SolidColorBrush(ColBarBg), null, new Rect(0, 0, w, h));
        if (vm == null) return;

        // Horizontal centre line for Pan / 50% reference
        var refPen = new Pen(new SolidColorBrush(ColLine), 0.8);
        dc.DrawLine(refPen, new Point(0, h / 2), new Point(w, h / 2));

        foreach (var note in vm.Notes)
        {
            double x   = vm.TickToX(note.StartTick);
            double bw  = Math.Max(4, note.Length * vm.TickWidth - 2);
            if (x + bw < 0 || x > w) continue;

            float value = vm.VelocityEditorParam switch
            {
                PianoRollViewModel.VelocityParam.Pan     => (note.Pan + 1f) / 2f,
                PianoRollViewModel.VelocityParam.Release => note.Release,
                _                                        => note.Velocity
            };

            double barH = Math.Max(0, value * (h - 4));
            if (barH <= 0) continue;
            double by   = h - barH - 2;

            Color barColor = note.IsSelected ? ColBarSel : ColBar;
            barColor = Color.FromArgb(200, barColor.R, barColor.G, barColor.B);

            dc.DrawRoundedRectangle(new SolidColorBrush(barColor), null,
                new Rect(x, by, bw, barH), 1, 1);
        }
    }

    // ── Mouse interaction: drag to change velocity ────────────────────────────
    private PianoRollNote? _dragNote = null;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var vm = ViewModel;
        if (vm == null) return;

        var p = e.GetPosition(this);
        _dragNote = HitNote(vm, p);
        if (_dragNote != null)
        {
            ApplyValue(vm, _dragNote, p);
            CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var vm = ViewModel;
        if (vm == null || _dragNote == null || e.LeftButton != MouseButtonState.Pressed) return;
        ApplyValue(vm, _dragNote, e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragNote = null;
        ReleaseMouseCapture();
    }

    private void ApplyValue(PianoRollViewModel vm, PianoRollNote note, Point p)
    {
        float v = (float)Math.Clamp(1.0 - p.Y / ActualHeight, 0, 1);
        switch (vm.VelocityEditorParam)
        {
            case PianoRollViewModel.VelocityParam.Pan:     note.Pan     = v * 2f - 1f; break;
            case PianoRollViewModel.VelocityParam.Release: note.Release = v; break;
            default:                                        note.Velocity = v; break;
        }
        Render();
    }

    private static PianoRollNote? HitNote(PianoRollViewModel vm, Point p)
    {
        foreach (var n in vm.Notes)
        {
            double x = vm.TickToX(n.StartTick);
            double bw = Math.Max(4, n.Length * vm.TickWidth - 2);
            if (p.X >= x && p.X <= x + bw) return n;
        }
        return null;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Render();
    }
}
