using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;

namespace Broiler.Graphics.WebAssembly;

/// <summary>
/// Rectangle strokes, rounded rectangles, images, and clips use the CPU renderer's
/// bounding-box policy. Non-axis-aligned rectangle fills require whole-frame CPU
/// fallback because the CPU fills their transformed corners.
/// </summary>
internal static class CanvasTransformPolicy
{
    internal static BRect ToDeviceAabb(BMatrix3x2 transform, BRect rect) =>
        BRenderGeometry.ToDeviceAabb(transform, rect);

    internal static double AverageScale(BMatrix3x2 transform) => BRenderGeometry.AverageScale(transform);

    internal static bool IsDrawable(BRect rect) => BRenderGeometry.IsDrawable(rect);
}
