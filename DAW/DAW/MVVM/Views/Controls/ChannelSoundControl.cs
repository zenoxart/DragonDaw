using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DAW.MVVM.Models.Sequencer;
using static DAW.MVVM.Views.Controls.DragonUI;

namespace DAW.MVVM.Views.Controls;

/// <summary>
/// "Sound Settings" editor for a single Channel-Rack channel: the FL-Studio
/// style voice-cutting Group (Cut / By / Cut self) and the amplitude envelope
/// (Delay → Attack → Hold → Decay → Sustain → Release) with a live graph.
///
/// Layout:
///   Row 0  Header — channel name
///   Row 1  GROUP panel: CUT field, BY field, CUT SELF toggle
///   Row 2  ENVELOPE panel: enable dot, graph, 6 knobs, 2 tension knobs
///
/// Bound directly to a <see cref="ChannelModel"/> (not an AudioEffect, unlike
/// the other Dragon-Particle plugin controls) via the <see cref="Channel"/>
/// dependency property. Edits write straight back to the model; MainViewModel
/// already listens for these property changes and applies them live to the
/// channel's <c>ChannelRackBusProvider</c>.
/// </summary>
public sealed class ChannelSoundControl : FrameworkElement
{
    public static readonly DependencyProperty ChannelProperty =
        DependencyProperty.Register(nameof(Channel), typeof(ChannelModel), typeof(ChannelSoundControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnChannelChanged));

