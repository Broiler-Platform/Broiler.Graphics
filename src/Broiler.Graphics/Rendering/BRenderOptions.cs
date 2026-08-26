namespace Broiler.Graphics;

/// <summary>Pixel layout of a surface's backing buffer.</summary>
public enum BPixelFormat
{
    /// <summary>32-bit BGRA, 8 bits per channel, straight alpha (Direct2D's default premul maps here).</summary>
    Bgra8 = 0,
    /// <summary>32-bit RGBA, 8 bits per channel, straight alpha.</summary>
    Rgba8 = 1,
}

/// <summary>
/// Immutable description used to create a surface. Kept platform-neutral; backends translate the
/// fields into their own swap-chain / render-target descriptors.
/// </summary>
public readonly record struct BSurfaceDescriptor(
    BSize Size,
    double DpiScale,
    BPixelFormat PixelFormat = BPixelFormat.Bgra8,
    bool EnableTransparency = false)
{
    public static BSurfaceDescriptor Default(BSize size) => new(size, 1.0);
}

/// <summary>
/// Renderer-wide options that tune quality vs. performance. Immutable.
/// </summary>
public readonly record struct BRenderOptions(
    bool Antialias = true,
    bool VSync = true,
    bool SubpixelText = true)
{
    public static BRenderOptions Default => new();
}
