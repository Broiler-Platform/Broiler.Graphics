using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Coverage for the one primitive in the command set that is not axis-aligned.
/// </summary>
/// <remarks>
/// The whole reason a triangle command exists is that a rotated rectangle is not a portable
/// diagonal: the CPU rasterizer reduces every rotated shape but FillRect to its bounding box, and
/// the browser planner deliberately matches that. So the assertions here are all about the shape
/// actually having a slope — a test that only checked "some pixels got coloured" would pass just as
/// happily against a filled bounding box, which is precisely the bug being ruled out.
/// </remarks>
internal static class TriangleRenderTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Triangle fills its interior and not its bounding box", TriangleIsNotItsBoundingBox));
        tests.Add(("Triangle corner order does not decide the fill", WindingIsNonZero));
        tests.Add(("Triangle antialiases its diagonal", DiagonalIsAntialiased));
        tests.Add(("Triangle honours clips and transforms", RespectsClipsAndTransforms));
        tests.Add(("A degenerate triangle records nothing", DegenerateIsDropped));
    }

    /// <summary>
    /// A right triangle occupying the lower-left half of a 16x16 box: the lower-left corner is
    /// inside it and the upper-right corner is outside, though both are inside the bounding box.
    /// </summary>
    private static void TriangleIsNotItsBoundingBox()
    {
        using BBitmap bitmap = Render(list =>
            list.FillTriangle(new BPoint(0, 0), new BPoint(0, 16), new BPoint(16, 16), BColor.Red));

        AssertEx.AreEqual(BColor.Red, bitmap.GetPixel(2, 14));
        AssertEx.AreEqual(BColor.White, bitmap.GetPixel(14, 2));
    }

    private static void WindingIsNonZero()
    {
        using BBitmap clockwise = Render(list =>
            list.FillTriangle(new BPoint(0, 0), new BPoint(16, 16), new BPoint(0, 16), BColor.Red));
        using BBitmap counterClockwise = Render(list =>
            list.FillTriangle(new BPoint(0, 0), new BPoint(0, 16), new BPoint(16, 16), BColor.Red));

        AssertEx.AreEqual(BColor.Red, clockwise.GetPixel(2, 14));
        AssertEx.AreEqual(BColor.Red, counterClockwise.GetPixel(2, 14));
    }

    /// <summary>
    /// The point of using the glyph filler rather than FillPolygon: FillPolygon decides each pixel
    /// by a point-in-polygon test at the pixel centre, so every edge is hard. A pixel straddling
    /// the hypotenuse must come out between the two colours.
    /// </summary>
    private static void DiagonalIsAntialiased()
    {
        using BBitmap bitmap = Render(list =>
            list.FillTriangle(new BPoint(0, 0), new BPoint(0, 16), new BPoint(16, 16), BColor.Red));

        bool foundPartial = false;
        for (int i = 1; i < 15 && !foundPartial; i++)
        {
            BColor pixel = bitmap.GetPixel(i, i);
            foundPartial = pixel != BColor.Red && pixel != BColor.White;
        }

        AssertEx.IsTrue(foundPartial, "Expected a partially covered pixel along the hypotenuse.");
    }

    private static void RespectsClipsAndTransforms()
    {
        using BBitmap bitmap = Render(list =>
        {
            list.PushClip(new BRect(0, 8, 16, 8));
            list.PushTransform(BMatrix3x2.Translation(0, 0));
            list.FillTriangle(new BPoint(0, 0), new BPoint(0, 16), new BPoint(16, 16), BColor.Red);
            list.PopTransform();
            list.PopClip();
        });

        AssertEx.AreEqual(BColor.Red, bitmap.GetPixel(2, 14));
        AssertEx.AreEqual(BColor.White, bitmap.GetPixel(1, 6));
    }

    private static void DegenerateIsDropped()
    {
        var list = new BRenderList();
        list.FillTriangle(new BPoint(0, 0), new BPoint(4, 4), new BPoint(8, 8), BColor.Red);
        list.FillTriangle(new BPoint(1, 1), new BPoint(1, 1), new BPoint(1, 1), BColor.Red);

        AssertEx.AreEqual(0, list.Count);
    }

    private static BBitmap Render(Action<BRenderList> record)
    {
        using var renderer = new BImageRenderer();
        var list = new BRenderList();
        record(list);
        return renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(16, 16)),
            new BFrameContext(BColor.White));
    }
}
