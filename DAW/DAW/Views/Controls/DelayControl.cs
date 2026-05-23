using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using static DAW.Views.Controls.DragonUI;

namespace DAW.Views.Controls;

/// <summary>
/// DragonDAW Delay — Valhalla Delay–inspired UI in the Red Dragon style.
///
/// Layout:
///   Header  (mode selector)
///   ─────────────────────────────────────────────────────────
///   Row 1 — TIMING      │  TIME    RATIO    (+ SYNC badge)
///   Row 2 — FEEDBACK    │  FBACK   CROSS
///   Row 3 — TONE        │  LO CUT  HI CUT
///   Row 4 — MODULATION  │  MOD R   MOD D   (+ shape btns)
///   Row 5 — MIX         │  DRY     WET
/// </summary>
public sealed class DelayControl : FrameworkElement
{
    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(DelayEffect), typeof(DelayControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((DelayControl)d).InvalidateVisual()));

    public DelayEffect? Effect
    {
        get => (DelayEffect?)GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    // ── Knob descriptor ───────────────────────────────────────────────────
    private record struct KD(
        string Label, double Min, double Max,
        Func<DelayEffect, double> Get,
        Action<DelayEffect, double> Set,
        string Unit, double Default,
        bool IsLog = false);

    // Ordered: TIME, RATIO, FEEDBACK, CROSS, LO CUT, HI CUT, MOD R, MOD D, DRY, WET
    private static readonly KD[] Knobs =
    [
        new("TIME",    1,    2000,   e => e.DelayTime,     (e,v) => e.DelayTime     = v, "ms",  250, IsLog: true),
        new("RATIO",   0.25, 4.0,    e => e.Ratio,         (e,v) => e.Ratio         = v, "×",   1.0, IsLog: true),
        new("FBACK",   0,    0.95,   e => e.Feedback,      (e,v) => e.Feedback      = v, "%",   0.4),
        new("CROSS",   0,    0.95,   e => e.CrossFeedback, (e,v) => e.CrossFeedback = v, "%",   0.0),
        new("LO CUT",  20,   2000,   e => e.LowCut,        (e,v) => e.LowCut        = v, "Hz",  20,  IsLog: true),
        new("HI CUT",  1000, 20000,  e => e.HighCut,       (e,v) => e.HighCut       = v, "Hz",  20000, IsLog: true),
        new("MOD R",   0.01, 10,     e => e.ModRate,       (e,v) => e.ModRate       = v, "Hz",  0.5, IsLog: true),
        new("MOD D",   0,    0.50,   e => e.ModDepth,      (e,v) => e.ModDepth      = v, "%",   0.0),
        new("DRY",     0,    1,      e => e.DryLevel,      (e,v) => e.DryLevel      = v, "%",   1.0),
        new("WET",     0,    1,      e => e.WetLevel,      (e,v) => e.WetLevel      = v, "%",   0.5),
    ];

    // Sections: (label, colour, knob indices)
    private static readonly (string Label, Color Accent, int[] KI)[] Sections =
    [
        ("TIMING",     CGold,    [0, 1]),
        ("FEEDBACK",   CRed,     [2, 3]),
        ("TONE",       CTextSec, [4, 5]),
        ("MODULATION", CRed,     [6, 7]),
        ("MIX",        CGold,    [8, 9]),
    ];

    // ── State ─────────────────────────────────────────────────────────────
    private int    _dk = -1;
    private double _dy, _db;
    private Point[] _kc = new Point[Knobs.Length];
    private double[] _kr = new double[Knobs.Length];

    // Mode + shape button hit rects
    private Rect   _modePrev, _modeNext, _modeRect;
    private Rect[] _shapeBtns = new Rect[3];

    public DelayControl()
    {
        ClipToBounds = SnapsToDevicePixels = Focusable = true;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RENDER
    // ══════════════════════════════════════════════════════════════════════

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W < 30 || H < 30) return;
        var fx = Effect;

        // Background
        dc.DrawRoundedRectangle(BBg, PBorder, new Rect(0, 0, W, H), 6, 6);
        DrawScrew(dc, 8, 8);   DrawScrew(dc, W - 8, 8);
        DrawScrew(dc, 8, H-8); DrawScrew(dc, W - 8, H-8);

        // ── Header ──
        double hdrH = 38;
        DrawHeader(dc, new Rect(0, 0, W, hdrH), "DELAY", "Stereo Echo");

        // Mode selector (centre of header)
        string modeName = fx != null && fx.DelayMode >= 0 && fx.DelayMode < DelayEffect.ModeNames.Length
            ? DelayEffect.ModeNames[fx.DelayMode] : "Normal";
        double modeW = 90;
        _modeRect = new Rect(W / 2 - modeW / 2, 8, modeW, hdrH - 16);
        double arW = 18;
        _modePrev = new Rect(_modeRect.X - arW - 3, _modeRect.Y, arW, _modeRect.Height);
        _modeNext = new Rect(_modeRect.Right + 3,   _modeRect.Y, arW, _modeRect.Height);
        dc.DrawRoundedRectangle(BSurface, PBorder,    _modePrev, 3, 3);
        dc.DrawRoundedRectangle(BSurface, PBorderRed, _modeRect, 3, 3);
        dc.DrawRoundedRectangle(BSurface, PBorder,    _modeNext, 3, 3);
        //var pT = T("◄", 8, BRed); dc.DrawText(pT, new Point(_modePrev.X + (_modePrev.Width - pT.Width)/2, _modePrev.Y + (_modePrev.Height - pT.Height)/2));
        //var nT = T("►", 8, BRed); dc.DrawText(nT, new Point(_modeNext.X + (_modeNext.Width - nT.Width)/2, _modeNext.Y + (_modeNext.Height - nT.Height)/2));
        //var mT = T(modeName, 9, BTextPri, FontWeights.SemiBold, TFCond);
        //dc.DrawText(mT, new Point(W/2 - mT.Width/2, hdrH/2 - mT.Height/2));

        // ── Section rows ──
        double pad    = 8;
        double usableH = H - hdrH - pad * 2;
        double secH   = usableH / Sections.Length;
        double kR     = Math.Clamp(secH * 0.30, 14, 26);

        for (int s = 0; s < Sections.Length; s++)
        {
            var (label, accent, ki) = Sections[s];
            double sY = hdrH + pad + secH * s;
            var sRect = new Rect(pad, sY, W - pad * 2, secH - 3);
            DrawSection(dc, sRect, label, accent);

            // Two knobs side by side inside section
            double knobAreaW = sRect.Width - 12;
            double kSp = knobAreaW / ki.Length;
            double kCy = sY + 22 + kR;

            for (int j = 0; j < ki.Length; j++)
            {
                int idx = ki[j];
                double cx = sRect.X + 6 + kSp * j + kSp / 2;
                _kc[idx] = new Point(cx, kCy);
                _kr[idx] = kR;

                if (fx == null) continue;
                var k = Knobs[idx];
                double v = k.Get(fx);
                double n = k.IsLog ? LogN(k, v) : LinN(k, v);
                DrawKnob(dc, cx, kCy, kR, Math.Clamp(n, 0, 1), accent, k.Label, FmtK(k, v));
            }

            // MOD shape buttons (only in Modulation section)
            if (label == "MODULATION" && fx != null)
            {
                double sbW = 38, sbH = 16, sbGap = 4;
                double totalSB = 3 * sbW + 2 * sbGap;
                double sbX = sRect.Right - totalSB - 8;
                double sbY = kCy - sbH / 2;
                string[] shapeLabels = ["Sine", "Tri", "S&H"];
                for (int b = 0; b < 3; b++)
                {
                    _shapeBtns[b] = new Rect(sbX + b * (sbW + sbGap), sbY, sbW, sbH);
                    DrawButton(dc, _shapeBtns[b], shapeLabels[b], fx.ModShape == b, 7.5);
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static double LogN(KD k, double v)
        => (Math.Log10(Math.Max(v, 1e-9)) - Math.Log10(Math.Max(k.Min, 1e-9)))
         / (Math.Log10(k.Max) - Math.Log10(Math.Max(k.Min, 1e-9)));

    private static double LinN(KD k, double v) => (v - k.Min) / (k.Max - k.Min);

    private static string FmtK(KD k, double v) => k.Unit switch
    {
        "ms" => v >= 1000 ? $"{v/1000:F2}s" : $"{v:F0}ms",
        "%"  => $"{v*100:F0}%",
        "Hz" => v >= 1000 ? $"{v/1000:F1}k" : $"{v:F0}Hz",
        "×"  => $"{v:F2}×",
        _    => $"{v:F2}"
    };

    // ══════════════════════════════════════════════════════════════════════
    //  INTERACTION
    // ══════════════════════════════════════════════════════════════════════

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var fx = Effect; if (fx == null) return;
        var p = e.GetPosition(this);

        // Mode arrows
        if (_modePrev.Contains(p)) { fx.DelayMode = (fx.DelayMode - 1 + DelayEffect.ModeNames.Length) % DelayEffect.ModeNames.Length; e.Handled = true; return; }
        if (_modeNext.Contains(p) || _modeRect.Contains(p)) { fx.DelayMode = (fx.DelayMode + 1) % DelayEffect.ModeNames.Length; e.Handled = true; return; }

        // Shape buttons
        for (int b = 0; b < _shapeBtns.Length; b++)
            if (_shapeBtns[b].Contains(p)) { fx.ModShape = b; e.Handled = true; return; }

        // Knob drag start
        for (int i = 0; i < _kc.Length; i++)
        {
            var d = p - _kc[i];
            double hr = _kr[i] + 10;
            if (d.X * d.X + d.Y * d.Y <= hr * hr)
            {
                _dk = i; _dy = p.Y; _db = Knobs[i].Get(fx);
                CaptureMouse(); e.Handled = true; return;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dk < 0 || Effect == null) return;
        var k  = Knobs[_dk];
        double dy = _dy - e.GetPosition(this).Y;
        bool   sh = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        double sens = sh ? 600 : 200;
        if (k.IsLog)
        {
            double lMin = Math.Log10(Math.Max(k.Min, 1e-9)), lMax = Math.Log10(k.Max);
            k.Set(Effect, Math.Pow(10, Math.Clamp(Math.Log10(Math.Max(_db, 1e-9)) + dy * (lMax - lMin) / sens, lMin, lMax)));
        }
        else
        {
            k.Set(Effect, Math.Clamp(_db + dy * (k.Max - k.Min) / sens, k.Min, k.Max));
        }
        InvalidateVisual(); e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonUp(e); if (_dk >= 0) { _dk = -1; ReleaseMouseCapture(); e.Handled = true; } }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (Effect == null) return;
        var p = e.GetPosition(this);
        for (int i = 0; i < _kc.Length; i++)
        {
            var d = p - _kc[i]; double hr = _kr[i] + 12;
            if (d.X * d.X + d.Y * d.Y <= hr * hr)
            { Knobs[i].Set(Effect, Knobs[i].Default); InvalidateVisual(); e.Handled = true; return; }
        }
    }

    protected override Size MeasureOverride(Size av)
        => new(Math.Max(double.IsInfinity(av.Width)  ? 420 : av.Width,  360),
               Math.Max(double.IsInfinity(av.Height) ? 480 : av.Height, 420));
}
