using System;
using System.Collections.Generic;
using Broiler.Media.Image;
using Spec = Broiler.Graphics.Tests.PngFormatBuilder.ApngFrameSpec;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Tests for APNG decoding and frame compositing. Frames are built with
/// <see cref="PngFormatBuilder.BuildApng"/>; the decoder must composite them onto
/// the canvas per the spec's blend (SOURCE/OVER) and dispose (NONE/BACKGROUND/
/// PREVIOUS) rules. Expected pixel values are derived from the spec, independently
/// of the decoder.
/// </summary>
internal static class ApngTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("APNG decodes frame count, size and loop", ApngBasics));
        tests.Add(("APNG SOURCE blend overwrites a region", ApngSourceBlend));
        tests.Add(("APNG OVER blend composites alpha", ApngOverBlend));
        tests.Add(("APNG dispose BACKGROUND clears region", ApngDisposeBackground));
        tests.Add(("APNG dispose PREVIOUS reverts region", ApngDisposePrevious));
        tests.Add(("APNG per-frame delays are decoded", ApngDelays));
        tests.Add(("Static Decode of APNG returns default image", ApngStaticDecode));
        tests.Add(("DecodeAnimation of plain PNG is single frame", PlainPngSingleFrame));
        tests.Add(("APNG encode round-trips frames and timing", ApngEncodeRoundTrip));
        tests.Add(("Encoded APNG default image is frame 0", ApngEncodeDefaultImage));
        tests.Add(("EncodeAnimation rejects mismatched frame size", ApngEncodeRejectsBadSize));
        tests.Add(("EncodeAnimation rejects non-PNG formats", ApngEncodeRejectsNonPng));
    }

    private static BImageSequence MakeSequence(int w, int h, int loop, params (int dn, int dd)[] delays)
    {
        var frames = new List<BImageFrame>();
        for (int k = 0; k < delays.Length; k++)
        {
            byte[] px = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                px[i * 4] = (byte)(i * 3 + k * 40);
                px[i * 4 + 1] = (byte)(i * 5 + k * 20);
                px[i * 4 + 2] = (byte)(k * 60 + i);
                px[i * 4 + 3] = (byte)(200 + k * 10);
            }
            frames.Add(new BImageFrame(new BPixelBuffer(w, h, px), delays[k].dn, delays[k].dd));
        }
        return new BImageSequence(frames, w, h, loop);
    }

    private static byte[] Fill(int w, int h, byte r, byte g, byte b, byte a)
    {
        byte[] px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = r;
            px[i * 4 + 1] = g;
            px[i * 4 + 2] = b;
            px[i * 4 + 3] = a;
        }
        return px;
    }

    private static void AssertPixel(BPixelBuffer buf, int x, int y, byte r, byte g, byte b, byte a, string where)
    {
        int o = (y * buf.Width + x) * 4;
        if (buf.Rgba[o] != r || buf.Rgba[o + 1] != g || buf.Rgba[o + 2] != b || buf.Rgba[o + 3] != a)
            throw new AssertException(
                $"{where}: pixel ({x},{y}) expected [{r},{g},{b},{a}] but was " +
                $"[{buf.Rgba[o]},{buf.Rgba[o + 1]},{buf.Rgba[o + 2]},{buf.Rgba[o + 3]}].");
    }

    private static void ApngBasics()
    {
        var frames = new List<Spec>
        {
            new(4, 4, 0, 0, 1, 10, 0, 0, Fill(4, 4, 255, 0, 0, 255)),
            new(4, 4, 0, 0, 1, 10, 0, 0, Fill(4, 4, 0, 255, 0, 255)),
        };
        byte[] apng = PngFormatBuilder.BuildApng(4, 4, numPlays: 0, frames);
        var seq = MediaImageBridge.DecodeAnimation(apng);

        AssertEx.IsTrue(seq.IsAnimated, "should be animated");
        AssertEx.AreEqual(2, seq.Frames.Count);
        AssertEx.AreEqual(4, seq.Width);
        AssertEx.AreEqual(4, seq.Height);
        AssertEx.AreEqual(0, seq.LoopCount, "0 plays == loop forever");
        AssertPixel(seq.Frames[0].Pixels, 2, 2, 255, 0, 0, 255, "frame0");
        AssertPixel(seq.Frames[1].Pixels, 2, 2, 0, 255, 0, 255, "frame1");
    }

    private static void ApngSourceBlend()
    {
        var frames = new List<Spec>
        {
            new(4, 4, 0, 0, 1, 10, 0, 0, Fill(4, 4, 255, 0, 0, 255)),           // full red
            new(2, 2, 1, 1, 1, 10, 0, 0, Fill(2, 2, 0, 0, 255, 255)),           // blue 2x2 at (1,1), SOURCE
        };
        var seq = MediaImageBridge.DecodeAnimation(PngFormatBuilder.BuildApng(4, 4, 1, frames));

        AssertPixel(seq.Frames[1].Pixels, 0, 0, 255, 0, 0, 255, "outside region stays red");
        AssertPixel(seq.Frames[1].Pixels, 1, 1, 0, 0, 255, 255, "region overwritten blue");
        AssertPixel(seq.Frames[1].Pixels, 2, 2, 0, 0, 255, 255, "region overwritten blue");
        AssertPixel(seq.Frames[1].Pixels, 3, 3, 255, 0, 0, 255, "outside region stays red");
    }

    private static void ApngOverBlend()
    {
        var frames = new List<Spec>
        {
            new(2, 2, 0, 0, 1, 10, 0, 0, Fill(2, 2, 255, 0, 0, 255)),           // opaque red
            new(2, 2, 0, 0, 1, 10, 0, 1, Fill(2, 2, 0, 255, 0, 128)),           // green @ a=128, OVER
        };
        var seq = MediaImageBridge.DecodeAnimation(PngFormatBuilder.BuildApng(2, 2, 1, frames));

        // over(src=(0,255,0,128), dst=(255,0,0,255)): a=255, r=127, g=128, b=0.
        AssertPixel(seq.Frames[1].Pixels, 0, 0, 127, 128, 0, 255, "over-blended");
        AssertPixel(seq.Frames[1].Pixels, 1, 1, 127, 128, 0, 255, "over-blended");
    }

    private static void ApngDisposeBackground()
    {
        var frames = new List<Spec>
        {
            new(4, 4, 0, 0, 1, 10, 0, 0, Fill(4, 4, 255, 0, 0, 255)),           // full red, dispose NONE
            new(2, 2, 0, 0, 1, 10, 1, 0, Fill(2, 2, 0, 0, 255, 255)),           // blue at (0,0), dispose BACKGROUND
            new(2, 2, 2, 2, 1, 10, 0, 0, Fill(2, 2, 0, 255, 0, 255)),           // green at (2,2)
        };
        var seq = MediaImageBridge.DecodeAnimation(PngFormatBuilder.BuildApng(4, 4, 1, frames));

        // After frame1's BACKGROUND dispose, its region is cleared to transparent before frame2.
        AssertPixel(seq.Frames[2].Pixels, 0, 0, 0, 0, 0, 0, "disposed-to-background region is transparent");
        AssertPixel(seq.Frames[2].Pixels, 2, 2, 0, 255, 0, 255, "frame2 green region");
        AssertPixel(seq.Frames[2].Pixels, 3, 0, 255, 0, 0, 255, "untouched region stays red");
    }

    private static void ApngDisposePrevious()
    {
        var frames = new List<Spec>
        {
            new(4, 4, 0, 0, 1, 10, 0, 0, Fill(4, 4, 255, 0, 0, 255)),           // full red, dispose NONE
            new(2, 2, 1, 1, 1, 10, 2, 0, Fill(2, 2, 0, 0, 255, 255)),           // blue at (1,1), dispose PREVIOUS
            new(1, 1, 0, 0, 1, 10, 0, 0, Fill(1, 1, 0, 255, 0, 255)),           // green at (0,0)
        };
        var seq = MediaImageBridge.DecodeAnimation(PngFormatBuilder.BuildApng(4, 4, 1, frames));

        AssertPixel(seq.Frames[1].Pixels, 1, 1, 0, 0, 255, 255, "frame1 shows blue");
        // PREVIOUS reverts the blue region back to what it was (red) before frame2.
        AssertPixel(seq.Frames[2].Pixels, 1, 1, 255, 0, 0, 255, "region reverted to red");
        AssertPixel(seq.Frames[2].Pixels, 0, 0, 0, 255, 0, 255, "frame2 green pixel");
    }

    private static void ApngDelays()
    {
        var frames = new List<Spec>
        {
            new(1, 1, 0, 0, 1, 10, 0, 0, Fill(1, 1, 10, 20, 30, 255)),
            new(1, 1, 0, 0, 3, 0, 0, 0, Fill(1, 1, 40, 50, 60, 255)),  // den 0 ⇒ 1/100s
        };
        var seq = MediaImageBridge.DecodeAnimation(PngFormatBuilder.BuildApng(1, 1, 5, frames));

        AssertEx.AreEqual(5, seq.LoopCount);
        AssertEx.AreEqual(1, seq.Frames[0].DelayNumerator);
        AssertEx.AreEqual(10, seq.Frames[0].DelayDenominator);
        AssertEx.IsTrue(Math.Abs(seq.Frames[0].Delay.TotalSeconds - 0.1) < 1e-9, "frame0 delay 0.1s");
        AssertEx.IsTrue(Math.Abs(seq.Frames[1].Delay.TotalSeconds - 0.03) < 1e-9, "den 0 means hundredths");
    }

    private static void ApngStaticDecode()
    {
        var frames = new List<Spec>
        {
            new(2, 2, 0, 0, 1, 10, 0, 0, Fill(2, 2, 1, 2, 3, 255)),
            new(2, 2, 0, 0, 1, 10, 0, 0, Fill(2, 2, 9, 9, 9, 255)),
        };
        byte[] apng = PngFormatBuilder.BuildApng(2, 2, 1, frames);

        // Plain Decode returns the default image (the IDAT = first frame).
        var still = MediaImageBridge.Decode(apng);
        AssertEx.AreEqual(2, still.Width);
        AssertPixel(still, 0, 0, 1, 2, 3, 255, "default image is frame 0");
    }

    private static void PlainPngSingleFrame()
    {
        var src = new BPixelBuffer(3, 3, FillBuf(3, 3));
        byte[] png = MediaImageBridge.Encode(src, ImageEncodeFormat.Png);
        var seq = MediaImageBridge.DecodeAnimation(png);

        AssertEx.IsFalse(seq.IsAnimated, "plain PNG is not animated");
        AssertEx.AreEqual(1, seq.Frames.Count);
        AssertEx.AreEqual(1, seq.LoopCount);
        for (int i = 0; i < src.Rgba.Length; i++)
            AssertEx.AreEqual(src.Rgba[i], seq.FirstFrame.Rgba[i]);
    }

    private static byte[] FillBuf(int w, int h)
    {
        byte[] px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = (byte)(i * 7);
            px[i * 4 + 1] = (byte)(i * 11);
            px[i * 4 + 2] = (byte)(i * 13);
            px[i * 4 + 3] = 255;
        }
        return px;
    }

    private static void ApngEncodeRoundTrip()
    {
        var src = MakeSequence(6, 5, loop: 3, (1, 10), (2, 10), (5, 100));
        byte[] apng = MediaImageBridge.EncodeAnimation(src);

        var decoded = MediaImageBridge.DecodeAnimation(apng);
        AssertEx.IsTrue(decoded.IsAnimated, "round-tripped APNG is animated");
        AssertEx.AreEqual(src.Frames.Count, decoded.Frames.Count);
        AssertEx.AreEqual(src.Width, decoded.Width);
        AssertEx.AreEqual(src.Height, decoded.Height);
        AssertEx.AreEqual(3, decoded.LoopCount);

        // Full-canvas SOURCE/NONE frames decode back to exactly the inputs (PNG is lossless).
        for (int k = 0; k < src.Frames.Count; k++)
        {
            BImageFrame want = src.Frames[k], got = decoded.Frames[k];
            AssertEx.AreEqual(want.DelayNumerator, got.DelayNumerator, $"frame {k} delay num");
            AssertEx.AreEqual(want.DelayDenominator, got.DelayDenominator, $"frame {k} delay den");
            for (int i = 0; i < want.Pixels.Rgba.Length; i++)
                if (want.Pixels.Rgba[i] != got.Pixels.Rgba[i])
                    throw new AssertException($"frame {k} byte {i} differs after round-trip.");
        }
    }

    private static void ApngEncodeDefaultImage()
    {
        var src = MakeSequence(4, 4, loop: 0, (1, 10), (1, 10));
        byte[] apng = MediaImageBridge.EncodeAnimation(src);

        // A plain (non-animated) decode must return frame 0 from IDAT.
        var still = MediaImageBridge.Decode(apng);
        for (int i = 0; i < still.Rgba.Length; i++)
            AssertEx.AreEqual(src.Frames[0].Pixels.Rgba[i], still.Rgba[i]);
    }

    private static void ApngEncodeRejectsBadSize()
    {
        var frames = new List<BImageFrame>
        {
            new(new BPixelBuffer(4, 4, new byte[4 * 4 * 4]), 1, 10),
            new(new BPixelBuffer(3, 4, new byte[3 * 4 * 4]), 1, 10), // wrong width
        };
        var seq = new BImageSequence(frames, 4, 4, 0);
        AssertEx.Throws<ArgumentException>(() => MediaImageBridge.EncodeAnimation(seq));
    }

    private static void ApngEncodeRejectsNonPng()
    {
        var seq = MakeSequence(2, 2, 1, (1, 10));
        AssertEx.Throws<NotSupportedException>(
            () => MediaImageBridge.EncodeAnimation(seq, ImageEncodeFormat.Jpeg));
    }
}
