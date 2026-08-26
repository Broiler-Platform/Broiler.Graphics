using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Android;

/// <summary>
/// Owns the EGL display, config, context, and current drawing surface, and performs the
/// upload-and-blit present.
/// </summary>
/// <remarks>
/// The split between this type and its EGL surface is the Android-specific part of the design. On a
/// handheld the drawing surface is destroyed and recreated constantly — every rotation, every
/// backgrounding, every configuration change — while the context and the GPU resources inside it can
/// survive. So the display, config, and context live for the session, and only the
/// <c>EGLSurface</c> is torn down and rebuilt through <see cref="AttachWindow"/> and
/// <see cref="DetachWindow"/>. Rebuilding the context on every rotation would throw away the
/// texture and framebuffer for no reason.
///
/// A context that genuinely dies reports <c>EGL_CONTEXT_LOST</c>, which surfaces as the neutral
/// <see cref="BDeviceLostException"/> rather than an Android-specific error, so a host can treat it
/// the same way it treats a Direct3D device reset.
/// </remarks>
internal sealed class AndroidOpenGlEsSession : IDisposable
{
    private readonly IntPtr _display;
    private readonly IntPtr _config;
    private readonly IntPtr _context;
    private IntPtr _surface;
    private uint _texture;
    private uint _framebuffer;
    private int _textureWidth;
    private int _textureHeight;
    private bool _disposed;

    private AndroidOpenGlEsSession(
        IntPtr display,
        IntPtr config,
        IntPtr context,
        IntPtr surface,
        AndroidOpenGlEsDriverInfo driverInfo)
    {
        _display = display;
        _config = config;
        _context = context;
        _surface = surface;
        DriverInfo = driverInfo;
    }

    public AndroidOpenGlEsDriverInfo DriverInfo { get; }

    /// <summary>True when an EGL drawing surface is currently attached.</summary>
    public bool HasSurface => _surface != IntPtr.Zero;

    /// <summary>Creates a session drawing to an off-screen pbuffer.</summary>
    public static bool TryCreatePbuffer(
        int width,
        int height,
        out AndroidOpenGlEsSession? session,
        out string diagnostic)
    {
        session = null;
        diagnostic = string.Empty;

        try
        {
            session = CreatePbuffer(width, height);
            diagnostic = "Created an EGL pbuffer with an OpenGL ES 3 context. " + session.DriverInfo.ToDiagnosticString();
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or AndroidOpenGlEsException)
        {
            diagnostic = "Could not create an EGL/OpenGL ES pbuffer context: " + exception.Message;
            return false;
        }
    }

    /// <summary>Creates a session drawing to an <c>ANativeWindow</c>.</summary>
    public static bool TryCreateWindow(
        IntPtr nativeWindow,
        bool preferRgbaFormat,
        out AndroidOpenGlEsSession? session,
        out string diagnostic)
    {
        session = null;
        diagnostic = string.Empty;

        if (nativeWindow == IntPtr.Zero)
        {
            diagnostic = "No ANativeWindow was supplied.";
            return false;
        }

        try
        {
            session = CreateWindow(nativeWindow, preferRgbaFormat);
            diagnostic = "Created an EGL window surface with an OpenGL ES 3 context. " + session.DriverInfo.ToDiagnosticString();
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or AndroidOpenGlEsException)
        {
            diagnostic = "Could not create an EGL/OpenGL ES window surface: " + exception.Message;
            return false;
        }
    }

