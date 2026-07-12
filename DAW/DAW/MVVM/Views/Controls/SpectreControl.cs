using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using static DAW.MVVM.Views.Controls.DragonUI;

namespace DAW.MVVM.Views.Controls;

/// <summary>
/// Spectre — Compact Parallel Multiband Saturator UI
///
/// Layout (top→bottom):
///   ┌─ Header ──────────────────────────────────────────────────────────┐
///   │  SPECTRE  Multiband Saturator   [Subtle|Medium|Aggr]  [Off|4x|16x]│
///   ├─ EQ Canvas (draggable nodes, right-click = context menu) ─────────┤
///   │  ●  ●  ●  ●  ●   (5 band handles — drag for freq/gain)           │
///   ├─ Global bar (INPUT / MIX / OUTPUT / DE-EMPH) ──────────────────── │
///   └────────────────────────────────────────────────────────────────────┘
///
/// Right-click on any node opens a ContextMenu with:
///   • Saturation algorithm (11 items)
///   • Channel mode         (5 items)
///   • Reset gain
///   • Enable / Disable band
/// </summary>
public sealed class SpectreControl : FrameworkElement
{
    // ── Dependency property ───────────────────────────────────────────────────
    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(SpectreEffect), typeof(SpectreControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnEffectChanged));

    public SpectreEffect? Effect
    {
        get => (SpectreEffect?)GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    private static void OnEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (SpectreControl)d;
        if (e.OldValue is SpectreEffect old)
        {
            old.PropertyChanged -= ctrl.OnProp;
            foreach (var b in old.Bands) b.PropertyChanged -= ctrl.OnProp;
        }
        if (e.NewValue is SpectreEffect nfx)
        {
            nfx.PropertyChanged += ctrl.OnProp;
            foreach (var b in nfx.Bands) b.PropertyChanged += ctrl.OnProp;
        }
        ctrl.InvalidateVisual();
    }

    private void OnProp(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Color CEqBg   = Color.FromRgb(0x09, 0x11, 0x18);
    private static readonly Color CEqGrid = Color.FromRgb(0x16, 0x20, 0x2C);
    private static readonly Color CEqZero = Color.FromRgb(0x22, 0x30, 0x42);

    private static readonly Color[] CBand =
    [
        Color.FromRgb(0xFF, 0x6B, 0x35), // 0 Lo  — orange
        Color.FromRgb(0xFF, 0xD1, 0x30), // 1     — yellow
        Color.FromRgb(0x2E, 0xCC, 0x71), // 2     — green
        Color.FromRgb(0x3B, 0x82, 0xF6), // 3     — blue
        Color.FromRgb(0xA8, 0x55, 0xFF), // 4 Hi  — purple
    ];

    private static readonly Pen PGrid = P(CEqGrid, 0.5);
    private static readonly Pen PZero = P(CEqZero, 1.0);

    // ── Layout ────────────────────────────────────────────────────────────────
    private const double HdrH    = 38;
    private const double CanvasH  = 200;
    private const double GlobH    = 52;
    private const double Pad      = 8;

    // Minimum window content size used by PluginWindow
    public const double MinW = 560;
    public const double MinH = HdrH + Pad + CanvasH + Pad + GlobH + Pad;

    // ── EQ constants ──────────────────────────────────────────────────────────
    private const double LogMin = 1.30103;   // log10(20)
    private const double LogMax = 4.30103;   // log10(20000)
    private const double MaxDb  = 24.0;
    private const double NodeR  = 8.0;

    // ── Hit areas ─────────────────────────────────────────────────────────────
    private Rect    _canvasRect;
    private Point[] _nodePos      = new Point[SpectreEffect.BandCount];

    // Header
    private Rect[] _satBtns = new Rect[3];
    private Rect[] _osBtns  = new Rect[3];

    // Global bar
    private Rect   _inSliderTrack, _mixSliderTrack, _outSliderTrack;
    private Rect   _deEmphBtn;
    private Rect   _inSliderThumb, _mixSliderThumb, _outSliderThumb;

    // ── Drag state ────────────────────────────────────────────────────────────
    private int    _dragBand   = -1;
    private Point  _dragStart;
    private float  _dragFreq0, _dragGain0;

    private enum SliderDrag { None, Input, Mix, Output }
    private SliderDrag _sDrag;
    private double _sDragX, _sDragBase;

    // ── Constructor ───────────────────────────────────────────────────────────
    public SpectreControl()
    {
        ClipToBounds      = true;
        SnapsToDevicePixels = true;
        Focusable         = true;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  RENDER
    // ══════════════════════════════════════════════════════════════════════════

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W < 80 || H < 80) return;

        var fx = Effect;

        // ── Background ──
        dc.DrawRoundedRectangle(BBg, PBorder, new Rect(0, 0, W, H), 6, 6);
        DrawScrew(dc, 8, 8); DrawScrew(dc, W - 8, 8);
        DrawScrew(dc, 8, H - 8); DrawScrew(dc, W - 8, H - 8);

        // ── Header ──
        DrawHeader(dc, new Rect(0, 0, W, HdrH), "SPECTRE", "Multiband Saturator");
        if (fx != null) RenderHeaderButtons(dc, fx, W);

        double y = HdrH + Pad;

        // ── EQ Canvas ──
        _canvasRect = new Rect(Pad, y, W - Pad * 2, CanvasH);
        RenderCanvas(dc, fx, _canvasRect);
        y += CanvasH + Pad;

        // ── Global bar ──
        RenderGlobal(dc, fx, new Rect(Pad, y, W - Pad * 2, GlobH));
    }

    // ── Header: SatMode + OS buttons ─────────────────────────────────────────

    private void RenderHeaderButtons(DrawingContext dc, SpectreEffect fx, double W)
    {
        double bH = 18, bY = (HdrH - bH) / 2;
        double satW = 58, osW = 30, gap = 3;
        double totalW = 3 * satW + 2 * gap + 8 + 3 * osW + 2 * gap;
        double x = W - Pad - totalW;

        for (int i = 0; i < 3; i++)
        {
            _satBtns[i] = new Rect(x + i * (satW + gap), bY, satW, bH);
            DrawButton(dc, _satBtns[i], SpectreEffect.SatModeNames[i], fx.SatMode == i, 7.5);
        }

        x += 3 * (satW + gap) + 8;
        string[] osL = ["Off", "4×", "16×"];
        for (int i = 0; i < 3; i++)
        {
            _osBtns[i] = new Rect(x + i * (osW + gap), bY, osW, bH);
            DrawButton(dc, _osBtns[i], osL[i], fx.Oversampling == i, 7.5);
        }
    }

    // ── EQ Canvas ─────────────────────────────────────────────────────────────

    private void RenderCanvas(DrawingContext dc, SpectreEffect? fx, Rect r)
    {
        // BG gradient
        var bg = new LinearGradientBrush(CEqBg, Color.FromRgb(0x06, 0x0D, 0x12), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 1), r, 4, 4);

        double x0 = r.X, y0 = r.Y, w = r.Width, h = r.Height;

        // Freq grid
        double[] freqs = [50, 100, 200, 500, 1000, 2000, 5000, 10000];
        foreach (var f in freqs)
        {
            double px = x0 + FreqToX(f, w);
            bool major = f is 100 or 1000 or 10000;
            dc.DrawLine(major ? PZero : PGrid, new Point(px, y0), new Point(px, y0 + h));
            if (major)
            {
                string lbl = f >= 1000 ? $"{f / 1000}k" : $"{f}";
                var t = Txt(lbl, 7, BTextDim, tf: TFMono);
                dc.DrawText(t, new Point(px - t.Width / 2, y0 + h - t.Height - 2));
            }
        }

        // dB grid
        foreach (double db in new[] { 6.0, 12.0, 18.0, 24.0 })
        {
            double py = y0 + DbToY(db, h);
            dc.DrawLine(PGrid, new Point(x0, py), new Point(x0 + w, py));
            var t = Txt($"{db:F0}", 7, BTextDim, tf: TFMono);
            dc.DrawText(t, new Point(x0 + 3, py - t.Height / 2));
        }

        // Zero line
        double zy = y0 + h - 10;
        dc.DrawLine(PZero, new Point(x0, zy), new Point(x0 + w, zy));

        if (fx == null) return;

        // Band curves + nodes
        for (int b = 0; b < SpectreEffect.BandCount; b++)
        {
            var band = fx.Bands[b];
            if (band.Enabled && band.Gain >= 0.5f)
                RenderBandCurve(dc, band, r, CBand[b]);
        }

        for (int b = 0; b < SpectreEffect.BandCount; b++)
        {
            var band = fx.Bands[b];
            double nx = x0 + FreqToX(band.Frequency, w);
            double ny = y0 + DbToY(band.Gain, h);
            _nodePos[b] = new Point(nx, ny);
            RenderNode(dc, band, b, nx, ny);
        }
    }

    private static void RenderBandCurve(DrawingContext dc, SpectreBand band, Rect r, Color color)
    {
        double x0 = r.X, y0 = r.Y, w = r.Width, h = r.Height;
        double baseY = y0 + h - 10;

        var fill = new LinearGradientBrush(
            Color.FromArgb(55, color.R, color.G, color.B),
            Color.FromArgb(0,  color.R, color.G, color.B), 90);
        fill.Freeze();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(170, color.R, color.G, color.B)), 1.5);
        pen.Freeze();

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool first = true;
            for (int i = 0; i <= 90; i++)
            {
                double t    = i / 90.0;
                double freq = Math.Pow(10, LogMin + t * (LogMax - LogMin));
                double db   = Math.Clamp(BandResponse(band, freq), 0, MaxDb);
                double px   = x0 + FreqToX(freq, w);
                double py   = y0 + DbToY(db, h);
                if (first) { ctx.BeginFigure(new Point(px, baseY), true, true); ctx.LineTo(new Point(px, py), false, false); first = false; }
                else ctx.LineTo(new Point(px, py), true, false);
            }
            ctx.LineTo(new Point(x0 + w, baseY), false, false);
        }
        geo.Freeze();
        dc.DrawGeometry(fill, pen, geo);
    }

    private void RenderNode(DrawingContext dc, SpectreBand band, int idx, double nx, double ny)
    {
        var color  = CBand[idx];
        bool active = band.Enabled && band.Gain > 0.01f;
        byte alpha  = active ? (byte)255 : (byte)70;
        var nc = Color.FromArgb(alpha, color.R, color.G, color.B);

        // Glow ring
        if (active)
        {
            var glow = new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B));
            glow.Freeze();
            dc.DrawEllipse(glow, null, new Point(nx, ny), NodeR + 5, NodeR + 5);
        }

        // Node fill + outline
        dc.DrawEllipse(B(nc),
            new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha / 2 + 80), 255, 255, 255)), 1.2),
            new Point(nx, ny), NodeR, NodeR);

        // Band number
        var lbl = Txt($"{idx + 1}", 7, B(Color.FromArgb(alpha, 255, 255, 255)), FontWeights.Bold, TFCond);
        dc.DrawText(lbl, new Point(nx - lbl.Width / 2, ny - lbl.Height / 2));

        // Tooltip below node: algo + channel abbreviation
        if (band.Enabled)
        {
            string tip = SpectreEffect.AlgorithmNames[band.Algorithm];
            string ch  = band.ChannelMode switch { 1 => "L", 2 => "R", 3 => "M", 4 => "S", _ => "" };
            if (ch.Length > 0) tip += $" · {ch}";
            var tipT = Txt(tip, 6.5, B(Color.FromArgb(160, color.R, color.G, color.B)), tf: TFCond);
            dc.DrawText(tipT, new Point(nx - tipT.Width / 2, ny + NodeR + 2));
        }
    }

    // ── Global bar ────────────────────────────────────────────────────────────

    private void RenderGlobal(DrawingContext dc, SpectreEffect? fx, Rect r)
    {
        // Dark section background
        var secBg = new LinearGradientBrush(
            Color.FromRgb(0x14, 0x18, 0x1E),
            Color.FromRgb(0x0E, 0x12, 0x18), 90);
        secBg.Freeze();
        dc.DrawRoundedRectangle(secBg, P(CBorder, 1), r, 4, 4);

        double W       = r.Width;
        double slotW   = (W - 140) / 3.0;   // width of each slider slot
        double sH      = 4;                  // track height
        double tW      = 10;                 // thumb width
        double trackY  = r.Y + r.Height / 2 - sH / 2 + 2;
        double labY    = r.Y + 8;
        double valY    = r.Y + r.Height / 2 + 6;
        double thumbH  = 18;
        double thumbY  = r.Y + r.Height / 2 - thumbH / 2 + 2;

        // INPUT
        double ix = r.X + 10;
        _inSliderTrack = new Rect(ix, trackY, slotW - 10, sH);
        double inNorm  = fx == null ? 0.5 : Math.Clamp((fx.InputGain + 24) / 48.0, 0, 1);
        _inSliderThumb = new Rect(ix + inNorm * (_inSliderTrack.Width - tW), thumbY, tW, thumbH);
        RenderSlot(dc, "INPUT", ix, labY, valY, _inSliderTrack, _inSliderThumb,
            fx?.InputGain ?? 0, $"{fx?.InputGain ?? 0:+0.0;-0.0}dB", CGold, inNorm);

        // MIX (center)
        double mx = ix + slotW + 10;
        _mixSliderTrack = new Rect(mx, trackY, slotW - 10, sH);
        double mixNorm  = fx?.Mix ?? 0.5f;
        _mixSliderThumb = new Rect(mx + mixNorm * (_mixSliderTrack.Width - tW), thumbY, tW, thumbH);
        RenderSlot(dc, "MIX", mx, labY, valY, _mixSliderTrack, _mixSliderThumb,
            mixNorm, $"{(fx?.Mix ?? 0.5f) * 100:F0}%", CRed, mixNorm);

        // OUTPUT
        double ox = mx + slotW + 10;
        _outSliderTrack = new Rect(ox, trackY, slotW - 10, sH);
        double outNorm  = fx == null ? 0.5 : Math.Clamp((fx.OutputGain + 24) / 48.0, 0, 1);
        _outSliderThumb = new Rect(ox + outNorm * (_outSliderTrack.Width - tW), thumbY, tW, thumbH);
        RenderSlot(dc, "OUTPUT", ox, labY, valY, _outSliderTrack, _outSliderThumb,
            fx?.OutputGain ?? 0, $"{fx?.OutputGain ?? 0:+0.0;-0.0}dB", CGold, outNorm);

        // DE-EMPH button (right side)
        _deEmphBtn = new Rect(r.Right - 70, r.Y + (r.Height - 20) / 2, 64, 20);
        DrawButton(dc, _deEmphBtn, "DE-EMPH", fx?.DeEmphasis ?? false, 8);
    }

    private static void RenderSlot(DrawingContext dc,
        string label, double x, double labY, double valY,
        Rect track, Rect thumb,
        double rawVal, string valStr,
        Color accentColor, double norm)
    {
        // Label
        var lT = Txt(label, 7.5, BTextDim, tf: TFCond);
        dc.DrawText(lT, new Point(x, labY));

        // Track bg
        dc.DrawRoundedRectangle(BSurface, null, track, 2, 2);

        // Filled portion
        double fillW = norm * (track.Width - 10);
        if (fillW > 0)
        {
            var fillBrush = new LinearGradientBrush(
                Color.FromArgb(180, accentColor.R, accentColor.G, accentColor.B),
                Color.FromArgb(100, accentColor.R, accentColor.G, accentColor.B), 0);
            fillBrush.Freeze();
            dc.DrawRoundedRectangle(fillBrush, null,
                new Rect(track.X, track.Y, fillW, track.Height), 2, 2);
        }

        // Thumb
        var thumbGrad = new LinearGradientBrush(
            Color.FromRgb(0x4A, 0x4A, 0x4A),
            Color.FromRgb(0x28, 0x28, 0x28), 90);
        thumbGrad.Freeze();
        dc.DrawRoundedRectangle(thumbGrad,
            new Pen(new SolidColorBrush(Color.FromArgb(120, accentColor.R, accentColor.G, accentColor.B)), 1),
            thumb, 2, 2);

        // Center notch on thumb
        double cx = thumb.X + thumb.Width / 2;
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(160, accentColor.R, accentColor.G, accentColor.B)), 1.5),
            new Point(cx, thumb.Y + 3), new Point(cx, thumb.Bottom - 3));

        // Value label
        var vT = Txt(valStr, 7.5, B(accentColor), FontWeights.SemiBold, TFMono);
        dc.DrawText(vT, new Point(x, valY));
    }

    // ── Coordinate math ───────────────────────────────────────────────────────

    private static double FreqToX(double freq, double w)
        => (Math.Log10(Math.Clamp(freq, 20, 20000)) - LogMin) / (LogMax - LogMin) * w;

    private static double DbToY(double db, double h)
        => (1.0 - db / MaxDb) * (h - 18) + 4;

    private static double YToDb(double y, double h)
        => Math.Clamp((1.0 - (y - 4) / (h - 18)) * MaxDb, 0, MaxDb);

    private static double XToFreq(double x, double w)
        => Math.Clamp(Math.Pow(10, LogMin + x / w * (LogMax - LogMin)), 20, 20000);

    // ── Band frequency response (same biquad as SpectreEffect) ───────────────

    private static double BandResponse(SpectreBand band, double freq)
    {
        if (band.Gain < 0.01f) return 0;
        const double sr = 44100;
        double w0    = 2.0 * Math.PI * band.Frequency / sr;
        double cosW  = Math.Cos(w0), sinW = Math.Sin(w0);
        double alpha = sinW / (2.0 * band.Q);
        double A     = Math.Pow(10.0, band.Gain / 40.0);
        double b0, b1, b2, a0, a1, a2;

        if (band.BandType == BandType.LowShelf)
        {
            double sa = Math.Sqrt(A);
            b0 = A*((A+1)-(A-1)*cosW+2*sa*alpha); b1 = 2*A*((A-1)-(A+1)*cosW); b2 = A*((A+1)-(A-1)*cosW-2*sa*alpha);
            a0 = (A+1)+(A-1)*cosW+2*sa*alpha;     a1 = -2*((A-1)+(A+1)*cosW); a2 = (A+1)+(A-1)*cosW-2*sa*alpha;
        }
        else if (band.BandType == BandType.HighShelf)
        {
            double sa = Math.Sqrt(A);
            b0 = A*((A+1)+(A-1)*cosW+2*sa*alpha); b1 = -2*A*((A-1)+(A+1)*cosW); b2 = A*((A+1)+(A-1)*cosW-2*sa*alpha);
            a0 = (A+1)-(A-1)*cosW+2*sa*alpha;     a1 = 2*((A-1)-(A+1)*cosW);    a2 = (A+1)-(A-1)*cosW-2*sa*alpha;
        }
        else
        {
            b0 = 1+alpha*A; b1 = -2*cosW; b2 = 1-alpha*A;
            a0 = 1+alpha/A; a1 = -2*cosW; a2 = 1-alpha/A;
        }

        b0/=a0; b1/=a0; b2/=a0; a1/=a0; a2/=a0;
        double ww = 2*Math.PI*freq/sr;
        double cw = Math.Cos(ww), c2 = Math.Cos(2*ww), sw = Math.Sin(ww), s2 = Math.Sin(2*ww);
        double nr = b0+b1*cw+b2*c2, ni = -(b1*sw+b2*s2);
        double dr = 1+a1*cw+a2*c2, di = -(a1*sw+a2*s2);
        double mag = Math.Sqrt((nr*nr+ni*ni)/Math.Max(dr*dr+di*di, 1e-20));
        return 20*Math.Log10(Math.Max(mag, 1e-10));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  INTERACTION
    // ══════════════════════════════════════════════════════════════════════════

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var fx = Effect; if (fx == null) return;
        var p = e.GetPosition(this);

        // Header buttons
        for (int i = 0; i < 3; i++) if (_satBtns[i].Contains(p)) { fx.SatMode    = i; e.Handled = true; return; }
        for (int i = 0; i < 3; i++) if (_osBtns[i].Contains(p))  { fx.Oversampling = i; e.Handled = true; return; }

        // De-emphasis
        if (_deEmphBtn.Contains(p)) { fx.DeEmphasis = !fx.DeEmphasis; e.Handled = true; return; }

        // Global sliders — check thumb hit first, then track
        var sl = HitSlider(p);
        if (sl != SliderDrag.None)
        {
            _sDrag = sl; _sDragX = p.X;
            _sDragBase = sl switch
            {
                SliderDrag.Input  => fx.InputGain,
                SliderDrag.Output => fx.OutputGain,
                _                 => fx.Mix
            };
            CaptureMouse(); e.Handled = true; return;
        }

        // EQ canvas — start node drag
        if (_canvasRect.Contains(p))
        {
            for (int b = 0; b < SpectreEffect.BandCount; b++)
            {
                var d = p - _nodePos[b];
                if (d.X * d.X + d.Y * d.Y <= (NodeR + 8) * (NodeR + 8))
                {
                    _dragBand = b; _dragStart = p;
                    _dragFreq0 = fx.Bands[b].Frequency;
                    _dragGain0 = fx.Bands[b].Gain;
                    CaptureMouse(); e.Handled = true; return;
                }
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var fx = Effect; if (fx == null) return;
        var p  = e.GetPosition(this);
        bool sh = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (_dragBand >= 0 && e.LeftButton == MouseButtonState.Pressed)
        {
            double dx      = p.X - _dragStart.X;
            double dy      = _dragStart.Y - p.Y;
            double newFreq = _dragFreq0 * Math.Pow(20000.0 / 20.0, dx / _canvasRect.Width);
            double newGain = _dragGain0 + dy / (_canvasRect.Height - 18) * MaxDb * (sh ? 0.2 : 1.0);
            fx.Bands[_dragBand].Frequency = (float)Math.Clamp(newFreq, 20, 20000);
            fx.Bands[_dragBand].Gain      = (float)Math.Clamp(newGain, 0, MaxDb);
            e.Handled = true;
        }
        else if (_sDrag != SliderDrag.None && e.LeftButton == MouseButtonState.Pressed)
        {
            var track = _sDrag switch { SliderDrag.Input => _inSliderTrack, SliderDrag.Output => _outSliderTrack, _ => _mixSliderTrack };
            double dx   = p.X - _sDragX;
            double sens = sh ? 0.2 : 1.0;
            double dNorm = dx / (track.Width - 10) * sens;

            if (_sDrag == SliderDrag.Mix)
                fx.Mix = (float)Math.Clamp(_sDragBase + dNorm, 0, 1);
            else
            {
                double v = Math.Clamp(_sDragBase + dNorm * 48.0, -24, 24);
                if (_sDrag == SliderDrag.Input)  fx.InputGain  = (float)v;
                else                             fx.OutputGain = (float)v;
            }
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragBand >= 0 || _sDrag != SliderDrag.None)
        {
            _dragBand = -1; _sDrag = SliderDrag.None;
            ReleaseMouseCapture(); e.Handled = true;
        }
    }

    // ── Right-click → context menu ────────────────────────────────────────────

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var fx = Effect; if (fx == null) return;
        var p  = e.GetPosition(this);

        // Global slider right-click = reset
        var sl = HitSlider(p);
        if (sl != SliderDrag.None)
        {
            if (sl == SliderDrag.Input)  fx.InputGain  = 0;
            if (sl == SliderDrag.Output) fx.OutputGain = 0;
            if (sl == SliderDrag.Mix)    fx.Mix        = 0.5f;
            e.Handled = true; return;
        }

        // Node right-click → context menu
        if (!_canvasRect.Contains(p)) return;
        for (int b = 0; b < SpectreEffect.BandCount; b++)
        {
            var d = p - _nodePos[b];
            if (d.X * d.X + d.Y * d.Y <= (NodeR + 10) * (NodeR + 10))
            {
                OpenBandMenu(fx.Bands[b], b);
                e.Handled = true; return;
            }
        }
    }

    private void OpenBandMenu(SpectreBand band, int idx)
    {
        var color = CBand[idx];
        var menu  = new ContextMenu { Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x20)) };

        // ── Header item (non-clickable, band name + colour) ──
        var header = new MenuItem
        {
            Header = BuildMenuHeader($"Band {idx + 1}  —  {band.DisplayName}", color),
            IsEnabled = false
        };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        // ── Saturation algorithm ──
        var algHeader = new MenuItem { Header = BuildMenuLabel("SATURATION"), IsEnabled = false };
        menu.Items.Add(algHeader);

        for (int i = 0; i < SpectreEffect.AlgorithmNames.Length; i++)
        {
            int capture = i;
            var item = new MenuItem
            {
                Header      = BuildMenuItemHeader(SpectreEffect.AlgorithmNames[i], i == band.Algorithm, color),
                IsCheckable = false,
                Tag         = capture
            };
            item.Click += (_, _) => { band.Algorithm = capture; };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        // ── Channel mode ──
        var chHeader = new MenuItem { Header = BuildMenuLabel("CHANNEL"), IsEnabled = false };
        menu.Items.Add(chHeader);

        string[] chIcons = ["⬜", "◧", "◨", "◈", "◉"];
        for (int i = 0; i < SpectreEffect.ChannelModeNames.Length; i++)
        {
            int capture = i;
            var item = new MenuItem
            {
                Header      = BuildMenuItemHeader($"{chIcons[i]}  {SpectreEffect.ChannelModeNames[i]}", i == band.ChannelMode, color),
                IsCheckable = false
            };
            item.Click += (_, _) => { band.ChannelMode = capture; };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        // ── Utility ──
        var resetItem = new MenuItem { Header = BuildMenuItemHeader("Reset Gain", false, Color.FromRgb(0x88, 0x88, 0x88)) };
        resetItem.Click += (_, _) => band.Gain = 0;
        menu.Items.Add(resetItem);

        var enableItem = new MenuItem
        {
            Header = BuildMenuItemHeader(band.Enabled ? "Disable Band" : "Enable Band", false,
                band.Enabled ? Color.FromRgb(0xFF, 0x6B, 0x6B) : Color.FromRgb(0x2E, 0xCC, 0x71))
        };
        enableItem.Click += (_, _) => band.Enabled = !band.Enabled;
        menu.Items.Add(enableItem);

        // Style the menu
        StyleContextMenu(menu);
        menu.IsOpen = true;
    }

    // ── Context menu builders ─────────────────────────────────────────────────

    private static object BuildMenuHeader(string text, Color accent)
    {
        var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            FontWeight = FontWeights.SemiBold, FontSize = 11, VerticalAlignment = VerticalAlignment.Center
        });
        return sp;
    }

    private static object BuildMenuLabel(string text)
        => new TextBlock
        {
            Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x66, 0x77)),
            FontSize = 9, FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 2, 0, 1), VerticalAlignment = VerticalAlignment.Center
        };

    private static object BuildMenuItemHeader(string text, bool active, Color accent)
    {
        var sp = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        // Active indicator dot
        sp.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 5, Height = 5, Margin = new Thickness(0, 0, 7, 0),
            Fill = active ? new SolidColorBrush(accent) : Brushes.Transparent,
            Stroke = active ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x44)),
            StrokeThickness = 1
        });
        sp.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(active
                ? Color.FromArgb(255, accent.R, accent.G, accent.B)
                : Color.FromRgb(0xBB, 0xBB, 0xBB)),
            FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal
        });
        return sp;
    }

    // XAML template string for the ContextMenu — gives us full control over
    // the popup background and eliminates the white icon-column panel that
    // WPF's default MenuItem template always renders.
    private const string MenuXaml = """
        <ContextMenu xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     Background="#14181F"
                     BorderBrush="#28303F"
                     BorderThickness="1"
                     Padding="0,4,0,4">
            <ContextMenu.Resources>
                <!-- Full MenuItem template: no icon column, no white panel -->
                <ControlTemplate x:Key="MI" TargetType="MenuItem">
                    <Border x:Name="Bd"
                            Background="Transparent"
                            Padding="12,3,16,3">
                        <ContentPresenter ContentSource="Header"
                                          VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                            <Setter TargetName="Bd" Property="Background" Value="#1E2530"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="Bd" Property="Opacity" Value="0.55"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
                <Style TargetType="MenuItem">
                    <Setter Property="Template" Value="{StaticResource MI}"/>
                    <Setter Property="Foreground" Value="#BBBBBB"/>
                    <Setter Property="Background" Value="Transparent"/>
                </Style>
                <Style TargetType="Separator">
                    <Setter Property="Background" Value="#28303F"/>
                    <Setter Property="Height" Value="1"/>
                    <Setter Property="Margin" Value="0,3,0,3"/>
                </Style>
            </ContextMenu.Resources>
        </ContextMenu>
        """;

    private static void StyleContextMenu(ContextMenu menu)
    {
        // Parse a fresh resource dictionary from the XAML template string
        // and copy the styles into the real menu's resources. This avoids
        // the white icon-column panel without needing a full XAML ContextMenu.
        var dummy = (ContextMenu)System.Windows.Markup.XamlReader.Parse(MenuXaml);

        menu.Background      = dummy.Background;
        menu.BorderBrush     = dummy.BorderBrush;
        menu.BorderThickness = dummy.BorderThickness;
        menu.Padding         = dummy.Padding;

        foreach (var key in dummy.Resources.Keys)
            menu.Resources[key] = dummy.Resources[key];
    }

    // ── Mousewheel: Q adjustment ──────────────────────────────────────────────

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var fx = Effect; if (fx == null) return;
        var p  = e.GetPosition(this);
        if (!_canvasRect.Contains(p)) return;

        for (int b = 0; b < SpectreEffect.BandCount; b++)
        {
            var d = p - _nodePos[b];
            if (d.X * d.X + d.Y * d.Y <= (NodeR + 10) * (NodeR + 10))
            {
                double factor = e.Delta > 0 ? 1.1 : 0.9;
                fx.Bands[b].Q = (float)Math.Clamp(fx.Bands[b].Q * factor, 0.1, 10);
                e.Handled = true; return;
            }
        }
    }

    // ── Slider hit test ───────────────────────────────────────────────────────

    private SliderDrag HitSlider(Point p)
    {
        // Expand hit area: full track height + thumb
        bool HitTrack(Rect track, Rect thumb)
        {
            var expanded = new Rect(track.X, track.Y - 10, track.Width, track.Height + 20);
            return expanded.Contains(p) || thumb.Contains(p);
        }

        if (HitTrack(_inSliderTrack,  _inSliderThumb))  return SliderDrag.Input;
        if (HitTrack(_mixSliderTrack, _mixSliderThumb)) return SliderDrag.Mix;
        if (HitTrack(_outSliderTrack, _outSliderThumb)) return SliderDrag.Output;
        return SliderDrag.None;
    }

    // ── Measure ───────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size av)
    {
        double h = double.IsInfinity(av.Height) ? MinH : Math.Max(av.Height, MinH);
        double w = double.IsInfinity(av.Width)  ? MinW : Math.Max(av.Width,  MinW);
        return new Size(w, h);
    }

    // ── Formatted text helper ─────────────────────────────────────────────────

    private static FormattedText Txt(string text, double size, Brush brush,
        FontWeight? weight = null, Typeface? tf = null)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf ?? TFSans, size, brush, 1.0);
        if (weight.HasValue) ft.SetFontWeight(weight.Value);
        return ft;
    }
}
