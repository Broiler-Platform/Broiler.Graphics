using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Graphics;

/// <summary>
/// Lists the font families installed on this machine. Returning nothing is legal and means the host
/// has no font source to ask.
/// </summary>
public delegate IEnumerable<string> BFontFamilyEnumerator();

/// <summary>
/// The families the host has, for anything that offers the user a choice of font.
/// </summary>
/// <remarks>
/// <para>
/// A font picker that lists a fixed set of well-known names is wrong in both directions: it offers
/// families this machine does not have, and hides the several hundred it does. So the list comes
/// from the host — from a backend that registered <see cref="Use"/> (DirectWrite knows the answer
/// exactly, including families installed from outside a font directory), and otherwise from
/// scanning the platform's font directories and reading each file's own name.
/// </para>
/// <para>
/// The sibling of this is <see cref="BSystemFontFiles"/>, which answers "which file is this family"
/// for the software rasterizer. The two fit together: <see cref="InstallFontFileResolver"/> wires
/// the same scan into it, so a family this offers is one the renderer can then actually draw.
/// </para>
/// <para>
/// Enumeration is cached, because it reads the file system and a font list is asked for every time
/// a dialog opens. <see cref="Refresh"/> drops the cache for the rare host that installs a font
/// while it runs.
/// </para>
/// </remarks>
public static class BSystemFonts
{
    private static readonly object Gate = new();
    private static volatile BFontFamilyEnumerator? _enumerator;

    // Volatile because the fast path reads it without taking the lock: a font list is asked for on
    // whichever thread opens a dialog, and every write to it happens under Gate.
    private static volatile string[]? _families;

    /// <summary>Whether a host enumerator has been registered.</summary>
    public static bool HasEnumerator => _enumerator is not null;

    /// <summary>
    /// Registers the process-wide enumerator. Last call wins, so a composition root can replace one
    /// a backend installed.
    /// </summary>
    public static void Use(BFontFamilyEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        lock (Gate)
        {
            _enumerator = enumerator;
            _families = null;
        }
    }

    /// <summary>
    /// Registers <paramref name="enumerator"/> only if nothing has been registered yet, and reports
    /// whether it took. This is how a graphics backend offers its font source without overriding an
    /// application that has already chosen one.
    /// </summary>
    public static bool UseIfUnset(BFontFamilyEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        lock (Gate)
        {
            if (_enumerator is not null)
                return false;

            _enumerator = enumerator;
            _families = null;
            return true;
        }
    }

    /// <summary>Removes the registered enumerator, restoring the built-in font-directory scan.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            _enumerator = null;
            _families = null;
        }
    }

    /// <summary>Drops the cached list, so the next call asks the host again.</summary>
    public static void Refresh()
    {
        lock (Gate)
            _families = null;
    }

    /// <summary>
    /// Every installed family, sorted, de-duplicated and free of blanks. Empty on a host with no
    /// font source — a browser page, or a machine whose font directories are unreadable — which
    /// callers are expected to fall back from rather than present as "no fonts installed".
    /// </summary>
    public static IReadOnlyList<string> GetFamilies()
    {
        string[]? cached = _families;
        if (cached is not null)
            return cached;

        lock (Gate)
        {
            // Resolved inside the lock, so a Clear() racing the first call cannot publish a list
            // built from the enumerator it just removed.
            _families ??= Normalize(Enumerate());
            return _families;
        }
    }

    /// <summary>
    /// The file holding a family's closest face, from the built-in scan. False when no enumerator
    /// registered by a host has an opinion — the scan is the only source that knows about files.
    /// </summary>
    public static bool TryGetFontFile(string? family, bool bold, bool italic, out string path) =>
        InstalledFontScan.Shared.TryResolve(family, bold, italic, out path);

    /// <summary>
    /// Points <see cref="BSystemFontFiles"/> at the built-in scan, so the software rasterizer draws
    /// each family in its own face rather than drawing everything in one discovered fallback.
    /// </summary>
    /// <remarks>
    /// A composition root calls this; it is not automatic. Installing a resolver changes what every
    /// run in the process is drawn with, and a host that pins its own faces — a renderer comparing
    /// output across machines, say — must be able to keep them. Does nothing if a resolver is
    /// already installed, and reports whether one is installed now.
    /// </remarks>
    public static bool InstallFontFileResolver()
    {
        if (BSystemFontFiles.HasResolver)
            return true;

        if (InstalledFontScan.Shared.Families.Count == 0)
            return false;

        BSystemFontFiles.Use(InstalledFontScan.Shared.TryResolve);
        return true;
    }

    private static IEnumerable<string> Enumerate()
    {
        BFontFamilyEnumerator? enumerator = _enumerator;
        if (enumerator is null)
            return InstalledFontScan.Shared.Families;

        try
        {
            return enumerator() ?? [];
        }
        catch (Exception)
        {
            // A host enumerator that throws must not take the dialog that asked down with it; the
            // caller's own fallback list is a far better outcome than no dialog at all.
            return [];
        }
    }

    private static string[] Normalize(IEnumerable<string> families)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();
        foreach (string? family in families)
        {
            string trimmed = family?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || !seen.Add(trimmed))
                continue;

            normalized.Add(trimmed);
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return [.. normalized];
    }
}
