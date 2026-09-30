using Broiler.Graphics.Geometry;

namespace Broiler.Graphics.Rendering;

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
public readonly record struct BSurfaceDescriptor(BSize Size, double DpiScale, BPixelFormat PixelFormat = BPixelFormat.Bgra8, bool EnableTransparency = false)
{
    public static BSurfaceDescriptor Default(BSize size) => new(size, 1.0);
}

/// <summary>
/// Renderer-wide options that tune quality vs. performance. Immutable.
/// <para>
/// Note: Due to C# runtime struct rules, zero-initialized struct instances produced by
/// <c>default(BRenderOptions)</c> have all boolean flags set to <c>false</c>.
/// Use <see cref="Default"/> or <c>new BRenderOptions()</c> for default high-quality rendering.
/// For a deliberate low-quality configuration, use <see cref="LowQuality"/>.
/// </para>
/// </summary>
public readonly record struct BRenderOptions(bool Antialias = true, bool VSync = true, bool SubpixelText = true)
{
    /// <summary>
    /// Explicit parameterless constructor ensuring <c>new BRenderOptions()</c> sets high-quality defaults
    /// (<see cref="Antialias"/> = true, <see cref="VSync"/> = true, <see cref="SubpixelText"/> = true).
    /// </summary>
    public BRenderOptions() : this(Antialias: true, VSync: true, SubpixelText: true)
    {
    }

    /// <summary>
    /// High-quality rendering options with Antialias, VSync, and SubpixelText enabled.
    /// </summary>
    public static BRenderOptions Default => new(Antialias: true, VSync: true, SubpixelText: true);

    /// <summary>
    /// Deliberate low-quality rendering options with Antialias, VSync, and SubpixelText disabled.
    /// </summary>
    public static BRenderOptions LowQuality => new(Antialias: false, VSync: false, SubpixelText: false);
}
