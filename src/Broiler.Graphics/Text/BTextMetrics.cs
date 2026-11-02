using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using System;

namespace Broiler.Graphics.Text;

public readonly record struct BTextMetrics(
    BSize Size,
    double Baseline,
    double Advance,
    double LineHeight);

public interface IBTextMetricsProvider
{
    double MeasureAdvance(string text, BFontStyle font);

    double GetLineHeight(BFontStyle font);
}

/// <summary>Stable reference ink extents above and below the alphabetic baseline.</summary>
public readonly record struct BTextVerticalMetrics(double Ascent, double Descent);

/// <summary>Optional host measurement of a fixed reference string (Hg), independent of caption text.</summary>
public interface IBTextVerticalMetricsProvider
{
    BTextVerticalMetrics GetVerticalMetrics(BFontStyle font);
}

public static class BTextMeasurer
{
    private static readonly object ProviderLock = new();
    private static readonly IBTextMetricsProvider FallbackProvider = new FallbackTextMetricsProvider();
    private static IBTextMetricsProvider _provider = FallbackProvider;

    public static void Register(IBTextMetricsProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (ProviderLock)
            _provider = provider;
    }

    public static bool UseProviderIfDefault(IBTextMetricsProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (ProviderLock)
        {
            if (!ReferenceEquals(_provider, FallbackProvider))
                return false;

            _provider = provider;
            return true;
        }
    }

    public static BTextMetrics Measure(BTextRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Measure(run.Text, run.Font);
    }

    public static BTextMetrics Measure(string text, BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        double advance = MeasureAdvance(text, font);
        double lineHeight = GetLineHeight(font);
        return new BTextMetrics(new BSize(advance, lineHeight), Math.Round(font.Size * 0.8, 2), advance, lineHeight);
    }

    public static double MeasureAdvance(string text, BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        // A face the font carries is measured from its own advances, whatever provider is
        // registered: a provider resolves FamilyName against installed fonts, and the face is the
        // one font it cannot find there. BImageRenderer and every backend draw it the same way.
        if (font.Face is { } face)
            return Math.Round(face.MeasureAdvance(text, font.Size), 2);

        return _provider.MeasureAdvance(text, font);
    }

    public static double GetLineHeight(BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return _provider.GetLineHeight(font);
    }

    /// <summary>Measures a stable reference so changing a caption cannot move its baseline.</summary>
    public static BTextVerticalMetrics GetVerticalMetrics(BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (font.Face is { } face)
            return face.GetVerticalMetrics(font.Size);
        if (_provider is IBTextVerticalMetricsProvider vertical)
            return vertical.GetVerticalMetrics(font);
        bool bold = font.Weight >= BFontWeight.Bold;
        var system = FallbackSystemFont.For(font.FamilyName, bold, font.Slant != BFontSlant.Normal);
        return system?.GetVerticalMetrics(font.Size, bold)
            ?? new BTextVerticalMetrics(font.Size * 0.8, font.Size * 0.2);
    }

    private sealed class FallbackTextMetricsProvider : IBTextMetricsProvider
    {
        public double MeasureAdvance(string text, BFontStyle font)
        {
            double size = font.Size;
            bool bold = font.Weight >= BFontWeight.Bold;
            bool italic = font.Slant is BFontSlant.Italic or BFontSlant.Oblique;

            // Mirror BImageRenderer's pen advance exactly so measurement (caret,
            // hit-testing, selection, scroll) matches what is actually drawn. When a
            // real host font backs rendering, use its glyph advances; otherwise every
            // glyph is drawn with the fixed-width block font. The family is resolved the
            // same way the renderer resolves it, so both land on one face.
            double blockAdvance = Math.Max(1.0, size * 0.62);
            FallbackSystemFont? realFont = FallbackSystemFont.For(font.FamilyName, bold, italic);

            double advance = 0;
            foreach (char character in text)
            {
                if (character is '\r' or '\n')
                    continue;

                advance += realFont is not null
                    ? realFont.GetAdvancePixels(character, bold, size, blockAdvance)
                    : blockAdvance;
            }

            return Math.Round(advance, 2);
        }

        public double GetLineHeight(BFontStyle font) => Math.Ceiling(font.Size * 1.25);
    }
}
