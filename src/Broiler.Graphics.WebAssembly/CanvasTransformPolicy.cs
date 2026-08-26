using System;

namespace Broiler.Graphics.WebAssembly;

/// <summary>
/// The transform-semantics policy for the direct-Canvas backend. Phase 5 selects
/// <b>axis-aligned bounding-box emulation</b>: the backend reproduces exactly what
/// the CPU reference renderer (<c>BImageRenderer</c>) does, so the CPU renderer stays
/// a pixel-exact oracle for the whole command set.
/// <para>
/// A device rectangle is produced by transforming the four corners of a logical
/// rectangle through <c>current * pixelScale</c> and taking their axis-aligned bounding
/// box. Radii and stroke thickness scale by the average of the transform's row lengths.
/// These are the identical formulas used by <c>BImageRenderer.TransformRect</c> and
/// <c>BImageRenderer.CurrentAverageScale</c>; do not diverge without updating the CPU
/// oracle and the conformance suite together.
/// </para>
/// <para>
/// Rotation, shear, and negative scale therefore render as their transformed bounding
/// box (matching the CPU renderer), not as true rotated/sheared geometry. Translation
/// and axis-aligned scaling are exact. See the Phase 5 transform-semantics decision
/// record for the rationale and the documented differences from native Canvas
/// geometric transforms.
/// </para>
/// </summary>
internal static class CanvasTransformPolicy
{
    /// <summary>
    /// Transforms a logical rectangle to its device-space axis-aligned bounding box
    /// under <paramref name="transform"/> (already the product of the current transform
    /// and the DPI pixel scale). Mirrors <c>BImageRenderer.TransformRect</c>.
    /// </summary>
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

    /// <summary>
    /// The average uniform scale of <paramref name="transform"/> used to scale corner
    /// radii and stroke thickness. Mirrors <c>BImageRenderer.CurrentAverageScale</c>.
    /// </summary>
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
