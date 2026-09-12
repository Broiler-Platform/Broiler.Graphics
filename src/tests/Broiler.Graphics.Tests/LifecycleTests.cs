using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>Exercises production CPU renderer and surface lifetimes.</summary>
internal static class LifecycleTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Disposed CPU surface rejects access and replay", SurfaceThrowsAfterDispose));
        tests.Add(("CPU renderer leaves caller-owned surface alive", CallerOwnsSurface));
        tests.Add(("CPU renderer disposal is idempotent and rejects operations", RendererDisposal));
        tests.Add(("CPU renderer rejects incompatible surface", RenderRejectsForeignSurface));
        tests.Add(("Failed CPU resize preserves pixels and descriptor", FailedResizePreservesSurface));
        tests.Add(("Successful CPU resize replaces bitmap", ResizeReplacesBitmap));
        tests.Add(("RenderToImage result survives subsequent renders and disposal", RenderedImageIsOwnedByCaller));
    }

    private static readonly BSurfaceDescriptor Descriptor = BSurfaceDescriptor.Default(new BSize(10, 10));

    private static void SurfaceThrowsAfterDispose()
    {
        using var renderer = new BImageRenderer();
        var surface = (BImageSurface)renderer.CreateSurface(Descriptor);
        BBitmap pixels = surface.Bitmap;
        surface.Dispose();
        surface.Dispose();
        AssertEx.Throws<ObjectDisposedException>(() => surface.Resize(new BSize(5, 5), 1));
        AssertEx.Throws<ObjectDisposedException>(() => _ = surface.Bitmap);
        AssertEx.Throws<ObjectDisposedException>(() => pixels.GetPixel(0, 0));
        AssertEx.Throws<ObjectDisposedException>(() => renderer.Render(surface, new BRenderList(), BFrameContext.Default));
    }

    private static void CallerOwnsSurface()
    {
        var renderer = new BImageRenderer();
        using var surface = (BImageSurface)renderer.CreateSurface(Descriptor);
        renderer.Dispose();
        surface.Bitmap.SetPixel(0, 0, BColor.Red);
        AssertEx.AreEqual(BColor.Red, surface.Bitmap.GetPixel(0, 0));
        surface.Resize(new BSize(5, 5), 2);
        AssertEx.AreEqual(10, surface.Bitmap.Width);
    }

    private static void RendererDisposal()
    {
        var renderer = new BImageRenderer();
        using var surface = new BImageSurface(Descriptor);
        var pixels = new BPixelBuffer(1, 1, [255, 0, 0, 255]);
        var image = renderer.CreateImage(pixels);
        renderer.Dispose();
        renderer.Dispose();
        AssertEx.Throws<ObjectDisposedException>(() => renderer.CreateSurface(Descriptor));
        AssertEx.Throws<ObjectDisposedException>(() => renderer.CreateImage(pixels));
        AssertEx.Throws<ObjectDisposedException>(() => renderer.ReleaseImage(image));
        AssertEx.Throws<ObjectDisposedException>(() => renderer.Render(surface, new BRenderList(), BFrameContext.Default));
        AssertEx.Throws<ObjectDisposedException>(() => renderer.RenderToImage(new BRenderList(), Descriptor, BFrameContext.Default));
    }

    private static void RenderRejectsForeignSurface()
    {
        using var renderer = new BImageRenderer();
        using var incompatible = new IncompatibleSurface();
        AssertEx.Throws<ArgumentException>(() => renderer.Render(incompatible, new BRenderList(), BFrameContext.Default));
    }

    private static void FailedResizePreservesSurface()
    {
        using var surface = new BImageSurface(Descriptor);
        BBitmap original = surface.Bitmap;
        original.SetPixel(0, 0, BColor.Red);
        AssertEx.Throws<ArgumentOutOfRangeException>(() => surface.Resize(new BSize(double.MaxValue, 10), 2));
        AssertEx.AreEqual(Descriptor.Size, surface.Size);
        AssertEx.AreEqual(Descriptor.DpiScale, surface.DpiScale);
        AssertEx.IsTrue(ReferenceEquals(original, surface.Bitmap));
        AssertEx.AreEqual(BColor.Red, original.GetPixel(0, 0));
        // Dimensions fit individually, but their RGBA byte count overflows before allocation.
        AssertEx.Throws<OverflowException>(() => surface.Resize(new BSize(50_000, 50_000), 1));
        AssertEx.AreEqual(BColor.Red, surface.Bitmap.GetPixel(0, 0));
        using var renderer = new BImageRenderer();
        renderer.Render(surface, new BRenderList(), new BFrameContext(BColor.Blue));
        AssertEx.AreEqual(BColor.Blue, surface.Bitmap.GetPixel(0, 0));
    }

    private static void ResizeReplacesBitmap()
    {
        using var surface = new BImageSurface(Descriptor);
        BBitmap original = surface.Bitmap;
        surface.Resize(new BSize(3, 4), 2);
        AssertEx.AreEqual(6, surface.Bitmap.Width);
        AssertEx.AreEqual(8, surface.Bitmap.Height);
        AssertEx.Throws<ObjectDisposedException>(() => original.GetPixel(0, 0));
    }

    private static void RenderedImageIsOwnedByCaller()
    {
        var renderer = new BImageRenderer();
        using BBitmap first = renderer.RenderToImage(new BRenderList(), Descriptor, new BFrameContext(BColor.Red));
        using BBitmap second = renderer.RenderToImage(new BRenderList(), Descriptor, new BFrameContext(BColor.Blue));
        renderer.Dispose();
        AssertEx.AreEqual(BColor.Red, first.GetPixel(0, 0));
        AssertEx.AreEqual(BColor.Blue, second.GetPixel(0, 0));
    }

    private sealed class IncompatibleSurface : IBroilerSurface
    {
        public BSize Size => new(10, 10);
        public double DpiScale => 1;
        public void Resize(BSize size, double dpiScale) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
