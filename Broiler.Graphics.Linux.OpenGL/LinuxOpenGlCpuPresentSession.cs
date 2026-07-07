using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Linux.OpenGL;

internal sealed class LinuxOpenGlCpuPresentSession : IDisposable
{
    private readonly IntPtr _display;
    private readonly IntPtr _context;
    private readonly IntPtr _surface;
    private readonly LinuxOpenGlFunctions _gl;
    private uint _texture;
    private uint _framebuffer;
    private int _width;
    private int _height;
    private bool _disposed;

    private LinuxOpenGlCpuPresentSession(
        IntPtr display,
        IntPtr context,
        IntPtr surface,
        LinuxOpenGlFunctions gl,
        LinuxOpenGlDriverInfo driverInfo,
        int width,
        int height)
    {
        _display = display;
        _context = context;
        _surface = surface;
        _gl = gl;
        DriverInfo = driverInfo;
        _width = width;
        _height = height;
    }

    public LinuxOpenGlDriverInfo DriverInfo { get; }

    public static bool TryCreatePbuffer(
        int width,
        int height,
        out LinuxOpenGlCpuPresentSession? session,
        out string diagnostic)
    {
        session = null;
        diagnostic = string.Empty;

        try
        {
            session = CreatePbuffer(width, height);
            diagnostic = "Created EGL pbuffer with desktop OpenGL context. " + session.DriverInfo.ToDiagnosticString();
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or LinuxOpenGlException)
        {
            diagnostic = "Could not create EGL/OpenGL pbuffer context: " + exception.Message;
            return false;
        }
    }

    public static LinuxOpenGlCpuPresentSession CreateX11Window(
        IntPtr nativeDisplay,
        IntPtr nativeWindow,
        int width,
        int height)
    {
        if (nativeDisplay == IntPtr.Zero)
            throw new ArgumentException("An X11 display handle is required.", nameof(nativeDisplay));
        if (nativeWindow == IntPtr.Zero)
            throw new ArgumentException("An X11 window handle is required.", nameof(nativeWindow));

        return Create(
            width,
            height,
            LinuxEglNative.EGL_WINDOW_BIT,
            static (display, config, state) => LinuxEglNative.CreateWindowSurface(display, config, state.NativeWindow, [LinuxEglNative.EGL_NONE]),
            new NativeWindowState(nativeDisplay, nativeWindow));
    }

