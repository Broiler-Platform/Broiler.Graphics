using Broiler.Graphics.Text;

namespace Broiler.Graphics.Adapters;

public abstract class BFont : ILayoutFont
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

    /// <summary>
    /// The single family this font actually resolved to — never the CSS
    /// <c>font-family</c> list it was asked for.
    /// </summary>
    /// <remarks>
    /// A consumer that has the font and re-derives a family from the document's style instead
    /// picks a different face than the one every width was measured with. The declared value is a
    /// list (<c>"Verdana, Arial, Helvetica"</c>), and resolving it is this font's job, done once by
    /// <see cref="FontsHandler.GetCachedFont"/>; publishing the answer is what lets a render
    /// backend draw with the face that was measured. Empty when the implementation does not know
    /// (test doubles), in which case a consumer must fall back to its own resolution.
    /// </remarks>
    public virtual string Family => string.Empty;

    /// <summary>
    /// The style this font was resolved with — the bold and italic bits included, so a consumer
    /// does not have to re-read them from a style sheet it may not have.
    /// </summary>
    public virtual FontStyle Style => FontStyle.Regular;

    /// <summary>
    /// The font program this font measured with when a render backend cannot find it by
    /// <see cref="Family"/> — a web font from <c>@font-face</c>, or a file registered at runtime —
    /// and <see langword="null"/> for an installed family.
    /// </summary>
    /// <remarks>
    /// A consumer building a <see cref="BFontStyle"/> from this font passes it on as
    /// <see cref="BFontStyle.Face"/>, which is what makes a backend draw the face layout measured
    /// rather than an installed one that happens to answer to the name, or none at all.
    /// </remarks>
    public virtual BFontFace? Face => null;

    public abstract double GetWhitespaceWidth(BGraphics graphics);
}