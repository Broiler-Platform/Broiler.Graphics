using System;
using System.Collections.Generic;
using System.Drawing;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Multithreading roadmap item #3: the clip narrowing and the scanline-band parallelism ported into
/// <see cref="BCanvas"/>, and the exit gate for both.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two claims, and they are checked differently.</b> The threading claim is that a thread budget
/// changes nothing about the image, so it is checked by rendering the same drawing at several
/// budgets and comparing bytes — a property that can be asserted exactly, with no tolerance.
/// The narrowing claim is that clamping a primitive's loop to the clip's bounding box removes only
/// iterations the per-pixel test would have rejected anyway, so it is checked against a reference
/// built the slow way: draw with no clip at all, then keep the pixels the clip admits. If the
/// narrowing ever dropped a pixel the clip would have kept, the two images differ.
/// </para>
/// <para>
/// <b>The cases are chosen for what makes a clip more than a rectangle.</b> An excluding clip is
/// the one that must <em>not</em> narrow anything — it removes pixels from the admitted set and can
/// never add one, so a bound derived from it would be a bound on the wrong set. A rounded clip
/// narrows to its box while still rejecting its corners per pixel. A layer records the clip in
/// force when it was pushed, not when it is popped. Each of those is a way the running intersection
/// could be wrong while a plain rectangular clip still looked right.
/// </para>
/// </remarks>
internal static class RasterBandParallelismTests
{
    private const int Width = 320;
    private const int Height = 240;

    private static readonly int[] Budgets = [1, 2, 3, 4, 8];

    public static void Register(List<(string Name, Action Body)> tests)
    {
        // Every split primitive, at every budget, against its own single-threaded render.
        foreach ((string name, Action<BCanvas> draw) in Drawings())
        {
            (string Name, Action<BCanvas> Draw) captured = (name, draw);
            tests.Add((
                $"Raster bands leave '{captured.Name}' byte-identical at every thread budget",
                () => AssertBudgetsAgree(captured.Draw)));
        }

        tests.Add(("A budget of one splits no fill at all", BudgetOfOneSplitsNothing));
        tests.Add(("A budget of two splits no fill either, by the measured floor", BudgetOfTwoSplitsNothing));
        tests.Add(("A budget above the floor does split a large fill", BudgetAboveFloorSplits));
        tests.Add(("Bands never write outside the fill's clipped bounds", BandsStayInsideTheClip));

        tests.Add(("Clip narrowing keeps every pixel the clip admits", NarrowingMatchesMaskedReference));
        tests.Add(("An excluding clip narrows nothing", ExcludingClipDoesNotNarrow));
        tests.Add(("A rounded clip narrows to its box and still rounds", RoundedClipNarrowsButStillRounds));
        tests.Add(("Nested clips narrow to the intersection", NestedClipsIntersect));
        tests.Add(("Popping a clip restores the previous bounds", PoppingAClipRestoresBounds));
        tests.Add(("Restore unwinds the clip bounds with the clip stack", RestoreUnwindsClipBounds));
        tests.Add(("A clip that admits nothing draws nothing", EmptyClipDrawsNothing));
        tests.Add(("A glyph outside the clip draws nothing", GlyphOutsideClipDrawsNothing));
        tests.Add(("A glyph across the clip edge is unchanged by narrowing", GlyphAcrossClipEdgeMatchesReference));
        tests.Add(("A layer composites over the clip it was pushed under", LayerCompositesOverItsPushClip));
        tests.Add(("A layer whose inner clips have been popped still composites", LayerIgnoresInnerClipsAtRestore));
    }

    // ── The drawings every budget has to agree on ────────────────────────────

