using System;
using System.Collections.Generic;

namespace Broiler.Graphics.WebAssembly.Tests;

/// <summary>
/// Op-level tests for <see cref="CanvasFramePlanner"/>: command encoding, bounding-box
/// transform baking, the independent clip/transform stacks, lazy clip emission, and the
/// fallback/validation contract.
/// </summary>
internal static class CanvasFramePlannerTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("FillRect encodes device rect and color", FillRectEncodes));
        tests.Add(("Transparent fill is dropped", TransparentFillDropped));
        tests.Add(("Translation bakes into device rect", TranslationBakes));
        tests.Add(("DPR scales device rect", DprScales));
        tests.Add(("Clip emitted once, reused, cleared on pop", ClipLazyEmission));
        tests.Add(("Nested clips intersect", NestedClipsIntersect));
        tests.Add(("Pop clip keeps active transform", InterleavedClipTransform));
        tests.Add(("StrokeRect scales thickness with min 1", StrokeThicknessScales));
        tests.Add(("Rounded rect radii scale with transform", RoundedRadiiScale));
        tests.Add(("DrawText bakes baseline and string table", DrawTextEncodes));
        tests.Add(("DrawImage encodes source, dest, opacity", DrawImageEncodes));
        tests.Add(("All ten command kinds plan without fallback", AllKindsNoFallback));
        tests.Add(("Unbalanced list is rejected", UnbalancedRejected));
        tests.Add(("Planner is reusable across frames", ReusableAcrossFrames));
    }

    private static CanvasFrame Plan(BRenderList list, double dpr = 1.0, int backing = 256) =>
        new CanvasFramePlanner().Plan(list, backing, backing, dpr, BColor.White);

    private static void FillRectEncodes()
    {
        var list = new BRenderList();
        list.FillRect(new BRect(5, 6, 10, 8), BColor.Red);

        List<ReplayOp> ops = ReplayStream.Parse(Plan(list));
        AssertEx.AreEqual(1, ops.Count);
        ReplayOp fill = ops.Single(CanvasReplayOp.FillRect);
        AssertEx.AreClose(5, fill.Operands[0]);
        AssertEx.AreClose(6, fill.Operands[1]);
        AssertEx.AreClose(10, fill.Operands[2]);
        AssertEx.AreClose(8, fill.Operands[3]);
        AssertEx.AreEqual(BColor.Red.ToArgb(), (uint)fill.Operands[4]);
    }

    private static void TransparentFillDropped()
    {
        var list = new BRenderList();
        list.FillRect(new BRect(0, 0, 10, 10), new BColor(1, 2, 3, 0));
        AssertEx.AreEqual(0, Plan(list).OpCount);
    }

    private static void TranslationBakes()
    {
        var list = new BRenderList();
        list.PushTransform(BMatrix3x2.Translation(10, 20));
        list.FillRect(new BRect(1, 2, 3, 4), BColor.Black);
        list.PopTransform();

        ReplayOp fill = ReplayStream.Parse(Plan(list)).Single(CanvasReplayOp.FillRect);
        AssertEx.AreClose(11, fill.Operands[0]);
        AssertEx.AreClose(22, fill.Operands[1]);
        AssertEx.AreClose(3, fill.Operands[2]);
        AssertEx.AreClose(4, fill.Operands[3]);
    }

    private static void DprScales()
    {
        var list = new BRenderList();
        list.FillRect(new BRect(1, 2, 3, 4), BColor.Black);

        ReplayOp fill = ReplayStream.Parse(Plan(list, dpr: 2.0)).Single(CanvasReplayOp.FillRect);
        AssertEx.AreClose(2, fill.Operands[0]);
        AssertEx.AreClose(4, fill.Operands[1]);
        AssertEx.AreClose(6, fill.Operands[2]);
        AssertEx.AreClose(8, fill.Operands[3]);
    }

    private static void ClipLazyEmission()
    {
        var list = new BRenderList();
        list.PushClip(new BRect(0, 0, 20, 20));
        list.FillRect(new BRect(0, 0, 5, 5), BColor.Red);
        list.FillRect(new BRect(1, 1, 5, 5), BColor.Blue);
        list.PopClip();
        list.FillRect(new BRect(2, 2, 5, 5), BColor.Green);

        List<ReplayOp> ops = ReplayStream.Parse(Plan(list));
        // One SetClip (shared by the two clipped fills), one ClearClip before the last fill.
        AssertEx.AreEqual(1, ops.Count(CanvasReplayOp.SetClip));
        AssertEx.AreEqual(1, ops.Count(CanvasReplayOp.ClearClip));
        AssertEx.AreEqual(3, ops.Count(CanvasReplayOp.FillRect));
        // Order: SetClip, Fill, Fill, ClearClip, Fill.
        AssertEx.AreEqual(CanvasReplayOp.SetClip, ops[0].Code);
        AssertEx.AreEqual(CanvasReplayOp.ClearClip, ops[3].Code);
    }

    private static void NestedClipsIntersect()
    {
        var list = new BRenderList();
        list.PushClip(new BRect(0, 0, 20, 20));
        list.PushClip(new BRect(10, 10, 20, 20));
        list.FillRect(new BRect(0, 0, 40, 40), BColor.Red);
        list.PopClip();
        list.PopClip();

        ReplayOp clip = ReplayStream.Parse(Plan(list)).Single(CanvasReplayOp.SetClip);
        AssertEx.AreClose(10, clip.Operands[0]);
        AssertEx.AreClose(10, clip.Operands[1]);
        AssertEx.AreClose(10, clip.Operands[2]);
        AssertEx.AreClose(10, clip.Operands[3]);
    }

    private static void InterleavedClipTransform()
    {
        // PushClip, PushTransform, PopClip, draw, PopTransform: the transform must
        // survive the clip pop, and the draw must be unclipped.
        var list = new BRenderList();
        list.PushClip(new BRect(0, 0, 5, 5));
        list.PushTransform(BMatrix3x2.Translation(100, 100));
        list.PopClip();
        list.FillRect(new BRect(1, 1, 2, 2), BColor.Black);
        list.PopTransform();

        List<ReplayOp> ops = ReplayStream.Parse(Plan(list));
        AssertEx.AreEqual(0, ops.Count(CanvasReplayOp.SetClip), "Clip was popped; no clip should be applied.");
        AssertEx.AreEqual(0, ops.Count(CanvasReplayOp.ClearClip), "Clip was never applied, so nothing to clear.");
        ReplayOp fill = ops.Single(CanvasReplayOp.FillRect);
        AssertEx.AreClose(101, fill.Operands[0], message: "Transform must survive the clip pop.");
        AssertEx.AreClose(101, fill.Operands[1]);
    }

    private static void StrokeThicknessScales()
    {
        var list = new BRenderList();
        list.PushTransform(BMatrix3x2.Scale(2, 2));
        list.StrokeRect(new BRect(0, 0, 10, 10), BColor.Black, 2.0);
        list.PopTransform();

        ReplayOp stroke = ReplayStream.Parse(Plan(list)).Single(CanvasReplayOp.StrokeRect);
        AssertEx.AreClose(20, stroke.Operands[2]);
        AssertEx.AreClose(20, stroke.Operands[3]);
        AssertEx.AreClose(4, stroke.Operands[4], message: "Thickness scales by average scale 2.");
    }

    private static void RoundedRadiiScale()
    {
        var list = new BRenderList();
        list.PushTransform(BMatrix3x2.Scale(2, 2));
        list.FillRoundedRect(new BRect(0, 0, 10, 10), BColor.Black, 3, 3);
        list.PopTransform();

        ReplayOp rounded = ReplayStream.Parse(Plan(list)).Single(CanvasReplayOp.FillRoundedRect);
        AssertEx.AreClose(6, rounded.Operands[4]);
        AssertEx.AreClose(6, rounded.Operands[5]);
    }

    private static void DrawTextEncodes()
    {
        var list = new BRenderList();
        list.DrawText(
            new BTextRun("Hi", new BFontStyle("sans-serif", 10, BFontWeight.Bold), BColor.Black),
            new BPoint(4, 5));

        CanvasFrame frame = Plan(list);
        ReplayOp text = ReplayStream.Parse(frame).Single(CanvasReplayOp.DrawText);
        AssertEx.AreClose(4, text.Operands[0], message: "Baseline X.");
        AssertEx.AreClose(13, text.Operands[1], message: "Baseline Y = origin.Y + 0.8 * fontSize.");
        AssertEx.AreClose(10, text.Operands[2], message: "Font px.");
        AssertEx.AreClose(700, text.Operands[3], message: "Bold weight.");
        AssertEx.AreClose(0, text.Operands[4], message: "Not italic.");
        AssertEx.AreEqual(1, frame.Strings.Count);
        AssertEx.AreEqual("Hi", frame.Strings[0]);
        AssertEx.AreEqual(0, (int)text.Operands[6], message: "String index 0.");
    }

    private static void DrawImageEncodes()
    {
        var list = new BRenderList();
        BImageHandle image = BImageHandle.FromId(7, new BSize(2, 2));
        list.DrawImage(image, new BRect(0, 0, 2, 2), new BRect(25, 20, 4, 4), 0.5);

        ReplayOp draw = ReplayStream.Parse(Plan(list)).Single(CanvasReplayOp.DrawImage);
        AssertEx.AreEqual(7, (int)draw.Operands[0]);
        AssertEx.AreClose(0, draw.Operands[1]);
        AssertEx.AreClose(2, draw.Operands[4]);
        AssertEx.AreClose(25, draw.Operands[5]);
        AssertEx.AreClose(4, draw.Operands[8]);
        AssertEx.AreClose(0.5, draw.Operands[9]);
    }

    private static void AllKindsNoFallback()
    {
        var list = new BRenderList();
        list.FillRect(new BRect(0, 0, 10, 10), BColor.Red);
        list.StrokeRect(new BRect(0, 0, 10, 10), BColor.Blue, 1);
        list.FillRoundedRect(new BRect(0, 0, 10, 10), BColor.Green, 2, 2);
        list.StrokeRoundedRect(new BRect(0, 0, 10, 10), BColor.Black, 2, 2, 1);
        list.DrawText(new BTextRun("x"), new BPoint(1, 1));
        list.DrawImage(BImageHandle.FromId(1, new BSize(2, 2)), new BRect(0, 0, 2, 2), new BRect(0, 0, 4, 4));
        list.PushClip(new BRect(0, 0, 5, 5));
        list.PushTransform(BMatrix3x2.Translation(1, 1));
        list.FillRect(new BRect(0, 0, 2, 2), BColor.Black);
        list.PopTransform();
        list.PopClip();

        CanvasFrame frame = Plan(list);
        AssertEx.IsFalse(frame.RequiresCpuFallback, "Bounding-box policy represents every current command.");
        List<ReplayOp> ops = ReplayStream.Parse(frame);
        AssertEx.IsTrue(ops.Count(CanvasReplayOp.FillRect) >= 1);
        AssertEx.IsTrue(ops.Count(CanvasReplayOp.StrokeRect) == 1);
        AssertEx.IsTrue(ops.Count(CanvasReplayOp.FillRoundedRect) == 1);
        AssertEx.IsTrue(ops.Count(CanvasReplayOp.StrokeRoundedRect) == 1);
        AssertEx.IsTrue(ops.Count(CanvasReplayOp.DrawText) == 1);
        AssertEx.IsTrue(ops.Count(CanvasReplayOp.DrawImage) == 1);
        AssertEx.IsTrue(ops.Count(CanvasReplayOp.SetClip) == 1);
    }

    private static void UnbalancedRejected()
    {
        var list = new BRenderList();
        list.PushClip(new BRect(0, 0, 1, 1)); // never popped
        AssertEx.Throws<InvalidOperationException>(() => Plan(list));
    }

    private static void ReusableAcrossFrames()
    {
        var planner = new CanvasFramePlanner();

        var first = new BRenderList();
        first.FillRect(new BRect(0, 0, 3, 3), BColor.Red);
        CanvasFrame a = planner.Plan(first, 64, 64, 1.0, BColor.White);
        AssertEx.AreEqual(1, a.OpCount);

        var second = new BRenderList();
        second.PushClip(new BRect(0, 0, 10, 10));
        second.FillRect(new BRect(0, 0, 3, 3), BColor.Blue);
        second.FillRect(new BRect(1, 1, 3, 3), BColor.Green);
        second.PopClip();
        CanvasFrame b = planner.Plan(second, 64, 64, 1.0, BColor.White);
        // The second plan overwrites the reused buffers; its op count reflects only itself.
        AssertEx.AreEqual(3, b.OpCount);
        AssertEx.AreEqual(1, ReplayStream.Parse(b).Count(CanvasReplayOp.SetClip));
    }
}
