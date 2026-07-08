using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Tests for the Direct2D backend's CPU-side image path: the BGRA-premultiplied
/// conversion, the image store's handle lifecycle, and the renderer's
/// <c>CreateImage</c>/<c>ReleaseImage</c> (which decode through <see cref="BImageCodec"/>).
/// </summary>
internal static class WindowsImageTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("DirectWrite text metrics vary by font family", DirectWriteTextMetricsVaryByFontFamily));
        tests.Add(("BGRA conversion premultiplies and swaps channels", BgraConversion));
        tests.Add(("BGRA conversion zeroes fully transparent pixels", BgraConversionTransparent));
        tests.Add(("Image store add/get/remove lifecycle", StoreLifecycle));
        tests.Add(("Image store rejects unknown handles", StoreRejectsUnknown));
        tests.Add(("Renderer CreateImage decodes encoded bytes", RendererCreateImageDecodes));
        tests.Add(("Renderer CreateImage rejects garbage bytes", RendererRejectsGarbage));
        tests.Add(("Renderer CreateImage(BPixelBuffer) and release", RendererCreateFromPixels));
        tests.Add(("Renderer renders a basic Direct2D command list", RendererRendersBasicCommandList));
        tests.Add(("Renderer renders a Direct2D command list to image", RendererRendersCommandListToImage));
    }

    private static void DirectWriteTextMetricsVaryByFontFamily()
    {
        using var renderer = new Direct2DRenderer();

        double proportional = BTextMeasurer.MeasureAdvance("iiiiiiii", new BFontStyle("Segoe UI", 16));
        double monospace = BTextMeasurer.MeasureAdvance("iiiiiiii", new BFontStyle("Consolas", 16));

        Assert.True(proportional > 0, "proportional advance");
        Assert.True(monospace > 0, "monospace advance");
        Assert.True(Math.Abs(proportional - monospace) > 1, "DirectWrite metrics should preserve family-specific glyph advances.");
    }

    private static BPixelBuffer Rgba(int w, int h, byte[] data) => new(w, h, data);

    private static void BgraConversion()
    {
        // Opaque pixel is unchanged but channel order swaps R<->B; half-alpha premultiplies.
        var src = Rgba(2, 1, [200, 100, 50, 255, /*px2*/ 200, 100, 50, 128]);
        byte[] bgra = Direct2DImageStore.ToBgraPremultiplied(src);

        // px0 opaque: B=50, G=100, R=200, A=255
        Assert.AreEqual((byte)50, bgra[0], "B0");
        Assert.AreEqual((byte)100, bgra[1], "G0");
        Assert.AreEqual((byte)200, bgra[2], "R0");
        Assert.AreEqual((byte)255, bgra[3], "A0");

        // px1 a=128: B=(50*128+127)/255=25, G=(100*128+127)/255=50, R=(200*128+127)/255=100, A=128
        Assert.AreEqual((byte)25, bgra[4], "B1");
        Assert.AreEqual((byte)50, bgra[5], "G1");
        Assert.AreEqual((byte)100, bgra[6], "R1");
        Assert.AreEqual((byte)128, bgra[7], "A1");
    }

    private static void BgraConversionTransparent()
    {
        var src = Rgba(1, 1, [255, 255, 255, 0]);
        byte[] bgra = Direct2DImageStore.ToBgraPremultiplied(src);
        Assert.AreEqual((byte)0, bgra[0]);
        Assert.AreEqual((byte)0, bgra[1]);
        Assert.AreEqual((byte)0, bgra[2]);
        Assert.AreEqual((byte)0, bgra[3]);
    }

    private static void StoreLifecycle()
    {
        using var store = new Direct2DImageStore();
        BImageHandle h = store.Add(Rgba(4, 3, new byte[4 * 3 * 4]));

        Assert.True(h.IsValid, "handle valid");
        Assert.AreEqual(new BSize(4, 3), h.PixelSize, "handle carries pixel size");
        Assert.AreEqual(1, store.Count);

        Direct2DImage entry = store.Get(h);
        Assert.AreEqual(4, entry.Width);
        Assert.AreEqual(3, entry.Height);
        Assert.AreEqual(4 * 3 * 4, entry.BgraPremultiplied.Length);

        Assert.True(store.Remove(h), "remove returns true");
        Assert.AreEqual(0, store.Count);
        Assert.True(!store.Remove(h), "second remove is false");
    }

    private static void StoreRejectsUnknown()
    {
        using var store = new Direct2DImageStore();
        Assert.Throws<ArgumentException>(() => store.Get(BImageHandle.Invalid), "invalid handle");
        Assert.Throws<ArgumentException>(() => store.Get(BImageHandle.FromId(999, new BSize(1, 1))), "unknown id");
    }

    private static void RendererCreateImageDecodes()
    {
        // A real PNG produced by the managed encoder; the renderer must decode it via BImageCodec.
        var pixels = Rgba(5, 4, MakeGradient(5, 4));
        byte[] png = ManagedImageCodec.Instance.Encode(pixels, BImageEncodeFormat.Png);

        using var renderer = new Direct2DRenderer();
        BImageHandle handle = renderer.CreateImage(png);

        Assert.True(handle.IsValid, "decoded image handle is valid");
        Assert.AreEqual(new BSize(5, 4), handle.PixelSize, "handle reflects decoded size");
    }

    private static void RendererRejectsGarbage()
    {
        using var renderer = new Direct2DRenderer();
        byte[] junk = [1, 2, 3, 4, 5, 6, 7, 8];
        Assert.Throws<NotSupportedException>(() => renderer.CreateImage(junk));
    }

    private static void RendererCreateFromPixels()
    {
        using var renderer = new Direct2DRenderer();
        BImageHandle handle = renderer.CreateImage(Rgba(8, 8, new byte[8 * 8 * 4]));
        Assert.True(handle.IsValid);
        Assert.AreEqual(new BSize(8, 8), handle.PixelSize);
        renderer.ReleaseImage(handle); // must not throw
    }

    private static void RendererRendersBasicCommandList()
    {
        using var renderer = new Direct2DRenderer();
        using IBroilerSurface surface = renderer.CreateSurface(BSurfaceDescriptor.Default(new BSize(64, 64)));

        BImageHandle image = renderer.CreateImage(Rgba(2, 2, [
            255, 0, 0, 255,   0, 255, 0, 255,
            0, 0, 255, 255,   255, 255, 255, 128,
        ]));

        var list = new BRenderList();
        list.FillRect(new BRect(0, 0, 64, 64), BColor.White);
        list.StrokeRect(new BRect(4, 4, 56, 56), BColor.Blue, 2);
        list.PushClip(new BRect(8, 8, 48, 48));
        list.PushTransform(BMatrix3x2.Translation(4, 4));
        list.DrawImage(image, new BRect(0, 0, 2, 2), new BRect(8, 8, 16, 16), 0.85);
        list.DrawText(new BTextRun("D2D"), new BPoint(8, 32));
        list.PopTransform();
        list.PopClip();

        renderer.Render(surface, list, BFrameContext.Default);
        renderer.ReleaseImage(image);
    }

    private static void RendererRendersCommandListToImage()
    {
        using var renderer = new Direct2DRenderer();
        var list = new BRenderList();
        list.FillRect(new BRect(2, 2, 4, 4), BColor.Red);

        using BBitmap bitmap = renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(12, 12)),
            new BFrameContext(BColor.White));

        Assert.AreEqual(12, bitmap.Width, "bitmap width");
        Assert.AreEqual(12, bitmap.Height, "bitmap height");
        Assert.AreEqual(BColor.Red, bitmap.GetPixel(3, 3), "filled pixel");
        Assert.AreEqual(BColor.White, bitmap.GetPixel(0, 0), "background pixel");
    }

    private static byte[] MakeGradient(int w, int h)
    {
        byte[] px = new byte[w * h * 4];
        int i = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            px[i++] = (byte)(x * 20);
            px[i++] = (byte)(y * 30);
            px[i++] = (byte)(x + y);
            px[i++] = 255;
        }
        return px;
    }
}
