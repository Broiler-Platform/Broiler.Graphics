namespace Broiler.Graphics;

/// <summary>Platform-neutral settings used when creating a native window control.</summary>
public sealed record BControlOptions
{
    public BRect Bounds { get; init; } = BRect.Empty;

    public string Text { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public bool Visible { get; init; } = true;
}
