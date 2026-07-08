using System;

namespace Broiler.Graphics;

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
        return new BTextMetrics(new BSize(advance, lineHeight), Math.Round(font.SizeInPixels * 0.8, 2), advance, lineHeight);
    }

    public static double MeasureAdvance(string text, BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        return _provider.MeasureAdvance(text, font);
    }

    public static double GetLineHeight(BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return _provider.GetLineHeight(font);
    }

    private sealed class FallbackTextMetricsProvider : IBTextMetricsProvider
    {
        public double MeasureAdvance(string text, BFontStyle font)
        {
            double size = font.SizeInPixels;
            bool bold = font.Weight >= BFontWeight.Bold;

            // Mirror BImageRenderer's pen advance exactly so measurement (caret,
            // hit-testing, selection, scroll) matches what is actually drawn. When a
            // real host font backs rendering, use its glyph advances; otherwise every
            // glyph is drawn with the fixed-width block font.
            double blockAdvance = Math.Max(1.0, size * 0.62);
            FallbackSystemFont? realFont = FallbackSystemFont.Shared;

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

        public double GetLineHeight(BFontStyle font) => Math.Ceiling(font.SizeInPixels * 1.25);
    }
}
