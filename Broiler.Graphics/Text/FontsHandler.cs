#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Graphics;

public sealed class FontsHandler
{
    private readonly IFontCreator _fontCreator;
    private readonly Dictionary<string, string> _fontsMapping = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, RFontFamily> _existingFontFamilies = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, Dictionary<double, Dictionary<Graphics.FontStyle, RFont>>> _fontsCache = new(StringComparer.InvariantCultureIgnoreCase);
    // Fonts carrying CSS font-feature-settings are cached separately, keyed by
    // family/size/style/features, so the common (no-feature) path is unchanged.
    private readonly Dictionary<string, RFont> _featuredFontsCache = new(StringComparer.InvariantCultureIgnoreCase);

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
            _featuredFontsCache[key] = featured;
            return featured;
        }

        var font = TryGetFont(resolvedFamily, size, style);

        if (font != null)
            return font;

        font = CreateFont(resolvedFamily, size, style);
        _fontsCache[resolvedFamily][size][style] = font;

        return font;
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

    private RFont TryGetFont(string family, double size, Graphics.FontStyle style)
    {
        RFont font = null;

        if (_fontsCache.TryGetValue(family, out Dictionary<double, Dictionary<Graphics.FontStyle, RFont>> a))
        {
            if (a.TryGetValue(size, out Dictionary<Graphics.FontStyle, RFont> b))
            {
                if (b.TryGetValue(style, out RFont value))
                    font = value;
            }
            else
            {
                _fontsCache[family][size] = [];
            }
        }
        else
        {
            _fontsCache[family] = new Dictionary<double, Dictionary<Graphics.FontStyle, RFont>> { [size] = [] };
        }

        return font;
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
                ? _fontCreator.CreateFont(fontFamily, size, Graphics.FontStyle.Regular)
                : _fontCreator.CreateFont(family, size, Graphics.FontStyle.Regular);
        }
    }
}
