using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Broiler.Graphics.Linux.OpenGL;
using Broiler.Graphics.Linux.Vulkan;
using Broiler.Media;
using Broiler.Media.Image.Managed;

namespace Broiler.Graphics.Linux.Tests;

internal static class Program
{
    private static int Main()
    {
        // Composition root: register the concrete image codecs Broiler.Graphics decodes/encodes with.
        BImageCodecs.Use(new MediaCodecCatalog(ManagedImageCodecs.CreateCodecs()));

        var tests = new List<(string Name, Action Body)>
        {
            ("linux graphics baseline dependency probe is stable", BaselineDependencyProbeIsStable),
            ("linux graphics runtime diagnostics are stable", RuntimeDiagnosticsAreStable),
            ("opengl dependency probe is scoped", OpenGlDependencyProbeIsScoped),
            ("opengl driver info formats diagnostics", OpenGlDriverInfoFormatsDiagnostics),
            ("vulkan dependency probe is scoped", VulkanDependencyProbeIsScoped),
            ("vulkan device info formats diagnostics", VulkanDeviceInfoFormatsDiagnostics),
            ("linux graphics assemblies avoid windows references", LinuxAssembliesAvoidWindowsReferences),
            ("opengl render-to-image matches managed baseline", OpenGlRenderToImageMatchesManagedBaseline),
            ("opengl surface stores cpu-present frame", OpenGlSurfaceStoresCpuPresentFrame),
            ("opengl pixel conversion preserves top-down order", OpenGlPixelConversionPreservesTopDownOrder),
            ("opengl native replay accepts solid rect subset", OpenGlNativeReplayAcceptsSolidRectSubset),
            ("opengl native replay rejects unsupported commands", OpenGlNativeReplayRejectsUnsupportedCommands),
            ("opengl x11 window surface is linux-only", OpenGlX11WindowSurfaceIsLinuxOnly),
            ("vulkan render-to-image matches managed baseline", VulkanRenderToImageMatchesManagedBaseline),
            ("vulkan surface stores cpu-present frame", VulkanSurfaceStoresCpuPresentFrame),
            ("vulkan strict device creation is linux-only", VulkanStrictDeviceCreationIsLinuxOnly),
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

        return failures;
    }

    private static void BaselineDependencyProbeIsStable()
    {
        IReadOnlyList<LinuxNativeLibraryStatus> statuses = LinuxGraphicsDependencies.CheckWindowingBaseline();
        AssertContains(statuses, "egl");
        AssertContains(statuses, "opengl");
        AssertContains(statuses, "vulkan");
        AssertContains(statuses, "wayland-client");
        AssertContains(statuses, "xcb");
        AssertContains(statuses, "x11");
        AssertTrue(statuses.All(static status => !string.IsNullOrWhiteSpace(status.Diagnostic)),
            "Every dependency status should carry an actionable diagnostic.");
    }

    private static void OpenGlDependencyProbeIsScoped()
    {
        string[] ids = LinuxOpenGlRenderer.CheckDependencies().Select(static status => status.Id).ToArray();
        AssertTrue(ids.Contains("egl", StringComparer.Ordinal), "OpenGL probe should include EGL.");
        AssertTrue(ids.Contains("opengl", StringComparer.Ordinal), "OpenGL probe should include OpenGL.");
        AssertTrue(ids.Contains("x11", StringComparer.Ordinal), "OpenGL probe should include the first concrete X11 window path.");
        AssertFalse(ids.Contains("vulkan", StringComparer.Ordinal), "OpenGL probe should not require Vulkan.");
    }

    private static void RuntimeDiagnosticsAreStable()
    {
        LinuxGraphicsRuntimeReport report = LinuxGraphicsRuntimeDiagnostics.Capture();
        AssertFalse(string.IsNullOrWhiteSpace(report.OperatingSystemDescription), "Runtime diagnostics should include the OS description.");
        AssertFalse(string.IsNullOrWhiteSpace(report.RuntimeIdentifier), "Runtime diagnostics should include the runtime identifier.");
        AssertFalse(string.IsNullOrWhiteSpace(report.FrameworkDescription), "Runtime diagnostics should include the framework description.");
        AssertFalse(string.IsNullOrWhiteSpace(report.DisplayServer.Diagnostic), "Display diagnostics should explain the display-server state.");
    }

    private static void OpenGlDriverInfoFormatsDiagnostics()
    {
        var info = new LinuxOpenGlDriverInfo("Mesa", "llvmpipe", "4.5", "4.50");
        string diagnostic = info.ToDiagnosticString();
        AssertTrue(diagnostic.Contains("Mesa", StringComparison.Ordinal), "OpenGL driver diagnostic should include the vendor.");
        AssertTrue(diagnostic.Contains("llvmpipe", StringComparison.Ordinal), "OpenGL driver diagnostic should include the renderer.");
        AssertTrue(diagnostic.Contains("4.5", StringComparison.Ordinal), "OpenGL driver diagnostic should include the GL version.");
    }

    private static void VulkanDependencyProbeIsScoped()
    {
        string[] ids = LinuxVulkanRenderer.CheckDependencies().Select(static status => status.Id).ToArray();
        AssertTrue(ids.Contains("vulkan", StringComparer.Ordinal), "Vulkan probe should include Vulkan.");
        AssertTrue(ids.Contains("wayland-client", StringComparer.Ordinal), "Vulkan probe should include the planned Wayland WSI path.");
        AssertTrue(ids.Contains("xcb", StringComparer.Ordinal), "Vulkan probe should include the planned XCB WSI path.");
        AssertFalse(ids.Contains("egl", StringComparer.Ordinal), "Vulkan probe should not require EGL.");
    }

    private static void VulkanDeviceInfoFormatsDiagnostics()
    {
        var info = new LinuxVulkanDeviceInfo("lavapipe", "cpu", "1.3.0", "0x01020304", 0x10005, 0x0001);
        string diagnostic = info.ToDiagnosticString();
        AssertTrue(diagnostic.Contains("lavapipe", StringComparison.Ordinal), "Vulkan device diagnostic should include the device name.");
        AssertTrue(diagnostic.Contains("cpu", StringComparison.Ordinal), "Vulkan device diagnostic should include the device type.");
        AssertTrue(diagnostic.Contains("api=1.3.0", StringComparison.Ordinal), "Vulkan device diagnostic should include the device API version.");
    }

    private static void LinuxAssembliesAvoidWindowsReferences()
    {
        Assembly[] assemblies =
        [
            typeof(LinuxGraphicsDependencies).Assembly,
            typeof(LinuxOpenGlRenderer).Assembly,
            typeof(LinuxVulkanRenderer).Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            string[] references = assembly.GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty)
                .ToArray();
            AssertFalse(references.Any(static reference => reference.Contains("Windows", StringComparison.OrdinalIgnoreCase)),
                $"{assembly.GetName().Name} must not reference Windows assemblies.");
        }
    }

