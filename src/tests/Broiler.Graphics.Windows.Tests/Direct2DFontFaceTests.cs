using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Tests.Shared;
using Broiler.Graphics.Text;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Text in a font the run carries (<see cref="BFontStyle.Face"/>) on the Direct2D backend.
/// </summary>
/// <remarks>
/// A carried face is a web font, which DirectWrite cannot find by family name and used to
/// substitute: Acid3's white Ahem "X" on a fuchsia em box drew as a Segoe UI "X" with the fuchsia
/// round it. The backend now fills the face's own outlines as a path geometry, so like the triangle
/// suite this renders through the real device and reads the pixels back — the geometry calls are
/// vtable slots, and only a render proves them.
/// </remarks>
internal static class Direct2DFontFaceTests
{
    private static readonly BColor Fuchsia = BColor.FromArgb(255, 255, 0, 255);

    public static void Register(ICollection<(string Name, Action Body)> tests)
    {
        tests.Add(("Direct2D draws a carried face's full-em glyph over its whole em box", FillsTheEmBox));
        tests.Add(("Direct2D carried-face text agrees with the CPU renderer", AgreesWithTheCpuRenderer));
        tests.Add(("Direct2D stands DirectWrite text on the baseline its layout stated", DirectWriteTextStandsOnStatedBaseline));
        tests.Add(("Direct2D stands carried-face text on the baseline its layout stated", CarriedFaceStandsOnStatedBaseline));
    }

    private static void FillsTheEmBox()
    {
        using var renderer = new Direct2DRenderer();
        using BBitmap bitmap = Render(renderer);

        for (int y = 10; y < 30; y++)
        {
            for (int x = 10; x < 30; x++)
                Assert.AreEqual(BColor.White, bitmap.GetPixel(x, y), $"pixel ({x},{y}) of the em box");
        }

        Assert.AreEqual(BColor.Black, bitmap.GetPixel(30, 20), "right of the em box");
        Assert.AreEqual(BColor.Black, bitmap.GetPixel(20, 30), "below the em box");
    }

    private static void AgreesWithTheCpuRenderer()
    {
        using var direct2D = new Direct2DRenderer();
        using var cpu = new BImageRenderer();
        using BBitmap fromDirect2D = Render(direct2D);
        using BBitmap fromCpu = Render(cpu);

        foreach ((int x, int y) in (ReadOnlySpan<(int, int)>)[(10, 10), (29, 29), (20, 20), (9, 20), (30, 20), (20, 9), (20, 30)])
            Assert.AreEqual(fromCpu.GetPixel(x, y), fromDirect2D.GetPixel(x, y), $"({x},{y})");
    }

    /// <summary>
    /// A stated baseline (<see cref="BTextRun.Baseline"/>) becomes the format's
    /// uniform line spacing, which puts the first line's baseline that far below the layout box's
    /// top: a 100px Arial "H" has its foot on the line 92.8px down, not at Arial's 90.5px ascent.
    /// </summary>
    private static void DirectWriteTextStandsOnStatedBaseline()
    {
        using var renderer = new Direct2DRenderer();
        var list = new BRenderList();
        list.DrawText(new BTextRun("H", new BFontStyle("Arial", 100), BColor.Black) { Baseline = 92.8 }, new BPoint(10, 10));
        using BBitmap bitmap = renderer.RenderToImage(
            list, BSurfaceDescriptor.Default(new BSize(140, 140)), new BFrameContext(BColor.White));

        int bottom = LowestInkedRow(bitmap);
        Assert.True(Math.Abs((bottom + 1) - 102.8) <= 1.0, $"The H's ink ends at row {bottom}; its foot belongs on y = 102.8.");
    }

    private static void CarriedFaceStandsOnStatedBaseline()
    {
        BFontFace face = BFontFace.Load(SquareGlyphFont.Build()) ?? throw new AssertException("No face.");
        using var renderer = new Direct2DRenderer();
        var list = new BRenderList();
        list.DrawText(new BTextRun("X", new BFontStyle("AcidAhemTest", 100) { Face = face }, BColor.Black) { Baseline = 92.8 }, new BPoint(10, 10));
        using BBitmap bitmap = renderer.RenderToImage(
            list, BSurfaceDescriptor.Default(new BSize(140, 140)), new BFrameContext(BColor.White));

        Assert.AreEqual(122, LowestInkedRow(bitmap), "the square's foot, 0.2em under a baseline at 102.8");
    }

    /// <summary>The lowest row with a pixel at least half dark.</summary>
    private static int LowestInkedRow(BBitmap bitmap)
    {
        for (int y = bitmap.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).R < 128)
                    return y;
            }
        }

        throw new AssertException("Nothing was drawn.");
    }

    /// <summary>A 20px "X" in the square font, white on a fuchsia 20px box at (10,10), on black.</summary>
    private static BBitmap Render(IBroilerRenderer renderer)
    {
        BFontFace face = BFontFace.Load(SquareGlyphFont.Build()) ?? throw new AssertException("No face.");
        var list = new BRenderList();
        list.FillRect(new BRect(10, 10, 20, 20), Fuchsia);
        list.DrawText(
            new BTextRun("X", new BFontStyle("AcidAhemTest", 20) { Face = face }, BColor.White),
            new BPoint(10, 10));

        return renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(40, 40)),
            new BFrameContext(BColor.Black));
    }
}
