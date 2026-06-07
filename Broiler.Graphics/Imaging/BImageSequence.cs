using System;
using System.Collections.Generic;

namespace Broiler.Graphics;

/// <summary>
/// A decoded image as a sequence of one or more frames. A still image is a
/// single-frame sequence; an animated image (e.g. APNG) carries every frame
/// already composited onto the canvas, with per-frame delays.
/// </summary>
public sealed class BImageSequence
{
    public BImageSequence(IReadOnlyList<BImageFrame> frames, int width, int height, int loopCount)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
            throw new ArgumentException("An image sequence needs at least one frame.", nameof(frames));
        if (loopCount < 0)
            throw new ArgumentOutOfRangeException(nameof(loopCount));

        Frames = frames;
        Width = width;
        Height = height;
        LoopCount = loopCount;
    }

    /// <summary>The frames, in display order.</summary>
    public IReadOnlyList<BImageFrame> Frames { get; }

    /// <summary>Canvas width in pixels.</summary>
    public int Width { get; }

    /// <summary>Canvas height in pixels.</summary>
    public int Height { get; }

    /// <summary>Number of times the animation plays; 0 means loop forever.</summary>
    public int LoopCount { get; }

    /// <summary>True when there is more than one frame.</summary>
    public bool IsAnimated => Frames.Count > 1;

    /// <summary>Convenience accessor for the first (or only) frame's pixels.</summary>
    public BPixelBuffer FirstFrame => Frames[0].Pixels;

    /// <summary>Wraps a still image as a single-frame, single-play sequence.</summary>
    public static BImageSequence Static(BPixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        return new BImageSequence([new BImageFrame(pixels, 0, 100)], pixels.Width, pixels.Height, loopCount: 1);
    }
}
