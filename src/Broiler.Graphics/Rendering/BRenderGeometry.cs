using Broiler.Graphics.Geometry;
using System;

namespace Broiler.Graphics.Rendering;

/// <summary>Device-space geometry shared by CPU and browser replay.</summary>
internal static class BRenderGeometry
{
    internal static bool IsAxisAligned(BMatrix3x2 transform) =>
        Math.Abs(transform.M12) < 1e-6 && Math.Abs(transform.M21) < 1e-6;

    internal static BRect ToDeviceAabb(BMatrix3x2 transform, BRect rect)
    {
        BPoint p1 = transform.Transform(new BPoint(rect.Left, rect.Top));
        BPoint p2 = transform.Transform(new BPoint(rect.Right, rect.Top));
        BPoint p3 = transform.Transform(new BPoint(rect.Right, rect.Bottom));
        BPoint p4 = transform.Transform(new BPoint(rect.Left, rect.Bottom));

        double left = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        double top = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        double right = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        double bottom = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        return BRect.FromLTRB(left, top, right, bottom);
    }

    /// <summary>Average scale used by the raster replay for radii and stroke widths.</summary>
    internal static double AverageScale(BMatrix3x2 transform)
    {
        double x = Math.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12));
        double y = Math.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22));
        double scale = (x + y) / 2.0;
        return scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale) ? scale : 1.0;
    }

    /// <summary>True when a device rectangle is finite and has positive area.</summary>
    internal static bool IsDrawable(BRect rect) =>
        rect.Width > 0
        && rect.Height > 0
        && double.IsFinite(rect.X)
        && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Width)
        && double.IsFinite(rect.Height);
}
