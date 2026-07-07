using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Linux.OpenGL;

public sealed class LinuxOpenGlX11WindowSurface : ILinuxOpenGlPresentSurface
{
    private delegate int X11ErrorHandler(IntPtr display, IntPtr errorEvent);

    // Kept alive as a static field so the native error handler pointer stays
    // valid. Swallows best-effort focus errors instead of aborting the process.
    private static readonly X11ErrorHandler IgnoreX11Errors = static (_, _) => 0;
    private static bool _errorHandlerInstalled;

    private readonly LinuxOpenGlRendererOptions _options;
    private readonly string _title;
    private IntPtr _display;
    private IntPtr _window;
    private IntPtr _wmDeleteWindow;
    private LinuxOpenGlCpuPresentSession? _session;
    private BSurfaceDescriptor _descriptor;
    private BBitmap? _lastFrame;
    private string _diagnostic = "X11/EGL window surface has not been initialized.";
    private bool _isFocused;
    private bool _closeRequested;
    private bool _disposed;

    internal LinuxOpenGlX11WindowSurface(
        BSurfaceDescriptor descriptor,
        LinuxOpenGlRendererOptions options,
        string title)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _title = string.IsNullOrWhiteSpace(title) ? "Broiler.Graphics OpenGL" : title;
        _descriptor = ValidateDescriptor(descriptor);
        CreateWindowAndSession();
    }

    public BSize Size => _descriptor.Size;

    public double DpiScale => _descriptor.DpiScale;

    public BSurfaceDescriptor Descriptor => _descriptor;

    public bool IsGpuBacked => _session is not null;

    public string Diagnostic => _diagnostic;

    public LinuxOpenGlDriverInfo? DriverInfo => _session?.DriverInfo;

    public bool IsFocused => _isFocused;

    public bool IsCloseRequested => _closeRequested;

    public event Action<bool>? FocusChanged;

    public bool TryReplayNative(BRenderList renderList, BFrameContext frameContext, bool vsync, out string diagnostic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderList);

        diagnostic = string.Empty;
        if (!_options.EnableNativeCommandReplay)
        {
            diagnostic = "OpenGL native command replay is disabled by renderer options.";
            return false;
        }

        if (_session is null)
        {
            diagnostic = "OpenGL native command replay requires an active X11/EGL/OpenGL session.";
            return false;
        }

        if (!LinuxOpenGlNativeReplay.TryCreatePlan(renderList, _descriptor, frameContext, out LinuxOpenGlNativeReplayPlan? plan, out diagnostic) || plan is null)
            return false;

        try
        {
            _lastFrame?.Dispose();
            _lastFrame = null;
            _session.ReplayNative(plan, PixelWidth, PixelHeight, vsync);
            _diagnostic = "Presented through X11/EGL/OpenGL native command replay. " + diagnostic + " " + _session.DriverInfo.ToDiagnosticString();
            return true;
        }
        catch (Exception exception) when (_options.AllowCpuFallbackWhenOpenGlUnavailable)
        {
            diagnostic = "X11/EGL/OpenGL native command replay failed; using CPU-present fallback. " + exception.Message;
            _diagnostic = diagnostic;
            _session.Dispose();
            _session = null;
            return false;
        }
    }

    public void Resize(BSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _descriptor = ValidateDescriptor(_descriptor with { Size = size, DpiScale = dpiScale });
        if (_window != IntPtr.Zero)
        {
            LinuxX11Native.ResizeWindow(_display, _window, (uint)PixelWidth, (uint)PixelHeight);
            LinuxX11Native.Flush(_display);
        }
    }

    /// <summary>
    /// Gets the OS pointer position in this window's pixel space (origin at the
    /// window's top-left). Lets a client keep its own pointer in sync with the
    /// real cursor instead of accumulating raw relative deltas. <paramref name="inside"/>
    /// reports whether the pointer is currently over the window.
    /// </summary>
    public bool TryGetPointerPosition(out int x, out int y, out bool inside)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        x = 0;
        y = 0;
        inside = false;
        if (_display == IntPtr.Zero || _window == IntPtr.Zero)
            return false;

        LinuxX11Native.QueryPointer(
            _display,
            _window,
            out _,
            out _,
            out _,
            out _,
            out int winX,
            out int winY,
            out _);

        x = winX;
        y = winY;
        inside = winX >= 0 && winY >= 0 && winX < PixelWidth && winY < PixelHeight;
        return true;
    }

    public bool ProcessPendingEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_display == IntPtr.Zero)
            return false;

        bool processed = false;
        while (LinuxX11Native.Pending(_display) > 0)
        {
            LinuxX11Native.NextEvent(_display, out LinuxX11Native.XEvent inputEvent);
            processed = true;
            ProcessEvent(inputEvent);
        }

        return processed;
    }

    public void Present(BBitmap bitmap, bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bitmap);

        _lastFrame?.Dispose();
        _lastFrame = bitmap.Copy();

        if (_session is null)
            return;

        try
        {
            _session.Present(bitmap, PixelWidth, PixelHeight, vsync);
            _diagnostic = "Presented through X11/EGL/OpenGL. " + _session.DriverInfo.ToDiagnosticString();
        }
        catch (Exception exception) when (_options.AllowCpuFallbackWhenOpenGlUnavailable)
        {
            _diagnostic = "X11/EGL/OpenGL present failed; preserving CPU-present fallback. " + exception.Message;
            _session.Dispose();
            _session = null;
        }
    }

    public BBitmap ReadToBitmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_session is not null && _options.EnableGpuReadbackForRenderToImage)
        {
            try
            {
                return _session.ReadToBitmap();
            }
            catch (Exception exception) when (_options.AllowCpuFallbackWhenOpenGlUnavailable && _lastFrame is not null)
            {
                _diagnostic = "X11/EGL/OpenGL readback failed; returning CPU-present fallback. " + exception.Message;
            }
        }

        if (_lastFrame is not null)
            return _lastFrame.Copy();

        return new BBitmap(PixelWidth, PixelHeight);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _session?.Dispose();
        _lastFrame?.Dispose();

        if (_display != IntPtr.Zero && _window != IntPtr.Zero)
            LinuxX11Native.DestroyWindow(_display, _window);
        if (_display != IntPtr.Zero)
            LinuxX11Native.CloseDisplay(_display);

        _window = IntPtr.Zero;
        _display = IntPtr.Zero;
    }

    private void CreateWindowAndSession()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("X11/EGL OpenGL windows require Linux.");

        try
        {
            _display = LinuxX11Native.OpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
                throw new LinuxOpenGlException("XOpenDisplay failed. Ensure DISPLAY is set and an X11 server is available.");

            int screen = LinuxX11Native.DefaultScreen(_display);
            IntPtr root = LinuxX11Native.RootWindow(_display, screen);
            _window = LinuxX11Native.CreateSimpleWindow(
                _display,
                root,
                0,
                0,
                (uint)PixelWidth,
                (uint)PixelHeight,
                0,
                LinuxX11Native.BlackPixel(_display, screen),
                LinuxX11Native.WhitePixel(_display, screen));
            if (_window == IntPtr.Zero)
                throw new LinuxOpenGlException("XCreateSimpleWindow failed.");

            LinuxX11Native.StoreName(_display, _window, _title);
            LinuxX11Native.SelectInput(
                _display,
                _window,
                LinuxX11Native.ExposureMask |
                LinuxX11Native.StructureNotifyMask |
                LinuxX11Native.FocusChangeMask);
            RegisterCloseProtocol();
            InstallErrorHandlerOnce();
            LinuxX11Native.MapWindow(_display, _window);
            LinuxX11Native.Flush(_display);
            TryFocusWindow();
            _session = LinuxOpenGlCpuPresentSession.CreateX11Window(_display, _window, PixelWidth, PixelHeight);
            _diagnostic = "Created X11/EGL/OpenGL window surface. " + _session.DriverInfo.ToDiagnosticString();
        }
        catch (Exception exception) when (_options.AllowCpuFallbackWhenOpenGlUnavailable)
        {
            _diagnostic = "Could not create X11/EGL/OpenGL window surface: " + exception.Message;
            DisposeNativeWindow();
            throw new LinuxOpenGlException(_diagnostic, exception);
        }
    }

    private void ProcessEvent(LinuxX11Native.XEvent inputEvent)
    {
        switch (inputEvent.Type)
        {
            case LinuxX11Native.FocusIn:
                SetFocused(true);
                break;
            case LinuxX11Native.FocusOut:
                SetFocused(false);
                break;
            case LinuxX11Native.MapNotify:
                // Once the window is viewable, request keyboard focus so bare/
                // uncooperative window managers still route key events to it.
                TryFocusWindow();
                break;
            case LinuxX11Native.ConfigureNotify:
                ApplyConfigureSize(inputEvent.ConfigureWidth, inputEvent.ConfigureHeight);
                break;
            case LinuxX11Native.ClientMessage when _wmDeleteWindow != IntPtr.Zero && inputEvent.ClientMessageData0 == _wmDeleteWindow:
                _closeRequested = true;
                break;
        }
    }

    private void ApplyConfigureSize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
            return;

        double dpiScale = _descriptor.DpiScale <= 0 ? 1.0 : _descriptor.DpiScale;
        BSize logicalSize = new(pixelWidth / dpiScale, pixelHeight / dpiScale);
        if (Math.Abs(logicalSize.Width - _descriptor.Size.Width) < 0.5 &&
            Math.Abs(logicalSize.Height - _descriptor.Size.Height) < 0.5)
        {
            return;
        }

        _descriptor = ValidateDescriptor(_descriptor with { Size = logicalSize });
        _lastFrame?.Dispose();
        _lastFrame = null;
    }

    private void RegisterCloseProtocol()
    {
        _wmDeleteWindow = LinuxX11Native.InternAtom(_display, "WM_DELETE_WINDOW", LinuxX11Native.False);
        if (_wmDeleteWindow == IntPtr.Zero)
            return;

        LinuxX11Native.SetWmProtocols(_display, _window, [_wmDeleteWindow], 1);
    }

    private void SetFocused(bool focused)
    {
        if (_isFocused == focused)
            return;

        _isFocused = focused;
        FocusChanged?.Invoke(focused);
    }

    private static void InstallErrorHandlerOnce()
    {
        if (_errorHandlerInstalled)
            return;

        _errorHandlerInstalled = true;
        LinuxX11Native.SetErrorHandler(Marshal.GetFunctionPointerForDelegate(IgnoreX11Errors));
    }

    private void TryFocusWindow()
    {
        if (_display == IntPtr.Zero || _window == IntPtr.Zero)
            return;

        // Best effort: errors (e.g. BadMatch before the window is viewable) are
        // swallowed by the installed no-op handler.
        LinuxX11Native.SetInputFocus(_display, _window, LinuxX11Native.RevertToParent, LinuxX11Native.CurrentTime);
        LinuxX11Native.Sync(_display, LinuxX11Native.False);
    }

    private void DisposeNativeWindow()
    {
        _session?.Dispose();
        _session = null;

        if (_display != IntPtr.Zero && _window != IntPtr.Zero)
            LinuxX11Native.DestroyWindow(_display, _window);
        if (_display != IntPtr.Zero)
            LinuxX11Native.CloseDisplay(_display);

        _window = IntPtr.Zero;
        _display = IntPtr.Zero;
    }

    private int PixelWidth => ToPixelDimension(_descriptor.Size.Width, _descriptor.DpiScale, nameof(Size));

    private int PixelHeight => ToPixelDimension(_descriptor.Size.Height, _descriptor.DpiScale, nameof(Size));

    private static BSurfaceDescriptor ValidateDescriptor(BSurfaceDescriptor descriptor)
    {
        if (!IsPositiveFinite(descriptor.Size.Width) || !IsPositiveFinite(descriptor.Size.Height))
            throw new ArgumentOutOfRangeException(nameof(descriptor), "Surface size must be positive and finite.");

        return descriptor with
        {
            DpiScale = IsPositiveFinite(descriptor.DpiScale) ? descriptor.DpiScale : 1.0,
        };
    }

    private static int ToPixelDimension(double dip, double dpiScale, string name)
    {
        double pixels = Math.Ceiling(dip * dpiScale);
        if (pixels <= 0 || pixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Surface pixel dimensions are outside the supported range.");

        return (int)pixels;
    }

    private static bool IsPositiveFinite(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
