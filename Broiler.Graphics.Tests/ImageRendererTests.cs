using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

internal static class ImageRendererTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Image renderer returns filled pixels", RenderToImageFillsPixels));
        tests.Add(("Image renderer draws uploaded images", RenderDrawsUploadedImages));
        tests.Add(("Image renderer respects clips and transforms", RenderRespectsClipsAndTransforms));
        tests.Add(("Image renderer text fallback emits pixels", RenderTextFallbackEmitsPixels));
    }

    private static void RenderToImageFillsPixels()
    {
        using var renderer = new BImageRenderer();
        var list = new BRenderList();
        list.FillRect(new BRect(2, 2, 4, 4), BColor.Red);

        using BBitmap bitmap = renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(10, 10)),
            new BFrameContext(BColor.White));

        AssertEx.AreEqual(10, bitmap.Width);
        AssertEx.AreEqual(10, bitmap.Height);
        AssertEx.AreEqual(BColor.Red, bitmap.GetPixel(3, 3));
        AssertEx.AreEqual(BColor.White, bitmap.GetPixel(0, 0));
    }

    private static void RenderDrawsUploadedImages()
    {
        using var renderer = new BImageRenderer();
        BImageHandle image = renderer.CreateImage(new BPixelBuffer(1, 1, [0, 0, 255, 255]));
        var list = new BRenderList();
        list.DrawImage(image, new BRect(0, 0, 1, 1), new BRect(1, 1, 2, 2));

        using BBitmap bitmap = renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(4, 4)),
            new BFrameContext(BColor.White));

        AssertEx.AreEqual(BColor.Blue, bitmap.GetPixel(1, 1));
        AssertEx.AreEqual(BColor.Blue, bitmap.GetPixel(2, 2));
        renderer.ReleaseImage(image);
    }

    private static void RenderRespectsClipsAndTransforms()
    {
        using var renderer = new BImageRenderer();
        var list = new BRenderList();
        list.PushClip(new BRect(4, 4, 3, 3));
        list.PushTransform(BMatrix3x2.Translation(3, 3));
        list.FillRect(new BRect(0, 0, 4, 4), BColor.Green);
        list.PopTransform();
        list.PopClip();

        using BBitmap bitmap = renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(10, 10)),
            new BFrameContext(BColor.Transparent));

        AssertEx.AreEqual(BColor.Transparent, bitmap.GetPixel(3, 3));
        AssertEx.AreEqual(BColor.Green, bitmap.GetPixel(4, 4));
        AssertEx.AreEqual(BColor.Transparent, bitmap.GetPixel(7, 7));
    }

    private static void RenderTextFallbackEmitsPixels()
    {
        using var renderer = new BImageRenderer();
        var list = new BRenderList();
        list.DrawText(new BTextRun("OK", new BFontStyle("sans-serif", 14), BColor.Black), new BPoint(1, 1));

        using BBitmap bitmap = renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(30, 20)),
            new BFrameContext(BColor.White));

        bool foundInk = false;
        for (int y = 0; y < bitmap.Height && !foundInk; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) == BColor.Black)
                {
                    foundInk = true;
                    break;
                }
            }
        }

        AssertEx.IsTrue(foundInk, "Expected fallback text drawing to emit at least one black pixel.");
    }
}
