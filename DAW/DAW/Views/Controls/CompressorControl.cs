using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using DAW.Plugins;

namespace DAW.Views.Controls;

/// <summary>
/// Compressor control with switchable VU meter (GR / Input / Output).
/// Optimized: cached FormattedText, no per-frame allocations, timer-driven refresh.
/// </summary>
public sealed class CompressorControl : FrameworkElement
{
    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(CompressorEffect), typeof(CompressorControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnEffectChanged));

    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(CompressorControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public CompressorEffect? Effect
    {
        get => (CompressorEffect?)GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    // ── Theme palette (rebuilt per theme) ────────────────────────────────────

    private static string? _paletteTheme;
    private static Brush BgBrush       = null!;
    private static Brush PanelBrush    = null!;
    private static Brush SurfaceBrush  = null!;
    private static Pen   BorderPen     = null!;
    private static Brush AccentBrush   = null!;
    private static Brush TextPrimary   = null!;
    private static Brush TextSecondary = null!;
    private static Brush TextDim       = null!;

    // Meter
    private static Brush MeterBg       = null!;
    private static Pen   MeterBorderPn = null!;
    private static Brush GrBrush       = null!;
    private static Brush InBrush       = null!;
    private static Brush OutBrush      = null!;
    private static Pen   NeedlePenGr   = null!;
    private static Pen   NeedleGlowGr  = null!;
    private static Pen   NeedlePenIn   = null!;
    private static Pen   NeedleGlowIn  = null!;
    private static Pen   NeedlePenOut  = null!;
    private static Pen   NeedleGlowOut = null!;
    private static Brush PivotBrush    = null!;
    private static Pen   ScaleTickPn   = null!;
    private static Pen   ScaleMajorPn  = null!;
    private static Brush ScaleLblBr    = null!;

    // Knobs
    private static Brush KnobBg        = null!;
    private static Pen   KnobRingPn    = null!;
    private static Pen   KnobPtrPn     = null!;
    private static Pen   KnobArcBgPn   = null!;
    private static Pen   KnobArcFgPn   = null!;

    // Ratio / mode buttons
    private static Brush BtnOff        = null!;
    private static Brush BtnOn         = null!;
    private static Pen   BtnBorderOff  = null!;
    private static Pen   BtnBorderOn   = null!;

    private static readonly Typeface Sans = new("Segoe UI");
    private static readonly Typeface Mono = new("Consolas");

    private static void EnsurePalette()
    {
        var themeId = Services.ThemeService.Instance.CurrentTheme;
        if (_paletteTheme == themeId && BgBrush != null) return;
        _paletteTheme = themeId;

        BgBrush       = F(new SolidColorBrush(PluginTheme.DcBg));
        PanelBrush    = F(new SolidColorBrush(PluginTheme.DcPanel));
        SurfaceBrush  = F(new SolidColorBrush(PluginTheme.DcSurface));
        BorderPen     = FP(PluginTheme.DcBorder, 1);
        AccentBrush   = F(new SolidColorBrush(PluginTheme.DcAccent));
        TextPrimary   = F(new SolidColorBrush(PluginTheme.DcTextPrimary));
        TextSecondary = F(new SolidColorBrush(PluginTheme.DcTextSecondary));
        TextDim       = F(new SolidColorBrush(PluginTheme.DcTextDim));

        MeterBg       = F(new SolidColorBrush(PluginTheme.DcMeterBg));
        MeterBorderPn = FP(PluginTheme.DcMeterBorder, 1);
        GrBrush       = F(new SolidColorBrush(PluginTheme.DcGrColor));
        InBrush       = F(new SolidColorBrush(PluginTheme.DcInColor));
        OutBrush      = F(new SolidColorBrush(PluginTheme.DcOutColor));
        NeedlePenGr   = FP(PluginTheme.DcGrColor, 1.5);
        NeedleGlowGr  = FP(Color.FromArgb(40, PluginTheme.DcGrColor.R, PluginTheme.DcGrColor.G, PluginTheme.DcGrColor.B), 3);
        NeedlePenIn   = FP(PluginTheme.DcInColor, 1.5);
        NeedleGlowIn  = FP(Color.FromArgb(40, PluginTheme.DcInColor.R, PluginTheme.DcInColor.G, PluginTheme.DcInColor.B), 3);
        NeedlePenOut  = FP(PluginTheme.DcOutColor, 1.5);
        NeedleGlowOut = FP(Color.FromArgb(40, PluginTheme.DcOutColor.R, PluginTheme.DcOutColor.G, PluginTheme.DcOutColor.B), 3);
        PivotBrush    = F(new SolidColorBrush(PluginTheme.DcBorder));
        ScaleTickPn   = FP(PluginTheme.DcScaleTick, 0.8);
        ScaleMajorPn  = FP(PluginTheme.DcScaleMajor, 1);
        ScaleLblBr    = F(new SolidColorBrush(PluginTheme.DcScaleLabel));

        var kb = new RadialGradientBrush(PluginTheme.DcKnobLight, PluginTheme.DcKnobDark)
            { GradientOrigin = new Point(0.35, 0.35) };
        kb.Freeze();
        KnobBg        = kb;
        KnobRingPn    = FP(PluginTheme.DcKnobRing, 1.5);
        KnobPtrPn     = FP(PluginTheme.DcAccent, 2);
        KnobArcBgPn   = FP(PluginTheme.DcKnobArcBg, 2.5);
        KnobArcFgPn   = FP(PluginTheme.DcAccent, 2.5);

        BtnOff        = F(new SolidColorBrush(PluginTheme.DcBtnOff));
        BtnOn         = F(new SolidColorBrush(PluginTheme.DcBtnOn));
        BtnBorderOff  = FP(PluginTheme.DcBtnBorderOff, 1);
        BtnBorderOn   = FP(PluginTheme.DcBtnBorderOn, 1);
    }

    private static T F<T>(T b) where T : Freezable { b.Freeze(); return b; }
    private static Pen FP(Color c, double t) { var p = new Pen(new SolidColorBrush(c), t); p.Freeze(); return p; }

    // ── Knob defs ─────────────────────────────────────────────────────────

    private record struct KnobDef(string Label, double Min, double Max,
        Func<CompressorEffect, double> Get, Action<CompressorEffect, double> Set,
        string Unit, bool IsLog = false);

    private static readonly KnobDef[] Knobs =
    [
        new("INPUT",   -12, 48,   e => e.InputGain,  (e, v) => e.InputGain = v,  "dB"),
        new("ATTACK",  0.02, 100, e => e.Attack,     (e, v) => e.Attack = v,     "ms", IsLog: true),
        new("RELEASE", 10, 1200,  e => e.Release,    (e, v) => e.Release = v,    "ms", IsLog: true),
        new("OUTPUT",  -24, 12,   e => e.OutputGain, (e, v) => e.OutputGain = v, "dB"),
    ];

    // ── State ─────────────────────────────────────────────────────────────

    private int _dragKnobIndex = -1;
    private double _dragStartY, _dragStartVal;
    private Rect[]? _ratioRects;
    private Rect[]? _modeRects;
    private Point[]? _knobCenters;
    private double _knobR;
    private readonly DispatcherTimer _timer;

    public CompressorControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = true;

        // Single timer drives UI at ~30fps; no per-property invalidation needed
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private static void OnEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // No per-property subscription needed — timer handles refresh
        ((CompressorControl)d).InvalidateVisual();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RENDERING
    // ══════════════════════════════════════════════════════════════════════

    protected override void OnRender(DrawingContext dc)
    {
        EnsurePalette();
        double w = ActualWidth, h = ActualHeight;
        if (w < 20 || h < 20) return;

        var comp = Effect;
        if (comp == null) { dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h)); return; }

        if (IsCompact) { DrawCompact(dc, comp, w, h); return; }
        DrawFull(dc, comp, w, h);
    }

    // ── Full layout ───────────────────────────────────────────────────────

    private void DrawFull(DrawingContext dc, CompressorEffect comp, double w, double h)
    {
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        var panel = new Rect(6, 6, w - 12, h - 12);
        dc.DrawRoundedRectangle(PanelBrush, BorderPen, panel, 6, 6);

        // Header
        var hdr = new Rect(panel.X, panel.Y, panel.Width, 28);
        dc.DrawRoundedRectangle(SurfaceBrush, null, hdr, 6, 6);
        dc.DrawRectangle(SurfaceBrush, null, new Rect(hdr.X, hdr.Bottom - 6, hdr.Width, 6));

        var title = Txt("COMPRESSOR", 10, TextPrimary, FontWeights.SemiBold);
        dc.DrawText(title, new Point(hdr.X + 12, hdr.Y + (28 - title.Height) / 2));

        double grVal = Math.Clamp(comp.GainReduction, 0, 30);
        var grDisp = Txt($"-{grVal:F1} dB", 10, GrBrush, FontWeights.Bold, Mono);
        dc.DrawText(grDisp, new Point(hdr.Right - grDisp.Width - 12, hdr.Y + (28 - grDisp.Height) / 2));

        // Layout
        double mTop = hdr.Bottom + 8;
        double mH = Math.Min(h * 0.30, 100);
        double mW = Math.Min(panel.Width - 48, 280);
        var mRect = new Rect(panel.X + (panel.Width - mW) / 2, mTop, mW, mH);

        // Meter mode buttons (above meter, right-aligned)
        DrawModeButtons(dc, comp, mRect);

        // Meter
        DrawMeter(dc, comp, mRect);

        // Level bars
        DrawLevelBar(dc, comp.InputLevel, mRect.X - 14, mRect.Y + 4, 6, mRect.Height - 8, InBrush, "IN");
        DrawLevelBar(dc, comp.OutputLevel, mRect.Right + 8, mRect.Y + 4, 6, mRect.Height - 8, OutBrush, "OUT");

        // Knobs
        double knobRowY = mRect.Bottom + 14;
        double knobR = Math.Clamp((panel.Width - 60) / 12, 14, 26);
        double ratioY = knobRowY + knobR * 2 + 30;
        double knobSp = (panel.Width - 24) / Knobs.Length;
        _knobCenters = new Point[Knobs.Length];
        _knobR = knobR;
        for (int i = 0; i < Knobs.Length; i++)
        {
            double cx = panel.X + 12 + knobSp * i + knobSp / 2;
            double cy = knobRowY + knobR;
            _knobCenters[i] = new Point(cx, cy);
            DrawKnob(dc, comp, Knobs[i], cx, cy, knobR);
        }

        // Ratio buttons
        DrawRatioButtons(dc, comp, panel, ratioY);
    }

    // ── Meter mode buttons ────────────────────────────────────────────────

    private void DrawModeButtons(DrawingContext dc, CompressorEffect comp, Rect meterRect)
    {
        string[] labels = ["GR", "IN", "OUT"];
        CompressorMeterMode[] modes = [CompressorMeterMode.GainReduction, CompressorMeterMode.Input, CompressorMeterMode.Output];
        double btnW = 28, btnH = 14, gap = 3;
        double totalW = labels.Length * btnW + (labels.Length - 1) * gap;
        double startX = meterRect.Right - totalW;
        double y = meterRect.Y - btnH - 3;

        _modeRects = new Rect[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            bool active = comp.MeterMode == modes[i];
            var r = new Rect(startX + i * (btnW + gap), y, btnW, btnH);
            _modeRects[i] = r;

            dc.DrawRoundedRectangle(active ? BtnOn : BtnOff, active ? BtnBorderOn : BtnBorderOff, r, 2, 2);
            var t = Txt(labels[i], 7, active ? TextPrimary : TextDim, active ? FontWeights.Bold : FontWeights.Normal);
            dc.DrawText(t, new Point(r.X + (r.Width - t.Width) / 2, r.Y + (r.Height - t.Height) / 2));
        }
    }

    // ── VU Meter (mode-aware) ─────────────────────────────────────────────

    private void DrawMeter(DrawingContext dc, CompressorEffect comp, Rect r)
    {
        dc.DrawRoundedRectangle(MeterBg, MeterBorderPn, r, 4, 4);
        var inner = new Rect(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);

        var mode = comp.MeterMode;

        // Mode-specific config
        string meterLabel;
        double meterValue, meterMax;
        Brush valueBrush;
        Pen needlePen, glowPen;
        bool rightToLeft;

        switch (mode)
        {
            case CompressorMeterMode.Input:
                meterLabel = "INPUT LEVEL";
                meterValue = Math.Clamp(comp.InputLevel + 60, 0, 60); // 0..60 range
                meterMax = 60;
                valueBrush = InBrush;
                needlePen = NeedlePenIn;
                glowPen = NeedleGlowIn;
                rightToLeft = false;
                break;
            case CompressorMeterMode.Output:
                meterLabel = "OUTPUT LEVEL";
                meterValue = Math.Clamp(comp.OutputLevel + 60, 0, 60);
                meterMax = 60;
                valueBrush = OutBrush;
                needlePen = NeedlePenOut;
                glowPen = NeedleGlowOut;
                rightToLeft = false;
                break;
            default: // GainReduction
                meterLabel = "GAIN REDUCTION";
                meterValue = Math.Clamp(comp.GainReduction, 0, 20);
                meterMax = 20;
                valueBrush = GrBrush;
                needlePen = NeedlePenGr;
                glowPen = NeedleGlowGr;
                rightToLeft = true;
                break;
        }

        // Label
        var lbl = Txt(meterLabel, 7, TextDim);
        dc.DrawText(lbl, new Point(inner.X + inner.Width / 2 - lbl.Width / 2, inner.Y + 2));

        // Arc needle
        double pivotX = inner.X + inner.Width / 2;
        double pivotY = inner.Bottom - 6;
        double arcR = Math.Min(inner.Width * 0.42, inner.Height - 20);
        double arcStart, arcEnd;

        if (rightToLeft)
        {
            arcStart = Math.PI * 0.82; arcEnd = Math.PI * 0.18;
        }
        else
        {
            arcStart = Math.PI * 0.18; arcEnd = Math.PI * 0.82;
        }

        // Scale ticks
        int numTicks = mode == CompressorMeterMode.GainReduction ? 10 : 7;
        double[] grDbs = [0, 1, 2, 3, 4, 5, 7, 10, 15, 20];
        double[] levelDbs = [-60, -48, -36, -24, -12, -6, 0];

        if (mode == CompressorMeterMode.GainReduction)
        {
            foreach (var db in grDbs)
            {
                double t = Math.Clamp(db / meterMax, 0, 1);
                DrawScaleTick(dc, pivotX, pivotY, arcR, arcStart, arcEnd, t, db,
                    db == 0 || db == 5 || db == 10 || db == 20, $"{db}");
            }
        }
        else
        {
            foreach (var db in levelDbs)
            {
                double t = Math.Clamp((db + 60) / meterMax, 0, 1);
                bool major = db == -60 || db == -24 || db == -12 || db == 0;
                DrawScaleTick(dc, pivotX, pivotY, arcR, arcStart, arcEnd, t, 0, major, $"{db}");
            }
        }

        // Needle
        double norm = Math.Clamp(meterValue / meterMax, 0, 1);
        double needleAngle = arcStart + norm * (arcEnd - arcStart);
        double nLen = arcR - 2;
        double nx = pivotX + nLen * Math.Cos(needleAngle);
        double ny = pivotY - nLen * Math.Sin(needleAngle);

        dc.DrawLine(glowPen, new Point(pivotX, pivotY), new Point(nx, ny));
        dc.DrawLine(needlePen, new Point(pivotX, pivotY), new Point(nx, ny));
        dc.DrawEllipse(PivotBrush, BorderPen, new Point(pivotX, pivotY), 3, 3);

        // Value readout
        string valStr = mode == CompressorMeterMode.GainReduction
            ? $"-{meterValue:F1} dB"
            : $"{(meterValue - 60):F1} dB";
        var valTxt = Txt(valStr, 9, valueBrush, FontWeights.Bold, Mono);
        dc.DrawText(valTxt, new Point(pivotX - valTxt.Width / 2, pivotY - arcR - 16));

        // Horizontal bar below arc
        double barH = 4, barY2 = pivotY + 2;
        double barMaxW = inner.Width - 16;
        dc.DrawRoundedRectangle(SurfaceBrush, null, new Rect(inner.X + 8, barY2, barMaxW, barH), 2, 2);
        double barW = barMaxW * norm;
        if (barW > 1)
        {
            double bx = rightToLeft ? inner.Right - 8 - barW : inner.X + 8;
            dc.DrawRoundedRectangle(valueBrush, null, new Rect(bx, barY2, barW, barH), 2, 2);
        }
    }

    private void DrawScaleTick(DrawingContext dc, double px, double py, double arcR,
        double arcStart, double arcEnd, double t, double _, bool major, string label)
    {
        double angle = arcStart + t * (arcEnd - arcStart);
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        double r1 = arcR + 2, r2 = arcR + (major ? 7 : 4);
        dc.DrawLine(major ? ScaleMajorPn : ScaleTickPn,
            new Point(px + r1 * cos, py - r1 * sin),
            new Point(px + r2 * cos, py - r2 * sin));

        if (major)
        {
            var txt = Txt(label, 7, ScaleLblBr, FontWeights.Normal, Mono);
            dc.DrawText(txt, new Point(
                px + (r2 + 5) * cos - txt.Width / 2,
                py - (r2 + 5) * sin - txt.Height / 2));
        }
    }

    // ── Level bar ─────────────────────────────────────────────────────────

    private static void DrawLevelBar(DrawingContext dc, double levelDb, double x, double y, double bw, double bh, Brush fill, string label)
    {
        dc.DrawRoundedRectangle(MeterBg, null, new Rect(x, y, bw, bh), 2, 2);
        double level = Math.Clamp((levelDb + 60) / 60.0, 0, 1);
        double barH = level * (bh - 4);
        if (barH > 1)
            dc.DrawRoundedRectangle(fill, null, new Rect(x + 1, y + bh - 2 - barH, bw - 2, barH), 1, 1);
        var txt = Txt(label, 6, TextDim);
        dc.DrawText(txt, new Point(x + bw / 2 - txt.Width / 2, y + bh + 1));
    }

    // ── Knob ──────────────────────────────────────────────────────────────

    private static void DrawKnob(DrawingContext dc, CompressorEffect comp, KnobDef knob, double cx, double cy, double r)
    {
        double value = knob.Get(comp);
        double norm = knob.IsLog
            ? (Math.Log10(Math.Max(value, 0.001)) - Math.Log10(Math.Max(knob.Min, 0.001)))
              / (Math.Log10(knob.Max) - Math.Log10(Math.Max(knob.Min, 0.001)))
            : (value - knob.Min) / (knob.Max - knob.Min);
        norm = Math.Clamp(norm, 0, 1);

        double aMin = 5.0 * Math.PI / 4.0, aMax = -Math.PI / 4.0;
        double angle = aMin + norm * (aMax - aMin);

        DrawArc(dc, cx, cy, r + 4, aMin, aMax, KnobArcBgPn);
        DrawArc(dc, cx, cy, r + 4, aMin, angle, KnobArcFgPn);

        for (int t = 0; t <= 10; t++)
        {
            double ta = aMin + (double)t / 10 * (aMax - aMin);
            dc.DrawLine(ScaleTickPn,
                new Point(cx + (r + 7) * Math.Cos(ta), cy - (r + 7) * Math.Sin(ta)),
                new Point(cx + (r + 10) * Math.Cos(ta), cy - (r + 10) * Math.Sin(ta)));
        }

        dc.DrawEllipse(KnobBg, KnobRingPn, new Point(cx, cy), r, r);

        double pI = r * 0.25, pO = r * 0.85;
        dc.DrawLine(KnobPtrPn,
            new Point(cx + pI * Math.Cos(angle), cy - pI * Math.Sin(angle)),
            new Point(cx + pO * Math.Cos(angle), cy - pO * Math.Sin(angle)));

        var lbl = Txt(knob.Label, 8, TextSecondary, FontWeights.SemiBold);
        dc.DrawText(lbl, new Point(cx - lbl.Width / 2, cy - r - 18));

        string valStr = knob.Unit == "ms" && value < 1 ? $"{value * 1000:F0}µs"
            : knob.Unit == "ms" ? $"{value:F0}ms" : $"{value:F1}{knob.Unit}";
        var val = Txt(valStr, 8, AccentBrush, FontWeights.SemiBold, Mono);
        dc.DrawText(val, new Point(cx - val.Width / 2, cy + r + 8));
    }

    private static void DrawArc(DrawingContext dc, double cx, double cy, double r, double from, double to, Pen pen)
    {
        const int steps = 20;
        for (int s = 0; s < steps; s++)
        {
            double a1 = from + (to - from) * s / steps;
            double a2 = from + (to - from) * (s + 1) / steps;
            dc.DrawLine(pen,
                new Point(cx + r * Math.Cos(a1), cy - r * Math.Sin(a1)),
                new Point(cx + r * Math.Cos(a2), cy - r * Math.Sin(a2)));
        }
    }

    // ── Ratio buttons ─────────────────────────────────────────────────────

    private void DrawRatioButtons(DrawingContext dc, CompressorEffect comp, Rect panel, double y)
    {
        var label = Txt("RATIO", 8, TextSecondary, FontWeights.SemiBold);
        dc.DrawText(label, new Point(panel.X + panel.Width / 2 - label.Width / 2, y - 14));

        double btnW = 40, btnH = 22, gap = 5;
        int count = CompressorEffect.PresetRatios.Length;
        double totalW = count * btnW + (count - 1) * gap;
        double startX = panel.X + (panel.Width - totalW) / 2;

        _ratioRects = new Rect[count];
        for (int i = 0; i < count; i++)
        {
            double ratio = CompressorEffect.PresetRatios[i];
            bool active = Math.Abs(comp.Ratio - ratio) < 0.5;
            var rect = new Rect(startX + i * (btnW + gap), y, btnW, btnH);
            _ratioRects[i] = rect;

            dc.DrawRoundedRectangle(active ? BtnOn : BtnOff, active ? BtnBorderOn : BtnBorderOff, rect, 3, 3);
            var txt = Txt($"{ratio:F0}:1", 9, active ? TextPrimary : TextSecondary,
                active ? FontWeights.Bold : FontWeights.Normal);
            dc.DrawText(txt, new Point(rect.X + (rect.Width - txt.Width) / 2, rect.Y + (rect.Height - txt.Height) / 2));
        }
    }

    // ── Compact mode ──────────────────────────────────────────────────────

    private static void DrawCompact(DrawingContext dc, CompressorEffect comp, double w, double h)
    {
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

        double gr = Math.Clamp(comp.GainReduction, 0, 30);
        double barY = 4, barH = Math.Min(20, h * 0.35);
        dc.DrawRoundedRectangle(MeterBg, null, new Rect(4, barY, w - 8, barH), 2, 2);
        double barW = (w - 12) * Math.Clamp(gr / 30.0, 0, 1);
        if (barW > 1)
            dc.DrawRoundedRectangle(GrBrush, null, new Rect(w - 4 - barW, barY + 2, barW, barH - 4), 1, 1);

        dc.DrawText(Txt($"GR: -{gr:F1}dB", 8, GrBrush, FontWeights.Bold, Mono), new Point(8, barY + (barH - 10) / 2));
        var ratT = Txt($"{comp.Ratio:F0}:1", 8, AccentBrush, FontWeights.Bold, Mono);
        dc.DrawText(ratT, new Point(w - ratT.Width - 8, barY + (barH - 10) / 2));

        double infoY = barY + barH + 4;
        dc.DrawText(Txt($"In:{comp.InputGain:+0;-0}dB", 7, InBrush), new Point(6, infoY));
        dc.DrawText(Txt($"Out:{comp.OutputGain:+0;-0}dB", 7, OutBrush), new Point(w / 3, infoY));

        if (h > infoY + 26)
        {
            double r2 = infoY + 13;
            dc.DrawText(Txt($"Atk:{comp.Attack:F0}ms", 7, TextDim), new Point(6, r2));
            dc.DrawText(Txt($"Rel:{comp.Release:F0}ms", 7, TextDim), new Point(w / 2, r2));
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INTERACTION
    // ══════════════════════════════════════════════════════════════════════

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Effect == null || IsCompact) return;
        var pos = e.GetPosition(this);

        // Mode buttons
        if (_modeRects != null)
        {
            CompressorMeterMode[] modes = [CompressorMeterMode.GainReduction, CompressorMeterMode.Input, CompressorMeterMode.Output];
            for (int i = 0; i < _modeRects.Length; i++)
            {
                if (_modeRects[i].Contains(pos))
                {
                    Effect.MeterMode = modes[i];
                    e.Handled = true;
                    return;
                }
            }
        }

        // Ratio buttons
        if (_ratioRects != null)
        {
            for (int i = 0; i < _ratioRects.Length; i++)
            {
                if (_ratioRects[i].Contains(pos))
                {
                    Effect.Ratio = CompressorEffect.PresetRatios[i];
                    e.Handled = true;
                    return;
                }
            }
        }

        // Knobs
        if (_knobCenters != null)
        {
            for (int i = 0; i < _knobCenters.Length; i++)
            {
                double dx = pos.X - _knobCenters[i].X, dy = pos.Y - _knobCenters[i].Y;
                if (dx * dx + dy * dy <= (_knobR + 10) * (_knobR + 10))
                {
                    _dragKnobIndex = i;
                    _dragStartY = pos.Y;
                    _dragStartVal = Knobs[i].Get(Effect);
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragKnobIndex < 0 || Effect == null) return;
        var knob = Knobs[_dragKnobIndex];
        double dy = _dragStartY - e.GetPosition(this).Y;

        if (knob.IsLog)
        {
            double lMin = Math.Log10(Math.Max(knob.Min, 0.001)), lMax = Math.Log10(knob.Max);
            double lStart = Math.Log10(Math.Max(_dragStartVal, 0.001));
            knob.Set(Effect, Math.Pow(10, Math.Clamp(lStart + dy * (lMax - lMin) / 200.0, lMin, lMax)));
        }
        else
        {
            knob.Set(Effect, Math.Clamp(_dragStartVal + dy * (knob.Max - knob.Min) / 200.0, knob.Min, knob.Max));
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragKnobIndex >= 0) { _dragKnobIndex = -1; ReleaseMouseCapture(); e.Handled = true; }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (Effect == null || IsCompact || _knobCenters == null) return;
        var pos = e.GetPosition(this);
        for (int i = 0; i < _knobCenters.Length; i++)
        {
            double dx = pos.X - _knobCenters[i].X, dy = pos.Y - _knobCenters[i].Y;
            if (dx * dx + dy * dy <= (_knobR + 12) * (_knobR + 12))
            {
                var k = Knobs[i];
                k.Set(Effect, k.Label switch { "INPUT" => 0, "OUTPUT" => 0, "ATTACK" => 10, "RELEASE" => 100, _ => (k.Min + k.Max) / 2 });
                e.Handled = true;
                return;
            }
        }
    }

    // ── Text helper ───────────────────────────────────────────────────────

    private static FormattedText Txt(string text, double size, Brush brush,
        FontWeight? weight = null, Typeface? tf = null)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf ?? Sans, size, brush, 1.0);
        if (weight.HasValue) ft.SetFontWeight(weight.Value);
        return ft;
    }
}
