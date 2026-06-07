using System;

namespace Broiler.Graphics;

/// <summary>
/// A platform-neutral, tightly packed 32bpp pixel buffer in straight-alpha RGBA
/// byte order (R, G, B, A per pixel, row-major, no padding). This is the
/// exchange format between the image codec abstraction and bitmap consumers.
/// </summary>
public sealed class BPixelBuffer
{
    /// <summary>Number of bytes per pixel (RGBA).</summary>
    public const int BytesPerPixel = 4;

    /// <summary>Creates a buffer that takes ownership of <paramref name="rgba"/>.</summary>
    public BPixelBuffer(int width, int height, byte[] rgba)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(rgba);

        long expected = (long)width * height * BytesPerPixel;
        if (rgba.Length != expected)
            throw new ArgumentException(
                $"Pixel buffer length {rgba.Length} does not match {width}x{height}x{BytesPerPixel} = {expected}.",
                nameof(rgba));

        Width = width;
        Height = height;
        Rgba = rgba;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The raw RGBA bytes, length <c>Width * Height * 4</c>.</summary>
    public byte[] Rgba { get; }
}
