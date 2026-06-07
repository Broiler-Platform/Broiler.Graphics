using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Progressive JPEG (SOF2) decode tests. The frozen fixture is a 96x64 gradient
/// encoded by <see cref="ProgressiveJpegWriter"/> with spectral selection,
/// successive approximation, optimal Huffman tables, and cross-block EOB runs —
/// so it exercises the decoder's EOBn paths. It was independently confirmed to be
/// a valid progressive JPEG by System.Drawing (which decoded it to the same
/// gradient at MAE ~1.1). Here we assert the Broiler decoder reconstructs it too.
/// </summary>
internal static class JpegProgressiveTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Decodes a frozen progressive JPEG fixture", DecodesProgressive));
        tests.Add(("Progressive fixture is detected as JPEG", DetectedAsJpeg));
        tests.Add(("Progressive round-trips a gradient", ProgressiveRoundTrip));
        tests.Add(("Progressive round-trips odd dimensions", ProgressiveOddDimensions));
        tests.Add(("Progressive and baseline decode agree", ProgressiveMatchesBaseline));
    }

    private static BPixelBuffer Gradient(int w, int h)
    {
        byte[] rgba = new byte[w * h * 4];
        int i = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            rgba[i++] = (byte)(x * 255 / Math.Max(1, w - 1));
            rgba[i++] = (byte)(y * 255 / Math.Max(1, h - 1));
            rgba[i++] = (byte)((x + y) * 255 / Math.Max(1, w + h - 2));
            rgba[i++] = 255;
        }
        return new BPixelBuffer(w, h, rgba);
    }

    private static double MeanAbsErrorRgb(BPixelBuffer a, BPixelBuffer b)
    {
        long sum = 0;
        int n = a.Width * a.Height;
        for (int p = 0; p < n; p++)
        {
            int o = p * 4;
            sum += Math.Abs(a.Rgba[o] - b.Rgba[o]);
            sum += Math.Abs(a.Rgba[o + 1] - b.Rgba[o + 1]);
            sum += Math.Abs(a.Rgba[o + 2] - b.Rgba[o + 2]);
        }
        return sum / (double)(n * 3);
    }

    // 96x64 progressive JPEG of the analytic gradient (see class summary).
    private const string ProgressiveGradientBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wCEAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDIBCQkJDAsMGA0NGDIhHCEyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMv/CABEIAEAAYAMBIgACEQEDEQH/xABXAAEBAQAAAAAAAAAAAAAAAAADBgIQAQADAAAAAAAAAAAAAAAAAAABA2EBAQEBAQEAAAAAAAAAAAAAAAIEBQYBEQEBAQEBAAAAAAAAAAAAAAAAAgERQP/aAAwDAQACEQMRAAABhUZPCKMjoFGRUgjo6QR9qgUZHTPIyZ3DijI6BRkVIoyOkUZFQKMjpnkZM/hx26KgEdHSCOipBGR0CjIqZ5GTP4cUZFQKMjpFGRUijI6BRkVP/9oADAMBAAIRAxEAABAIELDfz704w8ABPDL37y8wIEL/2gAIAQEAAT8BitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRWitFaK0VorRW/9oACAECEQE/Aeuuuuuuuuuuuuuuuuuuuuuuuv/aAAgBAxEBPwGbTabTabTabTabTabTabTabTabTabTabTabTb/2gAIAQEAAT8QyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJkyZMmTJk/9oACAECEQE/EMMMMMMMMMMMMMMMMMMMMMMMMP/aAAgBAxEBPxD0f/8A/wD/AP8A/wD/AP/Z";

    private static void DecodesProgressive()
    {
        byte[] jpeg = Convert.FromBase64String(ProgressiveGradientBase64);
        var img = ManagedImageCodec.Instance.Decode(jpeg);

        AssertEx.AreEqual(96, img.Width);
        AssertEx.AreEqual(64, img.Height);

        long sum = 0;
        int w = 96, h = 64;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int er = x * 255 / (w - 1);
            int eg = y * 255 / (h - 1);
            int eb = (x + y) * 255 / (w + h - 2);
            int o = (y * w + x) * 4;
            sum += Math.Abs(img.Rgba[o] - er) + Math.Abs(img.Rgba[o + 1] - eg) + Math.Abs(img.Rgba[o + 2] - eb);
        }
        double mae = sum / (double)(w * h * 3);
        AssertEx.IsTrue(mae < 4.0, $"Progressive decode MAE too high: {mae:F2}");
    }

    private static void DetectedAsJpeg()
    {
        byte[] jpeg = Convert.FromBase64String(ProgressiveGradientBase64);
        AssertEx.IsTrue(JpegDecoder.IsJpeg(jpeg), "Progressive fixture not detected as JPEG");
    }

    private static void ProgressiveRoundTrip()
    {
        var src = Gradient(80, 48);
        byte[] jpeg = ProgressiveJpegWriter.Encode(src, quality: 85);
        var decoded = ManagedImageCodec.Instance.Decode(jpeg);
        AssertEx.AreEqual(80, decoded.Width);
        AssertEx.AreEqual(48, decoded.Height);
        AssertEx.IsTrue(MeanAbsErrorRgb(src, decoded) < 6.0, "Progressive round-trip error too high");
    }

    private static void ProgressiveOddDimensions()
    {
        var src = Gradient(23, 19); // partial MCUs in both axes
        byte[] jpeg = ProgressiveJpegWriter.Encode(src, quality: 85);
        var decoded = ManagedImageCodec.Instance.Decode(jpeg);
        AssertEx.AreEqual(23, decoded.Width);
        AssertEx.AreEqual(19, decoded.Height);
        AssertEx.IsTrue(MeanAbsErrorRgb(src, decoded) < 8.0, "Progressive odd-size error too high");
    }

    /// <summary>
    /// The progressive and baseline encoders quantize identically (same tables,
    /// same 4:2:0), so decoding each should land within a couple of levels — the
    /// only differences are IDCT/point-transform rounding.
    /// </summary>
    private static void ProgressiveMatchesBaseline()
    {
        var src = Gradient(64, 64);
        var prog = ManagedImageCodec.Instance.Decode(ProgressiveJpegWriter.Encode(src, quality: 90));
        var baseline = ManagedImageCodec.Instance.Decode(
            ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Jpeg, quality: 90));
        AssertEx.IsTrue(MeanAbsErrorRgb(prog, baseline) < 3.0,
            "Progressive and baseline decodes diverge more than expected");
    }
}


