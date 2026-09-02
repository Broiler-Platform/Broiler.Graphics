using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Coverage for the font list this backend publishes to <see cref="BSystemFonts"/>.
/// </summary>
/// <remarks>
/// The point of asking DirectWrite rather than reading the font directory is that the two disagree
/// about what a family is, and only DirectWrite's answer is one this backend can draw. So the
/// assertions here are about the grouping, not about the count: a name the file scan produces but
/// <c>CreateTextFormat</c> cannot resolve is exactly the bug this replaced.
/// </remarks>
internal static class SystemFontCollectionTests
{
    public static void Register(ICollection<(string Name, Action Body)> tests)
    {
        tests.Add(("Creating the renderer publishes the system font list", PublishesFamilies));
        tests.Add(("Families are weight-stretch-style groups, not face names", GroupsFacesUnderTheirFamily));
        tests.Add(("Every listed family is drawn in a face of its own", ListedFamiliesResolve));
    }

    private static void PublishesFamilies()
    {
        using var renderer = new Direct2DRenderer();

        IReadOnlyList<string> families = BSystemFonts.GetFamilies();

        Assert.True(BSystemFonts.HasEnumerator, "the backend registered a font enumerator");
        Assert.True(families.Count > 0, "Windows has installed fonts to list");
        Assert.True(families.Contains("Arial"), "Arial is on every Windows install");
        Assert.True(
            families.SequenceEqual(families.OrderBy(static family => family, StringComparer.OrdinalIgnoreCase)),
            "the list is sorted");
        Assert.AreEqual(
            families.Count,
            families.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "the list holds no duplicates");
    }

    /// <summary>
    /// "Arial Black" is the Black face of Arial, not a family. Listing it would offer the user a
    /// name <c>IDWriteFactory::CreateTextFormat</c> resolves to nothing and silently replaces with
    /// Segoe UI — which is what the file-scan fallback does, and why this backend enumerates the
    /// collection instead. The weight box beside the family list is how that face is reached.
    /// </summary>
    private static void GroupsFacesUnderTheirFamily()
    {
        using var renderer = new Direct2DRenderer();

        IReadOnlyList<string> families = BSystemFonts.GetFamilies();

        Assert.True(
            !families.Contains("Arial Black"),
            "a face name is not a family name in DirectWrite's system collection");
    }

    /// <summary>
    /// A listed family has to measure as itself. An unresolvable name is not an error DirectWrite
    /// reports — it substitutes a default and carries on — so the only way to catch one is to
    /// measure it against a name that certainly does not exist.
    /// </summary>
    private static void ListedFamiliesResolve()
    {
        using var renderer = new Direct2DRenderer();
        const string sample = "Handgloves quick brown fox 12345";

        var fallback = new BFontStyle("Nonesuch Broiler Family", 16);
        double fallbackAdvance = BTextMeasurer.MeasureAdvance(sample, fallback);

        // Fonts that genuinely share the substituted face's metrics exist — the Segoe UI relatives
        // measure identically for Latin text — so this asserts a proportion rather than a
        // universal. A regression that stops resolving family names takes the whole list with it.
        string[] families = [.. BSystemFonts.GetFamilies()];
        int distinct = families.Count(family =>
            Math.Abs(BTextMeasurer.MeasureAdvance(sample, new BFontStyle(family, 16)) - fallbackAdvance) >= 0.005);

        Assert.True(
            distinct > families.Length * 3 / 4,
            $"most listed families draw in a face of their own; {distinct} of {families.Length} did");
    }
}
