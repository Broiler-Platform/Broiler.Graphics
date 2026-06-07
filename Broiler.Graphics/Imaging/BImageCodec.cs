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

    /// <summary>True once a real codec has been registered (i.e. not the throwing default stub).</summary>
    public static bool IsRegistered => _current is not NotSupportedImageCodec;

    /// <summary>Registers the codec backend to use process-wide.</summary>
    public static void Register(IBImageCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _current = codec;
    }

    /// <summary>
    /// Registers the dependency-free <see cref="ManagedImageCodec"/> (PNG/APNG, BMP
    /// and JPEG) as the process-wide codec. A convenience for callers that don't
    /// supply a native backend.
    /// </summary>
    public static void UseManaged() => Register(ManagedImageCodec.Instance);

    /// <summary>
    /// Registers <see cref="ManagedImageCodec"/> only if no codec has been registered
    /// yet, so a backend can guarantee image support without overriding a caller's
    /// explicit <see cref="Register"/> choice. Returns true if it installed the codec.
    /// </summary>
    public static bool UseManagedIfUnset()
    {
        if (IsRegistered)
            return false;
        Register(ManagedImageCodec.Instance);
        return true;
    }

    /// <summary>Restores the throwing default stub. Test-only.</summary>
    internal static void ResetToDefault() => _current = new NotSupportedImageCodec();

    /// <summary>Decodes encoded image bytes through the current codec.</summary>
    public static BPixelBuffer Decode(ReadOnlySpan<byte> data) => _current.Decode(data);

    /// <summary>Decodes encoded image bytes into a frame sequence through the current codec.</summary>
    public static BImageSequence DecodeAnimation(ReadOnlySpan<byte> data) => _current.DecodeAnimation(data);

    /// <summary>Encodes a pixel buffer through the current codec.</summary>
    public static byte[] Encode(BPixelBuffer buffer, BImageEncodeFormat format, int quality = 100) =>
        _current.Encode(buffer, format, quality);

    /// <summary>Encodes a frame sequence into an animated format (e.g. APNG) through the current codec.</summary>
    public static byte[] EncodeAnimation(BImageSequence sequence, BImageEncodeFormat format = BImageEncodeFormat.Png) =>
        _current.EncodeAnimation(sequence, format);

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
