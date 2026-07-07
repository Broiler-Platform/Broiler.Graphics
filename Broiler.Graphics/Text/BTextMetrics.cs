using System;

namespace Broiler.Graphics;

public readonly record struct BTextMetrics(
    BSize Size,
    double Baseline,
    double Advance,
    double LineHeight);

public static class BTextMeasurer
{
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

    public static double GetLineHeight(BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return Math.Ceiling(font.SizeInPixels * 1.25);
    }
}
