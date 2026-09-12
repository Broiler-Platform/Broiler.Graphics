using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.WebAssembly.Tests;

/// <summary>
/// Pixel conformance between the CPU oracle (<c>BImageRenderer</c>) and the planned
/// direct-Canvas op stream replayed through <see cref="Canvas2DReferenceRasterizer"/>,
/// for the coordinate-exact subset (solid fills, rectangular clips, transforms, nearest
/// image blit) on integer device coordinates. This is the "backend command-coverage suite
/// shared with the CPU oracle" for the part that is deterministic off-browser; rounded/stroke/
/// text antialiasing and translucent compositing are covered by the browser-runtime gate.
/// </summary>
internal static class OracleConformanceTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Direct-canvas plan matches CPU oracle at DPR 1", () => RunScene(40, 30, 1.0)));
        tests.Add(("Direct-canvas plan matches CPU oracle at DPR 2", () => RunScene(20, 15, 2.0)));
        tests.Add(("Clip/transform interleaving matches CPU oracle", RunInterleaving));
        tests.Add(("Rotated and sheared fills use pixel-exact CPU fallback", TransformedFallbackMatchesOracle));
        tests.Add(("CPU fallback respects explicit backing dimensions", FallbackUsesBackingDimensions));
    }

    private static readonly byte[] CheckerRgba =
    [
        0xFF, 0xC1, 0x07, 0xFF,
        0x16, 0x6F, 0xB7, 0xFF,
        0x16, 0x6F, 0xB7, 0xFF,
        0xFF, 0xC1, 0x07, 0xFF,
    ];

    private static void RunScene(int logicalWidth, int logicalHeight, double dpr)
    {
        BColor clear = BColor.White;

        using var cpu = new BImageRenderer();
        BImageHandle checker = cpu.CreateImage(new BPixelBuffer(2, 2, (byte[])CheckerRgba.Clone()));

        BRenderList scene = BuildScene(checker);

        int backingWidth = (int)Math.Ceiling(logicalWidth * dpr);
        int backingHeight = (int)Math.Ceiling(logicalHeight * dpr);

        byte[] oracle = RenderOracle(cpu, scene, logicalWidth, logicalHeight, dpr, clear);

        var planner = new CanvasFramePlanner();
        CanvasFrame frame = planner.Plan(scene, backingWidth, backingHeight, dpr, clear);

        var reference = new Canvas2DReferenceRasterizer(backingWidth, backingHeight, clear.ToArgb());
        reference.RegisterImage(checker.Handle.Id, 2, 2, CheckerRgba);
        reference.Replay(frame);

        AssertBitmapsEqual(oracle, reference.Rgba, backingWidth, backingHeight);
    }

    private static void RunInterleaving()
    {
        BColor clear = BColor.White;
        using var cpu = new BImageRenderer();

        var scene = new BRenderList();
        // PushClip A, PushTransform T, PopClip, draw (clip gone, transform survives), PopTransform.
        scene.PushClip(new BRect(0, 0, 8, 8));
        scene.PushTransform(BMatrix3x2.Translation(20, 10));
        scene.PopClip();
        scene.FillRect(new BRect(0, 0, 6, 6), BColor.Red);
        scene.PopTransform();
        // A second clipped fill to exercise a real clip after the interleaving.
        scene.PushClip(new BRect(5, 5, 10, 10));
        scene.FillRect(new BRect(0, 0, 40, 40), BColor.Blue);
        scene.PopClip();
        scene.Validate();

        byte[] oracle = RenderOracle(cpu, scene, 40, 30, 1.0, clear);

        var frame = new CanvasFramePlanner().Plan(scene, 40, 30, 1.0, clear);
        var reference = new Canvas2DReferenceRasterizer(40, 30, clear.ToArgb());
        reference.Replay(frame);

        AssertBitmapsEqual(oracle, reference.Rgba, 40, 30);
    }

    private static BRenderList BuildScene(BImageHandle checker)
    {
        var scene = new BRenderList();
        scene.FillRect(new BRect(5, 5, 10, 8), BColor.Red);

        scene.PushClip(new BRect(0, 0, 20, 20));
        scene.FillRect(new BRect(10, 10, 20, 20), BColor.Blue);
        scene.PopClip();

        scene.PushTransform(BMatrix3x2.Translation(15, 2));
        scene.FillRect(new BRect(0, 0, 6, 6), BColor.Green);
        scene.PushClip(new BRect(2, 2, 10, 10));
        scene.FillRect(new BRect(0, 0, 30, 30), BColor.Black);
        scene.PopClip();
        scene.PopTransform();

        scene.PushTransform(BMatrix3x2.Scale(2, 2));
        scene.FillRect(new BRect(2, 2, 3, 3), new BColor(128, 0, 128));
        scene.PopTransform();

        scene.DrawImage(checker, new BRect(0, 0, 2, 2), new BRect(24, 20, 4, 4), 1.0);
        scene.Validate();
        return scene;
    }

    private static byte[] RenderOracle(
        BImageRenderer cpu,
        BRenderList scene,
        int logicalWidth,
        int logicalHeight,
        double dpr,
        BColor clear)
    {
        using var surface = (BImageSurface)cpu.CreateSurface(
            new BSurfaceDescriptor(new BSize(logicalWidth, logicalHeight), dpr, BPixelFormat.Rgba8, EnableTransparency: false));
        cpu.Render(surface, scene, new BFrameContext(clear));
        return (byte[])surface.Bitmap.ToPixelBuffer(copy: false).Rgba.Clone();
    }

    private static void AssertBitmapsEqual(byte[] oracle, byte[] actual, int width, int height)
    {
        if (oracle.Length != actual.Length)
            throw new AssertException($"Buffer length mismatch: oracle {oracle.Length}, actual {actual.Length}.");

        for (int i = 0; i < oracle.Length; i++)
        {
            if (oracle[i] == actual[i])
                continue;

            int pixel = i / 4;
            int channel = i % 4;
            int x = pixel % width;
            int y = pixel / width;
            throw new AssertException(
                $"Pixel ({x},{y}) channel {channel} differs: oracle {oracle[i]}, actual {actual[i]} (size {width}x{height}).");
        }
    }
    private static void TransformedFallbackMatchesOracle()
    {
        using var renderer = new BImageRenderer();
        using var fallback = new CanvasCpuFallback();
        BMatrix3x2[] transforms = [new(1, 1, -1, 1, 10, 0), new(1, 0, 0.5, 1, 0, 0)];
        foreach (var transform in transforms)
        {
            var scene = new BRenderList();
            scene.PushClip(new BRect(0, 0, 15, 15));
            scene.PushTransform(transform);
            scene.FillRect(new BRect(0, 0, 4, 4), BColor.Red);
            scene.PopTransform();
            scene.PopClip();
            scene.FillRect(new BRect(18, 18, 2, 2), BColor.Blue);
            AssertEx.IsTrue(new CanvasFramePlanner().Plan(scene, 20, 20, 1, BColor.White).RequiresCpuFallback);
            BBitmap actual = fallback.Render(renderer, scene, 20, 20, 1, 20, 20, BFrameContext.Default);
            byte[] oracle = RenderOracle(renderer, scene, 20, 20, 1, BColor.White);
            AssertBitmapsEqual(oracle, actual.CopyRgba(), 20, 20);
            AssertEx.AreEqual(BColor.White, actual.GetPixel(6, 0), "The enclosing box must remain unpainted.");
            AssertEx.AreEqual(BColor.Blue, actual.GetPixel(19, 19), "Fallback must replay the whole frame.");
        }
    }

    private static void FallbackUsesBackingDimensions()
    {
        using var renderer = new BImageRenderer();
        using var fallback = new CanvasCpuFallback();
        var scene = new BRenderList();
        // round(10 * 1.25) = 12; ceil(10 * 1.25) = 13. The browser supplies 12.
        BBitmap frame = fallback.Render(renderer, scene, 12, 12, 1.25, 10, 10, new BFrameContext(BColor.Red));
        AssertEx.AreEqual(12 * 12 * 4, frame.Rgba.Length);
        AssertEx.AreEqual(BColor.Red, frame.GetPixel(11, 11));
        BBitmap reused = fallback.Render(renderer, scene, 12, 12, 1.25, 10, 10, new BFrameContext(BColor.Blue));
        AssertEx.IsTrue(ReferenceEquals(frame, reused));
        AssertEx.AreEqual(BColor.Blue, reused.GetPixel(11, 11));
        BBitmap resized = fallback.Render(renderer, scene, 13, 13, 1.25, 10, 10, BFrameContext.Default);
        AssertEx.AreEqual(13 * 13 * 4, resized.Rgba.Length);
        AssertEx.Throws<ObjectDisposedException>(() => frame.GetPixel(0, 0));
    }

}