    private static void OpenGlRenderToImageMatchesManagedBaseline()
    {
        var list = new BRenderList();
        list.FillRect(new BRect(1, 1, 4, 4), BColor.Red);
        list.PushClip(new BRect(3, 3, 4, 4));
        list.FillRect(new BRect(0, 0, 8, 8), BColor.Blue);
        list.PopClip();

        BSurfaceDescriptor descriptor = BSurfaceDescriptor.Default(new BSize(8, 8));
        BFrameContext frame = new(BColor.White);

        using BImageRenderer managed = new();
        using LinuxOpenGlRenderer openGl = new(new LinuxOpenGlRendererOptions(TryCreateEglContext: false));
        using BBitmap expected = managed.RenderToImage(list, descriptor, frame);
        using BBitmap actual = openGl.RenderToImage(list, descriptor, frame);

        AssertEqual(expected.Width, actual.Width, "OpenGL CPU-present width should match baseline.");
        AssertEqual(expected.Height, actual.Height, "OpenGL CPU-present height should match baseline.");
        AssertEqual(expected.GetPixel(0, 0), actual.GetPixel(0, 0), "Clear pixel should match baseline.");
        AssertEqual(expected.GetPixel(2, 2), actual.GetPixel(2, 2), "Filled pixel should match baseline.");
        AssertEqual(expected.GetPixel(4, 4), actual.GetPixel(4, 4), "Clipped pixel should match baseline.");
    }

