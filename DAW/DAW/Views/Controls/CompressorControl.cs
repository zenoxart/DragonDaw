using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using static DAW.Views.Controls.DragonUI;

namespace DAW.Views.Controls;

/// <summary>
/// 1176-style FET Compressor — Red Dragon Design.
///
/// Fixed layout (500 × 480 window):
///   Row 0  [HDR_H]  Header + GR readout
///   Row 1  [120]    VU Meter (arc needle)
///   Row 2  [ROW_H]  4 knobs: INPUT  ATTACK  RELEASE  OUTPUT
///   Row 3  [BTN_H+24] Ratio section: 4 buttons + label
/// </summary>
public sealed class CompressorControl : FrameworkElement
{
    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(CompressorEffect), typeof(CompressorControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((CompressorControl)d).InvalidateVisual()));

    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(CompressorControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public CompressorEffect? Effect
    { get => (CompressorEffect?)GetValue(EffectProperty); set => SetValue(EffectProperty, value); }
    public bool IsCompact
    { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }

    private record struct KD(string Label, double Min, double Max,
        Func<CompressorEffect, double> Get, Action<CompressorEffect, double> Set,
        bool Log = false, double Def = 0);

    private static readonly KD[] Knobs =
    [
        new("INPUT",   -12, 48,    e => e.InputGain,  (e, v) => e.InputGain  = v, Def: 0),
        new("ATTACK",  0.02, 100,  e => e.Attack,     (e, v) => e.Attack     = v, Log: true, Def: 10),
        new("RELEASE", 10, 1200,   e => e.Release,    (e, v) => e.Release    = v, Log: true, Def: 100),
        new("OUTPUT",  -24, 12,    e => e.OutputGain, (e, v) => e.OutputGain = v, Def: 0),
    ];

    private int    _dragIdx = -1;
    private double _dragY, _dragBase;
    private Point[] _kc = new Point[4];
    private Rect[]  _rb = [];
    private readonly DispatcherTimer _timer;

    public CompressorControl()
    {
        ClipToBounds = SnapsToDevicePixels = Focusable = true;
        _timer = new DispatcherTimer(DispatcherPriority.Render)
            { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick    += (_, _) => InvalidateVisual();
        Loaded         += (_, _) => _timer.Start();
        Unloaded       += (_, _) => _timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W < 30 || H < 30) return;

        dc.DrawRoundedRectangle(BBg, PBorder, new Rect(0, 0, W, H), 6, 6);
        DrawScrew(dc, 8, 8); DrawScrew(dc, W - 8, 8);
        DrawScrew(dc, 8, H - 8); DrawScrew(dc, W - 8, H - 8);

        var comp = Effect;
        double gr = comp?.GainReduction ?? 0;

        // ── Row 0: Header ──
        DrawHeader(dc, new Rect(0, 0, W, HDR_H), "1176", "FET Compressor",
            $"GR  -{gr:F1} dB");

        if (comp == null) return;
        if (IsCompact) { DrawCompact(dc, comp, W, H); return; }

        // ── Row 1: VU Meter — fixed height 120 ──
        double mY = HDR_H + PAD;
        double mH = 120;
        DrawVU(dc, comp, W / 2, mY + mH * 0.78, Math.Min(W * 0.32, mH * 0.80), mY, mH);

        // ── Row 2: 4 Knobs ──
        double kY = mY + mH + PAD;
        var cxs = Columns(PAD, W - PAD * 2, 4);
        double kcy = KnobCY(kY, KR);
        for (int i = 0; i < 4; i++)
        {
            _kc[i] = new Point(cxs[i], kcy);
            var k = Knobs[i]; double v = k.Get(comp);
            double n = k.Log ? LogN(v, k.Min, k.Max) : LinN(v, k.Min, k.Max);
            DrawKnob(dc, cxs[i], kcy, KR, n, CGold, k.Label, FmtK(k, v));
        }

        // ── Row 3: Ratio section ──
        double ratY = kY + ROW_H + GAP;
        DrawRatioSection(dc, comp, W, ratY);
    }

    // VU meter with arc needle
    private static void DrawVU(DrawingContext dc, CompressorEffect comp,
        double cx, double cy, double r, double faceTop, double faceH)
    {
        // Face panel
        double fW = r * 2.6;
        var face = new Rect(cx - fW / 2, faceTop + 4, fW, faceH - 4);
        var fg = new LinearGradientBrush(
            Color.FromRgb(0x1C, 0x20, 0x28), Color.FromRgb(0x12, 0x15, 0x1C), 90);
        fg.Freeze();
        dc.DrawRoundedRectangle(fg, P(CRed, 1.5), face, 6, 6);

        double aS = Math.PI * 1.05, aE = 0.0;

        // Zone bands
        void Band(double f, double t, byte a, Color c)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)), r * 0.18);
            pen.Freeze();
            DrawArc(dc, cx, cy, r * 0.88, Lerp(aS, aE, f), Lerp(aS, aE, t), pen, 10);
        }
        Band(0.00, 0.75, 20, CGreen);
        Band(0.75, 0.90, 20, COrange);
        Band(0.90, 1.00, 20, CRed);

        // Ticks
        double[] tdb = [0, 1, 2, 3, 5, 7, 10, 14, 20];
        foreach (var db in tdb)
        {
            double n = 1 - db / 20.0, a = Lerp(aS, aE, n);
            bool mj = db == 0 || db == 5 || db == 10 || db == 20;
            dc.DrawLine(mj ? P(CTextSec, 1) : P(CTextDim, 0.7),
                new Point(cx + r * 0.76 * Math.Cos(a), cy - r * 0.76 * Math.Sin(a)),
                new Point(cx + r * (mj ? 0.92 : 0.87) * Math.Cos(a), cy - r * (mj ? 0.92 : 0.87) * Math.Sin(a)));
            if (mj)
            {
                var lbl = Txt($"{db}", 6.5, BTextDim, tf: TFMono);
                dc.DrawText(lbl, new Point(
                    cx + r * 1.03 * Math.Cos(a) - lbl.Width / 2,
                    cy - r * 1.03 * Math.Sin(a) - lbl.Height / 2));
            }
        }

        // GR label
        var gLabel = Txt("GR  dB", 8, BTextDim, FontWeights.SemiBold, TFCond);
        dc.DrawText(gLabel, new Point(cx - gLabel.Width / 2, face.Y + 5));

        // Needle
        double n2 = 1 - Math.Clamp(comp.GainReduction, 0, 20) / 20.0;
        double na = Lerp(aS, aE, n2), nl = r * 0.86;
        dc.DrawLine(P(Color.FromArgb(45, 0, 0, 0), 3),
            new Point(cx + 1, cy + 1),
            new Point(cx + (nl + 1) * Math.Cos(na), cy - (nl + 1) * Math.Sin(na)));
        dc.DrawLine(P(CTextPri, 1.5),
            new Point(cx, cy),
            new Point(cx + nl * Math.Cos(na), cy - nl * Math.Sin(na)));
        dc.DrawEllipse(BTextPri, null, new Point(cx, cy), 3.2, 3.2);
        dc.DrawEllipse(BPanel,   null, new Point(cx, cy), 1.4, 1.4);
    }

    private void DrawRatioSection(DrawingContext dc, CompressorEffect comp, double W, double y)
    {
        double secH = BTN_H + 30;
        var secR = new Rect(PAD, y, W - PAD * 2, secH);
        DrawSection(dc, secR, "Ratio", CTextDim);

        var lbl = Txt("RATIO", 8, BTextSec, FontWeights.Bold, TFCond);
        dc.DrawText(lbl, new Point(PAD + 10, y + (HDR_H * 0.55 - lbl.Height) / 2 + 2));

        int n = CompressorEffect.PresetRatios.Length;
        double bW = BTN_W, bH = BTN_H, gap = GAP;
        double total = n * bW + (n - 1) * gap;
        double sx = W / 2 - total / 2;
        double by = y + (secH - bH) / 2 + 4;

        _rb = new Rect[n];
        for (int i = 0; i < n; i++)
        {
            double ratio = CompressorEffect.PresetRatios[i];
            bool   act   = Math.Abs(comp.Ratio - ratio) < 0.5;
            _rb[i] = new Rect(sx + i * (bW + gap), by, bW, bH);
            DrawButton(dc, _rb[i], $"{ratio:F0}:1", act, 9);
        }
    }

    private static void DrawCompact(DrawingContext dc, CompressorEffect comp, double W, double H)
    {
        dc.DrawRectangle(BRed, null, new Rect(0, 0, 3, H));
        double gr = Math.Clamp(comp.GainReduction, 0, 20);
        double bH = 14, bY = (H - bH) / 2;
        DrawMeterBar(dc, new Rect(6, bY, W - 12, bH), gr / 20.0, rtl: true);
        var gt = Txt($"-{gr:F1}dB", 8, BGold, FontWeights.Bold, TFMono);
        dc.DrawText(gt, new Point(8, bY + (bH - gt.Height) / 2));
        var rt = Txt($"{comp.Ratio:F0}:1", 8, BRed, FontWeights.Bold, TFMono);
        dc.DrawText(rt, new Point(W - rt.Width - 6, bY + (bH - rt.Height) / 2));
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static double LogN(double v, double min, double max)
        => (Math.Log10(Math.Max(v, 1e-6)) - Math.Log10(Math.Max(min, 1e-6)))
         / (Math.Log10(max) - Math.Log10(Math.Max(min, 1e-6)));
    private static double LinN(double v, double min, double max) => (v - min) / (max - min);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static string FmtK(KD k, double v) => k.Label switch
    {
        "ATTACK"  => v < 1 ? $"{v * 1000:F0}µs" : $"{v:F1}ms",
        "RELEASE" => $"{v:F0}ms",
        _         => $"{v:+0.0;-0.0}dB"
    };

    // ── Mouse ──────────────────────────────────────────────────────────────
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Effect == null || IsCompact) return;
        var p = e.GetPosition(this);

        for (int i = 0; i < _rb.Length; i++)
            if (_rb[i].Contains(p))
            { Effect.Ratio = CompressorEffect.PresetRatios[i]; e.Handled = true; return; }

        for (int i = 0; i < _kc.Length; i++)
        {
            var d = p - _kc[i];
            if (d.X * d.X + d.Y * d.Y <= (KR + 14) * (KR + 14))
            { _dragIdx = i; _dragY = p.Y; _dragBase = Knobs[i].Get(Effect); CaptureMouse(); e.Handled = true; return; }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragIdx < 0 || Effect == null) return;
        var k  = Knobs[_dragIdx];
        double dy = _dragY - e.GetPosition(this).Y;
        bool   sh = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        double s  = sh ? 600 : 200;
        if (k.Log)
        {
            double lMin = Math.Log10(Math.Max(k.Min, 1e-6)), lMax = Math.Log10(k.Max);
            k.Set(Effect, Math.Pow(10, Math.Clamp(Math.Log10(Math.Max(_dragBase, 1e-6)) + dy * (lMax - lMin) / s, lMin, lMax)));
        }
        else k.Set(Effect, Math.Clamp(_dragBase + dy * (k.Max - k.Min) / s, k.Min, k.Max));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonUp(e); if (_dragIdx >= 0) { _dragIdx = -1; ReleaseMouseCapture(); } }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (Effect == null) return;
        var p = e.GetPosition(this);
        for (int i = 0; i < _kc.Length; i++)
        { var d = p - _kc[i]; if (d.X*d.X+d.Y*d.Y<=(KR+14)*(KR+14)) { Knobs[i].Set(Effect, Knobs[i].Def); e.Handled=true; return; } }
    }

    protected override Size MeasureOverride(Size av)
        => new(Math.Max(double.IsInfinity(av.Width)  ? 500 : av.Width,  440),
               Math.Max(double.IsInfinity(av.Height) ? 480 : av.Height, 420));
}
