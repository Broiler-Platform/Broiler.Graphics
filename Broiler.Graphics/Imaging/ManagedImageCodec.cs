using System;

namespace Broiler.Graphics;

/// <summary>
/// A fully managed <see cref="IBImageCodec"/> with no external or OS-specific
/// dependencies. Decoding auto-detects the format from the byte signature;
/// encoding targets the requested <see cref="BImageEncodeFormat"/>.
/// <para>
/// Supported formats: <b>PNG</b> (decode all colour types/bit depths, interlaced
/// and APNG; encode 8-bit RGBA and APNG via <see cref="EncodeAnimation"/>),
/// <b>BMP</b> (decode uncompressed 24/32bpp; encode 32bpp BGRA), and <b>JPEG</b>
/// (decode baseline and progressive, 1/3-component with the common subsamplings
/// and restart markers; encode baseline 4:2:0 YCbCr). JPEG is lossy and discards
/// the alpha channel.
/// </para>
/// </summary>
public sealed class ManagedImageCodec : IBImageCodec
{
    /// <summary>A shared, thread-safe instance (the codec is stateless).</summary>
    public static ManagedImageCodec Instance { get; } = new();

    /// <summary>Decodes PNG, BMP, or baseline JPEG bytes into an RGBA pixel buffer.</summary>
    public BPixelBuffer Decode(ReadOnlySpan<byte> data)
    {
        if (PngDecoder.IsPng(data))
            return PngDecoder.Decode(data);
        if (BmpDecoder.IsBmp(data))
            return BmpDecoder.Decode(data);
        if (JpegDecoder.IsJpeg(data))
            return JpegDecoder.Decode(data);

        throw new NotSupportedException(
            "Unrecognized image data. The managed codec decodes PNG, BMP, and JPEG; " +
            "the signature matched none.");
    }

    /// <summary>
    /// Decodes a frame sequence. PNG/APNG yields one frame per animation step
    /// (already composited); BMP and JPEG yield a single still frame.
    /// </summary>
    public BImageSequence DecodeAnimation(ReadOnlySpan<byte> data)
    {
        if (PngDecoder.IsPng(data))
            return PngDecoder.DecodeAnimation(data);
        return BImageSequence.Static(Decode(data));
    }

    /// <summary>Encodes a frame sequence as an APNG (the only animated format this codec supports).</summary>
    public byte[] EncodeAnimation(BImageSequence sequence, BImageEncodeFormat format = BImageEncodeFormat.Png)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (format != BImageEncodeFormat.Png)
            throw new NotSupportedException(
                $"Animation encoding is only supported as PNG (APNG); requested {format}.");
        return PngEncoder.EncodeAnimation(sequence);
    }

    /// <summary>Encodes a pixel buffer as PNG, BMP, or JPEG.</summary>
    public byte[] Encode(BPixelBuffer buffer, BImageEncodeFormat format, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return format switch
        {
            BImageEncodeFormat.Png => PngEncoder.Encode(buffer),
            BImageEncodeFormat.Bmp => BmpEncoder.Encode(buffer),
            BImageEncodeFormat.Jpeg => JpegEncoder.Encode(buffer, quality),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown encode format."),
        };
    }
}
