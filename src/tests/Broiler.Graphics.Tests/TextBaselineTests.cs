using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Text;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>
/// A text run whose layout stated its baseline (<see cref="BTextRun.Baseline"/>) is
/// drawn standing on that line.
/// </summary>
/// <remarks>
/// The renderer put every baseline 0.8em below the run's top, whatever the layout had computed.
/// Broiler.HTML's layout puts it 0.928em down for an installed font, so Acid3's 100px bold score
/// was drawn with its digits' feet about 13px above the line the page laid out for them, and all
/// other text proportionally high.
/// </remarks>
internal static class TextBaselineTests
{
    private const double Size = 100;
    private const double Baseline = 92.8;

    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("A host-font run stands on the baseline its layout stated", HostFontStandsOnStatedBaseline));
        tests.Add(("A carried-face run stands on the baseline its layout stated", CarriedFaceStandsOnStatedBaseline));
        tests.Add(("A run with no stated baseline keeps the renderer's 0.8em", UnstatedBaselineIsUnchanged));
    }

    /// <summary>
    /// An "H" sits on the baseline, so its lowest inked row is the one just above it. On a machine
    /// with no font at all the built-in block glyph is drawn, one em tall with its foot 0.2em below
    /// the baseline, and is held to that instead.
    /// </summary>
    private static void HostFontStandsOnStatedBaseline()
    {
        var list = new BRenderList();
        list.DrawText(new BTextRun("H", new BFontStyle("sans-serif", Size), BColor.Black) { Baseline = Baseline }, new BPoint(10, 10));

        using BBitmap bitmap = Render(list);
        double expected = 10 + Baseline + (FallbackSystemFont.Shared is null ? Size * 0.2 : 0);
        int bottom = LowestInkedRow(bitmap);
        AssertEx.IsTrue(
            Math.Abs((bottom + 1) - expected) <= 1.0,
            $"The H's ink ends at row {bottom}; its foot belongs on y = {expected:0.#}.");
    }

    /// <summary>The square glyph spans the descender to the ascender: 0.2em below the baseline, 0.8em above.</summary>
    private static void CarriedFaceStandsOnStatedBaseline()
    {
        BFontFace face = FontFaceTextTests.LoadFace();
        var list = new BRenderList();
        list.DrawText(new BTextRun("X", new BFontStyle("AcidAhemTest", Size) { Face = face }, BColor.Black) { Baseline = Baseline }, new BPoint(10, 10));

        using BBitmap bitmap = Render(list);
        AssertEx.AreEqual(122, LowestInkedRow(bitmap), "the square's foot, 0.2em under a baseline at 102.8");
        AssertEx.AreEqual(BColor.White, bitmap.GetPixel(50, 21), "above the square's top at 22.8");
        AssertEx.AreEqual(BColor.Black, bitmap.GetPixel(50, 23), "inside the square");
    }

    private static void UnstatedBaselineIsUnchanged()
    {
        var list = new BRenderList();
        list.DrawText(new BTextRun("X", new BFontStyle("AcidAhemTest", Size) { Face = FontFaceTextTests.LoadFace() }, BColor.Black), new BPoint(10, 10));
        AssertEx.IsTrue(list.Commands[0] is BRenderCommand.DrawText { Text.Baseline: null }, "no baseline is recorded");

        // Without one, a carried face hangs from its ascender: the square fills 10..110.
        using BBitmap bitmap = Render(list);
        AssertEx.AreEqual(109, LowestInkedRow(bitmap));
    }

    private static BBitmap Render(BRenderList list)
    {
        using var renderer = new BImageRenderer();
        return renderer.RenderToImage(
            list,
            BSurfaceDescriptor.Default(new BSize(140, 140)),
            new BFrameContext(BColor.White));
    }

    /// <summary>The lowest row with a pixel at least half dark.</summary>
    private static int LowestInkedRow(BBitmap bitmap)
    {
        for (int y = bitmap.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).R < 128)
                    return y;
            }
        }

        throw new AssertException("Nothing was drawn.");
    }
}
