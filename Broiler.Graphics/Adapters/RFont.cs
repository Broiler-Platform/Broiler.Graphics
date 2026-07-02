
namespace Broiler.Graphics;

public abstract class RFont : ILayoutFont
{
    public abstract double Size { get; }
    public abstract double Height { get; }
    public abstract double UnderlineOffset { get; }
    public abstract double LeftPadding { get; }

    /// <summary>
    /// Space-separated list of OpenType feature tags to enable for this font
    /// (from the CSS <c>font-feature-settings</c> property), e.g. <c>"ss05"</c>.
    /// Consumed by the text shaper to apply the corresponding GSUB lookups.
    /// </summary>
    public string? FontFeatures { get; set; }

    public abstract double GetWhitespaceWidth(RGraphics graphics);
}