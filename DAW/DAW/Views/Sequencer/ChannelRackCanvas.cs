using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.ViewModels.Sequencer;

namespace DAW.Views.Sequencer;

/// <summary>
/// Custom-rendered Channel Rack step grid.
///
/// New features in this version:
///   • Right-click on channel strip → ContextMenu with "Open in Piano Roll"
///   • Drag-and-drop audio files onto channel rows (DragEnter / Drop)
///   • Drop onto empty area below channels → create new channel
/// </summary>
public sealed class ChannelRackCanvas : FrameworkElement
{
    // ── Visuals ───────────────────────────────────────────────────────────────
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _background = new();
    private readonly DrawingVisual _grid       = new();
    private readonly DrawingVisual _steps      = new();
    private readonly DrawingVisual _playhead   = new();
    private readonly DrawingVisual _overlay    = new();

    // ── Layout ────────────────────────────────────────────────────────────────
    public const double StripW  = 190;
    public const double CellH   = 26;
    public const double CellW   = 24;
    public const double CellPad = 2;
    public const double HeaderH = 24;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color ColBg        = Color.FromRgb(0x1A, 0x1E, 0x26);
    private static readonly Color ColStrip     = Color.FromRgb(0x12, 0x15, 0x1C);
    private static readonly Color ColGrid      = Color.FromRgb(0x28, 0x2E, 0x3C);
    private static readonly Color ColGridGroup = Color.FromRgb(0x38, 0x42, 0x54);
    private static readonly Color ColStepOff   = Color.FromRgb(0x22, 0x28, 0x34);
    private static readonly Color ColPlayhead  = Color.FromRgb(0xFF, 0xD6, 0x00);
    private static readonly Color ColText      = Color.FromRgb(0xCC, 0xCC, 0xCC);
    private static readonly Color ColTextDim   = Color.FromRgb(0x66, 0x70, 0x80);
    private static readonly Color ColDropHi    = Color.FromArgb(60, 0x3B, 0x82, 0xF6);

    private static readonly Typeface Tf = new("Segoe UI");

