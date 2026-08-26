using System;
using System.Collections.Generic;

namespace Broiler.Graphics;

/// <summary>
/// Provides backend-neutral default CSS font-family fallback mappings.
/// </summary>
public static class FontFamilyFallbackPolicy
{
    private static readonly (string FamilyName, string[] Candidates)[] GenericFamilyCandidates =
    [
        ("sans-serif", ["Arial", "Helvetica", "Liberation Sans", "DejaVu Sans"]),
        ("serif", ["Times New Roman", "Liberation Serif", "DejaVu Serif"]),
        ("monospace", ["Courier New", "Liberation Mono", "DejaVu Sans Mono"]),
        ("cursive", ["Comic Sans MS", "URW Chancery L"]),
        ("fantasy", ["Impact"])
    ];

    private static readonly string[] HelveticaAliasCandidates = ["Arial", "Liberation Sans", "DejaVu Sans"];

    /// <summary>
    /// Resolves the default font-family mappings supported by the available
    /// system font set.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ResolveDefaultMappings(IEnumerable<string> availableFamilies)
    {
        ArgumentNullException.ThrowIfNull(availableFamilies);

        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in availableFamilies)
        {
            if (!string.IsNullOrWhiteSpace(family))
                available.Add(family);
        }

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (familyName, candidates) in GenericFamilyCandidates)
        {
            var resolved = FirstAvailable(available, candidates);
            if (resolved != null)
                mappings[familyName] = resolved;
        }

        if (!available.Contains("Helvetica"))
        {
            var resolved = FirstAvailable(available, HelveticaAliasCandidates);
            if (resolved != null)
                mappings["Helvetica"] = resolved;
        }

        return mappings;
    }

    private static string? FirstAvailable(HashSet<string> availableFamilies, params string[] candidates)
        => Array.Find(candidates, availableFamilies.Contains);
}
