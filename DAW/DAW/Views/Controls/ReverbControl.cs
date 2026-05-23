using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using static DAW.Views.Controls.DragonUI;

namespace DAW.Views.Controls;

/// <summary>
/// Room Reverb — Red Dragon Design.
///
/// Fixed layout (540 × 540 window):
///   Row 0  [HDR_H]       Header  +  Mode selector (◄ NAME ►)
///   Row 1  [ROW_H]       3 global knobs: MIX  PRE-DLY  DEPTH
///   ─── EARLY ─── (left half, ROW_H*2)  6 knobs 3×2
///   ─── LATE  ─── (right half)          DECAY (large) + 4 small knobs 2×2
///   Row N  [ROW_H]       3 tone knobs: HI-CUT  HI SHELF  LO SHELF
/// </summary>
public sealed class ReverbControl : FrameworkElement
{
    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(ReverbEffect), typeof(ReverbControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ReverbEffect? Effect
    { get => (ReverbEffect?)GetValue(EffectProperty); set => SetValue(EffectProperty, value); }

    private record struct KD(string L, double Min, double Max,
        Func<ReverbEffect, double> Get, Action<ReverbEffect, double> Set,
        string Unit, double Def, bool Log = false);

    private static readonly KD[] GK =
    [
        new("MIX",    0,1,    e=>e.Mix,      (e,v)=>e.Mix=v,      "%", 0.35),
        new("PRE-DLY",0,200,  e=>e.PreDelay, (e,v)=>e.PreDelay=v, "ms",20),
        new("DEPTH",  0,1,    e=>e.Depth,    (e,v)=>e.Depth=v,    "%", 0.5),
    ];
    private static readonly KD[] EK =
    [
        new("SIZE",  0,1,  e=>e.EarlySize,      (e,v)=>e.EarlySize=v,      "%",0.5),
        new("DIFF",  0,1,  e=>e.EarlyDiffusion, (e,v)=>e.EarlyDiffusion=v, "%",0.7),
        new("CROSS", 0,1,  e=>e.EarlyCross,     (e,v)=>e.EarlyCross=v,     "%",0.3),
        new("SEND",  0,1,  e=>e.EarlySend,      (e,v)=>e.EarlySend=v,      "%",0.6),
        new("MOD R", 0.1,5,e=>e.EarlyModRate,   (e,v)=>e.EarlyModRate=v,   "Hz",0.8),
        new("MOD D", 0,1,  e=>e.EarlyModDepth,  (e,v)=>e.EarlyModDepth=v,  "%",0.3),
    ];
    private static readonly KD[] LK =
    [
        new("DECAY", 0.1,30,e=>e.Decay,    (e,v)=>e.Decay=v,    "s",2.0,Log:true),
        new("SIZE",  0,1,   e=>e.LateSize, (e,v)=>e.LateSize=v, "%",0.5),
        new("CROSS", 0,1,   e=>e.LateCross,(e,v)=>e.LateCross=v,"%",0.3),
        new("BASS×", 0.5,2, e=>e.BassMult, (e,v)=>e.BassMult=v, "×",1.0),
        new("BXOVER",50,500,e=>e.BassXover,(e,v)=>e.BassXover=v,"Hz",200,Log:true),
    ];
    private static readonly KD[] TK =
    [
        new("HI-CUT", 1000,20000,e=>e.HighCut,  (e,v)=>e.HighCut=v,  "Hz",8000,Log:true),
        new("HI SHELF",-12,0,    e=>e.HighShelf,(e,v)=>e.HighShelf=v,"dB",-3),
        new("LO SHELF",-6,6,     e=>e.LowShelf, (e,v)=>e.LowShelf=v, "dB", 0),
    ];
    private static readonly KD[] All = [.. GK, .. EK, .. LK, .. TK];

    private int      _dk = -1;
    private double   _dy, _db;
    private Point[]  _kc = [];
    private double[] _kr = [];
    private Rect _modeR, _mPrev, _mNext;
    private readonly DispatcherTimer _timer;

    public ReverbControl()
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
        if (W < 60 || H < 60) return;

        dc.DrawRoundedRectangle(BBg, PBorder, new Rect(0, 0, W, H), 6, 6);
        DrawScrew(dc, 8, 8); DrawScrew(dc, W - 8, 8);
        DrawScrew(dc, 8, H - 8); DrawScrew(dc, W - 8, H - 8);

        var rev = Effect;
        if (rev == null) { DrawHeader(dc, new Rect(0, 0, W, HDR_H), "ROOM", "Reverb"); return; }

        // ── Row 0: Header + mode selector ──
        DrawHeader(dc, new Rect(0, 0, W, HDR_H), "ROOM", "Reverb");

        string modeName = rev.ReverbMode >= 0 && rev.ReverbMode < ReverbEffect.ModeNames.Length
            ? ReverbEffect.ModeNames[rev.ReverbMode] : "?";
        double mW = 110, arW = 22;
        _modeR = new Rect(W / 2 - mW / 2, 6, mW, HDR_H - 12);
        _mPrev = new Rect(_modeR.X - arW - 4, _modeR.Y, arW, _modeR.Height);
        _mNext = new Rect(_modeR.Right + 4,   _modeR.Y, arW, _modeR.Height);

        dc.DrawRoundedRectangle(BSurface, PBorderRed, _modeR, 3, 3);
        dc.DrawRoundedRectangle(BSurface, PBorder, _mPrev, 3, 3);
        dc.DrawRoundedRectangle(BSurface, PBorder, _mNext, 3, 3);

        var pT = Txt("◄", 8.5, BRed); dc.DrawText(pT, Ctr(_mPrev, pT));
        var nT = Txt("►", 8.5, BRed); dc.DrawText(nT, Ctr(_mNext, nT));
        var mT = Txt(modeName, 9, BTextPri, FontWeights.SemiBold, TFCond);
        dc.DrawText(mT, Ctr(_modeR, mT));

        _kc = new Point[All.Length]; _kr = new double[All.Length];
        int ki = 0;

        // ── Row 1: 3 global knobs ──
        double r1top = HDR_H + PAD;
        var gcxs = Columns(PAD, W - PAD * 2, 3);
        double gkcy = KnobCY(r1top, KR);
        foreach (var k in GK)
        {
            double cx = gcxs[ki]; _kc[ki] = new Point(cx, gkcy); _kr[ki] = KR;
            double v = k.Get(rev);
            DrawKnob(dc, cx, gkcy, KR, Norm(k, v), CGold, k.L, FmtK(k, v));
            ki++;
        }

        // ── Mid: Early (left) + Late (right) ──
        double midTop = r1top + ROW_H + GAP;
        double midH   = ROW_H * 2 + GAP;
        double halfW  = (W - PAD * 3) / 2;
        var eR = new Rect(PAD, midTop, halfW, midH);
        var lR = new Rect(PAD * 2 + halfW, midTop, halfW, midH);

        DrawSection(dc, eR, "Early Reflections", CRed);
        DrawSection(dc, lR, "Late Reverberation", CGold);

        // Early: 3×2 grid
        double ekR2 = Math.Clamp(Math.Min((halfW - 24) / 6.0, (midH - 28) / 5.0), 10, 18);
        double eSp = (halfW - 16) / 3.0, eRH = (midH - 26) / 2.0;
        int ei = 0;
        foreach (var k in EK)
        {
            int col = ei % 3, row = ei / 3;
            double cx = eR.X + 8 + eSp * col + eSp / 2;
            double cy = eR.Y + 22 + eRH * row + eRH / 2;
            _kc[ki] = new Point(cx, cy); _kr[ki] = ekR2;
            DrawKnob(dc, cx, cy, ekR2, Norm(k, k.Get(rev)), CRed, k.L, FmtK(k, k.Get(rev)));
            ki++; ei++;
        }

        // Late: DECAY large knob top-centre, 4 small in 2×2
        double decR = Math.Clamp(midH * 0.22, 18, 32);
        double dcx = lR.X + lR.Width / 2, dcy = lR.Y + 24 + decR;
        _kc[ki] = new Point(dcx, dcy); _kr[ki] = decR;
        { var k = LK[0]; DrawKnob(dc, dcx, dcy, decR, Norm(k, k.Get(rev)), CGold, k.L, FmtK(k, k.Get(rev)), glow: true); ki++; }

        double lkR2 = Math.Clamp((halfW - 24) / 4.5, 9, 14);
        double lSp = (halfW - 16) / 2.0, lrY = dcy + decR + 24;
        for (int li = 1; li < LK.Length; li++)
        {
            int col = (li - 1) % 2, row = (li - 1) / 2;
            double cx = lR.X + 8 + lSp * col + lSp / 2;
            double cy = lrY + (lkR2 * 2 + 22) * row + lkR2;
            _kc[ki] = new Point(cx, cy); _kr[ki] = lkR2;
            DrawKnob(dc, cx, cy, lkR2, Norm(LK[li], LK[li].Get(rev)), CGold, LK[li].L, FmtK(LK[li], LK[li].Get(rev)));
            ki++;
        }

        // ── Tone row (3 knobs, bottom) ──
        double toneTop = midTop + midH + GAP;
        double toneH   = ROW_H + GAP;
        var toneR = new Rect(PAD, toneTop, W - PAD * 2, toneH);
        DrawSection(dc, toneR, "Tone EQ", CTextSec);
        double tkR2 = Math.Clamp(toneH * 0.28, 10, 18);
        var tcxs = Columns(PAD, W - PAD * 2, TK.Length);
        double tcy = toneTop + 22 + tkR2;
        for (int ti = 0; ti < TK.Length; ti++)
        {
            double cx = tcxs[ti];
            _kc[ki] = new Point(cx, tcy); _kr[ki] = tkR2;
            DrawKnob(dc, cx, tcy, tkR2, Norm(TK[ti], TK[ti].Get(rev)), CTextSec, TK[ti].L, FmtK(TK[ti], TK[ti].Get(rev)));
            ki++;
        }
    }

