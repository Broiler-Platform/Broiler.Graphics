using System;
using System.Collections.Generic;
using System.Text;

namespace Broiler.Graphics.Tests;

/// <summary>
/// The read-safe font-program inspector: what it accepts, and the shapes of
/// malformed program it refuses rather than repairs (PDF roadmap §6.5).
/// </summary>
internal static class FontProgramInspectorTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("A bare sfnt is accepted and its characters recovered", BareSfntIsAccepted));
        tests.Add(("An OTTO font is accepted as CFF", OttoIsAcceptedAsCff));
        tests.Add(("Characters beyond the BMP are recovered", BeyondBmpIsRecovered));
        tests.Add(("A container outside the tuple is refused", ContainerOutsideTupleIsRefused));
        tests.Add(("Something that is not a font is refused", NotAFontIsRefused));
        tests.Add(("A table outside the tuple is refused", TableOutsideTupleIsRefused));
        tests.Add(("A static font carrying STAT is still accepted", StatIsStillAccepted));
        tests.Add(("A table running past the end is refused", TablePastEndIsRefused));
        tests.Add(("A length that would overflow is refused", OverflowingLengthIsRefused));
        tests.Add(("A table starting inside the directory is refused", TableInDirectoryIsRefused));
        tests.Add(("A directory that cannot fit is refused", UnfittableDirectoryIsRefused));
        tests.Add(("A font with no character map is refused", NoCharacterMapIsRefused));
        tests.Add(("A character map pointing outside itself is refused", CharacterMapOutsideItselfIsRefused));
        tests.Add(("A program larger than the budget is refused", OversizeProgramIsRefused));
        tests.Add(("The mapping budget is honoured", MappingBudgetIsHonoured));
        tests.Add(("An empty program is refused", EmptyProgramIsRefused));
        tests.Add(("Every mapping is reachable", EveryMappingIsReachable));
    }

    private const uint VersionTrueType = 0x00010000u;
    private const uint TagOtto = 0x4F54544Fu;

    /// <summary>Lays out a bare sfnt around the tables it is given.</summary>
    private static byte[] Sfnt(uint version, params (string Tag, byte[] Data)[] tables)
    {
        int directory = 12 + tables.Length * 16;
        int total = directory;
        foreach ((_, byte[] data) in tables)
            total += data.Length;

        var bytes = new byte[total];
        WriteU32(bytes, 0, version);
        WriteU16(bytes, 4, tables.Length);

        int at = directory;
        for (int i = 0; i < tables.Length; i++)
        {
            int record = 12 + i * 16;
            Encoding.ASCII.GetBytes(tables[i].Tag.PadRight(4)).CopyTo(bytes, record);
            WriteU32(bytes, record + 8, (uint)at);
            WriteU32(bytes, record + 12, (uint)tables[i].Data.Length);
            tables[i].Data.CopyTo(bytes, at);
            at += tables[i].Data.Length;
        }

        return bytes;
    }

    /// <summary>A cmap with one format-4 subtable mapping A, B and C to 1, 2 and 3.</summary>
    private static byte[] CmapFormat4()
    {
        var subtable = new byte[32];
        WriteU16(subtable, 0, 4);           // format
        WriteU16(subtable, 2, 32);          // length
        WriteU16(subtable, 6, 4);           // segCountX2, so two segments
        WriteU16(subtable, 14, 0x0043);     // endCode[0], 'C'
        WriteU16(subtable, 16, 0xFFFF);     // endCode[1], the required terminator
        WriteU16(subtable, 20, 0x0041);     // startCode[0], 'A'
        WriteU16(subtable, 22, 0xFFFF);     // startCode[1]
        WriteU16(subtable, 24, 0xFFC0);     // idDelta[0]: 'A' + this == 1
        WriteU16(subtable, 26, 1);          // idDelta[1]

        return Cmap(3, 1, subtable);
    }

    /// <summary>A cmap with one format-12 subtable mapping one plane-1 character.</summary>
    private static byte[] CmapFormat12(int codepoint, int glyph)
    {
        var subtable = new byte[28];
        WriteU16(subtable, 0, 12);
        WriteU32(subtable, 4, 28);          // length
        WriteU32(subtable, 12, 1);          // one group
        WriteU32(subtable, 16, (uint)codepoint);
        WriteU32(subtable, 20, (uint)codepoint);
        WriteU32(subtable, 24, (uint)glyph);

        return Cmap(3, 10, subtable);
    }

    private static byte[] Cmap(int platform, int encoding, byte[] subtable)
    {
        var cmap = new byte[12 + subtable.Length];
        WriteU16(cmap, 2, 1);               // one subtable
        WriteU16(cmap, 4, platform);
        WriteU16(cmap, 6, encoding);
        WriteU32(cmap, 8, 12);              // subtable offset
        subtable.CopyTo(cmap, 12);
        return cmap;
    }

    private static BFontProgramInspection Accept(byte[] program)
    {
        AssertEx.IsTrue(
            BFontProgramInspector.TryInspect(program, null, out BFontProgramInspection? inspection, out _),
            "Expected the program to be accepted.");
        AssertEx.IsTrue(inspection is not null, "An accepted program yields an inspection.");
        return inspection!;
    }

    private static BFontProgramRejection Refuse(byte[] program)
    {
        AssertEx.IsFalse(
            BFontProgramInspector.TryInspect(program, null, out BFontProgramInspection? inspection, out var rejection),
            "Expected the program to be refused.");
        AssertEx.IsTrue(inspection is null, "A refused program yields no inspection.");
        return rejection;
    }

    private static void WriteU16(byte[] bytes, int at, int value)
    {
        bytes[at] = (byte)(value >> 8);
        bytes[at + 1] = (byte)value;
    }

    private static void WriteU32(byte[] bytes, int at, uint value)
    {
        bytes[at] = (byte)(value >> 24);
        bytes[at + 1] = (byte)(value >> 16);
        bytes[at + 2] = (byte)(value >> 8);
        bytes[at + 3] = (byte)value;
    }

    private static void BareSfntIsAccepted()
    {
        BFontProgramInspection font = Accept(Sfnt(
            VersionTrueType,
            ("head", new byte[54]),
            ("cmap", CmapFormat4())));

        AssertEx.AreEqual(BFontProgramFormat.TrueType, font.Format);
        AssertEx.AreEqual(1, font.GlyphForCodepoint('A'));
        AssertEx.AreEqual(2, font.GlyphForCodepoint('B'));
        AssertEx.AreEqual(3, font.GlyphForCodepoint('C'));
        AssertEx.AreEqual(0, font.GlyphForCodepoint('D'));
    }

    private static void OttoIsAcceptedAsCff()
    {
        BFontProgramInspection font = Accept(Sfnt(TagOtto, ("cmap", CmapFormat4())));

        AssertEx.AreEqual(BFontProgramFormat.OpenTypeCff, font.Format);
    }

    private static void BeyondBmpIsRecovered()
    {
        BFontProgramInspection font = Accept(Sfnt(
            VersionTrueType,
            ("cmap", CmapFormat12(0x1F600, 42))));

        AssertEx.AreEqual(42, font.GlyphForCodepoint(0x1F600));
    }

    private static void ContainerOutsideTupleIsRefused()
    {
        foreach (string signature in new[] { "wOFF", "wOF2", "ttcf" })
            ContainerOutsideTupleIsRefused(signature);
    }

    private static void ContainerOutsideTupleIsRefused(string signature)
    {
        // The rasteriser accepts all three, because a caller who names a .woff on
        // a command line meant it. A program that arrived inside a document did
        // not come with that assurance.
        byte[] program = Sfnt(VersionTrueType, ("cmap", CmapFormat4()));
        Encoding.ASCII.GetBytes(signature).CopyTo(program, 0);

        AssertEx.AreEqual(BFontProgramRejection.UnsupportedContainer, Refuse(program));
    }

    private static void NotAFontIsRefused()
    {
        AssertEx.AreEqual(
            BFontProgramRejection.NotSfnt,
            Refuse(Encoding.ASCII.GetBytes("%PDF-1.7 and then some more bytes")));
    }

    private static void TableOutsideTupleIsRefused()
    {
        // variable, CFF2, colour, bitmap, Graphite, AAT
        foreach (string tag in new[] { "fvar", "CFF2", "COLR", "sbix", "Silf", "morx" })
            TableOutsideTupleIsRefused(tag);
    }

    private static void TableOutsideTupleIsRefused(string tag)
    {
        AssertEx.AreEqual(
            BFontProgramRejection.ExcludedTable,
            Refuse(Sfnt(VersionTrueType, (tag, new byte[8]), ("cmap", CmapFormat4()))));
    }

    private static void StatIsStillAccepted()
    {
        // STAT ships in plenty of fonts that are not variable, so excluding it
        // would refuse ordinary programs for a property they do not have. fvar is
        // what makes a font variable and fvar is what the list names.
        BFontProgramInspection font = Accept(Sfnt(
            VersionTrueType,
            ("STAT", new byte[8]),
            ("cmap", CmapFormat4())));

        AssertEx.AreEqual(1, font.GlyphForCodepoint('A'));
    }

    private static void TablePastEndIsRefused()
    {
        byte[] program = Sfnt(VersionTrueType, ("cmap", CmapFormat4()));
        WriteU32(program, 12 + 12, (uint)program.Length);   // length beyond the file

        AssertEx.AreEqual(BFontProgramRejection.TableOutOfBounds, Refuse(program));
    }

    private static void OverflowingLengthIsRefused()
    {
        // The bounds check is done in long arithmetic. In int arithmetic this
        // length wraps to a small number and the table appears to fit.
        byte[] program = Sfnt(VersionTrueType, ("cmap", CmapFormat4()));
        WriteU32(program, 12 + 12, 0xFFFFFFF0u);

        AssertEx.AreEqual(BFontProgramRejection.TableOutOfBounds, Refuse(program));
    }

    private static void TableInDirectoryIsRefused()
    {
        // It would overlap the structure that describes it, which no conforming
        // font does and which a crafted one uses to make one byte range mean two
        // different things.
        byte[] program = Sfnt(VersionTrueType, ("cmap", CmapFormat4()));
        WriteU32(program, 12 + 8, 4);

        AssertEx.AreEqual(BFontProgramRejection.TableOutOfBounds, Refuse(program));
    }

    private static void UnfittableDirectoryIsRefused()
    {
        byte[] program = Sfnt(VersionTrueType, ("cmap", CmapFormat4()));
        WriteU16(program, 4, 4096);   // far more records than there are bytes

        AssertEx.AreEqual(BFontProgramRejection.MalformedDirectory, Refuse(program));
    }

    private static void NoCharacterMapIsRefused()
    {
        // Not a failure to parse: a program whose glyphs spell nothing this build
        // can read answers no question a caller asked it.
        AssertEx.AreEqual(
            BFontProgramRejection.NoCharacterMap,
            Refuse(Sfnt(VersionTrueType, ("head", new byte[54]))));
    }

    private static void CharacterMapOutsideItselfIsRefused()
    {
        byte[] cmap = CmapFormat4();
        WriteU32(cmap, 8, (uint)cmap.Length + 64);   // subtable offset past the table

        AssertEx.AreEqual(
            BFontProgramRejection.MalformedCharacterMap,
            Refuse(Sfnt(VersionTrueType, ("cmap", cmap))));
    }

    private static void OversizeProgramIsRefused()
    {
        byte[] program = Sfnt(VersionTrueType, ("cmap", CmapFormat4()));
        var limits = new BFontInspectionLimits { MaxBytes = program.Length - 1 };

        AssertEx.IsFalse(
            BFontProgramInspector.TryInspect(program, limits, out _, out var rejection),
            "An oversize program is refused.");
        AssertEx.AreEqual(BFontProgramRejection.TooLarge, rejection);
    }

    private static void MappingBudgetIsHonoured()
    {
        byte[] program = Sfnt(VersionTrueType, ("cmap", CmapFormat4()));
        var limits = new BFontInspectionLimits { MaxMappings = 2 };

        AssertEx.IsTrue(
            BFontProgramInspector.TryInspect(program, limits, out BFontProgramInspection? font, out _),
            "The program is accepted within the budget.");
        AssertEx.AreEqual(2, font!.MappingCount);
    }

    private static void EmptyProgramIsRefused()
    {
        AssertEx.AreEqual(BFontProgramRejection.Empty, Refuse([]));
        AssertEx.AreEqual(BFontProgramRejection.Empty, Refuse(new byte[11]));
    }

    private static void EveryMappingIsReachable()
    {
        BFontProgramInspection font = Accept(Sfnt(VersionTrueType, ("cmap", CmapFormat4())));

        var recovered = new Dictionary<int, int>();
        foreach (KeyValuePair<int, int> mapping in font.Mappings)
            recovered[mapping.Key] = mapping.Value;

        AssertEx.AreEqual(3, recovered.Count);
        AssertEx.AreEqual(1, recovered['A']);
    }
}
