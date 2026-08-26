using System;

namespace Broiler.Graphics;

/// <summary>
/// A run of text sharing a single font style and color. This is the smallest unit handed to a
/// renderer's text drawing path; higher layers (DOM/CSS) split paragraphs into runs.
/// </summary>
public sealed record BTextRun(
    string Text,
    BFontStyle Font,
    BColor Color)
{
    public BTextRun(string text)
        : this(text ?? throw new ArgumentNullException(nameof(text)), BFontStyle.Default, BColor.Black)
    {
    }
}
