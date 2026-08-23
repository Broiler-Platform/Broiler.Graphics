namespace Broiler.Graphics;

/// <summary>
/// Font abstraction consumed by layout for text measurement and inline metrics.
/// Exposes only the metrics layout reads from the renderer's <c>RFont</c> today,
/// without binding consumers to a concrete graphics backend. Instances are
/// resolved and measured through the layout environment.
/// </summary>
public interface ILayoutFont
{
    /// <summary>
    /// The font size, in typographic <b>points</b> — the unit the CSS cascade resolves font sizes
    /// in here (<c>CssBoxProperties.ComputedFontSizePoints</c>). Multiply by 96/72 for CSS pixels,
    /// as <c>CssBoxProperties.GetEmHeight</c> does.
    /// </summary>
    /// <remarks>
    /// This said "CSS pixels" and was wrong, which is not a harmless comment: a consumer that
    /// believed it drew every run at three quarters of the size the same font had been measured at.
    /// </remarks>
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
