namespace Broiler.Graphics;

/// <summary>Encoded raster image formats understood by <see cref="IBImageCodec"/>.</summary>
public enum BImageEncodeFormat
{
    /// <summary>Lossless PNG (zlib/DEFLATE). Encoded as 8-bit RGBA.</summary>
    Png,

    /// <summary>Lossy JPEG. Supported only by codecs that opt in.</summary>
    Jpeg,

    /// <summary>Uncompressed Windows BMP. Encoded as 32bpp BGRA.</summary>
    Bmp,
}
