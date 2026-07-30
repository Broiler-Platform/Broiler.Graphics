using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Android;

/// <summary>
/// The <c>ANativeWindow</c> API from <c>libandroid.so</c>.
/// </summary>
/// <remarks>
/// This is the whole reason the backend can target plain <c>net10.0</c>. EGL needs an
/// <c>ANativeWindow*</c>, which is obtained from a Java <c>Surface</c> — but
/// <c>ANativeWindow_fromSurface</c> takes a <c>JNIEnv*</c> and a <c>jobject</c>, and both are just
/// pointers. A host holding a <c>SurfaceView</c> passes
/// <c>JniEnvironment.EnvironmentPointer</c> and <c>surface.Handle</c>, and no managed Android type
/// crosses into Graphics.
///
/// Ownership: <see cref="FromSurface"/> returns a reference the caller must release with
/// <see cref="Release"/>. <see cref="AndroidOpenGlEsWindowSurface"/> does not take ownership of a
/// window handed to it, because the host's surface lifecycle already owns it.
/// </remarks>
internal static class AndroidNativeWindowNative
{
    /// <summary>Matches <c>WINDOW_FORMAT_RGBA_8888</c>.</summary>
    public const int WindowFormatRgba8888 = 1;

    /// <summary>Matches <c>WINDOW_FORMAT_RGBX_8888</c>.</summary>
    public const int WindowFormatRgbx8888 = 2;

    [DllImport(AndroidNativeLibraries.AndroidRuntime, EntryPoint = "ANativeWindow_fromSurface")]
    public static extern IntPtr FromSurface(IntPtr jniEnvironment, IntPtr surface);

    [DllImport(AndroidNativeLibraries.AndroidRuntime, EntryPoint = "ANativeWindow_acquire")]
    public static extern void Acquire(IntPtr window);

    [DllImport(AndroidNativeLibraries.AndroidRuntime, EntryPoint = "ANativeWindow_release")]
    public static extern void Release(IntPtr window);

    [DllImport(AndroidNativeLibraries.AndroidRuntime, EntryPoint = "ANativeWindow_getWidth")]
    public static extern int GetWidth(IntPtr window);

    [DllImport(AndroidNativeLibraries.AndroidRuntime, EntryPoint = "ANativeWindow_getHeight")]
    public static extern int GetHeight(IntPtr window);

    [DllImport(AndroidNativeLibraries.AndroidRuntime, EntryPoint = "ANativeWindow_getFormat")]
    public static extern int GetFormat(IntPtr window);

    [DllImport(AndroidNativeLibraries.AndroidRuntime, EntryPoint = "ANativeWindow_setBuffersGeometry")]
    public static extern int SetBuffersGeometry(IntPtr window, int width, int height, int format);
}
