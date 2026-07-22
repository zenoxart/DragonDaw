using System.Windows;
using System.Windows.Media;

namespace DAW.Services;

/// <summary>
/// Theme definitions and runtime theme switching via application-level resource overrides.
/// </summary>
public sealed class ThemeService
{
    public static ThemeService Instance { get; } = new();

    public string CurrentTheme { get; private set; } = "DragonDark";

    public static readonly (string Id, string DisplayName)[] AvailableThemes =
    [
        ("DragonDark",    "🐉 Dragon Dark"),
        ("MidnightBlue",  "🌙 Midnight Blue"),
        ("ForestGreen",   "🌿 Forest Green"),
        ("Ember",         "🔥 Ember"),
        ("SnowWhite",     "☀ Snow White"),
        ("FrozenLagoon",  "🧊 Frozen Lagoon"),
    ];

    /// <summary>
    /// Applies the given theme by overwriting well-known application-level brush resources.
    /// </summary>
    public void ApplyTheme(string themeId)
    {
        CurrentTheme = themeId;
        var def = GetThemeColors(themeId);
        var res = Application.Current.Resources;

        // Backgrounds
        SetBrush(res, "DarkBg",    def.DarkBg);
        SetBrush(res, "PanelBg",   def.PanelBg);
        SetBrush(res, "ControlBg", def.ControlBg);
        SetBrush(res, "BorderBrush", def.Border);

        // Accent
        SetBrush(res, "AccentBrush", def.Accent);
        SetBrush(res, "AccentLightBrush", def.AccentLight);
        SetBrush(res, "AccentHoverBrush", def.AccentHover);

        // Text
        SetBrush(res, "TextPrimary",   def.TextPrimary);
        SetBrush(res, "TextSecondary", def.TextSecondary);
        SetBrush(res, "TextDim",       def.TextDim);

        // Highlight (used in ComboBox dropdown, menus, hover states)
        SetBrush(res, "HighlightBg",   def.HighlightBg);
        SetBrush(res, "DropdownBg",    def.DropdownBg);

        // Mixer channel selection
        SetBrush(res, "ChannelSelectionBg",   def.ChannelSelectionBg);
        SetLinearGradient(res, "ChannelSelectionGlow", def.Accent);

        // Fader / Meter / Knob
        SetBrush(res, "FaderBg",     def.FaderBg);
        SetBrush(res, "FaderBorder", def.FaderBorder);
        SetBrush(res, "MeterBg",    def.MeterBg);

        // Fader thumb gradient (Color resources, not brushes)
        res["FaderGradientLight"] = def.FaderGradientLight;
        res["FaderGradientDark"]  = def.FaderGradientDark;
    }

    public Color GetAccentColor(string themeId) => GetThemeColors(themeId).Accent;

    private static void SetBrush(ResourceDictionary res, string key, Color c)
    {
        res[key] = new SolidColorBrush(c);
    }

