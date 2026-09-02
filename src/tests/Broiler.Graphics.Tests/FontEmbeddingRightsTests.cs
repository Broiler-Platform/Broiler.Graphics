using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Covers reading a font's declared embedding permissions from <c>OS/2</c>.
/// </summary>
/// <remarks>
/// Reading, not enforcing. What a font declares is an input to somebody's
/// licence decision and never the decision itself, so these tests are about
/// reporting the file's claim faithfully — including when the file contradicts
/// itself, which real fonts do.
/// </remarks>
internal static class FontEmbeddingRightsTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("fsType bits are read as declared", FsTypeBitsAreRead));
        tests.Add(("Contradictory fsType bits take the strictest", ContradictionTakesTheStrictest));
        tests.Add(("A font with an OS/2 table reports its rights", FontWithOs2ReportsRights));
        tests.Add(("A font without OS/2 declares nothing", FontWithoutOs2IsUnknown));
        tests.Add(("A truncated OS/2 table declares nothing", TruncatedOs2IsUnknown));
    }

    private static void FsTypeBitsAreRead()
    {
        AssertEx.AreEqual(
            BFontEmbeddingPermission.Installable,
            BFontEmbeddingRights.FromFsType(0).Permission);
        AssertEx.AreEqual(
            BFontEmbeddingPermission.Restricted,
            BFontEmbeddingRights.FromFsType(0x0002).Permission);
        AssertEx.AreEqual(
            BFontEmbeddingPermission.PreviewAndPrint,
            BFontEmbeddingRights.FromFsType(0x0004).Permission);
        AssertEx.AreEqual(
            BFontEmbeddingPermission.Editable,
            BFontEmbeddingRights.FromFsType(0x0008).Permission);

        BFontEmbeddingRights strict = BFontEmbeddingRights.FromFsType(0x0008 | 0x0100 | 0x0200);
        AssertEx.IsTrue(strict.NoSubsetting, "Bit 8 forbids subsetting.");
        AssertEx.IsTrue(strict.BitmapEmbeddingOnly, "Bit 9 permits bitmaps only.");

        // Bit 0 is reserved, so a font that sets only it has still declared no
        // restriction — which is installable, not unknown.
        AssertEx.AreEqual(
            BFontEmbeddingPermission.Installable,
            BFontEmbeddingRights.FromFsType(0x0001).Permission);
    }

    private static void ContradictionTakesTheStrictest()
    {
        // The specification says these bits are mutually exclusive. Real fonts
        // set more than one anyway, and a file that contradicts itself about what
        // it permits is not one to give the benefit of the doubt to.
        AssertEx.AreEqual(
            BFontEmbeddingPermission.Restricted,
            BFontEmbeddingRights.FromFsType(0x0002 | 0x0008).Permission);
        AssertEx.AreEqual(
            BFontEmbeddingPermission.PreviewAndPrint,
            BFontEmbeddingRights.FromFsType(0x0004 | 0x0008).Permission);
    }

    private static void FontWithOs2ReportsRights()
    {
        TrueTypeFont font = Assert(Sfnt(os2FsType: 0x0004 | 0x0100));

        BFontEmbeddingRights rights = font.EmbeddingRights;
        AssertEx.AreEqual(BFontEmbeddingPermission.PreviewAndPrint, rights.Permission);
        AssertEx.IsTrue(rights.NoSubsetting, "The font forbids subsetting.");
        AssertEx.IsTrue(
            rights.Describe().Contains("no subsetting", StringComparison.Ordinal),
            "The description names the restriction: " + rights.Describe());
    }

    private static void FontWithoutOs2IsUnknown()
    {
        TrueTypeFont font = Assert(Sfnt(os2FsType: null));

        // Silence is not permission and not refusal. A caller that fails closed
        // treats it as a refusal; this only reports that the font said nothing.
        AssertEx.AreEqual(BFontEmbeddingPermission.Unknown, font.EmbeddingRights.Permission);
    }

    private static void TruncatedOs2IsUnknown()
    {
        TrueTypeFont font = Assert(Sfnt(os2FsType: 0x0002, truncateOs2: true));

        AssertEx.AreEqual(BFontEmbeddingPermission.Unknown, font.EmbeddingRights.Permission);
    }

    private static TrueTypeFont Assert(byte[] sfnt)
    {
        TrueTypeFont? font = TrueTypeFont.Load(sfnt);
        if (font is null)
            throw new AssertException("The assembled font did not load.");
        return font;
    }

    /// <summary>
    /// A minimal sfnt carrying <c>head</c> and optionally <c>OS/2</c>, assembled
    /// byte by byte. No font file is committed and none is read from the machine:
    /// the point is the table this reads, and a real font would bring a design
    /// and a licence with it for no benefit to the test.
    /// </summary>
    private static byte[] Sfnt(int? os2FsType, bool truncateOs2 = false)
    {
        var tables = new List<(string Tag, byte[] Data)>();

        // head: only unitsPerEm (offset 18) and indexToLocFormat (offset 50) are
        // read, so the rest is zeroes of the right length.
        byte[] head = new byte[54];
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);
        tables.Add(("head", head));

        // Load requires head and maxp and reads numGlyphs from the latter; a
        // font with neither is not an sfnt as far as the parser is concerned.
        byte[] maxp = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(maxp.AsSpan(0), 0x00010000);
        tables.Add(("maxp", maxp));

        if (os2FsType is int fsType)
        {
            byte[] os2 = new byte[truncateOs2 ? 8 : 96];
            if (!truncateOs2)
                BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(8), (ushort)fsType);
            tables.Add(("OS/2", os2));
        }

        tables.Sort(static (left, right) => string.CompareOrdinal(left.Tag, right.Tag));

        int directory = 12 + (tables.Count * 16);
        int total = directory;
        foreach ((_, byte[] data) in tables)
            total += (data.Length + 3) & ~3;

        byte[] sfnt = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(4), (ushort)tables.Count);

        int record = 12;
        int offset = directory;
        foreach ((string tag, byte[] data) in tables)
        {
            for (int i = 0; i < 4; i++)
                sfnt[record + i] = (byte)tag[i];
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 12), (uint)data.Length);
            data.CopyTo(sfnt.AsSpan(offset));

            record += 16;
            offset += (data.Length + 3) & ~3;
        }

        return sfnt;
    }
}
