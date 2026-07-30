using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Broiler.Graphics.Android.Tests;

/// <summary>
/// Covers the parts of the Android presentation backend that run without a GPU: geometry, pixel
/// orientation, EGL attribute construction, the dependency probe, and the surface state machine.
///
/// What it deliberately does not cover is the native path — creating a context, uploading a
/// texture, blitting, and reading pixels back all need a real EGL implementation. Those belong to
/// the phase A1 hardware gate in the root roadmap and cannot be asserted here.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string Name, Action Body)>
        {
            ("surface geometry scales logical size by density", GeometryScalesByDensity),
            ("surface geometry repairs a bad density", GeometryRepairsBadDensity),
            ("surface geometry rejects a non-positive size", GeometryRejectsBadSize),
            ("pixel conversion round-trips through bottom-up order", PixelConversionRoundTrips),
            ("pixel conversion actually flips rows", PixelConversionFlipsRows),
            ("pixel conversion rejects a mismatched buffer", PixelConversionRejectsMismatch),
            ("egl constants match the android values", EglConstantsMatchAndroid),
            ("gles entry points are the es3 blit set", GlesEntryPointsAreEs3),
            ("dependency probe reports every requirement", DependencyProbeReportsRequirements),
            ("window surface starts detached and unpresentable", WindowSurfaceStartsDetached),
            ("window surface drops frames while detached", WindowSurfaceDropsFramesWhileDetached),
            ("window surface rejects a null native window", WindowSurfaceRejectsNullWindow),
            ("renderer rejects a foreign surface", RendererRejectsForeignSurface),
            ("renderer disposes cleanly without a gpu", RendererDisposesWithoutGpu),
            ("android backend avoids android sdk references", BackendAvoidsAndroidSdkReferences),
            ("fallback font list covers android system fonts", FallbackFontCoversAndroid),
        };

        int failures = 0;
        foreach ((string name, Action body) in tests)
        {
            try
            {
                body();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"All {tests.Count} Android graphics tests passed."
            : $"{failures} of {tests.Count} Android graphics tests failed.");
        return failures;
    }

    // ---- geometry -----------------------------------------------------------------------------

    private static void GeometryScalesByDensity()
    {
        AssertEqual(200, AndroidSurfaceGeometry.ToPixels(100, 2.0), "A 2x density doubles the pixel size.");
        AssertEqual(100, AndroidSurfaceGeometry.ToPixels(100, 1.0), "A 1x density is unchanged.");

        // Fractional densities are the norm on Android (2.625 on many phones); rounding down would
        // leave a row of unpainted pixels at the edge of the surface.
        AssertEqual(263, AndroidSurfaceGeometry.ToPixels(100, 2.625), "A fractional density rounds up.");
    }

    private static void GeometryRepairsBadDensity()
    {
        BSurfaceDescriptor repaired = AndroidSurfaceGeometry.Validate(
            new BSurfaceDescriptor(new BSize(320, 480), 0));
        AssertEqual(1.0, repaired.DpiScale, "A zero density is corrected to 1.0 rather than producing a zero-sized texture.");

        BSurfaceDescriptor nan = AndroidSurfaceGeometry.Validate(
            new BSurfaceDescriptor(new BSize(320, 480), double.NaN));
        AssertEqual(1.0, nan.DpiScale, "A NaN density is corrected to 1.0.");
    }

    private static void GeometryRejectsBadSize()
    {
        AssertThrows<ArgumentOutOfRangeException>(
            () => AndroidSurfaceGeometry.Validate(new BSurfaceDescriptor(new BSize(0, 480), 1.0)),
            "A zero width is rejected.");
        AssertThrows<ArgumentOutOfRangeException>(
            () => AndroidSurfaceGeometry.Validate(new BSurfaceDescriptor(new BSize(320, double.PositiveInfinity), 1.0)),
            "An infinite height is rejected.");
    }

    // ---- pixel orientation --------------------------------------------------------------------

    private static void PixelConversionRoundTrips()
    {
        using BBitmap source = CreateGradient(4, 3);
        byte[] bottomUp = AndroidGlesPixelConversion.ToBottomUpRgba(source);
        using BBitmap roundTripped = AndroidGlesPixelConversion.FromBottomUpRgba(4, 3, bottomUp);

        AssertTrue(source.Rgba.SequenceEqual(roundTripped.Rgba), "Converting to bottom-up and back is lossless.");
    }

    private static void PixelConversionFlipsRows()
    {
        // Broiler bitmaps are top-down; GL textures and glReadPixels are bottom-up. Getting this
        // wrong renders the whole frame upside down, which no other assertion would catch.
        using BBitmap source = CreateGradient(2, 2);
        byte[] bottomUp = AndroidGlesPixelConversion.ToBottomUpRgba(source);

        int rowBytes = 2 * BPixelBuffer.BytesPerPixel;
        AssertTrue(
            source.Rgba.Slice(0, rowBytes).SequenceEqual(bottomUp.AsSpan(rowBytes, rowBytes)),
            "The first source row becomes the last row of the bottom-up buffer.");
    }

    private static void PixelConversionRejectsMismatch()
    {
        AssertThrows<ArgumentException>(
            () => AndroidGlesPixelConversion.FromBottomUpRgba(4, 4, new byte[8]),
            "A buffer that does not match the dimensions is rejected.");
    }

    // ---- native contract ----------------------------------------------------------------------

    private static void EglConstantsMatchAndroid()
    {
        // These three are exactly where the Linux backend cannot be copied: Android has no desktop
        // GL, so binding EGL_OPENGL_API or asking for EGL_OPENGL_BIT matches nothing.
        AssertEqual(0x30A0, AndroidEglNative.EGL_OPENGL_ES_API, "EGL_OPENGL_ES_API is 0x30A0, not the desktop 0x30A2.");
        AssertEqual(0x0040, AndroidEglNative.EGL_OPENGL_ES3_BIT, "EGL_OPENGL_ES3_BIT is 0x40, not the desktop EGL_OPENGL_BIT 0x8.");
        AssertEqual(0x3098, AndroidEglNative.EGL_CONTEXT_CLIENT_VERSION, "EGL_CONTEXT_CLIENT_VERSION is 0x3098.");
        AssertEqual(0x300E, AndroidEglNative.EGL_CONTEXT_LOST, "EGL_CONTEXT_LOST is 0x300E.");

        AssertTrue(
            AndroidNativeLibraries.EglCandidates.Contains("libEGL.so"),
            "The Android EGL soname has no .1 suffix and must be a candidate.");
        AssertTrue(
            AndroidNativeLibraries.GlesCandidates.Contains("libGLESv2.so"),
            "libGLESv2 is a fallback, because some devices have no libGLESv3 soname.");
    }

    private static void GlesEntryPointsAreEs3()
    {
        // glBlitFramebuffer is ES 3.0. It is what fixes the backend's feature floor, so if it ever
        // disappears from the import surface the ES 3 requirement has silently changed.
        MethodInfo? blit = typeof(AndroidGlesNative).GetMethod(
            "BlitFramebuffer",
            BindingFlags.Public | BindingFlags.Static);
        AssertTrue(blit is not null, "glBlitFramebuffer is imported.");

        // No shader entry points: the backend uploads and blits, it does not draw.
        string[] shaderCalls = ["CreateShader", "CompileShader", "CreateProgram", "DrawArrays", "DrawElements"];
        foreach (string name in shaderCalls)
        {
            AssertTrue(
                typeof(AndroidGlesNative).GetMethod(name, BindingFlags.Public | BindingFlags.Static) is null,
                $"{name} is absent; the backend has no shader pipeline.");
        }
    }

    private static void DependencyProbeReportsRequirements()
    {
        IReadOnlyList<AndroidNativeLibraryStatus> statuses = AndroidGraphicsDependencies.CheckPresentationBaseline();

        AssertEqual(3, statuses.Count, "EGL, OpenGL ES, and the native-window API are all probed.");
        AssertTrue(statuses.All(static s => !string.IsNullOrWhiteSpace(s.Diagnostic)), "Every status carries a diagnostic.");
        AssertTrue(
            statuses.Any(static s => s.Id == "egl") &&
            statuses.Any(static s => s.Id == "opengl-es") &&
            statuses.Any(static s => s.Id == "android-native-window"),
            "Each requirement is identified.");

        // On this host the libraries are absent. Reporting that is the correct answer, not an error:
        // it is what lets a host print an honest startup diagnostic instead of failing at eglGetDisplay.
        foreach (AndroidNativeLibraryStatus status in statuses.Where(static s => !s.IsAvailable))
            AssertTrue(status.Diagnostic.Contains("not found", StringComparison.Ordinal), "A missing library says so.");
    }

    // ---- surface state machine -----------------------------------------------------------------

    private static void WindowSurfaceStartsDetached()
    {
        using AndroidOpenGlEsRenderer renderer = new(NoGpuOptions());
        using AndroidOpenGlEsWindowSurface surface = renderer.CreateWindowSurface(Descriptor(320, 480, 2.0));

        AssertTrue(!surface.IsAttached, "A new window surface has no Android surface yet.");
        AssertTrue(!surface.IsGpuBacked, "No context exists before a window is attached.");
        AssertEqual(320.0, surface.Size.Width, "The descriptor size is preserved.");
        AssertEqual(2.0, surface.DpiScale, "The density is preserved.");
    }

    private static void WindowSurfaceDropsFramesWhileDetached()
    {
        using AndroidOpenGlEsRenderer renderer = new(NoGpuOptions());
        using AndroidOpenGlEsWindowSurface surface = renderer.CreateWindowSurface(Descriptor(64, 64, 1.0));
        using BBitmap frame = CreateGradient(64, 64);

        // Frames arrive between surfaceDestroyed and the next surfaceCreated during any rotation.
        // Dropping them must not throw, or a rotation would take the Activity down.
        surface.Present(frame, vsync: true);
        AssertTrue(surface.Diagnostic.Contains("not presented", StringComparison.Ordinal), "The dropped frame is reported honestly.");

        // The CPU frame is still retained, so readback returns real content rather than a blank.
        using BBitmap read = surface.ReadToBitmap();
        AssertEqual(64, read.Width, "Readback returns the retained frame.");
        AssertTrue(read.Rgba.SequenceEqual(frame.Rgba), "The retained frame is the one that was presented.");
    }

    private static void WindowSurfaceRejectsNullWindow()
    {
        using AndroidOpenGlEsRenderer renderer = new(NoGpuOptions());
        using AndroidOpenGlEsWindowSurface surface = renderer.CreateWindowSurface(Descriptor(64, 64, 1.0));

        AssertThrows<ArgumentException>(
            () => surface.AttachNativeWindow(IntPtr.Zero),
            "A null native window is rejected rather than passed to EGL.");

        // Detaching when nothing is attached is a no-op: surfaceDestroyed can arrive without a
        // matching surfaceCreated when an Activity is torn down early.
        surface.DetachNativeWindow();
    }

    // ---- renderer -------------------------------------------------------------------------------

    private static void RendererRejectsForeignSurface()
    {
        using AndroidOpenGlEsRenderer renderer = new(NoGpuOptions());
        using BImageRenderer cpu = new();
        using IBroilerSurface foreign = cpu.CreateSurface(Descriptor(32, 32, 1.0));

        AssertThrows<ArgumentException>(
            () => renderer.Render(foreign, new BRenderList(), BFrameContext.Default),
            "A surface from another renderer is rejected.");
    }

    private static void RendererDisposesWithoutGpu()
    {
        AndroidOpenGlEsRenderer renderer = new(NoGpuOptions());
        renderer.Dispose();
        renderer.Dispose();

        AssertThrows<ObjectDisposedException>(
            () => renderer.CreateSurface(Descriptor(16, 16, 1.0)),
            "A disposed renderer refuses to create surfaces.");
    }

    // ---- boundary --------------------------------------------------------------------------------

    private static void BackendAvoidsAndroidSdkReferences()
    {
        // The backend reaches Android through P/Invoke only, which is what lets it build and be
        // tested without the android workload and keeps Android managed types out of Graphics.
        Assembly assembly = typeof(AndroidOpenGlEsRenderer).Assembly;
        string[] forbidden = ["Mono.Android", "Java.Interop", "Xamarin.Android", "Microsoft.Maui"];

        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            foreach (string name in forbidden)
            {
                AssertTrue(
                    !reference.Name!.StartsWith(name, StringComparison.OrdinalIgnoreCase),
                    $"Broiler.Graphics.Android must not reference {reference.Name}.");
            }

            AssertTrue(
                !reference.Name!.StartsWith("Broiler.UI", StringComparison.Ordinal) &&
                !reference.Name.StartsWith("Broiler.Input", StringComparison.Ordinal),
                $"Broiler.Graphics.Android must not reference {reference.Name}.");
        }
    }

    private static void FallbackFontCoversAndroid()
    {
        // Android keeps every system face in /system/fonts, which none of the Linux, Windows, or
        // macOS roots reach. Without it an Android build renders no text at all.
        string source = typeof(BImageRenderer).Assembly.Location;
        AssertTrue(source.Length > 0, "The graphics assembly is loadable.");

        MethodInfo? roots = typeof(BImageRenderer).Assembly
            .GetType("Broiler.Graphics.FallbackSystemFont")
            ?.GetMethod("FontRoots", BindingFlags.NonPublic | BindingFlags.Static);
        AssertTrue(roots is not null, "FallbackSystemFont.FontRoots is present.");

        var enumerated = (System.Collections.IEnumerable)roots!.Invoke(null, null)!;
        List<string> values = enumerated.Cast<string>().ToList();
        AssertTrue(values.Contains("/system/fonts"), "The Android system font directory is probed.");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// Options that keep every test off the GPU. There is no EGL implementation on this host, and a
    /// test that depended on one would be a hardware test wearing a unit test's clothes.
    /// </summary>
    private static AndroidOpenGlEsRendererOptions NoGpuOptions() =>
        new(TryCreateEglContext: false, AllowCpuFallbackWhenOpenGlUnavailable: true);

    private static BSurfaceDescriptor Descriptor(double width, double height, double density) =>
        new(new BSize(width, height), density);

    private static BBitmap CreateGradient(int width, int height)
    {
        byte[] rgba = new byte[width * height * BPixelBuffer.BytesPerPixel];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = ((y * width) + x) * BPixelBuffer.BytesPerPixel;
                rgba[offset] = (byte)((x * 255) / Math.Max(1, width - 1));
                rgba[offset + 1] = (byte)((y * 255) / Math.Max(1, height - 1));
                rgba[offset + 2] = 0x40;
                rgba[offset + 3] = 0xFF;
            }
        }

        return new BBitmap(width, height, rgba, takeOwnership: true);
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