    private static double Norm(KD k, double v)
        => k.Log
            ? (Math.Log10(Math.Max(v, 1e-6)) - Math.Log10(Math.Max(k.Min, 1e-6)))
              / (Math.Log10(k.Max) - Math.Log10(Math.Max(k.Min, 1e-6)))
            : Math.Clamp((v - k.Min) / (k.Max - k.Min), 0, 1);

    private static string FmtK(KD k, double v) => k.Unit switch
    {
        "%" => $"{v * 100:F0}%",
        "ms" => $"{v:F0}ms",
        "Hz" => v >= 1000 ? $"{v / 1000:F1}k" : $"{v:F0}",
        "s"  => v >= 10 ? $"{v:F0}s" : $"{v:F1}s",
        "dB" => $"{v:F1}",
        "×"  => $"{v:F2}×",
        _    => $"{v:F1}"
    };

    // Centre a FormattedText in a Rect
    private static Point Ctr(Rect r, FormattedText t)
        => new(r.X + (r.Width - t.Width) / 2, r.Y + (r.Height - t.Height) / 2);

    // ── Mouse ──────────────────────────────────────────────────────────────
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var rev = Effect; if (rev == null) return;
        var p = e.GetPosition(this);

        if (_mPrev.Contains(p))
        { rev.ReverbMode = (rev.ReverbMode - 1 + ReverbEffect.ModeNames.Length) % ReverbEffect.ModeNames.Length; e.Handled = true; return; }
        if (_mNext.Contains(p) || _modeR.Contains(p))
        { rev.ReverbMode = (rev.ReverbMode + 1) % ReverbEffect.ModeNames.Length; e.Handled = true; return; }

