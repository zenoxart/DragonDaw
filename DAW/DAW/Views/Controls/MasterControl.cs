using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using DAW.ViewModels;
using static DAW.Views.Controls.DragonUI;

namespace DAW.Views.Controls;

/// <summary>
/// Dragon Particle — Intelligent Mastering UI
///
/// Layout (600 × 400):
///
///   ┌─ Header ───────────────────────────────────────────────────────────┐
///   │  DRAGON PARTICLE   Intelligent Mastering                           │
///   ├────────────────────────────────────────────────────────────────────┤
///   │  LEFT (1/4)          CENTER (1/2)          RIGHT (1/4)             │
///   │                                                                    │
///   │  ●  INPUT            ◉  AMOUNT             LOW  ────●──────        │
///   │  (knob, gold)        (large, red ring)     MID  ──────●────        │
///   │                      mode label            HIGH ────────●──        │
///   │  ●  OUTPUT                                                         │
///   │  (knob, gold)                                                      │
///   ├─ Meter strip (RMS / LUFS / Peak) ─────────────────────────────────┤
///   └────────────────────────────────────────────────────────────────────┘
///
/// Input / Output knobs are stacked vertically on the left.
/// Amount ring is centred in the middle column.
/// Low / Mid / High are horizontal sliders stacked vertically on the right.
/// All interaction delegated to MasterViewModel — no DSP logic here.
/// </summary>
public sealed class MasterControl : FrameworkElement
{
    // ── Dependency property ───────────────────────────────────────────────────
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(MasterViewModel),
            typeof(MasterControl),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender, OnVmChanged));

    public MasterViewModel? ViewModel
    {
        get => (MasterViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnVmChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (MasterControl)d;
        if (e.OldValue is MasterViewModel o) o.PropertyChanged -= c.OnProp;
        if (e.NewValue is MasterViewModel n) n.PropertyChanged += c.OnProp;
        c._graphView.ViewModel = e.NewValue as MasterViewModel;
        c.InvalidateVisual();
    }
    private void OnProp(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Color CAmount   = Color.FromRgb(0xC4, 0x1E, 0x3A);
    private static readonly Color CGainCol  = Color.FromRgb(0xD4, 0xA0, 0x17);
    private static readonly Color CEq       = Color.FromRgb(0x3B, 0x82, 0xF6);
    private static readonly Color CMtrGood  = Color.FromRgb(0x2E, 0xCC, 0x71);
    private static readonly Color CMtrWarn  = Color.FromRgb(0xFF, 0xA5, 0x00);
    private static readonly Color CMtrClip  = Color.FromRgb(0xE6, 0x39, 0x46);

    // ── Fixed geometry ────────────────────────────────────────────────────────
    private const double DesignW  = 600;
    private const double DesignH  = 400;
    private const double GraphH   = 260;   // height of the DAG graph view below
    private const double HdrH     = 38;
    private const double Pad      = 12;
    private const double MtrH     = 62;   // meter strip height

    // Column split (fractions of content width)
    private const double LeftFrac   = 0.22;
    private const double RightFrac  = 0.22;
    // → centre gets 1 - LeftFrac - RightFrac = 0.56

    // Amount knob
    private const double AmtR = 52;

    // Gain knobs (left column)
    private const double GainKR = 20;

    // EQ slider geometry
    private const double SliderH     = 16;   // track height (horizontal slider)
    private const double SliderThW   = 10;
    private const double SliderThH   = 24;

    public static readonly double MinW = DesignW;
    public static readonly double MinH = DesignH + GraphH;

    // ── Hit-test caches (written during OnRender) —————————————————————————————
    private Point _amtCtr;
    private Point _inGainCtr, _outGainCtr;
    // EQ sliders: stored as track Rects + thumb Rects
    private Rect _lowTrack,  _midTrack,  _highTrack;
    private Rect _lowThumb,  _midThumb,  _highThumb;

    // ── DAG graph view (View layer of DragonParticle MVP) ————————————————
    private readonly DragonParticleGraphView _graphView = new();

    // ── Interaction state ─────────────────────────────────────────────────────
    private enum Drag { None, Amount, InGain, OutGain, Low, Mid, High }
    private Drag   _drag;
    private double _dragOrigin;   // Y for knobs, X for sliders
    private double _dragBase;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MasterControl()
    {
        ClipToBounds = SnapsToDevicePixels = Focusable = true;
        AddVisualChild(_graphView);
        AddLogicalChild(_graphView);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  RENDER
    // ══════════════════════════════════════════════════════════════════════════

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W < 80 || H < 80) return;
        var vm = ViewModel;

        // The top DesignH pixels belong to this control's own rendering.
        // The DragonParticleGraphView visual child occupies everything below.
        double topH = Math.Min(H, DesignH);

        // Background + chrome
        dc.DrawRoundedRectangle(BBg, PBorder, new Rect(0, 0, W, topH), 6, 6);
        DrawScrew(dc, 8, 8); DrawScrew(dc, W - 8, 8);
        DrawScrew(dc, 8, topH - 8); DrawScrew(dc, W - 8, topH - 8);

        DrawHeader(dc, new Rect(0, 0, W, HdrH), "DRAGON PARTICLE", "Intelligent Mastering");

        // Content area geometry
        double cY  = HdrH + Pad;
        double cH  = topH - HdrH - Pad * 2 - MtrH - Pad;
        double cW  = W - Pad * 2;

        double leftW   = cW * LeftFrac;
        double rightW  = cW * RightFrac;
        double centreW = cW - leftW - rightW;

        double leftX   = Pad;
        double centreX = leftX + leftW;
        double rightX  = centreX + centreW;

        // ── LEFT: Input (top) + Output (bottom) knobs ─────────────────────
        RenderGainColumn(dc, vm, leftX, cY, leftW, cH);

        // ── CENTRE: Amount ring ────────────────────────────────────────────
        double amtCX = centreX + centreW / 2;
        double amtCY = cY + cH / 2 - 8;
        _amtCtr = new Point(amtCX, amtCY);
        RenderAmountRing(dc, vm, amtCX, amtCY, AmtR);

        // ── RIGHT: EQ sliders ─────────────────────────────────────────────
        RenderEqColumn(dc, vm, rightX, cY, rightW, cH);

        // ── METER strip ───────────────────────────────────────────────────
        double mtrY = topH - MtrH - Pad;
        RenderMeters(dc, vm, new Rect(Pad, mtrY, W - Pad * 2, MtrH));
    }

    // ── Left column: Input + Output knobs ─────────────────────────────────────

    private void RenderGainColumn(DrawingContext dc, MasterViewModel? vm,
        double x, double y, double w, double h)
    {
        // Column panel
        var bg = new LinearGradientBrush(Color.FromRgb(0x16, 0x1A, 0x22),
                                          Color.FromRgb(0x11, 0x14, 0x1A), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 0.8), new Rect(x, y, w, h), 4, 4);

        // Section label
        var lbl = Txt("GAIN", 7.5, B(Color.FromArgb(110, CGainCol.R, CGainCol.G, CGainCol.B)),
            FontWeights.Bold, TFCond);
        dc.DrawText(lbl, new Point(x + 8, y + 6));

        double cx    = x + w / 2;
        double quarter = h / 4;

        // INPUT knob — upper half centre
        double inCY = y + quarter;
        _inGainCtr = new Point(cx, inCY);
        DrawKnob(dc, cx, inCY, GainKR,
            vm?.InputGainNorm  ?? 0.5, CGainCol,
            "INPUT",  vm?.InputGainStr  ?? "0.0 dB");

        // Vertical divider
        dc.DrawLine(P(CBorder, 0.6),
            new Point(x + 8, y + h / 2),
            new Point(x + w - 8, y + h / 2));

        // OUTPUT knob — lower half centre
        double outCY = y + h - quarter;
        _outGainCtr = new Point(cx, outCY);
        DrawKnob(dc, cx, outCY, GainKR,
            vm?.OutputGainNorm ?? 0.5, CGainCol,
            "OUTPUT", vm?.OutputGainStr ?? "0.0 dB");
    }

    // ── Centre: Amount ring ───────────────────────────────────────────────────

    private static void RenderAmountRing(DrawingContext dc, MasterViewModel? vm,
        double cx, double cy, double r)
    {
        double norm = vm?.AmountNorm ?? 0.3;

        // Ambient outer glow
        var glowBrush = new RadialGradientBrush(
            Color.FromArgb(18, CAmount.R, CAmount.G, CAmount.B),
            Color.FromArgb(0,  CAmount.R, CAmount.G, CAmount.B));
        glowBrush.Freeze();
        dc.DrawEllipse(glowBrush, null, new Point(cx, cy), r + 30, r + 30);

        // Segmented activity ring
        const int segs = 36;
        int lit = (int)(norm * segs);
        for (int i = 0; i < segs; i++)
        {
            double a1 = 5 * Math.PI / 4 + i       * (6 * Math.PI / 4) / segs;
            double a2 = 5 * Math.PI / 4 + (i + 1) * (6 * Math.PI / 4) / segs;
            double am = (a1 + a2) / 2;
            double sr = r + 15;
            Color sc = i < lit
                ? (i < segs * 0.55 ? CMtrGood : i < segs * 0.80 ? CMtrWarn : CMtrClip)
                : Color.FromArgb(22, 0x44, 0x4C, 0x66);
            dc.DrawLine(new Pen(B(sc), 4),
                new Point(cx + (sr - 3) * Math.Cos(a1), cy - (sr - 3) * Math.Sin(a1)),
                new Point(cx + (sr + 3) * Math.Cos(am), cy - (sr + 3) * Math.Sin(am)));
        }

        // Knob
        DrawKnob(dc, cx, cy, r, norm, CAmount, "AMOUNT", vm?.AmountStr ?? "—", glow: norm > 0.05);

        // Mode label below
        string mode = norm < 0.2 ? "Transparent" :
                      norm < 0.5 ? "Gentle" :
                      norm < 0.75 ? "Musical" : "Heavy";
        var mt = Txt(mode, 8, B(Color.FromArgb(150, CAmount.R, CAmount.G, CAmount.B)), tf: TFCond);
        dc.DrawText(mt, new Point(cx - mt.Width / 2, cy + r + 22));
    }

    // ── Right column: EQ horizontal sliders ──────────────────────────────────

    private void RenderEqColumn(DrawingContext dc, MasterViewModel? vm,
        double x, double y, double w, double h)
    {
        // Column panel
        var bg = new LinearGradientBrush(Color.FromRgb(0x16, 0x1A, 0x22),
                                          Color.FromRgb(0x11, 0x14, 0x1A), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 0.8), new Rect(x, y, w, h), 4, 4);

        // Section label
        var lbl = Txt("EQ", 7.5, B(Color.FromArgb(110, CEq.R, CEq.G, CEq.B)),
            FontWeights.Bold, TFCond);
        dc.DrawText(lbl, new Point(x + 8, y + 6));

        // Three sliders evenly distributed vertically
        double slotH = h / 3.0;

        (string name, double norm, string val, Rect trackOut, Rect thumbOut)[] bands =
        [
            ("LOW",  vm?.LowNorm  ?? 0.5, vm?.LowStr  ?? "0.0", default, default),
            ("MID",  vm?.MidNorm  ?? 0.5, vm?.MidStr  ?? "0.0", default, default),
            ("HIGH", vm?.HighNorm ?? 0.5, vm?.HighStr ?? "0.0", default, default),
        ];

        for (int i = 0; i < 3; i++)
        {
            double slotY = y + slotH * i;
            double slotMidY = slotY + slotH / 2;

            // Label
            var bandLbl = Txt(bands[i].name, 8, BTextDim, FontWeights.Bold, TFCond);
            dc.DrawText(bandLbl, new Point(x + 8, slotMidY - bandLbl.Height / 2 - 10));

            // Value
            var valT = Txt(bands[i].val, 8, B(CEq), FontWeights.SemiBold, TFMono);
            dc.DrawText(valT, new Point(x + 8, slotMidY + 2));

            // Track (horizontal, leaving room for label/value on left)
            double tX  = x + 8;
            double tW  = w - 16;
            double tY  = slotMidY - SliderH / 2 - 10;
            var track = new Rect(tX, tY, tW, SliderH);
            dc.DrawRoundedRectangle(BSurface, null, track, 3, 3);

            // Filled portion
            double norm  = bands[i].norm;
            double fillW = (tW - 4) * norm;
            if (fillW > 0)
            {
                // Colour: blue left of centre, red right of centre
                bool boosting = norm > 0.5;
                Color fillC = boosting ? CEq : CAmount;
                var fillBr = new LinearGradientBrush(
                    Color.FromArgb(160, fillC.R, fillC.G, fillC.B),
                    Color.FromArgb(80,  fillC.R, fillC.G, fillC.B), 0);
                fillBr.Freeze();
                dc.DrawRoundedRectangle(fillBr, null,
                    new Rect(tX + 2, tY + 2, fillW, SliderH - 4), 2, 2);
            }

            // Centre mark (0 dB)
            double midX = tX + tW / 2;
            dc.DrawLine(P(Color.FromArgb(60, 0xFF, 0xFF, 0xFF), 1),
                new Point(midX, tY - 2), new Point(midX, tY + SliderH + 2));

            // Thumb
            double thX   = tX + norm * (tW - SliderThW);
            var thumb = new Rect(thX, tY + SliderH / 2 - SliderThH / 2, SliderThW, SliderThH);
            var thGrad = new LinearGradientBrush(
                Color.FromRgb(0x50, 0x56, 0x66),
                Color.FromRgb(0x2A, 0x2E, 0x38), 90);
            thGrad.Freeze();
            dc.DrawRoundedRectangle(thGrad,
                new Pen(new SolidColorBrush(Color.FromArgb(140, CEq.R, CEq.G, CEq.B)), 1),
                thumb, 2, 2);
            // Thumb notch
            double tcx = thX + SliderThW / 2;
            dc.DrawLine(P(Color.FromArgb(180, CEq.R, CEq.G, CEq.B), 1.5),
                new Point(tcx, thumb.Y + 3), new Point(tcx, thumb.Bottom - 3));

            // Cache for hit-test
            switch (i)
            {
                case 0: _lowTrack  = track; _lowThumb  = thumb; break;
                case 1: _midTrack  = track; _midThumb  = thumb; break;
                case 2: _highTrack = track; _highThumb = thumb; break;
            }
        }
    }

    // ── Meter strip ───────────────────────────────────────────────────────────

    private static void RenderMeters(DrawingContext dc, MasterViewModel? vm, Rect r)
    {
        var bg = new LinearGradientBrush(
            Color.FromRgb(0x0C, 0x10, 0x16),
            Color.FromRgb(0x0A, 0x0D, 0x12), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 0.8), r, 4, 4);

        double barH  = 8;
        double slotH = (r.Height - 10) / 3.0;

        (string label, double norm, string val, double target, Color accent)[] meters =
        [
            ("RMS",  vm?.MeterRmsNorm  ?? 0, vm?.MeterRmsStr  ?? "—", 0.65, CMtrGood),
            ("LUFS", vm?.MeterLufsNorm ?? 0, vm?.MeterLufsStr ?? "—", 0.60, CEq),
            ("PEAK", vm?.MeterPeakNorm ?? 0, vm?.MeterPeakStr ?? "—", 0.85, CMtrWarn),
        ];

        double lbW  = 36;   // label column
        double valW = 60;   // value column
        double barX = r.X + lbW + 6;
        double barW = r.Width - lbW - valW - 12;

        for (int i = 0; i < 3; i++)
        {
            var (label, norm, val, target, accent) = meters[i];
            double mY    = r.Y + 5 + i * slotH;
            double centY = mY + slotH / 2;
            double barY  = centY - barH / 2;

            // Label
            var lt = Txt(label, 7.5, BTextDim, FontWeights.Bold, TFCond);
            dc.DrawText(lt, new Point(r.X + 6, centY - lt.Height / 2));

            // Track
            var track = new Rect(barX, barY, barW, barH);
            dc.DrawRoundedRectangle(BSurface, null, track, 2, 2);

            // Fill
            if (norm > 0.005)
            {
                double fw = (barW - 4) * norm;
                Color fc = norm < 0.75 ? accent : norm < 0.92 ? CMtrWarn : CMtrClip;
                var fb = new LinearGradientBrush(
                    Color.FromArgb(200, fc.R, fc.G, fc.B),
                    Color.FromArgb(110, fc.R, fc.G, fc.B), 0);
                fb.Freeze();
                dc.DrawRoundedRectangle(fb, null,
                    new Rect(barX + 2, barY + 2, fw, barH - 4), 1, 1);
            }

            // Target marker
            double tx = barX + target * (barW - 4) + 2;
            dc.DrawLine(P(Color.FromArgb(80, 0xFF, 0xFF, 0xFF), 1),
                new Point(tx, barY - 2), new Point(tx, barY + barH + 2));

            // Value
            var vt = Txt(val, 8, B(accent), FontWeights.SemiBold, TFMono);
            dc.DrawText(vt, new Point(barX + barW + 6, centY - vt.Height / 2));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  INTERACTION
    // ══════════════════════════════════════════════════════════════════════════

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var vm = ViewModel; if (vm == null) return;
        var p  = e.GetPosition(this);

        _drag = HitTest(p);
        if (_drag == Drag.None) return;

        bool isSlider = _drag is Drag.Low or Drag.Mid or Drag.High;
        _dragOrigin = isSlider ? p.X : p.Y;
        _dragBase = _drag switch
        {
            Drag.Amount  => vm.Amount,
            Drag.InGain  => vm.InputGain,
            Drag.OutGain => vm.OutputGain,
            Drag.Low     => vm.Low,
            Drag.Mid     => vm.Mid,
            Drag.High    => vm.High,
            _            => 0
        };
        CaptureMouse(); e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var vm = ViewModel; if (vm == null || _drag == Drag.None) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var p   = e.GetPosition(this);
        bool sh = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (_drag is Drag.Low or Drag.Mid or Drag.High)
        {
            // Horizontal slider: delta X → ±6 dB
            double trackW = SliderWidth(_drag);
            double dx = p.X - _dragOrigin;
            double dv = dx / (trackW - SliderThW) * 12.0 * (sh ? 0.15 : 1.0);
            double v  = Math.Clamp(_dragBase + dv, -6, 6);
            if (_drag == Drag.Low)  vm.Low  = v;
            if (_drag == Drag.Mid)  vm.Mid  = v;
            if (_drag == Drag.High) vm.High = v;
        }
        else
        {
            // Vertical knob drag
            double dy   = _dragOrigin - p.Y;
            double sens = sh ? 500 : 150;
            if (_drag == Drag.Amount)  vm.Amount     = _dragBase + dy / sens;
            if (_drag == Drag.InGain)  vm.InputGain  = _dragBase + dy / sens * 12;
            if (_drag == Drag.OutGain) vm.OutputGain = _dragBase + dy / sens * 12;
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == Drag.None) return;
        _drag = Drag.None; ReleaseMouseCapture(); e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var vm = ViewModel; if (vm == null) return;
        switch (HitTest(e.GetPosition(this)))
        {
            case Drag.Amount:  vm.Amount     = 0.3; break;
            case Drag.InGain:  vm.InputGain  = 0;   break;
            case Drag.OutGain: vm.OutputGain = 0;   break;
            case Drag.Low:     vm.Low        = 0;   break;
            case Drag.Mid:     vm.Mid        = 0;   break;
            case Drag.High:    vm.High       = 0;   break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var vm = ViewModel; if (vm == null) return;
        bool sh   = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        double d  = e.Delta > 0 ? (sh ? 0.002 : 0.01) : (sh ? -0.002 : -0.01);
        switch (HitTest(e.GetPosition(this)))
        {
            case Drag.Amount:  vm.Amount     = Math.Clamp(vm.Amount    + d,       0,  1);  break;
            case Drag.InGain:  vm.InputGain  = Math.Clamp(vm.InputGain + d * 12, -12, 12); break;
            case Drag.OutGain: vm.OutputGain = Math.Clamp(vm.OutputGain+ d * 12, -12, 12); break;
            case Drag.Low:     vm.Low        = Math.Clamp(vm.Low       + d * 6,  -6,  6);  break;
            case Drag.Mid:     vm.Mid        = Math.Clamp(vm.Mid       + d * 6,  -6,  6);  break;
            case Drag.High:    vm.High       = Math.Clamp(vm.High      + d * 6,  -6,  6);  break;
        }
        e.Handled = true;
    }

    // ── Hit test ──────────────────────────────────────────────────────────────

    private Drag HitTest(Point p)
    {
        // Amount — large circle
        double amtR2 = (AmtR + 18) * (AmtR + 18);
        if (Dist2(p, _amtCtr)    <= amtR2)      return Drag.Amount;

        // Gain knobs
        double gr2 = (GainKR + 10) * (GainKR + 10);
        if (Dist2(p, _inGainCtr) <= gr2)         return Drag.InGain;
        if (Dist2(p, _outGainCtr)<= gr2)         return Drag.OutGain;

        // EQ sliders — inflate track vertically for easier grab
        if (HitSlider(_lowTrack,  _lowThumb,  p)) return Drag.Low;
        if (HitSlider(_midTrack,  _midThumb,  p)) return Drag.Mid;
        if (HitSlider(_highTrack, _highThumb, p)) return Drag.High;

        return Drag.None;
    }

    private static bool HitSlider(Rect track, Rect thumb, Point p)
    {
        var expanded = new Rect(track.X, track.Y - 12, track.Width, track.Height + 24);
        return expanded.Contains(p) || thumb.Contains(p);
    }

    private double SliderWidth(Drag d) => d switch
    {
        Drag.Low  => _lowTrack.Width,
        Drag.Mid  => _midTrack.Width,
        Drag.High => _highTrack.Width,
        _         => 1
    };

    private static double Dist2(Point a, Point b)
        => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    // ── Measure ───────────────────────────────────────────────────────────────

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _graphView;

    protected override Size MeasureOverride(Size av)
    {
        double w = Math.Max(double.IsInfinity(av.Width)  ? MinW : av.Width,  MinW);
        double h = Math.Max(double.IsInfinity(av.Height) ? MinH : av.Height, MinH);
        _graphView.Measure(new Size(w, GraphH));
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size fs)
    {
        // Position the DAG graph view immediately below the 400 px master-control section
        _graphView.Arrange(new Rect(0, DesignH, fs.Width, Math.Max(0, fs.Height - DesignH)));
        return fs;
    }
}