    public void Present(BBitmap bitmap, int targetWidth, int targetHeight, bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bitmap);
        MakeCurrent();
        try
        {
            EnsureFramebuffer(bitmap.Width, bitmap.Height);
            Upload(bitmap);
            BlitToDefaultFramebuffer(targetWidth, targetHeight);
            if (LinuxEglNative.SwapBuffers(_display, _surface) == LinuxEglNative.EGL_FALSE)
                throw EglFailure("eglSwapBuffers");

            _gl.Flush();
            _gl.ThrowIfError("OpenGL present");
        }
        finally
        {
            ReleaseCurrent();
        }
    }

    public void ReplayNative(LinuxOpenGlNativeReplayPlan plan, int targetWidth, int targetHeight, bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(plan);

        MakeCurrent();
        try
        {
            EnsureFramebuffer(plan.PixelWidth, plan.PixelHeight);
            AllocateFramebufferTexture(plan.PixelWidth, plan.PixelHeight);

            _gl.BindFramebuffer(LinuxOpenGlFunctions.GL_FRAMEBUFFER, _framebuffer);
            _gl.Viewport(0, 0, plan.PixelWidth, plan.PixelHeight);
            _gl.Disable(LinuxOpenGlFunctions.GL_SCISSOR_TEST);
            SetClearColor(plan.ClearColor);
            _gl.Clear(LinuxOpenGlFunctions.GL_COLOR_BUFFER_BIT);

            _gl.Enable(LinuxOpenGlFunctions.GL_SCISSOR_TEST);
            foreach (LinuxOpenGlNativeReplayOperation operation in plan.Operations)
            {
                PixelRect rect = operation.Rect;
                _gl.Scissor(rect.X, plan.PixelHeight - rect.Bottom, rect.Width, rect.Height);
                SetClearColor(operation.Color);
                _gl.Clear(LinuxOpenGlFunctions.GL_COLOR_BUFFER_BIT);
            }

            _gl.Disable(LinuxOpenGlFunctions.GL_SCISSOR_TEST);
            BlitToDefaultFramebuffer(targetWidth, targetHeight);
            if (LinuxEglNative.SwapBuffers(_display, _surface) == LinuxEglNative.EGL_FALSE)
                throw EglFailure("eglSwapBuffers");

            _gl.Flush();
            _gl.ThrowIfError("OpenGL native replay");
        }
        finally
        {
            ReleaseCurrent();
        }
    }

    public BBitmap ReadToBitmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MakeCurrent();

        byte[] bottomUp = new byte[checked(_width * _height * BPixelBuffer.BytesPerPixel)];
        GCHandle handle = GCHandle.Alloc(bottomUp, GCHandleType.Pinned);
        try
        {
            _gl.BindFramebuffer(LinuxOpenGlFunctions.GL_READ_FRAMEBUFFER, _framebuffer);
            _gl.PixelStorei(LinuxOpenGlFunctions.GL_PACK_ALIGNMENT, 1);
            _gl.ReadPixels(
                0,
                0,
                _width,
                _height,
                LinuxOpenGlFunctions.GL_RGBA,
                LinuxOpenGlFunctions.GL_UNSIGNED_BYTE,
                handle.AddrOfPinnedObject());
            _gl.ThrowIfError("glReadPixels");
        }
        finally
        {
            handle.Free();
            ReleaseCurrent();
        }

        return LinuxOpenGlPixelConversion.FromBottomUpRgba(_width, _height, bottomUp);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_display != IntPtr.Zero)
        {
            // Bind the context on the disposing thread so GL object deletion is
            // valid (deleting with no current context is a silent no-op/leak),
            // then release before tearing the context down.
            LinuxEglNative.MakeCurrent(_display, _surface, _surface, _context);
            if (_framebuffer != 0)
            {
                uint framebuffer = _framebuffer;
                _gl.DeleteFramebuffers(1, ref framebuffer);
                _framebuffer = 0;
            }

            if (_texture != 0)
            {
                uint texture = _texture;
                _gl.DeleteTextures(1, ref texture);
                _texture = 0;
            }

            LinuxEglNative.MakeCurrent(_display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_surface != IntPtr.Zero)
                LinuxEglNative.DestroySurface(_display, _surface);
            if (_context != IntPtr.Zero)
                LinuxEglNative.DestroyContext(_display, _context);

            LinuxEglNative.Terminate(_display);
        }
    }

    private static LinuxOpenGlCpuPresentSession CreatePbuffer(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        return Create(
            width,
            height,
            LinuxEglNative.EGL_PBUFFER_BIT,
            static (display, config, state) => LinuxEglNative.CreatePbufferSurface(
                display,
                config,
                [LinuxEglNative.EGL_WIDTH, state.Width, LinuxEglNative.EGL_HEIGHT, state.Height, LinuxEglNative.EGL_NONE]),
            new NativeWindowState(IntPtr.Zero, IntPtr.Zero, width, height));
    }

    private static LinuxOpenGlCpuPresentSession Create(
        int width,
        int height,
        int surfaceType,
        Func<IntPtr, IntPtr, NativeWindowState, IntPtr> createSurface,
        NativeWindowState nativeWindow)
    {
        IntPtr display = LinuxEglNative.GetDisplay(nativeWindow.NativeDisplay);
        if (display == IntPtr.Zero)
            throw EglFailure("eglGetDisplay");

        try
        {
            if (LinuxEglNative.Initialize(display, out _, out _) == LinuxEglNative.EGL_FALSE)
                throw EglFailure("eglInitialize");

            if (LinuxEglNative.BindApi(LinuxEglNative.EGL_OPENGL_API) == LinuxEglNative.EGL_FALSE)
                throw EglFailure("eglBindAPI(EGL_OPENGL_API)");

            IntPtr config = ChooseConfig(display, surfaceType);
            IntPtr surface = createSurface(display, config, nativeWindow with { Width = width, Height = height });
            if (surface == IntPtr.Zero)
                throw EglFailure(surfaceType == LinuxEglNative.EGL_WINDOW_BIT ? "eglCreateWindowSurface" : "eglCreatePbufferSurface");

            IntPtr context = CreateContext(display, config);
            if (LinuxEglNative.MakeCurrent(display, surface, surface, context) == LinuxEglNative.EGL_FALSE)
                throw EglFailure("eglMakeCurrent");

            LinuxOpenGlFunctions gl = LinuxOpenGlFunctions.LoadCurrentContext();
            LinuxOpenGlDriverInfo driverInfo = gl.GetDriverInfo();

            // Release the context from the creating thread. EGL contexts are
            // thread-affine, and the async render loop resumes draw/readback on
            // arbitrary thread-pool threads; leaving the context bound here makes
            // the next eglMakeCurrent fail with EGL_BAD_ACCESS. Each public
            // operation re-binds and releases around its own GL work.
            LinuxEglNative.MakeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return new LinuxOpenGlCpuPresentSession(display, context, surface, gl, driverInfo, width, height);
        }
        catch
        {
            LinuxEglNative.Terminate(display);
            throw;
        }
    }

    private static IntPtr ChooseConfig(IntPtr display, int surfaceType)
    {
        int[] attributes =
        [
            LinuxEglNative.EGL_SURFACE_TYPE, surfaceType,
            LinuxEglNative.EGL_RENDERABLE_TYPE, LinuxEglNative.EGL_OPENGL_BIT,
            LinuxEglNative.EGL_RED_SIZE, 8,
            LinuxEglNative.EGL_GREEN_SIZE, 8,
            LinuxEglNative.EGL_BLUE_SIZE, 8,
            LinuxEglNative.EGL_ALPHA_SIZE, 8,
            LinuxEglNative.EGL_DEPTH_SIZE, 0,
            LinuxEglNative.EGL_STENCIL_SIZE, 0,
            LinuxEglNative.EGL_NONE,
        ];

        IntPtr[] configs = new IntPtr[1];
        if (LinuxEglNative.ChooseConfig(display, attributes, configs, configs.Length, out int count) == LinuxEglNative.EGL_FALSE || count == 0)
            throw EglFailure("eglChooseConfig");

        return configs[0];
    }

    private static IntPtr CreateContext(IntPtr display, IntPtr config)
    {
        int[][] candidates =
        [
            [
                LinuxEglNative.EGL_CONTEXT_MAJOR_VERSION, 3,
                LinuxEglNative.EGL_CONTEXT_MINOR_VERSION, 3,
                LinuxEglNative.EGL_CONTEXT_OPENGL_PROFILE_MASK, LinuxEglNative.EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT,
                LinuxEglNative.EGL_NONE,
            ],
            [
                LinuxEglNative.EGL_CONTEXT_MAJOR_VERSION, 3,
                LinuxEglNative.EGL_CONTEXT_MINOR_VERSION, 0,
                LinuxEglNative.EGL_NONE,
            ],
            [LinuxEglNative.EGL_NONE],
        ];

        foreach (int[] attributes in candidates)
        {
            IntPtr context = LinuxEglNative.CreateContext(display, config, IntPtr.Zero, attributes);
            if (context != IntPtr.Zero)
                return context;
        }

        throw EglFailure("eglCreateContext");
    }

    private void EnsureFramebuffer(int width, int height)
    {
        if (_texture != 0 && _framebuffer != 0 && width == _width && height == _height)
            return;

        if (_framebuffer != 0)
        {
            uint framebuffer = _framebuffer;
            _gl.DeleteFramebuffers(1, ref framebuffer);
            _framebuffer = 0;
        }

        if (_texture != 0)
        {
            uint texture = _texture;
            _gl.DeleteTextures(1, ref texture);
            _texture = 0;
        }

        _width = width;
        _height = height;
        _gl.GenTextures(1, out _texture);
        _gl.BindTexture(LinuxOpenGlFunctions.GL_TEXTURE_2D, _texture);
        _gl.TexParameteri(LinuxOpenGlFunctions.GL_TEXTURE_2D, LinuxOpenGlFunctions.GL_TEXTURE_MIN_FILTER, LinuxOpenGlFunctions.GL_LINEAR);
        _gl.TexParameteri(LinuxOpenGlFunctions.GL_TEXTURE_2D, LinuxOpenGlFunctions.GL_TEXTURE_MAG_FILTER, LinuxOpenGlFunctions.GL_LINEAR);
        _gl.TexParameteri(LinuxOpenGlFunctions.GL_TEXTURE_2D, LinuxOpenGlFunctions.GL_TEXTURE_WRAP_S, LinuxOpenGlFunctions.GL_CLAMP_TO_EDGE);
        _gl.TexParameteri(LinuxOpenGlFunctions.GL_TEXTURE_2D, LinuxOpenGlFunctions.GL_TEXTURE_WRAP_T, LinuxOpenGlFunctions.GL_CLAMP_TO_EDGE);
        _gl.GenFramebuffers(1, out _framebuffer);
        _gl.BindFramebuffer(LinuxOpenGlFunctions.GL_FRAMEBUFFER, _framebuffer);
        _gl.FramebufferTexture2D(
            LinuxOpenGlFunctions.GL_FRAMEBUFFER,
            LinuxOpenGlFunctions.GL_COLOR_ATTACHMENT0,
            LinuxOpenGlFunctions.GL_TEXTURE_2D,
            _texture,
            0);
    }

    private void Upload(BBitmap bitmap)
    {
        byte[] bottomUp = LinuxOpenGlPixelConversion.ToBottomUpRgba(bitmap);
        GCHandle handle = GCHandle.Alloc(bottomUp, GCHandleType.Pinned);
        try
        {
            _gl.BindTexture(LinuxOpenGlFunctions.GL_TEXTURE_2D, _texture);
            _gl.PixelStorei(LinuxOpenGlFunctions.GL_UNPACK_ALIGNMENT, 1);
            _gl.TexImage2D(
                LinuxOpenGlFunctions.GL_TEXTURE_2D,
                0,
                LinuxOpenGlFunctions.GL_RGBA8,
                bitmap.Width,
                bitmap.Height,
                0,
                LinuxOpenGlFunctions.GL_RGBA,
                LinuxOpenGlFunctions.GL_UNSIGNED_BYTE,
                handle.AddrOfPinnedObject());
            _gl.BindFramebuffer(LinuxOpenGlFunctions.GL_FRAMEBUFFER, _framebuffer);
            _gl.FramebufferTexture2D(
                LinuxOpenGlFunctions.GL_FRAMEBUFFER,
                LinuxOpenGlFunctions.GL_COLOR_ATTACHMENT0,
                LinuxOpenGlFunctions.GL_TEXTURE_2D,
                _texture,
                0);
            uint status = _gl.CheckFramebufferStatus(LinuxOpenGlFunctions.GL_FRAMEBUFFER);
            if (status != LinuxOpenGlFunctions.GL_FRAMEBUFFER_COMPLETE)
                throw new LinuxOpenGlException($"OpenGL framebuffer is incomplete: 0x{status:X}.");
            _gl.ThrowIfError("OpenGL texture upload");
        }
        finally
        {
            handle.Free();
        }
    }

    private void AllocateFramebufferTexture(int width, int height)
    {
        _gl.BindTexture(LinuxOpenGlFunctions.GL_TEXTURE_2D, _texture);
        _gl.PixelStorei(LinuxOpenGlFunctions.GL_UNPACK_ALIGNMENT, 1);
        _gl.TexImage2D(
            LinuxOpenGlFunctions.GL_TEXTURE_2D,
            0,
            LinuxOpenGlFunctions.GL_RGBA8,
            width,
            height,
            0,
            LinuxOpenGlFunctions.GL_RGBA,
            LinuxOpenGlFunctions.GL_UNSIGNED_BYTE,
            IntPtr.Zero);
        _gl.BindFramebuffer(LinuxOpenGlFunctions.GL_FRAMEBUFFER, _framebuffer);
        _gl.FramebufferTexture2D(
            LinuxOpenGlFunctions.GL_FRAMEBUFFER,
            LinuxOpenGlFunctions.GL_COLOR_ATTACHMENT0,
            LinuxOpenGlFunctions.GL_TEXTURE_2D,
            _texture,
            0);
        uint status = _gl.CheckFramebufferStatus(LinuxOpenGlFunctions.GL_FRAMEBUFFER);
        if (status != LinuxOpenGlFunctions.GL_FRAMEBUFFER_COMPLETE)
            throw new LinuxOpenGlException($"OpenGL framebuffer is incomplete: 0x{status:X}.");
    }

    private void SetClearColor(BColor color) =>
        _gl.ClearColor(color.Rf, color.Gf, color.Bf, color.Af);

    private void BlitToDefaultFramebuffer(int targetWidth, int targetHeight)
    {
        _gl.BindFramebuffer(LinuxOpenGlFunctions.GL_READ_FRAMEBUFFER, _framebuffer);
        _gl.BindFramebuffer(LinuxOpenGlFunctions.GL_DRAW_FRAMEBUFFER, 0);
        _gl.Viewport(0, 0, targetWidth, targetHeight);
        _gl.ClearColor(0, 0, 0, 0);
        _gl.Clear(LinuxOpenGlFunctions.GL_COLOR_BUFFER_BIT);
        _gl.BlitFramebuffer(
            0,
            0,
            _width,
            _height,
            0,
            0,
            targetWidth,
            targetHeight,
            LinuxOpenGlFunctions.GL_COLOR_BUFFER_BIT,
            LinuxOpenGlFunctions.GL_NEAREST);
    }

    private void MakeCurrent()
    {
        if (LinuxEglNative.MakeCurrent(_display, _surface, _surface, _context) == LinuxEglNative.EGL_FALSE)
            throw EglFailure("eglMakeCurrent");
    }

    private void ReleaseCurrent() =>
        LinuxEglNative.MakeCurrent(_display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    private static LinuxOpenGlException EglFailure(string operation) =>
        new($"{operation} failed with EGL error 0x{LinuxEglNative.GetError():X}.");

    private readonly record struct NativeWindowState(IntPtr NativeDisplay, IntPtr NativeWindow, int Width = 0, int Height = 0);
}
