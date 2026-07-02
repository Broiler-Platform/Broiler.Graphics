namespace Broiler.Graphics;

/// <summary>
/// Font abstraction consumed by layout for text measurement and inline metrics.
/// Exposes only the metrics layout reads from the renderer's <c>RFont</c> today,
/// without binding consumers to a concrete graphics backend. Instances are
/// resolved and measured through the layout environment.
/// </summary>
public interface ILayoutFont
{
    /// <summary>The font size, in CSS pixels.</summary>
    double Size { get; }

    /// <summary>The line height of the font, in CSS pixels.</summary>
    double Height { get; }

    /// <summary>Offset of the underline from the baseline, in CSS pixels.</summary>
    double UnderlineOffset { get; }

    /// <summary>Left-side bearing applied before the first glyph, in CSS pixels.</summary>
    double LeftPadding { get; }

    /// <summary>
    /// Space-separated OpenType feature tags enabled for this font (from
    /// <c>font-feature-settings</c>), or <c>null</c> when none are set.
    /// </summary>
    string? FontFeatures { get; }
}
