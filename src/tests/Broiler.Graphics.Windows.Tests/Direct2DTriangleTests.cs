using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Coverage for the triangle command on the Direct2D backend.
/// </summary>
/// <remarks>
/// Direct2D has a call for rectangles, rounded rectangles and ellipses but none for an arbitrary
/// polygon, so a triangle is the one command here that has to build an ID2D1PathGeometry by hand -
/// GetFactory, CreatePathGeometry, Open, BeginFigure, AddLines, EndFigure, Close, FillGeometry.
/// Every one of those is a vtable slot index, and a wrong index is an access violation rather than
/// a compile error, so this suite renders through the real device and reads the pixels back. It is
/// the only thing that proves the indices.
/// </remarks>
internal static class Direct2DTriangleTests
{
    public static void Register(ICollection<(string Name, Action Body)> tests)
    {
        tests.Add(("Direct2D fills a triangle's interior and not its bounding box", FillsInteriorOnly));
        tests.Add(("Direct2D triangle agrees with the CPU renderer", AgreesWithTheCpuRenderer));
    }

    private static void FillsInteriorOnly()
    {
        using var renderer = new Direct2DRenderer();
        using BBitmap bitmap = Render(renderer);

        Assert.AreEqual(BColor.Red, bitmap.GetPixel(2, 14), "a pixel inside the triangle");
        Assert.AreEqual(BColor.White, bitmap.GetPixel(14, 2), "a pixel inside the bounding box but outside the triangle");
    }

    /// <summary>
    /// Not a pixel-exact comparison — Direct2D and the CPU scanline filler antialias differently,
    /// and demanding they match would be asserting a coincidence. What must agree is the shape:
    /// the same corners in, the same corners covered and uncovered out.
    /// </summary>
    private static void AgreesWithTheCpuRenderer()
    {
        using var direct2D = new Direct2DRenderer();
        using var cpu = new BImageRenderer();
        using BBitmap fromDirect2D = Render(direct2D);
        using BBitmap fromCpu = Render(cpu);

        foreach ((int x, int y) in (ReadOnlySpan<(int, int)>)[(2, 14), (1, 15), (7, 14)])
            AssertSameCoverage(fromDirect2D, fromCpu, x, y, covered: true);

        foreach ((int x, int y) in (ReadOnlySpan<(int, int)>)[(14, 2), (15, 1), (9, 2)])
            AssertSameCoverage(fromDirect2D, fromCpu, x, y, covered: false);
    }

    private static void AssertSameCoverage(BBitmap direct2D, BBitmap cpu, int x, int y, bool covered)
    {
        BColor expected = covered ? BColor.Red : BColor.White;
        Assert.AreEqual(expected, direct2D.GetPixel(x, y), $"Direct2D at ({x},{y})");
        Assert.AreEqual(expected, cpu.GetPixel(x, y), $"CPU renderer at ({x},{y})");
    }

    /// <summary>The lower-left half of a 16x16 box, so the diagonal runs corner to corner.</summary>
    private static BBitmap Render(IBroilerRenderer renderer)
    {
        var list = new BRenderList();
        list.FillTriangle(new BPoint(0, 0), new BPoint(0, 16), new BPoint(16, 16), BColor.Red);

        return renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(16, 16)),
            new BFrameContext(BColor.White));
    }
}
