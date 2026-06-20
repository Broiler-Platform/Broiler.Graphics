using System;
using System.IO;

namespace Broiler.Graphics;

/// <summary>
/// A dependency-free RGBA bitmap owned by Broiler.Graphics.
/// </summary>
public sealed class BBitmap : IDisposable
{
    private readonly byte[] _rgba;
    private bool _disposed;

    public BBitmap(int width, int height)
        : this(width, height, new byte[checked(width * height * BPixelBuffer.BytesPerPixel)], takeOwnership: true)
    {
    }

    public BBitmap(BPixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        Width = pixels.Width;
        Height = pixels.Height;
        _rgba = pixels.Rgba;
    }

    public BBitmap(int width, int height, byte[] rgba, bool takeOwnership = false)
    {
        ValidateDimensions(width, height);
        ArgumentNullException.ThrowIfNull(rgba);

        long expected = (long)width * height * BPixelBuffer.BytesPerPixel;
        if (rgba.Length != expected)
            throw new ArgumentException(
                $"Pixel buffer length {rgba.Length} does not match {width}x{height}x{BPixelBuffer.BytesPerPixel} = {expected}.",
                nameof(rgba));

        Width = width;
        Height = height;
        _rgba = takeOwnership ? rgba : (byte[])rgba.Clone();
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlySpan<byte> Rgba => _rgba;

    public BColor GetPixel(int x, int y)
    {
        ThrowIfDisposed();
        ValidatePixelCoordinates(x, y);

        int index = GetPixelIndex(x, y);
        return new BColor(_rgba[index], _rgba[index + 1], _rgba[index + 2], _rgba[index + 3]);
    }

    public void SetPixel(int x, int y, BColor color)
    {
        ThrowIfDisposed();
        ValidatePixelCoordinates(x, y);
        WritePixelUnchecked(x, y, color);
    }

    public void Clear(BColor color)
    {
        ThrowIfDisposed();
        ErasePixels(color);
    }

    public BCanvas OpenCanvas()
    {
        ThrowIfDisposed();
        return new BCanvas(this);
    }

    public BPixelBuffer ToPixelBuffer(bool copy = true)
    {
        ThrowIfDisposed();
        return new BPixelBuffer(Width, Height, copy ? (byte[])_rgba.Clone() : _rgba);
    }

    public byte[] CopyRgba()
    {
        ThrowIfDisposed();
        return (byte[])_rgba.Clone();
    }

    public BBitmap Copy()
    {
        ThrowIfDisposed();
        return new BBitmap(Width, Height, _rgba);
    }

    public BBitmap ResizeNearest(int width, int height)
    {
        ThrowIfDisposed();
        ValidateDimensions(width, height);

        if (width == Width && height == Height)
            return Copy();

        var resized = new BBitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            int srcY = Math.Min(Height - 1, (int)((long)y * Height / height));
            for (int x = 0; x < width; x++)
            {
                int srcX = Math.Min(Width - 1, (int)((long)x * Width / width));
                resized.SetPixel(x, y, GetPixel(srcX, srcY));
            }
        }

        return resized;
    }

    public byte[] Encode(BImageEncodeFormat format = BImageEncodeFormat.Png, int quality = 100)
    {
        ThrowIfDisposed();
        EnsureImageCodec();
        return BImageCodec.Encode(ToPixelBuffer(copy: true), format, quality);
    }

    public void Save(string filePath, BImageEncodeFormat format = BImageEncodeFormat.Png, int quality = 100)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        File.WriteAllBytes(filePath, Encode(format, quality));
    }

    public static BBitmap Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureImageCodec();
        return new BBitmap(BImageCodec.Decode(data));
    }

    public static BBitmap Decode(ReadOnlySpan<byte> data)
    {
        EnsureImageCodec();
        return new BBitmap(BImageCodec.Decode(data));
    }

    public static BBitmap Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] data;
        if (stream is MemoryStream ms)
        {
            data = ms.ToArray();
        }
        else
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            data = copy.ToArray();
        }

        return Decode(data);
    }

    public static BBitmap Decode(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Decode(File.ReadAllBytes(path));
    }

    public void Dispose()
    {
        _disposed = true;
    }

    internal void ErasePixels(BColor color)
    {
        for (int i = 0; i < _rgba.Length; i += BPixelBuffer.BytesPerPixel)
        {
            _rgba[i] = color.R;
            _rgba[i + 1] = color.G;
            _rgba[i + 2] = color.B;
            _rgba[i + 3] = color.A;
        }
    }

    internal void WritePixelUnchecked(int x, int y, BColor color)
    {
        int index = GetPixelIndex(x, y);
        _rgba[index] = color.R;
        _rgba[index + 1] = color.G;
        _rgba[index + 2] = color.B;
        _rgba[index + 3] = color.A;
    }

    private int GetPixelIndex(int x, int y) => checked(((y * Width) + x) * BPixelBuffer.BytesPerPixel);

    private void ValidatePixelCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
    }

    private static void EnsureImageCodec() => BImageCodec.UseManagedIfUnset();
}
