using System;
using System.IO;
using System.Text;

namespace Broiler.Graphics;

/// <summary>
/// Reads the family and face names out of a font file's <c>name</c> table.
/// </summary>
/// <remarks>
/// <para>
/// A font picker has to show the names the host would show, and those live inside the files —
/// <c>arialbd.ttf</c> is "Arial" in the "Bold" face, and a filename parser guesses that at best.
/// Guessing is what <c>Broiler.Documents.Cli</c>'s <c>--font-dir</c> scan does, and its own remarks
/// call it a heuristic; this reads the answer instead.
/// </para>
/// <para>
/// Only the header, the table directory and the <c>name</c> table are read, so indexing a font
/// costs a few kilobytes rather than the whole file — some installed faces run past 20 MB. A
/// TrueType collection (<c>.ttc</c>) carries several fonts behind one header and every one of them
/// is read, because a collection is how a host usually ships a family's CJK faces.
/// </para>
/// </remarks>
internal static class FontNameTable
{
    /// <summary>The family name (name ID 1), which is what a font list shows.</summary>
    private const int NameIdFamily = 1;

    /// <summary>The face within the family (name ID 2): "Regular", "Bold Italic", and so on.</summary>
    private const int NameIdSubfamily = 2;

    /// <summary>A name table record, decoded far enough to rank it against the others.</summary>
    internal readonly record struct FaceName(string Family, string Subfamily);

