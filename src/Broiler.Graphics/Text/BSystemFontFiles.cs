using System;

namespace Broiler.Graphics;

/// <summary>
/// Resolves a font family to a file on this machine. Returns false when the host has no face for
/// that family, which leaves the caller on its own fallback.
/// </summary>
public delegate bool BFontFileResolver(string? family, bool bold, bool italic, out string path);

/// <summary>
/// The host's installed fonts, as seen by the software rasterizer.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BImageRenderer"/> and <see cref="BTextMeasurer"/>'s built-in provider used to draw and
/// measure every run with one discovered sans face, whatever family was asked for. That is fine as
/// a last resort on a font-less box, but it is not fine when the requested family <i>is</i>
/// installed: text laid out against one face and drawn in another does not fit the space reserved
/// for it, and the words in a line visibly drift apart.
/// </para>
/// <para>
/// Enumerating families means reading each file's <c>name</c> table, and something already does
/// that — <c>Broiler.Layout.Text.SystemFontIndex</c>, which owns the question "which file is this
/// family" for the whole engine. Graphics cannot reference Layout (the dependency runs the other
/// way), so the index registers itself here, the same way image codecs arrive through
/// <see cref="BImageCodecs"/>. Nothing registered means the previous single-face behaviour, so a
/// host that never wires one up is unaffected.
/// </para>
/// </remarks>
public static class BSystemFontFiles
{
    private static volatile BFontFileResolver? _resolver;

    /// <summary>Whether a resolver has been registered.</summary>
    public static bool HasResolver => _resolver is not null;

    /// <summary>
    /// Registers the process-wide resolver. Last call wins; a composition root that wants a
    /// different font source can replace one already installed.
    /// </summary>
    public static void Use(BFontFileResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>Removes the registered resolver, restoring the single-fallback-face behaviour.</summary>
    public static void Clear() => _resolver = null;

    internal static bool TryResolve(string? family, bool bold, bool italic, out string path)
    {
        BFontFileResolver? resolver = _resolver;
        if (resolver is null || string.IsNullOrWhiteSpace(family))
        {
            path = string.Empty;
            return false;
        }

        try
        {
            return resolver(family, bold, italic, out path) && !string.IsNullOrEmpty(path);
        }
        catch (Exception)
        {
            // A resolver that throws must not take a frame down with it: the caller's fallback face
            // still renders the run, which is strictly better than no page at all.
            path = string.Empty;
            return false;
        }
    }
}
