namespace Broiler.Graphics.Linux.OpenGL;

public sealed record LinuxOpenGlDriverInfo(
    string Vendor,
    string Renderer,
    string Version,
    string ShadingLanguageVersion)
{
    public string ToDiagnosticString() =>
        $"OpenGL vendor={Vendor}; renderer={Renderer}; version={Version}; glsl={ShadingLanguageVersion}.";
}