    /// <summary>
    /// Reads every face in <paramref name="path"/>. Returns an empty array for anything that is not
    /// a font this can read, which includes a bitmap-only face, a truncated download and a file
    /// that merely carries a font extension.
    /// </summary>
    internal static FaceName[] Read(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096);
            return ReadFaces(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException or ArgumentException)
        {
            return [];
        }
    }

    private static FaceName[] ReadFaces(FileStream stream)
    {
        Span<byte> header = stackalloc byte[12];
        if (!TryReadAt(stream, 0, header))
            return [];

        // 'ttcf' fronts a collection: a count and then one sfnt offset per font.
        if (ReadTag(header) == 0x74746366u)
        {
            Span<byte> count = stackalloc byte[4];
            if (!TryReadAt(stream, 8, count))
                return [];

            uint fonts = ReadUInt32(count);
            if (fonts == 0 || fonts > 512)
                return [];

            var names = new FaceName[fonts];
            int found = 0;
            Span<byte> offset = stackalloc byte[4];
            for (uint index = 0; index < fonts; index++)
            {
                if (!TryReadAt(stream, 12 + index * 4, offset))
                    break;

                if (TryReadFace(stream, ReadUInt32(offset), out FaceName name))
                    names[found++] = name;
            }

            return found == names.Length ? names : names[..found];
        }

        return TryReadFace(stream, 0, out FaceName single) ? [single] : [];
    }

    private static bool TryReadFace(FileStream stream, long sfntOffset, out FaceName name)
    {
        name = default;
        if (!TryFindTable(stream, sfntOffset, 0x6E616D65u /* 'name' */, out long tableOffset, out uint tableLength))
            return false;

        // 6-byte header (format, count, string offset) plus 12 bytes per record. A table smaller
        // than that carries no records worth reading.
        if (tableLength < 6 || tableLength > 1 << 20)
            return false;

        byte[] table = new byte[tableLength];
        if (!TryReadAt(stream, tableOffset, table))
            return false;

        int records = ReadUInt16(table.AsSpan(2));
        int stringOffset = ReadUInt16(table.AsSpan(4));
        if (6 + (records * 12) > table.Length)
            return false;

        string? family = null;
        string? subfamily = null;
        int familyRank = int.MinValue;
        int subfamilyRank = int.MinValue;

        for (int index = 0; index < records; index++)
        {
            ReadOnlySpan<byte> record = table.AsSpan(6 + (index * 12), 12);
            int nameId = ReadUInt16(record[6..]);
            if (nameId != NameIdFamily && nameId != NameIdSubfamily)
                continue;

            int platform = ReadUInt16(record);
            int encoding = ReadUInt16(record[2..]);
            int language = ReadUInt16(record[4..]);
            int length = ReadUInt16(record[8..]);
            int offset = stringOffset + ReadUInt16(record[10..]);
            if (length == 0 || offset < 0 || offset + length > table.Length)
                continue;

            string? value = Decode(table.AsSpan(offset, length), platform, encoding);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            int rank = Rank(platform, language);
            if (nameId == NameIdFamily)
            {
                if (rank <= familyRank)
                    continue;

                family = value.Trim();
                familyRank = rank;
            }
            else
            {
                if (rank <= subfamilyRank)
                    continue;

                subfamily = value.Trim();
                subfamilyRank = rank;
            }
        }

        if (string.IsNullOrWhiteSpace(family))
            return false;

        name = new FaceName(family, subfamily ?? string.Empty);
        return true;
    }

    private static bool TryFindTable(FileStream stream, long sfntOffset, uint wanted, out long tableOffset, out uint tableLength)
    {
        tableOffset = 0;
        tableLength = 0;

        Span<byte> header = stackalloc byte[12];
        if (!TryReadAt(stream, sfntOffset, header))
            return false;

        uint version = ReadTag(header);
        // 0x00010000 is TrueType outlines, 'OTTO' is CFF, 'true'/'typ1' are the old Apple flavours.
        if (version is not (0x00010000u or 0x4F54544Fu or 0x74727565u or 0x74797031u))
            return false;

        int tables = ReadUInt16(header[4..]);
        if (tables <= 0 || tables > 512)
            return false;

        byte[] directory = new byte[tables * 16];
        if (!TryReadAt(stream, sfntOffset + 12, directory))
            return false;

        for (int index = 0; index < tables; index++)
        {
            ReadOnlySpan<byte> entry = directory.AsSpan(index * 16, 16);
            if (ReadTag(entry) != wanted)
                continue;

            tableOffset = ReadUInt32(entry[8..]);
            tableLength = ReadUInt32(entry[12..]);
            return tableOffset >= 0 && tableLength > 0;
        }

        return false;
    }

    /// <summary>
    /// How much a record's name is worth, so an English Windows name beats a Japanese one and any
    /// name beats none. Records are not ordered by usefulness, so every one is scored and the best
    /// kept.
    /// </summary>
    private static int Rank(int platform, int language) => platform switch
    {
        // Windows, English (any region): what a font list on this platform shows.
        3 when (language & 0x3FF) == 0x09 => 40,
        3 => 30,
        // Unicode platform records carry no language of their own.
        0 => 20,
        // Macintosh, English.
        1 when language == 0 => 15,
        _ => 10,
    };

    private static string? Decode(ReadOnlySpan<byte> value, int platform, int encoding)
    {
        // Platform 1 (Macintosh) with encoding 0 is MacRoman; everything else this reads is
        // UTF-16BE. Treating MacRoman as Latin-1 is exact for ASCII and close enough above it —
        // and a font whose only name is a non-ASCII MacRoman string is vanishingly rare next to
        // one carrying a Windows record too, which outranks it anyway.
        if (platform == 1 && encoding == 0)
            return Encoding.Latin1.GetString(value);

        if (value.Length % 2 != 0)
            return null;

        return Encoding.BigEndianUnicode.GetString(value);
    }

    private static bool TryReadAt(FileStream stream, long offset, Span<byte> destination)
    {
        if (offset < 0 || offset + destination.Length > stream.Length)
            return false;

        stream.Position = offset;
        return stream.ReadAtLeast(destination, destination.Length, throwOnEndOfStream: false) == destination.Length;
    }

    private static uint ReadTag(ReadOnlySpan<byte> value) => ReadUInt32(value);

    private static uint ReadUInt32(ReadOnlySpan<byte> value) =>
        ((uint)value[0] << 24) | ((uint)value[1] << 16) | ((uint)value[2] << 8) | value[3];

    private static int ReadUInt16(ReadOnlySpan<byte> value) => (value[0] << 8) | value[1];
}
