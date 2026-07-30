using System;

namespace Broiler.Graphics.Android;

/// <summary>
/// Converts a Java <c>Surface</c> into the <c>ANativeWindow*</c> that EGL needs, without pulling an
/// Android managed type into Graphics.
/// </summary>
/// <remarks>
/// Both arguments to <c>ANativeWindow_fromSurface</c> are plain pointers, so a host can supply them
/// from .NET for Android as <c>JniEnvironment.EnvironmentPointer</c> and <c>surface.Handle</c>:
///
/// <code>
/// IntPtr window = AndroidNativeWindow.FromSurface(
///     JniEnvironment.EnvironmentPointer,
///     holder.Surface!.Handle);
/// try
/// {
///     surface.AttachNativeWindow(window);
///     // ... present frames ...
/// }
/// finally
/// {
///     surface.DetachNativeWindow();
///     AndroidNativeWindow.Release(window);
/// }
/// </code>
///
/// The returned window carries a reference the caller must release. Release it only after
/// <see cref="AndroidOpenGlEsWindowSurface.DetachNativeWindow"/> has returned, because EGL still
/// touches the window while its drawing surface exists.
/// </remarks>
public static class AndroidNativeWindow
{
    /// <summary>
    /// Acquires an <c>ANativeWindow*</c> for a Java <c>Surface</c>. The caller owns the returned
    /// reference and must pass it to <see cref="Release"/>.
    /// </summary>
    /// <param name="jniEnvironment">A <c>JNIEnv*</c> for the calling thread.</param>
    /// <param name="surface">A local or global reference to an <c>android.view.Surface</c>.</param>
    public static IntPtr FromSurface(IntPtr jniEnvironment, IntPtr surface)
    {
        if (jniEnvironment == IntPtr.Zero)
            throw new ArgumentException("A JNIEnv pointer is required.", nameof(jniEnvironment));
        if (surface == IntPtr.Zero)
            throw new ArgumentException("A Surface handle is required.", nameof(surface));

        AndroidNativeLibraries.EnsureRegistered();

        IntPtr window = AndroidNativeWindowNative.FromSurface(jniEnvironment, surface);
        if (window == IntPtr.Zero)
            throw new AndroidOpenGlEsException("ANativeWindow_fromSurface returned no window for the supplied Surface.");

        return window;
    }

    /// <summary>Releases a window reference obtained from <see cref="FromSurface"/>.</summary>
    public static void Release(IntPtr nativeWindow)
    {
        if (nativeWindow == IntPtr.Zero)
            return;

        AndroidNativeLibraries.EnsureRegistered();
        AndroidNativeWindowNative.Release(nativeWindow);
    }

    /// <summary>The window's current width in physical pixels, or 0 when unavailable.</summary>
    public static int GetWidth(IntPtr nativeWindow)
    {
        if (nativeWindow == IntPtr.Zero)
            return 0;

        AndroidNativeLibraries.EnsureRegistered();
        return AndroidNativeWindowNative.GetWidth(nativeWindow);
    }

    /// <summary>The window's current height in physical pixels, or 0 when unavailable.</summary>
    public static int GetHeight(IntPtr nativeWindow)
    {
        if (nativeWindow == IntPtr.Zero)
            return 0;

        AndroidNativeLibraries.EnsureRegistered();
        return AndroidNativeWindowNative.GetHeight(nativeWindow);
    }
}
