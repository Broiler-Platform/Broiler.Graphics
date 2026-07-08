using System;
using System.Collections.Generic;
using System.IO;
using Broiler.Media;
using Broiler.Media.Image;
using Broiler.Media.Image.Managed;
using MediaImageBuffer = Broiler.Media.Image.ImageBuffer;
using MediaImageFrame = Broiler.Media.Image.ImageFrame;
using MediaImageSequence = Broiler.Media.Image.ImageSequence;

namespace Broiler.Graphics;

internal static class MediaImageBridge
{
    private static readonly MediaCodecCatalog Catalog = new(ManagedImageCodecs.CreateCodecs());

    public static BPixelBuffer Decode(ReadOnlySpan<byte> data) =>
        ToGraphics(DecodeMedia(data, preserveAnimation: false).FirstFrame);

    public static BImageSequence DecodeAnimation(ReadOnlySpan<byte> data) =>
        ToGraphics(DecodeMedia(data, preserveAnimation: true));

    public static byte[] Encode(BPixelBuffer buffer, ImageEncodeFormat format, int quality = 100)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return EncodeMedia(MediaImageSequence.Static(ToMedia(buffer)), format, quality);
    }

    public static byte[] EncodeAnimation(
        BImageSequence sequence,
        ImageEncodeFormat format = ImageEncodeFormat.Png,
        int quality = 100)
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

    private static MediaImageSequence DecodeMedia(ReadOnlySpan<byte> data, bool preserveAnimation)
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
        return codec.DecodeAsync(
                decodeInput,
                new ImageDecodeOptions(preserveAnimation: preserveAnimation))
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    private static byte[] EncodeMedia(MediaImageSequence sequence, ImageEncodeFormat format, int quality)
    {
        ImageCodec codec = CodecFor(format);
        using var output = new MemoryStream();
        codec.EncodeAsync(sequence, output, new ImageEncodeOptions(format, quality))
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return output.ToArray();
    }

    private static ImageCodec CodecFor(ImageEncodeFormat format) => format switch
    {
        ImageEncodeFormat.Png => (ImageCodec)Catalog.FindById(PngImageCodec.CodecDescriptor.Id)!,
        ImageEncodeFormat.Jpeg => (ImageCodec)Catalog.FindById(JpegImageCodec.CodecDescriptor.Id)!,
        ImageEncodeFormat.Bmp => (ImageCodec)Catalog.FindById(BmpImageCodec.CodecDescriptor.Id)!,
        ImageEncodeFormat.Gif => (ImageCodec)Catalog.FindById(GifImageCodec.CodecDescriptor.Id)!,
        ImageEncodeFormat.WebP => (ImageCodec)Catalog.FindById(WebpImageCodec.CodecDescriptor.Id)!,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown image encode format."),
    };

    private static MediaImageBuffer ToMedia(BPixelBuffer buffer) =>
        new(buffer.Width, buffer.Height, (byte[])buffer.Rgba.Clone());

    private static BPixelBuffer ToGraphics(MediaImageBuffer buffer) =>
        new(buffer.Width, buffer.Height, (byte[])buffer.Rgba.Clone());

    private static MediaImageSequence ToMedia(BImageSequence sequence)
    {
        var frames = new List<MediaImageFrame>(sequence.Frames.Count);
        foreach (BImageFrame frame in sequence.Frames)
            frames.Add(new MediaImageFrame(ToMedia(frame.Pixels), frame.DelayNumerator, frame.DelayDenominator));

        return new MediaImageSequence(frames, sequence.Width, sequence.Height, sequence.LoopCount);
    }

    private static BImageSequence ToGraphics(MediaImageSequence sequence)
    {
        var frames = new List<BImageFrame>(sequence.Frames.Count);
        foreach (MediaImageFrame frame in sequence.Frames)
            frames.Add(new BImageFrame(ToGraphics(frame.Pixels), frame.DelayNumerator, frame.DelayDenominator));

        return new BImageSequence(frames, sequence.Width, sequence.Height, sequence.LoopCount);
    }
}
