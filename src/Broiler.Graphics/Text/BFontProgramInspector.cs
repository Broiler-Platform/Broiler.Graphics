using System;
using System.Collections.Generic;
using System.Text;

namespace Broiler.Graphics;

/// <summary>Why a font program was not accepted for inspection.</summary>
public enum BFontProgramRejection
{
    /// <summary>Accepted.</summary>
    None,

    /// <summary>Too short to be a font, or empty.</summary>
    Empty,

    /// <summary>Longer than the caller's byte budget.</summary>
    TooLarge,

    /// <summary>The leading four bytes name no accepted sfnt version.</summary>
    NotSfnt,

    /// <summary>
    /// A container this build does not accept: WOFF, WOFF2, or a font collection.
    /// </summary>
    UnsupportedContainer,

    /// <summary>The table directory does not fit, or a record runs past the end.</summary>
    MalformedDirectory,

    /// <summary>A table's offset and length do not lie inside the program.</summary>
    TableOutOfBounds,

    /// <summary>The program declares a table outside the accepted tuple.</summary>
    ExcludedTable,

    /// <summary>No character map this build reads.</summary>
    NoCharacterMap,

    /// <summary>The character map is malformed or exceeds a limit.</summary>
    MalformedCharacterMap,
}

/// <summary>The sfnt flavours this build accepts.</summary>
public enum BFontProgramFormat
{
    /// <summary>Glyph outlines in <c>glyf</c>.</summary>
    TrueType,

    /// <summary>Glyph outlines in a <c>CFF </c> table — an <c>OTTO</c> font.</summary>
    OpenTypeCff,
}

/// <summary>Bounds for one inspection. Nothing here means unlimited.</summary>
public sealed record BFontInspectionLimits
{
    public static BFontInspectionLimits Default { get; } = new();

    /// <summary>Largest program this will look at.</summary>
    public int MaxBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Largest table directory. A real font has tens of tables.</summary>
    public int MaxTables { get; init; } = 512;

    /// <summary>Largest segment or group count in a character map subtable.</summary>
    public int MaxCharacterMapSegments { get; init; } = 16384;

    /// <summary>Largest number of character-to-glyph entries recovered.</summary>
    public int MaxMappings { get; init; } = 65536;
}

/// <summary>
/// What one accepted font program says about which characters its glyphs spell.
/// </summary>
/// <remarks>
/// Deliberately narrow. The only question a caller asks a font it did not
/// provision is what its glyphs mean, so that is the only question this answers:
/// no outlines, no metrics, no names. A surface that answered more would have to
/// parse more, and every table parsed is attack surface bought for a use nobody
/// has.
/// </remarks>
public sealed class BFontProgramInspection
{
    private readonly Dictionary<int, int> _glyphForCodepoint;

    internal BFontProgramInspection(
        BFontProgramFormat format,
        Dictionary<int, int> glyphForCodepoint)
    {
        Format = format;
        _glyphForCodepoint = glyphForCodepoint;
    }

    public BFontProgramFormat Format { get; }

    /// <summary>How many character-to-glyph pairs were recovered.</summary>
    public int MappingCount => _glyphForCodepoint.Count;

    /// <summary>The glyph a character maps to, or 0 when the font maps none.</summary>
    public int GlyphForCodepoint(int codepoint) =>
        _glyphForCodepoint.TryGetValue(codepoint, out int glyph) ? glyph : 0;

    /// <summary>Every character-to-glyph pair, in no particular order.</summary>
    public IEnumerable<KeyValuePair<int, int>> Mappings => _glyphForCodepoint;
}

