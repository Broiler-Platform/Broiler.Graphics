using Broiler.Graphics.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace Broiler.Graphics.Text;

/// <summary>
/// A font program that travels with the text drawn in it, for a face a backend cannot find by its
/// family name: a web font loaded from <c>@font-face</c>, or a file a host registered at runtime.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a name is not enough.</b> A render list used to describe its font only by
/// <see cref="BFontStyle.FamilyName"/>, and every backend resolves that name against the fonts
/// installed on the machine. A web font is installed nowhere, so the backend substituted a face of
/// its own while layout had measured the web font's advances: the words drifted, and a font whose
/// glyphs <em>are</em> the content (Acid3's Ahem-like <c>AcidAhemTest</c>, whose "X" is a full-em
/// square) drew as somebody else's letter. Carrying the program on <see cref="BFontStyle.Face"/>
/// lets every backend draw the very outlines layout measured.
/// </para>
/// <para>
/// <b>One instance per font program.</b> <see cref="For"/> hands back the same face for the same
/// <see cref="TrueTypeFont"/>, so two runs in one web font compare equal as
/// <see cref="BFontStyle"/> values, and a backend's per-font caches (DirectWrite text formats)
/// keep working. Equality is identity: two faces are the same font when they are the same program.
/// </para>
/// <para>
/// <b>How it is laid out.</b> The pen starts at the run's origin, the top of its em box, and the
/// baseline sits the face's <c>hhea</c> ascender below it; each glyph advances by its <c>hmtx</c>
/// width, and right-to-left or cursive text is shaped through <see cref="ComplexTextShaper"/> first.
/// That is how the managed text measurer of Broiler.HTML places and measures a registered font, so a
/// run drawn here lands in the space its layout reserved for it.
/// </para>
/// </remarks>
public sealed class BFontFace
{
    private static readonly ConditionalWeakTable<TrueTypeFont, BFontFace> Faces = new();

    private BFontFace(TrueTypeFont font) => Font = font;

    /// <summary>The parsed font program whose outlines this face draws.</summary>
    public TrueTypeFont Font { get; }

    /// <summary>
    /// The face for <paramref name="font"/>: the same instance for the same program, or
    /// <see langword="null"/> when it has no outlines any backend could fill.
    /// </summary>
    public static BFontFace? For(TrueTypeFont? font)
    {
        if (font is null || !font.HasOutlines)
            return null;

        return Faces.GetValue(font, static key => new BFontFace(key));
    }

    /// <summary>Parses <paramref name="data"/> (TrueType, OpenType or WOFF) into a face, or <see langword="null"/>.</summary>
    public static BFontFace? Load(byte[] data) => For(TrueTypeFont.Load(data));

    private int UnitsPerEm => Font.UnitsPerEm > 0 ? Font.UnitsPerEm : 1000;

    /// <summary>The pen advance of <paramref name="text"/> at <paramref name="size"/>, in the size's unit.</summary>
    public double MeasureAdvance(string text, double size)
    {
        ArgumentNullException.ThrowIfNull(text);

        double scale = size / UnitsPerEm;
        double advance = 0;
        foreach (var glyph in Glyphs(text))
            advance += glyph.Advance * scale;

        return advance;
    }

    /// <summary>
    /// The outlines of <paramref name="text"/> set at <paramref name="size"/> with the top of the em
    /// box at <paramref name="origin"/>, as closed polygons in the run's own y-down coordinates.
    /// </summary>
    /// <remarks>
    /// A backend fills them with the nonzero winding rule, after mapping them through whatever
    /// transform it draws the run with. Glyphs with no outline (spaces) contribute nothing but their
    /// advance.
    /// </remarks>
    public List<PointF[]> GetRunOutline(string text, double size, BPoint origin)
    {
        ArgumentNullException.ThrowIfNull(text);

        var outline = new List<PointF[]>();
        double scale = size / UnitsPerEm;
        double baseline = origin.Y + (Font.Ascender * scale);
        double penX = origin.X;

        foreach ((int glyph, int advance, int xOffset, int yOffset) in Glyphs(text))
        {
            if (glyph > 0)
            {
                double glyphX = penX + (xOffset * scale);
                double glyphY = baseline - (yOffset * scale);
                foreach (PointF[] contour in Font.GetGlyphContours(glyph))
                {
                    // The cached contour is shared (TrueTypeFont.GetGlyphContours), so it is copied,
                    // and font units are y-up while the run's space is y-down.
                    var points = new PointF[contour.Length];
                    for (int i = 0; i < contour.Length; i++)
                    {
                        points[i] = new PointF(
                            (float)(glyphX + (contour[i].X * scale)),
                            (float)(glyphY - (contour[i].Y * scale)));
                    }

                    outline.Add(points);
                }
            }

            penX += advance * scale;
        }

        return outline;
    }

    /// <summary>The run's glyphs in visual order with their advances and offsets, in font units.</summary>
    private IEnumerable<(int Glyph, int Advance, int XOffset, int YOffset)> Glyphs(string text)
    {
        if (ComplexTextShaper.RequiresShaping(text))
        {
            foreach (ShapedGlyph shaped in ComplexTextShaper.Shape(Font, text))
                yield return (shaped.Glyph, shaped.Advance, shaped.XOffset, shaped.YOffset);

            yield break;
        }

        for (int i = 0; i < text.Length;)
        {
            int codepoint = UnicodeCodepointReader.ReadCodePoint(text, i, out int next);
            i = next;

            int glyph = Font.GetGlyphIndex(codepoint);
            yield return (glyph, Font.GetAdvanceWidth(glyph), 0, 0);
        }
    }
}