    /// <summary>
    /// Sets a LinearGradientBrush resource that fades from a semi-transparent
    /// accent colour at the bottom to transparent at the top — used for the
    /// channel-selection glow overlay.
    /// </summary>
    private static void SetLinearGradient(ResourceDictionary res, string key, Color accent)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint   = new System.Windows.Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0x80, accent.R, accent.G, accent.B), 0.0));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0x25, accent.R, accent.G, accent.B), 0.4));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0));
        brush.Freeze();
        res[key] = brush;
    }

    private static ThemeDef GetThemeColors(string id) => id switch
    {
        "MidnightBlue" => new(
            DarkBg:       Color.FromRgb(0x0B, 0x0F, 0x1A),
            PanelBg:      Color.FromRgb(0x11, 0x17, 0x27),
            ControlBg:    Color.FromRgb(0x1A, 0x22, 0x35),
            Border:       Color.FromRgb(0x26, 0x30, 0x48),
            Accent:       Color.FromRgb(0x3B, 0x82, 0xF6),
            AccentLight:  Color.FromRgb(0x60, 0xA5, 0xFA),
            AccentHover:  Color.FromRgb(0x93, 0xBB, 0xFD),
            TextPrimary:  Color.FromRgb(0xC9, 0xD1, 0xD9),
            TextSecondary:Color.FromRgb(0x8B, 0x94, 0x9E),
            TextDim:      Color.FromRgb(0x48, 0x4F, 0x58),
            HighlightBg:  Color.FromRgb(0x25, 0x2B, 0x37),
            DropdownBg:   Color.FromRgb(0x14, 0x1A, 0x2A),
            FaderBg:      Color.FromRgb(0x2A, 0x30, 0x40),
            FaderBorder:  Color.FromRgb(0x40, 0x48, 0x58),
            MeterBg:      Color.FromRgb(0x08, 0x0C, 0x18),
            FaderGradientLight: Color.FromRgb(0x4A, 0x4A, 0x4A),
            FaderGradientDark:  Color.FromRgb(0x35, 0x35, 0x35),
            ChannelSelectionBg: Color.FromRgb(0x1C, 0x28, 0x44)),
        "ForestGreen" => new(
            DarkBg:       Color.FromRgb(0x0A, 0x12, 0x0A),
            PanelBg:      Color.FromRgb(0x12, 0x1E, 0x14),
            ControlBg:    Color.FromRgb(0x1A, 0x2A, 0x1C),
            Border:       Color.FromRgb(0x28, 0x3C, 0x2A),
            Accent:       Color.FromRgb(0x22, 0xC5, 0x5E),
            AccentLight:  Color.FromRgb(0x4A, 0xDE, 0x80),
            AccentHover:  Color.FromRgb(0x86, 0xEF, 0xAC),
            TextPrimary:  Color.FromRgb(0xC9, 0xD1, 0xD9),
            TextSecondary:Color.FromRgb(0x8B, 0x94, 0x9E),
            TextDim:      Color.FromRgb(0x48, 0x4F, 0x58),
            HighlightBg:  Color.FromRgb(0x1E, 0x30, 0x20),
            DropdownBg:   Color.FromRgb(0x0E, 0x18, 0x10),
            FaderBg:      Color.FromRgb(0x28, 0x38, 0x2A),
            FaderBorder:  Color.FromRgb(0x38, 0x4C, 0x3A),
            MeterBg:      Color.FromRgb(0x08, 0x10, 0x08),
            FaderGradientLight: Color.FromRgb(0x4A, 0x4A, 0x4A),
            FaderGradientDark:  Color.FromRgb(0x35, 0x35, 0x35),
            ChannelSelectionBg: Color.FromRgb(0x18, 0x2E, 0x1C)),
        "Ember" => new(
            DarkBg:       Color.FromRgb(0x14, 0x0C, 0x08),
            PanelBg:      Color.FromRgb(0x1E, 0x14, 0x10),
            ControlBg:    Color.FromRgb(0x2A, 0x1E, 0x18),
            Border:       Color.FromRgb(0x3E, 0x2C, 0x22),
            Accent:       Color.FromRgb(0xF9, 0x73, 0x16),
            AccentLight:  Color.FromRgb(0xFB, 0x92, 0x3C),
            AccentHover:  Color.FromRgb(0xFD, 0xBA, 0x74),
            TextPrimary:  Color.FromRgb(0xC9, 0xD1, 0xD9),
            TextSecondary:Color.FromRgb(0x8B, 0x94, 0x9E),
            TextDim:      Color.FromRgb(0x48, 0x4F, 0x58),
            HighlightBg:  Color.FromRgb(0x30, 0x22, 0x18),
            DropdownBg:   Color.FromRgb(0x1A, 0x12, 0x0C),
            FaderBg:      Color.FromRgb(0x38, 0x28, 0x1E),
            FaderBorder:  Color.FromRgb(0x50, 0x3A, 0x2C),
            MeterBg:      Color.FromRgb(0x10, 0x08, 0x04),
            FaderGradientLight: Color.FromRgb(0x4A, 0x4A, 0x4A),
            FaderGradientDark:  Color.FromRgb(0x35, 0x35, 0x35),
            ChannelSelectionBg: Color.FromRgb(0x30, 0x20, 0x14)),
        "SnowWhite" => new(
            DarkBg:       Color.FromRgb(0xF5, 0xF5, 0xF7),
            PanelBg:      Color.FromRgb(0xEB, 0xEB, 0xEF),
            ControlBg:    Color.FromRgb(0xE0, 0xE0, 0xE5),
            Border:       Color.FromRgb(0xC8, 0xC8, 0xD0),
            Accent:       Color.FromRgb(0xC4, 0x1E, 0x3A),
            AccentLight:  Color.FromRgb(0xE6, 0x39, 0x46),
            AccentHover:  Color.FromRgb(0xF7, 0x27, 0x35),
            TextPrimary:  Color.FromRgb(0x1A, 0x1A, 0x2E),
            TextSecondary:Color.FromRgb(0x5A, 0x5A, 0x6E),
            TextDim:      Color.FromRgb(0x9A, 0x9A, 0xA8),
            HighlightBg:  Color.FromRgb(0xD8, 0xD8, 0xE2),
            DropdownBg:   Color.FromRgb(0xF0, 0xF0, 0xF4),
            FaderBg:      Color.FromRgb(0xD0, 0xD0, 0xD8),
            FaderBorder:  Color.FromRgb(0xB0, 0xB0, 0xBB),
            MeterBg:      Color.FromRgb(0xDE, 0xDE, 0xE5),
            FaderGradientLight: Color.FromRgb(0xF0, 0xF0, 0xF4),
            FaderGradientDark:  Color.FromRgb(0xC0, 0xC0, 0xCC),
            ChannelSelectionBg: Color.FromRgb(0xD4, 0xC8, 0xCC)),
        "FrozenLagoon" => new(
            DarkBg:       Color.FromRgb(0x05, 0x12, 0x18),
            PanelBg:      Color.FromRgb(0x09, 0x1E, 0x26),
            ControlBg:    Color.FromRgb(0x0E, 0x2C, 0x36),
            Border:       Color.FromRgb(0x16, 0x42, 0x52),
            Accent:       Color.FromRgb(0x00, 0xC5, 0xCD),
            AccentLight:  Color.FromRgb(0x2E, 0xE0, 0xE8),
            AccentHover:  Color.FromRgb(0x7A, 0xEE, 0xF4),
            TextPrimary:  Color.FromRgb(0xD0, 0xEE, 0xF2),
            TextSecondary:Color.FromRgb(0x7A, 0xAA, 0xB8),
            TextDim:      Color.FromRgb(0x35, 0x58, 0x65),
            HighlightBg:  Color.FromRgb(0x0C, 0x2E, 0x3A),
            DropdownBg:   Color.FromRgb(0x06, 0x18, 0x20),
            FaderBg:      Color.FromRgb(0x10, 0x38, 0x44),
            FaderBorder:  Color.FromRgb(0x1C, 0x50, 0x60),
            MeterBg:      Color.FromRgb(0x03, 0x0E, 0x14),
            FaderGradientLight: Color.FromRgb(0x2E, 0x6E, 0x7A),
            FaderGradientDark:  Color.FromRgb(0x14, 0x42, 0x50),
            ChannelSelectionBg: Color.FromRgb(0x0A, 0x2C, 0x3C)),
        _ => new( // DragonDark (default)
            DarkBg:       Color.FromRgb(0x0D, 0x11, 0x17),
            PanelBg:      Color.FromRgb(0x16, 0x1B, 0x22),
            ControlBg:    Color.FromRgb(0x21, 0x26, 0x2D),
            Border:       Color.FromRgb(0x30, 0x36, 0x3D),
            Accent:       Color.FromRgb(0xC4, 0x1E, 0x3A),
            AccentLight:  Color.FromRgb(0xE6, 0x39, 0x46),
            AccentHover:  Color.FromRgb(0xF7, 0x27, 0x35),
            TextPrimary:  Color.FromRgb(0xC9, 0xD1, 0xD9),
            TextSecondary:Color.FromRgb(0x8B, 0x94, 0x9E),
            TextDim:      Color.FromRgb(0x48, 0x4F, 0x58),
            HighlightBg:  Color.FromRgb(0x25, 0x2B, 0x37),
            DropdownBg:   Color.FromRgb(0x1A, 0x1F, 0x26),
            FaderBg:      Color.FromRgb(0x3A, 0x3A, 0x3A),
            FaderBorder:  Color.FromRgb(0x55, 0x55, 0x55),
            MeterBg:      Color.FromRgb(0x0A, 0x0E, 0x14),
            FaderGradientLight: Color.FromRgb(0x4A, 0x4A, 0x4A),
            FaderGradientDark:  Color.FromRgb(0x35, 0x35, 0x35),
            ChannelSelectionBg: Color.FromRgb(0x2A, 0x1A, 0x1E)),
    };

    private record ThemeDef(
        Color DarkBg, Color PanelBg, Color ControlBg, Color Border,
        Color Accent, Color AccentLight, Color AccentHover,
        Color TextPrimary, Color TextSecondary, Color TextDim,
        Color HighlightBg, Color DropdownBg,
        Color FaderBg, Color FaderBorder, Color MeterBg,
        Color FaderGradientLight, Color FaderGradientDark,
        Color ChannelSelectionBg);
}
