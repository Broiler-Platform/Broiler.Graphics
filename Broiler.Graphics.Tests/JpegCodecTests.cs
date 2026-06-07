using System;
using System.Collections.Generic;
using System.IO;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Tests for the baseline JPEG codec. Component-level checks (DCT, Huffman,
/// magnitude coding) validate the building blocks independently of the full
/// pipeline; round-trip checks bound the reconstruction error of the lossy chain.
/// </summary>
internal static class JpegCodecTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("DCT forward+inverse restores a block", DctRoundTrips));
        tests.Add(("DCT of a flat block is DC-only", DctFlatIsDcOnly));
        tests.Add(("Huffman builds canonical DC luma codes", HuffmanCanonicalCodes));
        tests.Add(("Magnitude coding round-trips signed values", MagnitudeRoundTrips));
        tests.Add(("JPEG round-trips a gradient within tolerance", JpegGradientWithinTolerance));
        tests.Add(("JPEG reproduces a flat colour nearly exactly", JpegFlatColor));
        tests.Add(("JPEG preserves odd dimensions", JpegOddDimensions));
        tests.Add(("JPEG handles a 1x1 image", JpegSinglePixel));
        tests.Add(("JPEG grayscale source round-trips", JpegGrayscaleSource));
        tests.Add(("JPEG restart markers decode identically", JpegRestartMarkers));
        tests.Add(("Codec detects JPEG on decode", JpegAutoDetected));
        tests.Add(("Higher JPEG quality lowers error", JpegQualityMonotonic));
        tests.Add(("Optimal Huffman builds a decodable prefix code", OptimalHuffmanRoundTrips));
        tests.Add(("Optimal Huffman favours frequent symbols", OptimalHuffmanFavoursFrequent));
        tests.Add(("Optimal Huffman handles a single symbol", OptimalHuffmanSingleSymbol));
        tests.Add(("Optimized JPEG is smaller and lossless-equal", OptimizedJpegSmaller));
    }

    private static void OptimizedJpegSmaller()
    {
        var src = Gradient(96, 96);
        byte[] standard = JpegEncoder.Encode(src, quality: 85, restartInterval: 0, optimize: false);
        byte[] optimized = JpegEncoder.Encode(src, quality: 85, restartInterval: 0, optimize: true);

        AssertEx.IsTrue(optimized.Length < standard.Length,
            $"Optimized JPEG ({optimized.Length} B) should be smaller than standard ({standard.Length} B).");

        // Same coefficients either way → identical decoded pixels.
        var a = ManagedImageCodec.Instance.Decode(standard);
        var b = ManagedImageCodec.Instance.Decode(optimized);
        for (int i = 0; i < a.Rgba.Length; i++)
            if (a.Rgba[i] != b.Rgba[i])
                throw new AssertException($"Optimized vs standard decode differ at byte {i}.");
    }

    // ---- Optimal Huffman tests ------------------------------------------

    private static void OptimalHuffmanRoundTrips()
    {
        var rng = new Random(99);
        var freq = new int[256];
        var used = new List<int>();
        for (int s = 0; s < 256; s++)
        {
            // A sparse, skewed distribution like real AC symbol histograms.
            freq[s] = s % 3 == 0 ? rng.Next(0, 500) : rng.Next(0, 5);
            if (freq[s] > 0)
                used.Add(s);
        }

        (byte[] bits, byte[] values) = JpegOptimalHuffman.Generate(freq);

        int total = 0;
        for (int l = 0; l < 16; l++)
            total += bits[l];
        AssertEx.AreEqual(values.Length, total, "BITS sum must equal the number of symbols.");
        AssertEx.IsTrue(values.Length >= used.Count, "Every used symbol must receive a code.");

        var table = JpegHuffmanTable.Build(bits, values);

        // Encode the whole symbol list then decode it back: proves a valid prefix code.
        using var ms = new MemoryStream();
        var writer = new JpegBitWriter(ms);
        foreach (byte sym in values)
            writer.WriteSymbol(table, sym);
        writer.FlushToByte();

        byte[] bytes = ms.ToArray();
        var reader = new JpegBitReader(bytes, 0, bytes.Length);
        foreach (byte sym in values)
            AssertEx.AreEqual(sym, table.Decode(reader), "Optimal Huffman symbol did not round-trip.");
    }

    private static void OptimalHuffmanFavoursFrequent()
    {
        var freq = new int[256];
        freq[10] = 100000; // very common
        freq[20] = 10;
        freq[200] = 1;     // very rare
        (byte[] bits, byte[] values) = JpegOptimalHuffman.Generate(freq);
        var table = JpegHuffmanTable.Build(bits, values);

        AssertEx.IsTrue(table.SizeOf(10) <= table.SizeOf(20), "Frequent symbol should be no longer than a rarer one.");
        AssertEx.IsTrue(table.SizeOf(20) <= table.SizeOf(200), "Rarer symbol should get a longer code.");
        for (int l = 0; l < 16; l++)
            AssertEx.IsTrue(bits[l] >= 0);
    }

    private static void OptimalHuffmanSingleSymbol()
    {
        var freq = new int[256];
        freq[7] = 42;
        (byte[] bits, byte[] values) = JpegOptimalHuffman.Generate(freq);
        AssertEx.AreEqual(1, values.Length, "Only one symbol should be coded.");
        AssertEx.AreEqual((byte)7, values[0]);

        var table = JpegHuffmanTable.Build(bits, values);
        AssertEx.IsTrue(table.SizeOf(7) >= 1, "The lone symbol still needs a non-empty code.");

        using var ms = new MemoryStream();
        var w = new JpegBitWriter(ms);
        w.WriteSymbol(table, 7);
        w.WriteSymbol(table, 7);
        w.FlushToByte();
        byte[] bytes = ms.ToArray();
        var reader = new JpegBitReader(bytes, 0, bytes.Length);
        AssertEx.AreEqual(7, table.Decode(reader));
        AssertEx.AreEqual(7, table.Decode(reader));
    }

    // ---- Component tests -------------------------------------------------

    private static void DctRoundTrips()
    {
        var rng = new Random(1234);
        var block = new double[64];
        var original = new double[64];
        for (int i = 0; i < 64; i++)
            block[i] = original[i] = rng.Next(-128, 128);

        JpegDct.Forward(block);
        JpegDct.Inverse(block);

        for (int i = 0; i < 64; i++)
            if (Math.Abs(block[i] - original[i]) > 1e-6)
                throw new AssertException($"DCT round-trip drifted at {i}: {block[i]} vs {original[i]}.");
    }

    private static void DctFlatIsDcOnly()
    {
        var block = new double[64];
        for (int i = 0; i < 64; i++)
            block[i] = 50.0; // constant (already level-shifted)

        JpegDct.Forward(block);

        // DC term of a constant block: 8 * value for the orthonormal basis.
        AssertEx.IsTrue(Math.Abs(block[0] - 8 * 50.0) < 1e-6, $"Unexpected DC term {block[0]}.");
        for (int i = 1; i < 64; i++)
            AssertEx.IsTrue(Math.Abs(block[i]) < 1e-6, $"AC coefficient {i} should be ~0 but was {block[i]}.");
    }

    private static void HuffmanCanonicalCodes()
    {
        var table = JpegHuffmanTable.Build(JpegTables.DcLuminanceBits, JpegTables.DcLuminanceValues);

        // BITS = {0,1,5,...}: symbol 0 is the only length-2 code (00); symbols 1..5
        // are the length-3 codes 010,011,100,101,110.
        AssertEx.AreEqual(2, table.SizeOf(0));
        AssertEx.AreEqual(0b00, table.CodeOf(0));
        AssertEx.AreEqual(3, table.SizeOf(1));
        AssertEx.AreEqual(0b010, table.CodeOf(1));
        AssertEx.AreEqual(0b011, table.CodeOf(2));
        AssertEx.AreEqual(0b100, table.CodeOf(3));
        AssertEx.AreEqual(0b110, table.CodeOf(5));
    }

    private static void MagnitudeRoundTrips()
    {
        for (int v = -2048; v <= 2048; v++)
        {
            int size = JpegTables.Magnitude(v);
            int pattern = JpegTables.ToBitPattern(v, size);
            int restored = JpegTables.Extend(pattern, size);
            if (restored != v)
                throw new AssertException($"Magnitude coding failed for {v}: got {restored} (size {size}).");
        }
    }

    // ---- Pipeline helpers ------------------------------------------------

    private static BPixelBuffer Gradient(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        int i = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            rgba[i++] = (byte)(x * 255 / Math.Max(1, width - 1));
            rgba[i++] = (byte)(y * 255 / Math.Max(1, height - 1));
            rgba[i++] = (byte)((x + y) * 255 / Math.Max(1, width + height - 2));
            rgba[i++] = 255;
        }
        return new BPixelBuffer(width, height, rgba);
    }

    /// <summary>Mean absolute error over the RGB channels (alpha is forced opaque by JPEG).</summary>
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

    // ---- Pipeline tests --------------------------------------------------

    private static void JpegGradientWithinTolerance()
    {
        var src = Gradient(64, 48);
        byte[] jpeg = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Jpeg, quality: 90);
        var decoded = ManagedImageCodec.Instance.Decode(jpeg);

        AssertEx.AreEqual(src.Width, decoded.Width);
        AssertEx.AreEqual(src.Height, decoded.Height);
        double mae = MeanAbsErrorRgb(src, decoded);
        AssertEx.IsTrue(mae < 6.0, $"JPEG gradient MAE too high: {mae:F2}");
    }

    private static void JpegFlatColor()
    {
        int w = 20, h = 20;
        byte[] rgba = new byte[w * h * 4];
        for (int p = 0; p < w * h; p++)
        {
            int o = p * 4;
            rgba[o] = 120; rgba[o + 1] = 200; rgba[o + 2] = 60; rgba[o + 3] = 255;
        }
        var src = new BPixelBuffer(w, h, rgba);
        byte[] jpeg = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Jpeg, quality: 95);
        var decoded = ManagedImageCodec.Instance.Decode(jpeg);

        for (int p = 0; p < w * h; p++)
        {
            int o = p * 4;
            AssertEx.IsTrue(Math.Abs(decoded.Rgba[o] - 120) <= 3, "R drifted on flat colour");
            AssertEx.IsTrue(Math.Abs(decoded.Rgba[o + 1] - 200) <= 3, "G drifted on flat colour");
            AssertEx.IsTrue(Math.Abs(decoded.Rgba[o + 2] - 60) <= 3, "B drifted on flat colour");
        }
    }

    private static void JpegOddDimensions()
    {
        var src = Gradient(17, 13); // not a multiple of the 16x16 MCU
        byte[] jpeg = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Jpeg, quality: 90);
        var decoded = ManagedImageCodec.Instance.Decode(jpeg);

        AssertEx.AreEqual(17, decoded.Width);
        AssertEx.AreEqual(13, decoded.Height);
        AssertEx.IsTrue(MeanAbsErrorRgb(src, decoded) < 8.0, "Odd-size JPEG error too high");
    }

    private static void JpegSinglePixel()
    {
        var src = new BPixelBuffer(1, 1, [200, 100, 50, 255]);
        byte[] jpeg = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Jpeg, quality: 92);
        var decoded = ManagedImageCodec.Instance.Decode(jpeg);

        AssertEx.AreEqual(1, decoded.Width);
        AssertEx.AreEqual(1, decoded.Height);
        AssertEx.IsTrue(Math.Abs(decoded.Rgba[0] - 200) <= 6, "1x1 R drifted");
        AssertEx.IsTrue(Math.Abs(decoded.Rgba[1] - 100) <= 6, "1x1 G drifted");
        AssertEx.IsTrue(Math.Abs(decoded.Rgba[2] - 50) <= 6, "1x1 B drifted");
    }

    private static void JpegGrayscaleSource()
    {
        int w = 32, h = 32;
        byte[] rgba = new byte[w * h * 4];
        int i = 0;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            byte g = (byte)(x * 255 / (w - 1));
            rgba[i++] = g; rgba[i++] = g; rgba[i++] = g; rgba[i++] = 255;
        }
        var src = new BPixelBuffer(w, h, rgba);
        byte[] jpeg = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Jpeg, quality: 90);
        var decoded = ManagedImageCodec.Instance.Decode(jpeg);
        AssertEx.IsTrue(MeanAbsErrorRgb(src, decoded) < 5.0, "Grayscale JPEG error too high");
    }

    private static void JpegRestartMarkers()
    {
        var src = Gradient(48, 32);
        // Restart markers reset DC prediction but must reconstruct identical pixels.
        byte[] plain = JpegEncoder.Encode(src, quality: 90, restartInterval: 0);
        byte[] restart = JpegEncoder.Encode(src, quality: 90, restartInterval: 2);

        var a = ManagedImageCodec.Instance.Decode(plain);
        var b = ManagedImageCodec.Instance.Decode(restart);

        AssertEx.AreEqual(a.Rgba.Length, b.Rgba.Length);
        for (int p = 0; p < a.Rgba.Length; p++)
            if (a.Rgba[p] != b.Rgba[p])
                throw new AssertException($"Restart-marker decode differs at byte {p}.");
    }

    private static void JpegAutoDetected()
    {
        var src = Gradient(16, 16);
        byte[] jpeg = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Jpeg, quality: 85);
        AssertEx.IsTrue(JpegDecoder.IsJpeg(jpeg), "Encoder output not detected as JPEG");
        var decoded = ManagedImageCodec.Instance.Decode(jpeg); // dispatches by signature
        AssertEx.AreEqual(16, decoded.Width);
    }

    private static void JpegQualityMonotonic()
    {
        var src = Gradient(64, 64);
        var low = ManagedImageCodec.Instance.Decode(
            ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Jpeg, quality: 40));
        var high = ManagedImageCodec.Instance.Decode(
            ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Jpeg, quality: 95));

        double lowErr = MeanAbsErrorRgb(src, low);
        double highErr = MeanAbsErrorRgb(src, high);
        AssertEx.IsTrue(highErr < lowErr, $"Quality 95 ({highErr:F2}) should beat quality 40 ({lowErr:F2}).");
    }
}
