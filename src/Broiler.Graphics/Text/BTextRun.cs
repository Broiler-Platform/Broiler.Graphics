using Broiler.Graphics.Color;
using System;

namespace Broiler.Graphics.Text;

/// <summary>
/// A run of text sharing a single font style and color. This is the smallest unit handed to a
/// renderer's text drawing path; higher layers (DOM/CSS) split paragraphs into runs.
/// </summary>
public sealed record BTextRun(string Text, BFontStyle Font, BColor Color)
{
    public BTextRun(string text) : this(text ?? throw new ArgumentNullException(nameof(text)), BFontStyle.Default, BColor.Black) { }

    /// <summary>
    /// How far below the drawing origin the run's baseline lies, when whoever laid the run out
    /// decided it; <see langword="null"/> leaves it to the backend's own face (its ascent, or
    /// <see cref="BTextMeasurer"/>'s 0.8em for the managed renderer).
    /// </summary>
    /// <remarks>
    /// A layout engine stands images, inline blocks, underlines and the next line on the baseline
    /// it computed, and only it knows where that is. Each backend used to put the baseline where
    /// its own font metrics said, which differed from layout and from each other: the managed
    /// renderer 0.8em below the top, while Broiler.HTML's layout expects 0.928em for an installed
    /// font, so Acid3's 100px score was drawn 13px above the line laid out for it. Every backend
    /// draws a run that states its baseline on exactly that line. It travels with the run, as the
    /// font does, so a host that copies commands from one list into another keeps it.
    /// </remarks>
    public double? Baseline { get; init; }
}
