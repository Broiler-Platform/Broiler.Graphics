using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Coverage for the frame a resize is required to produce before it returns.
/// </summary>
/// <remarks>
/// A window that only invalidates on WM_SIZE looks correct in every screenshot taken at rest and
/// wrong for the whole of a drag: Windows runs the drag in a modal loop inside DefWindowProc where
/// WM_PAINT is the lowest-priority message, so the posted paint starves behind the move stream and
/// the compositor keeps showing the last frame that was presented - at the size it was drawn for.
///
/// The assertion has to be about ordering, not about pixels. MoveWindow delivers WM_SIZE with
/// SendMessage semantics, so it does not return until the window procedure has handled it: if a
/// frame is drawn synchronously, the render count has already gone up by the time MoveWindow
/// returns, and if the handler only invalidates, it has not.
/// </remarks>
internal static class WindowResizePaintTests
{
    public static void Register(ICollection<(string Name, Action Body)> tests)
    {
        tests.Add(("Resize draws a frame before it returns", ResizePaintsSynchronously));
        tests.Add(("Resize draws the frame at the new size", ResizePaintsAtNewSize));
        tests.Add(("A resize to the same size still reports that size", ResizeToSameSizeIsStable));
    }

    private static void ResizePaintsSynchronously()
    {
        using var window = new CountingWindow();
        window.Show();

        int before = window.RenderCount;
        Resize(window, 420, 260);

        Assert.True(
            window.RenderCount > before,
            $"Expected a resize to draw before returning; render count stayed at {before}.");
    }

    private static void ResizePaintsAtNewSize()
    {
        using var window = new CountingWindow();
        window.Show();

        Resize(window, 480, 300);

        // The size the frame was built for, not the size the window reports afterwards: a frame
        // drawn for the old extent and stretched by the compositor is exactly the defect.
        Assert.True(
            window.LastRenderSize.Width > 0 && window.LastRenderSize.Height > 0,
            "Expected the resize to have produced a frame at all.");
        AssertClose(window.ClientSize.Width, window.LastRenderSize.Width, "width");
        AssertClose(window.ClientSize.Height, window.LastRenderSize.Height, "height");
    }

    private static void ResizeToSameSizeIsStable()
    {
        using var window = new CountingWindow();
        window.Show();

        Resize(window, 400, 240);
        BSize afterFirst = window.ClientSize;
        Resize(window, 400, 240);

        AssertClose(afterFirst.Width, window.ClientSize.Width, "width");
        AssertClose(afterFirst.Height, window.ClientSize.Height, "height");
    }

    /// <summary>
    /// MoveWindow rather than SetWindowPos: it is the simplest call that delivers WM_SIZE
    /// synchronously, which is the whole point of the assertion.
    /// </summary>
    private static void Resize(CountingWindow window, int width, int height)
    {
        Assert.True(window.NativeHandle != IntPtr.Zero, "Expected the window to have been realized.");
        Assert.True(
            MoveWindow(window.NativeHandle, -4000, -4000, width, height, bRepaint: true),
            "MoveWindow failed.");
    }

    private static void AssertClose(double expected, double actual, string what)
    {
        // Client extent is reported in DIPs, so a scaled display costs a fraction of a pixel.
        if (Math.Abs(expected - actual) > 1.0)
            throw new AssertException($"Expected the frame {what} to match the client {what}: {expected} vs {actual}.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool bRepaint);

    /// <summary>Records how often, and at what size, the window asked for a frame.</summary>
    private sealed class CountingWindow() : Direct2DWindow(new BWindowOptions
    {
        Title = "Broiler resize probe",
        ClientWidth = 320,
        ClientHeight = 200,
        Left = -4000,
        Top = -4000,
        OwnsMessageLoop = false,
    })
    {
        public int RenderCount { get; private set; }

        public BSize LastRenderSize { get; private set; }

        protected override BRenderList? BuildRenderList(BSize clientSize)
        {
            RenderCount++;
            LastRenderSize = clientSize;

            // A real list rather than null, so the frame goes all the way through the renderer and
            // the swap chain the way a resize during a drag would.
            var list = new BRenderList();
            list.FillRect(new BRect(0, 0, clientSize.Width, clientSize.Height), BColor.White);
            return list;
        }
    }
}
