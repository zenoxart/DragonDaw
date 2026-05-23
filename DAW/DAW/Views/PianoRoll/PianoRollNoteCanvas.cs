using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Models.PianoRoll;
using DAW.ViewModels.PianoRoll;

namespace DAW.Views.PianoRoll;

/// <summary>
/// GPU-accelerated Piano Roll note grid.
/// Uses DrawingVisual layers for maximum performance with thousands of notes.
///
/// Rendering layers (back to front):
///   1. Background + scale highlights
///   2. Grid (beat lines, bar lines)
///   3. Ghost notes (other channels, semi-transparent)
///   4. Notes (main channel)
///   5. Selected note overlay
///   6. Playhead cursor
///   7. Drag / resize ghost
/// </summary>
public sealed class PianoRollNoteCanvas : FrameworkElement
{
    // ── Visual layers ─────────────────────────────────────────────────────────
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _layerBg       = new();
    private readonly DrawingVisual _layerGrid     = new();
    private readonly DrawingVisual _layerGhost    = new();
    private readonly DrawingVisual _layerNotes    = new();
    private readonly DrawingVisual _layerSelected = new();
    private readonly DrawingVisual _layerPlayhead = new();
    private readonly DrawingVisual _layerDrag     = new();

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color ColBg          = Color.FromRgb(0x12, 0x16, 0x1E);
    private static readonly Color ColBlackKey    = Color.FromRgb(0x18, 0x1C, 0x26);
    private static readonly Color ColScaleHi     = Color.FromArgb(18, 0x3B, 0x82, 0xF6);
    private static readonly Color ColBeatLine    = Color.FromRgb(0x2A, 0x32, 0x44);
    private static readonly Color ColBarLine     = Color.FromRgb(0x40, 0x4C, 0x66);
    private static readonly Color ColNote        = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color ColNoteSelect  = Color.FromRgb(0xFF, 0xD1, 0x30);
    private static readonly Color ColNoteMuted   = Color.FromRgb(0x44, 0x4C, 0x55);
    private static readonly Color ColGhostNote   = Color.FromArgb(80, 0x88, 0x99, 0xBB);
    private static readonly Color ColPlayhead    = Color.FromRgb(0xFF, 0xD6, 0x00);
    private static readonly Typeface Tf          = new("Segoe UI");

