using System;
using System.Globalization;

namespace Broiler.Graphics.Linux.Demo;

internal sealed record LinuxDemoOptions(
    GraphicsBackend Backend,
    bool OpenWindow,
    bool EnableEvdevInput,
    bool RunUntilClose,
    int DurationMilliseconds,
    string? ArtifactDirectory)
{
    public static LinuxDemoOptions Parse(string[] args)
    {
        GraphicsBackend backend = GraphicsBackend.OpenGl;
        bool openWindow = false;
        bool enableEvdevInput = false;
        bool runUntilClose = false;
        int? durationMilliseconds = null;
        string? artifactDirectory = null;

        foreach (string arg in args)
        {
            if (arg.Equals("--vulkan", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--backend=vulkan", StringComparison.OrdinalIgnoreCase))
            {
                backend = GraphicsBackend.Vulkan;
            }
            else if (arg.Equals("--opengl", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--backend=opengl", StringComparison.OrdinalIgnoreCase))
            {
                backend = GraphicsBackend.OpenGl;
            }
            else if (arg.Equals("--window", StringComparison.OrdinalIgnoreCase))
            {
                openWindow = true;
            }
            else if (arg.Equals("--enable-evdev-input", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--input", StringComparison.OrdinalIgnoreCase))
            {
                enableEvdevInput = true;
            }
            else if (arg.Equals("--interactive", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--until-close", StringComparison.OrdinalIgnoreCase))
            {
                runUntilClose = true;
            }
            else if (arg.StartsWith("--duration-ms=", StringComparison.OrdinalIgnoreCase))
            {
                string value = arg["--duration-ms=".Length..];
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                    durationMilliseconds = parsed;
            }
            else if (arg.StartsWith("--artifact-dir=", StringComparison.OrdinalIgnoreCase))
            {
                artifactDirectory = arg["--artifact-dir=".Length..];
            }
        }

        int defaultDuration = openWindow && enableEvdevInput ? 10_000 : 2_000;
        return new LinuxDemoOptions(
            backend,
            openWindow,
            enableEvdevInput,
            runUntilClose,
            durationMilliseconds ?? defaultDuration,
            string.IsNullOrWhiteSpace(artifactDirectory) ? null : artifactDirectory);
    }
}

internal enum GraphicsBackend
{
    OpenGl,
    Vulkan,
}
