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

        double advance = 0;
        foreach (char character in text)
            advance += GetCharacterAdvance(character, font.SizeInPixels);

        return Math.Round(advance, 2);
    }

    public static double GetLineHeight(BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return Math.Ceiling(font.SizeInPixels * 1.25);
    }

    private static double GetCharacterAdvance(char character, double em)
    {
        if (char.IsControl(character))
            return 0;
        if (char.IsWhiteSpace(character))
            return em * 0.32;
        if (char.IsPunctuation(character) || char.IsSymbol(character))
            return GetPunctuationOrSymbolAdvance(character, em);
        if (character >= 0x2E80)
            return em;
        if (char.IsDigit(character))
            return em * 0.56;
        if (IsNarrowLatin(character))
            return em * 0.32;
        if (IsWideLatin(character))
            return em * 0.78;
        if (char.IsUpper(character))
            return em * 0.6;

        return em * 0.5;
    }

    private static double GetPunctuationOrSymbolAdvance(char character, double em) =>
        character switch
        {
            '.' or ',' or ':' or ';' or '\'' or '`' or '!' or '|' => em * 0.28,
            '"' => em * 0.36,
            '-' or '_' or '/' or '\\' => em * 0.38,
            '(' or ')' or '[' or ']' or '{' or '}' => em * 0.34,
            '>' or '<' => em * 0.5,
            _ => em * 0.45,
        };

    private static bool IsNarrowLatin(char character) =>
        character is 'i' or 'l' or 'j' or 't' or 'f' or 'r' or 'I';

    private static bool IsWideLatin(char character) =>
        character is 'm' or 'w' or 'M' or 'W';
}
