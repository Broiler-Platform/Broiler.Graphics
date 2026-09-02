using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace Broiler.Graphics.WebAssembly;

/// <summary>
/// An <see cref="IBTextMetricsProvider"/> backed by the browser's Canvas 2D <c>measureText</c>, using
/// the exact font string the replay module paints with (<c>fontString</c> in the JS module). Registering
/// it makes managed text measurement — caret placement, selection highlights, hit-testing, line
/// wrapping, and the Formatting-codes fixed-width grid — agree with what the canvas actually draws.
/// <para>
/// The built-in fallback cannot: the browser sandbox exposes no host font files, so the fallback
/// advances every glyph by a fixed block width. With real faces now resolved for painting, that fixed
/// width drifts from the drawn glyphs — systematically in a monospace view, subtly under a proportional
/// one. Measuring through the same canvas closes the gap.
/// </para>
/// Advances are cached per (text, font) because layout re-measures the same runs every frame; a cache
/// hit avoids a JS boundary crossing. Line height keeps the fallback's formula so vertical layout is
/// unchanged — only horizontal advances were mismatched.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class CanvasTextMetricsProvider : IBTextMetricsProvider
{
    private readonly Dictionary<CacheKey, double> _advanceCache = new();

    public double MeasureAdvance(string text, BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        if (text.Length == 0)
            return 0;

        string family = font.FamilyName ?? string.Empty;
        bool italic = font.Slant != BFontSlant.Normal;
        var key = new CacheKey(text, family, font.Size, (int)font.Weight, italic);
        if (_advanceCache.TryGetValue(key, out double cached))
            return cached;

        double advance = CanvasInterop.MeasureAdvance(
            text, font.Size, (int)font.Weight, italic ? 1 : 0, family);
        if (!double.IsFinite(advance) || advance < 0)
            advance = 0;
        advance = Math.Round(advance, 2);

        _advanceCache[key] = advance;
        return advance;
    }

    public double GetLineHeight(BFontStyle font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return Math.Ceiling(font.Size * 1.25);
    }

    private readonly record struct CacheKey(string Text, string Family, double Size, int Weight, bool Italic);
}