    /// <summary>
    /// One entry per primitive that goes through the band partitioner, sized above the split
    /// threshold so the parallel path is the one being compared rather than the inline path.
    /// </summary>
    private static IEnumerable<(string Name, Action<BCanvas> Draw)> Drawings()
    {
        yield return ("fill rect", canvas => canvas.FillRect(Full, Blue));

        yield return ("fill rect under a clip", canvas =>
        {
            canvas.PushClip(new RectangleF(40, 30, 200, 160));
            canvas.FillRect(Full, Blue);
            canvas.PopClip();
        });

        yield return ("fill rect under an excluding clip", canvas =>
        {
            canvas.PushClipExclude(new RectangleF(80, 60, 120, 90));
            canvas.FillRect(Full, Blue);
            canvas.PopClip();
        });

        yield return ("fill rect under a rounded clip", canvas =>
        {
            canvas.PushClipRounded(new RectangleF(20, 20, 260, 190), 40, 30, 40, 30, 40, 30, 40, 30);
            canvas.FillRect(Full, Blue);
            canvas.PopClip();
        });

        yield return ("rounded rect", canvas => canvas.FillRoundedRect(Full, Blue, 48, 36));

        yield return ("rectangle stroke", canvas => canvas.DrawRectangleStroke(Full, Blue, 12));

        yield return ("rounded rect stroke", canvas => canvas.DrawRoundedRectangleStroke(Full, Blue, 48, 36, 10));

        yield return ("line", canvas => canvas.DrawLine(new PointF(5, 5), new PointF(310, 230), Blue, 24));

        yield return ("polygon", canvas => canvas.FillPolygon(
            [new PointF(10, 10), new PointF(300, 40), new PointF(250, 230), new PointF(30, 200)], Blue));

        yield return ("glyph contours", canvas => canvas.FillGlyphContours(BigGlyph(), Blue));

        yield return ("glyph contours under a clip", canvas =>
        {
            canvas.PushClip(new RectangleF(60, 40, 180, 150));
            canvas.FillGlyphContours(BigGlyph(), Blue);
            canvas.PopClip();
        });

        yield return ("bitmap", canvas =>
        {
            using BBitmap source = Checkerboard(64, 64);
            canvas.DrawBitmap(source, Full, new RectangleF(0, 0, 64, 64));
        });

        yield return ("tiled bitmap", canvas =>
        {
            using BBitmap source = Checkerboard(32, 32);
            canvas.FillRectTiled(source, Full, new RectangleF(0, 0, 32, 32), new PointF(3, 5));
        });

        yield return ("linear gradient", canvas => canvas.FillLinearGradientRect(
            Full, [Blue, Red, Green], null, 37f));

        yield return ("radial gradient", canvas => canvas.FillRadialGradientRect(
            Full, [Blue, Red, Green], null, 0.4f, 0.6f));

        yield return ("conic gradient", canvas => canvas.FillConicGradientRect(
            Full, [Blue, Red, Green], null, 0.5f, 0.5f, 20f));

        yield return ("opacity layer", canvas =>
        {
            canvas.PushClip(new RectangleF(30, 20, 240, 180));
            canvas.SaveOpacityLayer(0.4f);
            canvas.FillRect(Full, Blue);
            canvas.FillRoundedRect(new RectangleF(60, 50, 160, 120), Red, 30, 24);
            canvas.RestoreOpacityLayer();
            canvas.PopClip();
        });

        yield return ("blend layer", canvas =>
        {
            canvas.FillRect(Full, Green);
            canvas.SaveBlendLayer("multiply");
            canvas.FillRect(new RectangleF(20, 20, 260, 190), Blue);
            canvas.RestoreBlendLayer();
        });

        yield return ("scaled and translated", canvas =>
        {
            canvas.Save();
            canvas.Translate(17, 11);
            canvas.Scale(1.7f);
            canvas.PushClip(new RectangleF(0, 0, 150, 120));
            canvas.FillRect(Full, Blue);
            canvas.FillGlyphContours(BigGlyph(), Red);
            canvas.PopClip();
            canvas.Restore();
        });
    }

    /// <summary>
    /// Renders one drawing at every budget and asserts the bytes match the single-threaded render.
    /// </summary>
    private static void AssertBudgetsAgree(Action<BCanvas> draw)
    {
        byte[] reference = RenderAt(1, draw);
        foreach (int budget in Budgets)
        {
            byte[] actual = RenderAt(budget, draw);
            AssertEx.IsTrue(
                SameBytes(reference, actual),
                $"Rendering at a budget of {budget} thread(s) produced different pixels than at one.");
        }
    }

    // ── What the partitioner decided ─────────────────────────────────────────

    private static void BudgetOfOneSplitsNothing() =>
        AssertSplitCount(budget: 1, expectedSplits: 0);

    /// <summary>
    /// A two-way split measured slower than no split at all, so the partitioner refuses it; see
    /// <c>BRasterParallelism.MinimumBandCount</c>. Without this the two-core case is a regression
    /// rather than a speedup, which is the one result a parallel path must not produce.
    /// </summary>
    private static void BudgetOfTwoSplitsNothing() =>
        AssertSplitCount(budget: 2, expectedSplits: 0);

    private static void BudgetAboveFloorSplits() =>
        AssertSplitCount(budget: 4, expectedSplits: 1);