    // ── DP ────────────────────────────────────────────────────────────────────
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(PatternViewModel),
            typeof(ChannelRackCanvas),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender, OnVmChanged));

    public PatternViewModel? ViewModel
    {
        get => (PatternViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnVmChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ChannelRackCanvas)d;
        if (e.OldValue is PatternViewModel old)
        {
            old.PropertyChanged -= c.OnVmProp;
            old.Channels.CollectionChanged -= c.OnChannelsChanged;
        }
        if (e.NewValue is PatternViewModel nvm)
        {
            nvm.PropertyChanged += c.OnVmProp;
            nvm.Channels.CollectionChanged += c.OnChannelsChanged;
            foreach (var ch in nvm.Channels) c.SubscribeChannel(ch);
        }
        c.Redraw();
    }

    private void OnVmProp(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PatternViewModel.CurrentStep)
                           or nameof(PatternViewModel.IsPlaying))
            RedrawPlayhead();
        else
        {
            InvalidateMeasure();
            Dispatcher.InvokeAsync(Redraw, DispatcherPriority.Render);
        }
    }

    private void OnChannelsChanged(object? s,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (ChannelViewModel ch in e.NewItems) SubscribeChannel(ch);
        Dispatcher.InvokeAsync(Redraw, DispatcherPriority.Render);
    }

    private void SubscribeChannel(ChannelViewModel ch)
    {
        ch.PropertyChanged += (_, _) =>
            Dispatcher.InvokeAsync(Redraw, DispatcherPriority.Render);
        foreach (var sv in ch.Steps)
            sv.PropertyChanged += (_, _) =>
                Dispatcher.InvokeAsync(Redraw, DispatcherPriority.Render);
    }

    // ── Interaction state ─────────────────────────────────────────────────────
    private (int ch, int step) _hoverCell  = (-1, -1);
    private int                _hoverRow   = -1;   // strip hover (for context menu)
    private bool               _isPainting = false;
    private bool               _paintValue = true;
    private int                _dropTargetRow = -1; // drag-over highlight

    // ── Constructor ───────────────────────────────────────────────────────────
    public ChannelRackCanvas()
    {
        _visuals = new VisualCollection(this);
        _visuals.Add(_background);
        _visuals.Add(_grid);
        _visuals.Add(_steps);
        _visuals.Add(_playhead);
        _visuals.Add(_overlay);

        ClipToBounds = AllowDrop = Focusable = true;
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size av)
    {
        double w = StripW + (ViewModel?.ActivePattern?.StepCount ?? 16) * (CellW + CellPad);
        double h = HeaderH + (ViewModel?.Channels.Count ?? 0) * CellH;
        // Always return the full content size so the ScrollViewer can scroll.
        return new Size(w, double.IsInfinity(av.Height) ? h : av.Height);
    }

    // ── Full Redraw ───────────────────────────────────────────────────────────

    private void Redraw()
    {
        DrawBackground();
        DrawGrid();
        DrawSteps();
        RedrawPlayhead();
        DrawOverlay();
    }

    private void DrawBackground()
    {
        using var dc = _background.RenderOpen();
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        dc.DrawRectangle(new SolidColorBrush(ColBg),    null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(new SolidColorBrush(ColStrip), null, new Rect(0, 0, StripW, ActualHeight));
    }

    private void DrawGrid()
    {
        using var dc = _grid.RenderOpen();
        var vm = ViewModel; if (vm == null) return;

        int steps = vm.ActivePattern?.StepCount ?? 16;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x17)),
            null, new Rect(StripW, 0, Math.Max(0, ActualWidth - StripW), HeaderH));

        var gp  = new Pen(new SolidColorBrush(ColGrid),      0.5);
        var ggp = new Pen(new SolidColorBrush(ColGridGroup),  1.0);
        var rp  = new Pen(new SolidColorBrush(ColGrid),       0.5);

        for (int s = 0; s < steps; s++)
        {
            double x = StripW + s * (CellW + CellPad);
            dc.DrawLine(s % 4 == 0 ? ggp : gp, new Point(x, 0), new Point(x, ActualHeight));
            string num = (s + 1).ToString();
            var ft = new FormattedText(num, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Tf, 8, new SolidColorBrush(s % 4 == 0 ? ColText : ColTextDim), 1.0);
            dc.DrawText(ft, new Point(x + 4, 4));
        }

        for (int r = 0; r <= vm.Channels.Count; r++)
        {
            double y = HeaderH + r * CellH;
            dc.DrawLine(rp, new Point(0, y), new Point(ActualWidth, y));
        }

        for (int r = 0; r < vm.Channels.Count; r++)
        {
            var ch = vm.Channels[r];
            double y = HeaderH + r * CellH;

            // Selected indicator
            if (vm.SelectedChannel == ch)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0xC4, 0x1E, 0x3A)),
                    null, new Rect(0, y, StripW, CellH));

            // LED
            var ledColor = ch.IsMuted ? Color.FromRgb(0x44, 0x44, 0x44) : ch.ChannelColor;
            dc.DrawEllipse(new SolidColorBrush(ledColor), null,
                new Point(12, y + CellH / 2), 4, 4);

            // Icon
            var iconFt = new FormattedText(ch.PluginIcon, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Tf, 11, new SolidColorBrush(ColText), 1.0);
            dc.DrawText(iconFt, new Point(22, y + (CellH - iconFt.Height) / 2));

            // Name — truncated to strip width
            var nameFt = new FormattedText(ch.Name, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Tf, 9.5,
                new SolidColorBrush(ch.IsMuted ? ColTextDim : ColText), 1.0)
            { MaxTextWidth = StripW - 80, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(nameFt, new Point(38, y + (CellH - nameFt.Height) / 2));

            // Sample indicator (green dot when a file is loaded)
            if (!string.IsNullOrEmpty(ch.Model.SamplePath))
            {
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
                    null, new Point(StripW - 42, y + CellH / 2), 3, 3);
            }

            // Mixer track label
            var trFt = new FormattedText(
                ch.MixerTrack == 0 ? "M" : ch.MixerTrack.ToString(),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 8,
                new SolidColorBrush(ColTextDim), 1.0);
            dc.DrawText(trFt, new Point(StripW - 54, y + (CellH - trFt.Height) / 2));

            // ── Volume knob ───────────────────────────────────────────────────
            DrawKnob(dc, ch.Volume, StripW - 18, y + CellH / 2, 9);
        }
    }

    private void DrawSteps()
    {
        using var dc = _steps.RenderOpen();
        var vm = ViewModel; if (vm?.ActivePattern == null) return;
        int steps = vm.ActivePattern.StepCount;

        for (int r = 0; r < vm.Channels.Count; r++)
        {
            var ch = vm.Channels[r];
            double y = HeaderH + r * CellH + CellPad;
            double h = CellH - CellPad * 2;

            for (int s = 0; s < steps && s < ch.Steps.Count; s++)
            {
                double x = StripW + s * (CellW + CellPad) + CellPad;
                double w = CellW - CellPad;
                var step = ch.Steps[s];
                Color c;
                if (step.IsActive)
                {
                    float v = step.Velocity;
                    c = Color.FromArgb(255,
                        (byte)(ch.ChannelColor.R * 0.55 + 255 * v * 0.45),
                        (byte)(ch.ChannelColor.G * 0.55 + 255 * v * 0.45),
                        (byte)(ch.ChannelColor.B * 0.55 + 255 * v * 0.45));
                }
                else
                    c = s % 8 < 4 ? ColStepOff : Color.FromRgb(0x1C, 0x22, 0x2E);

                dc.DrawRoundedRectangle(new SolidColorBrush(c), null,
                    new Rect(x, y, w, h), 2, 2);
            }
        }
    }

    private void RedrawPlayhead()
    {
        using var dc = _playhead.RenderOpen();
        var vm = ViewModel;
        if (vm?.ActivePattern == null || !vm.IsPlaying || vm.CurrentStep < 0) return;

        double x = StripW + vm.CurrentStep * (CellW + CellPad);
        double h = HeaderH + vm.Channels.Count * CellH;
        if (h <= HeaderH) return;

        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(200,
            ColPlayhead.R, ColPlayhead.G, ColPlayhead.B)), 2),
            new Point(x + 1, 0), new Point(x + 1, h));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28,
            ColPlayhead.R, ColPlayhead.G, ColPlayhead.B)),
            null, new Rect(x, HeaderH, CellW, h - HeaderH));
    }

    private void DrawOverlay()
    {
        using var dc = _overlay.RenderOpen();

        // Step hover
        if (_hoverCell != (-1, -1))
        {
            (int cr, int cs) = _hoverCell;
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), null,
                new Rect(StripW + cs * (CellW + CellPad) + CellPad,
                         HeaderH + cr * CellH + CellPad,
                         CellW - CellPad, CellH - CellPad * 2), 2, 2);
        }

        // Strip hover (for context-menu indication)
        if (_hoverRow >= 0 && _hoverCell == (-1, -1))
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                null, new Rect(0, HeaderH + _hoverRow * CellH, StripW, CellH));
        }

        // Drag-over highlight
        if (_dropTargetRow >= 0)
        {
            int row = _dropTargetRow;
            var rect = row < (ViewModel?.Channels.Count ?? 0)
                ? new Rect(0, HeaderH + row * CellH, StripW + 4, CellH)
                : new Rect(0, HeaderH + (ViewModel?.Channels.Count ?? 0) * CellH, StripW + 4, CellH);
            dc.DrawRectangle(new SolidColorBrush(ColDropHi),
                new Pen(new SolidColorBrush(Color.FromArgb(180, 0x3B, 0x82, 0xF6)), 1.5), rect);

            // "+" icon for new-channel drop
            if (row >= (ViewModel?.Channels.Count ?? 0))
            {
                var ft = new FormattedText("+ drop to add channel",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 9,
                    new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)), 1.0);
                dc.DrawText(ft, new Point(8, HeaderH + row * CellH + (CellH - ft.Height) / 2));
            }
        }
    }

    // ── Knob drag state ───────────────────────────────────────────────────────
    private int    _knobDragRow    = -1;
    private double _knobDragStartY;
    private float  _knobDragStartVol;

    /// <summary>Returns the channel row index when the point is over a volume knob, else -1.</summary>
    private int HitTestKnob(Point p)
    {
        var vm = ViewModel; if (vm == null) return -1;
        if (p.Y < HeaderH) return -1;
        int row = (int)((p.Y - HeaderH) / CellH);
        if (row < 0 || row >= vm.Channels.Count) return -1;
        double cx = StripW - 18;
        double cy = HeaderH + row * CellH + CellH / 2.0;
        double dx = p.X - cx, dy = p.Y - cy;
        return (dx * dx + dy * dy) <= 11 * 11 ? row : -1;
    }

    /// <summary>Draws a small rotary knob centred at (cx, cy) with the given radius.</summary>
    private static void DrawKnob(DrawingContext dc, float value, double cx, double cy, double r)
    {
        // Background circle
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x22, 0x28, 0x36)),
            new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x50, 0x64)), 1),
            new Point(cx, cy), r, r);

        // Filled arc (pie): value 0 → -135°, value 1 → +135°
        double startDeg = -135.0;
        double endDeg   = startDeg + value * 270.0;
        double sRad     = (startDeg - 90.0) * Math.PI / 180.0;
        double eRad     = (endDeg   - 90.0) * Math.PI / 180.0;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(cx, cy), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(cx + r * Math.Cos(sRad), cy + r * Math.Sin(sRad)), false, false);
            ctx.ArcTo(new Point(cx + r * Math.Cos(eRad), cy + r * Math.Sin(eRad)),
                new Size(r, r), 0, (endDeg - startDeg) > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(180, 0x3B, 0x82, 0xF6)), null, geo);

        // Indicator line pointing to current value
        dc.DrawLine(new Pen(Brushes.White, 1.5),
            new Point(cx, cy),
            new Point(cx + (r - 1) * Math.Cos(eRad), cy + (r - 1) * Math.Sin(eRad)));

        // Small % label below knob
        var ft = new FormattedText($"{(int)(value * 100)}",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 6.5,
            new SolidColorBrush(Color.FromRgb(0x88, 0x94, 0xA8)), 1.0);
        dc.DrawText(ft, new Point(cx - ft.Width / 2, cy + r + 1));
    }

    // ── Hit testing ───────────────────────────────────────────────────────────

    private (int ch, int step) HitTestGrid(Point p)
    {
        var vm = ViewModel; if (vm?.ActivePattern == null) return (-1, -1);
        if (p.X < StripW || p.Y < HeaderH) return (-1, -1);
        int ch   = (int)((p.Y - HeaderH) / CellH);
        int step = (int)((p.X - StripW)  / (CellW + CellPad));
        if (ch < 0 || ch >= vm.Channels.Count) return (-1, -1);
        if (step < 0 || step >= vm.ActivePattern.StepCount) return (-1, -1);
        return (ch, step);
    }

    private int HitTestRow(Point p)
    {
        if (p.Y < HeaderH) return -1;
        return (int)((p.Y - HeaderH) / CellH);
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p    = e.GetPosition(this);

        // Knob drag
        if (_knobDragRow >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            double delta = (_knobDragStartY - p.Y) / 80.0; // 80px = full range
            float  newVol = Math.Clamp(_knobDragStartVol + (float)delta, 0f, 1f);
            ViewModel!.Channels[_knobDragRow].Volume = newVol;
            Dispatcher.InvokeAsync(Redraw, DispatcherPriority.Render);
            e.Handled = true;
            return;
        }

        var cell = HitTestGrid(p);
        int row  = HitTestRow(p);

        bool changed = cell != _hoverCell || row != _hoverRow;
        _hoverCell = cell;
        _hoverRow  = (p.X < StripW && cell == (-1, -1)) ? row : -1;
        if (changed) DrawOverlay();

        if (_isPainting && cell != (-1, -1))
            SetStep(cell.ch, cell.step, _paintValue);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverCell = (-1, -1);
        _hoverRow  = -1;
        DrawOverlay();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p    = e.GetPosition(this);

        // Knob: double-click resets to 100%, single-click starts drag
        int knobRow = HitTestKnob(p);
        if (knobRow >= 0)
        {
            if (e.ClickCount == 2 && ViewModel != null)
            {
                ViewModel.Channels[knobRow].Volume = 1.0f;
                Dispatcher.InvokeAsync(Redraw, DispatcherPriority.Render);
            }
            else
            {
                _knobDragRow      = knobRow;
                _knobDragStartY   = p.Y;
                _knobDragStartVol = ViewModel!.Channels[knobRow].Volume;
                CaptureMouse();
            }
            e.Handled = true;
            return;
        }

        var cell = HitTestGrid(p);
        if (cell == (-1, -1)) return;

        _paintValue = !(ViewModel?.Channels[cell.ch].Steps[cell.step].IsActive ?? false);
        _isPainting = true;
        CaptureMouse();
        SetStep(cell.ch, cell.step, _paintValue);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_knobDragRow >= 0) { _knobDragRow = -1; ReleaseMouseCapture(); return; }
        if (_isPainting) { _isPainting = false; ReleaseMouseCapture(); }
    }

    // ── Right-click → Context Menu ────────────────────────────────────────────

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var vm = ViewModel; if (vm == null) return;
        var p   = e.GetPosition(this);

        // Right-click on step grid → erase step
        var cell = HitTestGrid(p);
        if (cell != (-1, -1))
        {
            SetStep(cell.ch, cell.step, false);
            e.Handled = true;
            return;
        }

        // Right-click on channel strip → channel context menu
        int row = HitTestRow(p);
        if (p.X < StripW && row >= 0 && row < vm.Channels.Count)
        {
            var ch = vm.Channels[row];
            OpenChannelContextMenu(ch, vm);
            e.Handled = true;
        }
    }

    private void OpenChannelContextMenu(ChannelViewModel ch, PatternViewModel vm)
    {
        var menu = new ContextMenu
        {
            Background   = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20)),
            BorderBrush  = new SolidColorBrush(Color.FromRgb(0x28, 0x30, 0x40)),
            BorderThickness = new Thickness(1),
            Padding      = new Thickness(0, 4, 0, 4)
        };

        menu.Resources[typeof(MenuItem)] = BuildMenuItemStyle();

        // ── Header (channel name, non-interactive) ────────────────────────
        var header = new MenuItem
        {
            Header      = BuildMenuText($"🎹  {ch.Name}", bold: true, accent: ch.ChannelColor),
            IsEnabled   = false
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(0x28, 0x30, 0x40)) });

        // ── Open in Piano Roll ────────────────────────────────────────────
        var openPR = new MenuItem
        {
            Header  = BuildMenuText("🎼  Open in Piano Roll"),
            Command = vm.OpenInPianoRollCommand,
            CommandParameter = ch
        };
        menu.Items.Add(openPR);

        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(0x28, 0x30, 0x40)) });

        // ── Mute / Solo ───────────────────────────────────────────────────
        var muteItem = new MenuItem { Header = BuildMenuText(ch.IsMuted ? "🔊  Unmute" : "🔇  Mute") };
        muteItem.Click += (_, _) => ch.IsMuted = !ch.IsMuted;
        menu.Items.Add(muteItem);

        var soloItem = new MenuItem { Header = BuildMenuText(ch.IsSolo ? "◎  Unsolo" : "◉  Solo") };
        soloItem.Click += (_, _) => ch.IsSolo = !ch.IsSolo;
        menu.Items.Add(soloItem);

        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(0x28, 0x30, 0x40)) });

        // ── Route to Mixer ────────────────────────────────────────────────
        var routeItem = new MenuItem { Header = BuildMenuText("🔀  Route to Mixer →") };
        routeItem.Style = BuildMenuItemStyle();

        // Master (0)
        var masterRoute = new MenuItem { Header = BuildMenuText(ch.MixerTrack == 0 ? "✓  Master" : "  Master") };
        masterRoute.Click += (_, _) => { ch.MixerTrack = 0; Redraw(); };
        routeItem.Items.Add(masterRoute);

        for (int i = 1; i <= 8; i++)
        {
            int idx = i;
            string label = ch.MixerTrack == idx ? $"✓  Mixer {idx}" : $"  Mixer {idx}";
            var routeSub = new MenuItem { Header = BuildMenuText(label) };
            routeSub.Click += (_, _) => { ch.MixerTrack = idx; Redraw(); };
            routeItem.Items.Add(routeSub);
        }
        menu.Items.Add(routeItem);

        menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(0x28, 0x30, 0x40)) });

        // ── Remove channel ────────────────────────────────────────────────
        var removeItem = new MenuItem
        {
            Header  = BuildMenuText("🗑  Remove Channel"),
            Command = vm.RemoveChannelCommand,
            CommandParameter = ch
        };
        menu.Items.Add(removeItem);

        menu.IsOpen = true;
    }

    private static Style BuildMenuItemStyle()
    {
        var style = new Style(typeof(MenuItem));
        var factory = new FrameworkElementFactory(typeof(ContentPresenter));
        factory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        factory.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 3, 16, 3));
        factory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        var template = new ControlTemplate(typeof(MenuItem));
        template.VisualTree = factory;

        // Hover trigger
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cp.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 3, 16, 3));
        bd.AppendChild(cp);
        var tmpl2 = new ControlTemplate(typeof(MenuItem)) { VisualTree = bd };
        var t = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        t.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x1E, 0x25, 0x32))));
        tmpl2.Triggers.Add(t);

        style.Setters.Add(new Setter(MenuItem.TemplateProperty, tmpl2));
        style.Setters.Add(new Setter(MenuItem.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))));
        style.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent));
        return style;
    }

    private static object BuildMenuText(string text, bool bold = false, Color? accent = null)
    {
        return new TextBlock
        {
            Text       = text,
            FontSize   = 11,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = accent.HasValue
                ? new SolidColorBrush(accent.Value)
                : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────────

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        _dropTargetRow = HitTestDropRow(e.GetPosition(this));
        DrawOverlay();
        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy;
        int row = HitTestDropRow(e.GetPosition(this));
        if (row != _dropTargetRow) { _dropTargetRow = row; DrawOverlay(); }
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        _dropTargetRow = -1;
        DrawOverlay();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        _dropTargetRow = -1;
        DrawOverlay();

        var vm = ViewModel; if (vm == null) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        var p   = e.GetPosition(this);
        int row = HitTestDropRow(p);
        var vm2 = ViewModel!;

        if (row >= 0 && row < vm2.Channels.Count)
        {
            // Drop onto existing channel → replace its sample
            vm2.DropSampleOnChannel(row, files[0]);
        }
        else
        {
            // Drop below all channels → create new channel for each file
            foreach (var f in files)
            {
                string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".wav" or ".mp3" or ".aif" or ".aiff" or ".flac" or ".ogg")
                    vm2.DropSampleAsNewChannel(f);
            }
        }

        e.Handled = true;
    }

    private int HitTestDropRow(Point p)
    {
        if (p.Y < HeaderH) return -1;
        return (int)((p.Y - HeaderH) / CellH);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetStep(int ch, int step, bool active)
    {
        var vm = ViewModel; if (vm == null) return;
        if (ch < 0 || ch >= vm.Channels.Count) return;
        var channel = vm.Channels[ch];
        if (step < 0 || step >= channel.Steps.Count) return;
        channel.Steps[step].IsActive = active;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Redraw();
    }
}