/// <summary>
/// Reads what an untrusted font program's glyphs mean, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is not the rasteriser's parser and must not become it.</strong>
/// <see cref="TrueTypeFont"/> reads fonts a caller provisioned — system faces, a
/// file named on a command line — and is written for that: it accepts a WOFF
/// container, follows a font collection, and reads a short table as zeros so a
/// slightly wrong font still draws. Every one of those is right for a face the
/// caller chose and wrong for a program that arrived inside a document somebody
/// sent them.
/// </para>
/// <para>
/// So this parses separately and refuses rather than repairs. The accepted tuple
/// is pinned (PDF roadmap §6.5): a bare sfnt, version <c>0x00010000</c> or
/// <c>OTTO</c>, whose declared tables all lie inside it and none of which names a
/// format outside the tuple. Variable fonts, CFF2, colour and bitmap glyphs,
/// Graphite and AAT, WOFF and WOFF2, and font collections are all refused by
/// name. Nothing here executes hinting bytecode or any other embedded program:
/// there is no interpreter in this file, which is the only guarantee of that
/// worth making.
/// </para>
/// <para>
/// It is stricter than recovering text strictly requires — a variable font's
/// character map is as valid as any other's — and that is deliberate. A pinned
/// tuple is a smaller thing to be wrong about than a policy of reading whatever
/// parses, and widening it later is a decision someone takes on purpose.
/// </para>
/// </remarks>
public static class BFontProgramInspector
{
    private const uint VersionTrueType = 0x00010000u;
    private const uint TagOtto = 0x4F54544Fu;       // 'OTTO'
    private const uint TagTrue = 0x74727565u;       // 'true', legacy Mac
    private const uint TagCollection = 0x74746366u; // 'ttcf'
    private const uint TagWoff = 0x774F4646u;       // 'wOFF'
    private const uint TagWoff2 = 0x774F4632u;      // 'wOF2'

    /// <summary>
    /// Tables whose presence puts a font outside the accepted tuple.
    /// </summary>
    /// <remarks>
    /// Each is the table that <em>defines</em> its format rather than one that
    /// merely accompanies it. <c>fvar</c> is what makes a font variable, so it is
    /// listed and <c>STAT</c> is not — <c>STAT</c> ships in plenty of static
    /// fonts, and refusing it would reject ordinary programs for a property they
    /// do not have.
    /// </remarks>
    private static readonly HashSet<string> ExcludedTables = new(StringComparer.Ordinal)
    {
        // Variable fonts: the outlines are not what the static tables say.
        "fvar", "gvar", "cvar",
        // CFF2, which is the variable-capable outline format.
        "CFF2",
        // Colour and bitmap glyphs.
        "COLR", "CPAL", "SVG ", "CBDT", "CBLC", "sbix", "EBDT", "EBLC",
        // Graphite.
        "Silf", "Glat", "Gloc", "Sill",
        // AAT.
        "morx", "mort", "kerx", "ankr", "bsln", "lcar",
    };

    /// <summary>
    /// Inspects a font program, or says why it was not accepted.
    /// </summary>
    public static bool TryInspect(
        ReadOnlySpan<byte> program,
        BFontInspectionLimits? limits,
        out BFontProgramInspection? inspection,
        out BFontProgramRejection rejection)
    {
        limits ??= BFontInspectionLimits.Default;
        inspection = null;

        if (program.Length < 12)
        {
            rejection = BFontProgramRejection.Empty;
            return false;
        }

        if (program.Length > limits.MaxBytes)
        {
            rejection = BFontProgramRejection.TooLarge;
            return false;
        }

        uint version = U32(program, 0);
        if (version is TagCollection or TagWoff or TagWoff2)
        {
            rejection = BFontProgramRejection.UnsupportedContainer;
            return false;
        }

        BFontProgramFormat format;
        if (version is VersionTrueType or TagTrue)
            format = BFontProgramFormat.TrueType;
        else if (version == TagOtto)
            format = BFontProgramFormat.OpenTypeCff;
        else
        {
            rejection = BFontProgramRejection.NotSfnt;
            return false;
        }

        int tableCount = U16(program, 4);
        if (tableCount == 0 || tableCount > limits.MaxTables)
        {
            rejection = BFontProgramRejection.MalformedDirectory;
            return false;
        }

        // Checked before the loop rather than inside it: the directory's size is
        // known from its count, and a count that cannot fit is malformed however
        // few of its records happen to be readable.
        long directoryEnd = 12L + (long)tableCount * 16L;
        if (directoryEnd > program.Length)
        {
            rejection = BFontProgramRejection.MalformedDirectory;
            return false;
        }

        var tables = new Dictionary<string, (int Offset, int Length)>(tableCount, StringComparer.Ordinal);
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            string tag = Tag(program, record);
            if (ExcludedTables.Contains(tag))
            {
                rejection = BFontProgramRejection.ExcludedTable;
                return false;
            }

            uint offset = U32(program, record + 8);
            uint length = U32(program, record + 12);

            // In long arithmetic, so a length near uint.MaxValue cannot wrap into
            // a small number that passes the bounds check.
            if ((long)offset + length > program.Length)
            {
                rejection = BFontProgramRejection.TableOutOfBounds;
                return false;
            }

            if (offset < directoryEnd)
            {
                // A table starting inside the directory overlaps the structure
                // that describes it, which no conforming font does.
                rejection = BFontProgramRejection.TableOutOfBounds;
                return false;
            }

            tables[tag] = ((int)offset, (int)length);
        }