    private static void OpenGlSurfaceStoresCpuPresentFrame()
    {
        using LinuxOpenGlRenderer openGl = new(new LinuxOpenGlRendererOptions(TryCreateEglContext: false));
        using LinuxOpenGlSurface surface = (LinuxOpenGlSurface)openGl.CreateSurface(BSurfaceDescriptor.Default(new BSize(6, 5)));
        var list = new BRenderList();
        list.FillRect(new BRect(2, 2, 2, 2), BColor.Green);

        openGl.Render(surface, list, new BFrameContext(BColor.Transparent));
        using BBitmap readback = surface.ReadToBitmap();

        AssertFalse(surface.IsGpuBacked, "The Windows-hosted test should use CPU fallback rather than pretending to own EGL.");
        AssertFalse(string.IsNullOrWhiteSpace(surface.Diagnostic), "Surface diagnostic should explain the presentation state.");
        AssertEqual(6, readback.Width, "Surface readback width should match descriptor.");
        AssertEqual(5, readback.Height, "Surface readback height should match descriptor.");
        AssertEqual(BColor.Green, readback.GetPixel(2, 2), "Rendered frame should be stored for readback.");
    }

    private static void OpenGlPixelConversionPreservesTopDownOrder()
    {
        using BBitmap bitmap = new(2, 2, [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255,
        ], takeOwnership: true);

        byte[] bottomUp = LinuxOpenGlPixelConversion.ToBottomUpRgba(bitmap);
        using BBitmap roundTrip = LinuxOpenGlPixelConversion.FromBottomUpRgba(2, 2, bottomUp);

        AssertEqual(BColor.Red, roundTrip.GetPixel(0, 0), "Top-left pixel should survive OpenGL bottom-up conversion.");
        AssertEqual(BColor.White, roundTrip.GetPixel(1, 1), "Bottom-right pixel should survive OpenGL bottom-up conversion.");
    }

    private static void OpenGlNativeReplayAcceptsSolidRectSubset()
    {
        var list = new BRenderList();
        list.FillRect(new BRect(1, 1, 4, 4), BColor.Red);
        list.StrokeRect(new BRect(0, 0, 8, 8), BColor.Blue, 1);
        list.PushClip(new BRect(2, 2, 3, 3));
        list.FillRect(new BRect(0, 0, 8, 8), BColor.Green);
        list.PopClip();

        LinuxOpenGlNativeReplayInspection inspection = LinuxOpenGlNativeReplay.Inspect(
            list,
            BSurfaceDescriptor.Default(new BSize(8, 8)),
            new BFrameContext(BColor.White));

        AssertTrue(inspection.IsSupported, inspection.Diagnostic);
        AssertTrue(inspection.NativeOperationCount >= 7, "Clear, fill, four stroke strips, and clipped fill should become native operations.");
        AssertTrue(inspection.Diagnostic.Contains("native replay", StringComparison.OrdinalIgnoreCase), "Inspection diagnostic should mention native replay.");
    }

    private static void OpenGlNativeReplayRejectsUnsupportedCommands()
    {
        var alphaList = new BRenderList();
        alphaList.FillRect(new BRect(0, 0, 4, 4), new BColor(255, 0, 0, 128));
        AssertUnsupportedNativeReplay(alphaList, "opaque fill");

        var transformList = new BRenderList();
        transformList.PushTransform(BMatrix3x2.Translation(1, 1));
        transformList.FillRect(new BRect(0, 0, 4, 4), BColor.Red);
        transformList.PopTransform();
        AssertUnsupportedNativeReplay(transformList, "transforms");

        var imageList = new BRenderList();
        imageList.DrawImage(BImageHandle.FromId(1, new BSize(1, 1)), new BRect(0, 0, 1, 1), new BRect(0, 0, 4, 4));
        AssertUnsupportedNativeReplay(imageList, "image");

        var textList = new BRenderList();
        textList.DrawText(new BTextRun("Phase 6"), new BPoint(0, 0));
        AssertUnsupportedNativeReplay(textList, "text");
    }

    private static void OpenGlX11WindowSurfaceIsLinuxOnly()
    {
        if (OperatingSystem.IsLinux())
            return;

        using LinuxOpenGlRenderer renderer = new(new LinuxOpenGlRendererOptions(TryCreateEglContext: false));
        AssertThrows<PlatformNotSupportedException>(
            () => renderer.CreateX11WindowSurface(BSurfaceDescriptor.Default(new BSize(8, 8))),
            "Linux");
    }

