using System;

namespace Broiler.Graphics.Android;

/// <summary>
/// Flips RGBA rows between Broiler's top-down bitmaps and OpenGL's bottom-up texture and readback
/// order.
/// </summary>
/// <remarks>
/// This duplicates <c>LinuxOpenGlPixelConversion</c>. The two backends live in different assemblies
/// with no shared platform layer between them, and promoting the helper into the platform-neutral
/// <c>Broiler.Graphics</c> core would put a GL-specific memory layout into the neutral contract.
/// Keeping a small internal copy per backend is the cheaper of the two, and it is what the Linux
/// and Windows backends already do.
/// </remarks>
internal static class AndroidGlesPixelConversion
{
    public static byte[] ToBottomUpRgba(BBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        int rowBytes = checked(bitmap.Width * BPixelBuffer.BytesPerPixel);
        byte[] bottomUp = new byte[checked(rowBytes * bitmap.Height)];
        ReadOnlySpan<byte> source = bitmap.Rgba;

        for (int y = 0; y < bitmap.Height; y++)
        {
            int sourceOffset = y * rowBytes;
            int destinationOffset = (bitmap.Height - y - 1) * rowBytes;
            source.Slice(sourceOffset, rowBytes).CopyTo(bottomUp.AsSpan(destinationOffset, rowBytes));
        }

        return bottomUp;
    }

    public static BBitmap FromBottomUpRgba(int width, int height, byte[] bottomUpRgba)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(bottomUpRgba);

        int rowBytes = checked(width * BPixelBuffer.BytesPerPixel);
        int expected = checked(rowBytes * height);
        if (bottomUpRgba.Length != expected)
            throw new ArgumentException("OpenGL ES readback buffer length does not match the supplied dimensions.", nameof(bottomUpRgba));

        byte[] topDown = new byte[bottomUpRgba.Length];
        for (int y = 0; y < height; y++)
        {
            int sourceOffset = (height - y - 1) * rowBytes;
            int destinationOffset = y * rowBytes;
            bottomUpRgba.AsSpan(sourceOffset, rowBytes).CopyTo(topDown.AsSpan(destinationOffset, rowBytes));
        }

        return new BBitmap(width, height, topDown, takeOwnership: true);
    }
}
