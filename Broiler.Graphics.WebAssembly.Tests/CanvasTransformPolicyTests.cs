using System;
using System.Collections.Generic;

namespace Broiler.Graphics.WebAssembly.Tests;

/// <summary>
/// Tests the axis-aligned bounding-box transform policy: translation and axis-aligned
/// scaling are exact, and rotation/shear collapse to the transformed bounding box exactly
/// as the CPU reference renderer does.
/// </summary>
internal static class CanvasTransformPolicyTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Identity leaves a rect unchanged", IdentityUnchanged));
        tests.Add(("Translation offsets a rect", TranslationOffsets));
        tests.Add(("Axis-aligned scale scales a rect", AxisScale));
        tests.Add(("Rotation collapses to bounding box", RotationBoundingBox));
        tests.Add(("Average scale is the mean of row lengths", AverageScaleMean));
        tests.Add(("Degenerate scale falls back to 1", DegenerateScale));
    }

    private static void IdentityUnchanged()
    {
        BRect r = CanvasTransformPolicy.ToDeviceAabb(BMatrix3x2.Identity, new BRect(3, 4, 10, 6));
        AssertEx.AreClose(3, r.X);
        AssertEx.AreClose(4, r.Y);
        AssertEx.AreClose(10, r.Width);
        AssertEx.AreClose(6, r.Height);
    }

    private static void TranslationOffsets()
    {
        BRect r = CanvasTransformPolicy.ToDeviceAabb(BMatrix3x2.Translation(5, -2), new BRect(3, 4, 10, 6));
        AssertEx.AreClose(8, r.X);
        AssertEx.AreClose(2, r.Y);
        AssertEx.AreClose(10, r.Width);
        AssertEx.AreClose(6, r.Height);
    }

    private static void AxisScale()
    {
        BRect r = CanvasTransformPolicy.ToDeviceAabb(BMatrix3x2.Scale(2, 3), new BRect(1, 2, 4, 5));
        AssertEx.AreClose(2, r.X);
        AssertEx.AreClose(6, r.Y);
        AssertEx.AreClose(8, r.Width);
        AssertEx.AreClose(15, r.Height);
    }

    private static void RotationBoundingBox()
    {
        // 90-degree rotation: point (x,y) -> (-y, x). A 10x4 rect becomes a 4x10 bounding box.
        var rotate90 = new BMatrix3x2(0, 1, -1, 0, 0, 0);
        BRect r = CanvasTransformPolicy.ToDeviceAabb(rotate90, new BRect(0, 0, 10, 4));
        AssertEx.AreClose(-4, r.X);
        AssertEx.AreClose(0, r.Y);
        AssertEx.AreClose(4, r.Width);
        AssertEx.AreClose(10, r.Height);
    }

    private static void AverageScaleMean()
    {
        AssertEx.AreClose(1, CanvasTransformPolicy.AverageScale(BMatrix3x2.Identity));
        AssertEx.AreClose(2.5, CanvasTransformPolicy.AverageScale(BMatrix3x2.Scale(2, 3)));
        AssertEx.AreClose(1, CanvasTransformPolicy.AverageScale(new BMatrix3x2(0, 1, -1, 0, 0, 0)));
    }

    private static void DegenerateScale()
    {
        AssertEx.AreClose(1, CanvasTransformPolicy.AverageScale(BMatrix3x2.Scale(0, 0)));
    }
}
