using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Tests.Shared;
using Broiler.Graphics.Text;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Text in a font the run carries (<see cref="BFontStyle.Face"/>) — a web font no machine has
/// installed — is measured and drawn from that font's own program.
/// </summary>
/// <remarks>
/// The render list used to name only a family, and the renderer resolved it against installed
/// fonts: a web font was measured by layout and then drawn in some other face. Acid3's
/// <c>map::after</c> draws an "X" in an Ahem-like font whose "X" is a full-em square, white on a
/// fuchsia box exactly one em square, so the box is solid white in a browser; Broiler drew an
/// installed "X" and the fuchsia showed round it.
/// </remarks>
internal static class FontFaceTextTests
{
    private static readonly BColor Fuchsia = BColor.FromArgb(255, 255, 0, 255);

    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("A run in a carried face fills that face's full-em glyph", CarriedFaceFillsItsEmBox));
        tests.Add(("A run in a carried face follows the render transform", CarriedFaceFollowsTransform));
        tests.Add(("A run in a carried face is measured from its advances", CarriedFaceIsMeasuredFromItsAdvances));
        tests.Add(("One face per font program, and font styles carrying it compare equal", OneFacePerProgram));
    }

    internal static BFontFace LoadFace() =>
        BFontFace.Load(SquareGlyphFont.Build()) ?? throw new AssertException("The square-glyph font did not load.");

    /// <summary>
    /// The Acid3 case: a 20px "X" in the square font, white on a fuchsia em box, leaves no fuchsia.
    /// The family is one no machine has, so only the carried program can draw this.
    /// </summary>
    private static void CarriedFaceFillsItsEmBox()
    {
        BFontFace face = LoadFace();
        var list = new BRenderList();
        list.FillRect(new BRect(10, 10, 20, 20), Fuchsia);
        list.DrawText(
            new BTextRun("X", new BFontStyle("AcidAhemTest", 20) { Face = face }, BColor.White),
            new BPoint(10, 10));

        using var renderer = new BImageRenderer();
        using BBitmap bitmap = renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(40, 40)),
            new BFrameContext(BColor.Black));

        for (int y = 10; y < 30; y++)
        {
            for (int x = 10; x < 30; x++)
                AssertEx.AreEqual(BColor.White, bitmap.GetPixel(x, y), $"pixel ({x},{y}) of the em box");
        }

        // The glyph is the em box and no more: one em wide, from the ascender to the descender.
        AssertEx.AreEqual(BColor.Black, bitmap.GetPixel(30, 20), "right of the em box");
        AssertEx.AreEqual(BColor.Black, bitmap.GetPixel(20, 30), "below the em box");
        AssertEx.AreEqual(BColor.Black, bitmap.GetPixel(20, 9), "above the em box");
    }

    private static void CarriedFaceFollowsTransform()
    {
        BFontFace face = LoadFace();
        var list = new BRenderList();
        list.PushTransform(BMatrix3x2.Translation(4, 6));
        list.DrawText(new BTextRun("XX", new BFontStyle("AcidAhemTest", 10) { Face = face }, BColor.Red), new BPoint(0, 0));
        list.PopTransform();

        using var renderer = new BImageRenderer();
        using BBitmap bitmap = renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(30, 20)),
            new BFrameContext(BColor.White));

        // Two abutting 10px squares from (4,6): 20 wide, 10 tall, no seam between them.
        AssertEx.AreEqual(BColor.Red, bitmap.GetPixel(4, 6), "top-left corner");
        AssertEx.AreEqual(BColor.Red, bitmap.GetPixel(14, 10), "where the two glyphs meet");
        AssertEx.AreEqual(BColor.Red, bitmap.GetPixel(23, 15), "bottom-right corner");
        AssertEx.AreEqual(BColor.White, bitmap.GetPixel(3, 6), "left of the run");
        AssertEx.AreEqual(BColor.White, bitmap.GetPixel(24, 10), "right of the run");
        AssertEx.AreEqual(BColor.White, bitmap.GetPixel(10, 16), "below the run");
    }

    private static void CarriedFaceIsMeasuredFromItsAdvances()
    {
        BFontFace face = LoadFace();

        AssertEx.AreEqual(60.0, BTextMeasurer.MeasureAdvance("XXX", new BFontStyle("AcidAhemTest", 20) { Face = face }));
        AssertEx.AreEqual(60.0, face.MeasureAdvance("XXX", 20));
    }

    private static void OneFacePerProgram()
    {
        TrueTypeFont font = TrueTypeFont.Load(SquareGlyphFont.Build()) ?? throw new AssertException("No font.");
        BFontFace? first = BFontFace.For(font);
        AssertEx.IsTrue(first is not null, "a font with outlines has a face");
        AssertEx.IsTrue(ReferenceEquals(first, BFontFace.For(font)), "the same program gives the same face");
        AssertEx.AreEqual(
            new BFontStyle("AcidAhemTest", 20) { Face = first },
            new BFontStyle("AcidAhemTest", 20) { Face = BFontFace.For(font) });
        AssertEx.AreNotEqual(new BFontStyle("AcidAhemTest", 20), new BFontStyle("AcidAhemTest", 20) { Face = first });
        AssertEx.IsTrue(BFontFace.For(null) is null, "no program, no face");
    }
}
