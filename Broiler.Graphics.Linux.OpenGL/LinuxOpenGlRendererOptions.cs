namespace Broiler.Graphics.Linux.OpenGL;

public sealed record LinuxOpenGlRendererOptions(
    bool TryCreateEglContext = true,
    bool AllowCpuFallbackWhenOpenGlUnavailable = true,
    bool EnableGpuReadbackForRenderToImage = true,
    bool EnableNativeCommandReplay = true)
{
    public static LinuxOpenGlRendererOptions Default { get; } = new();
}
