using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Tiles drawn with <see cref="BImageSampling.NearestNeighbor"/> at a 150% display scale keep their
/// pixels whole and meet edge to edge.
/// </summary>
/// <remarks>
/// Acid2 paints the yellow behind its eyes with two layers of a 2×2 checkerboard, half yellow and
/// half transparent, over red, the second layer a pixel to the right of the first, so together they
/// are solid yellow. In the Windows window at 150%, Direct2D blended each 2px tile's pixels into its
/// 3 device pixels, and the red showed through the half-transparent middle as an orange dither.
/// </remarks>
internal static class Direct2DImageSamplingTests
{
    private const double DpiScale = 1.5;
    private const int Dips = 24;
    private static readonly BColor Yellow = new(255, 255, 0);

    public static void Register(ICollection<(string Name, Action Body)> tests)
    {
        tests.Add(("Direct2D draws nearest-neighbour checkerboard tiles at 150% as solid yellow", NearestTilesAreSolid));
        tests.Add(("Direct2D still blends linear tiles at 150%", LinearTilesBlend));
    }

    private static void NearestTilesAreSolid()
    {
        using var renderer = new Direct2DRenderer();
        using BBitmap bitmap = Render(renderer, BImageSampling.NearestNeighbor);

        int size = (int)(Dips * DpiScale);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                Assert.AreEqual(Yellow, bitmap.GetPixel(x, y), $"device pixel ({x},{y})");
        }
    }

    /// <summary>Control: the default sampling is still the smooth one, which this scene shows.</summary>
    private static void LinearTilesBlend()
    {
        using var renderer = new Direct2DRenderer();
        using BBitmap bitmap = Render(renderer, BImageSampling.Linear);

        bool blended = false;
        int size = (int)(Dips * DpiScale);
        for (int y = 0; y < size && !blended; y++)
        {
            for (int x = 0; x < size && !blended; x++)
                blended = bitmap.GetPixel(x, y) != Yellow;
        }

        Assert.True(blended, "linear sampling blends the checkerboard with the red behind it");
    }

    /// <summary>
    /// Red, then two layers of 2×2 checkerboard tiles over it, the second offset 1 DIP to the right,
    /// on a 24×24 DIP surface at 150%.
    /// </summary>
    private static BBitmap Render(IBroilerRenderer renderer, BImageSampling sampling)
    {
        // Yellow at the top left and bottom right, transparent elsewhere.
        byte[] rgba =
        [
            255, 255, 0, 255, 0, 0, 0, 0,
            0, 0, 0, 0, 255, 255, 0, 255,
        ];
        BImageHandle image = renderer.CreateImage(new BPixelBuffer(2, 2, rgba));

        var list = new BRenderList();
        var all = new BRect(0, 0, Dips, Dips);
        list.FillRect(all, BColor.Red);
        list.PushClip(all);
        foreach (double offset in (ReadOnlySpan<double>)[0, 1])
        {
            for (double y = 0; y < Dips; y += 2)
            {
                for (double x = offset - 2; x < Dips; x += 2)
                    list.DrawImage(image, new BRect(0, 0, 2, 2), new BRect(x, y, 2, 2), 1.0, sampling);
            }
        }
        list.PopClip();

        int size = (int)(Dips * DpiScale);
        return renderer.RenderToImage(
            list,
            new BSurfaceDescriptor(new BSize(size, size), DpiScale),
            new BFrameContext(BColor.White));
    }
}
