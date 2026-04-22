using System.Windows.Media;
using DAW.Services;

namespace DAW.Plugins;

/// <summary>
/// Provides theme-aware colors for plugin windows and controls.
/// Detects whether the current theme is light or dark and returns appropriate palette.
/// </summary>
public static class PluginTheme
{
    public static bool IsLight => ThemeService.Instance.CurrentTheme == "SnowWhite";

    // ── Window chrome ─────────────────────────────────────────────────────

    public static Color WindowBg       => IsLight ? C(0xF0, 0xF0, 0xF4) : C(0x16, 0x1B, 0x22);
    public static Color WindowBorder   => IsLight ? C(0x88, 0xAA, 0xCC) : C(0x26, 0x61, 0x9C);
    public static Color TitleBarBg     => IsLight ? C(0x3B, 0x82, 0xC8) : C(0x1A, 0x45, 0x70);
    public static Color TitleText      => IsLight ? C(0xFF, 0xFF, 0xFF) : C(0xFF, 0xFF, 0xFF);
    public static Color TitleAccent    => IsLight ? C(0xC0, 0xDD, 0xF8) : C(0x5B, 0xA4, 0xE6);

    // ── Preset bar ────────────────────────────────────────────────────────

    public static Color PresetBarBg    => IsLight ? C(0xE8, 0xE8, 0xEE) : C(0x12, 0x17, 0x1E);
    public static Color PresetBarBorder=> IsLight ? C(0xD0, 0xD0, 0xD8) : C(0x21, 0x26, 0x2D);
    public static Color PresetLabel    => IsLight ? C(0x70, 0x78, 0x88) : C(0x60, 0x68, 0x78);

    // ── Content area ──────────────────────────────────────────────────────

    public static Color ControlBg      => IsLight ? C(0xE0, 0xE0, 0xE8) : C(0x21, 0x26, 0x2D);
    public static Color SurfaceBg      => IsLight ? C(0xEA, 0xEA, 0xF0) : C(0x0A, 0x0E, 0x14);
    public static Color TextPrimary    => IsLight ? C(0x1A, 0x1A, 0x2E) : C(0xCC, 0xCC, 0xCC);
    public static Color TextSecondary  => IsLight ? C(0x5A, 0x5A, 0x6E) : C(0x88, 0x88, 0x88);
    public static Color TextAccent     => IsLight ? C(0x1A, 0x60, 0xB0) : C(0x5B, 0xA4, 0xE6);
    public static Color TextHint       => IsLight ? CA(120, 0x60, 0x70, 0x88) : CA(100, 0x88, 0x99, 0xAA);
    public static Color Border         => IsLight ? C(0xC0, 0xC0, 0xCC) : C(0x30, 0x36, 0x3D);
    public static Color Separator      => IsLight ? C(0xC8, 0xC8, 0xD0) : C(0x30, 0x36, 0x3D);

    // ── Buttons ───────────────────────────────────────────────────────────

    public static Color BtnBg          => IsLight ? C(0xDA, 0xDA, 0xE2) : C(0x21, 0x26, 0x2D);
    public static Color BtnFg          => IsLight ? C(0x44, 0x44, 0x55) : C(0xAA, 0xAA, 0xAA);
    public static Color BtnBorder      => IsLight ? C(0xBB, 0xBB, 0xC8) : C(0x30, 0x36, 0x3D);
    public static Color BtnHover       => IsLight ? C(0xCC, 0xCC, 0xD8) : C(0x30, 0x36, 0x3D);

    // ── Combo box ─────────────────────────────────────────────────────────

    public static Color ComboBg        => IsLight ? C(0xF4, 0xF4, 0xF8) : C(0x1A, 0x1F, 0x26);
    public static Color ComboFg        => IsLight ? C(0x1A, 0x1A, 0x2E) : C(0xC9, 0xD1, 0xD9);
    public static Color ComboBorder    => IsLight ? C(0xBB, 0xBB, 0xC8) : C(0x30, 0x36, 0x3D);

    // ── Fader / slider templates ──────────────────────────────────────────

    public static Color FaderTrack     => IsLight ? C(0xC8, 0xC8, 0xD2) : C(0x0D, 0x11, 0x17);
    public static Color FaderThumbBg   => IsLight ? C(0xB0, 0xB0, 0xBB) : C(0x3A, 0x3A, 0x3A);
    public static Color FaderThumbBdr  => IsLight ? C(0x90, 0x90, 0xA0) : C(0x55, 0x55, 0x55);
    public static Color FaderGradLight => IsLight ? C(0xCC, 0xCC, 0xD5) : C(0x4A, 0x4A, 0x4A);
    public static Color FaderGradDark  => IsLight ? C(0xA0, 0xA0, 0xAE) : C(0x35, 0x35, 0x35);

    // ── Dialog ────────────────────────────────────────────────────────────

    public static Color DialogBg       => IsLight ? C(0xF0, 0xF0, 0xF4) : C(0x16, 0x1B, 0x22);
    public static Color InputBg        => IsLight ? C(0xFF, 0xFF, 0xFF) : C(0x21, 0x26, 0x2D);
    public static Color InputFg        => IsLight ? C(0x1A, 0x1A, 0x2E) : C(0xC9, 0xD1, 0xD9);
    public static Color InputBorder    => IsLight ? C(0xBB, 0xBB, 0xC8) : C(0x30, 0x36, 0x3D);

    // ── Shadow ────────────────────────────────────────────────────────────

    public static Color ShadowColor    => IsLight ? Color.FromArgb(60, 0, 0, 0) : Colors.Black;
    public static double ShadowOpacity => IsLight ? 0.25 : 0.5;

    // ── DrawingContext controls (CompressorControl, ReverbControl, EQ) ─────