        if (e.ClickCount == 2)
            for (int i = 0; i < _kc.Length && i < All.Length; i++)
            { var d = p - _kc[i]; double hr = _kr[i] + 10; if (d.X*d.X+d.Y*d.Y<=hr*hr) { All[i].Set(rev, All[i].Def); e.Handled=true; return; } }

        for (int i = 0; i < _kc.Length && i < All.Length; i++)
        { var d = p - _kc[i]; double hr = _kr[i] + 12; if (d.X*d.X+d.Y*d.Y<=hr*hr) { _dk=i; _dy=p.Y; _db=All[i].Get(rev); CaptureMouse(); e.Handled=true; return; } }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dk < 0 || Effect == null) return;
        var k = All[_dk]; double dy = _dy - e.GetPosition(this).Y;
        bool sh = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        double s = sh ? 600 : 200;
        if (k.Log) { double lMin=Math.Log10(Math.Max(k.Min,1e-6)),lMax=Math.Log10(k.Max); k.Set(Effect,Math.Pow(10,Math.Clamp(Math.Log10(Math.Max(_db,1e-6))+dy*(lMax-lMin)/s,lMin,lMax))); }
        else k.Set(Effect, Math.Clamp(_db + dy * (k.Max - k.Min) / s, k.Min, k.Max));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonUp(e); if (_dk >= 0) { _dk = -1; ReleaseMouseCapture(); } }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e); if (Effect == null) return;
        var p = e.GetPosition(this);
        for (int i = 0; i < _kc.Length && i < All.Length; i++)
        { var d = p - _kc[i]; double hr = _kr[i] + 12; if (d.X*d.X+d.Y*d.Y<=hr*hr) { All[i].Set(Effect, All[i].Def); e.Handled=true; return; } }
    }

    protected override Size MeasureOverride(Size av)
        => new(Math.Max(double.IsInfinity(av.Width)  ? 560 : av.Width,  480),
               Math.Max(double.IsInfinity(av.Height) ? 580 : av.Height, 490));
}
