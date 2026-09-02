using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Coverage for the window text a <see cref="Direct2DWindow"/> carries.
/// </summary>
/// <remarks>
/// This exists because the caption was silently empty on every Broiler window for as long as the
/// backend had one. <c>WM_NCCREATE</c> answered TRUE directly, so <c>DefWindowProc</c> never saw
/// the message — and that is what copies <c>CREATESTRUCT.lpszName</c> into the window text. The
/// window looked correct in every other respect, which is exactly why nothing caught it: only the
/// title bar and the Alt+Tab entry were wrong, and no test read either. Reading the caption back
/// out of the OS is the only assertion that would have.
/// </remarks>
internal static class WindowCaptionTests
{
    public static void Register(ICollection<(string Name, Action Body)> tests)
    {
        tests.Add(("Window carries the title it was created with", CarriesCreatedTitle));
        tests.Add(("Window takes a title set after it exists", TakesRetitle));
    }

    private static void CarriesCreatedTitle()
    {
        using var window = new CaptionProbeWindow(CreateOptions("Broiler caption probe"));
        window.Show();

        Assert.AreEqual("Broiler caption probe", ReadCaption(window.NativeHandle));
    }

    private static void TakesRetitle()
    {
        using var window = new CaptionProbeWindow(CreateOptions("first"));
        window.Show();
        window.SetTitle("second");

        Assert.AreEqual("second", ReadCaption(window.NativeHandle));
    }

    /// <summary>
    /// Off-screen and unowned, and explicitly not the loop owner: <see cref="BWindow.Show"/>
    /// realizes the window without blocking, and disposing it must not post a quit to the runner.
    /// </summary>
    private static BWindowOptions CreateOptions(string title) =>
        new()
        {
            Title = title,
            ClientWidth = 200,
            ClientHeight = 120,
            Left = -4000,
            Top = -4000,
            OwnsMessageLoop = false,
        };

    private static string ReadCaption(IntPtr hwnd)
    {
        Assert.True(hwnd != IntPtr.Zero, "Expected the window to have been realized.");

        var text = new StringBuilder(512);
        _ = GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    /// <summary>The smallest concrete window there is: it draws nothing and answers no input.</summary>
    private sealed class CaptionProbeWindow(BWindowOptions options) : Direct2DWindow(options)
    {
        protected override BRenderList? BuildRenderList(BSize clientSize) => null;
    }
}
