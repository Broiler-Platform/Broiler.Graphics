using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Linux;

public sealed record LinuxGraphicsRuntimeReport(
    string OperatingSystemDescription,
    Architecture ProcessArchitecture,
    string RuntimeIdentifier,
    string FrameworkDescription,
    LinuxDisplayServerStatus DisplayServer)
{
    public string Diagnostic =>
        $"{OperatingSystemDescription}; arch={ProcessArchitecture}; rid={RuntimeIdentifier}; {DisplayServer.Diagnostic}";
}

public sealed record LinuxDisplayServerStatus(
    string SessionType,
    string? WaylandDisplay,
    string? X11Display,
    bool HasWaylandDisplay,
    bool HasX11Display,
    string Diagnostic);

public static class LinuxGraphicsRuntimeDiagnostics
{
    public static LinuxGraphicsRuntimeReport Capture() =>
        new(
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture,
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.FrameworkDescription,
            CheckDisplayServer());

    public static LinuxDisplayServerStatus CheckDisplayServer()
    {
        string sessionType = Normalize(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")) ?? "unknown";
        string? waylandDisplay = Normalize(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        string? x11Display = Normalize(Environment.GetEnvironmentVariable("DISPLAY"));

        string diagnostic = (waylandDisplay, x11Display) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"Display environment reports session={sessionType}, Wayland={waylandDisplay}, X11={x11Display}.",
            ({ Length: > 0 }, _) => $"Display environment reports session={sessionType}, Wayland={waylandDisplay}.",
            (_, { Length: > 0 }) => $"Display environment reports session={sessionType}, X11={x11Display}.",
            _ => $"No Wayland or X11 display environment was detected; Linux graphics will need headless/offscreen fallback or an active display session.",
        };

        return new LinuxDisplayServerStatus(
            sessionType,
            waylandDisplay,
            x11Display,
            waylandDisplay is not null,
            x11Display is not null,
            diagnostic);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
