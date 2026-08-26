using System;

namespace Broiler.Graphics;

/// <summary>
/// One frame of an animated image: the fully-composited pixels for this point in
/// the animation, plus how long it is displayed. The delay is a rational number of
/// seconds (<see cref="DelayNumerator"/> / <see cref="DelayDenominator"/>), matching
/// the APNG <c>fcTL</c> encoding.
/// </summary>
public sealed class BImageFrame
{
    public BImageFrame(BPixelBuffer pixels, int delayNumerator, int delayDenominator)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (delayNumerator < 0)
            throw new ArgumentOutOfRangeException(nameof(delayNumerator));
        if (delayDenominator < 0)
            throw new ArgumentOutOfRangeException(nameof(delayDenominator));

        Pixels = pixels;
        DelayNumerator = delayNumerator;
        DelayDenominator = delayDenominator;
    }

    /// <summary>The composited RGBA pixels for this frame, at the animation's canvas size.</summary>
    public BPixelBuffer Pixels { get; }

    /// <summary>Numerator of the display duration, in seconds.</summary>
    public int DelayNumerator { get; }

    /// <summary>Denominator of the display duration; per APNG, 0 is treated as 100.</summary>
    public int DelayDenominator { get; }

    /// <summary>The display duration. A 0 denominator is interpreted as hundredths of a second.</summary>
    public TimeSpan Delay =>
        TimeSpan.FromSeconds(DelayNumerator / (double)(DelayDenominator == 0 ? 100 : DelayDenominator));
}