    private static void VulkanRenderToImageMatchesManagedBaseline()
    {
        var list = new BRenderList();
        list.FillRect(new BRect(1, 1, 4, 4), BColor.Red);
        list.PushClip(new BRect(3, 3, 4, 4));
        list.FillRect(new BRect(0, 0, 8, 8), BColor.Blue);
        list.PopClip();

        BSurfaceDescriptor descriptor = BSurfaceDescriptor.Default(new BSize(8, 8));
        BFrameContext frame = new(BColor.White);

        using BImageRenderer managed = new();
        using LinuxVulkanRenderer vulkan = new(new LinuxVulkanRendererOptions(TryCreateVulkanDevice: false));
        using BBitmap expected = managed.RenderToImage(list, descriptor, frame);
        using BBitmap actual = vulkan.RenderToImage(list, descriptor, frame);

        AssertEqual(expected.Width, actual.Width, "Vulkan CPU-present width should match baseline.");
        AssertEqual(expected.Height, actual.Height, "Vulkan CPU-present height should match baseline.");
        AssertEqual(expected.GetPixel(0, 0), actual.GetPixel(0, 0), "Clear pixel should match baseline.");
        AssertEqual(expected.GetPixel(2, 2), actual.GetPixel(2, 2), "Filled pixel should match baseline.");
        AssertEqual(expected.GetPixel(4, 4), actual.GetPixel(4, 4), "Clipped pixel should match baseline.");
    }

    private static void VulkanSurfaceStoresCpuPresentFrame()
    {
        using LinuxVulkanRenderer vulkan = new(new LinuxVulkanRendererOptions(TryCreateVulkanDevice: false));
        using LinuxVulkanSurface surface = (LinuxVulkanSurface)vulkan.CreateSurface(BSurfaceDescriptor.Default(new BSize(6, 5)));
        var list = new BRenderList();
        list.FillRect(new BRect(2, 2, 2, 2), BColor.Green);

        vulkan.Render(surface, list, new BFrameContext(BColor.Transparent));
        using BBitmap readback = surface.ReadToBitmap();

        AssertFalse(surface.IsDeviceBacked, "Disabled Vulkan device creation should use CPU-present fallback.");
        AssertTrue(surface.Diagnostic.Contains("disabled", StringComparison.OrdinalIgnoreCase), "Surface diagnostic should explain disabled Vulkan device creation.");
        AssertEqual(6, readback.Width, "Surface readback width should match descriptor.");
        AssertEqual(5, readback.Height, "Surface readback height should match descriptor.");
        AssertEqual(BColor.Green, readback.GetPixel(2, 2), "Rendered frame should be stored for readback.");
    }

    private static void VulkanStrictDeviceCreationIsLinuxOnly()
    {
        if (OperatingSystem.IsLinux())
            return;

        using LinuxVulkanRenderer vulkan = new(new LinuxVulkanRendererOptions(AllowCpuFallbackWhenVulkanUnavailable: false));
        AssertThrows<PlatformNotSupportedException>(
            () => vulkan.CreateSurface(BSurfaceDescriptor.Default(new BSize(8, 8))),
            "Linux");
    }

    private static void AssertContains(IReadOnlyList<LinuxNativeLibraryStatus> statuses, string id)
    {
        AssertTrue(statuses.Any(status => status.Id == id), $"Expected dependency probe to include '{id}'.");
    }

    private static void AssertUnsupportedNativeReplay(BRenderList list, string expectedMessageFragment)
    {
        LinuxOpenGlNativeReplayInspection inspection = LinuxOpenGlNativeReplay.Inspect(
            list,
            BSurfaceDescriptor.Default(new BSize(8, 8)),
            new BFrameContext(BColor.White));

        AssertFalse(inspection.IsSupported, "Native replay inspection should reject the unsupported command.");
        AssertTrue(inspection.Diagnostic.Contains(expectedMessageFragment, StringComparison.OrdinalIgnoreCase),
            $"Inspection diagnostic should mention {expectedMessageFragment}. Actual: {inspection.Diagnostic}");
    }

    private static void AssertThrows<TException>(Action action, string expectedMessageFragment)
        where TException : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
        }
        catch (TException exception)
        {
            AssertTrue(exception.Message.Contains(expectedMessageFragment, StringComparison.Ordinal),
                $"Expected exception message to mention {expectedMessageFragment}.");
        }
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
