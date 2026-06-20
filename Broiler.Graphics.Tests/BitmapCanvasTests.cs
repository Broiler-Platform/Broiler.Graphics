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
        tests.Add(("BCanvas composites opacity layers", CanvasOpacityLayerComposites));
        tests.Add(("BCanvas fills gradients", CanvasGradientFills));
        tests.Add(("BCanvas draws bitmap regions", CanvasDrawsBitmapRegions));
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
}
