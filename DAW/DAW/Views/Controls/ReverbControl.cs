using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using DAW.Plugins;

namespace DAW.Views.Controls;

/// <summary>
/// Valhalla Room–inspired reverb UI with rotary knobs.
/// Sections: Global (top), Early (left), Late (right), Tone (bottom-center).
/// </summary>
public sealed class ReverbControl : FrameworkElement
{
    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(ReverbEffect), typeof(ReverbControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ReverbEffect? Effect
    {
        get => (ReverbEffect?)GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    // ── Theme palette (rebuilt per theme) ──────────────────────────────────

    private static string? _paletteTheme;
    private static Brush BgBrush      = null!;
    private static Brush PanelBrush   = null!;
    private static Brush SurfaceBrush = null!;
    private static Brush SectionBrush = null!;
    private static Pen   BorderPen    = null!;
    private static Pen   SectionPen   = null!;
    private static Brush AccentCyan   = null!;
    private static Brush AccentPurple = null!;
    private static Brush AccentWarm   = null!;
    private static Brush AccentGreen  = null!;
    private static Brush TextPrimary  = null!;
    private static Brush TextSecondary= null!;
    private static Brush TextDim      = null!;

    private static Brush KnobBg       = null!;
    private static Pen   KnobRingPn   = null!;
    private static Pen   ArcBgPn      = null!;
    private static Pen   ArcCyanPn    = null!;
    private static Pen   ArcPurplePn  = null!;
    private static Pen   ArcWarmPn    = null!;
    private static Pen   ArcGreenPn   = null!;
    private static Pen   PtrCyanPn    = null!;
    private static Pen   PtrPurplePn  = null!;
    private static Pen   PtrWarmPn    = null!;
    private static Pen   PtrGreenPn   = null!;
    private static Pen   GlowCyanPn   = null!;
    private static Pen   GlowPurplePn = null!;
    private static Pen   GlowWarmPn   = null!;
    private static Brush MeterBgBrush = null!;
    private static Brush MeterEarly   = null!;
    private static Brush MeterLate    = null!;

    private static readonly Typeface Sans = new("Segoe UI");
    private static readonly Typeface Mono = new("Consolas");

    private static void EnsurePalette()
    {
        var themeId = Services.ThemeService.Instance.CurrentTheme;
        if (_paletteTheme == themeId && BgBrush != null) return;
        _paletteTheme = themeId;

        BgBrush       = Fb(PluginTheme.RvBg);
        PanelBrush    = Fb(PluginTheme.RvPanel);
        SurfaceBrush  = Fb(PluginTheme.RvSurface);
        SectionBrush  = Fb(PluginTheme.RvSection);
        BorderPen     = FP(PluginTheme.RvBorder, 1);
        SectionPen    = FP(PluginTheme.RvSectionBorder, 1);

        // Accent colors stay vibrant in both themes
        AccentCyan    = Fb(Color.FromRgb(0x00, 0xD4, 0xFF));
        AccentPurple  = Fb(Color.FromRgb(0x9C, 0x6A, 0xFF));
        AccentWarm    = Fb(Color.FromRgb(0xFF, 0x8C, 0x42));
        AccentGreen   = Fb(Color.FromRgb(0x42, 0xE6, 0x95));

        TextPrimary   = Fb(PluginTheme.DcTextPrimary);
        TextSecondary = Fb(PluginTheme.DcTextSecondary);
        TextDim       = Fb(PluginTheme.DcTextDim);

        var kb = new RadialGradientBrush(PluginTheme.DcKnobLight, PluginTheme.DcKnobDark)
            { GradientOrigin = new Point(0.35, 0.35) };
        kb.Freeze();
        KnobBg      = kb;
        KnobRingPn  = FP(PluginTheme.DcKnobRing, 1.5);
        ArcBgPn     = FP(PluginTheme.DcKnobArcBg, 2.5);
        ArcCyanPn   = FP(Color.FromRgb(0x00, 0xD4, 0xFF), 2.5);
        ArcPurplePn = FP(Color.FromRgb(0x9C, 0x6A, 0xFF), 2.5);
        ArcWarmPn   = FP(Color.FromRgb(0xFF, 0x8C, 0x42), 2.5);
        ArcGreenPn  = FP(Color.FromRgb(0x42, 0xE6, 0x95), 2.5);
        PtrCyanPn   = FP(Color.FromRgb(0x00, 0xD4, 0xFF), 2);
        PtrPurplePn = FP(Color.FromRgb(0x9C, 0x6A, 0xFF), 2);
        PtrWarmPn   = FP(Color.FromRgb(0xFF, 0x8C, 0x42), 2);
        PtrGreenPn  = FP(Color.FromRgb(0x42, 0xE6, 0x95), 2);
        GlowCyanPn  = FP(Color.FromArgb(30, 0x00, 0xD4, 0xFF), 6);
        GlowPurplePn= FP(Color.FromArgb(30, 0x9C, 0x6A, 0xFF), 6);
        GlowWarmPn  = FP(Color.FromArgb(30, 0xFF, 0x8C, 0x42), 6);
        MeterBgBrush= Fb(PluginTheme.DcMeterBg);
        MeterEarly  = Fb(Color.FromRgb(0x9C, 0x6A, 0xFF));
        MeterLate   = Fb(Color.FromRgb(0x00, 0xD4, 0xFF));
    }

    private static SolidColorBrush Fb(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen FP(Color c, double t) { var p = new Pen(new SolidColorBrush(c), t); p.Freeze(); return p; }

    // ── Knob definitions ──────────────────────────────────────────────────

    private enum KnobSection { Global, Early, Late, Tone }

    private record struct KnobDef(string Label, double Min, double Max,
        Func<ReverbEffect, double> Get, Action<ReverbEffect, double> Set,
        string Unit, KnobSection Section, double DefaultVal,
        bool IsLarge = false, bool IsLog = false);

    // All knobs in layout order per section
    private static readonly KnobDef[] GlobalKnobs =
    [
        new("MIX",       0, 1,    e => e.Mix,      (e,v) => e.Mix = v,      "%",  KnobSection.Global, 0.35),
        new("PRE-DLY",   0, 200,  e => e.PreDelay, (e,v) => e.PreDelay = v, "ms", KnobSection.Global, 20),
        new("DEPTH",     0, 1,    e => e.Depth,    (e,v) => e.Depth = v,    "%",  KnobSection.Global, 0.5),
    ];

    private static readonly KnobDef[] EarlyKnobs =
    [
        new("SIZE",      0, 1,     e => e.EarlySize,      (e,v) => e.EarlySize = v,      "%",  KnobSection.Early, 0.5),
        new("DIFFUSION", 0, 1,     e => e.EarlyDiffusion, (e,v) => e.EarlyDiffusion = v, "%",  KnobSection.Early, 0.7),
        new("CROSS",     0, 1,     e => e.EarlyCross,     (e,v) => e.EarlyCross = v,     "%",  KnobSection.Early, 0.3),
        new("SEND",      0, 1,     e => e.EarlySend,      (e,v) => e.EarlySend = v,      "%",  KnobSection.Early, 0.6),
        new("MOD RATE",  0.1, 5.0, e => e.EarlyModRate,   (e,v) => e.EarlyModRate = v,   "Hz", KnobSection.Early, 0.8),
        new("MOD DPT",   0, 1,     e => e.EarlyModDepth,  (e,v) => e.EarlyModDepth = v,  "%",  KnobSection.Early, 0.3),
    ];

    private static readonly KnobDef[] LateKnobs =
    [
        new("DECAY",     0.1, 30,   e => e.Decay,     (e,v) => e.Decay = v,     "s",  KnobSection.Late, 2.0, IsLarge: true, IsLog: true),
        new("SIZE",      0, 1,      e => e.LateSize,  (e,v) => e.LateSize = v,  "%",  KnobSection.Late, 0.5),
        new("CROSS",     0, 1,      e => e.LateCross, (e,v) => e.LateCross = v, "%",  KnobSection.Late, 0.3),
        new("BASS ×",    0.5, 2.0,  e => e.BassMult,  (e,v) => e.BassMult = v,  "×",  KnobSection.Late, 1.0),
        new("BASS Hz",   50, 500,   e => e.BassXover, (e,v) => e.BassXover = v, "Hz", KnobSection.Late, 200, IsLog: true),
    ];

    private static readonly KnobDef[] ToneKnobs =
    [
        new("HIGH CUT",  1000, 20000, e => e.HighCut,   (e,v) => e.HighCut = v,   "Hz", KnobSection.Tone, 8000, IsLog: true),
        new("HI SHELF",  -12, 0,      e => e.HighShelf, (e,v) => e.HighShelf = v, "dB", KnobSection.Tone, -3),
        new("LO SHELF",  -6, 6,       e => e.LowShelf,  (e,v) => e.LowShelf = v, "dB", KnobSection.Tone, 0),
    ];

    // Flattened array for hit-testing
    private static readonly KnobDef[] AllKnobs = [.. GlobalKnobs, .. EarlyKnobs, .. LateKnobs, .. ToneKnobs];

    // ── State ─────────────────────────────────────────────────────────────

    private int _dragKnobIndex = -1;
    private double _dragStartY, _dragStartVal;
    private bool _shiftHeld;
    private Point[] _knobCenters = [];
    private double[] _knobRadii = [];
    private Rect _modeRect;
    private readonly DispatcherTimer _timer;

    public ReverbControl()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = true;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RENDERING
    // ══════════════════════════════════════════════════════════════════════

    protected override void OnRender(DrawingContext dc)
    {
        EnsurePalette();
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 40) return;

        var rev = Effect;
        if (rev == null) { dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h)); return; }

        // Background
        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));
        var panel = new Rect(4, 4, w - 8, h - 8);
        dc.DrawRoundedRectangle(PanelBrush, BorderPen, panel, 8, 8);

        // Layout geometry
        double pad = 8;
        double topBarH = 70;
        double bottomBarH = 80;
        double midH = panel.Height - topBarH - bottomBarH - pad * 3;
        double midW = panel.Width - pad * 2;
        double halfW = (midW - pad) / 2;

        double topY = panel.Y + pad;
        double midY = topY + topBarH + pad;
        double botY = midY + midH + pad;

        // ── Header ──
        var headerRect = new Rect(panel.X + pad, topY, panel.Width - pad * 2, 22);
        dc.DrawRoundedRectangle(SurfaceBrush, null, headerRect, 4, 4);
        var title = Txt("REVERB", 10, AccentCyan, FontWeights.Bold);
        dc.DrawText(title, new Point(headerRect.X + 10, headerRect.Y + (22 - title.Height) / 2));

        // Mode display
        string modeName = rev.ReverbMode >= 0 && rev.ReverbMode < ReverbEffect.ModeNames.Length
            ? ReverbEffect.ModeNames[rev.ReverbMode] : "?";
        var modeText = Txt(modeName, 9, AccentPurple, FontWeights.SemiBold);
        double modeW = Math.Max(modeText.Width + 20, 100);
        _modeRect = new Rect(headerRect.Right - modeW - 4, headerRect.Y + 2, modeW, 18);
        dc.DrawRoundedRectangle(SectionBrush, SectionPen, _modeRect, 3, 3);
        dc.DrawText(modeText, new Point(_modeRect.X + (_modeRect.Width - modeText.Width) / 2,
            _modeRect.Y + (_modeRect.Height - modeText.Height) / 2));

        // Prepare knob center storage
        _knobCenters = new Point[AllKnobs.Length];
        _knobRadii = new double[AllKnobs.Length];
        int ki = 0; // running index into AllKnobs

        // ── Global Section (top bar, below header) ──
        double globalY = topY + 26;
        double globalH = topBarH - 26;
        double globalKnobR = Math.Min(globalH * 0.32, 16);
        double globalSp = (panel.Width - pad * 2) / (GlobalKnobs.Length + 1); // extra slot for depth meter
        for (int i = 0; i < GlobalKnobs.Length; i++)
        {
            double cx = panel.X + pad + globalSp * (i + 0.5);
            double cy = globalY + globalH / 2;
            _knobCenters[ki] = new Point(cx, cy);
            _knobRadii[ki] = globalKnobR;
            DrawKnob(dc, rev, GlobalKnobs[i], cx, cy, globalKnobR, ArcCyanPn, PtrCyanPn, AccentCyan, null);
            ki++;
        }

        // Depth meter (Early vs Late balance)
        double meterX = panel.X + pad + globalSp * (GlobalKnobs.Length + 0.1);
        double meterW = globalSp * 0.7;
        double meterY = globalY + 10;
        double meterH = globalH - 20;
        DrawDepthMeter(dc, rev, meterX, meterY, meterW, meterH);

        // ── Early Reflections (left) ──
        var earlyRect = new Rect(panel.X + pad, midY, halfW, midH);
        dc.DrawRoundedRectangle(SectionBrush, SectionPen, earlyRect, 6, 6);
        var earlyLabel = Txt("EARLY REFLECTIONS", 8, AccentPurple, FontWeights.SemiBold);
        dc.DrawText(earlyLabel, new Point(earlyRect.X + 10, earlyRect.Y + 6));

        // Layout: 3×2 grid of knobs
        double earlyKnobR = Math.Min((earlyRect.Width - 40) / 8, (earlyRect.Height - 50) / 6);
        earlyKnobR = Math.Clamp(earlyKnobR, 10, 20);
        int cols = 3, rows = 2;
        double eCellW = (earlyRect.Width - 16) / cols;
        double eCellH = (earlyRect.Height - 28) / rows;

        for (int i = 0; i < EarlyKnobs.Length; i++)
        {
            int col = i % cols, row = i / cols;
            double cx = earlyRect.X + 8 + eCellW * col + eCellW / 2;
            double cy = earlyRect.Y + 28 + eCellH * row + eCellH / 2;
            _knobCenters[ki] = new Point(cx, cy);
            _knobRadii[ki] = earlyKnobR;
            DrawKnob(dc, rev, EarlyKnobs[i], cx, cy, earlyKnobR, ArcPurplePn, PtrPurplePn, AccentPurple, GlowPurplePn);
            ki++;
        }

        // ── Late Reverberation (right) ──
        var lateRect = new Rect(panel.X + pad + halfW + pad, midY, halfW, midH);
        dc.DrawRoundedRectangle(SectionBrush, SectionPen, lateRect, 6, 6);
        var lateLabel = Txt("LATE REVERBERATION", 8, AccentCyan, FontWeights.SemiBold);
        dc.DrawText(lateLabel, new Point(lateRect.X + 10, lateRect.Y + 6));

        // Decay gets a large knob centered; others form a row below
        double lateLargeR = Math.Min((lateRect.Height - 60) * 0.33, 30);
        lateLargeR = Math.Clamp(lateLargeR, 16, 36);
        double decayCx = lateRect.X + lateRect.Width / 2;
        double decayCy = lateRect.Y + 28 + lateLargeR + 4;
        _knobCenters[ki] = new Point(decayCx, decayCy);
        _knobRadii[ki] = lateLargeR;
        DrawKnob(dc, rev, LateKnobs[0], decayCx, decayCy, lateLargeR, ArcCyanPn, PtrCyanPn, AccentCyan, GlowCyanPn);
        ki++;

        // Remaining late knobs in a row below
        int lateSmallCount = LateKnobs.Length - 1;
        double lateSmallR = Math.Min((lateRect.Width - 30) / (lateSmallCount * 3), 16);
        lateSmallR = Math.Clamp(lateSmallR, 10, 18);
        double lateRowY = decayCy + lateLargeR + 30 + lateSmallR;
        double lateSp = (lateRect.Width - 16) / lateSmallCount;

        for (int i = 1; i < LateKnobs.Length; i++)
        {
            double cx = lateRect.X + 8 + lateSp * (i - 1) + lateSp / 2;
            _knobCenters[ki] = new Point(cx, lateRowY);
            _knobRadii[ki] = lateSmallR;
            DrawKnob(dc, rev, LateKnobs[i], cx, lateRowY, lateSmallR, ArcCyanPn, PtrCyanPn, AccentCyan, null);
            ki++;
        }

        // ── Tone / EQ (bottom center) ──
        double toneW = panel.Width * 0.6;
        double toneX = panel.X + (panel.Width - toneW) / 2;
        var toneRect = new Rect(toneX, botY, toneW, bottomBarH);
        dc.DrawRoundedRectangle(SectionBrush, SectionPen, toneRect, 6, 6);
        var toneLabel = Txt("TONE", 8, AccentWarm, FontWeights.SemiBold);
        dc.DrawText(toneLabel, new Point(toneRect.X + 10, toneRect.Y + 6));

        double toneKnobR = Math.Min((toneRect.Width - 30) / (ToneKnobs.Length * 3), 16);
        toneKnobR = Math.Clamp(toneKnobR, 10, 18);
        double toneSp = (toneRect.Width - 16) / ToneKnobs.Length;
        double toneCy = toneRect.Y + 24 + toneKnobR + 4;

        for (int i = 0; i < ToneKnobs.Length; i++)
        {
            double cx = toneRect.X + 8 + toneSp * i + toneSp / 2;
            _knobCenters[ki] = new Point(cx, toneCy);
            _knobRadii[ki] = toneKnobR;
            DrawKnob(dc, rev, ToneKnobs[i], cx, toneCy, toneKnobR, ArcWarmPn, PtrWarmPn, AccentWarm, GlowWarmPn);
            ki++;
        }
    }

    // ── Depth meter ───────────────────────────────────────────────────────

    private static void DrawDepthMeter(DrawingContext dc, ReverbEffect rev, double x, double y, double w, double h)
    {
        dc.DrawRoundedRectangle(MeterBgBrush, null, new Rect(x, y, w, h), 3, 3);
        double depth = Math.Clamp(rev.Depth, 0, 1);
        double earlyH = h * (1 - depth);
        double lateH = h * depth;
        if (earlyH > 1)
            dc.DrawRoundedRectangle(MeterEarly, null, new Rect(x + 1, y + 1, w - 2, earlyH - 1), 2, 2);
        if (lateH > 1)
            dc.DrawRoundedRectangle(MeterLate, null, new Rect(x + 1, y + earlyH, w - 2, lateH - 1), 2, 2);

        var elbl = Txt("E", 7, TextDim);
        dc.DrawText(elbl, new Point(x + w / 2 - elbl.Width / 2, y + 2));
        var llbl = Txt("L", 7, TextDim);
        dc.DrawText(llbl, new Point(x + w / 2 - llbl.Width / 2, y + h - llbl.Height - 2));
    }

    // ── Knob rendering ────────────────────────────────────────────────────

    private static void DrawKnob(DrawingContext dc, ReverbEffect rev, KnobDef knob,
        double cx, double cy, double r, Pen arcPen, Pen ptrPen, Brush valBrush, Pen? glowPen)
    {
        double value = knob.Get(rev);
        double norm = knob.IsLog
            ? (Math.Log10(Math.Max(value, 0.001)) - Math.Log10(Math.Max(knob.Min, 0.001)))
              / (Math.Log10(knob.Max) - Math.Log10(Math.Max(knob.Min, 0.001)))
            : (value - knob.Min) / (knob.Max - knob.Min);
        norm = Math.Clamp(norm, 0, 1);

        double aMin = 5.0 * Math.PI / 4.0, aMax = -Math.PI / 4.0;
        double angle = aMin + norm * (aMax - aMin);

        // Glow (large knobs only)
        if (glowPen != null && knob.IsLarge)
            dc.DrawEllipse(null, glowPen, new Point(cx, cy), r + 6, r + 6);

        // Arc background + active arc
        double arcR = r + 4;
        DrawArc(dc, cx, cy, arcR, aMin, aMax, ArcBgPn);
        DrawArc(dc, cx, cy, arcR, aMin, angle, arcPen);

        // Scale ticks
        for (int t = 0; t <= 10; t++)
        {
            double ta = aMin + (double)t / 10 * (aMax - aMin);
            dc.DrawLine(t % 5 == 0 ? SectionPen : new Pen(TextDim, 0.5),
                new Point(cx + (r + 7) * Math.Cos(ta), cy - (r + 7) * Math.Sin(ta)),
                new Point(cx + (r + (t % 5 == 0 ? 11 : 9)) * Math.Cos(ta), cy - (r + (t % 5 == 0 ? 11 : 9)) * Math.Sin(ta)));
        }

        // Knob body
        dc.DrawEllipse(KnobBg, KnobRingPn, new Point(cx, cy), r, r);

        // Pointer line
        double pI = r * 0.25, pO = r * 0.85;
        dc.DrawLine(ptrPen,
            new Point(cx + pI * Math.Cos(angle), cy - pI * Math.Sin(angle)),
            new Point(cx + pO * Math.Cos(angle), cy - pO * Math.Sin(angle)));

        // Label above
        var lbl = Txt(knob.Label, knob.IsLarge ? 9 : 7, TextSecondary, FontWeights.SemiBold);
        dc.DrawText(lbl, new Point(cx - lbl.Width / 2, cy - r - (knob.IsLarge ? 20 : 16)));

        // Value below
        string valStr = FormatKnobValue(knob, value);
        var val = Txt(valStr, knob.IsLarge ? 9 : 7, valBrush, FontWeights.SemiBold, Mono);
        dc.DrawText(val, new Point(cx - val.Width / 2, cy + r + (knob.IsLarge ? 8 : 6)));
    }

    private static string FormatKnobValue(KnobDef knob, double value) => knob.Unit switch
    {
        "%" => $"{value * 100:F0}%",
        "ms" => $"{value:F0}ms",
        "Hz" => value >= 1000 ? $"{value / 1000:F1}k" : $"{value:F0}Hz",
        "s" => value >= 10 ? $"{value:F0}s" : $"{value:F1}s",
        "dB" => $"{value:F1}dB",
        "×" => $"{value:F2}×",
        _ => $"{value:F1}"
    };

    private static void DrawArc(DrawingContext dc, double cx, double cy, double r, double from, double to, Pen pen)
    {
        const int steps = 24;
        for (int s = 0; s < steps; s++)
        {
            double a1 = from + (to - from) * s / steps;
            double a2 = from + (to - from) * (s + 1) / steps;
            dc.DrawLine(pen,
                new Point(cx + r * Math.Cos(a1), cy - r * Math.Sin(a1)),
                new Point(cx + r * Math.Cos(a2), cy - r * Math.Sin(a2)));
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  INTERACTION
    // ══════════════════════════════════════════════════════════════════════

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var rev = Effect;
        if (rev == null) return;
        var pos = e.GetPosition(this);

        // Double-click: reset knob to default
        if (e.ClickCount == 2)
        {
            for (int i = 0; i < _knobCenters.Length && i < AllKnobs.Length; i++)
            {
                double dx = pos.X - _knobCenters[i].X, dy = pos.Y - _knobCenters[i].Y;
                double hitR = _knobRadii.Length > i ? _knobRadii[i] + 12 : 26;
                if (dx * dx + dy * dy <= hitR * hitR)
                {
                    AllKnobs[i].Set(rev, AllKnobs[i].DefaultVal);
                    e.Handled = true;
                    return;
                }
            }
        }

        // Mode button (cycle through modes)
        if (_modeRect.Contains(pos))
        {
            rev.ReverbMode = (rev.ReverbMode + 1) % ReverbEffect.ModeNames.Length;
            e.Handled = true;
            return;
        }

        // Knob drag
        for (int i = 0; i < _knobCenters.Length && i < AllKnobs.Length; i++)
        {
            double dx = pos.X - _knobCenters[i].X, dy = pos.Y - _knobCenters[i].Y;
            double hitR = _knobRadii.Length > i ? _knobRadii[i] + 12 : 26;
            if (dx * dx + dy * dy <= hitR * hitR)
            {
                _dragKnobIndex = i;
                _dragStartY = pos.Y;
                _dragStartVal = AllKnobs[i].Get(rev);
                _shiftHeld = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragKnobIndex < 0 || Effect == null) return;
        var knob = AllKnobs[_dragKnobIndex];
        double dy = _dragStartY - e.GetPosition(this).Y;

        // Fine-tuning: Shift reduces sensitivity by 5×
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        double sensitivity = shift ? 500.0 : 200.0;

        if (knob.IsLog)
        {
            double lMin = Math.Log10(Math.Max(knob.Min, 0.001)), lMax = Math.Log10(knob.Max);
            double lStart = Math.Log10(Math.Max(_dragStartVal, 0.001));
            double newVal = Math.Pow(10, Math.Clamp(lStart + dy * (lMax - lMin) / sensitivity, lMin, lMax));
            knob.Set(Effect, newVal);
        }
        else
        {
            double newVal = Math.Clamp(_dragStartVal + dy * (knob.Max - knob.Min) / sensitivity, knob.Min, knob.Max);
            knob.Set(Effect, newVal);
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragKnobIndex >= 0) { _dragKnobIndex = -1; ReleaseMouseCapture(); e.Handled = true; }
    }

    /// <summary>Right-click resets knob to default.</summary>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (Effect == null) return;
        var pos = e.GetPosition(this);

        for (int i = 0; i < _knobCenters.Length && i < AllKnobs.Length; i++)
        {
            double dx = pos.X - _knobCenters[i].X, dy = pos.Y - _knobCenters[i].Y;
            double hitR = _knobRadii.Length > i ? _knobRadii[i] + 12 : 26;
            if (dx * dx + dy * dy <= hitR * hitR)
            {
                AllKnobs[i].Set(Effect, AllKnobs[i].DefaultVal);
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
