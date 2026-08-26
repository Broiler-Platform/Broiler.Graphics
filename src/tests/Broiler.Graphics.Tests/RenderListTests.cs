using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>Tests for <see cref="BRenderList"/> recording, ordering and stack balancing.</summary>
internal static class RenderListTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("RenderList records commands in order", RecordsInOrder));
        tests.Add(("RenderList preserves command payloads", PreservesPayloads));
        tests.Add(("Validate accepts balanced clips", BalancedClipsValidate));
        tests.Add(("Validate rejects unbalanced clip push", UnbalancedClipPushFails));
        tests.Add(("Validate rejects clip pop underflow", ClipPopUnderflowFails));
        tests.Add(("Validate accepts balanced transforms", BalancedTransformsValidate));
        tests.Add(("Validate rejects transform pop underflow", TransformPopUnderflowFails));
        tests.Add(("DrawImage rejects out-of-range opacity", DrawImageOpacityValidated));
        tests.Add(("StrokeRect rejects negative thickness", StrokeNegativeThicknessFails));
        tests.Add(("ResourceHandle equality is by value", ResourceHandleEquality));
        tests.Add(("ImageHandle equality is by value", ImageHandleEquality));
    }

    private static void RecordsInOrder()
    {
        var list = new BRenderList();
        list.PushClip(new BRect(0, 0, 100, 100));
        list.FillRect(new BRect(0, 0, 10, 10), BColor.Red);
        list.StrokeRect(new BRect(0, 0, 10, 10), BColor.Blue, 2.0);
        list.PopClip();

        AssertEx.AreEqual(4, list.Count);
        AssertEx.IsInstanceOf<BRenderCommand.PushClip>(list.Commands[0]);
        AssertEx.IsInstanceOf<BRenderCommand.FillRect>(list.Commands[1]);
        AssertEx.IsInstanceOf<BRenderCommand.StrokeRect>(list.Commands[2]);
        AssertEx.IsInstanceOf<BRenderCommand.PopClip>(list.Commands[3]);
    }

    private static void PreservesPayloads()
    {
        var list = new BRenderList();
        var rect = new BRect(5, 6, 7, 8);
        list.FillRect(rect, BColor.Green);

        var fill = (BRenderCommand.FillRect)list.Commands[0];
        AssertEx.AreEqual(rect, fill.Rect);
        AssertEx.AreEqual(BColor.Green, fill.Color);

        list.PushTransform(BMatrix3x2.Translation(3, 4));
        var push = (BRenderCommand.PushTransform)list.Commands[1];
        AssertEx.AreEqual(BMatrix3x2.Translation(3, 4), push.Transform);
    }

    private static void BalancedClipsValidate()
    {
        var list = new BRenderList();
        list.PushClip(new BRect(0, 0, 1, 1));
        list.PushClip(new BRect(0, 0, 1, 1));
        list.PopClip();
        list.PopClip();
        list.Validate(); // should not throw
    }

    private static void UnbalancedClipPushFails()
    {
        var list = new BRenderList();
        list.PushClip(new BRect(0, 0, 1, 1));
        AssertEx.Throws<InvalidOperationException>(list.Validate);
    }

    private static void ClipPopUnderflowFails()
    {
        var list = new BRenderList();
        list.PopClip();
        AssertEx.Throws<InvalidOperationException>(list.Validate);
    }

    private static void BalancedTransformsValidate()
    {
        var list = new BRenderList();
        list.PushTransform(BMatrix3x2.Scale(2, 2));
        list.FillRect(new BRect(0, 0, 1, 1), BColor.Black);
        list.PopTransform();
        list.Validate();
    }

    private static void TransformPopUnderflowFails()
    {
        var list = new BRenderList();
        list.PushTransform(BMatrix3x2.Identity);
        list.PopTransform();
        list.PopTransform(); // one too many
        AssertEx.Throws<InvalidOperationException>(list.Validate);
    }

    private static void DrawImageOpacityValidated()
    {
        var list = new BRenderList();
        var image = BImageHandle.FromId(1, new BSize(10, 10));
        AssertEx.Throws<ArgumentOutOfRangeException>(
            () => list.DrawImage(image, BRect.Empty, BRect.Empty, opacity: 1.5));
        AssertEx.AreEqual(0, list.Count);
    }

    private static void StrokeNegativeThicknessFails()
    {
        var list = new BRenderList();
        AssertEx.Throws<ArgumentOutOfRangeException>(
            () => list.StrokeRect(BRect.Empty, BColor.Black, thickness: -1.0));
    }

    private static void ResourceHandleEquality()
    {
        var a = new BResourceHandle(BResourceKind.Image, 42);
        var b = new BResourceHandle(BResourceKind.Image, 42);
        var c = new BResourceHandle(BResourceKind.Font, 42);

        AssertEx.AreEqual(a, b);
        AssertEx.IsTrue(a == b);
        AssertEx.AreEqual(a.GetHashCode(), b.GetHashCode());
        AssertEx.AreNotEqual(a, c);
        AssertEx.IsTrue(a != c);
    }

    private static void ImageHandleEquality()
    {
        var size = new BSize(16, 16);
        var a = BImageHandle.FromId(7, size);
        var b = BImageHandle.FromId(7, size);
        var c = BImageHandle.FromId(7, new BSize(32, 32));

        AssertEx.AreEqual(a, b);
        AssertEx.IsTrue(a == b);
        AssertEx.AreNotEqual(a, c);
        AssertEx.IsFalse(a.Equals(c));
    }
}
