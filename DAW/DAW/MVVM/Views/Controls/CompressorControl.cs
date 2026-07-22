using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using static DAW.MVVM.Views.Controls.DragonUI;

namespace DAW.MVVM.Views.Controls;

/// <summary>
/// 1176-style FET Compressor — Dragon Particle-matched design.
///
/// Layout mirrors the DragonParticle "Intelligent Mastering" plugin:
///
///   ┌─ Header ───────────────────────────────────────────────────────────┐
///   │  1176   FET Compressor                         GR  -x.x dB         │
///   ├────────────────────────────────────────────────────────────────────┤
///   │  GAIN (gold)         GAIN REDUCTION (red ring)   TIME (blue)       │
///   │  ● INPUT             ◉  segmented activity ring   ● ATTACK         │
///   │  ● OUTPUT            -x.x dB  ·  mode label        ● RELEASE       │
///   ├─ Ratio section (buttons) ─────────────────────────────────────────┤
///   ├─ Meter strip:  IN / GR / OUT bars with target markers ────────────┤
///   └────────────────────────────────────────────────────────────────────┘
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

    // ── Dragon Particle accent palette (mirrors MasterControl) ─────────────
    private static readonly Color CGainCol = Color.FromRgb(0xD4, 0xA0, 0x17); // gold — gain
    private static readonly Color CTimeCol = Color.FromRgb(0x3B, 0x82, 0xF6); // blue — attack/release
    private static readonly Color CGrCol   = Color.FromRgb(0xC4, 0x1E, 0x3A); // dragon red — gain reduction

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

        // ── Geometry ──
        double meterStripH = 78;
        double ratioH       = BTN_H + 30;
        double topY  = HDR_H + PAD;
        double panelH = Math.Max(H - topY - PAD - meterStripH - GAP - ratioH - GAP - PAD, 150);

        double cW = W - PAD * 2;
        double leftW   = cW * 0.24;
        double rightW  = cW * 0.24;
        double centreW = cW - leftW - rightW - GAP * 2;

        double leftX   = PAD;
        double centreX = leftX + leftW + GAP;
        double rightX  = centreX + centreW + GAP;

        // ── LEFT: GAIN panel (Input / Output) ──
        RenderKnobPanel(dc, new Rect(leftX, topY, leftW, panelH), "GAIN", CGainCol,
            comp, Knobs[0], 0, Knobs[3], 3);

        // ── CENTRE: Gain-reduction segmented ring ──
        double ringR = Math.Min(centreW, panelH) * 0.30;
        DrawGrDial(dc, centreX + centreW / 2, topY + panelH / 2, ringR, gr);

        // ── RIGHT: TIME panel (Attack / Release) ──
        RenderKnobPanel(dc, new Rect(rightX, topY, rightW, panelH), "TIME", CTimeCol,
            comp, Knobs[1], 1, Knobs[2], 2);

        // ── Ratio section ──
        double ratY = topY + panelH + GAP;
        DrawRatioSection(dc, comp, W, ratY, ratioH);

        // ── Meter strip: IN / GR / OUT ──
        double mtrY = ratY + ratioH + GAP;
        RenderMeterStrip(dc, comp, new Rect(PAD, mtrY, W - PAD * 2, meterStripH));
    }

    // ── Left/right knob panel (gradient bg, section label, 2 stacked knobs) ─
    private void RenderKnobPanel(DrawingContext dc, Rect r, string label, Color accent,
        CompressorEffect comp, KD topK, int topIdx, KD botK, int botIdx)
    {
        var bg = new LinearGradientBrush(Color.FromRgb(0x16, 0x1A, 0x22),
                                          Color.FromRgb(0x11, 0x14, 0x1A), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 0.8), r, 4, 4);

        var lbl = Txt(label, 7.5, B(Color.FromArgb(110, accent.R, accent.G, accent.B)),
            FontWeights.Bold, TFCond);
        dc.DrawText(lbl, new Point(r.X + 8, r.Y + 6));

        double cx = r.X + r.Width / 2;
        double quarter = r.Height / 4;

        double topCY = r.Y + quarter;
        _kc[topIdx] = new Point(cx, topCY);
        double vTop = topK.Get(comp);
        double nTop = topK.Log ? LogN(vTop, topK.Min, topK.Max) : LinN(vTop, topK.Min, topK.Max);
        DrawKnob(dc, cx, topCY, KR, nTop, accent, topK.Label, FmtK(topK, vTop));

        dc.DrawLine(P(CBorder, 0.6),
            new Point(r.X + 8, r.Y + r.Height / 2),
            new Point(r.Right - 8, r.Y + r.Height / 2));

        double botCY = r.Y + r.Height - quarter;
        _kc[botIdx] = new Point(cx, botCY);
        double vBot = botK.Get(comp);
        double nBot = botK.Log ? LogN(vBot, botK.Min, botK.Max) : LinN(vBot, botK.Min, botK.Max);
        DrawKnob(dc, cx, botCY, KR, nBot, accent, botK.Label, FmtK(botK, vBot));
    }

    // ── Centre: gain-reduction segmented activity ring (read-only meter) ───
    private static void DrawGrDial(DrawingContext dc, double cx, double cy, double r, double grDb)
    {
        double norm = Math.Clamp(grDb, 0, 20) / 20.0;

        // Ambient glow
        var glow = new RadialGradientBrush(
            Color.FromArgb(18, CGrCol.R, CGrCol.G, CGrCol.B),
            Color.FromArgb(0,  CGrCol.R, CGrCol.G, CGrCol.B));
        glow.Freeze();
        dc.DrawEllipse(glow, null, new Point(cx, cy), r + 30, r + 30);

        // Segmented ring — green (light) → orange (moderate) → red (heavy)
        const int segs = 36;
        int lit = (int)(norm * segs);
        for (int i = 0; i < segs; i++)
        {
            double a1 = 5 * Math.PI / 4 + i       * (6 * Math.PI / 4) / segs;
            double a2 = 5 * Math.PI / 4 + (i + 1) * (6 * Math.PI / 4) / segs;
            double am = (a1 + a2) / 2;
            double sr = r + 15;
            Color sc = i < lit
                ? (i < segs * 0.55 ? CGreen : i < segs * 0.80 ? COrange : CRed)
                : Color.FromArgb(22, 0x44, 0x4C, 0x66);
            dc.DrawLine(new Pen(B(sc), 4),
                new Point(cx + (sr - 3) * Math.Cos(a1), cy - (sr - 3) * Math.Sin(a1)),
                new Point(cx + (sr + 3) * Math.Cos(am), cy - (sr + 3) * Math.Sin(am)));
        }

        // Rim + face — same construction as DrawKnob, but static (no pointer)
        var rim = new RadialGradientBrush(
            Color.FromRgb(0x3A, 0x3E, 0x4C), Color.FromRgb(0x1A, 0x1D, 0x24))
        { GradientOrigin = new Point(0.35, 0.30), Center = new Point(0.35, 0.30) };
        rim.Freeze();
        dc.DrawEllipse(rim, null, new Point(cx, cy), r + 1.5, r + 1.5);

        var face = new RadialGradientBrush(
            Color.FromRgb(0x2C, 0x31, 0x3E), Color.FromRgb(0x15, 0x18, 0x20))
        { GradientOrigin = new Point(0.35, 0.30), Center = new Point(0.35, 0.30) };
        face.Freeze();
        dc.DrawEllipse(face, P(CBorder, 1.2), new Point(cx, cy), r, r);

        // Value, centred in the dial
        var val = Txt($"-{grDb:F1}", 15, BTextPri, FontWeights.Bold, TFMono);
        dc.DrawText(val, new Point(cx - val.Width / 2, cy - val.Height / 2 - 4));
        var unit = Txt("dB GR", 8, BTextDim, tf: TFCond);
        dc.DrawText(unit, new Point(cx - unit.Width / 2, cy + val.Height / 2 - 6));

        // Label above (fixed distance, same rule as DrawKnob)
        var lbl = Txt("GAIN REDUCTION", 8.5, BTextSec, FontWeights.SemiBold, TFCond);
        dc.DrawText(lbl, new Point(cx - lbl.Width / 2, cy - r - 5 - LBL_H + (LBL_H - lbl.Height) / 2));

        // Mode label below
        string mode = grDb < 2 ? "Transparent" : grDb < 6 ? "Light" : grDb < 12 ? "Moderate" : "Heavy";
        var mt = Txt(mode, 8, B(Color.FromArgb(150, CGrCol.R, CGrCol.G, CGrCol.B)), tf: TFCond);
        dc.DrawText(mt, new Point(cx - mt.Width / 2, cy + r + 22));
    }

    private void DrawRatioSection(DrawingContext dc, CompressorEffect comp, double W, double y, double secH)
    {
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

    // ── Bottom meter strip — IN / GR / OUT bars, matching Dragon Particle's
    //    RMS / LUFS / PEAK strip (bar + gradient fill + target marker) ─────
    private static void RenderMeterStrip(DrawingContext dc, CompressorEffect comp, Rect r)
    {
        var bg = new LinearGradientBrush(
            Color.FromRgb(0x0C, 0x10, 0x16), Color.FromRgb(0x0A, 0x0D, 0x12), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 0.8), r, 4, 4);

        double barH  = 8;
        double slotH = (r.Height - 10) / 3.0;

        double inNorm  = Norm01(comp.InputLevel);
        double outNorm = Norm01(comp.OutputLevel);
        double grNorm  = Math.Clamp(comp.GainReduction, 0, 20) / 20.0;

        (string label, double norm, string val, double target, Color accent)[] meters =
        [
            ("IN",  inNorm,  $"{comp.InputLevel:F1} dB",  0.85, CGainCol),
            ("GR",  grNorm,  $"-{comp.GainReduction:F1} dB", -1,   CGrCol),
            ("OUT", outNorm, $"{comp.OutputLevel:F1} dB", 0.85, CGainCol),
        ];

        double lbW  = 32;
        double valW = 62;
        double barX = r.X + lbW + 6;
        double barW = r.Width - lbW - valW - 12;

        for (int i = 0; i < 3; i++)
        {
            var (label, norm, val, target, accent) = meters[i];
            double mY    = r.Y + 5 + i * slotH;
            double centY = mY + slotH / 2;
            double barY  = centY - barH / 2;

            var lt = Txt(label, 7.5, BTextDim, FontWeights.Bold, TFCond);
            dc.DrawText(lt, new Point(r.X + 6, centY - lt.Height / 2));

            var track = new Rect(barX, barY, barW, barH);
            dc.DrawRoundedRectangle(BSurface, null, track, 2, 2);

            if (norm > 0.005)
            {
                double fw = (barW - 4) * norm;
                Color fc = norm < 0.75 ? accent : norm < 0.92 ? COrange : CRed;
                var fb = new LinearGradientBrush(
                    Color.FromArgb(200, fc.R, fc.G, fc.B),
                    Color.FromArgb(110, fc.R, fc.G, fc.B), 0);
                fb.Freeze();
                dc.DrawRoundedRectangle(fb, null,
                    new Rect(barX + 2, barY + 2, fw, barH - 4), 1, 1);
            }

            if (target >= 0)
            {
                double tx = barX + target * (barW - 4) + 2;
                dc.DrawLine(P(Color.FromArgb(80, 0xFF, 0xFF, 0xFF), 1),
                    new Point(tx, barY - 2), new Point(tx, barY + barH + 2));
            }

            var vt = Txt(val, 8, B(accent), FontWeights.SemiBold, TFMono);
            dc.DrawText(vt, new Point(barX + barW + 6, centY - vt.Height / 2));
        }
    }

    private static double Norm01(double db) => Math.Clamp((db + 60.0) / 60.0, 0, 1);

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
        => new(Math.Max(double.IsInfinity(av.Width)  ? 520 : av.Width,  460),
               Math.Max(double.IsInfinity(av.Height) ? 480 : av.Height, 460));
}
