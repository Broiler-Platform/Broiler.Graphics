using static Broiler.Native.Windows.WindowNative;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Coverage for the icon a <see cref="Direct2DWindow"/> shows on the taskbar, in Alt+Tab, and in
/// its caption and system menu.
/// </summary>
/// <remarks>
/// An executable's <c>ApplicationIcon</c> is embedded as resource IDI_APPLICATION, which is what
/// Explorer shows. The caption and its system menu draw the window's icon instead, and the window
/// class used to be registered without one, so every Broiler window showed Windows' generic glyph
/// there whatever icon its executable carried; only the taskbar fell back to the executable's. This
/// runner carries an <c>ApplicationIcon</c> of its own so the class icon can be read back out of
/// the OS.
/// </remarks>
internal static class WindowIconTests
{
    public static void Register(ICollection<(string Name, Action Body)> tests)
    {
        tests.Add(("Window class carries the executable's icon", CarriesExecutableIcon));
        tests.Add(("An icon the app sets overrides the executable's until it is cleared", SetIconOverridesUntilCleared));
    }

    private static void CarriesExecutableIcon()
    {
        IntPtr executableIcon = LoadIcon(GetModuleHandle(null), new IntPtr(IdiApplication));
        Assert.True(executableIcon != IntPtr.Zero,
            "Run the suite from its own executable (dotnet run), which carries an ApplicationIcon.");

        using var window = new IconProbeWindow(CreateOptions());
        window.Show();

        // LoadIcon hands out one shared handle per resource, so the class holds this very one.
        Assert.AreEqual(executableIcon, GetClassLongPtr(window.NativeHandle, GclpHIcon));
        Assert.True(GetClassLongPtr(window.NativeHandle, GclpHIconSm) != IntPtr.Zero,
            "Windows should have taken the small icon from the same resource.");
    }

    private static void SetIconOverridesUntilCleared()
    {
        using var window = new IconProbeWindow(CreateOptions());
        window.Show();
        IntPtr classIcon = GetClassLongPtr(window.NativeHandle, GclpHIcon);

        window.SetIcon(SolidIcon(16));
        Assert.True(WindowIcon(window, IconBig) != IntPtr.Zero, "The app's icon should be on the window.");
        Assert.True(WindowIcon(window, IconSmall) != IntPtr.Zero, "The app's small icon should be on the window.");

        // Cleared, the window falls back to its class icon - the executable's.
        window.SetIcon(null);
        Assert.AreEqual(IntPtr.Zero, WindowIcon(window, IconBig));
        Assert.AreEqual(IntPtr.Zero, WindowIcon(window, IconSmall));
        Assert.AreEqual(classIcon, GetClassLongPtr(window.NativeHandle, GclpHIcon));
    }

    private static IntPtr WindowIcon(BWindow window, int which) =>
        SendMessage(window.NativeHandle, WmGetIcon, new IntPtr(which), IntPtr.Zero);

    private static BPixelBuffer SolidIcon(int size)
    {
        byte[] rgba = new byte[size * size * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = 0x20;
            rgba[i + 1] = 0x60;
            rgba[i + 2] = 0xC0;
            rgba[i + 3] = 0xFF;
        }

        return new BPixelBuffer(size, size, rgba);
    }

    /// <summary>
    /// Off-screen and unowned, and explicitly not the loop owner: <see cref="BWindow.Show"/>
    /// realizes the window without blocking, and disposing it must not post a quit to the runner.
    /// </summary>
    private static BWindowOptions CreateOptions() =>
        new()
        {
            Title = "Broiler icon probe",
            ClientWidth = 200,
            ClientHeight = 120,
            Left = -4000,
            Top = -4000,
            OwnsMessageLoop = false,
        };

    /// <summary>The smallest concrete window there is: it draws nothing and answers no input.</summary>
    private sealed class IconProbeWindow(BWindowOptions options) : Direct2DWindow(options)
    {
        protected override BRenderList? BuildRenderList(BSize clientSize) => null;
    }
}