    public ChannelModel? Channel
    {
        get => (ChannelModel?)GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    private static void OnChannelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ChannelSoundControl)d;
        if (e.OldValue is ChannelModel old) old.PropertyChanged -= c.OnModelPropertyChanged;
        if (e.NewValue is ChannelModel nw)  nw.PropertyChanged  += c.OnModelPropertyChanged;
    }

    private void OnModelPropertyChanged(object? s, PropertyChangedEventArgs e) => InvalidateVisual();

    // Mint-green accent — matches this app's Sampler-family styling, distinct
    // from the red/gold Dragon Particle palette used by the effect plugins.
    private static readonly Color Accent = Color.FromRgb(0x4A, 0xDE, 0x80);

    private record struct EK(string L, double Min, double Max,
        Func<ChannelModel, double> Get, Action<ChannelModel, double> Set, string Unit, double Def);

    private static readonly EK[] EnvKnobs =
    [
        new("DELAY", 0, 2, m => m.EnvDelay,   (m, v) => m.EnvDelay   = (float)v, "s", 0.0),
        new("ATT",   0, 2, m => m.EnvAttack,  (m, v) => m.EnvAttack  = (float)v, "s", 0.001),
        new("HOLD",  0, 2, m => m.EnvHold,    (m, v) => m.EnvHold    = (float)v, "s", 0.0),
        new("DEC",   0, 4, m => m.EnvDecay,   (m, v) => m.EnvDecay   = (float)v, "s", 0.30),
        new("SUS",   0, 1, m => m.EnvSustain, (m, v) => m.EnvSustain = (float)v, "%", 1.0),
        new("REL",   0, 4, m => m.EnvRelease, (m, v) => m.EnvRelease = (float)v, "s", 0.05),
    ];
    private static readonly EK[] TensionKnobs =
    [
        new("TENSION", -1, 1, m => m.EnvAttackTension,  (m, v) => m.EnvAttackTension  = (float)v, "t", 0.0),
        new("TENSION", -1, 1, m => m.EnvReleaseTension, (m, v) => m.EnvReleaseTension = (float)v, "t", 0.0),
    ];
    private static readonly EK[] AllKnobs = [.. EnvKnobs, .. TensionKnobs];

    private int      _dk = -1;
    private double   _dy, _db;
    private Point[]  _kc = [];
    private double[] _kr = [];
    private Rect _cutFieldR, _byFieldR, _cutSelfR, _envEnableR;

    public ChannelSoundControl()
    {
        ClipToBounds = SnapsToDevicePixels = Focusable = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W < 60 || H < 60) return;

        dc.DrawRoundedRectangle(BBg, PBorder, new Rect(0, 0, W, H), 6, 6);
        DrawScrew(dc, 8, 8); DrawScrew(dc, W - 8, 8);
        DrawScrew(dc, 8, H - 8); DrawScrew(dc, W - 8, H - 8);

        var ch = Channel;
        DrawHeader(dc, new Rect(0, 0, W, HDR_H), "SOUND", ch?.Name ?? "Channel");
        if (ch == null) return;

        _kc = new Point[AllKnobs.Length]; _kr = new double[AllKnobs.Length];
        Array.Fill(_kc, new Point(-1000, -1000));

        double top     = HDR_H + PAD;
        double groupH  = 128;
        var    groupR  = new Rect(PAD, top, W - PAD * 2, groupH);
        DrawGroupPanel(dc, groupR, ch);

        double envTop = top + groupH + GAP;
        var    envR   = new Rect(PAD, envTop, W - PAD * 2, Math.Max(280, H - envTop - PAD));
        DrawEnvelopePanel(dc, envR, ch);
    }

    // ── Group panel ──────────────────────────────────────────────────────────

    private void DrawGroupPanel(DrawingContext dc, Rect r, ChannelModel ch)
    {
        DrawSection(dc, r, "Group", Accent);

        double fieldTop = r.Y + 26;
        double fieldH   = 28;

        _cutFieldR = new Rect(r.X + 10, fieldTop, r.Width - 20, fieldH);
        DrawGroupField(dc, _cutFieldR, "CUT", CutLabel(ch.CutGroup));

        _byFieldR = new Rect(r.X + 10, _cutFieldR.Bottom + 20, r.Width - 20, fieldH);
        DrawGroupField(dc, _byFieldR, "BY", CutLabel(ch.CutByGroup));

        _cutSelfR = new Rect(r.X + 10, _byFieldR.Bottom + 14, r.Width - 20, 26);
        DrawToggle(dc, _cutSelfR, "CUT SELF", ch.CutSelf);
    }

    private static string CutLabel(int g) => g <= 0 ? "None" : $"Group {g}";

    private static void DrawGroupField(DrawingContext dc, Rect r, string label, string value)
    {
        var lbl = Txt(label, 8, BTextSec, FontWeights.SemiBold, TFCond);
        dc.DrawText(lbl, new Point(r.X, r.Y - lbl.Height - 3));

        dc.DrawRoundedRectangle(BSurface, PBorder, r, 4, 4);
        var val = Txt(value, 10.5, BTextPri, FontWeights.SemiBold, TFCond);
        dc.DrawText(val, new Point(r.X + 10, r.Y + (r.Height - val.Height) / 2));

        // Chevron affordance (click cycles the value; right-click cycles back)
        double cx = r.Right - 16, cy = r.Y + r.Height / 2;
        var geo = new StreamGeometry();
        using (var gctx = geo.Open())
        {
            gctx.BeginFigure(new Point(cx - 4, cy - 3), false, false);
            gctx.LineTo(new Point(cx, cy + 3), true, false);
            gctx.LineTo(new Point(cx + 4, cy - 3), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, P(CTextSec, 1.4), geo);
    }

    // ── Envelope panel ───────────────────────────────────────────────────────

    private void DrawEnvelopePanel(DrawingContext dc, Rect r, ChannelModel ch)
    {
        var bg = new LinearGradientBrush(Color.FromRgb(0x16, 0x1A, 0x22),
                                          Color.FromRgb(0x11, 0x14, 0x1A), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 0.8), r, 4, 4);

        // Enable dot + label
        double ecx = r.X + 16, ecy = r.Y + 15;
        _envEnableR = new Rect(ecx - 8, ecy - 8, 16, 16);
        dc.DrawEllipse(ch.EnvelopeEnabled ? B(Accent) : BSurface, P(Accent, 1.2), new Point(ecx, ecy), 6, 6);
        var lbl = Txt("ENVELOPE", 8.5, B(Color.FromArgb(190, Accent.R, Accent.G, Accent.B)), FontWeights.Bold, TFCond);
        dc.DrawText(lbl, new Point(ecx + 13, ecy - lbl.Height / 2));

        // Graph
        double graphTop = r.Y + 32;
        var graphR = new Rect(r.X + 10, graphTop, r.Width - 20, 110);
        dc.DrawRoundedRectangle(B(Color.FromRgb(0x0A, 0x0D, 0x12)), P(CBorder, 0.8), graphR, 3, 3);
        DrawEnvelopeGraph(dc, graphR, ch);

        // 6 main knobs
        double knobTop = graphR.Bottom + 14;
        var knobRow = new Rect(r.X + 10, knobTop, r.Width - 20, ROW_H);
        var cxs = Columns(knobRow.X, knobRow.Width, EnvKnobs.Length);
        double kr  = Math.Clamp(knobRow.Width / EnvKnobs.Length * 0.28, 12, KR);
        double kcy = KnobCY(knobTop, kr);
        for (int i = 0; i < EnvKnobs.Length; i++)
        {
            var k = EnvKnobs[i];
            double v = k.Get(ch);
            _kc[i] = new Point(cxs[i], kcy); _kr[i] = kr;
            DrawKnob(dc, cxs[i], kcy, kr, NormEK(k, v), Accent, k.L, FmtEK(k, v));
        }

        // 2 tension knobs, positioned under ATT and REL
        double tR   = 11;
        double tCy  = kcy + kr + 51;
        double[] tCenters = [cxs[1], cxs[5]];
        for (int i = 0; i < TensionKnobs.Length; i++)
        {
            var k = TensionKnobs[i];
            double v = k.Get(ch);
            int idx = EnvKnobs.Length + i;
            _kc[idx] = new Point(tCenters[i], tCy); _kr[idx] = tR;
            DrawKnob(dc, tCenters[i], tCy, tR, NormEK(k, v), Accent, k.L, FmtEK(k, v));
        }
    }

    /// <summary>
    /// Draws the ADSR-style curve. Segment widths are visually weighted by each
    /// stage's duration (compressed/floored so no stage ever fully vanishes or
    /// dominates) rather than drawn exactly to time-scale — Sustain has no real
    /// duration of its own (it holds until the voice ends or is choked), so it
    /// gets a fixed illustrative width.
    /// </summary>
    private static void DrawEnvelopeGraph(DrawingContext dc, Rect r, ChannelModel ch)
    {
        const double pad = 8;
        var inner  = new Rect(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2);
        double bottom = inner.Bottom, top = inner.Y;

        double wDelay   = VisW(ch.EnvDelay, 2);
        double wAttack  = VisW(ch.EnvAttack, 2);
        double wHold    = VisW(ch.EnvHold, 2);
        double wDecay   = VisW(ch.EnvDecay, 4);
        const double wSustain = 0.6;
        double wRelease = VisW(ch.EnvRelease, 4);
        double total = wDelay + wAttack + wHold + wDecay + wSustain + wRelease;

        double x = inner.X;
        double xDelayEnd   = x += inner.Width * wDelay   / total;
        double xAttackEnd  = x += inner.Width * wAttack  / total;
        double xHoldEnd    = x += inner.Width * wHold    / total;
        double xDecayEnd   = x += inner.Width * wDecay   / total;
        double xSustainEnd = x += inner.Width * wSustain / total;
        double xReleaseEnd = inner.Right;

        double sustainY = bottom - (bottom - top) * Math.Clamp(ch.EnvSustain, 0, 1);

        var pts = new List<Point> { new(inner.X, bottom), new(xDelayEnd, bottom) };

        const int steps = 16;
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            double px = xDelayEnd + (xAttackEnd - xDelayEnd) * t;
            double py = bottom - (bottom - top) * ShapeT(t, ch.EnvAttackTension);
            pts.Add(new Point(px, py));
        }
        pts.Add(new Point(xHoldEnd, top));
        pts.Add(new Point(xDecayEnd, sustainY));
        pts.Add(new Point(xSustainEnd, sustainY));
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            double px = xSustainEnd + (xReleaseEnd - xSustainEnd) * t;
            double py = sustainY + (bottom - sustainY) * ShapeT(t, ch.EnvReleaseTension);
            pts.Add(new Point(px, py));
        }

        // Faint fill under the curve
        var fillGeo = new StreamGeometry();
        using (var fctx = fillGeo.Open())
        {
            fctx.BeginFigure(new Point(inner.X, bottom), true, true);
            foreach (var p in pts) fctx.LineTo(p, true, false);
            fctx.LineTo(new Point(inner.Right, bottom), true, false);
        }
        fillGeo.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(38, Accent.R, Accent.G, Accent.B)), null, fillGeo);

        // Stroke
        var geo = new StreamGeometry();
        using (var gctx = geo.Open())
        {
            gctx.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Count; i++) gctx.LineTo(pts[i], true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Accent), 2) { LineJoin = PenLineJoin.Round }, geo);

        // Breakpoint nodes
        void Dot(Point p) => dc.DrawEllipse(new SolidColorBrush(Accent), new Pen(Brushes.Black, 0.6), p, 3.2, 3.2);
        Dot(new Point(xDelayEnd, bottom));
        Dot(new Point(xHoldEnd, top));
        Dot(new Point(xDecayEnd, sustainY));
        Dot(new Point(xSustainEnd, sustainY));
    }

    private static double VisW(double seconds, double maxSeconds)
        => 0.4 + 0.6 * Math.Clamp(seconds / Math.Max(maxSeconds, 1e-6), 0, 1);

    /// <summary>Mirrors ChannelRackBusProvider's curve bend so the graph matches what you'll hear.</summary>
    private static double ShapeT(double t, double tension)
    {
        t = Math.Clamp(t, 0, 1);
        double exponent = Math.Pow(2, 3 * Math.Clamp(tension, -1, 1));
        return Math.Pow(t, exponent);
    }

    private static double NormEK(EK k, double v) => Math.Clamp((v - k.Min) / (k.Max - k.Min), 0, 1);

    private static string FmtEK(EK k, double v) => k.Unit switch
    {
        "s" => v < 1 ? $"{v * 1000:F0}ms" : $"{v:F2}s",
        "%" => $"{v * 100:F0}%",
        "t" => $"{v:+0.00;-0.00;0.00}",
        _   => $"{v:F2}"
    };

    // ── Mouse ────────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var ch = Channel; if (ch == null) return;
        var p = e.GetPosition(this);

        if (_cutFieldR.Contains(p)) { ch.CutGroup   = (ch.CutGroup   + 1) % 9; e.Handled = true; return; }
        if (_byFieldR.Contains(p))  { ch.CutByGroup = (ch.CutByGroup + 1) % 9; e.Handled = true; return; }
        if (_cutSelfR.Contains(p))  { ch.CutSelf    = !ch.CutSelf;             e.Handled = true; return; }
        if (_envEnableR.Contains(p)){ ch.EnvelopeEnabled = !ch.EnvelopeEnabled; e.Handled = true; return; }

        if (e.ClickCount == 2)
            for (int i = 0; i < _kc.Length && i < AllKnobs.Length; i++)
            { var d = p - _kc[i]; double hr = _kr[i] + 10; if (d.X*d.X+d.Y*d.Y<=hr*hr) { AllKnobs[i].Set(ch, AllKnobs[i].Def); e.Handled=true; return; } }

        for (int i = 0; i < _kc.Length && i < AllKnobs.Length; i++)
        { var d = p - _kc[i]; double hr = _kr[i] + 12; if (d.X*d.X+d.Y*d.Y<=hr*hr) { _dk=i; _dy=p.Y; _db=AllKnobs[i].Get(ch); CaptureMouse(); e.Handled=true; return; } }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dk < 0 || Channel == null) return;
        var k = AllKnobs[_dk]; double dy = _dy - e.GetPosition(this).Y;
        bool sh = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        double s = sh ? 600 : 200;
        k.Set(Channel, Math.Clamp(_db + dy * (k.Max - k.Min) / s, k.Min, k.Max));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonUp(e); if (_dk >= 0) { _dk = -1; ReleaseMouseCapture(); } }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var ch = Channel; if (ch == null) return;
        var p = e.GetPosition(this);

        if (_cutFieldR.Contains(p)) { ch.CutGroup   = (ch.CutGroup   + 8) % 9; e.Handled = true; return; }
        if (_byFieldR.Contains(p))  { ch.CutByGroup = (ch.CutByGroup + 8) % 9; e.Handled = true; return; }

        for (int i = 0; i < _kc.Length && i < AllKnobs.Length; i++)
        { var d = p - _kc[i]; double hr = _kr[i] + 12; if (d.X*d.X+d.Y*d.Y<=hr*hr) { AllKnobs[i].Set(ch, AllKnobs[i].Def); e.Handled=true; return; } }
    }

    protected override Size MeasureOverride(Size av)
        => new(Math.Max(double.IsInfinity(av.Width) ? 420 : av.Width, 360),
               Math.Max(double.IsInfinity(av.Height) ? 620 : av.Height, 560));
}
