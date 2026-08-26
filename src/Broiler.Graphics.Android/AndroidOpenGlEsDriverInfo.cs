namespace Broiler.Graphics.Android;

/// <summary>Identifies the OpenGL ES driver a session is running on.</summary>
public sealed record AndroidOpenGlEsDriverInfo(
    string Vendor,
    string Renderer,
    string Version,
    string ShadingLanguageVersion)
{
    public string ToDiagnosticString() =>
        $"OpenGL ES vendor={Vendor}; renderer={Renderer}; version={Version}; glsl={ShadingLanguageVersion}.";
}
