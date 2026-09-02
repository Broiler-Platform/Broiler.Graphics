using System;
using System.Collections.Generic;
using Broiler.Media.Image;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Tests for <see cref="BImageResource"/>: the discriminant between an encoded
/// and a decoded payload, the intrinsic size a bounded inspection establishes,
/// and the two places the type deliberately refuses to guess.
/// </summary>
internal static class ImageResourceTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Encoded resource inspects its own intrinsic size", EncodedResourceInspectsItsSize));
        tests.Add(("A stated size beats inspecting the payload", StatedSizeWins));
        tests.Add(("Decoded resource takes its size from the pixels", DecodedResourceUsesBufferSize));
        tests.Add(("A decoded resource never fabricates an encoding", DecodedResourceHasNoEncoding));
        tests.Add(("An encoded resource does not decode on access", EncodedResourceDoesNotDecode));
        tests.Add(("An unreadable header leaves the size unknown", UnreadableHeaderHasNoSize));
        tests.Add(("Encoded bytes are carried through unchanged", EncodedBytesAreUnchanged));
    }

    private static BPixelBuffer MakeGradient(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = ((y * width) + x) * 4;
                rgba[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                rgba[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                rgba[i + 2] = 96;
                rgba[i + 3] = 255;
            }
        }

        return new BPixelBuffer(width, height, rgba);
    }

    private static byte[] Png(int width, int height) =>
        MediaImageBridge.Encode(MakeGradient(width, height), ImageEncodeFormat.Png);

    private static void EncodedResourceInspectsItsSize()
    {
        BImageResource resource = BImageResource.FromEncoded(Png(43, 27), "image/png");

        AssertEx.AreEqual(BImagePayloadKind.Encoded, resource.Kind);
        AssertEx.IsTrue(resource.HasIntrinsicSize, "A PNG's header states its size.");
        AssertEx.AreEqual(43, resource.PixelWidth!.Value);
        AssertEx.AreEqual(27, resource.PixelHeight!.Value);
        AssertEx.AreEqual("image/png", resource.MediaType!);
    }

    private static void StatedSizeWins()
    {
        // A source document that already states the size is more authoritative
        // than the payload: a DOCX drawing and a PDF image dictionary both say
        // how big the picture is, and that is the size the document means even
        // when the encoded bytes disagree.
        BImageResource resource = BImageResource.FromEncoded(Png(43, 27), "image/png", 100, 50);

        AssertEx.AreEqual(100, resource.PixelWidth!.Value);
        AssertEx.AreEqual(50, resource.PixelHeight!.Value);
    }

    private static void DecodedResourceUsesBufferSize()
    {
        BImageResource resource = BImageResource.FromPixels(MakeGradient(19, 11));

        AssertEx.AreEqual(BImagePayloadKind.Decoded, resource.Kind);
        AssertEx.IsTrue(resource.HasIntrinsicSize, "Decoded pixels always know their own size.");
        AssertEx.AreEqual(19, resource.PixelWidth!.Value);
        AssertEx.AreEqual(11, resource.PixelHeight!.Value);
        AssertEx.IsTrue(resource.MediaType is null, "Decoded samples have no media type.");
    }

    private static void DecodedResourceHasNoEncoding()
    {
        // The load-bearing refusal. Re-encoding here would hand back bytes that
        // are not the document's, and a lossy round trip would change the picture
        // on the way. A caller that needs bytes encodes them itself.
        BImageResource resource = BImageResource.FromPixels(MakeGradient(8, 8));

        AssertEx.IsFalse(resource.TryGetEncoded(out _, out _), "A decoded resource has no encoded form.");
        AssertEx.IsTrue(resource.TryGetPixels(out BPixelBuffer? pixels), "A decoded resource yields its pixels.");
        AssertEx.AreEqual(8, pixels!.Width);
    }

    private static void EncodedResourceDoesNotDecode()
    {
        BImageResource resource = BImageResource.FromEncoded(Png(8, 8), "image/png");

        AssertEx.IsFalse(resource.TryGetPixels(out _), "An encoded resource does not decode on access.");
        AssertEx.IsTrue(resource.TryGetEncoded(out _, out string? mediaType), "An encoded resource yields its bytes.");
        AssertEx.AreEqual("image/png", mediaType!);
    }

    private static void UnreadableHeaderHasNoSize()
    {
        // Null is a distinct answer from zero: the resource still carries the
        // bytes, and the caller learns it cannot place them at a natural size.
        BImageResource resource = BImageResource.FromEncoded(
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            "application/octet-stream");

        AssertEx.IsFalse(resource.HasIntrinsicSize, "An unreadable header establishes no size.");
        AssertEx.IsTrue(resource.PixelWidth is null, "Unknown width is null, not zero.");
        AssertEx.IsTrue(resource.PixelHeight is null, "Unknown height is null, not zero.");
        AssertEx.IsTrue(resource.TryGetEncoded(out _, out _), "The bytes are still carried.");
    }

    private static void EncodedBytesAreUnchanged()
    {
        byte[] original = Png(12, 9);
        BImageResource resource = BImageResource.FromEncoded(original, "image/png");

        AssertEx.IsTrue(resource.TryGetEncoded(out ReadOnlyMemory<byte> carried, out _), "Encoded bytes are available.");
        AssertEx.AreEqual(original.Length, carried.Length);

        ReadOnlySpan<byte> span = carried.Span;
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != span[i])
                throw new InvalidOperationException($"Byte {i} changed passing through the resource.");
        }
    }
}
