namespace Broiler.Graphics.Android;

/// <summary>
/// Renderer behavior knobs, mirroring <c>LinuxOpenGlRendererOptions</c>.
/// </summary>
/// <param name="TryCreateEglContext">
/// Attempt EGL/GLES presentation. When false the surface keeps the last CPU-rendered frame and
/// presents nothing, which is what the offscreen and test paths want.
/// </param>
/// <param name="AllowCpuFallbackWhenOpenGlUnavailable">
/// Fall back to holding the CPU frame when a context cannot be created or a present fails. Turning
/// this off makes those conditions throw, which is the right choice for a host that would rather
/// fail loudly than show a stale frame.
/// </param>
/// <param name="EnableGpuReadbackForRenderToImage">
/// Read pixels back from the GPU for <see cref="IBroilerRenderer.RenderToImage"/> rather than
/// returning the CPU frame that was uploaded. Readback exercises the real pipeline, which is what
/// makes it useful as a presentation check.
/// </param>
/// <param name="PreferRgbaWindowFormat">
/// Ask the native window for an RGBA_8888 buffer geometry. Leave it on unless the host has already
/// configured the surface format itself.
/// </param>
public sealed record AndroidOpenGlEsRendererOptions(
    bool TryCreateEglContext = true,
    bool AllowCpuFallbackWhenOpenGlUnavailable = true,
    bool EnableGpuReadbackForRenderToImage = true,
    bool PreferRgbaWindowFormat = true)
{
    public static AndroidOpenGlEsRendererOptions Default { get; } = new();
}
