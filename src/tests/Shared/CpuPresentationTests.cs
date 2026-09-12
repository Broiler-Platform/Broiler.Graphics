using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests.Shared;

/// <summary>Behavior contracts shared by the real CPU and CPU-presentation backends.</summary>
internal static class CpuPresentationTests
{
    internal static void Register(List<(string Name, Action Body)> tests, string backend,
        Func<IBroilerRenderer> createRenderer, Func<IBroilerSurface, BBitmap> read)
    {
        tests.Add(($"{backend}: repeated frames preserve snapshots and isolate surfaces", () =>
        {
            using var renderer = createRenderer();
            var descriptor = BSurfaceDescriptor.Default(new BSize(4, 4));
            using var first = renderer.CreateSurface(descriptor);
            using var second = renderer.CreateSurface(descriptor);
            var list = new BRenderList();
            list.FillRect(new BRect(0, 0, 2, 2), BColor.Red);
            renderer.Render(first, list, new BFrameContext(BColor.White));
            using var snapshot = read(first);
            renderer.Render(second, new BRenderList(), new BFrameContext(BColor.Green));
            list.Clear();
            renderer.Render(first, list, new BFrameContext(BColor.Blue));
            using var next = read(first);
            using var other = read(second);
            Equal(BColor.Red, snapshot.GetPixel(0, 0));
            Equal(BColor.White, snapshot.GetPixel(3, 3));
            Equal(BColor.Blue, next.GetPixel(0, 0));
            Equal(BColor.Blue, next.GetPixel(3, 3));
            Equal(BColor.Green, other.GetPixel(0, 0));

            first.Resize(new BSize(3, 2), 2);
            renderer.Render(first, list, new BFrameContext(BColor.Red));
            using var resized = read(first);
            Equal(6, resized.Width);
            Equal(4, resized.Height);
            Equal(BColor.Red, resized.GetPixel(5, 3));
            first.Dispose();
            Throws<ObjectDisposedException>(() => renderer.Render(first, list, BFrameContext.Default));
        }));

        tests.Add(($"{backend}: foreign image cannot draw or release local resource", () =>
        {
            using var owner = createRenderer();
            using var other = createRenderer();
            var foreign = owner.CreateImage(new BPixelBuffer(1, 1, [255, 0, 0, 255]));
            var local = other.CreateImage(new BPixelBuffer(1, 1, [0, 0, 255, 255]));
            var descriptor = BSurfaceDescriptor.Default(new BSize(1, 1));
            using var surface = other.CreateSurface(descriptor);
            var list = new BRenderList();
            list.DrawImage(foreign, new BRect(0, 0, 1, 1), new BRect(0, 0, 1, 1));
            Throws<ArgumentException>(() => other.Render(surface, list, BFrameContext.Default));
            other.ReleaseImage(foreign);
            list.Clear();
            list.DrawImage(local, new BRect(0, 0, 1, 1), new BRect(0, 0, 1, 1));
            other.Render(surface, list, BFrameContext.Default);
            using var result = read(surface);
            Equal(BColor.Blue, result.GetPixel(0, 0));
        }));
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