    private static void AssertSplitCount(int budget, int expectedSplits)
    {
        int configured = BRasterParallelism.MaxDegreeOfParallelism;
        BRasterParallelism.MaxDegreeOfParallelism = budget;
        BRasterParallelism.ResetDiagnostics();
        BRasterParallelism.CollectDiagnostics = true;
        try
        {
            using var bitmap = new BBitmap(Width, Height);
            using BCanvas canvas = bitmap.OpenCanvas();
            canvas.FillRect(Full, Blue);

            (long inline, long split, _, _) = BRasterParallelism.Diagnostics;
            AssertEx.AreEqual(expectedSplits, (int)split, $"Wrong split count at a budget of {budget}.");
            AssertEx.AreEqual(expectedSplits == 0 ? 1 : 0, (int)inline, $"Wrong inline count at a budget of {budget}.");
        }
        finally
        {
            BRasterParallelism.CollectDiagnostics = false;
            BRasterParallelism.MaxDegreeOfParallelism = configured;
        }
    }

    /// <summary>
    /// A split fill under a clip must not touch a pixel outside it. Checked on the surface rather
    /// than through the diagnostics, because a band that computed its rows from the unclipped
    /// bounds would still report one split.
    /// </summary>
    private static void BandsStayInsideTheClip()
    {
        var clip = new RectangleF(50, 40, 200, 150);
        byte[] pixels = RenderAt(8, canvas =>
        {
            canvas.PushClip(clip);
            canvas.FillRect(Full, Blue);
            canvas.PopClip();
        });

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool inside = clip.Contains(x + 0.5f, y + 0.5f);
                bool painted = !IsWhite(pixels, x, y);
                AssertEx.AreEqual(inside, painted, $"Pixel ({x},{y}) should {(inside ? "" : "not ")}have been painted.");
            }
        }
    }

    // ── The narrowing keeps exactly the pixels the clip admits ───────────────

    /// <summary>
    /// The general check: a clipped render must equal the unclipped render with the clip applied
    /// afterwards as a mask. Narrowing can only fail by removing a pixel too many, and this is what
    /// would see it.
    /// </summary>
    private static void NarrowingMatchesMaskedReference()
    {
        var clip = new RectangleF(43.5f, 27.25f, 173.75f, 121.5f);

        byte[] clipped = RenderAt(1, canvas =>
        {
            canvas.PushClip(clip);
            canvas.FillLinearGradientRect(Full, [Blue, Red, Green], null, 33f);
            canvas.PopClip();
        });

        byte[] unclipped = RenderAt(1, canvas =>
            canvas.FillLinearGradientRect(Full, [Blue, Red, Green], null, 33f));

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool admitted = clip.Contains(x + 0.5f, y + 0.5f);
                AssertEx.IsTrue(
                    SamePixel(clipped, admitted ? unclipped : WhiteSurface, x, y),
                    $"Pixel ({x},{y}) disagrees with the masked reference.");
            }
        }
    }

    /// <summary>
    /// An excluding clip removes pixels, so a bound taken from it would be a bound on the pixels it
    /// <em>rejects</em>. Anything drawn outside the excluded rectangle must survive.
    /// </summary>
    private static void ExcludingClipDoesNotNarrow()
    {
        var excluded = new RectangleF(120, 90, 80, 60);
        byte[] pixels = RenderAt(4, canvas =>
        {
            canvas.PushClipExclude(excluded);
            canvas.FillRect(Full, Blue);
            canvas.PopClip();
        });

        AssertEx.IsTrue(IsWhite(pixels, 160, 120), "The excluded region should be untouched.");
        AssertEx.IsFalse(IsWhite(pixels, 2, 2), "The top-left corner is outside the exclusion and should be painted.");
        AssertEx.IsFalse(IsWhite(pixels, Width - 3, Height - 3), "The bottom-right corner should be painted.");
    }

    private static void RoundedClipNarrowsButStillRounds()
    {
        var box = new RectangleF(60, 40, 200, 160);
        byte[] pixels = RenderAt(4, canvas =>
        {
            canvas.PushClipRounded(box, 60, 50, 60, 50, 60, 50, 60, 50);
            canvas.FillRect(Full, Blue);
            canvas.PopClip();
        });

        AssertEx.IsFalse(IsWhite(pixels, 160, 120), "The middle of the rounded box should be painted.");
        AssertEx.IsTrue(IsWhite(pixels, 61, 41), "The rounded corner should still be cut away.");
        AssertEx.IsTrue(IsWhite(pixels, 10, 10), "Outside the box should be untouched.");
    }

    private static void NestedClipsIntersect()
    {
        byte[] pixels = RenderAt(4, canvas =>
        {
            canvas.PushClip(new RectangleF(40, 40, 200, 100));
            canvas.PushClip(new RectangleF(120, 20, 160, 180));
            canvas.FillRect(Full, Blue);
            canvas.PopClip();
            canvas.PopClip();
        });

        AssertEx.IsFalse(IsWhite(pixels, 160, 80), "The intersection should be painted.");
        AssertEx.IsTrue(IsWhite(pixels, 60, 80), "Left of the inner clip should be untouched.");
        AssertEx.IsTrue(IsWhite(pixels, 160, 160), "Below the outer clip should be untouched.");
    }

    private static void PoppingAClipRestoresBounds()
    {
        byte[] pixels = RenderAt(4, canvas =>
        {
            canvas.PushClip(new RectangleF(100, 100, 40, 40));
            canvas.PopClip();
            canvas.FillRect(Full, Blue);
        });

        AssertEx.IsFalse(IsWhite(pixels, 5, 5), "After the pop the whole surface is drawable again.");
        AssertEx.IsFalse(IsWhite(pixels, Width - 5, Height - 5), "After the pop the whole surface is drawable again.");
    }

    private static void RestoreUnwindsClipBounds()
    {
        byte[] pixels = RenderAt(4, canvas =>
        {
            canvas.Save();
            canvas.PushClip(new RectangleF(100, 100, 40, 40));
            canvas.PushClip(new RectangleF(110, 110, 10, 10));
            canvas.Restore();
            canvas.FillRect(Full, Blue);
        });

        AssertEx.IsFalse(IsWhite(pixels, 5, 5), "Restore should have unwound both clips.");
        AssertEx.IsFalse(IsWhite(pixels, Width - 5, Height - 5), "Restore should have unwound both clips.");
    }

    private static void EmptyClipDrawsNothing()
    {
        byte[] pixels = RenderAt(4, canvas =>
        {
            canvas.PushClip(new RectangleF(40, 40, 60, 60));
            canvas.PushClip(new RectangleF(200, 200, 60, 60));
            canvas.FillRect(Full, Blue);
            canvas.PopClip();
            canvas.PopClip();
        });

        AssertEx.IsTrue(SameBytes(WhiteSurface, pixels), "Two disjoint clips admit nothing, so nothing should be drawn.");
    }

    /// <summary>
    /// The glyph path rejects on its bounding box before it transforms and copies its points, which
    /// is a second place the narrowing could drop a glyph it should have drawn.
    /// </summary>
    private static void GlyphOutsideClipDrawsNothing()
    {
        byte[] pixels = RenderAt(4, canvas =>
        {
            canvas.PushClip(new RectangleF(0, 0, 60, 60));
            canvas.FillGlyphContours([Triangle(150, 150, 60)], Blue);
            canvas.PopClip();
        });

        AssertEx.IsTrue(SameBytes(WhiteSurface, pixels), "A glyph entirely outside the clip should draw nothing.");
    }

    private static void GlyphAcrossClipEdgeMatchesReference()
    {
        var clip = new RectangleF(0, 0, 160, 240);
        IReadOnlyList<PointF[]> glyph = [Triangle(140, 120, 90)];

        byte[] clipped = RenderAt(4, canvas =>
        {
            canvas.PushClip(clip);
            canvas.FillGlyphContours(glyph, Blue);
            canvas.PopClip();
        });

        byte[] unclipped = RenderAt(1, canvas => canvas.FillGlyphContours(glyph, Blue));

        bool anyPainted = false;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool admitted = clip.Contains(x + 0.5f, y + 0.5f);
                AssertEx.IsTrue(
                    SamePixel(clipped, admitted ? unclipped : WhiteSurface, x, y),
                    $"Glyph pixel ({x},{y}) disagrees with the masked reference.");
                anyPainted |= admitted && !IsWhite(clipped, x, y);
            }
        }

        AssertEx.IsTrue(anyPainted, "The glyph should straddle the clip edge, not miss it entirely.");
    }

    /// <summary>
    /// A layer's buffer is composited over the box its clip admitted when it was <em>pushed</em>.
    /// A layer pushed with no clip must still composite over the whole surface.
    /// </summary>
    private static void LayerCompositesOverItsPushClip()
    {
        byte[] clippedLayer = RenderAt(4, canvas =>
        {
            canvas.PushClip(new RectangleF(40, 30, 200, 150));
            canvas.SaveOpacityLayer(0.5f);
            canvas.FillRect(Full, Blue);
            canvas.RestoreOpacityLayer();
            canvas.PopClip();
        });

        AssertEx.IsFalse(IsWhite(clippedLayer, 120, 100), "Inside the clip the layer should have landed.");
        AssertEx.IsTrue(IsWhite(clippedLayer, 5, 5), "Outside the clip nothing should have landed.");

        byte[] unclippedLayer = RenderAt(4, canvas =>
        {
            canvas.SaveOpacityLayer(0.5f);
            canvas.FillRect(Full, Blue);
            canvas.RestoreOpacityLayer();
        });

        AssertEx.IsFalse(IsWhite(unclippedLayer, 2, 2), "An unclipped layer composites over the whole surface.");
        AssertEx.IsFalse(IsWhite(unclippedLayer, Width - 3, Height - 3), "An unclipped layer composites over the whole surface.");
    }

    /// <summary>
    /// The bound is taken when the layer is pushed, so clips the layer's own content pushed and
    /// popped in between must not shrink it. Reading the stack at restore time would.
    /// </summary>
    private static void LayerIgnoresInnerClipsAtRestore()
    {
        byte[] pixels = RenderAt(4, canvas =>
        {
            canvas.SaveOpacityLayer(0.5f);
            canvas.PushClip(new RectangleF(100, 100, 20, 20));
            canvas.FillRect(new RectangleF(100, 100, 20, 20), Red);
            canvas.PopClip();
            canvas.FillRect(new RectangleF(0, 0, 40, 40), Blue);
            canvas.RestoreOpacityLayer();
        });

        AssertEx.IsFalse(IsWhite(pixels, 10, 10), "Content drawn after the inner clip was popped must composite.");
        AssertEx.IsFalse(IsWhite(pixels, 110, 110), "Content drawn under the inner clip must composite too.");
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static RectangleF Full => new(0, 0, Width, Height);

    private static BColor Blue => new(0x20, 0x40, 0xE0);

    private static BColor Red => new(0xE0, 0x30, 0x30);

    private static BColor Green => new(0x20, 0xC0, 0x40);

    private static readonly byte[] WhiteSurface = BuildWhiteSurface();

    private static byte[] BuildWhiteSurface()
    {
        using var bitmap = new BBitmap(Width, Height);
        bitmap.Clear(BColor.White);
        return bitmap.CopyRgba();
    }

    private static byte[] RenderAt(int budget, Action<BCanvas> draw)
    {
        int configured = BRasterParallelism.MaxDegreeOfParallelism;
        BRasterParallelism.MaxDegreeOfParallelism = budget;
        try
        {
            using var bitmap = new BBitmap(Width, Height);
            bitmap.Clear(BColor.White);
            using (BCanvas canvas = bitmap.OpenCanvas())
                draw(canvas);

            return bitmap.CopyRgba();
        }
        finally
        {
            BRasterParallelism.MaxDegreeOfParallelism = configured;
        }
    }

    /// <summary>A glyph large enough to clear the split threshold, so the banded path is exercised.</summary>
    private static IReadOnlyList<PointF[]> BigGlyph() =>
    [
        [new PointF(30, 20), new PointF(290, 40), new PointF(240, 220), new PointF(40, 200)],
        [new PointF(110, 80), new PointF(200, 90), new PointF(180, 160), new PointF(120, 150)],
    ];

    private static PointF[] Triangle(float centerX, float centerY, float radius) =>
    [
        new PointF(centerX, centerY - radius),
        new PointF(centerX + radius, centerY + radius),
        new PointF(centerX - radius, centerY + radius),
    ];

    private static BBitmap Checkerboard(int width, int height)
    {
        var bitmap = new BBitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                bitmap.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? Blue : Red);
        }

        return bitmap;
    }

    private static bool IsWhite(byte[] pixels, int x, int y) => SamePixel(pixels, WhiteSurface, x, y);

    private static bool SamePixel(byte[] left, byte[] right, int x, int y)
    {
        int index = ((y * Width) + x) * 4;
        return left[index] == right[index]
            && left[index + 1] == right[index + 1]
            && left[index + 2] == right[index + 2]
            && left[index + 3] == right[index + 3];
    }

    private static bool SameBytes(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length)
            return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
                return false;
        }

        return true;
    }
}
