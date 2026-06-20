namespace Broiler.Graphics;

/// <summary>Platform-neutral settings used when creating a render window.</summary>
public sealed record BWindowOptions
{
    public string Title { get; init; } = "Broiler.Graphics";

    public int ClientWidth { get; init; } = 1024;

    public int ClientHeight { get; init; } = 768;

    public BColor ClearColor { get; init; } = BColor.White;

    public bool EnableTransparency { get; init; }

    public BRenderOptions RenderOptions { get; init; } = BRenderOptions.Default;
}
