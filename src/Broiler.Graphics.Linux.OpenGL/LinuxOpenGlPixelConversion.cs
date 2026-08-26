using System;

namespace Broiler.Graphics.Linux.OpenGL;

public static class LinuxOpenGlPixelConversion
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
            throw new ArgumentException("OpenGL readback buffer length does not match the supplied dimensions.", nameof(bottomUpRgba));

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
