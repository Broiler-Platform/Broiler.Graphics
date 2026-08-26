using System;

namespace Broiler.Graphics;

/// <summary>
/// In-memory bitmap surface used by <see cref="BImageRenderer"/>.
/// </summary>
public sealed class BImageSurface : IBroilerSurface
{
    private BBitmap _bitmap;
    private BSize _size;
    private double _dpiScale;
    private bool _disposed;

    public BImageSurface(BSurfaceDescriptor descriptor)
    {
        _size = ValidateSize(descriptor.Size);
        _dpiScale = NormalizeDpiScale(descriptor.DpiScale);
        _bitmap = new BBitmap(PixelWidth, PixelHeight);
    }

    public BSize Size => _size;

    public double DpiScale => _dpiScale;

    public BBitmap Bitmap
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bitmap;
        }
    }

    public void Resize(BSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _size = ValidateSize(size);
        _dpiScale = NormalizeDpiScale(dpiScale);

        _bitmap.Dispose();
        _bitmap = new BBitmap(PixelWidth, PixelHeight);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _bitmap.Dispose();
    }

    private int PixelWidth => ToPixelDimension(_size.Width, _dpiScale, nameof(Size));

    private int PixelHeight => ToPixelDimension(_size.Height, _dpiScale, nameof(Size));

    private static BSize ValidateSize(BSize size)
    {
        if (!IsPositiveFinite(size.Width) || !IsPositiveFinite(size.Height))
            throw new ArgumentOutOfRangeException(nameof(size), "Surface size must be positive and finite.");

        return size;
    }

    private static double NormalizeDpiScale(double dpiScale) =>
        IsPositiveFinite(dpiScale) ? dpiScale : 1.0;

    private static bool IsPositiveFinite(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static int ToPixelDimension(double dip, double dpiScale, string name)
    {
        double pixels = Math.Ceiling(dip * dpiScale);
        if (pixels <= 0 || pixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Surface pixel dimensions are outside the supported range.");

        return (int)pixels;
    }
}
