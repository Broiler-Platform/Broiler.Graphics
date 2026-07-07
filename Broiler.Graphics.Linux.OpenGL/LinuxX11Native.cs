using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Linux.OpenGL;

internal static class LinuxX11Native
{
    public const int False = 0;

    public const int FocusIn = 9;
    public const int FocusOut = 10;
    public const int MapNotify = 19;
    public const int ConfigureNotify = 22;
    public const int ClientMessage = 33;

    public const int RevertToParent = 2;
    public const int CurrentTime = 0;

    public const long ExposureMask = 1L << 15;
    public const long StructureNotifyMask = 1L << 17;
    public const long FocusChangeMask = 1L << 21;

    [DllImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
    public static extern IntPtr OpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    public static extern int CloseDisplay(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
    public static extern int DefaultScreen(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XRootWindow")]
    public static extern IntPtr RootWindow(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6", EntryPoint = "XBlackPixel")]
    public static extern IntPtr BlackPixel(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6", EntryPoint = "XWhitePixel")]
    public static extern IntPtr WhitePixel(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6", EntryPoint = "XCreateSimpleWindow")]
    public static extern IntPtr CreateSimpleWindow(
        IntPtr display,
        IntPtr parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        IntPtr border,
        IntPtr background);

    [DllImport("libX11.so.6", EntryPoint = "XStoreName")]
    public static extern int StoreName(
        IntPtr display,
        IntPtr window,
        [MarshalAs(UnmanagedType.LPStr)] string windowName);

    [DllImport("libX11.so.6", EntryPoint = "XSelectInput")]
    public static extern int SelectInput(IntPtr display, IntPtr window, long eventMask);

    [DllImport("libX11.so.6", EntryPoint = "XMapWindow")]
    public static extern int MapWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6", EntryPoint = "XResizeWindow")]
    public static extern int ResizeWindow(IntPtr display, IntPtr window, uint width, uint height);

    [DllImport("libX11.so.6", EntryPoint = "XDestroyWindow")]
    public static extern int DestroyWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6", EntryPoint = "XFlush")]
    public static extern int Flush(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XPending")]
    public static extern int Pending(IntPtr display);

    [DllImport("libX11.so.6", EntryPoint = "XNextEvent")]
    public static extern int NextEvent(IntPtr display, out XEvent inputEvent);

    [DllImport("libX11.so.6", EntryPoint = "XInternAtom")]
    public static extern IntPtr InternAtom(
        IntPtr display,
        [MarshalAs(UnmanagedType.LPStr)] string atomName,
        int onlyIfExists);

    [DllImport("libX11.so.6", EntryPoint = "XSetWMProtocols")]
    public static extern int SetWmProtocols(
        IntPtr display,
        IntPtr window,
        IntPtr[] protocols,
        int count);

    [DllImport("libX11.so.6", EntryPoint = "XSetInputFocus")]
    public static extern int SetInputFocus(IntPtr display, IntPtr focus, int revertTo, int time);

    [DllImport("libX11.so.6", EntryPoint = "XQueryPointer")]
    public static extern int QueryPointer(
        IntPtr display,
        IntPtr window,
        out IntPtr rootReturn,
        out IntPtr childReturn,
        out int rootX,
        out int rootY,
        out int winX,
        out int winY,
        out uint maskReturn);

    [DllImport("libX11.so.6", EntryPoint = "XSync")]
    public static extern int Sync(IntPtr display, int discard);

    [DllImport("libX11.so.6", EntryPoint = "XGetInputFocus")]
    public static extern int GetInputFocus(IntPtr display, out IntPtr focusReturn, out int revertToReturn);

    // Installs an error handler so a premature/best-effort XSetInputFocus (e.g.
    // a BadMatch on a not-yet-viewable window) cannot abort the process. Xlib's
    // default handler calls exit(); returning from a custom handler is ignored.
    [DllImport("libX11.so.6", EntryPoint = "XSetErrorHandler")]
    public static extern IntPtr SetErrorHandler(IntPtr handler);

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    public struct XEvent
    {
        [FieldOffset(0)]
        public int Type;

        // XClientMessageEvent.data.l[0] on the 64-bit Linux targets in the
        // Phase 0 baseline. It is only read when Type == ClientMessage.
        [FieldOffset(56)]
        public IntPtr ClientMessageData0;

        // XConfigureEvent.width/height on 64-bit Linux. These overlap the
        // XClientMessageEvent union storage and are read only for ConfigureNotify.
        [FieldOffset(56)]
        public int ConfigureWidth;

        [FieldOffset(60)]
        public int ConfigureHeight;
    }
}
