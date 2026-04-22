using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using DAW.Plugins;
using DAW.Services;

namespace DAW.Views.Controls;

/// <summary>
/// Custom-drawn UI for SaturationEffect, inspired by the Black Box HG-2 hardware.
///
/// Layout (top → bottom):
///   Row 1:  PENTODE knob | TRIODE knob | DENSITY knob
///   Row 2:  PARALLEL knob | SAT FREQ buttons | AIR knob   + [ON] toggle
///   Row 3:  OUTPUT slider | MIX slider
///
/// Interaction:
///   Drag knob up/down = adjust · Shift+Drag = fine · Right-click = reset
///   Click SAT FREQ button = select band · Click ON = toggle parallel circuit
///   Click/drag slider = set value directly
/// </summary>
public sealed class SaturationControl : FrameworkElement
{
    // ── Dependency property ───────────────────────────────────────────────────
    // Renamed to SaturationEffectProperty to avoid hiding UIElement.EffectProperty
    public static readonly DependencyProperty SaturationEffectProperty =
        DependencyProperty.Register(
            nameof(Effect),
            typeof(SaturationEffect),
            typeof(SaturationControl),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnEffectChanged));

    public SaturationEffect? Effect
    {
        get => (SaturationEffect?)GetValue(SaturationEffectProperty);
        set => SetValue(SaturationEffectProperty, value);
    }

    private static void OnEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (SaturationControl)d;
        if (e.OldValue is SaturationEffect old) old.PropertyChanged -= ctrl.OnEffectPropChanged;
        if (e.NewValue is SaturationEffect nw)  nw.PropertyChanged  += ctrl.OnEffectPropChanged;
        ctrl.InvalidateVisual();
    }

    private void OnEffectPropChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);

    // ── Theme palette ─────────────────────────────────────────────────────────
    private static string? _themeId;
    private static Brush   _bg          = null!;
    private static Brush   _panel       = null!;
    private static Brush   _accent      = null!;
    private static Brush   _warm        = null!;   // amber  — pentode
    private static Brush   _cool        = null!;   // blue   — triode / air
    private static Brush   _textPri     = null!;
    private static Brush   _textDim     = null!;
    private static Pen     _borderPen   = null!;
    private static Pen     _dividerPen  = null!;
    private static Brush   _knobBg      = null!;
    private static Brush   _activeBtn   = null!;
    private static Brush   _inactiveBtn = null!;

    private static void EnsurePalette()
    {
        var theme = ThemeService.Instance.CurrentTheme;
        if (_themeId == theme && _bg != null) return;
        _themeId     = theme;
        _bg          = FB(PluginTheme.WindowBg);
        _panel       = FB(PluginTheme.ControlBg);
        _accent      = FB(PluginTheme.TextAccent);         // was wrongly "Accent"
        _warm        = FB(Color.FromRgb(0xFF, 0x8C, 0x00)); // amber — pentode
        _cool        = FB(Color.FromRgb(0x5B, 0xA4, 0xE6)); // blue  — triode/air
        _textPri     = FB(PluginTheme.TextPrimary);
        _textDim     = FB(PluginTheme.TextHint);
        _borderPen   = FP(new Pen(FB(PluginTheme.Border), 1));
        _dividerPen  = FP(new Pen(FB(Color.FromArgb(50, 200, 200, 200)), 1)
                           { DashStyle = DashStyles.Dash });
        _knobBg      = FB(PluginTheme.SurfaceBg);
        _activeBtn   = FB(Color.FromRgb(0xFF, 0x6B, 0x35));
        _inactiveBtn = FB(PluginTheme.ControlBg);
    }

    private static SolidColorBrush FB(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen             FP(Pen p)   { p.Freeze(); return p; }

    // ── Drag state ────────────────────────────────────────────────────────────
    private enum DragTarget { None, Pentode, Triode, Density, Parallel, Air, Output, Mix }

    private DragTarget _drag        = DragTarget.None;
    private double     _dragStartY  = 0;
    private double     _dragBaseVal = 0;

    // ── Cached layout rects ───────────────────────────────────────────────────
    private Rect   _pentodeKnob, _triodeKnob, _densityKnob;
    private Rect   _parallelKnob, _airKnob;
    private Rect   _outputSlider, _mixSlider;
    private Rect   _parallelBtn;
    private Rect[] _freqBtns = new Rect[3];

    // ── Rendering ─────────────────────────────────────────────────────────────
    protected override void OnRender(DrawingContext dc)
    {
        EnsurePalette();
        var fx = Effect;
        double W = ActualWidth, H = ActualHeight;
        if (W < 20 || H < 20) return;

        dc.DrawRectangle(_bg, null, new Rect(0, 0, W, H));

        double pad  = 16;
        double colW = (W - pad * 2) / 3;
        double kR   = Math.Min(colW * 0.28, 34.0);

        // ── Row 1: Pentode | Triode | Density ────────────────────────────────
        double r1LabelY = 12;
        double r1CY     = r1LabelY + 14 + kR;

        _pentodeKnob = KR(pad + colW * 0.5, r1CY, kR);
        _triodeKnob  = KR(pad + colW * 1.5, r1CY, kR);
        _densityKnob = KR(pad + colW * 2.5, r1CY, kR);

        DrawKnob(dc, _pentodeKnob, fx?.PentodeDrive ?? 0.3f, "PENTODE", _warm,   r1LabelY);
        DrawKnob(dc, _triodeKnob,  fx?.TriodeDrive  ?? 0.2f, "TRIODE",  _cool,   r1LabelY);
        DrawKnob(dc, _densityKnob, fx?.Density       ?? 0.5f, "DENSITY", _accent, r1LabelY);

        // ── Divider + section label ───────────────────────────────────────────
        double divY = r1CY + kR + 26;
        dc.DrawLine(_dividerPen, new Point(pad, divY), new Point(W - pad, divY));
        Txt(dc, W / 2, divY - 11, "─── PARALLEL SATURATION ───", _textDim, 8.5, true);

        // Parallel ON/OFF button
        _parallelBtn = new Rect(W - pad - 46, divY + 10, 46, 20);
        bool parOn = fx?.ParallelOn ?? false;
        dc.DrawRoundedRectangle(parOn ? _activeBtn : _inactiveBtn, _borderPen, _parallelBtn, 4, 4);
        Txt(dc, _parallelBtn.Left + _parallelBtn.Width / 2, _parallelBtn.Top + 3,
            parOn ? "ON" : "OFF", _textPri, 10, true);

        // ── Row 2: Parallel knob | SAT FREQ buttons | Air knob ───────────────
        double r2LabelY = divY + 10;
        double r2CY     = r2LabelY + 14 + kR;
        float  dimAlpha = parOn ? 1f : 0.3f;

        _parallelKnob = KR(pad + colW * 0.5, r2CY, kR);
        _airKnob      = KR(pad + colW * 2.5, r2CY, kR);

        DrawKnob(dc, _parallelKnob, fx?.ParallelSat ?? 0f, "PARALLEL", _warm, r2LabelY, dimAlpha);
        DrawKnob(dc, _airKnob,      fx?.Air         ?? 0f, "AIR",      _cool, r2LabelY, dimAlpha);

        // Freq buttons: LOW | FLAT | HIGH
        double fbX = pad + colW + 4;
        double fbW = (colW - 8) / 3;
        double fbH = 20;
        double fbY = r2CY - fbH / 2;
        int    sel = fx?.SatFreq ?? 1;
        Txt(dc, fbX + (colW - 8) / 2, fbY - 14, "SAT FREQ", _textDim, 9, true);

        string[] fl = ["LOW", "FLAT", "HIGH"];
        for (int fi = 0; fi < 3; fi++)
        {
            _freqBtns[fi] = new Rect(fbX + fi * fbW, fbY, fbW - 2, fbH);
            bool s2 = fi == sel;
            dc.DrawRoundedRectangle(s2 ? _activeBtn : Dim(_inactiveBtn, dimAlpha),
                                    _borderPen, _freqBtns[fi], 3, 3);
            Txt(dc, _freqBtns[fi].Left + _freqBtns[fi].Width / 2, _freqBtns[fi].Top + 3,
                fl[fi], s2 ? FB(Colors.White) : _textDim, 9, true);
        }

        // ── Row 3: OUTPUT slider | MIX slider ────────────────────────────────
        double r3LabelY = r2CY + kR + 20;
        double slH = 8, slW = colW - 20;

        _outputSlider = new Rect(pad + 10,        r3LabelY + 18, slW, slH);
        _mixSlider    = new Rect(pad + colW + 10, r3LabelY + 18, slW, slH);

        DrawSlider(dc, _outputSlider,
            NormOut(fx?.OutputGain ?? 0f), "OUTPUT",
            $"{(fx?.OutputGain ?? 0f):+0.0;-0.0} dB", r3LabelY, _accent);
        DrawSlider(dc, _mixSlider,
            fx?.Mix ?? 1f, "MIX",
            $"{(int)((fx?.Mix ?? 1f) * 100)}%", r3LabelY, _cool);
    }

    // ── Draw helpers ──────────────────────────────────────────────────────────

    private static Rect KR(double cx, double cy, double r)
        => new(cx - r, cy - r, r * 2, r * 2);

    private void DrawKnob(DrawingContext dc, Rect rect, float value,
                          string label, Brush accent, double labelY, float alpha = 1f)
    {
        double cx = rect.Left + rect.Width  / 2;
        double cy = rect.Top  + rect.Height / 2;
        double r  = rect.Width / 2;

        var bg  = alpha < 1f ? Dim(_knobBg, alpha) : _knobBg;
        var ac  = alpha < 1f ? Dim(accent,  alpha) : accent;

        dc.DrawEllipse(bg, new Pen(alpha < 1f ? Dim(_textDim, alpha * 0.4f) : _textDim, 1.5),
                       new Point(cx, cy), r, r);

        // Arc background (track)
        DrawArc(dc, cx, cy, r - 5, -220, 40, 2.5, Dim(_panel, alpha < 1f ? alpha : 0.6f));

        // Arc value fill
        double endDeg = -220 + value * 260;
        if (endDeg > -219) DrawArc(dc, cx, cy, r - 5, -220, endDeg, 2.5, ac);

        // Pointer
        double ang = endDeg * Math.PI / 180;
        var pw = new Point(cx + Math.Cos(ang) * (r - 9), cy + Math.Sin(ang) * (r - 9));
        dc.DrawLine(new Pen(ac, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                    new Point(cx, cy), pw);
        dc.DrawEllipse(ac, null, new Point(cx, cy), 2.5, 2.5);

        // Labels
        Txt(dc, cx, labelY + 2,       label,              alpha < 1f ? Dim(_textPri, alpha) : _textPri, 9,   true);
        Txt(dc, cx, rect.Bottom + 3, $"{value * 100:F0}", alpha < 1f ? Dim(_textDim, alpha) : _textDim, 8.5, true);
    }

    private void DrawSlider(DrawingContext dc, Rect r, float value,
                            string label, string valStr, double labelY, Brush accent)
    {
        Txt(dc, r.Left + r.Width / 2, labelY + 2, label, _textPri, 9, true);
        dc.DrawRoundedRectangle(_panel, _borderPen, r, 4, 4);
        if (value > 0.002f)
            dc.DrawRoundedRectangle(accent, null, new Rect(r.Left, r.Top, value * r.Width, r.Height), 4, 4);
        dc.DrawEllipse(_textPri, null, new Point(r.Left + value * r.Width, r.Top + r.Height / 2), 5, 5);
        Txt(dc, r.Left + r.Width / 2, r.Bottom + 5, valStr, _textDim, 8.5, true);
    }

    private void Txt(DrawingContext dc, double cx, double y, string text,
                     Brush fg, double size, bool centered)
    {
        var ft = new FormattedText(text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, fg,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(centered ? cx - ft.Width / 2 : cx, y));
    }

    private static void DrawArc(DrawingContext dc, double cx, double cy,
                                 double r, double startDeg, double endDeg,
                                 double thickness, Brush brush)
    {
        if (Math.Abs(endDeg - startDeg) < 0.5) return;
        double s = startDeg * Math.PI / 180, e = endDeg * Math.PI / 180;
        var p1  = new Point(cx + r * Math.Cos(s), cy + r * Math.Sin(s));
        var p2  = new Point(cx + r * Math.Cos(e), cy + r * Math.Sin(e));
        var seg = new ArcSegment(p2, new Size(r, r), 0, endDeg - startDeg > 180,
                                 SweepDirection.Clockwise, true);
        var geo = new PathGeometry(new[] { new PathFigure(p1, new[] { seg }, false) });
        dc.DrawGeometry(null,
            new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            geo);
    }

    private static SolidColorBrush Dim(Brush b, float alpha)
    {
        var c = b is SolidColorBrush scb ? scb.Color : Colors.Gray;
        return new SolidColorBrush(Color.FromArgb((byte)(c.A * alpha), c.R, c.G, c.B));
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);

        // Freq buttons
        for (int fi = 0; fi < 3; fi++)
        {
            if (_freqBtns[fi].Contains(p) && Effect != null)
            { Effect.SatFreq = fi; e.Handled = true; return; }
        }

        // Parallel ON toggle
        if (_parallelBtn.Contains(p) && Effect != null)
        { Effect.ParallelOn = !Effect.ParallelOn; e.Handled = true; return; }

        // Sliders — direct click sets value
        if (_outputSlider.Contains(p))
        { BeginDrag(DragTarget.Output, p); ApplySlider(DragTarget.Output, p); e.Handled = true; return; }
        if (_mixSlider.Contains(p))
        { BeginDrag(DragTarget.Mix,    p); ApplySlider(DragTarget.Mix,    p); e.Handled = true; return; }

        // Knob drag
        var t = HitKnob(p);
        if (t != DragTarget.None) { BeginDrag(t, p); e.Handled = true; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == DragTarget.None || Effect == null) return;
        var p = e.GetPosition(this);
        if (_drag is DragTarget.Output or DragTarget.Mix)
        {
            ApplySlider(_drag, p);
        }
        else
        {
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            double newV = Math.Clamp(_dragBaseVal + (_dragStartY - p.Y) * (shift ? 0.002 : 0.008), 0, 1);
            SetVal(_drag, newV);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag == DragTarget.None) return;
        ReleaseMouseCapture();
        _drag = DragTarget.None;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        var t = HitKnob(e.GetPosition(this));
        if (t != DragTarget.None && Effect != null) { ResetVal(t); e.Handled = true; }
    }

    private void BeginDrag(DragTarget t, Point p)
    {
        _drag        = t;
        _dragStartY  = p.Y;
        _dragBaseVal = GetVal(t);
        CaptureMouse();
    }

    private void ApplySlider(DragTarget t, Point p)
    {
        var r = t == DragTarget.Output ? _outputSlider : _mixSlider;
        SetVal(t, Math.Clamp((p.X - r.Left) / r.Width, 0, 1));
    }

    private DragTarget HitKnob(Point p)
    {
        if (_pentodeKnob.Contains(p))  return DragTarget.Pentode;
        if (_triodeKnob.Contains(p))   return DragTarget.Triode;
        if (_densityKnob.Contains(p))  return DragTarget.Density;
        if (_parallelKnob.Contains(p)) return DragTarget.Parallel;
        if (_airKnob.Contains(p))      return DragTarget.Air;
        return DragTarget.None;
    }

    private double GetVal(DragTarget t) => t switch
    {
        DragTarget.Pentode  => Effect?.PentodeDrive ?? 0.3,
        DragTarget.Triode   => Effect?.TriodeDrive  ?? 0.2,
        DragTarget.Density  => Effect?.Density      ?? 0.5,
        DragTarget.Parallel => Effect?.ParallelSat  ?? 0,
        DragTarget.Air      => Effect?.Air          ?? 0,
        DragTarget.Output   => NormOut(Effect?.OutputGain ?? 0),
        DragTarget.Mix      => Effect?.Mix          ?? 1,
        _                   => 0
    };

    private void SetVal(DragTarget t, double v)
    {
        if (Effect == null) return;
        switch (t)
        {
            case DragTarget.Pentode:  Effect.PentodeDrive = (float)v;              break;
            case DragTarget.Triode:   Effect.TriodeDrive  = (float)v;              break;
            case DragTarget.Density:  Effect.Density      = (float)v;              break;
            case DragTarget.Parallel: Effect.ParallelSat  = (float)v;              break;
            case DragTarget.Air:      Effect.Air          = (float)v;              break;
            case DragTarget.Output:   Effect.OutputGain   = DenormOut((float)v);   break;
            case DragTarget.Mix:      Effect.Mix          = (float)v;              break;
        }
    }

    private void ResetVal(DragTarget t)
    {
        if (Effect == null) return;
        switch (t)
        {
            case DragTarget.Pentode:  Effect.PentodeDrive = 0.3f; break;
            case DragTarget.Triode:   Effect.TriodeDrive  = 0.2f; break;
            case DragTarget.Density:  Effect.Density      = 0.5f; break;
            case DragTarget.Parallel: Effect.ParallelSat  = 0f;   break;
            case DragTarget.Air:      Effect.Air          = 0f;   break;
            case DragTarget.Output:   Effect.OutputGain   = 0f;   break;
            case DragTarget.Mix:      Effect.Mix          = 1f;   break;
        }
    }

    // ── Output gain: -12..+12 dB ↔ 0..1 ─────────────────────────────────────
    private static float NormOut(float db)  => (db + 12f) / 24f;
    private static float DenormOut(float n) => n * 24f - 12f;

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width)  || available.Width  <= 0 ? 380 : available.Width;
        double h = double.IsInfinity(available.Height) || available.Height <= 0 ? 380 : available.Height;
        return new Size(w, h);
    }
}
