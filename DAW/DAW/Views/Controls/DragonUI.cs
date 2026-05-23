using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DAW.Views.Controls;

/// <summary>
/// Shared drawing primitives — Red Dragon Design System.
///
/// GRID CONSTANTS (all plugins use these):
///   HDR_H  = 36    header bar height
///   ROW_H  = 72    standard knob row height  (label 12 + gap 4 + knob 40 + gap 4 + value 12)
///   BTN_H  = 26    button height
///   BTN_W  = 52    standard button width
///   PAD    = 10    outer padding
///   GAP    = 8     inter-element gap
///   KR     = 20    standard knob radius
///   KR_LG  = 28    large knob radius (featured parameter)
///
/// All plugins: fixed window size from PluginWindow, fixed grid.
/// Labels centred above knob, value below — always consistent.
/// </summary>
public static class DragonUI
{
    // ── Grid constants ────────────────────────────────────────────────────
    public const double HDR_H  = 36;
    public const double ROW_H  = 72;   // full knob row: lbl + knob + val
    public const double BTN_H  = 26;
    public const double BTN_W  = 52;
    public const double PAD    = 10;
    public const double GAP    = 8;
    public const double KR     = 20;   // standard knob radius
    public const double KR_LG  = 28;   // large/featured knob radius
    public const double LROW_H = 88;   // large knob row height

    // ── Palette ───────────────────────────────────────────────────────────
    public static readonly Color CBackground = Color.FromRgb(0x11, 0x13, 0x16);
    public static readonly Color CPanel      = Color.FromRgb(0x1A, 0x1D, 0x23);
    public static readonly Color CSurface    = Color.FromRgb(0x22, 0x27, 0x2F);
    public static readonly Color CBorder     = Color.FromRgb(0x2D, 0x33, 0x42);
    public static readonly Color CRed        = Color.FromRgb(0xC4, 0x1E, 0x3A);
    public static readonly Color CRedLight   = Color.FromRgb(0xE6, 0x39, 0x46);
    public static readonly Color CGold       = Color.FromRgb(0xD4, 0xA0, 0x17);
    public static readonly Color CGoldLight  = Color.FromRgb(0xF0, 0xC0, 0x40);
    public static readonly Color CTextPri    = Color.FromRgb(0xE8, 0xE8, 0xE8);
    public static readonly Color CTextSec    = Color.FromRgb(0x8B, 0x94, 0x9E);
    public static readonly Color CTextDim    = Color.FromRgb(0x48, 0x4F, 0x58);
    public static readonly Color CGreen      = Color.FromRgb(0x2E, 0xCC, 0x40);
    public static readonly Color COrange     = Color.FromRgb(0xFF, 0x8C, 0x00);

    // Pre-frozen brushes
    public static readonly Brush BBg      = B(CBackground);
    public static readonly Brush BPanel   = B(CPanel);
    public static readonly Brush BSurface = B(CSurface);
    public static readonly Brush BRed     = B(CRed);
    public static readonly Brush BRedL    = B(CRedLight);
    public static readonly Brush BGold    = B(CGold);
    public static readonly Brush BTextPri = B(CTextPri);
    public static readonly Brush BTextSec = B(CTextSec);
    public static readonly Brush BTextDim = B(CTextDim);
    public static readonly Brush BGreen   = B(CGreen);
    public static readonly Brush BOrange  = B(COrange);

    // Pre-frozen pens
    public static readonly Pen PBorder    = P(CBorder, 1);
    public static readonly Pen PBorderRed = P(CRed, 1);
    public static readonly Pen PArcBg     = P(Color.FromArgb(55, 0x44, 0x4C, 0x66), 2.8);

    // Typefaces
    public static readonly Typeface TFSans = new("Segoe UI");
    public static readonly Typeface TFMono = new("Consolas");
    public static readonly Typeface TFCond = new("Arial Narrow");

    // ── Factories ─────────────────────────────────────────────────────────
    public static Brush B(Color c) { var x = new SolidColorBrush(c); x.Freeze(); return x; }
    public static Pen   P(Color c, double t) { var x = new Pen(new SolidColorBrush(c), t); x.Freeze(); return x; }

