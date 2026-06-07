using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Tests for the dependency-free <see cref="ManagedImageCodec"/>: PNG/BMP
/// round-trips, format detection, and CRC integrity.
/// </summary>
internal static class ImageCodecTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("PNG round-trips RGBA pixels exactly", PngRoundTrips));
        tests.Add(("BMP round-trips RGBA pixels exactly", BmpRoundTrips));
        tests.Add(("PNG decodes a single pixel", PngSinglePixel));
        tests.Add(("Decoded PNG carries a valid CRC", PngHasValidCrc));
        tests.Add(("Codec detects PNG vs BMP on decode", FormatAutoDetect));
        tests.Add(("Decode rejects unknown signatures", UnknownSignatureRejected));
        tests.Add(("UseManaged registers the managed codec", UseManagedRegisters));
        tests.Add(("UseManagedIfUnset guards an explicit codec", CodecRegistrationGuard));
        tests.Add(("Crc32 matches the known IEND vector", Crc32KnownVector));
        tests.Add(("Decodes 8-bit grayscale PNG", PngDecodesGrayscale8));
        tests.Add(("Decodes 1-bit grayscale PNG", PngDecodesGrayscale1));
        tests.Add(("Decodes 8-bit palette PNG with tRNS", PngDecodesPaletteWithTrns));
        tests.Add(("Decodes grayscale+alpha PNG", PngDecodesGrayAlpha));
        tests.Add(("Decodes RGB PNG with colour-key tRNS", PngDecodesRgbColorKey));
        tests.Add(("Decodes 16-bit RGB PNG", PngDecodes16BitRgb));
        tests.Add(("Decodes an Adam7-interlaced PNG", PngDecodesAdam7));
        tests.Add(("Adam7 decode handles tiny images", PngDecodesAdam7Tiny));
        tests.Add(("Decodes a frozen Adam7 PNG fixture", PngDecodesFrozenAdam7));
    }

    /// <summary>Builds a deterministic test image with a varied alpha channel.</summary>
    private static BPixelBuffer MakeGradient(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        int i = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                rgba[i++] = (byte)(x * 7 + 1);
                rgba[i++] = (byte)(y * 11 + 2);
                rgba[i++] = (byte)((x ^ y) * 13 + 3);
                rgba[i++] = (byte)(255 - ((x + y) & 0xFF));
            }
        }
        return new BPixelBuffer(width, height, rgba);
    }

    private static void AssertPixelsEqual(BPixelBuffer expected, BPixelBuffer actual)
    {
        AssertEx.AreEqual(expected.Width, actual.Width, "width mismatch");
        AssertEx.AreEqual(expected.Height, actual.Height, "height mismatch");
        AssertEx.AreEqual(expected.Rgba.Length, actual.Rgba.Length, "buffer length mismatch");
        for (int i = 0; i < expected.Rgba.Length; i++)
        {
            if (expected.Rgba[i] != actual.Rgba[i])
                throw new AssertException(
                    $"Pixel byte {i} differs: expected {expected.Rgba[i]}, got {actual.Rgba[i]}.");
        }
    }

    private static void PngRoundTrips()
    {
        var src = MakeGradient(37, 19);
        byte[] encoded = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Png);
        var decoded = ManagedImageCodec.Instance.Decode(encoded);
        AssertPixelsEqual(src, decoded);
    }

    private static void BmpRoundTrips()
    {
        var src = MakeGradient(40, 24);
        byte[] encoded = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Bmp);
        var decoded = ManagedImageCodec.Instance.Decode(encoded);
        AssertPixelsEqual(src, decoded);
    }

    private static void PngSinglePixel()
    {
        var src = new BPixelBuffer(1, 1, [10, 20, 30, 40]);
        byte[] encoded = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Png);
        var decoded = ManagedImageCodec.Instance.Decode(encoded);
        AssertPixelsEqual(src, decoded);
    }

    private static void PngHasValidCrc()
    {
        // A corrupted byte in the IDAT/CRC region must be rejected.
        var src = MakeGradient(8, 8);
        byte[] encoded = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Png);
        encoded[encoded.Length - 6] ^= 0xFF; // flip a byte inside the IEND chunk's data/crc area
        AssertEx.Throws<FormatException>(() => ManagedImageCodec.Instance.Decode(encoded));
    }

    private static void FormatAutoDetect()
    {
        var src = MakeGradient(5, 5);
        byte[] png = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Png);
        byte[] bmp = ManagedImageCodec.Instance.Encode(src, BImageEncodeFormat.Bmp);

        AssertPixelsEqual(src, ManagedImageCodec.Instance.Decode(png));
        AssertPixelsEqual(src, ManagedImageCodec.Instance.Decode(bmp));
    }

    private static void UnknownSignatureRejected()
    {
        byte[] junk = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];
        AssertEx.Throws<NotSupportedException>(() => ManagedImageCodec.Instance.Decode(junk));
    }

    private static void UseManagedRegisters()
    {
        BImageCodec.UseManaged();
        AssertEx.IsInstanceOf<ManagedImageCodec>(BImageCodec.Current);

        var src = MakeGradient(6, 6);
        byte[] encoded = BImageCodec.Encode(src, BImageEncodeFormat.Png);
        AssertPixelsEqual(src, BImageCodec.Decode(encoded));
    }

    private sealed class DummyCodec : IBImageCodec
    {
        public BPixelBuffer Decode(ReadOnlySpan<byte> data) => throw new NotImplementedException();
        public byte[] Encode(BPixelBuffer buffer, BImageEncodeFormat format, int quality = 100) =>
            throw new NotImplementedException();
    }

    private static void CodecRegistrationGuard()
    {
        BImageCodec.ResetToDefault();
        AssertEx.IsFalse(BImageCodec.IsRegistered, "default stub is not a registered codec");

        AssertEx.IsTrue(BImageCodec.UseManagedIfUnset(), "installs the managed codec when unset");
        AssertEx.IsTrue(BImageCodec.IsRegistered);
        AssertEx.IsInstanceOf<ManagedImageCodec>(BImageCodec.Current);

        var dummy = new DummyCodec();
        BImageCodec.Register(dummy);
        AssertEx.IsFalse(BImageCodec.UseManagedIfUnset(), "must not override an explicitly registered codec");
        AssertEx.IsTrue(ReferenceEquals(dummy, BImageCodec.Current), "the explicit codec is preserved");

        BImageCodec.UseManaged(); // leave a usable codec installed for any later test
    }

    private static void Crc32KnownVector()
    {
        // CRC-32 of the bytes "IEND" is the well-known 0xAE426082.
        byte[] iend = [(byte)'I', (byte)'E', (byte)'N', (byte)'D'];
        AssertEx.AreEqual(0xAE426082u, Crc32Probe(iend));
    }

    // Crc32 is internal to Broiler.Graphics; the test assembly sees it via InternalsVisibleTo.
    private static uint Crc32Probe(byte[] data) => Crc32.Compute(data);

    private static void AssertRgba(BPixelBuffer buf, int x, int y, byte r, byte g, byte b, byte a)
    {
        int i = (y * buf.Width + x) * 4;
        if (buf.Rgba[i] != r || buf.Rgba[i + 1] != g || buf.Rgba[i + 2] != b || buf.Rgba[i + 3] != a)
            throw new AssertException(
                $"Pixel ({x},{y}) expected [{r},{g},{b},{a}] but was " +
                $"[{buf.Rgba[i]},{buf.Rgba[i + 1]},{buf.Rgba[i + 2]},{buf.Rgba[i + 3]}].");
    }

    private static void PngDecodesGrayscale8()
    {
        // 2x2 grayscale: 0, 128, 255, 64.
        byte[][] rows = [[0, 128], [255, 64]];
        byte[] png = PngFormatBuilder.Build(2, 2, 8, 0, rows);
        var img = ManagedImageCodec.Instance.Decode(png);
        AssertRgba(img, 0, 0, 0, 0, 0, 255);
        AssertRgba(img, 1, 0, 128, 128, 128, 255);
        AssertRgba(img, 0, 1, 255, 255, 255, 255);
        AssertRgba(img, 1, 1, 64, 64, 64, 255);
    }

    private static void PngDecodesGrayscale1()
    {
        // 8x1 1-bit row: bits 1010_0001 -> packed into one byte 0xA1.
        byte[][] rows = [[0xA1]];
        byte[] png = PngFormatBuilder.Build(8, 1, 1, 0, rows);
        var img = ManagedImageCodec.Instance.Decode(png);
        // 1-bit samples scale to 0 or 255.
        byte[] expected = [255, 0, 255, 0, 0, 0, 0, 255];
        for (int x = 0; x < 8; x++)
            AssertRgba(img, x, 0, expected[x], expected[x], expected[x], 255);
    }

    private static void PngDecodesPaletteWithTrns()
    {
        // 3-entry palette; indices 0,1,2 across a 3x1 row.
        byte[] palette = [10, 20, 30, 40, 50, 60, 70, 80, 90];
        byte[] trns = [0, 128]; // index 0 fully transparent, index 1 half, index 2 defaults opaque
        byte[][] rows = [[0, 1, 2]];
        byte[] png = PngFormatBuilder.Build(3, 1, 8, 3, rows, palette, trns);
        var img = ManagedImageCodec.Instance.Decode(png);
        AssertRgba(img, 0, 0, 10, 20, 30, 0);
        AssertRgba(img, 1, 0, 40, 50, 60, 128);
        AssertRgba(img, 2, 0, 70, 80, 90, 255);
    }

    private static void PngDecodesGrayAlpha()
    {
        // 2x1 grayscale+alpha: (gray=100,a=50), (gray=200,a=255).
        byte[][] rows = [[100, 50, 200, 255]];
        byte[] png = PngFormatBuilder.Build(2, 1, 8, 4, rows);
        var img = ManagedImageCodec.Instance.Decode(png);
        AssertRgba(img, 0, 0, 100, 100, 100, 50);
        AssertRgba(img, 1, 0, 200, 200, 200, 255);
    }

    private static void PngDecodesRgbColorKey()
    {
        // RGB 8-bit, tRNS keys out pure magenta (255,0,255).
        byte[] trns = [0, 255, 0, 0, 0, 255]; // 16-bit-per-sample key: R=255,G=0,B=255
        byte[][] rows = [[255, 0, 255, 1, 2, 3]];
        byte[] png = PngFormatBuilder.Build(2, 1, 8, 2, rows, palette: null, trns: trns);
        var img = ManagedImageCodec.Instance.Decode(png);
        AssertRgba(img, 0, 0, 255, 0, 255, 0);   // keyed transparent
        AssertRgba(img, 1, 0, 1, 2, 3, 255);     // opaque
    }

    private static void PngDecodes16BitRgb()
    {
        // 1x1 16-bit RGB: R=0x1234, G=0x5678, B=0x9ABC -> high bytes 0x12,0x56,0x9A.
        byte[][] rows = [[0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC]];
        byte[] png = PngFormatBuilder.Build(1, 1, 16, 2, rows);
        var img = ManagedImageCodec.Instance.Decode(png);
        AssertRgba(img, 0, 0, 0x12, 0x56, 0x9A, 255);
    }

    private static void PngDecodesAdam7()
    {
        // PNG is lossless, so an interlaced encode must de-interlace back exactly.
        var src = MakeGradient(33, 21); // odd size to span partial passes
        byte[] png = PngFormatBuilder.BuildInterlacedRgba(src.Width, src.Height, src.Rgba);
        var decoded = ManagedImageCodec.Instance.Decode(png);
        AssertPixelsEqual(src, decoded);
    }

    private static void PngDecodesAdam7Tiny()
    {
        // 1x1 (only pass 1 exists) and 5x3 (several passes empty) edge cases.
        foreach ((int w, int h) in new[] { (1, 1), (5, 3), (8, 8), (2, 9) })
        {
            var src = MakeGradient(w, h);
            byte[] png = PngFormatBuilder.BuildInterlacedRgba(w, h, src.Rgba);
            AssertPixelsEqual(src, ManagedImageCodec.Instance.Decode(png));
        }
    }

    // 16x16 Adam7-interlaced RGBA PNG, independently confirmed by System.Drawing to
    // decode to the gradient below (R=x*17, G=y*17, B=(x+y)*8, A=255-x*8).
    private const string FrozenAdam7Base64 = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAFo9M/3AAADIUlEQVR4nAXBIYyzPBgA4Fd+8qs7eamaXFIzOVExOdMEiWqQuCJx1IFjVcyx1DDHkgrmONGEc5xpOMenyu+Q/Z8HACBIOA8A8hykzAeg8LlYiBugMl6svDcA9DNQel4kjQdL8wbAxoHafJH2Plj7bgDD343DcdJw7TykJWB63Di9TpqmnadVCVheNy7TScuq8/JZArbpxm01afvsvP0uAfDfgPHnRvFx4fg8SXwdNI47i9PG47wE4MeA+Xmj/LpwHk+Sp4PmeWd51Xh+LwH0NWAdb1SnC9f5JHU1aH3vrH42Xr9LAJ8G7PON+mrh/j5J/xy0f3fWfzfe/5aA4M9O4GNlcJgFnEYFl95A1DpI6gBZAQh/7AQfVoZPs8CXUeGoNzhpHc7qgGUBiB52Qk8ro5dZ0GhUNOkNzVpHZR3orQDETzvhl5XxaBY8GRXPesNl6/itDvxRAJKXnchoZTKZhcxGJWVv5K118lEH+SoA6WgnOlmZzmah5aj0rTf60Tr9qoP+KgDZZCc2W5mVs7C3UdlHb+yrdfarDvanAOSznXi5Mn+bhX+Myr96479a53/q4P8VAOhPQOjvjtHHRtDnStFhYeg4c3SaBDqPEl0Gha69RlFnUNxalDQOpbVHWRlQXgCQj4DI547JYSPkuFJyWhg5z5xcJkGuoyTRoEjca5J0hqStJVnjSF57IstAqgKAHQJixx2z00bYeaXssjB2nTmLJsHiUbJkUCztNcs6w/LWMtk4VtWe3crA7gWAOAUkzjsWl42I60pFtDARz1wkkxDpKEU2KJH3WsjOiKq14tY4ca+9eJRBPAsAdQlIXXesoo2oeKUqWZhKZ66ySah8lEoOSlW9VrfOqHtr1aNx6ll79SqDehcAJgrIxDs2yUZMulKTLczkMzdyEqYapbkNytx7bR6dMc/WmlfjzLv25qsM5rsAcElALt2xyzbi8pU6uTBXzdzdJuHuo3SPQblnr92rM+7dWvfVOPdde/dTBvdbAIQsoJDvOMiNhGql4bawcJ95eEwiPEcZXoMK716Hr86E79aGn8aF39qHf2UI/xX/AxxtOh9LHsBoAAAAAElFTkSuQmCC";

    private static void PngDecodesFrozenAdam7()
    {
        byte[] png = Convert.FromBase64String(FrozenAdam7Base64);
        var img = ManagedImageCodec.Instance.Decode(png);
        AssertEx.AreEqual(16, img.Width);
        AssertEx.AreEqual(16, img.Height);

        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
        {
            int o = (y * 16 + x) * 4;
            AssertEx.AreEqual((byte)(x * 17), img.Rgba[o], $"R at ({x},{y})");
            AssertEx.AreEqual((byte)(y * 17), img.Rgba[o + 1], $"G at ({x},{y})");
            AssertEx.AreEqual((byte)((x + y) * 8), img.Rgba[o + 2], $"B at ({x},{y})");
            AssertEx.AreEqual((byte)(255 - x * 8), img.Rgba[o + 3], $"A at ({x},{y})");
        }
    }
}

