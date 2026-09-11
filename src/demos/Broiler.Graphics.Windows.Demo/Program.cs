using static Broiler.Native.Windows.WindowNative;
using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Windows.Demo;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        _ = SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2, best effort.

        using var window = new Direct2DDemoWindow();
        return window.Run();
    }

}
