using System;
using System.Collections.Generic;
using System.Drawing;

namespace Broiler.Graphics.Tests;

internal static class BitmapCanvasTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("BBitmap stores and copies RGBA pixels", BitmapStoresPixels));
        tests.Add(("BCanvas fills and clips rectangles", CanvasFillRespectsClip));
        tests.Add(("BCanvas scales draws about the origin", CanvasScaleMagnifiesDraws));
        tests.Add(("BCanvas composites opacity layers", CanvasOpacityLayerComposites));
        tests.Add(("BCanvas fills gradients", CanvasGradientFills));
        tests.Add(("BCanvas draws bitmap regions", CanvasDrawsBitmapRegions));
        tests.Add(("BCanvas rounds one corner without squaring the rest", CanvasRoundsASingleCorner));
    }

    private static void BitmapStoresPixels()
    {
        using var bitmap = new BBitmap(2, 1);
        bitmap.SetPixel(0, 0, BColor.Red);
        bitmap.SetPixel(1, 0, new BColor(1, 2, 3, 4));

        AssertEx.AreEqual(BColor.Red, bitmap.GetPixel(0, 0));
        AssertEx.AreEqual(new BColor(1, 2, 3, 4), bitmap.GetPixel(1, 0));

        using BBitmap copy = bitmap.Copy();
        copy.SetPixel(0, 0, BColor.Blue);

        AssertEx.AreEqual(BColor.Red, bitmap.GetPixel(0, 0));
        AssertEx.AreEqual(BColor.Blue, copy.GetPixel(0, 0));
    }

    private static void CanvasFillRespectsClip()
    {
        using var bitmap = new BBitmap(4, 4);
        using BCanvas canvas = bitmap.OpenCanvas();

        canvas.PushClip(new RectangleF(1, 1, 2, 2));
        canvas.FillRect(new RectangleF(0, 0, 4, 4), BColor.Green);
        canvas.PopClip();

        AssertEx.AreEqual(BColor.Transparent, bitmap.GetPixel(0, 0));
        AssertEx.AreEqual(BColor.Green, bitmap.GetPixel(1, 1));
        AssertEx.AreEqual(BColor.Green, bitmap.GetPixel(2, 2));
        AssertEx.AreEqual(BColor.Transparent, bitmap.GetPixel(3, 3));
    }

    private static void CanvasScaleMagnifiesDraws()
    {
        using var bitmap = new BBitmap(8, 8);
        using BCanvas canvas = bitmap.OpenCanvas();

        // A uniform 2× scale maps the layout rect (1,1,2,2) to device (2,2,4,4): pixels x,y in [2,5].
        canvas.Save();
        canvas.Scale(2f);
        canvas.FillRect(new RectangleF(1, 1, 2, 2), BColor.Green);
        canvas.Restore();

        AssertEx.AreEqual(BColor.Transparent, bitmap.GetPixel(1, 1)); // above/left of the scaled rect
        AssertEx.AreEqual(BColor.Green, bitmap.GetPixel(2, 2));       // scaled top-left
        AssertEx.AreEqual(BColor.Green, bitmap.GetPixel(5, 5));       // scaled bottom-right
        AssertEx.AreEqual(BColor.Transparent, bitmap.GetPixel(6, 6)); // just past the scaled rect

        // Restore() returned the scale to 1, so a subsequent draw is unscaled again.
        canvas.FillRect(new RectangleF(0, 0, 1, 1), BColor.Blue);
        AssertEx.AreEqual(BColor.Blue, bitmap.GetPixel(0, 0));
    }

    private static void CanvasOpacityLayerComposites()
    {
        using var bitmap = new BBitmap(1, 1);
        bitmap.Clear(BColor.White);

        using BCanvas canvas = bitmap.OpenCanvas();
        canvas.SaveOpacityLayer(0.5f);
        canvas.FillRect(new RectangleF(0, 0, 1, 1), BColor.Black);
        canvas.RestoreOpacityLayer();

        BColor pixel = bitmap.GetPixel(0, 0);
        AssertEx.AreEqual(255, pixel.A);
        AssertEx.IsTrue(pixel.R is >= 126 and <= 128, $"Expected half-gray red channel, got {pixel.R}.");
        AssertEx.IsTrue(pixel.G is >= 126 and <= 128, $"Expected half-gray green channel, got {pixel.G}.");
        AssertEx.IsTrue(pixel.B is >= 126 and <= 128, $"Expected half-gray blue channel, got {pixel.B}.");
    }

    private static void CanvasGradientFills()
    {
        using var bitmap = new BBitmap(3, 1);
        using BCanvas canvas = bitmap.OpenCanvas();

        canvas.FillLinearGradientRect(
            new RectangleF(0, 0, 3, 1),
            new[] { BColor.Black, BColor.White },
            new[] { 0f, 1f },
            90f);

        AssertEx.IsTrue(bitmap.GetPixel(0, 0).R < bitmap.GetPixel(2, 0).R);
    }

    private static void CanvasDrawsBitmapRegions()
    {
        using var source = new BBitmap(2, 1);
        source.SetPixel(0, 0, BColor.Red);
        source.SetPixel(1, 0, BColor.Blue);

        using var destination = new BBitmap(2, 1);
        using BCanvas canvas = destination.OpenCanvas();
        canvas.DrawBitmap(source, new RectangleF(0, 0, 2, 1), new RectangleF(0, 0, 2, 1));

        AssertEx.AreEqual(BColor.Red, destination.GetPixel(0, 0));
        AssertEx.AreEqual(BColor.Blue, destination.GetPixel(1, 0));
    }

    /// <summary>
    /// A rounded clip with only one non-zero corner must cut that corner and leave the other three
    /// square. The containment test used to answer "in the band between two opposing radii?", and a
    /// band spans the whole box once its opposing corner is square — so a lone rounded corner
    /// clipped nothing at all and the shape came out a plain rectangle.
    /// </summary>
    private static void CanvasRoundsASingleCorner()
    {
        using var bitmap = new BBitmap(10, 10);
        using BCanvas canvas = bitmap.OpenCanvas();

        // Top-left corner rounded by 6px; the other three square.
        canvas.PushClipRounded(new RectangleF(0, 0, 10, 10), 6, 6, 0, 0, 0, 0, 0, 0);
        canvas.FillRect(new RectangleF(0, 0, 10, 10), BColor.Green);
        canvas.PopClip();

        // (0,0) is 8.0 from the corner arc's centre (6,6) — outside it, so cut away.
        AssertEx.AreEqual(BColor.Transparent, bitmap.GetPixel(0, 0));

        // The three square corners, and the middle, all survive.
        AssertEx.AreEqual(BColor.Green, bitmap.GetPixel(9, 0));
        AssertEx.AreEqual(BColor.Green, bitmap.GetPixel(9, 9));
        AssertEx.AreEqual(BColor.Green, bitmap.GetPixel(0, 9));
        AssertEx.AreEqual(BColor.Green, bitmap.GetPixel(5, 5));
    }
}