    public static FormattedText Txt(string text, double size, Brush brush,
        FontWeight? weight = null, Typeface? tf = null)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf ?? TFSans, size, brush, 1.0);
        if (weight.HasValue) ft.SetFontWeight(weight.Value);
        return ft;
    }

    // ── Layout helpers ────────────────────────────────────────────────────
    /// <summary>
    /// Returns evenly spaced centre X coordinates for `count` columns
    /// within [x .. x+width].
    /// </summary>
    public static double[] Columns(double x, double width, int count)
    {
        double sp = width / count;
        var result = new double[count];
        for (int i = 0; i < count; i++)
            result[i] = x + sp * i + sp / 2;
        return result;
    }

    /// <summary>
    /// Centre Y of a knob in a row that starts at `rowTop`.
    /// rowTop is the top of the row (label starts here),
    /// the knob centre sits at rowTop + LBL_H + GAP + r.
    /// </summary>
    public const double LBL_H = 12;
    public const double VAL_H = 12;
    public static double KnobCY(double rowTop, double r) => rowTop + LBL_H + GAP + r;

    // ── Standard knob ─────────────────────────────────────────────────────
    /// <summary>
    /// Draws a knob at (cx, cy) with radius r.
    /// Label is centred directly above, value directly below — fixed spacing.
    /// </summary>
    public static void DrawKnob(DrawingContext dc,
        double cx, double cy, double r,
        double norm, Color accent,
        string label, string value,
        bool glow = false)
    {
        norm = Math.Clamp(norm, 0, 1);
        const double aMin = 5.0 * Math.PI / 4.0;  // 225°
        const double aMax = -Math.PI / 4.0;         // 315°
        double angle = aMin + norm * (aMax - aMin);

        // Optional glow
        if (glow)
        {
            var gb = new SolidColorBrush(Color.FromArgb(18, accent.R, accent.G, accent.B));
            gb.Freeze();
            dc.DrawEllipse(gb, null, new Point(cx, cy), r + 10, r + 10);
        }

        // Arc track + filled arc
        DrawArc(dc, cx, cy, r + 5, aMin, aMax, PArcBg);
        if (norm > 0.004)
            DrawArc(dc, cx, cy, r + 5, aMin, angle, P(accent, 2.8));

        // Tick marks (5 major, 10 minor)
        for (int t = 0; t <= 10; t++)
        {
            double ta = aMin + (double)t / 10 * (aMax - aMin);
            bool   mj = t % 5 == 0;
            dc.DrawLine(mj ? P(CTextDim, 0.9) : P(Color.FromArgb(35, 0x55, 0x5E, 0x72), 0.6),
                new Point(cx + (r + 8) * Math.Cos(ta), cy - (r + 8) * Math.Sin(ta)),
                new Point(cx + (r + (mj ? 13 : 10)) * Math.Cos(ta), cy - (r + (mj ? 13 : 10)) * Math.Sin(ta)));
        }

        // Rim + face
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

        // Gold pointer dot
        dc.DrawEllipse(B(CGold), null,
            new Point(cx + r * 0.74 * Math.Cos(angle), cy - r * 0.74 * Math.Sin(angle)),
            r * 0.13, r * 0.13);

        // Label (above, centred, fixed distance)
        double lblY = cy - r - 5 - LBL_H;
        var lbl = Txt(label, 8.5, BTextSec, FontWeights.SemiBold, TFCond);
        dc.DrawText(lbl, new Point(cx - lbl.Width / 2, lblY + (LBL_H - lbl.Height) / 2));

        // Value (below, centred, fixed distance)
        double valY = cy + r + 5;
        var val = Txt(value, 8.5, B(accent), FontWeights.SemiBold, TFMono);
        dc.DrawText(val, new Point(cx - val.Width / 2, valY));
    }

    // ── Section panel ─────────────────────────────────────────────────────
    /// <summary>Draws a labelled section panel with a coloured left-edge accent.</summary>
    public static void DrawSection(DrawingContext dc, Rect r, string label, Color accent)
    {
        dc.DrawRoundedRectangle(BPanel, PBorder, r, 4, 4);
        dc.DrawRectangle(B(Color.FromArgb(200, accent.R, accent.G, accent.B)),
            null, new Rect(r.X, r.Y, 2.5, r.Height));
        var lbl = Txt(label.ToUpper(), 8, B(accent), FontWeights.Bold, TFCond);
        dc.DrawText(lbl, new Point(r.X + 10, r.Y + (HDR_H * 0.55 - lbl.Height) / 2));
    }

    // ── Header ────────────────────────────────────────────────────────────
    public static void DrawHeader(DrawingContext dc, Rect r,
        string name, string subtitle, string? rightText = null)
    {
        // Dark gradient with red-tinted left side
        var g = new LinearGradientBrush();
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x0A, 0x14), 0.0));
        g.GradientStops.Add(new GradientStop(CPanel, 0.30));
        g.GradientStops.Add(new GradientStop(CPanel, 1.0));
        g.StartPoint = new Point(0, 0); g.EndPoint = new Point(1, 0);
        g.Freeze();
        dc.DrawRoundedRectangle(g, PBorder, r, 4, 4);

        // Red left stripe
        dc.DrawRectangle(BRed, null, new Rect(r.X, r.Y, 3, r.Height));

        double tx = r.X + 14;
        var sub = Txt(subtitle.ToUpper(), 7, BTextDim, tf: TFCond);
        var tit = Txt(name.ToUpper(), 12, BTextPri, FontWeights.Bold, TFCond);
        dc.DrawText(sub, new Point(tx, r.Y + r.Height / 2 - sub.Height - 1));
        dc.DrawText(tit, new Point(tx, r.Y + r.Height / 2));

        if (rightText != null)
        {
            var rt = Txt(rightText, 9, B(CGold), FontWeights.Bold, TFMono);
            dc.DrawText(rt, new Point(r.Right - rt.Width - 12, r.Y + (r.Height - rt.Height) / 2));
        }
    }

    // ── Button ────────────────────────────────────────────────────────────
    /// <summary>Standard pill button. Active = red gradient + LED.</summary>
    public static void DrawButton(DrawingContext dc, Rect r, string label, bool active,
        double fontSize = 9)
    {
        Brush bg;
        if (active)
        {
            var bg2 = new LinearGradientBrush(CRedLight, CRed, 90);
            bg2.Freeze();
            bg = bg2;
        }
        else bg = BSurface;

        dc.DrawRoundedRectangle(bg, active ? PBorderRed : PBorder, r, 3, 3);

        // LED
        var lc = new Point(r.X + 9, r.Y + r.Height / 2);
        dc.DrawEllipse(B(active
                ? Color.FromRgb(0xFF, 0x70, 0x70)
                : Color.FromRgb(0x30, 0x18, 0x18)),
            null, lc, 2.5, 2.5);
        if (active)
            dc.DrawEllipse(B(Color.FromArgb(45, 0xFF, 0x90, 0x90)), null, lc, 5, 5);

        var txt = Txt(label, fontSize, active ? BTextPri : BTextSec,
            active ? FontWeights.Bold : FontWeights.Normal, TFCond);
        dc.DrawText(txt, new Point(r.X + 18, r.Y + (r.Height - txt.Height) / 2));
    }

    // ── Toggle button (no LED, centred text) ─────────────────────────────
    public static void DrawToggle(DrawingContext dc, Rect r, string label, bool active,
        double fontSize = 9)
    {
        Brush bg;
        if (active)
        {
            var bg2 = new LinearGradientBrush(CRedLight, CRed, 90);
            bg2.Freeze();
            bg = bg2;
        }
        else bg = BSurface;

        dc.DrawRoundedRectangle(bg, active ? PBorderRed : PBorder, r, 3, 3);
        var txt = Txt(label, fontSize, active ? BTextPri : BTextSec,
            active ? FontWeights.Bold : FontWeights.Normal, TFCond);
        dc.DrawText(txt, new Point(r.X + (r.Width - txt.Width) / 2, r.Y + (r.Height - txt.Height) / 2));
    }

    // ── Horizontal divider ────────────────────────────────────────────────
    public static void DrawDivider(DrawingContext dc, double x, double y, double w,
        string? label = null)
    {
        dc.DrawLine(PBorder, new Point(x, y), new Point(w, y));
        if (label != null)
        {
            var lbl = Txt(label.ToUpper(), 7.5, BTextDim, FontWeights.SemiBold, TFCond);
            double lx = x + (w - x - lbl.Width) / 2;
            // erase line under label
            dc.DrawRectangle(BBg, null, new Rect(lx - 4, y - 4, lbl.Width + 8, 8));
            dc.DrawText(lbl, new Point(lx, y - lbl.Height / 2));
        }
    }

    // ── Meter bar ─────────────────────────────────────────────────────────
    public static void DrawMeterBar(DrawingContext dc, Rect r, double norm,
        bool rtl = false)
    {
        dc.DrawRoundedRectangle(BSurface, PBorder, r, 2, 2);
        if (norm < 0.002) return;
        norm = Math.Clamp(norm, 0, 1);
        Color c = norm < 0.80 ? CGreen : norm < 0.95 ? COrange : CRed;
        double bw = (r.Width - 4) * norm;
        double bx = rtl ? r.Right - 2 - bw : r.X + 2;
        dc.DrawRoundedRectangle(B(c), null, new Rect(bx, r.Y + 2, bw, r.Height - 4), 1, 1);
    }

    // ── Arc ───────────────────────────────────────────────────────────────
    public static void DrawArc(DrawingContext dc, double cx, double cy, double r,
        double from, double to, Pen pen, int steps = 28)
    {
        for (int s = 0; s < steps; s++)
        {
            double a1 = from + (to - from) * s       / steps;
            double a2 = from + (to - from) * (s + 1) / steps;
            dc.DrawLine(pen,
                new Point(cx + r * Math.Cos(a1), cy - r * Math.Sin(a1)),
                new Point(cx + r * Math.Cos(a2), cy - r * Math.Sin(a2)));
        }
    }

    // ── Corner screw ─────────────────────────────────────────────────────
    public static void DrawScrew(DrawingContext dc, double cx, double cy)
    {
        dc.DrawEllipse(BSurface, P(CBorder, 0.8), new Point(cx, cy), 4, 4);
        dc.DrawLine(P(Color.FromArgb(55, 0xCC, 0xCC, 0xCC), 0.7),
            new Point(cx - 2.3, cy - 2.3), new Point(cx + 2.3, cy + 2.3));
    }
}
