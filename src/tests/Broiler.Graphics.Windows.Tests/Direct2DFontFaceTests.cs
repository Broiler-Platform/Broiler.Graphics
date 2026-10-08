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
