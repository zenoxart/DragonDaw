using System.Windows.Media;

namespace DAW.Views.Arrangement;

/// <summary>
/// A flat list item in the Pattern Browser panel.
/// Represents either a pattern header row or a channel row beneath it.
/// The list is rebuilt whenever patterns or channels change.
/// </summary>
public sealed class PatternBrowserItem
{
    /// <summary>True = pattern header row. False = channel row (indented).</summary>
    public bool IsPatternRow { get; init; }

    /// <summary>True when this pattern is the currently active one (shown with accent colour).</summary>
    public bool IsActive { get; set; }

    /// <summary>Display icon: "▦" for patterns, PluginIcon string for channels.</summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>Display text shown in the label column.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Small color dot shown for channel rows (transparent for pattern rows).</summary>
    public Color DotColor { get; init; }

    // ── Backing references (used in code-behind for DnD / click logic) ────

    /// <summary>The underlying PatternModel (non-null when IsPatternRow = true).</summary>
    public DAW.Models.Sequencer.PatternModel? Pattern { get; init; }

    /// <summary>The underlying ChannelViewModel (non-null when IsPatternRow = false).</summary>
    public DAW.ViewModels.Sequencer.ChannelViewModel? Channel { get; init; }
}
