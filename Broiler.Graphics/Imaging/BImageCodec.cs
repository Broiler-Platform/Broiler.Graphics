using System;

namespace Broiler.Graphics;

/// <summary>
/// Process-wide entry point to the active <see cref="IBImageCodec"/>. Until a
/// real backend is registered through <see cref="Register"/>, the default
/// implementation is a stub that throws <see cref="NotSupportedException"/> — the
/// OS-dependent GDI+ codec has been removed and a managed/native replacement is
/// expected to live alongside this submodule.
/// </summary>
public static class BImageCodec
{
    private static IBImageCodec _current = new NotSupportedImageCodec();

    /// <summary>The codec used by <see cref="Decode"/> and <see cref="Encode"/>.</summary>
    public static IBImageCodec Current => _current;

    /// <summary>Registers the codec backend to use process-wide.</summary>
    public static void Register(IBImageCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _current = codec;
    }

    /// <summary>Decodes encoded image bytes through the current codec.</summary>
    public static BPixelBuffer Decode(ReadOnlySpan<byte> data) => _current.Decode(data);

    /// <summary>Encodes a pixel buffer through the current codec.</summary>
    public static byte[] Encode(BPixelBuffer buffer, BImageEncodeFormat format, int quality = 100) =>
        _current.Encode(buffer, format, quality);

    /// <summary>
    /// Placeholder codec installed by default. Every operation throws to make the
    /// missing backend obvious rather than silently producing blank images.
    /// </summary>
    private sealed class NotSupportedImageCodec : IBImageCodec
    {
        private const string Message =
            "No Broiler.Graphics image codec is registered. The OS-dependent GDI+ codec was " +
            "removed; register a managed or native IBImageCodec via BImageCodec.Register.";

        public BPixelBuffer Decode(ReadOnlySpan<byte> data) => throw new NotSupportedException(Message);

        public byte[] Encode(BPixelBuffer buffer, BImageEncodeFormat format, int quality = 100) =>
            throw new NotSupportedException(Message);
    }
}
