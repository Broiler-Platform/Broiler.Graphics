using System;

namespace Broiler.Graphics.Android;

/// <summary>
/// Validates surface descriptors and converts logical sizes to pixel dimensions.
/// </summary>
/// <remarks>
/// Android changes both halves of this constantly — rotation changes the size, and a display or
/// font-scale change moves the density — so the conversion is shared rather than repeated in each
/// surface. A non-finite or non-positive density is corrected to 1.0 instead of propagating into a
/// zero-sized texture that fails much later with an opaque framebuffer error.
/// </remarks>
internal static class AndroidSurfaceGeometry
{
    public static BSurfaceDescriptor Validate(BSurfaceDescriptor descriptor)
    {
        if (!IsPositiveFinite(descriptor.Size.Width) || !IsPositiveFinite(descriptor.Size.Height))
            throw new ArgumentOutOfRangeException(nameof(descriptor), "Surface size must be positive and finite.");

        return descriptor with
        {
            DpiScale = IsPositiveFinite(descriptor.DpiScale) ? descriptor.DpiScale : 1.0,
        };
    }

    public static int ToPixels(double logical, double dpiScale)
    {
        double pixels = Math.Ceiling(logical * dpiScale);
        if (pixels <= 0 || pixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(logical), "Surface pixel dimensions are outside the supported range.");

        return (int)pixels;
    }

    public static bool IsPositiveFinite(double value) => value > 0 && double.IsFinite(value);
}