        if (!tables.TryGetValue("cmap", out (int Offset, int Length) cmap))
        {
            rejection = BFontProgramRejection.NoCharacterMap;
            return false;
        }

        Dictionary<int, int>? mappings = ReadCharacterMap(
            program.Slice(cmap.Offset, cmap.Length),
            limits);

        if (mappings is null)
        {
            rejection = BFontProgramRejection.MalformedCharacterMap;
            return false;
        }

        inspection = new BFontProgramInspection(format, mappings);
        rejection = BFontProgramRejection.None;
        return true;
    }

    /// <summary>
    /// The best character map the table offers, or null when none is readable.
    /// </summary>
    /// <remarks>
    /// Every read below is against the <c>cmap</c> slice, so a subtable offset
    /// that points outside the table cannot reach the rest of the program.
    /// </remarks>
    private static Dictionary<int, int>? ReadCharacterMap(
        ReadOnlySpan<byte> cmap,
        BFontInspectionLimits limits)
    {
        if (cmap.Length < 4)
            return null;

        int subtableCount = U16(cmap, 2);
        if (subtableCount == 0 || 4L + (long)subtableCount * 8L > cmap.Length)
            return null;

        int best = -1;
        int bestRank = -1;
        for (int i = 0; i < subtableCount; i++)
        {
            int record = 4 + i * 8;
            int platform = U16(cmap, record);
            int encoding = U16(cmap, record + 2);
            uint offset = U32(cmap, record + 4);
            if (offset + 4L > cmap.Length)
                continue;

            int rank = Rank(platform, encoding);
            if (rank > bestRank)
            {
                bestRank = rank;
                best = (int)offset;
            }
        }

        if (best < 0)
            return null;

        var mappings = new Dictionary<int, int>();
        return ReadSubtable(cmap[best..], mappings, limits) ? mappings : null;
    }

    /// <summary>
    /// How much a subtable is preferred. Unicode full repertoire first, then
    /// Unicode BMP, then anything else that is readable at all.
    /// </summary>
    private static int Rank(int platform, int encoding) => (platform, encoding) switch
    {
        (3, 10) => 4,
        (0, 4) or (0, 6) => 4,
        (3, 1) => 3,
        (0, _) => 2,
        (3, 0) => 1,
        _ => 0,
    };

    private static bool ReadSubtable(
        ReadOnlySpan<byte> subtable,
        Dictionary<int, int> mappings,
        BFontInspectionLimits limits)
    {
        if (subtable.Length < 4)
            return false;

        return U16(subtable, 0) switch
        {
            0 => ReadFormat0(subtable, mappings, limits),
            4 => ReadFormat4(subtable, mappings, limits),
            6 => ReadFormat6(subtable, mappings, limits),
            12 => ReadFormat12(subtable, mappings, limits),
            _ => false,
        };
    }

    /// <summary>Byte encoding: 256 single-byte codes.</summary>
    private static bool ReadFormat0(
        ReadOnlySpan<byte> subtable,
        Dictionary<int, int> mappings,
        BFontInspectionLimits limits)
    {
        if (subtable.Length < 262)
            return false;

        for (int code = 0; code < 256; code++)
        {
            int glyph = subtable[6 + code];
            if (glyph != 0)
                Add(mappings, code, glyph, limits);
        }

        return true;
    }

    /// <summary>Segment mapping to delta values: the usual BMP subtable.</summary>
    private static bool ReadFormat4(
        ReadOnlySpan<byte> subtable,
        Dictionary<int, int> mappings,
        BFontInspectionLimits limits)
    {
        if (subtable.Length < 14)
            return false;

        int segCountX2 = U16(subtable, 6);
        if (segCountX2 == 0 || (segCountX2 & 1) != 0)
            return false;

        int segCount = segCountX2 / 2;
        if (segCount > limits.MaxCharacterMapSegments)
            return false;

        int endCodes = 14;
        int startCodes = endCodes + segCountX2 + 2;
        int idDeltas = startCodes + segCountX2;
        int idRangeOffsets = idDeltas + segCountX2;
        if ((long)idRangeOffsets + segCountX2 > subtable.Length)
            return false;

        for (int segment = 0; segment < segCount; segment++)
        {
            int end = U16(subtable, endCodes + segment * 2);
            int start = U16(subtable, startCodes + segment * 2);
            if (start > end)
                continue;

            int delta = U16(subtable, idDeltas + segment * 2);
            int rangeOffset = U16(subtable, idRangeOffsets + segment * 2);

            for (int code = start; code <= end; code++)
            {
                if (code == 0xFFFF)
                    continue;

                int glyph;
                if (rangeOffset == 0)
                {
                    glyph = (code + delta) & 0xFFFF;
                }
                else
                {
                    // The offset is stated from the range-offset slot itself,
                    // which is the one piece of this format everybody gets wrong.
                    int at = idRangeOffsets + segment * 2 + rangeOffset + (code - start) * 2;
                    if (at + 1 >= subtable.Length)
                        return false;

                    glyph = U16(subtable, at);
                    if (glyph != 0)
                        glyph = (glyph + delta) & 0xFFFF;
                }

                if (glyph != 0 && !Add(mappings, code, glyph, limits))
                    return true;
            }
        }

        return true;
    }

    /// <summary>Trimmed table mapping: a single contiguous run.</summary>
    private static bool ReadFormat6(
        ReadOnlySpan<byte> subtable,
        Dictionary<int, int> mappings,
        BFontInspectionLimits limits)
    {
        if (subtable.Length < 10)
            return false;

        int first = U16(subtable, 6);
        int count = U16(subtable, 8);
        if (count > limits.MaxCharacterMapSegments || 10L + (long)count * 2L > subtable.Length)
            return false;

        for (int i = 0; i < count; i++)
        {
            int glyph = U16(subtable, 10 + i * 2);
            if (glyph != 0 && !Add(mappings, first + i, glyph, limits))
                return true;
        }

        return true;
    }

    /// <summary>Segmented coverage: the subtable that reaches beyond the BMP.</summary>
    private static bool ReadFormat12(
        ReadOnlySpan<byte> subtable,
        Dictionary<int, int> mappings,
        BFontInspectionLimits limits)
    {
        if (subtable.Length < 16)
            return false;

        uint groups = U32(subtable, 12);
        if (groups > (uint)limits.MaxCharacterMapSegments || 16L + (long)groups * 12L > subtable.Length)
            return false;

        for (int i = 0; i < groups; i++)
        {
            int record = 16 + i * 12;
            uint start = U32(subtable, record);
            uint end = U32(subtable, record + 4);
            uint startGlyph = U32(subtable, record + 8);
            if (start > end || end > 0x10FFFF)
                continue;

            for (uint code = start; code <= end; code++)
            {
                long glyph = startGlyph + (code - start);
                if (glyph is <= 0 or > 0xFFFF)
                    continue;

                if (!Add(mappings, (int)code, (int)glyph, limits))
                    return true;
            }
        }

        return true;
    }

    /// <summary>Records a pair; false once the caller's budget is spent.</summary>
    private static bool Add(
        Dictionary<int, int> mappings,
        int codepoint,
        int glyph,
        BFontInspectionLimits limits)
    {
        if (mappings.Count >= limits.MaxMappings)
            return false;

        mappings[codepoint] = glyph;
        return true;
    }

    private static string Tag(ReadOnlySpan<byte> data, int offset) =>
        Encoding.ASCII.GetString(data.Slice(offset, 4));

    private static int U16(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 8) | data[offset + 1];

    private static uint U32(ReadOnlySpan<byte> data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];
}
