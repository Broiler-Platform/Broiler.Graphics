using System;

namespace Broiler.Graphics.Android;

/// <summary>
/// An on-screen Android surface bound to an <c>ANativeWindow</c>, driven by the host's
/// <c>SurfaceHolder</c> callbacks.
/// </summary>
/// <remarks>
/// The host owns the <c>SurfaceView</c> lifecycle and forwards it here:
/// <list type="bullet">
/// <item><description><c>surfaceCreated</c>/<c>surfaceChanged</c> → <see cref="AttachNativeWindow"/></description></item>
/// <item><description><c>surfaceDestroyed</c> → <see cref="DetachNativeWindow"/></description></item>
/// </list>
///
/// Detaching destroys only the EGL drawing surface; the context and its texture survive, so a
/// rotation does not rebuild GPU state. Between detach and the next attach the surface is not
/// presentable — <see cref="Present"/> keeps the CPU frame and reports it in
/// <see cref="Diagnostic"/> rather than throwing, because a frame arriving mid-rotation is normal
/// and must not take the application down.
///
/// The window handle is borrowed, not owned. The host obtained it from
/// <c>ANativeWindow_fromSurface</c> and is responsible for releasing it after
/// <see cref="DetachNativeWindow"/> returns.
/// </remarks>
public sealed class AndroidOpenGlEsWindowSurface : IAndroidPresentSurface
{
    private readonly AndroidOpenGlEsRendererOptions _options;
    private BSurfaceDescriptor _descriptor;
    private AndroidOpenGlEsSession? _session;
    private BBitmap? _lastFrame;
    private IntPtr _nativeWindow;
    private string _diagnostic = "No Android surface has been attached yet.";
    private bool _disposed;

    internal AndroidOpenGlEsWindowSurface(BSurfaceDescriptor descriptor, AndroidOpenGlEsRendererOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _descriptor = AndroidSurfaceGeometry.Validate(descriptor);
    }

    public BSize Size => _descriptor.Size;

    public double DpiScale => _descriptor.DpiScale;

    public BSurfaceDescriptor Descriptor => _descriptor;

    /// <summary>True while an Android surface is attached and presentable.</summary>
    public bool IsAttached => _nativeWindow != IntPtr.Zero && _session is { HasSurface: true };

    /// <summary>True when an EGL/OpenGL ES context exists, whether or not a surface is attached.</summary>
    public bool IsGpuBacked => _session is not null;

    /// <summary>Human-readable description of the presentation route currently in use.</summary>
    public string Diagnostic => _diagnostic;

    public AndroidOpenGlEsDriverInfo? DriverInfo => _session?.DriverInfo;

    /// <summary>
    /// Binds a native window, creating the EGL context on first use and rebuilding only the drawing
    /// surface afterwards.
    /// </summary>
    /// <param name="nativeWindow">
    /// An <c>ANativeWindow*</c>, normally from <c>ANativeWindow_fromSurface</c>. Use
    /// <see cref="AndroidNativeWindow.FromSurface"/> if the host has a <c>JNIEnv*</c> and a
    /// <c>Surface</c> handle rather than a window pointer.
    /// </param>
    public void AttachNativeWindow(IntPtr nativeWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (nativeWindow == IntPtr.Zero)
            throw new ArgumentException("A native window is required.", nameof(nativeWindow));

        _nativeWindow = nativeWindow;

        if (!_options.TryCreateEglContext)
        {
            _diagnostic = "EGL/OpenGL ES context creation is disabled by renderer options.";
            return;
        }

        // An existing context is reused across surface loss; that is the whole point of keeping the
        // session alive when the Android surface goes away.
        if (_session is not null)
        {
            try
            {
                _session.AttachWindow(nativeWindow, _options.PreferRgbaWindowFormat);
                _diagnostic = "Reattached the Android surface to the existing OpenGL ES context. " + _session.DriverInfo.ToDiagnosticString();
                return;
            }
            catch (Exception exception) when (exception is not BDeviceLostException)
            {
                _diagnostic = "Could not reattach the existing OpenGL ES context; recreating it. " + exception.Message;
                _session.Dispose();
                _session = null;
            }
        }

        if (AndroidOpenGlEsSession.TryCreateWindow(nativeWindow, _options.PreferRgbaWindowFormat, out AndroidOpenGlEsSession? session, out string diagnostic))
        {
            _session = session;
            _diagnostic = diagnostic;
            return;
        }

        _diagnostic = diagnostic;
        if (!_options.AllowCpuFallbackWhenOpenGlUnavailable)
            throw new AndroidOpenGlEsException(diagnostic);
    }

    /// <summary>
    /// Releases the EGL drawing surface while keeping the context and its GPU resources. Call this
    /// from <c>surfaceDestroyed</c> before releasing the window handle.
    /// </summary>
    public void DetachNativeWindow()
    {
        if (_disposed)
            return;

        _session?.DetachWindow();
        _nativeWindow = IntPtr.Zero;
        _diagnostic = "The Android surface is detached; presentation is paused.";
    }

    public void Resize(BSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _descriptor = AndroidSurfaceGeometry.Validate(_descriptor with { Size = size, DpiScale = dpiScale });
        _lastFrame?.Dispose();
        _lastFrame = null;

        // A resize on Android arrives as surfaceChanged, which also hands back a native window.
        // Rebinding it refreshes the EGL surface's buffer geometry for the new size.
        if (_nativeWindow != IntPtr.Zero && _session is not null)
            AttachNativeWindow(_nativeWindow);
    }

    public void Present(BBitmap bitmap, bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bitmap);

        _lastFrame?.Dispose();
        _lastFrame = bitmap.Copy();

        if (_session is null || !_session.HasSurface)
        {
            // Frames can arrive between surfaceDestroyed and the next surfaceCreated — during a
            // rotation, for instance. Dropping them is correct; throwing would crash the Activity.
            _diagnostic = "No Android surface is attached; the frame was rendered but not presented.";
            return;
        }

        try
        {
            _session.Present(bitmap, PixelWidth, PixelHeight, vsync);
            _diagnostic = "Presented through EGL/OpenGL ES. " + _session.DriverInfo.ToDiagnosticString();
        }
        catch (Exception exception) when (exception is not BDeviceLostException && _options.AllowCpuFallbackWhenOpenGlUnavailable)
        {
            _diagnostic = "OpenGL ES present failed; holding the CPU frame. " + exception.Message;
            _session.Dispose();
            _session = null;
        }
    }

    public BBitmap ReadToBitmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_session is { HasSurface: true } && _options.EnableGpuReadbackForRenderToImage)
        {
            try
            {
                return _session.ReadToBitmap();
            }
            catch (Exception exception) when (_options.AllowCpuFallbackWhenOpenGlUnavailable && _lastFrame is not null)
            {
                _diagnostic = "OpenGL ES readback failed; returning the CPU frame. " + exception.Message;
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
        _nativeWindow = IntPtr.Zero;
    }

    private int PixelWidth => AndroidSurfaceGeometry.ToPixels(_descriptor.Size.Width, _descriptor.DpiScale);

    private int PixelHeight => AndroidSurfaceGeometry.ToPixels(_descriptor.Size.Height, _descriptor.DpiScale);
}
