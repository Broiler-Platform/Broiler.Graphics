using System;

namespace Broiler.Graphics;

/// <summary>
/// Platform-neutral raster image codec: decodes encoded image bytes into a
/// <see cref="BPixelBuffer"/> and encodes a buffer back into bytes. Backends
/// (e.g. a managed PNG/JPEG codec or a native one) implement this and register
/// themselves through <see cref="BImageCodec"/>.
/// </summary>
public interface IBImageCodec
{
    /// <summary>Decodes <paramref name="data"/> into an RGBA pixel buffer.</summary>
    BPixelBuffer Decode(ReadOnlySpan<byte> data);

    /// <summary>Encodes <paramref name="buffer"/> into the requested format.</summary>
    /// <param name="quality">Encoder quality hint (1–100), honored for lossy formats.</param>
    byte[] Encode(BPixelBuffer buffer, BImageEncodeFormat format, int quality = 100);
}
