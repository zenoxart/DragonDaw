using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DAW.Audio.Effects;
using static DAW.MVVM.Views.Controls.DragonUI;

namespace DAW.MVVM.Views.Controls;

/// <summary>
/// Room Reverb — Dragon Particle-matched design.
///
/// Layout (resizes; minimum ≈480 × 620):
///   Row 0  [HDR_H]   Header + Mode selector (◄ NAME ►)
///   Row 1  [ROOM_H]  Room panel — live spatial visualization of the reverb
///   Row 2  [ROW_H]   GLOBAL panel (red): MIX  PRE-DLY  DEPTH  HEIGHT  WIDTH
///   Row 3            EARLY / LATE tab view (red / gold), both rendered as a
///                    uniform 4-column knob grid:
///                      Early tab: DIFF CROSS SEND MOD-R MOD-D + its own HI-CUT/HI-SHELF/LO-SHELF
///                      Late  tab: DECAY CROSS BASS× BXOVER + its own HI-CUT/HI-SHELF/LO-SHELF
///
/// Early and late reverberation each carry independent tone shaping now
/// (ReverbEffect.EarlyHighCut/EarlyHighShelf/EarlyLowShelf vs. the late
/// HighCut/HighShelf/LowShelf), so switching tabs edits genuinely separate
/// parameters rather than a shared EQ.
///
/// Every knob group sits in a gradient panel with a corner label, matching
/// the GAIN / TIME column panels used in MasterControl and CompressorControl —
/// gold = level/character-shaping, red = the primary/featured parameters
/// (mix + early reflections).
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
        new("HEIGHT", 0,1,    e=>e.EarlySize,(e,v)=>e.EarlySize=v,"%", 0.5),
        new("WIDTH",  0,1,    e=>e.LateSize, (e,v)=>e.LateSize=v, "%", 0.5),
    ];
    private static readonly KD[] EK =
    [
        new("DIFF",  0,1,  e=>e.EarlyDiffusion, (e,v)=>e.EarlyDiffusion=v, "%",0.7),
        new("CROSS", 0,1,  e=>e.EarlyCross,     (e,v)=>e.EarlyCross=v,     "%",0.3),
        new("SEND",  0,1,  e=>e.EarlySend,      (e,v)=>e.EarlySend=v,      "%",0.6),
        new("MOD R", 0.1,5,e=>e.EarlyModRate,   (e,v)=>e.EarlyModRate=v,   "Hz",0.8),
        new("MOD D", 0,1,  e=>e.EarlyModDepth,  (e,v)=>e.EarlyModDepth=v,  "%",0.3),
    ];
    private static readonly KD[] EQE =
    [
        new("HI-CUT", 1000,20000,e=>e.EarlyHighCut,  (e,v)=>e.EarlyHighCut=v,  "Hz",8000,Log:true),
        new("HI SHELF",-12,0,    e=>e.EarlyHighShelf,(e,v)=>e.EarlyHighShelf=v,"dB",-3),
        new("LO SHELF",-6,6,     e=>e.EarlyLowShelf, (e,v)=>e.EarlyLowShelf=v, "dB", 0),
    ];
    private static readonly KD[] LK =
    [
        new("DECAY", 0.1,30,e=>e.Decay,    (e,v)=>e.Decay=v,    "s",2.0,Log:true),
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
    private static readonly KD[] All = [.. GK, .. EK, .. EQE, .. LK, .. TK];

    // Contiguous sub-views used by the Early/Late tab content — these line up
    // exactly with their position inside `All` because EK+EQE and LK+TK are
    // adjacent ranges in the concatenation above, so a knob's index in these
    // arrays plus its section's base offset always matches `All`.
    private static readonly KD[] EarlyTab  = [.. EK, .. EQE];
    private static readonly KD[] LateTab   = [.. LK, .. TK];

    // ── Dragon Particle accent palette (mirrors MasterControl / CompressorControl) ─
    private static readonly Color CGlobalCol = Color.FromRgb(0xC4, 0x1E, 0x3A); // red   — global / featured (Mix)
    private static readonly Color CEarlyCol  = Color.FromRgb(0xC4, 0x1E, 0x3A); // red   — early reflections
    private static readonly Color CLateCol   = Color.FromRgb(0xD4, 0xA0, 0x17); // gold  — late reverberation

    private int      _dk = -1;
    private double   _dy, _db;
    private Point[]  _kc = [];
    private double[] _kr = [];
    private Rect _modeR, _mPrev, _mNext;
    private readonly DispatcherTimer _timer;

    private const double ROOM_H = 128; // room visualization panel height

    // ── Room visualization state ─────────────────────────────────────────
    // Renders the reverb as a small architectural room instead of an abstract
    // graph: sound radiates from the source and reflects off the walls
    // (gold = direct → cyan = early reflections → purple = late tail), with
    // late energy leaving a fading volumetric fog behind. Geometry and
    // behaviour are driven directly by the effect's own parameters:
    //   width ← LateSize   height ← EarlySize   depth ← Depth (early/late balance)
    //   early amount ← EarlySend   diffusion ← EarlyDiffusion
    //   damping ← HighCut (lower cutoff = more HF absorption)
    //   decay / fog persistence ← Decay   stereo spread ← 1-EarlyCross
    private static readonly Color RoomDirect = Color.FromRgb(0xF2, 0xB5, 0x63); // gold
    private static readonly Color RoomEarly  = Color.FromRgb(0x54, 0xE6, 0xCF); // cyan
    private static readonly Color RoomLate   = Color.FromRgb(0xA0, 0x84, 0xF5); // purple

    private sealed class RoomRay
    { public double X, Y, Z, Vx, Vy, Vz, Alpha, Age; public int Bounces; }
    private sealed class RoomFog
    { public Point P; public double Age, MaxAge, Radius, Strength; public Color Color; }
    private sealed class RoomFlash
    { public Point P; public double Age, Max, Scale; public Color Color; }
    private readonly record struct RoomGeo(double X0, double Y0, double X1, double Y1, double Dx, double Dy, double DepthPx);

    private readonly List<RoomRay>   _rays    = [];
    private readonly List<RoomFog>   _fog     = [];
    private readonly List<RoomFlash> _flashes = [];
    private readonly Random          _roomRng = new();
    private DateTime _roomLastTick  = DateTime.Now;
    private DateTime _roomLastPulse = DateTime.MinValue;
    private RoomGeo  _roomGeo;
    private Rect     _roomRect;

    // ── Early/Late tab view state ────────────────────────────────────────
    private int  _activeReverbTab; // 0 = Early Reflections, 1 = Late Reverberation
    private Rect _tabEarlyR, _tabLateR;

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

        // ── Room visualization panel ──
        double roomTop = HDR_H + PAD;
        var roomRect = new Rect(PAD, roomTop, W - PAD * 2, ROOM_H);
        DrawRoomPanel(dc, roomRect, rev);

        _kc = new Point[All.Length]; _kr = new double[All.Length];
        Array.Fill(_kc, new Point(-1000, -1000)); // knobs not drawn this frame (hidden tab) stay unclickable

        // ── Row 1: GLOBAL panel — MIX, PRE-DLY, DEPTH, HEIGHT, WIDTH ──
        double r1top = roomTop + ROOM_H + GAP;
        double r1H   = ROW_H;
        var globalR = new Rect(PAD, r1top, W - PAD * 2, r1H);
        DrawPanel(dc, globalR, "Global", CGlobalCol);
        var gcxs = Columns(PAD, W - PAD * 2, GK.Length);
        double gr = Math.Clamp((W - PAD * 2) / GK.Length * 0.30, 13, KR);
        double gkcy = KnobCY(r1top, gr);
        for (int gi = 0; gi < GK.Length; gi++)
        {
            var k = GK[gi];
            double cx = gcxs[gi]; _kc[gi] = new Point(cx, gkcy); _kr[gi] = gr;
            double v = k.Get(rev);
            DrawKnob(dc, cx, gkcy, gr, Norm(k, v), CGlobalCol, k.L, FmtK(k, v));
        }

        // ── Early / Late tab view ──
        double tabTop = r1top + r1H + GAP;
        double tabH   = ROW_H * 2 + GAP + 26;
        var tabArea = new Rect(PAD, tabTop, W - PAD * 2, tabH);
        DrawReverbTabs(dc, tabArea, rev);
    }

    // ── Gradient panel with corner label — matches GAIN/TIME column panels
    //    in CompressorControl / MasterControl. Replaces the plain red-edge
    //    DrawSection so every knob group reads as part of the same system. ─
    private static void DrawPanel(DrawingContext dc, Rect r, string label, Color accent)
    {
        var bg = new LinearGradientBrush(Color.FromRgb(0x16, 0x1A, 0x22),
                                          Color.FromRgb(0x11, 0x14, 0x1A), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 0.8), r, 4, 4);

        var lbl = Txt(label.ToUpper(), 7.5, B(Color.FromArgb(110, accent.R, accent.G, accent.B)),
            FontWeights.Bold, TFCond);
        dc.DrawText(lbl, new Point(r.X + 8, r.Y + 6));
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

    // ── Room visualization ─────────────────────────────────────────
    private void DrawRoomPanel(DrawingContext dc, Rect area, ReverbEffect rev)
    {
        DrawPanel(dc, area, "Room", CGold);
        _roomRect = area;

        var now = DateTime.Now;
        double dt = Math.Min((now - _roomLastTick).TotalSeconds, 0.05);
        _roomLastTick = now;

        double inset = 10;
        var inner = new Rect(area.X + inset, area.Y + 18,
            Math.Max(10, area.Width - inset * 2), Math.Max(10, area.Height - 18 - inset));

        double roomPxW = Lerp(inner.Width * 0.32, inner.Width * 0.86, rev.LateSize);
        double roomPxH = Lerp(inner.Height * 0.35, inner.Height * 0.88, rev.EarlySize);
        double depthPx = Lerp(10, Math.Min(46, inner.Width * 0.22), rev.Depth);

        double dx = depthPx * 0.55, dy = -depthPx * 0.34;
        double cx = inner.X + inner.Width / 2 - dx * 0.35;
        double cy = inner.Y + inner.Height / 2 - dy * 0.35;
        var geo = new RoomGeo(cx - roomPxW / 2, cy - roomPxH / 2, cx + roomPxW / 2, cy + roomPxH / 2, dx, dy, depthPx);
        _roomGeo = geo;

        if ((now - _roomLastPulse).TotalSeconds > 2.6)
        { SpawnPulse(rev, geo, null); _roomLastPulse = now; }

        // back face + connectors
        var back = new Rect(geo.X0 + geo.Dx, geo.Y0 + geo.Dy, geo.X1 - geo.X0, geo.Y1 - geo.Y0);
        var wallPen = P(Color.FromArgb(70, 120, 128, 150), 1);
        var connPen = P(Color.FromArgb(40, 120, 128, 150), 1);
        dc.DrawRectangle(null, wallPen, back);
        dc.DrawLine(connPen, new Point(geo.X0, geo.Y0), new Point(back.X, back.Y));
        dc.DrawLine(connPen, new Point(geo.X1, geo.Y0), new Point(back.Right, back.Y));
        dc.DrawLine(connPen, new Point(geo.X1, geo.Y1), new Point(back.Right, back.Bottom));
        dc.DrawLine(connPen, new Point(geo.X0, geo.Y1), new Point(back.X, back.Bottom));

        // receding floor grid — makes Depth legible even before a pulse fires
        var rungPen = P(Color.FromArgb(36, 120, 128, 150), 1);
        const int rungs = 3;
        for (int k = 1; k < rungs; k++)
        {
            double z = (double)k / rungs * geo.DepthPx;
            dc.DrawLine(rungPen, Project(geo, geo.X0, geo.Y1, z), Project(geo, geo.X1, geo.Y1, z));
        }

        // front face
        dc.DrawRectangle(null, P(Color.FromArgb(150, 200, 205, 220), 1.3),
            new Rect(geo.X0, geo.Y0, geo.X1 - geo.X0, geo.Y1 - geo.Y0));

        double diffusionRad = rev.EarlyDiffusion * 0.9;
        double dampNorm   = 1 - Math.Clamp((rev.HighCut - 1000) / 19000.0, 0, 1);
        double dampFactor = 0.10 + dampNorm * 0.55;
        double earlyAmt = (1 - rev.Depth) * (0.35 + 0.65 * rev.EarlySend);
        double lateAmt  = rev.Depth * (0.35 + 0.65 * rev.LateSize);
        double decayS   = Math.Clamp(rev.Decay, 0.2, 10);

        // fog (late reflections)
        for (int i = _fog.Count - 1; i >= 0; i--)
        {
            var f = _fog[i];
            f.Age += dt;
            if (f.Age >= f.MaxAge) { _fog.RemoveAt(i); continue; }
            double a = Math.Clamp((1 - f.Age / f.MaxAge) * f.Strength, 0, 1);
            var gb = new RadialGradientBrush(
                Color.FromArgb((byte)(a * 100), f.Color.R, f.Color.G, f.Color.B),
                Color.FromArgb(0, f.Color.R, f.Color.G, f.Color.B));
            gb.Freeze();
            dc.DrawEllipse(gb, null, f.P, f.Radius, f.Radius);
        }

        // rays
        const double speed = 60; // px/sec
        int maxBounces = 3 + (int)Math.Round(Math.Clamp(lateAmt, 0, 1) * 22);
        for (int i = _rays.Count - 1; i >= 0; i--)
        {
            var r = _rays[i];
            double dist = speed * dt;
            double nx = r.X + r.Vx * dist, ny = r.Y + r.Vy * dist, nz = r.Z + r.Vz * dist;
            bool bounced = false;
            if (nx < geo.X0) { nx = geo.X0; r.Vx *= -1; bounced = true; }
            if (nx > geo.X1) { nx = geo.X1; r.Vx *= -1; bounced = true; }
            if (ny < geo.Y0) { ny = geo.Y0; r.Vy *= -1; bounced = true; }
            if (ny > geo.Y1) { ny = geo.Y1; r.Vy *= -1; bounced = true; }
            if (nz < 0) { nz = 0; r.Vz *= -1; bounced = true; }
            if (nz > geo.DepthPx) { nz = geo.DepthPx; r.Vz *= -1; bounced = true; }

            var p0 = Project(geo, r.X, r.Y, r.Z);
            var p1 = Project(geo, nx, ny, nz);
            double closeness = 1 - (NormZ(geo, r.Z) + NormZ(geo, nz)) / 2;

            Color col = r.Bounces == 0 ? RoomDirect
                : r.Bounces <= 2 ? LerpColor(RoomDirect, RoomEarly, r.Bounces / 2.0)
                : LerpColor(RoomEarly, RoomLate, Math.Min(1, (r.Bounces - 2) / 4.0));

            double segAlpha = Math.Clamp(r.Alpha * (0.35 + closeness * 0.65) * (0.5 + earlyAmt * 0.7), 0, 1);
            double w = (r.Bounces == 0 ? 1.4 : 0.9) * (0.5 + closeness * 0.7);
            if (segAlpha > 0.015)
            {
                var glowPen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(segAlpha * 70), col.R, col.G, col.B)), w + 2.4);
                glowPen.Freeze();
                dc.DrawLine(glowPen, p0, p1);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(segAlpha * 235), col.R, col.G, col.B)), w);
                pen.Freeze();
                dc.DrawLine(pen, p0, p1);
            }

            r.X = nx; r.Y = ny; r.Z = nz;

            if (bounced)
            {
                r.Bounces++;
                r.Vx += (_roomRng.NextDouble() * 2 - 1) * diffusionRad;
                r.Vy += (_roomRng.NextDouble() * 2 - 1) * diffusionRad;
                r.Vz += (_roomRng.NextDouble() * 2 - 1) * diffusionRad;
                double vlen = Math.Sqrt(r.Vx * r.Vx + r.Vy * r.Vy + r.Vz * r.Vz);
                if (vlen < 1e-4) vlen = 1;
                r.Vx /= vlen; r.Vy /= vlen; r.Vz /= vlen;

                r.Alpha *= (1 - dampFactor);

                var fp = Project(geo, r.X, r.Y, r.Z);
                double fscale = 0.5 + (1 - NormZ(geo, r.Z)) * 0.8;
                _flashes.Add(new RoomFlash { P = fp, Age = 0, Max = 0.4, Color = col, Scale = fscale });

                if (r.Bounces >= 2 && lateAmt > 0.02)
                {
                    double radius = Lerp(6, 16, Math.Clamp(lateAmt, 0, 1)) * (0.5 + (1 - NormZ(geo, r.Z)) * 0.7);
                    _fog.Add(new RoomFog
                    {
                        P = fp, Age = 0, MaxAge = decayS * 0.8 + 0.4, Radius = radius,
                        Color = RoomLate, Strength = Math.Clamp((0.25 + lateAmt * 0.5) * r.Alpha, 0, 1)
                    });
                    if (_fog.Count > 140) _fog.RemoveRange(0, _fog.Count - 140);
                }
            }

            r.Age += dt;
            r.Alpha -= dt * (0.06 + dampNorm * 0.10);
            if (r.Alpha <= 0.03 || r.Bounces > maxBounces || r.Age > decayS * 1.6 + 1)
                _rays.RemoveAt(i);
        }

        // wall-hit flashes
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var f = _flashes[i];
            f.Age += dt;
            double p = f.Age / f.Max;
            if (p >= 1) { _flashes.RemoveAt(i); continue; }
            double a = Math.Clamp((1 - p) * (0.4 + f.Scale * 0.6), 0, 1);
            var b = new SolidColorBrush(Color.FromArgb((byte)(a * 255), f.Color.R, f.Color.G, f.Color.B));
            b.Freeze();
            double rad = (2 + p * 7) * f.Scale;
            dc.DrawEllipse(b, null, f.P, rad, rad);
        }

        // sources
        foreach (var s in SourcePositions(geo, rev))
        {
            var p = Project(geo, s.X, s.Y, s.Z);
            double sc = 0.55 + (1 - NormZ(geo, s.Z)) * 0.6;
            var glow = new RadialGradientBrush(
                Color.FromArgb(200, RoomDirect.R, RoomDirect.G, RoomDirect.B),
                Color.FromArgb(0, RoomDirect.R, RoomDirect.G, RoomDirect.B));
            glow.Freeze();
            dc.DrawEllipse(glow, null, p, 10 * sc, 10 * sc);
            dc.DrawEllipse(B(RoomDirect), null, p, 2.2 * sc, 2.2 * sc);
        }
    }

    private static double NormZ(RoomGeo geo, double z)
        => geo.DepthPx > 0 ? Math.Clamp(z / geo.DepthPx, 0, 1) : 0;

    private static Point Project(RoomGeo geo, double x, double y, double z)
    {
        double t = NormZ(geo, z);
        return new Point(x + t * geo.Dx, y + t * geo.Dy);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    private static (double X, double Y, double Z)[] SourcePositions(RoomGeo geo, ReverbEffect rev)
    {
        double cx = (geo.X0 + geo.X1) / 2, cy = (geo.Y0 + geo.Y1) / 2, cz = geo.DepthPx * 0.5;
        double stereo = Math.Clamp(1 - rev.EarlyCross, 0, 1);
        double spread = stereo * (geo.X1 - geo.X0) * 0.30;
        return stereo < 0.05
            ? [(cx, cy, cz)]
            : [(cx - spread / 2, cy, cz), (cx + spread / 2, cy, cz)];
    }

    private void SpawnPulse(ReverbEffect rev, RoomGeo geo, Point? clickOverride)
    {
        (double X, double Y, double Z)[] srcs = clickOverride.HasValue
            ? [(clickOverride.Value.X, clickOverride.Value.Y, geo.DepthPx * 0.5)]
            : SourcePositions(geo, rev);

        int perSource = (int)Math.Round(Lerp(5, 20, rev.EarlySend));

        foreach (var s in srcs)
        {
            for (int i = 0; i < perSource; i++)
            {
                double u = _roomRng.NextDouble() * 2 - 1;
                double theta = _roomRng.NextDouble() * Math.PI * 2;
                double rr = Math.Sqrt(Math.Max(0, 1 - u * u));
                _rays.Add(new RoomRay
                {
                    X = s.X, Y = s.Y, Z = s.Z,
                    Vx = rr * Math.Cos(theta), Vy = rr * Math.Sin(theta) * 0.9, Vz = u,
                    Bounces = 0, Alpha = 1, Age = 0
                });
            }
            _flashes.Add(new RoomFlash { P = Project(geo, s.X, s.Y, s.Z), Age = 0, Max = 0.4, Color = RoomDirect, Scale = 1 });
        }
        if (_rays.Count > 400) _rays.RemoveRange(0, _rays.Count - 400);
    }

    // ── Early/Late tab view ─────────────────────────────────────────
    private void DrawReverbTabs(DrawingContext dc, Rect area, ReverbEffect rev)
    {
        var bg = new LinearGradientBrush(Color.FromRgb(0x16, 0x1A, 0x22),
                                          Color.FromRgb(0x11, 0x14, 0x1A), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 0.8), area, 4, 4);

        const double tabH = 22;
        double tw = area.Width / 2;
        _tabEarlyR = new Rect(area.X + 4, area.Y + 4, tw - 6, tabH);
        _tabLateR  = new Rect(area.X + tw + 2, area.Y + 4, tw - 6, tabH);
        DrawTab(dc, _tabEarlyR, "EARLY REFLECTIONS", CEarlyCol, _activeReverbTab == 0);
        DrawTab(dc, _tabLateR,  "LATE REVERBERATION", CLateCol, _activeReverbTab == 1);

        var content = new Rect(area.X + 8, area.Y + tabH + 10, area.Width - 16, area.Height - tabH - 16);

        int baseEK  = GK.Length;
        int baseEQE = baseEK + EK.Length;
        int baseLK  = baseEQE + EQE.Length;

        if (_activeReverbTab == 0)
        {
            // 8 knobs: DIFF CROSS SEND MOD-R MOD-D · HI-CUT HI-SHELF LO-SHELF
            DrawKnobGrid(dc, content, EarlyTab, rev, CEarlyCol, 4, 0, baseEK);
        }
        else
        {
            // 7 knobs, same 4-column grid as the Early tab: DECAY CROSS BASS× BXOVER · HI-CUT HI-SHELF LO-SHELF
            DrawKnobGrid(dc, content, LateTab, rev, CLateCol, 4, 0, baseLK);
        }
    }

    private static void DrawTab(DrawingContext dc, Rect r, string label, Color accent, bool active)
    {
        if (active)
        {
            var bg = new LinearGradientBrush(Color.FromArgb(70, accent.R, accent.G, accent.B),
                                              Color.FromArgb(25, accent.R, accent.G, accent.B), 90);
            bg.Freeze();
            dc.DrawRoundedRectangle(bg, P(accent, 1), r, 3, 3);
        }
        else
        {
            dc.DrawRoundedRectangle(BSurface, PBorder, r, 3, 3);
        }
        var txt = Txt(label, 7.5, active ? B(accent) : BTextSec,
            active ? FontWeights.Bold : FontWeights.Normal, TFCond);
        dc.DrawText(txt, Ctr(r, txt));
    }

    // Lays `items` out in a `cols`-wide grid inside `area`, writing hit-test
    // data into `_kc`/`_kr` starting at `baseIndex` (must match the item's
    // position in `All`, see the EarlyTab/LateSmall comment above).
    private void DrawKnobGrid(DrawingContext dc, Rect area, KD[] items, ReverbEffect rev, Color accent,
        int cols, double topInset, int baseIndex)
    {
        if (items.Length == 0 || area.Width <= 0 || area.Height <= topInset) return;
        int rows = (int)Math.Ceiling(items.Length / (double)cols);
        double cellW = area.Width / cols;
        double cellH = (area.Height - topInset) / Math.Max(1, rows);
        double r = Math.Clamp(Math.Min(cellW, cellH) * 0.30, 9, 20);
        for (int i = 0; i < items.Length; i++)
        {
            int col = i % cols, row = i / cols;
            double cx = area.X + cellW * col + cellW / 2;
            double cy = area.Y + topInset + cellH * row + cellH / 2;
            _kc[baseIndex + i] = new Point(cx, cy); _kr[baseIndex + i] = r;
            var k = items[i];
            DrawKnob(dc, cx, cy, r, Norm(k, k.Get(rev)), accent, k.L, FmtK(k, k.Get(rev)));
        }
    }

    // ── Mouse ──────────────────────────────────────────────────────────────
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var rev = Effect; if (rev == null) return;
        var p = e.GetPosition(this);

        if (_roomRect.Contains(p))
        { SpawnPulse(rev, _roomGeo, p); e.Handled = true; return; }

        if (_tabEarlyR.Contains(p)) { _activeReverbTab = 0; InvalidateVisual(); e.Handled = true; return; }
        if (_tabLateR.Contains(p))  { _activeReverbTab = 1; InvalidateVisual(); e.Handled = true; return; }

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
               Math.Max(double.IsInfinity(av.Height) ? 580 + ROOM_H + GAP : av.Height, 490 + ROOM_H + GAP));
}