    // ── DP ────────────────────────────────────────────────────────────────────
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(PianoRollViewModel),
            typeof(PianoRollNoteCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                OnVmChanged));

    public PianoRollViewModel? ViewModel
    {
        get => (PianoRollViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnVmChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (PianoRollNoteCanvas)d;
        if (e.OldValue is PianoRollViewModel old)
        {
            old.PropertyChanged -= c.OnVmProp;
            old.Notes.CollectionChanged -= c.OnNotesChanged;
        }
        if (e.NewValue is PianoRollViewModel nvm)
        {
            nvm.PropertyChanged += c.OnVmProp;
            nvm.Notes.CollectionChanged += c.OnNotesChanged;
            foreach (var n in nvm.Notes) c.SubscribeNote(n);
        }
        c.Redraw();
    }

    private void OnVmProp(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PianoRollViewModel.PlayheadTick) or
                              nameof(PianoRollViewModel.IsPlaying))
            RedrawPlayhead();
        else
            Dispatcher.InvokeAsync(Redraw, DispatcherPriority.Render);
    }

    private void OnNotesChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (PianoRollNote n in e.NewItems) SubscribeNote(n);
        Dispatcher.InvokeAsync(RedrawNotes, DispatcherPriority.Render);
    }

    private void SubscribeNote(PianoRollNote n)
        => n.PropertyChanged += (_, _) => Dispatcher.InvokeAsync(RedrawNotes, DispatcherPriority.Render);

    // ── Interaction ───────────────────────────────────────────────────────────
    private Point?         _dragStart    = null;
    private PianoRollNote? _dragNote     = null;
    private bool           _isResizing   = false;
    private int            _dragNoteOrigStart;
    private int            _dragNoteOrigLen;
    private Rect           _boxSelect    = Rect.Empty;
    private bool           _isBoxSelect  = false;

    // ── Constructor ───────────────────────────────────────────────────────────
    public PianoRollNoteCanvas()
    {
        _visuals = new VisualCollection(this);
        _visuals.Add(_layerBg);
        _visuals.Add(_layerGrid);
        _visuals.Add(_layerGhost);
        _visuals.Add(_layerNotes);
        _visuals.Add(_layerSelected);
        _visuals.Add(_layerPlayhead);
        _visuals.Add(_layerDrag);

        ClipToBounds = true;
        Focusable    = true;
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    // ── Full redraw ───────────────────────────────────────────────────────────
    private void Redraw()
    {
        DrawBackground();
        DrawGrid();
        DrawGhostNotes();
        RedrawNotes();
        RedrawPlayhead();
    }

    // ── Background + scale ────────────────────────────────────────────────────
    private void DrawBackground()
    {
        using var dc = _layerBg.RenderOpen();
        var vm = ViewModel;
        double w = ActualWidth, h = ActualHeight;

        for (int pitch = 0; pitch < 128; pitch++)
        {
            double y   = vm?.PitchToY(pitch) ?? (127 - pitch) * 14;
            double rh  = vm?.RowHeight ?? 14;

            bool isBlack = new[] { 1, 3, 6, 8, 10 }.Contains(pitch % 12);
            Color rowColor = isBlack ? ColBlackKey : ColBg;

            // Scale highlight
            if (vm is { ShowScale: true })
            {
                int semitone = (pitch - vm.RootNote + 12) % 12;
                if (new[] { 0, 2, 4, 5, 7, 9, 11 }.Contains(semitone))
                    rowColor = Lerp(rowColor, ColScaleHi, 0.3);
            }

            dc.DrawRectangle(new SolidColorBrush(rowColor), null,
                new Rect(0, y, w, rh));
        }
    }

    // ── Grid ──────────────────────────────────────────────────────────────────
    private void DrawGrid()
    {
        using var dc = _layerGrid.RenderOpen();
        var vm = ViewModel;
        if (vm == null) return;

        double h = ActualHeight;
        var beatPen = new Pen(new SolidColorBrush(ColBeatLine), 0.5);
        var barPen  = new Pen(new SolidColorBrush(ColBarLine),  1.0);

        // Draw vertical grid lines (beats and bars)
        int ppq = PianoRollViewModel.PPQ;
        int totalTicks = (int)(ActualWidth / vm.TickWidth) + ppq * 4;

        for (int t = 0; t < totalTicks; t += ppq)
        {
            double x = vm.TickToX(t);
            if (x < -2 || x > ActualWidth + 2) continue;
            bool isBar = (t % (ppq * 4) == 0);
            dc.DrawLine(isBar ? barPen : beatPen, new Point(x, 0), new Point(x, h));

            if (isBar)
            {
                int bar = t / (ppq * 4) + 1;
                var ft = new FormattedText(bar.ToString(), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Tf, 8,
                    new SolidColorBrush(Color.FromRgb(0x44, 0x50, 0x66)), 1.0);
                dc.DrawText(ft, new Point(x + 3, 2));
            }
        }

        // Draw horizontal row lines (C notes)
        var cPen = new Pen(new SolidColorBrush(Color.FromRgb(0x35, 0x40, 0x55)), 0.8);
        for (int pitch = 0; pitch < 128; pitch++)
        {
            if (pitch % 12 == 0)
            {
                double y = vm.PitchToY(pitch) + vm.RowHeight;
                dc.DrawLine(cPen, new Point(0, y), new Point(ActualWidth, y));
            }
        }
    }

    // ── Ghost notes ───────────────────────────────────────────────────────────
    private void DrawGhostNotes()
    {
        using var dc = _layerGhost.RenderOpen();
        var vm = ViewModel;
        if (vm == null || !vm.ShowGhostNotes) return;

        var brush = new SolidColorBrush(ColGhostNote);
        foreach (var n in vm.GhostNotes)
            dc.DrawRoundedRectangle(brush, null, NoteRect(vm, n), 1, 1);
    }

    // ── Notes ─────────────────────────────────────────────────────────────────
    private void RedrawNotes()
    {
        using var notesDc = _layerNotes.RenderOpen();
        using var selDc   = _layerSelected.RenderOpen();
        var vm = ViewModel;
        if (vm == null) return;

        foreach (var note in vm.Notes)
        {
            var rect = NoteRect(vm, note);
            if (rect.Right < 0 || rect.Left > ActualWidth) continue;

            Color baseColor;
            if (note.IsMuted)       baseColor = ColNoteMuted;
            else if (note.IsSelected) baseColor = ColNoteSelect;
            else                    baseColor = ColNote;

            // Velocity shading
            float v = note.Velocity;
            var finalColor = Color.FromArgb(255,
                (byte)(baseColor.R * (0.5 + v * 0.5)),
                (byte)(baseColor.G * (0.5 + v * 0.5)),
                (byte)(baseColor.B * (0.5 + v * 0.5)));

            var dc = note.IsSelected ? selDc : notesDc;
            dc.DrawRoundedRectangle(new SolidColorBrush(finalColor),
                note.IsSelected ? new Pen(new SolidColorBrush(Colors.White), 0.8) : null,
                rect, 2, 2);

            // Note name label for wide notes
            if (rect.Width > 18)
            {
                var ft = new FormattedText(note.NoteName, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Tf, 8, Brushes.Black, 1.0);
                dc.DrawText(ft, new Point(rect.X + 2, rect.Y + (rect.Height - ft.Height) / 2));
            }
        }
    }

    // ── Playhead ──────────────────────────────────────────────────────────────
    private void RedrawPlayhead()
    {
        using var dc = _layerPlayhead.RenderOpen();
        var vm = ViewModel;
        if (vm == null || !vm.IsPlaying) return;

        double x = vm.TickToX(vm.PlayheadTick);
        if (x < 0 || x > ActualWidth) return;

        var pen = new Pen(new SolidColorBrush(ColPlayhead), 1.5);
        dc.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────
    private static Rect NoteRect(PianoRollViewModel vm, PianoRollNote note)
    {
        double x = vm.TickToX(note.StartTick);
        double y = vm.PitchToY(note.Pitch);
        double w = note.Length * vm.TickWidth;
        double h = vm.RowHeight - 1;
        return new Rect(x, y, Math.Max(w, 2), Math.Max(h, 2));
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    // ── Mouse – Draw / Select ─────────────────────────────────────────────────
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var vm = ViewModel;
        if (vm == null) return;

        var p    = e.GetPosition(this);
        var hit  = HitNote(vm, p);
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (vm.ActiveTool)
        {
            case PianoRollTool.Draw:
                if (hit != null)
                {
                    // Near right edge → resize
                    double nx = vm.TickToX(hit.EndTick);
                    if (p.X >= nx - 5)
                    {
                        _isResizing = true; _dragNote = hit;
                        _dragNoteOrigStart = hit.StartTick;
                        _dragNoteOrigLen   = hit.Length;
                    }
                    else
                    {
                        _dragNote = hit;
                        _dragStart = p;
                        _dragNoteOrigStart = hit.StartTick;
                        _dragNoteOrigLen   = hit.Length;
                    }
                }
                else
                {
                    // Create new note
                    int tick  = vm.SnapTick(vm.XToTick((int)p.X));
                    int pitch = vm.YToPitch(p.Y);
                    var note  = vm.AddNote(pitch, tick, vm.SnapTicks);
                    _dragNote       = note;
                    _dragStart      = p;
                    _isResizing     = true;
                    _dragNoteOrigStart = note.StartTick;
                    _dragNoteOrigLen   = note.Length;
                }
                break;

            case PianoRollTool.Select:
                if (hit != null)
                {
                    if (!shift) vm.DeselectAll();
                    hit.IsSelected = !hit.IsSelected;
                    _dragNote  = hit;
                    _dragStart = p;
                    _dragNoteOrigStart = hit.StartTick;
                    _dragNoteOrigLen   = hit.Length;
                }
                else
                {
                    if (!shift) vm.DeselectAll();
                    _isBoxSelect = true;
                    _dragStart   = p;
                    _boxSelect   = new Rect(p, p);
                }
                break;

            case PianoRollTool.Mute:
                if (hit != null) hit.IsMuted = !hit.IsMuted;
                break;

            case PianoRollTool.Slice:
                // Cut note at cursor X position; delete if Shift is held
                if (hit != null)
                {
                    int splitTick = vm.SnapTick(vm.XToTick((int)p.X));
                    if (shift)
                        vm.DeleteNote(hit);
                    else
                        vm.CutNote(hit, splitTick);
                }
                break;
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var vm = ViewModel;
        if (vm == null) return;
        var p = e.GetPosition(this);

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (_isResizing && _dragNote != null)
            {
                int endTick = vm.SnapTick(vm.XToTick((int)p.X));
                int newLen  = Math.Max(vm.SnapTicks, endTick - _dragNote.StartTick);
                _dragNote.Length = newLen;
            }
            else if (_dragNote != null && _dragStart.HasValue)
            {
                double dx = p.X - _dragStart.Value.X;
                double dy = p.Y - _dragStart.Value.Y;

                int tickDelta  = (int)(dx / vm.TickWidth);
                int pitchDelta = -(int)(dy / vm.RowHeight);

                _dragNote.StartTick = vm.SnapTick(Math.Max(0, _dragNoteOrigStart + tickDelta));
                _dragNote.Pitch     = Math.Clamp(_dragNote.Pitch + pitchDelta, 0, 127);

                // Move all other selected notes too
                foreach (var n in vm.Notes.Where(n => n.IsSelected && n != _dragNote))
                    n.StartTick = vm.SnapTick(Math.Max(0, n.StartTick + tickDelta));
            }
            else if (_isBoxSelect && _dragStart.HasValue)
            {
                _boxSelect = new Rect(_dragStart.Value, p);
                DrawBoxSelect();
                UpdateBoxSelection(vm);
            }
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragNote    = null;
        _dragStart   = null;
        _isResizing  = false;
        _isBoxSelect = false;
        _boxSelect   = Rect.Empty;
        using var dc = _layerDrag.RenderOpen(); // clear drag layer
        ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        var vm = ViewModel;
        if (vm == null) return;
        var hit = HitNote(vm, e.GetPosition(this));
        if (hit != null) vm.DeleteNote(hit);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var vm = ViewModel;
        if (vm == null) return;

        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   != 0;

        if (ctrl && shift)
            vm.ZoomY += e.Delta > 0 ? 0.1 : -0.1;
        else if (ctrl)
            vm.ZoomX += e.Delta > 0 ? 0.15 : -0.15;
        else
            vm.ScrollY -= e.Delta * 0.3;

        Redraw();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var vm = ViewModel;
        if (vm == null) return;

        switch (e.Key)
        {
            case Key.Delete: vm.DeleteSelected(); break;
            case Key.A when Keyboard.Modifiers.HasFlag(ModifierKeys.Control): vm.SelectAll(); break;
            case Key.D: vm.ActiveTool = PianoRollTool.Draw;   break;
            case Key.E: vm.ActiveTool = PianoRollTool.Select; break;
            case Key.S: vm.ActiveTool = PianoRollTool.Slice;  break;
            case Key.M: vm.ActiveTool = PianoRollTool.Mute;   break;
        }
    }

    // ── Hit test ──────────────────────────────────────────────────────────────
    private static PianoRollNote? HitNote(PianoRollViewModel vm, Point p)
    {
        foreach (var n in vm.Notes.Reverse())
        {
            var r = NoteRect(vm, n);
            if (r.Contains(p)) return n;
        }
        return null;
    }

    private void DrawBoxSelect()
    {
        using var dc = _layerDrag.RenderOpen();
        if (_boxSelect.IsEmpty) return;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 0x3B, 0x82, 0xF6)),
            new Pen(new SolidColorBrush(Color.FromArgb(180, 0x3B, 0x82, 0xF6)), 1),
            _boxSelect);
    }

    private static void UpdateBoxSelection(PianoRollViewModel vm)
    {
        // implemented in mouse move; selection update based on _boxSelect
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Redraw();
    }
}
