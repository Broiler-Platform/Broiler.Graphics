using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Linux.OpenGL;

internal static class LinuxEglNative
{
    public const int EGL_FALSE = 0;
    public const int EGL_TRUE = 1;

    public const int EGL_NONE = 0x3038;
    public const int EGL_RED_SIZE = 0x3024;
    public const int EGL_GREEN_SIZE = 0x3023;
    public const int EGL_BLUE_SIZE = 0x3022;
    public const int EGL_ALPHA_SIZE = 0x3021;
    public const int EGL_DEPTH_SIZE = 0x3025;
    public const int EGL_STENCIL_SIZE = 0x3026;
    public const int EGL_SURFACE_TYPE = 0x3033;
    public const int EGL_RENDERABLE_TYPE = 0x3040;
    public const int EGL_WIDTH = 0x3057;
    public const int EGL_HEIGHT = 0x3056;
    public const int EGL_CONTEXT_MAJOR_VERSION = 0x3098;
    public const int EGL_CONTEXT_MINOR_VERSION = 0x30FB;
    public const int EGL_CONTEXT_OPENGL_PROFILE_MASK = 0x30FD;

    public const int EGL_PBUFFER_BIT = 0x0001;
    public const int EGL_WINDOW_BIT = 0x0004;
    public const int EGL_OPENGL_BIT = 0x0008;
    public const int EGL_OPENGL_API = 0x30A2;
    public const int EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT = 0x00000001;

    [DllImport("libEGL.so.1", EntryPoint = "eglGetDisplay")]
    public static extern IntPtr GetDisplay(IntPtr displayId);

    [DllImport("libEGL.so.1", EntryPoint = "eglInitialize")]
    public static extern int Initialize(IntPtr display, out int major, out int minor);

    [DllImport("libEGL.so.1", EntryPoint = "eglTerminate")]
    public static extern int Terminate(IntPtr display);

    [DllImport("libEGL.so.1", EntryPoint = "eglBindAPI")]
    public static extern int BindApi(int api);

    [DllImport("libEGL.so.1", EntryPoint = "eglChooseConfig")]
    public static extern int ChooseConfig(
        IntPtr display,
        int[] attribList,
        IntPtr[] configs,
        int configSize,
        out int numConfig);

    [DllImport("libEGL.so.1", EntryPoint = "eglCreateContext")]
    public static extern IntPtr CreateContext(
        IntPtr display,
        IntPtr config,
        IntPtr shareContext,
        int[] attribList);

    [DllImport("libEGL.so.1", EntryPoint = "eglDestroyContext")]
    public static extern int DestroyContext(IntPtr display, IntPtr context);

    [DllImport("libEGL.so.1", EntryPoint = "eglCreatePbufferSurface")]
    public static extern IntPtr CreatePbufferSurface(
        IntPtr display,
        IntPtr config,
        int[] attribList);

    [DllImport("libEGL.so.1", EntryPoint = "eglCreateWindowSurface")]
    public static extern IntPtr CreateWindowSurface(
        IntPtr display,
        IntPtr config,
        IntPtr nativeWindow,
        int[] attribList);

    [DllImport("libEGL.so.1", EntryPoint = "eglDestroySurface")]
    public static extern int DestroySurface(IntPtr display, IntPtr surface);

    [DllImport("libEGL.so.1", EntryPoint = "eglMakeCurrent")]
    public static extern int MakeCurrent(
        IntPtr display,
        IntPtr draw,
        IntPtr read,
        IntPtr context);

    [DllImport("libEGL.so.1", EntryPoint = "eglSwapBuffers")]
    public static extern int SwapBuffers(IntPtr display, IntPtr surface);

    [DllImport("libEGL.so.1", EntryPoint = "eglGetError")]
    public static extern int GetError();

    [DllImport("libEGL.so.1", EntryPoint = "eglGetProcAddress")]
    public static extern IntPtr GetProcAddress([MarshalAs(UnmanagedType.LPUTF8Str)] string procName);
}
