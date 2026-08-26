using System.Collections.Generic;

namespace Broiler.Graphics.Linux;

public static class LinuxGraphicsDependencies
{
    public static LinuxNativeLibraryRequirement Egl { get; } = new(
        "egl",
        "Mesa EGL",
        ["libEGL.so.1", "libEGL.so"]);

    public static LinuxNativeLibraryRequirement OpenGl { get; } = new(
        "opengl",
        "OpenGL loader",
        ["libGL.so.1", "libGLESv2.so.2", "libOpenGL.so.0"]);

    public static LinuxNativeLibraryRequirement Vulkan { get; } = new(
        "vulkan",
        "Vulkan loader",
        ["libvulkan.so.1", "libvulkan.so"]);

    public static LinuxNativeLibraryRequirement WaylandClient { get; } = new(
        "wayland-client",
        "Wayland client library",
        ["libwayland-client.so.0", "libwayland-client.so"]);

    public static LinuxNativeLibraryRequirement Xcb { get; } = new(
        "xcb",
        "XCB client library",
        ["libxcb.so.1", "libxcb.so"]);

    public static LinuxNativeLibraryRequirement X11 { get; } = new(
        "x11",
        "X11 client library",
        ["libX11.so.6", "libX11.so"]);

    public static IReadOnlyList<LinuxNativeLibraryRequirement> WindowingBaseline { get; } =
    [
        Egl,
        OpenGl,
        Vulkan,
        WaylandClient,
        Xcb,
        X11,
    ];

    public static IReadOnlyList<LinuxNativeLibraryStatus> CheckWindowingBaseline() =>
        LinuxNativeLibraryProbe.Check(WindowingBaseline);
}