    private static AndroidOpenGlEsSession CreatePbuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        return Create(
            AndroidEglNative.EGL_PBUFFER_BIT,
            (display, config) => AndroidEglNative.CreatePbufferSurface(
                display,
                config,
                [AndroidEglNative.EGL_WIDTH, width, AndroidEglNative.EGL_HEIGHT, height, AndroidEglNative.EGL_NONE]),
            "eglCreatePbufferSurface");
    }

    private static AndroidOpenGlEsSession CreateWindow(IntPtr nativeWindow, bool preferRgbaFormat)
    {
        return Create(
            AndroidEglNative.EGL_WINDOW_BIT,
            (display, config) =>
            {
                ConfigureWindowGeometry(display, config, nativeWindow, preferRgbaFormat);
                return AndroidEglNative.CreateWindowSurface(display, config, nativeWindow, null);
            },
            "eglCreateWindowSurface");
    }

    private static AndroidOpenGlEsSession Create(
        int surfaceType,
        Func<IntPtr, IntPtr, IntPtr> createSurface,
        string createSurfaceName)
    {
        AndroidNativeLibraries.EnsureRegistered();

        IntPtr display = AndroidEglNative.GetDisplay(AndroidEglNative.EGL_DEFAULT_DISPLAY);
        if (display == AndroidEglNative.EGL_NO_DISPLAY)
            throw EglFailure("eglGetDisplay");

        try
        {
            if (AndroidEglNative.Initialize(display, out _, out _) == AndroidEglNative.EGL_FALSE)
                throw EglFailure("eglInitialize");

            // Android has no desktop GL. Binding EGL_OPENGL_API here would fail, and requesting
            // EGL_OPENGL_BIT in the config would match nothing.
            if (AndroidEglNative.BindApi(AndroidEglNative.EGL_OPENGL_ES_API) == AndroidEglNative.EGL_FALSE)
                throw EglFailure("eglBindAPI(EGL_OPENGL_ES_API)");

            IntPtr config = ChooseConfig(display, surfaceType);
            IntPtr surface = createSurface(display, config);
            if (surface == AndroidEglNative.EGL_NO_SURFACE)
                throw EglFailure(createSurfaceName);

            IntPtr context = CreateContext(display, config);
            if (AndroidEglNative.MakeCurrent(display, surface, surface, context) == AndroidEglNative.EGL_FALSE)
                throw EglFailure("eglMakeCurrent");

            AndroidOpenGlEsDriverInfo driverInfo = AndroidGlesNative.GetDriverInfo();

            // Release the context from the creating thread: EGL contexts are thread-affine and the
            // render loop may resume on a different thread, which would make the next
            // eglMakeCurrent fail with EGL_BAD_ACCESS. Every operation re-binds around its own work.
            AndroidEglNative.MakeCurrent(display, AndroidEglNative.EGL_NO_SURFACE, AndroidEglNative.EGL_NO_SURFACE, AndroidEglNative.EGL_NO_CONTEXT);
            return new AndroidOpenGlEsSession(display, config, context, surface, driverInfo);
        }
        catch
        {
            AndroidEglNative.Terminate(display);
            throw;
        }
    }

    private static void ConfigureWindowGeometry(IntPtr display, IntPtr config, IntPtr nativeWindow, bool preferRgbaFormat)
    {
        // EGL requires the window's buffer format to match the chosen config's native visual, and
        // the format is the only part ANativeWindow lets us set. Width and height stay 0 so the
        // window keeps the size the compositor gave it.
        int format = preferRgbaFormat ? AndroidNativeWindowNative.WindowFormatRgba8888 : 0;
        if (AndroidEglNative.GetConfigAttrib(display, config, AndroidEglNative.EGL_NATIVE_VISUAL_ID, out int visualId) != AndroidEglNative.EGL_FALSE &&
            visualId != 0)
        {
            format = visualId;
        }

        AndroidNativeWindowNative.SetBuffersGeometry(nativeWindow, 0, 0, format);
    }

    private static IntPtr ChooseConfig(IntPtr display, int surfaceType)
    {
        int[] attributes =
        [
            AndroidEglNative.EGL_SURFACE_TYPE, surfaceType,
            AndroidEglNative.EGL_RENDERABLE_TYPE, AndroidEglNative.EGL_OPENGL_ES3_BIT,
            AndroidEglNative.EGL_RED_SIZE, 8,
            AndroidEglNative.EGL_GREEN_SIZE, 8,
            AndroidEglNative.EGL_BLUE_SIZE, 8,
            AndroidEglNative.EGL_ALPHA_SIZE, 8,
            AndroidEglNative.EGL_DEPTH_SIZE, 0,
            AndroidEglNative.EGL_STENCIL_SIZE, 0,
            AndroidEglNative.EGL_NONE,
        ];

        IntPtr[] configs = new IntPtr[1];
        if (AndroidEglNative.ChooseConfig(display, attributes, configs, configs.Length, out int count) == AndroidEglNative.EGL_FALSE || count == 0)
            throw EglFailure("eglChooseConfig(EGL_OPENGL_ES3_BIT)");

        return configs[0];
    }

    private static IntPtr CreateContext(IntPtr display, IntPtr config)
    {
        // ES 3 only. glBlitFramebuffer is an ES 3.0 entry point, so an ES 2 context would load but
        // fail at present time; refusing here turns that into a clear creation failure.
        IntPtr context = AndroidEglNative.CreateContext(
            display,
            config,
            AndroidEglNative.EGL_NO_CONTEXT,
            [AndroidEglNative.EGL_CONTEXT_CLIENT_VERSION, 3, AndroidEglNative.EGL_NONE]);

        if (context == AndroidEglNative.EGL_NO_CONTEXT)
            throw EglFailure("eglCreateContext(client version 3)");

        return context;
    }

    /// <summary>
    /// Rebuilds the EGL drawing surface for a new <c>ANativeWindow</c>, keeping the context and its
    /// GPU resources. Call this from the host's surface-created and surface-changed callbacks.
    /// </summary>
    public void AttachWindow(IntPtr nativeWindow, bool preferRgbaFormat)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (nativeWindow == IntPtr.Zero)
            throw new ArgumentException("A native window is required.", nameof(nativeWindow));

        DetachWindow();

        ConfigureWindowGeometry(_display, _config, nativeWindow, preferRgbaFormat);
        IntPtr surface = AndroidEglNative.CreateWindowSurface(_display, _config, nativeWindow, null);
        if (surface == AndroidEglNative.EGL_NO_SURFACE)
            throw EglFailure("eglCreateWindowSurface");

        _surface = surface;
    }

    /// <summary>
    /// Destroys the EGL drawing surface while keeping the context alive. Call this from the host's
    /// surface-destroyed callback.
    /// </summary>
    public void DetachWindow()
    {
        if (_disposed || _surface == IntPtr.Zero)
            return;

        AndroidEglNative.MakeCurrent(_display, AndroidEglNative.EGL_NO_SURFACE, AndroidEglNative.EGL_NO_SURFACE, AndroidEglNative.EGL_NO_CONTEXT);
        AndroidEglNative.DestroySurface(_display, _surface);
        _surface = IntPtr.Zero;
    }

    /// <summary>Uploads a CPU-rendered frame and blits it to the drawing surface.</summary>
    public void Present(BBitmap bitmap, int targetWidth, int targetHeight, bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bitmap);

        if (_surface == IntPtr.Zero)
            throw new AndroidOpenGlEsException("There is no EGL drawing surface attached; the Android surface has been destroyed.");

        MakeCurrent();
        try
        {
            AndroidEglNative.SwapInterval(_display, vsync ? 1 : 0);
            EnsureFramebuffer(bitmap.Width, bitmap.Height);
            Upload(bitmap);
            BlitToDefaultFramebuffer(targetWidth, targetHeight);

            if (AndroidEglNative.SwapBuffers(_display, _surface) == AndroidEglNative.EGL_FALSE)
                throw EglFailure("eglSwapBuffers");

            AndroidGlesNative.Flush();
            AndroidGlesNative.ThrowIfError("OpenGL ES present");
        }
        finally
        {
            ReleaseCurrent();
        }
    }

    /// <summary>Reads the current framebuffer back as a top-down RGBA bitmap.</summary>
    public BBitmap ReadToBitmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_textureWidth <= 0 || _textureHeight <= 0)
            throw new AndroidOpenGlEsException("Nothing has been presented, so there is no framebuffer to read.");

        MakeCurrent();
        byte[] bottomUp = new byte[checked(_textureWidth * _textureHeight * BPixelBuffer.BytesPerPixel)];
        GCHandle handle = GCHandle.Alloc(bottomUp, GCHandleType.Pinned);
        try
        {
            AndroidGlesNative.BindFramebuffer(AndroidGlesNative.GL_READ_FRAMEBUFFER, _framebuffer);
            AndroidGlesNative.PixelStorei(AndroidGlesNative.GL_PACK_ALIGNMENT, 1);
            AndroidGlesNative.ReadPixels(
                0,
                0,
                _textureWidth,
                _textureHeight,
                AndroidGlesNative.GL_RGBA,
                AndroidGlesNative.GL_UNSIGNED_BYTE,
                handle.AddrOfPinnedObject());
            AndroidGlesNative.ThrowIfError("glReadPixels");
        }
        finally
        {
            handle.Free();
            ReleaseCurrent();
        }

        return AndroidGlesPixelConversion.FromBottomUpRgba(_textureWidth, _textureHeight, bottomUp);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        AndroidEglNative.MakeCurrent(_display, AndroidEglNative.EGL_NO_SURFACE, AndroidEglNative.EGL_NO_SURFACE, AndroidEglNative.EGL_NO_CONTEXT);

        if (_surface != IntPtr.Zero)
        {
            AndroidEglNative.DestroySurface(_display, _surface);
            _surface = IntPtr.Zero;
        }

        if (_context != IntPtr.Zero)
            AndroidEglNative.DestroyContext(_display, _context);

        AndroidEglNative.Terminate(_display);
    }

    private void EnsureFramebuffer(int width, int height)
    {
        if (_texture != 0 && _framebuffer != 0 && width == _textureWidth && height == _textureHeight)
            return;

        if (_framebuffer != 0)
        {
            uint framebuffer = _framebuffer;
            AndroidGlesNative.DeleteFramebuffers(1, ref framebuffer);
            _framebuffer = 0;
        }

        if (_texture != 0)
        {
            uint texture = _texture;
            AndroidGlesNative.DeleteTextures(1, ref texture);
            _texture = 0;
        }

        _textureWidth = width;
        _textureHeight = height;

        AndroidGlesNative.GenTextures(1, out _texture);
        AndroidGlesNative.BindTexture(AndroidGlesNative.GL_TEXTURE_2D, _texture);
        AndroidGlesNative.TexParameteri(AndroidGlesNative.GL_TEXTURE_2D, AndroidGlesNative.GL_TEXTURE_MIN_FILTER, AndroidGlesNative.GL_LINEAR);
        AndroidGlesNative.TexParameteri(AndroidGlesNative.GL_TEXTURE_2D, AndroidGlesNative.GL_TEXTURE_MAG_FILTER, AndroidGlesNative.GL_LINEAR);
        AndroidGlesNative.TexParameteri(AndroidGlesNative.GL_TEXTURE_2D, AndroidGlesNative.GL_TEXTURE_WRAP_S, AndroidGlesNative.GL_CLAMP_TO_EDGE);
        AndroidGlesNative.TexParameteri(AndroidGlesNative.GL_TEXTURE_2D, AndroidGlesNative.GL_TEXTURE_WRAP_T, AndroidGlesNative.GL_CLAMP_TO_EDGE);
        AndroidGlesNative.GenFramebuffers(1, out _framebuffer);
        AndroidGlesNative.BindFramebuffer(AndroidGlesNative.GL_FRAMEBUFFER, _framebuffer);
        AndroidGlesNative.FramebufferTexture2D(
            AndroidGlesNative.GL_FRAMEBUFFER,
            AndroidGlesNative.GL_COLOR_ATTACHMENT0,
            AndroidGlesNative.GL_TEXTURE_2D,
            _texture,
            0);
    }

    private void Upload(BBitmap bitmap)
    {
        byte[] bottomUp = AndroidGlesPixelConversion.ToBottomUpRgba(bitmap);
        GCHandle handle = GCHandle.Alloc(bottomUp, GCHandleType.Pinned);
        try
        {
            AndroidGlesNative.BindTexture(AndroidGlesNative.GL_TEXTURE_2D, _texture);
            AndroidGlesNative.PixelStorei(AndroidGlesNative.GL_UNPACK_ALIGNMENT, 1);
            AndroidGlesNative.TexImage2D(
                AndroidGlesNative.GL_TEXTURE_2D,
                0,
                AndroidGlesNative.GL_RGBA8,
                bitmap.Width,
                bitmap.Height,
                0,
                AndroidGlesNative.GL_RGBA,
                AndroidGlesNative.GL_UNSIGNED_BYTE,
                handle.AddrOfPinnedObject());
            AndroidGlesNative.BindFramebuffer(AndroidGlesNative.GL_FRAMEBUFFER, _framebuffer);
            AndroidGlesNative.FramebufferTexture2D(
                AndroidGlesNative.GL_FRAMEBUFFER,
                AndroidGlesNative.GL_COLOR_ATTACHMENT0,
                AndroidGlesNative.GL_TEXTURE_2D,
                _texture,
                0);

            uint status = AndroidGlesNative.CheckFramebufferStatus(AndroidGlesNative.GL_FRAMEBUFFER);
            if (status != AndroidGlesNative.GL_FRAMEBUFFER_COMPLETE)
                throw new AndroidOpenGlEsException($"OpenGL ES framebuffer is incomplete: 0x{status:X}.");

            AndroidGlesNative.ThrowIfError("OpenGL ES texture upload");
        }
        finally
        {
            handle.Free();
        }
    }

    private void BlitToDefaultFramebuffer(int targetWidth, int targetHeight)
    {
        AndroidGlesNative.BindFramebuffer(AndroidGlesNative.GL_READ_FRAMEBUFFER, _framebuffer);
        AndroidGlesNative.BindFramebuffer(AndroidGlesNative.GL_DRAW_FRAMEBUFFER, 0);
        AndroidGlesNative.Viewport(0, 0, targetWidth, targetHeight);
        AndroidGlesNative.Disable(AndroidGlesNative.GL_SCISSOR_TEST);
        AndroidGlesNative.ClearColor(0, 0, 0, 0);
        AndroidGlesNative.Clear(AndroidGlesNative.GL_COLOR_BUFFER_BIT);
        AndroidGlesNative.BlitFramebuffer(
            0,
            0,
            _textureWidth,
            _textureHeight,
            0,
            0,
            targetWidth,
            targetHeight,
            AndroidGlesNative.GL_COLOR_BUFFER_BIT,
            AndroidGlesNative.GL_NEAREST);
    }

    private void MakeCurrent()
    {
        if (AndroidEglNative.MakeCurrent(_display, _surface, _surface, _context) != AndroidEglNative.EGL_FALSE)
            return;

        int error = AndroidEglNative.GetError();

        // EGL_CONTEXT_LOST is the GPU equivalent of a device reset. Reporting it through the
        // neutral BDeviceLostException lets a host recover the same way it would on any other
        // backend, instead of parsing an Android-specific error code.
        if (error == AndroidEglNative.EGL_CONTEXT_LOST)
            throw new BDeviceLostException("The OpenGL ES context was lost and must be recreated.", error);

        throw new AndroidOpenGlEsException($"eglMakeCurrent failed with EGL error 0x{error:X}.");
    }

    private void ReleaseCurrent() =>
        AndroidEglNative.MakeCurrent(_display, AndroidEglNative.EGL_NO_SURFACE, AndroidEglNative.EGL_NO_SURFACE, AndroidEglNative.EGL_NO_CONTEXT);

    private static AndroidOpenGlEsException EglFailure(string operation) =>
        new($"{operation} failed with EGL error 0x{AndroidEglNative.GetError():X}.");
}
