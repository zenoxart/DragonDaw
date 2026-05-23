using System.Windows;
using System.Windows.Media;
using DAW.Audio.Effects.DragonParticle;
using DAW.ViewModels;
using static DAW.Views.Controls.DragonUI;

namespace DAW.Views.Controls;

// ═══════════════════════════════════════════════════════════════════════════════
//  VIEW LAYER — DragonParticle DAG Signal Flow Visualiser
//
//  Renders the parallel DSP graph that the DragonParticlePresenter manages.
//  Responsibilities:
//   • Display every DSP node as a labelled box (name + self latency + total latency)
//   • Draw directed edges between nodes (signal flow arrows)
//   • Colour-code the three parallel chains and latency-compensation delay lines
//   • Show a legend
//
//  Constraints (MVP):
//   • No DSP logic — reads only NodeLatencyInfo records from MasterViewModel
//   • No direct access to Model or Presenter
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class DragonParticleGraphView : FrameworkElement
{
    // ── Chain colours ─────────────────────────────────────────────────────────
    private static readonly Color CChainA  = Color.FromRgb(0xC4, 0x1E, 0x3A); // Dragon red  — MV2 → PuigChild → Scheps73
    private static readonly Color CChainB  = Color.FromRgb(0x3B, 0x82, 0xF6); // Blue        — API-550B
    private static readonly Color CChainC  = Color.FromRgb(0x00, 0xC5, 0xCD); // Teal        — NLS Buss
    private static readonly Color CCompDly = Color.FromRgb(0x60, 0x6E, 0x84); // Muted steel — latency-compensation paths
    private static readonly Color CSumNode = Color.FromRgb(0xD4, 0xA0, 0x17); // Gold        — Magic Mids output
    private static readonly Color CSource  = Color.FromRgb(0xD8, 0xE0, 0xF0); // Soft white  — Upper Range input

    // ── Node geometry ─────────────────────────────────────────────────────────
    private const double NodeW = 92;
    private const double NodeH = 46;
    private const double HalfW = NodeW / 2;
    private const double HalfH = NodeH / 2;

    // ── Dependency property ───────────────────────────────────────────────────
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(MasterViewModel),
            typeof(DragonParticleGraphView),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender, OnVmChanged));

    public MasterViewModel? ViewModel
    {
        get => (MasterViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnVmChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (DragonParticleGraphView)d;
        if (e.OldValue is MasterViewModel o) o.PropertyChanged -= v.OnProp;
        if (e.NewValue is MasterViewModel n) n.PropertyChanged += v.OnProp;
        v.InvalidateVisual();
    }

    private void OnProp(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.InvokeAsync(InvalidateVisual, System.Windows.Threading.DispatcherPriority.Render);

    // ══════════════════════════════════════════════════════════════════════════
    //  RENDER
    // ══════════════════════════════════════════════════════════════════════════

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, H = ActualHeight;
        if (W < 100 || H < 60) return;

        var latencies = ViewModel?.DagLatencies;

        // ── Background ────────────────────────────────────────────────────────
        var bg = new LinearGradientBrush(
            Color.FromRgb(0x0B, 0x10, 0x18),
            Color.FromRgb(0x07, 0x0B, 0x12), 90);
        bg.Freeze();
        dc.DrawRoundedRectangle(bg, P(CBorder, 0.8), new Rect(0, 0, W, H), 6, 6);

        // ── Section header ────────────────────────────────────────────────────
        var hdrTxt = Txt(
            "◈  DAG SIGNAL FLOW  ◈  Parallel Processing Graph with Latency-Aware Summing",
            7.5, BTextDim, FontWeights.Bold, TFCond);
        dc.DrawText(hdrTxt, new Point(W / 2 - hdrTxt.Width / 2, 6));
        dc.DrawLine(P(CBorder, 0.8), new Point(8, 22), new Point(W - 8, 22));

        // ── Node grid (4 rows × 3 columns) ───────────────────────────────────
        //
        //   Row 0        [Upper Range]
        //               /      |      \
        //   Row 1  [MV2]   [API-550B]  [NLS Buss]
        //            |          |           |
        //   Row 2 [PuigChild] [CompB▪▪]  [CompC▪▪]
        //            |          |    ╲       |    ╲
        //   Row 3 [Scheps73] ─→─→─→─→ [Magic Mids]
        //
        double[] colX = { W * 0.18, W * 0.50, W * 0.82 };
        double graphY = 28, graphH = H - graphY - 28 /* legend */;
        double[] rowY =
        {
            graphY + graphH * 0.09,  // Row 0 — Upper Range (input)
            graphY + graphH * 0.35,  // Row 1 — MV2, API-550B, NLS Buss
            graphY + graphH * 0.63,  // Row 2 — PuigChild, CompDelayB, CompDelayC
            graphY + graphH * 0.88,  // Row 3 — Scheps73, Magic Mids
        };

        NodeLatencyInfo? Get(string id) =>
            latencies?.FirstOrDefault(n => n.NodeId == id);

        // ── Edges (drawn behind nodes) ────────────────────────────────────────

        // Input split: Upper Range → all three chains
        DrawEdge(dc, colX[1], rowY[0] + HalfH, colX[0], rowY[1] - HalfH, CChainA);
        DrawEdge(dc, colX[1], rowY[0] + HalfH, colX[1], rowY[1] - HalfH, CChainB);
        DrawEdge(dc, colX[1], rowY[0] + HalfH, colX[2], rowY[1] - HalfH, CChainC);

        // Chain A: MV2 → PuigChild → Scheps73 → Magic Mids (horizontal)
        DrawEdge(dc, colX[0], rowY[1] + HalfH, colX[0], rowY[2] - HalfH, CChainA);
        DrawEdge(dc, colX[0], rowY[2] + HalfH, colX[0], rowY[3] - HalfH, CChainA);
        DrawEdge(dc, colX[0] + HalfW, rowY[3],  colX[1] - HalfW, rowY[3], CChainA);

        // Chain B: API-550B → CompDelayB → Magic Mids (straight vertical)
        DrawEdge(dc, colX[1], rowY[1] + HalfH, colX[1], rowY[2] - HalfH, CChainB);
        DrawEdge(dc, colX[1], rowY[2] + HalfH, colX[1], rowY[3] - HalfH, CCompDly, dashed: true);

        // Chain C: NLS Buss → CompDelayC → Magic Mids (converging diagonal)
        DrawEdge(dc, colX[2], rowY[1] + HalfH, colX[2], rowY[2] - HalfH, CChainC);
        DrawEdge(dc, colX[2], rowY[2] + HalfH, colX[1] + HalfW, rowY[3] - HalfH, CCompDly, dashed: true);

        // ── Nodes ─────────────────────────────────────────────────────────────
        DrawNode(dc, colX[1], rowY[0], "UPPER RANGE",   Get("upper_range"), CSource,  isSource: true);
        DrawNode(dc, colX[0], rowY[1], "MV2",            Get("mv2"),         CChainA);
        DrawNode(dc, colX[1], rowY[1], "API-550B",       Get("api550b"),     CChainB);
        DrawNode(dc, colX[2], rowY[1], "NLS BUSS",       Get("nls"),         CChainC);
        DrawNode(dc, colX[0], rowY[2], "PUIGCHILD 670",  Get("puig670"),     CChainA);
        DrawNode(dc, colX[1], rowY[2], "COMP DELAY B",   Get("comp_b"),      CCompDly, isComp: true);
        DrawNode(dc, colX[2], rowY[2], "COMP DELAY C",   Get("comp_c"),      CCompDly, isComp: true);
        DrawNode(dc, colX[0], rowY[3], "SCHEPS 73",      Get("scheps73"),    CChainA);
        DrawNode(dc, colX[1], rowY[3], "MAGIC MIDS",     Get("magic_mids"),  CSumNode, isSink: true);

        // ── Legend ────────────────────────────────────────────────────────────
        DrawLegend(dc, W, H);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  DRAWING PRIMITIVES
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Renders a single DSP node box with name and latency badges.</summary>
    private static void DrawNode(DrawingContext dc, double cx, double cy, string label,
        NodeLatencyInfo? info, Color accent,
        bool isSource = false, bool isSink = false, bool isComp = false)
    {
        var rect = new Rect(cx - HalfW, cy - HalfH, NodeW, NodeH);

        // Background fill
        byte alpha = isSource || isSink ? (byte)70 : isComp ? (byte)28 : (byte)45;
        var bgBrush = new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B));
        bgBrush.Freeze();

        // Border pen
        byte borderAlpha = isComp ? (byte)90 : (byte)210;
        double thickness = isSource || isSink ? 1.5 : isComp ? 0.7 : 1.1;
        var borderPen = new Pen(
            new SolidColorBrush(Color.FromArgb(borderAlpha, accent.R, accent.G, accent.B)),
            thickness);
        if (isComp) borderPen.DashStyle = DashStyles.Dot;
        borderPen.Freeze();

        dc.DrawRoundedRectangle(bgBrush, borderPen, rect, 5, 5);

        // Glow ring for source/sink
        if (isSource || isSink)
        {
            var glowPen = new Pen(
                new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)), 6);
            glowPen.Freeze();
            dc.DrawRoundedRectangle(null, glowPen, new Rect(cx - HalfW - 3, cy - HalfH - 3, NodeW + 6, NodeH + 6), 7, 7);
        }

        // Label
        Brush labelBrush = isComp
            ? B(Color.FromArgb(130, accent.R, accent.G, accent.B))
            : B(accent);
        var nameT = Txt(label, 7, labelBrush, FontWeights.Bold, TFCond);
        double textX = cx - Math.Min(nameT.Width, NodeW - 8) / 2;
        dc.DrawText(nameT, new Point(textX, cy - HalfH + 5));

        if (info != null)
        {
            // self latency
            var selfT = Txt($"self {info.SelfLatency} smp", 6.5, BTextDim, tf: TFMono);
            dc.DrawText(selfT, new Point(cx - HalfW + 5, cy + 2));
            // total latency
            var totT  = Txt($"total {info.TotalLatency} smp", 6.5, BTextSec, tf: TFMono);
            dc.DrawText(totT,  new Point(cx - HalfW + 5, cy + 12));
        }
    }

    /// <summary>Draws a directed edge with an arrowhead at the destination.</summary>
    private static void DrawEdge(DrawingContext dc,
        double x1, double y1, double x2, double y2,
        Color c, bool dashed = false)
    {
        byte alpha = dashed ? (byte)90 : (byte)150;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        var pen = new Pen(brush, dashed ? 1.0 : 1.4);
        if (dashed) pen.DashStyle = DashStyles.Dash;
        pen.Freeze();

        dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));

        // Arrowhead
        double dx = x2 - x1, dy = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        double ux = dx / len, uy = dy / len;
        const double aLen = 7, aWid = 3.5;

        var arrowBrush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        arrowBrush.Freeze();

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(x2, y2), true, true);
            ctx.LineTo(new Point(x2 - aLen * ux + aWid * (-uy), y2 - aLen * uy + aWid * ux), true, false);
            ctx.LineTo(new Point(x2 - aLen * ux - aWid * (-uy), y2 - aLen * uy - aWid * ux), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(arrowBrush, null, geo);
    }

    /// <summary>Renders a horizontal legend strip at the bottom of the control.</summary>
    private static void DrawLegend(DrawingContext dc, double W, double H)
    {
        double y = H - 14;

        (string label, Color c, bool dashed)[] items =
        [
            ("Chain A: MV2 → PuigChild 670 → Scheps 73", CChainA,  false),
            ("Chain B: API-550B",                          CChainB,  false),
            ("Chain C: NLS Buss",                          CChainC,  false),
            ("Latency Compensation",                       CCompDly, true),
        ];

        double x = 12;
        foreach (var (label, c, dashed) in items)
        {
            var lineBrush = new SolidColorBrush(Color.FromArgb(160, c.R, c.G, c.B));
            lineBrush.Freeze();
            var linePen = new Pen(lineBrush, 1.5);
            if (dashed) linePen.DashStyle = DashStyles.Dash;
            linePen.Freeze();

            dc.DrawLine(linePen, new Point(x, y), new Point(x + 18, y));

            var lbl = Txt(label, 6.5, BTextDim, tf: TFCond);
            dc.DrawText(lbl, new Point(x + 20, y - lbl.Height / 2));
            x += 20 + lbl.Width + 14;

            if (x > W - 80) break; // avoid overflow on narrow windows
        }
    }
}
