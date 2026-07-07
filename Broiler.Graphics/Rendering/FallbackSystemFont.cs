using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Broiler.Graphics;

/// <summary>
/// Discovers a real sans-serif TrueType/OpenType face from the host and exposes
/// cached glyph outlines so <see cref="BImageRenderer"/> can rasterize genuine
/// text instead of the built-in 5x7 block font. Discovery is best-effort: when
/// no usable font is found (a truly font-less system) the provider is null and
/// callers fall back to the block font.
/// </summary>
internal sealed class FallbackSystemFont
{
    private readonly TrueTypeFont _regular;
    private readonly TrueTypeFont _bold;
    private readonly Dictionary<int, IReadOnlyList<PointF[]>> _regularContours = [];
    private readonly Dictionary<int, IReadOnlyList<PointF[]>> _boldContours = [];
    private readonly Dictionary<int, int> _regularGlyphIndex = [];
    private readonly Dictionary<int, int> _boldGlyphIndex = [];
    private readonly Dictionary<int, int> _regularAdvance = [];
    private readonly Dictionary<int, int> _boldAdvance = [];

    private FallbackSystemFont(TrueTypeFont regular, string regularPath, TrueTypeFont? bold, string? boldPath)
    {
        _regular = regular;
        _bold = bold ?? regular;
        RegularPath = regularPath;
        BoldPath = boldPath;
    }

    public string RegularPath { get; }

    public string? BoldPath { get; }

    private static readonly Lazy<FallbackSystemFont?> LazyShared = new(TryLoad, isThreadSafe: true);

    /// <summary>The process-wide resolved font, or null when none could be loaded.</summary>
    public static FallbackSystemFont? Shared => LazyShared.Value;

    /// <summary>A human-readable summary of what was resolved, for diagnostics/logging.</summary>
    public static string Describe()
    {
        FallbackSystemFont? font = Shared;
        if (font is null)
            return "none found on host; using built-in fallback bitmap font.";

        string bold = font.BoldPath is null ? " (no bold variant; synthesizing from regular)" : "";
        return font.RegularPath + bold;
    }

    public bool TryGetGlyph(int codepoint, bool bold, out IReadOnlyList<PointF[]> contours, out int advanceWidth, out int unitsPerEm)
    {
        (int glyphIndex, int advance, int upem) = Resolve(codepoint, bold);
        unitsPerEm = upem;
        advanceWidth = advance;
        if (glyphIndex <= 0)
        {
            contours = [];
            return false;
        }

        Dictionary<int, IReadOnlyList<PointF[]>> cache = bold ? _boldContours : _regularContours;
        if (!cache.TryGetValue(glyphIndex, out IReadOnlyList<PointF[]>? cached))
        {
            cached = (bold ? _bold : _regular).GetGlyphContours(glyphIndex);
            cache[glyphIndex] = cached;
        }

        contours = cached;
        return true;
    }

    /// <summary>
    /// Horizontal advance in pixels for a codepoint, mirroring exactly how
    /// <see cref="BImageRenderer"/> advances the pen so text measurement and
    /// rendering agree (caret/hit-test/selection alignment). Falls back to
    /// <paramref name="blockAdvance"/> for codepoints the font lacks (which are
    /// drawn with the block glyph).
    /// </summary>
    public double GetAdvancePixels(int codepoint, bool bold, double sizePixels, double blockAdvance)
    {
        (int glyphIndex, int advance, int upem) = Resolve(codepoint, bold);
        if (glyphIndex <= 0)
            return blockAdvance;

        return advance > 0 ? advance * (sizePixels / upem) : blockAdvance;
    }

