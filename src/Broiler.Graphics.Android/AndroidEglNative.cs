using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Android;

/// <summary>
/// EGL entry points and constants used by the Android presentation backend.
/// </summary>
/// <remarks>
/// The shape mirrors the Linux EGL binding, but three things differ and all three are load-bearing:
/// Android binds the ES API rather than desktop GL (<see cref="EGL_OPENGL_ES_API"/> instead of
/// <c>EGL_OPENGL_API</c>), configs must request <see cref="EGL_OPENGL_ES3_BIT"/> instead of
/// <c>EGL_OPENGL_BIT</c>, and the library soname has no <c>.1</c> suffix. Getting any of them wrong
/// produces an <c>eglChooseConfig</c> or <c>eglCreateContext</c> failure that is hard to read.
/// </remarks>
internal static class AndroidEglNative
{
    public const int EGL_FALSE = 0;
    public const int EGL_TRUE = 1;

    public static readonly IntPtr EGL_NO_DISPLAY = IntPtr.Zero;
    public static readonly IntPtr EGL_NO_SURFACE = IntPtr.Zero;
    public static readonly IntPtr EGL_NO_CONTEXT = IntPtr.Zero;
    public static readonly IntPtr EGL_DEFAULT_DISPLAY = IntPtr.Zero;

    public const int EGL_NONE = 0x3038;
    public const int EGL_ALPHA_SIZE = 0x3021;
    public const int EGL_BLUE_SIZE = 0x3022;
    public const int EGL_GREEN_SIZE = 0x3023;
    public const int EGL_RED_SIZE = 0x3024;
    public const int EGL_DEPTH_SIZE = 0x3025;
    public const int EGL_STENCIL_SIZE = 0x3026;
    public const int EGL_SURFACE_TYPE = 0x3033;
    public const int EGL_RENDERABLE_TYPE = 0x3040;
    public const int EGL_NATIVE_VISUAL_ID = 0x302E;
    public const int EGL_HEIGHT = 0x3056;
    public const int EGL_WIDTH = 0x3057;

    /// <summary>Also spelled <c>EGL_CONTEXT_MAJOR_VERSION</c> in EGL 1.5; the value is the same.</summary>
    public const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;

    public const int EGL_PBUFFER_BIT = 0x0001;
    public const int EGL_WINDOW_BIT = 0x0004;

    public const int EGL_OPENGL_ES_BIT = 0x0001;
    public const int EGL_OPENGL_ES2_BIT = 0x0004;
    public const int EGL_OPENGL_ES3_BIT = 0x0040;

    public const int EGL_OPENGL_ES_API = 0x30A0;

    public const int EGL_SUCCESS = 0x3000;
    public const int EGL_CONTEXT_LOST = 0x300E;
    public const int EGL_BAD_SURFACE = 0x300D;
    public const int EGL_BAD_NATIVE_WINDOW = 0x300B;

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglGetDisplay")]
    public static extern IntPtr GetDisplay(IntPtr displayId);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglInitialize")]
    public static extern int Initialize(IntPtr display, out int major, out int minor);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglTerminate")]
    public static extern int Terminate(IntPtr display);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglBindAPI")]
    public static extern int BindApi(int api);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglChooseConfig")]
    public static extern int ChooseConfig(
        IntPtr display,
        int[] attribList,
        IntPtr[] configs,
        int configSize,
        out int numConfig);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglGetConfigAttrib")]
    public static extern int GetConfigAttrib(IntPtr display, IntPtr config, int attribute, out int value);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglCreateContext")]
    public static extern IntPtr CreateContext(IntPtr display, IntPtr config, IntPtr shareContext, int[] attribList);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglDestroyContext")]
    public static extern int DestroyContext(IntPtr display, IntPtr context);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglCreatePbufferSurface")]
    public static extern IntPtr CreatePbufferSurface(IntPtr display, IntPtr config, int[] attribList);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglCreateWindowSurface")]
    public static extern IntPtr CreateWindowSurface(IntPtr display, IntPtr config, IntPtr nativeWindow, int[]? attribList);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglDestroySurface")]
    public static extern int DestroySurface(IntPtr display, IntPtr surface);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglMakeCurrent")]
    public static extern int MakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglSwapBuffers")]
    public static extern int SwapBuffers(IntPtr display, IntPtr surface);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglSwapInterval")]
    public static extern int SwapInterval(IntPtr display, int interval);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglQuerySurface")]
    public static extern int QuerySurface(IntPtr display, IntPtr surface, int attribute, out int value);

    [DllImport(AndroidNativeLibraries.Egl, EntryPoint = "eglGetError")]
    public static extern int GetError();
}
