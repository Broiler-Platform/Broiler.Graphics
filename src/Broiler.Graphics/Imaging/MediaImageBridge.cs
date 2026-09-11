using System;
using System.Collections.Generic;
using System.IO;
using Broiler.Media;
using Broiler.Media.Image;

namespace Broiler.Graphics.Imaging;

internal static class MediaImageBridge
{
    // The catalog is supplied by the application composition root via BImageCodecs.Use(...);
    // Graphics no longer constructs or selects concrete codecs itself.
    private static MediaCodecCatalog Catalog => BImageCodecs.Catalog;

    public static BPixelBuffer Decode(ReadOnlySpan<byte> data) => ToGraphics(DecodeMedia(data, preserveAnimation: false).FirstFrame);

    public static BImageSequence DecodeAnimation(ReadOnlySpan<byte> data) => ToGraphics(DecodeMedia(data, preserveAnimation: true));

    public static byte[] Encode(BPixelBuffer buffer, ImageEncodeFormat format, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return EncodeMedia(ImageSequence.Static(ToMedia(buffer)), format, quality);
    }

    public static byte[] EncodeAnimation(BImageSequence sequence, ImageEncodeFormat format = ImageEncodeFormat.Png, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        return format switch
        {
            ImageEncodeFormat.Png or ImageEncodeFormat.Gif or ImageEncodeFormat.WebP =>
                EncodeMedia(ToMedia(sequence), format, quality),

            ImageEncodeFormat.Bmp or ImageEncodeFormat.Jpeg => throw new NotSupportedException(
                $"Animation encoding is only supported as PNG (APNG), GIF, or WebP; requested {format}."),

            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown image encode format."),
        };
    }

    private static ImageSequence DecodeMedia(ReadOnlySpan<byte> data, bool preserveAnimation)
    {
        byte[] bytes = data.ToArray();
        using var probeInput = new MediaInput(new MemoryStream(bytes), leaveOpen: false);
        MediaCodecMatch? match = Catalog.SelectAsync(MediaKind.Image, probeInput).AsTask().GetAwaiter().GetResult();

        if (match?.Codec is not ImageCodec codec)
        {
            throw new NotSupportedException(
                "Unrecognized image data. The media image codec catalog matched no image codec.");
        }

        using var decodeInput = new MediaInput(new MemoryStream(bytes), leaveOpen: false);

        // The synchronous path, rather than blocking on the asynchronous one.
        // This bridge is called from rendering code that has no async context to
        // return to, and GetResult() on a codec's read is what deadlocks where
        // continuations run on a single thread. The two paths decode through the
        // same DecodeCore, so nothing about the image changes with the call.
        return codec.Decode(decodeInput, new ImageDecodeOptions(preserveAnimation: preserveAnimation));
    }

    private static byte[] EncodeMedia(ImageSequence sequence, ImageEncodeFormat format, int quality)
    {
        ImageCodec codec = CodecFor(format);
        using var output = new MemoryStream();

        codec.EncodeAsync(sequence, output, new ImageEncodeOptions(format, quality)).AsTask().GetAwaiter().GetResult();

        return output.ToArray();
    }

    private static ImageCodec CodecFor(ImageEncodeFormat format) =>
        Catalog.FindEncoder(format) ?? throw new NotSupportedException($"The registered image codec catalog has no codec able to encode {format}.");

    private static ImageBuffer ToMedia(BPixelBuffer buffer) =>
        new(buffer.Width, buffer.Height, (byte[])buffer.Rgba.Clone());

    private static BPixelBuffer ToGraphics(ImageBuffer buffer) =>
        new(buffer.Width, buffer.Height, (byte[])buffer.Rgba.Clone());

    private static ImageSequence ToMedia(BImageSequence sequence)
    {
        var frames = new List<ImageFrame>(sequence.Frames.Count);
        foreach (BImageFrame frame in sequence.Frames)
            frames.Add(new ImageFrame(ToMedia(frame.Pixels), frame.DelayNumerator, frame.DelayDenominator));

        return new ImageSequence(frames, sequence.Width, sequence.Height, sequence.LoopCount);
    }

    private static BImageSequence ToGraphics(ImageSequence sequence)
    {
        var frames = new List<BImageFrame>(sequence.Frames.Count);
        foreach (ImageFrame frame in sequence.Frames)
            frames.Add(new BImageFrame(ToGraphics(frame.Pixels), frame.DelayNumerator, frame.DelayDenominator));

        return new BImageSequence(frames, sequence.Width, sequence.Height, sequence.LoopCount);
    }
}