    private (int GlyphIndex, int Advance, int UnitsPerEm) Resolve(int codepoint, bool bold)
    {
        TrueTypeFont face = bold ? _bold : _regular;
        Dictionary<int, int> glyphCache = bold ? _boldGlyphIndex : _regularGlyphIndex;
        Dictionary<int, int> advanceCache = bold ? _boldAdvance : _regularAdvance;

        int unitsPerEm = face.UnitsPerEm > 0 ? face.UnitsPerEm : 1000;
        if (!glyphCache.TryGetValue(codepoint, out int glyphIndex))
        {
            glyphIndex = face.GetGlyphIndex(codepoint);
            glyphCache[codepoint] = glyphIndex;
        }

        int advance = 0;
        if (glyphIndex > 0 && !advanceCache.TryGetValue(glyphIndex, out advance))
        {
            advance = face.GetAdvanceWidth(glyphIndex);
            advanceCache[glyphIndex] = advance;
        }

        return (glyphIndex, advance, unitsPerEm);
    }

    private static FallbackSystemFont? TryLoad()
    {
        foreach ((string regular, string? bold) in EnumerateCandidates())
        {
            TrueTypeFont? face = TryLoadFace(regular);
            if (face is null || !face.HasOutlines)
                continue;

            TrueTypeFont? boldFace = bold is null ? null : TryLoadFace(bold);
            if (boldFace is not null && !boldFace.HasOutlines)
                boldFace = null;

            return new FallbackSystemFont(face, regular, boldFace, boldFace is null ? null : bold);
        }

        return null;
    }

    private static TrueTypeFont? TryLoadFace(string path)
    {
        try
        {
            return File.Exists(path) ? TrueTypeFont.LoadFromFile(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static IEnumerable<(string Regular, string? Bold)> EnumerateCandidates()
    {
        // Preferred, well-known sans-serif faces first (regular + matching bold).
        foreach ((string Regular, string? Bold) known in KnownFontPairs)
        {
            if (File.Exists(known.Regular))
                yield return known;
        }

        // Last resort: scan common font roots for any usable .ttf/.otf so a box
        // with an unusual font set still renders real text.
        foreach (string root in FontRoots())
        {
            string? scanned = TryFindAnyFont(root);
            if (scanned is not null)
                yield return (scanned, null);
        }
    }

    private static readonly (string Regular, string? Bold)[] KnownFontPairs =
    [
        // Debian/Ubuntu
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"),
        ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"),
        ("/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf", "/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf"),
        ("/usr/share/fonts/truetype/freefont/FreeSans.ttf", "/usr/share/fonts/truetype/freefont/FreeSansBold.ttf"),
        // Fedora/RHEL
        ("/usr/share/fonts/dejavu-sans-fonts/DejaVuSans.ttf", "/usr/share/fonts/dejavu-sans-fonts/DejaVuSans-Bold.ttf"),
        // Arch
        ("/usr/share/fonts/TTF/DejaVuSans.ttf", "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf"),
        // Windows
        ("C:/Windows/Fonts/segoeui.ttf", "C:/Windows/Fonts/segoeuib.ttf"),
        ("C:/Windows/Fonts/arial.ttf", "C:/Windows/Fonts/arialbd.ttf"),
        ("C:/Windows/Fonts/tahoma.ttf", "C:/Windows/Fonts/tahomabd.ttf"),
        // macOS
        ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf"),
        ("/Library/Fonts/Arial.ttf", "/Library/Fonts/Arial Bold.ttf"),
    ];

    private static IEnumerable<string> FontRoots()
    {
        yield return "/usr/share/fonts";
        yield return "/usr/local/share/fonts";
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, ".fonts");
            yield return Path.Combine(home, ".local", "share", "fonts");
        }
    }

    private static string? TryFindAnyFont(string root)
    {
        if (!Directory.Exists(root))
            return null;

        try
        {
            string? best = null;
            foreach (string path in Directory.EnumerateFiles(root, "*.ttf", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                // Prefer an upright, non-bold, non-italic sans face.
                if (name.Contains("Sans", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Italic", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Oblique", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Bold", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                best ??= path;
            }

            return best;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
