#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Graphics;

/// <summary>
/// Resolves and caches fonts by family/size/style.
/// <para>
/// <b>Thread-safe.</b> The instance this type is usually reached through is the
/// one owned by the <c>RAdapter</c> behind the process-wide
/// <c>CompatProvider.ImageAdapter</c>, so every render thread shares one of
/// these. All four caches are therefore concurrent collections rather than
/// plain dictionaries — see the note on <c>_fontsCache</c> for why the nesting
/// went away with them.
/// </para>
/// </summary>
public sealed class FontsHandler
{
    private readonly IFontCreator _fontCreator;
    private readonly ConcurrentDictionary<string, string> _fontsMapping = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly ConcurrentDictionary<string, RFontFamily> _existingFontFamilies = new(StringComparer.InvariantCultureIgnoreCase);

    // Flat, not the three nested dictionaries this used to be. Nesting cannot be
    // made safe by swapping in ConcurrentDictionary at each level: the lookup was
    // a read of the outer map followed by a *write* of a fresh inner map, so two
    // threads missing on the same family both published one and whichever lost
    // took its already-cached sizes with it. One dictionary keyed by the whole
    // tuple has no such window, and it costs one hash instead of three.
    private readonly ConcurrentDictionary<FontCacheKey, RFont> _fontsCache = new(FontCacheKeyComparer.Instance);

    // Fonts carrying CSS font-feature-settings are cached separately, keyed by
    // family/size/style/features, so the common (no-feature) path is unchanged.
    private readonly ConcurrentDictionary<string, RFont> _featuredFontsCache = new(StringComparer.InvariantCultureIgnoreCase);

    public FontsHandler(IFontCreator fontCreator)
    {
        ArgumentNullException.ThrowIfNull(fontCreator);

        _fontCreator = fontCreator;
    }

    public bool IsFontExists(string family)
    {
        return TryResolveAvailableFamily(family, out _);
    }

    public void AddFontFamily(RFontFamily fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);

        _existingFontFamilies[fontFamily.Name] = fontFamily;
    }

    public void AddFontFamilyMapping(string fromFamily, string toFamily)
    {
        ArgumentException.ThrowIfNullOrEmpty(fromFamily);
        ArgumentException.ThrowIfNullOrEmpty(toFamily);

        _fontsMapping[fromFamily] = toFamily;
    }

    public RFont GetCachedFont(string family, double size, Graphics.FontStyle style, string fontFeatures = null)
    {
        var resolvedFamily = ResolveFontFamily(family);

        if (!string.IsNullOrEmpty(fontFeatures))
        {
            string key = resolvedFamily + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "|" + (int)style + "|" + fontFeatures;
            if (_featuredFontsCache.TryGetValue(key, out var featured))
                return featured;

            featured = CreateFont(resolvedFamily, size, style);
            featured.FontFeatures = fontFeatures;
            // GetOrAdd, not the indexer: RFont.FontFeatures is settable, so two
            // threads publishing different instances for one key would leave some
            // callers holding a font whose features another thread is still
            // writing. Whoever wins, every caller leaves with that same instance.
            return _featuredFontsCache.GetOrAdd(key, featured);
        }

        var key2 = new FontCacheKey(resolvedFamily, size, style);
        if (_fontsCache.TryGetValue(key2, out RFont font))
            return font;

        // Deliberately not the GetOrAdd(key, factory) overload. CreateFont can
        // fall back to a different style and, on some adapters, touches host
        // font state; running it inside the dictionary's bucket is a wider
        // window than running it outside and racing only on publication. RFont
        // holds no unmanaged handle and is not IDisposable, so a loser's font is
        // ordinary garbage.
        return _fontsCache.GetOrAdd(key2, CreateFont(resolvedFamily, size, style));
    }

    /// <summary>Cache key for the no-features path: family, size and style together.</summary>
    private readonly record struct FontCacheKey(string Family, double Size, Graphics.FontStyle Style);

    /// <summary>
    /// Compares <see cref="FontCacheKey"/> with the family matched exactly the way
    /// the nested dictionary this replaced did — its outer level was built with
    /// <see cref="StringComparer.InvariantCultureIgnoreCase"/>. Normalising the
    /// family into the key instead (upper- or lower-casing it) would be a
    /// different relation on the scripts where casing does not round-trip, so the
    /// comparer is spelled out rather than folded into the key.
    /// </summary>
    private sealed class FontCacheKeyComparer : IEqualityComparer<FontCacheKey>
    {
        public static readonly FontCacheKeyComparer Instance = new();

        public bool Equals(FontCacheKey x, FontCacheKey y) =>
            x.Size.Equals(y.Size)
            && x.Style == y.Style
            && StringComparer.InvariantCultureIgnoreCase.Equals(x.Family, y.Family);

        public int GetHashCode(FontCacheKey key) => HashCode.Combine(
            key.Family is null ? 0 : StringComparer.InvariantCultureIgnoreCase.GetHashCode(key.Family),
            key.Size,
            key.Style);
    }

    private string ResolveFontFamily(string family)
    {
        if (TryResolveAvailableFamily(family, out var resolvedFamily))
            return resolvedFamily;

        return EnumerateFamilyCandidates(family).FirstOrDefault() ?? family;
    }

    private bool TryResolveAvailableFamily(string family, out string resolvedFamily)
    {
        foreach (var candidate in EnumerateFamilyCandidates(family))
        {
            if (_existingFontFamilies.ContainsKey(candidate))
            {
                resolvedFamily = candidate;
                return true;
            }

            if (_fontsMapping.TryGetValue(candidate, out string mappedFamily)
                && _existingFontFamilies.ContainsKey(mappedFamily))
            {
                resolvedFamily = mappedFamily;
                return true;
            }
        }

        resolvedFamily = family;
        return false;
    }

    private static IEnumerable<string> EnumerateFamilyCandidates(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
            yield break;

        foreach (var rawCandidate in family.Split(','))
        {
            var candidate = rawCandidate.Trim().Trim('\'', '"');
            if (!string.IsNullOrWhiteSpace(candidate))
                yield return candidate;
        }
    }

    private RFont CreateFont(string family, double size, Graphics.FontStyle style)
    {
        RFontFamily fontFamily;

        try
        {
            return _existingFontFamilies.TryGetValue(family, out fontFamily)
                ? _fontCreator.CreateFont(fontFamily, size, style)
                : _fontCreator.CreateFont(family, size, style);
        }
        catch (Exception ex)
        {
            // handle possibility of no requested style exists for the font, use regular then
            System.Diagnostics.Debug.WriteLine($"[HtmlRenderer] FontsHandler.GetCachedFont style fallback for '{family}': {ex.Message}");
            return _existingFontFamilies.TryGetValue(family, out fontFamily)
                ? _fontCreator.CreateFont(fontFamily, size, FontStyle.Regular)
                : _fontCreator.CreateFont(family, size, FontStyle.Regular);
        }
    }
}
