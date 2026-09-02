using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Broiler.Graphics;

/// <summary>
/// The host's installed font files, indexed by the family and face names they carry.
/// </summary>
/// <remarks>
/// <para>
/// This is the fallback source behind <see cref="BSystemFonts"/>: a backend that can ask the
/// platform directly (DirectWrite, fontconfig) registers its own enumerator and this never runs.
/// Everywhere else — a Linux or Android head drawing through the software rasterizer — the font
/// directories are the only list there is.
/// </para>
/// <para>
/// The scan answers two questions from one pass, because they have the same answer: which families
/// exist (for a font picker) and which file each face lives in (for
/// <see cref="BSystemFontFiles"/>, so a run is drawn in the family it was measured in). Listing a
/// family the renderer cannot then draw would be worse than listing nothing.
/// </para>
/// </remarks>
internal sealed class InstalledFontScan
{
    private static readonly string[] FontExtensions = [".ttf", ".otf", ".ttc", ".otc"];

    /// <summary>
    /// A ceiling on files inspected, so a font directory someone has poured a foundry into cannot
    /// stall the first font dialog. Well past any real system font set — Windows ships about 400.
    /// </summary>
    private const int MaxFilesScanned = 4096;

    private static readonly Lazy<InstalledFontScan> LazyShared = new(Scan, isThreadSafe: true);

    private readonly Dictionary<string, FaceFiles> _families;

    private InstalledFontScan(Dictionary<string, FaceFiles> families)
    {
        _families = families;
        Families = families.Keys.OrderBy(static family => family, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>The process-wide scan, run once on first use.</summary>
    internal static InstalledFontScan Shared => LazyShared.Value;

    /// <summary>Every family found, ordered by name.</summary>
    internal IReadOnlyList<string> Families { get; }

    /// <summary>The file holding the closest face this family has to the one asked for.</summary>
    internal bool TryResolve(string? family, bool bold, bool italic, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(family) || !_families.TryGetValue(family.Trim(), out FaceFiles? faces))
            return false;

        string? resolved = faces.Pick(bold, italic);
        if (resolved is null)
            return false;

        path = resolved;
        return true;
    }

    private static InstalledFontScan Scan()
    {
        var families = new Dictionary<string, FaceFiles>(StringComparer.OrdinalIgnoreCase);
        int budget = MaxFilesScanned;

        foreach (string directory in FontDirectories())
        {
            foreach (string file in EnumerateFontFiles(directory))
            {
                if (budget-- <= 0)
                    return new InstalledFontScan(families);

                foreach (FontNameTable.FaceName face in FontNameTable.Read(file))
                {
                    if (!families.TryGetValue(face.Family, out FaceFiles? entry))
                    {
                        entry = new FaceFiles();
                        families[face.Family] = entry;
                    }

                    (bool bold, bool italic) = ReadFace(face.Subfamily);
                    entry.SetIfAbsent(bold, italic, file);
                }
            }
        }

        return new InstalledFontScan(families);
    }

    /// <summary>
    /// Where each platform keeps its fonts, user directories last so a system face wins a name
    /// collision.
    /// </summary>
    /// <remarks>
    /// Deliberately not shared with <c>FallbackSystemFont</c>'s roots, which exist to find any one
    /// usable sans face on a machine whose well-known paths all missed. That list omits Windows and
    /// macOS on purpose, because their faces are named there outright; this one cannot, because it
    /// is looking for every family rather than a single fallback.
    /// </remarks>
    private static IEnumerable<string> FontDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows))
                yield return Path.Combine(windows, "Fonts");

            // Per-user installs, which since Windows 10 do not need an administrator and so are
            // where a font a user added themselves usually lands.
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                yield return Path.Combine(localAppData, "Microsoft", "Windows", "Fonts");

            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "/System/Library/Fonts";
            yield return "/Library/Fonts";
            string macHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(macHome))
                yield return Path.Combine(macHome, "Library", "Fonts");

            yield break;
        }

        yield return "/usr/share/fonts";
        yield return "/usr/local/share/fonts";

        // Android keeps every system face here and has none of the paths above.
        yield return "/system/fonts";

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            yield break;

        yield return Path.Combine(home, ".fonts");
        yield return Path.Combine(home, ".local", "share", "fonts");
    }

    private static IEnumerable<string> EnumerateFontFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return [];

        try
        {
            return Directory
                .EnumerateFiles(directory, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    // A font directory is not the place to follow a link out of, and a cycle
                    // through one would never terminate.
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = true,
                    MaxRecursionDepth = 8,
                })
                .Where(static file => FontExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                // Ordinal, so two machines with the same fonts build the same index whatever order
                // their file systems hand the entries over in.
                .OrderBy(static file => file, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Reads a face out of a subfamily name such as "Bold Italic".</summary>
    private static (bool Bold, bool Italic) ReadFace(string subfamily) =>
        (subfamily.Contains("bold", StringComparison.OrdinalIgnoreCase),
            subfamily.Contains("italic", StringComparison.OrdinalIgnoreCase) ||
            subfamily.Contains("oblique", StringComparison.OrdinalIgnoreCase));

    /// <summary>The four faces of one family, each optional.</summary>
    private sealed class FaceFiles
    {
        private string? _regular;
        private string? _bold;
        private string? _italic;
        private string? _boldItalic;

        internal void SetIfAbsent(bool bold, bool italic, string path)
        {
            switch (bold, italic)
            {
                case (true, true):
                    _boldItalic ??= path;
                    break;
                case (true, false):
                    _bold ??= path;
                    break;
                case (false, true):
                    _italic ??= path;
                    break;
                default:
                    _regular ??= path;
                    break;
            }
        }

        /// <summary>
        /// The best available face. Falling back to the regular one is deliberate: drawing bold
        /// text in the regular face is wrong but recoverable, while returning nothing sends the
        /// family to an unrelated host font and moves every glyph on the line.
        /// </summary>
        internal string? Pick(bool bold, bool italic) => (bold, italic) switch
        {
            (true, true) => _boldItalic ?? _bold ?? _italic ?? _regular,
            (true, false) => _bold ?? _regular ?? _boldItalic,
            (false, true) => _italic ?? _regular ?? _boldItalic,
            _ => _regular ?? _bold ?? _italic ?? _boldItalic,
        };
    }
}