    // Panel / surface for DrawingContext
    public static Color DcBg           => IsLight ? C(0xF0, 0xF0, 0xF6) : C(0x0D, 0x11, 0x17);
    public static Color DcPanel        => IsLight ? C(0xE6, 0xE6, 0xEE) : C(0x16, 0x1B, 0x22);
    public static Color DcSurface      => IsLight ? C(0xDA, 0xDA, 0xE2) : C(0x21, 0x26, 0x2D);
    public static Color DcBorder       => IsLight ? C(0xC0, 0xC0, 0xCC) : C(0x30, 0x36, 0x3D);
    public static Color DcAccent       => IsLight ? C(0x1A, 0x60, 0xB0) : C(0x5B, 0xA4, 0xE6);
    public static Color DcTextPrimary  => IsLight ? C(0x1A, 0x1A, 0x2E) : C(0xC9, 0xD1, 0xD9);
    public static Color DcTextSecondary=> IsLight ? C(0x5A, 0x5A, 0x6E) : C(0x8B, 0x94, 0x9E);
    public static Color DcTextDim      => IsLight ? C(0x90, 0x90, 0xA0) : C(0x48, 0x4F, 0x58);

    // Knob
    public static Color DcKnobLight    => IsLight ? C(0xD2, 0xD2, 0xDC) : C(0x2A, 0x2F, 0x36);
    public static Color DcKnobDark     => IsLight ? C(0xBB, 0xBB, 0xC8) : C(0x16, 0x1B, 0x22);
    public static Color DcKnobRing     => IsLight ? C(0xA0, 0xA0, 0xAE) : C(0x30, 0x36, 0x3D);
    public static Color DcKnobArcBg    => IsLight ? C(0xCC, 0xCC, 0xD5) : C(0x21, 0x26, 0x2D);

    // Meter
    public static Color DcMeterBg      => IsLight ? C(0xD0, 0xD0, 0xD8) : C(0x0A, 0x0E, 0x14);
    public static Color DcMeterBorder  => IsLight ? C(0xBB, 0xBB, 0xC8) : C(0x1A, 0x20, 0x30);

    // Buttons (ratio, mode)
    public static Color DcBtnOff       => IsLight ? C(0xD8, 0xD8, 0xE0) : C(0x1A, 0x1F, 0x26);
    public static Color DcBtnOn        => IsLight ? C(0x3B, 0x82, 0xC8) : C(0x26, 0x61, 0x9C);
    public static Color DcBtnBorderOff => IsLight ? C(0xBB, 0xBB, 0xC8) : C(0x30, 0x36, 0x3D);
    public static Color DcBtnBorderOn  => IsLight ? C(0x1A, 0x60, 0xB0) : C(0x5B, 0xA4, 0xE6);

    // Scale ticks
    public static Color DcScaleTick    => IsLight ? C(0xBB, 0xBB, 0xC8) : C(0x30, 0x36, 0x3D);
    public static Color DcScaleMajor   => IsLight ? C(0x90, 0x90, 0xA0) : C(0x48, 0x4F, 0x58);
    public static Color DcScaleLabel   => IsLight ? C(0x90, 0x90, 0xA0) : C(0x48, 0x4F, 0x58);

    // Compressor specific
    public static Color DcGrColor      => C(0xF8, 0x51, 0x49); // Same in both themes
    public static Color DcInColor      => C(0x2E, 0xCC, 0x40);
    public static Color DcOutColor     => IsLight ? C(0x1A, 0x60, 0xB0) : C(0x5B, 0xA4, 0xE6);

    // ── Reverb-specific ──────────────────────────────────────────────────

    public static Color RvBg           => IsLight ? C(0xF0, 0xF0, 0xF6) : C(0x08, 0x0A, 0x12);
    public static Color RvPanel        => IsLight ? C(0xE8, 0xE8, 0xEE) : C(0x10, 0x14, 0x20);
    public static Color RvSection      => IsLight ? C(0xE0, 0xE0, 0xE8) : C(0x12, 0x16, 0x24);
    public static Color RvSurface      => IsLight ? C(0xD8, 0xD8, 0xE0) : C(0x18, 0x1C, 0x2A);
    public static Color RvBorder       => IsLight ? C(0xBB, 0xBB, 0xC8) : C(0x28, 0x2E, 0x40);
    public static Color RvSectionBorder=> IsLight ? C(0xC8, 0xC8, 0xD0) : C(0x20, 0x28, 0x38);

    // EQ specific
    public static Color EqBg           => IsLight ? C(0xE8, 0xE8, 0xF0) : C(0x0A, 0x0E, 0x14);
    public static Color EqGrid         => IsLight ? CA(30, 0x00, 0x00, 0x00) : CA(30, 0xFF, 0xFF, 0xFF);
    public static Color EqGridMajor    => IsLight ? CA(50, 0x00, 0x00, 0x00) : CA(50, 0xFF, 0xFF, 0xFF);
    public static Color EqZeroLine     => IsLight ? CA(80, 0x1A, 0x60, 0xB0) : CA(80, 0x5B, 0xA4, 0xE6);
    public static Color EqCurve        => IsLight ? CA(200, 0x20, 0x20, 0x30) : CA(200, 0xFF, 0xFF, 0xFF);
    public static Color EqLabel        => IsLight ? CA(120, 0x44, 0x44, 0x55) : CA(120, 0xCC, 0xCC, 0xCC);

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color CA(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    /// <summary>Creates a frozen SolidColorBrush.</summary>
    public static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>Creates a frozen Pen.</summary>
    public static Pen Pen(Color c, double thickness) { var p = new Pen(new SolidColorBrush(c), thickness); p.Freeze(); return p; }
}
