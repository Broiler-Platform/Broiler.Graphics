using static Broiler.Native.Windows.WindowNative;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using Broiler.Graphics.Windowing;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Windows.Tests;

internal static class BackendExperienceTests
{
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmDpiChanged = 0x02E0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT PtReserved;
        public POINT PtMaxSize;
        public POINT PtMaxPosition;
        public POINT PtMinTrackSize;
        public POINT PtMaxTrackSize;
    }

    public static void Register(ICollection<(string Name, Action Body)> tests)
    {
        tests.Add(("Direct2D solid brush cache reuses brushes for matching colors", BrushCachingReusesBrushes));
        tests.Add(("DirectWrite text format cache reuses formats for matching fonts", TextFormatCachingReusesFormats));
        tests.Add(("DirectWrite metrics provider caches text advance measurements", MetricsProviderCachesAdvance));
        tests.Add(("Window enforces minimum client size via WM_GETMINMAXINFO", WindowEnforcesMinClientSize));
        tests.Add(("Window applies suggested rectangle on WM_DPICHANGED", WindowAppliesDpiSuggestedRect));
        tests.Add(("Window tracks frame diagnostics and triggers FrameRendered event", WindowTracksFrameDiagnostics));
    }

    private static void BrushCachingReusesBrushes()
    {
        using var renderer = new Direct2DRenderer();
        var descriptor = new BSurfaceDescriptor(new BSize(100, 100), 1.0);
        using var surface = renderer.CreateSurface(descriptor);

        var list = new BRenderList();
        // 5 rectangles with the same color
        for (int i = 0; i < 5; i++)
            list.FillRect(new BRect(i * 10, 0, 10, 10), BColor.Red);

        // 3 rectangles with blue
        for (int i = 0; i < 3; i++)
            list.FillRect(new BRect(0, i * 10, 10, 10), BColor.Blue);

        renderer.Render(surface, list, new BFrameContext(BColor.White, 0, BRenderOptions.Default));

        Assert.True(renderer.CachedBrushCount == 2, $"Expected 2 cached brushes, but got {renderer.CachedBrushCount}");
        Assert.True(renderer.BrushCacheHits == 6, $"Expected 6 brush cache hits, but got {renderer.BrushCacheHits}");
        Assert.True(renderer.BrushCacheMisses == 2, $"Expected 2 brush cache misses, but got {renderer.BrushCacheMisses}");
    }

    private static void TextFormatCachingReusesFormats()
    {
        using var renderer = new Direct2DRenderer();
        var descriptor = new BSurfaceDescriptor(new BSize(200, 200), 1.0);
        using var surface = renderer.CreateSurface(descriptor);

        var font = new BFontStyle("sans-serif", 14.0);
        var list = new BRenderList();
        list.DrawText(new BTextRun("First line", font, BColor.Black), new BPoint(0, 0));
        list.DrawText(new BTextRun("Second line", font, BColor.Black), new BPoint(0, 20));
        list.DrawText(new BTextRun("Third line", font, BColor.Black), new BPoint(0, 40));

        renderer.Render(surface, list, new BFrameContext(BColor.White, 0, BRenderOptions.Default));

        Assert.True(renderer.CachedTextFormatCount == 1, $"Expected 1 cached format, got {renderer.CachedTextFormatCount}");
        Assert.True(renderer.TextFormatCacheHits == 2, $"Expected 2 format cache hits, got {renderer.TextFormatCacheHits}");
        Assert.True(renderer.TextFormatCacheMisses == 1, $"Expected 1 format cache miss, got {renderer.TextFormatCacheMisses}");
    }

    private static void MetricsProviderCachesAdvance()
    {
        var font = new BFontStyle("sans-serif", 16.0);
        string text = "Broiler.Mail Experience Test String";

        double first = BTextMeasurer.MeasureAdvance(text, font);
        Assert.True(first > 0, "Measured advance should be positive");

        double second = BTextMeasurer.MeasureAdvance(text, font);
        Assert.AreEqual(first, second, "Repeated measurement should be identical");
    }

    private static void WindowEnforcesMinClientSize()
    {
        using var window = new DiagnosticsTestWindow(minWidth: 640, minHeight: 480);
        window.Show();

        var info = new MINMAXINFO();
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MINMAXINFO>());
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            SendMessage(window.NativeHandle, WmGetMinMaxInfo, IntPtr.Zero, ptr);
            MINMAXINFO result = Marshal.PtrToStructure<MINMAXINFO>(ptr);

            Assert.True(result.PtMinTrackSize.X >= 640, $"Expected MinTrackSize.X >= 640, got {result.PtMinTrackSize.X}");
            Assert.True(result.PtMinTrackSize.Y >= 480, $"Expected MinTrackSize.Y >= 480, got {result.PtMinTrackSize.Y}");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void WindowAppliesDpiSuggestedRect()
    {
        using var window = new DiagnosticsTestWindow(minWidth: 400, minHeight: 300);
        window.Show();

        var suggested = new RECT(-3500, -3500, -3000, -3100);
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
        try
        {
            Marshal.StructureToPtr(suggested, ptr, false);
            // Send WM_DPICHANGED with 144 DPI (150%)
            IntPtr dpiWparam = new IntPtr((144 << 16) | 144);
            SendMessage(window.NativeHandle, WmDpiChanged, dpiWparam, ptr);

            GetWindowRect(window.NativeHandle, out RECT actual);
            Assert.AreEqual(suggested.Width, actual.Width, "Window width should match suggested DPI rect width");
            Assert.AreEqual(suggested.Height, actual.Height, "Window height should match suggested DPI rect height");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void WindowTracksFrameDiagnostics()
    {
        using var window = new DiagnosticsTestWindow(minWidth: 400, minHeight: 300);
        long recordedFrame = -1;
        TimeSpan recordedDuration = TimeSpan.Zero;
        window.FrameRendered += (_, e) =>
        {
            recordedFrame = e.FrameIndex;
            recordedDuration = e.Duration;
        };

        window.Show();
        // Invalidate to trigger a frame
        window.Invalidate();
        SendMessage(window.NativeHandle, 0x000F /* WM_PAINT */, IntPtr.Zero, IntPtr.Zero);

        Assert.True(window.FrameCount > 0, "Window should have rendered at least one frame");
        Assert.True(recordedFrame >= 0, "FrameRendered event should have fired with valid frame index");
        Assert.True(window.LastRenderDuration >= TimeSpan.Zero, "LastRenderDuration should be non-negative");
    }

    private sealed class DiagnosticsTestWindow(int minWidth, int minHeight) : Direct2DWindow(new BWindowOptions
    {
        Title = "Broiler diagnostics probe",
        ClientWidth = minWidth,
        ClientHeight = minHeight,
        MinClientWidth = minWidth,
        MinClientHeight = minHeight,
        Left = -4000,
        Top = -4000,
        OwnsMessageLoop = false,
    })
    {
        protected override BRenderList? BuildRenderList(BSize clientSize)
        {
            var list = new BRenderList();
            list.FillRect(new BRect(0, 0, clientSize.Width, clientSize.Height), BColor.White);
            return list;
        }
    }
}
