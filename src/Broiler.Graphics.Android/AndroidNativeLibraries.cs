using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Android;

/// <summary>
/// Resolves the EGL, GLES, and native-window libraries this backend imports.
/// </summary>
/// <remarks>
/// The <c>DllImport</c> names below are the canonical Android sonames, but two of them vary in
/// practice: desktop-style hosts expose EGL as <c>libEGL.so.1</c>, and on some devices
/// <c>libGLESv3.so</c> is absent while <c>libGLESv2.so</c> exports the ES 3 entry points anyway
/// (Android's "GLESv2" library has carried ES 3 since API 18). A resolver with a candidate list
/// keeps a working device from failing on a naming detail.
/// </remarks>
internal static class AndroidNativeLibraries
{
    /// <summary>Import name for EGL. Resolved against <see cref="EglCandidates"/>.</summary>
    public const string Egl = "libEGL.so";

    /// <summary>Import name for OpenGL ES. Resolved against <see cref="GlesCandidates"/>.</summary>
    public const string Gles = "libGLESv3.so";

    /// <summary>Import name for the Android native-window API.</summary>
    public const string AndroidRuntime = "libandroid.so";

    public static IReadOnlyList<string> EglCandidates { get; } = ["libEGL.so", "libEGL.so.1"];

    public static IReadOnlyList<string> GlesCandidates { get; } =
        ["libGLESv3.so", "libGLESv2.so", "libGLESv2.so.2"];

    private static bool s_registered;
    private static readonly object s_gate = new();

    /// <summary>
    /// Registers the resolver. Safe to call repeatedly; only the first call installs it.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (s_registered)
            return;

        lock (s_gate)
        {
            if (s_registered)
                return;

            NativeLibrary.SetDllImportResolver(typeof(AndroidNativeLibraries).Assembly, Resolve);
            s_registered = true;
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        IReadOnlyList<string>? candidates = libraryName switch
        {
            Egl => EglCandidates,
            Gles => GlesCandidates,
            _ => null,
        };

        if (candidates is null)
            return IntPtr.Zero;

        foreach (string candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out IntPtr handle))
                return handle;
        }

        // Zero lets the runtime fall back to its own probing, which produces the normal
        // DllNotFoundException with the original name rather than a resolver-shaped error.
        return IntPtr.Zero;
    }

    /// <summary>Reports whether a library can be loaded, for the dependency probe.</summary>
    public static bool TryLoadAny(IReadOnlyList<string> candidates, out string resolvedName)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (string candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                NativeLibrary.Free(handle);
                resolvedName = candidate;
                return true;
            }
        }

        resolvedName = string.Empty;
        return false;
    }
}
