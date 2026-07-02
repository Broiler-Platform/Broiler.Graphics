#nullable disable
namespace Broiler.Graphics;

public static class UnicodeCodepointReader
{
    public const int ReplacementCodePoint = 0xFFFD;

    public static int ReadCodePoint(string text, int index, out int nextIndex)
    {
        char c = text[index];
        if (char.IsHighSurrogate(c))
        {
            if (index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                nextIndex = index + 2;
                return char.ConvertToUtf32(c, text[index + 1]);
            }

            nextIndex = index + 1;
            return ReplacementCodePoint;
        }

        if (char.IsLowSurrogate(c))
        {
            nextIndex = index + 1;
            return ReplacementCodePoint;
        }

        nextIndex = index + 1;
        return c;
    }
}
