using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using static DAW.MVVM.Views.Controls.DragonUI;

namespace DAW.MVVM.Views.Controls;

/// <summary>
/// HG-2-style Tube Saturator — Red Dragon Design.
///
/// Fixed layout (460 × 320 window):
///   Row 0  [HDR_H]        Header
///   Row 1  [ROW_H]        5 knobs: PENTODE  TRIODE  DENSITY  AIR  OUTPUT
///   Divider               "PARALLEL 12AX7"
///   Row 2  [ROW_H+BTN_H]  PARALLEL knob | SAT FREQ buttons | MIX knob | ON toggle
/// </summary>
public sealed class SaturationControl : FrameworkElement
{
    public static readonly DependencyProperty SaturationEffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(SaturationEffect), typeof(SaturationControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnFxChanged));

    public SaturationEffect? Effect
    { get => (SaturationEffect?)GetValue(SaturationEffectProperty); set => SetValue(SaturationEffectProperty, value); }

    private static void OnFxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (SaturationControl)d;
        if (e.OldValue is SaturationEffect o) o.PropertyChanged -= c.OnProp;
        if (e.NewValue is SaturationEffect n) n.PropertyChanged += c.OnProp;
        c.InvalidateVisual();
    }
    private void OnProp(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);

    private enum DT { None, Pentode, Triode, Density, Air, Output, Parallel, Mix }
    private DT     _drag; private double _dragY, _dragBase;
    private Point  _kPt, _kTri, _kDen, _kAir, _kOut, _kPar, _kMix;
    private Rect   _onBtn;
    private Rect[] _freqBtns = new Rect[3];

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W < 30 || H < 30) return;

        dc.DrawRoundedRectangle(BBg, PBorder, new Rect(0, 0, W, H), 6, 6);
        DrawScrew(dc, 8, 8); DrawScrew(dc, W - 8, 8);
        DrawScrew(dc, 8, H - 8); DrawScrew(dc, W - 8, H - 8);

        var fx = Effect;
        DrawHeader(dc, new Rect(0, 0, W, HDR_H), "HG-2", "Tube Saturator");

        // ── Row 1: 5 knobs ──
        double r1top = HDR_H + PAD;
        var cxs1 = Columns(PAD, W - PAD * 2, 5);
        double kcy1 = KnobCY(r1top, KR);

        _kPt  = new Point(cxs1[0], kcy1);
        _kTri = new Point(cxs1[1], kcy1);
        _kDen = new Point(cxs1[2], kcy1);
        _kAir = new Point(cxs1[3], kcy1);
        _kOut = new Point(cxs1[4], kcy1);

        DrawKnob(dc, _kPt.X,  kcy1, KR, fx?.PentodeDrive ?? 0.3f,  CRed,     "PENTODE", $"{(fx?.PentodeDrive ?? 0.3f)*100:F0}");
        DrawKnob(dc, _kTri.X, kcy1, KR, fx?.TriodeDrive  ?? 0.2f,  CGold,    "TRIODE",  $"{(fx?.TriodeDrive  ?? 0.2f)*100:F0}");
        DrawKnob(dc, _kDen.X, kcy1, KR, fx?.Density      ?? 0.5f,  CTextSec, "DENSITY", $"{(fx?.Density      ?? 0.5f)*100:F0}");
        DrawKnob(dc, _kAir.X, kcy1, KR, fx?.Air          ?? 0f,    CGold,    "AIR",     $"{(fx?.Air          ?? 0f)*100:F0}");
        DrawKnob(dc, _kOut.X, kcy1, KR, NOut(fx?.OutputGain ?? 0f), CRed,    "OUTPUT",  $"{fx?.OutputGain ?? 0f:+0.0;-0.0}dB");

        // ── Divider ──
        double divY = r1top + ROW_H + GAP;
        DrawDivider(dc, PAD, divY, W - PAD, "Parallel 12AX7");

        // ── Row 2: PARALLEL  |  SAT FREQ  |  MIX  +  ON ──
        double r2top = divY + 8;
        bool parOn = fx?.ParallelOn ?? false;
        float dimA = parOn ? 1f : 0.30f;

        // ON toggle — top right of row
        _onBtn = new Rect(W - PAD - 46, r2top, 46, BTN_H);
        DrawToggle(dc, _onBtn, parOn ? "ON" : "OFF", parOn);

        // PARALLEL knob (left quarter)
        double r2kcy = KnobCY(r2top + BTN_H + 4, KR);
        _kPar = new Point(W * 0.12, r2kcy);
        _kMix = new Point(W * 0.88, r2kcy);

        DrawKnob(dc, _kPar.X, r2kcy, KR,
            (fx?.ParallelSat ?? 0f) * dimA,
            Dim(CRed, dimA), "PARALLEL",
            $"{(fx?.ParallelSat ?? 0f)*100:F0}");

        DrawKnob(dc, _kMix.X, r2kcy, KR,
            fx?.Mix ?? 1f, CGold, "MIX",
            $"{(fx?.Mix ?? 1f)*100:F0}%");

        // SAT FREQ 3 buttons — centred
        string[] fl = ["LOW", "FLAT", "HIGH"];
        int sel = fx?.SatFreq ?? 1;
        double fbW = 50, fbH = BTN_H, fbGap = 6;
        double fbTW = 3 * fbW + 2 * fbGap;
        double fbX = W / 2 - fbTW / 2;
        double fbY = r2top + BTN_H + 4 + (ROW_H - BTN_H) / 2;

        var fqL = Txt("SAT FREQ", 7.5, BTextDim, FontWeights.Bold, TFCond);
        dc.DrawText(fqL, new Point(W / 2 - fqL.Width / 2, fbY - fqL.Height - 4));

        for (int fi = 0; fi < 3; fi++)
        {
            _freqBtns[fi] = new Rect(fbX + fi * (fbW + fbGap), fbY, fbW, fbH);
            DrawButton(dc, _freqBtns[fi], fl[fi], fi == sel && parOn, 8.5);
        }
    }

    private static float  NOut(float db) => (db + 12f) / 24f;
    private static float  DOut(float n)  => n * 24f - 12f;
    private static Color  Dim(Color c, float a) => Color.FromArgb((byte)(255 * a), c.R, c.G, c.B);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        for (int fi = 0; fi < 3; fi++)
            if (_freqBtns[fi].Contains(p) && Effect != null)
            { Effect.SatFreq = fi; e.Handled = true; return; }
        if (_onBtn.Contains(p) && Effect != null)
        { Effect.ParallelOn = !Effect.ParallelOn; e.Handled = true; return; }
        var t = Hit(p);
        if (t != DT.None) { _drag = t; _dragY = p.Y; _dragBase = Get(t); CaptureMouse(); e.Handled = true; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == DT.None || Effect == null) return;
        bool sh = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        Set(_drag, Math.Clamp(_dragBase + (_dragY - e.GetPosition(this).Y) * (sh ? 0.002 : 0.006), 0, 1));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { if (_drag != DT.None) { ReleaseMouseCapture(); _drag = DT.None; } }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    { var t = Hit(e.GetPosition(this)); if (t != DT.None && Effect != null) { Reset(t); e.Handled = true; } }

    private DT Hit(Point p)
    {
        double r2 = (KR + 14) * (KR + 14);
        Point[] kps = [_kPt, _kTri, _kDen, _kAir, _kOut, _kPar, _kMix];
        DT[]    dts = [DT.Pentode, DT.Triode, DT.Density, DT.Air, DT.Output, DT.Parallel, DT.Mix];
        for (int i = 0; i < kps.Length; i++)
        { var d = p - kps[i]; if (d.X*d.X+d.Y*d.Y<=r2) return dts[i]; }
        return DT.None;
    }

    private double Get(DT t) => t switch
    {
        DT.Pentode  => Effect?.PentodeDrive ?? 0.3,
        DT.Triode   => Effect?.TriodeDrive  ?? 0.2,
        DT.Density  => Effect?.Density      ?? 0.5,
        DT.Air      => Effect?.Air          ?? 0,
        DT.Output   => NOut(Effect?.OutputGain ?? 0),
        DT.Parallel => Effect?.ParallelSat  ?? 0,
        DT.Mix      => Effect?.Mix          ?? 1,
        _           => 0
    };

    private void Set(DT t, double v)
    {
        if (Effect == null) return;
        switch (t)
        {
            case DT.Pentode:  Effect.PentodeDrive = (float)v;         break;
            case DT.Triode:   Effect.TriodeDrive  = (float)v;         break;
            case DT.Density:  Effect.Density      = (float)v;         break;
            case DT.Air:      Effect.Air          = (float)v;         break;
            case DT.Output:   Effect.OutputGain   = DOut((float)v);   break;
            case DT.Parallel: Effect.ParallelSat  = (float)v;         break;
            case DT.Mix:      Effect.Mix          = (float)v;         break;
        }
    }

    private void Reset(DT t)
    {
        if (Effect == null) return;
        switch (t)
        {
            case DT.Pentode:  Effect.PentodeDrive = 0.3f; break;
            case DT.Triode:   Effect.TriodeDrive  = 0.2f; break;
            case DT.Density:  Effect.Density      = 0.5f; break;
            case DT.Air:      Effect.Air          = 0f;   break;
            case DT.Output:   Effect.OutputGain   = 0f;   break;
            case DT.Parallel: Effect.ParallelSat  = 0f;   break;
            case DT.Mix:      Effect.Mix          = 1f;   break;
        }
    }

    protected override Size MeasureOverride(Size av)
        => new(Math.Max(double.IsInfinity(av.Width)  ? 480 : av.Width,  460),
               Math.Max(double.IsInfinity(av.Height) ? 340 : av.Height, 320));
}
