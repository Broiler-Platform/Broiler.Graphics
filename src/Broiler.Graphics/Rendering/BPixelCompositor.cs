using Broiler.Graphics.Color;
using Broiler.Graphics.Imaging;
using System;
using BlendMode = Broiler.Graphics.Rendering.BCanvas.BlendMode;

namespace Broiler.Graphics.Rendering;

/// <summary>Straight-alpha pixel compositing used by the raster canvas.</summary>
internal static class BPixelCompositor
{
    internal static BColor ApplyOpacity(BColor color, float opacity)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);
        byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
        return new BColor(color.R, color.G, color.B, alpha);
    }

    internal static void BlendPixel(BBitmap bitmap, int x, int y, BColor source, BlendMode blendMode)
    {
        if (source.A == 0)
            return;

        BColor destination = bitmap.GetPixel(x, y);
        BColor blendedSource = ApplyBlendMode(source, destination, blendMode);
        bitmap.WritePixelUnchecked(x, y, CompositeSourceOver(blendedSource, destination));
    }

    private static BColor ApplyBlendMode(BColor source, BColor destination, BlendMode blendMode)
    {
        return blendMode switch
        {
            BlendMode.multiply => new BColor(
                                        (byte)((source.R * destination.R + 127) / 255),
                                        (byte)((source.G * destination.G + 127) / 255),
                                        (byte)((source.B * destination.B + 127) / 255),
                                        source.A),
            BlendMode.screen => new BColor(
                                        (byte)(255 - ((255 - source.R) * (255 - destination.R) + 127) / 255),
                                        (byte)(255 - ((255 - source.G) * (255 - destination.G) + 127) / 255),
                                        (byte)(255 - ((255 - source.B) * (255 - destination.B) + 127) / 255),
                                        source.A),
            BlendMode.darken => new BColor(Math.Min(source.R, destination.R), Math.Min(source.G, destination.G), Math.Min(source.B, destination.B), source.A),
            BlendMode.lighten => new BColor(Math.Max(source.R, destination.R), Math.Max(source.G, destination.G), Math.Max(source.B, destination.B), source.A),
            BlendMode.overlay => new BColor(OverlayChannel(source.R, destination.R), OverlayChannel(source.G, destination.G), OverlayChannel(source.B, destination.B), source.A),
            BlendMode.difference => new BColor((byte)Math.Abs(source.R - destination.R), (byte)Math.Abs(source.G - destination.G), (byte)Math.Abs(source.B - destination.B), source.A),
            BlendMode.plus_lighter => new BColor(AdditiveClampChannel(source.R, destination.R), AdditiveClampChannel(source.G, destination.G), AdditiveClampChannel(source.B, destination.B), source.A),
            _ => source,
        };
    }

    private static BColor CompositeSourceOver(BColor source, BColor destination)
    {
        float srcA = source.A / 255f;
        float dstA = destination.A / 255f;
        float outA = srcA + (dstA * (1f - srcA));

        if (outA <= 0f)
            return BColor.Transparent;

        byte r = CompositeChannel(source.R, destination.R, srcA, dstA, outA);
        byte g = CompositeChannel(source.G, destination.G, srcA, dstA, outA);
        byte b = CompositeChannel(source.B, destination.B, srcA, dstA, outA);
        byte a = (byte)Math.Clamp((int)Math.Round(outA * 255f), 0, 255);

        return new BColor(r, g, b, a);
    }

    private static byte CompositeChannel(byte source, byte destination, float srcA, float dstA, float outA)
    {
        float value = ((source * srcA) + (destination * dstA * (1f - srcA))) / outA;
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private static byte OverlayChannel(byte source, byte destination)
    {
        if (destination < 128)
            return (byte)Math.Clamp((2 * source * destination + 127) / 255, 0, 255);

        return (byte)Math.Clamp(255 - ((2 * (255 - source) * (255 - destination) + 127) / 255), 0, 255);
    }

    private static byte AdditiveClampChannel(byte source, byte destination) =>
        (byte)Math.Min(255, source + destination);
}
