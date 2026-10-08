using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests.Shared;

/// <summary>
/// An Ahem-like TrueType font assembled byte by byte: 1000 units per em, ascender 800, descender
/// -200, and one glyph, "X", that is a full-em square from the descender to the ascender with a
/// full-em advance.
/// </summary>
/// <remarks>
/// It stands for a web font: a program no machine has installed, so a backend that draws it by
/// family name draws something else, and one that draws its outlines fills the whole em box with no
/// gap — which a pixel test can tell apart from any installed face's "X". No font file is committed
/// and none is read from the machine, for the same reason the other font tests build theirs.
/// </remarks>
internal static class SquareGlyphFont
{
    public const int UnitsPerEm = 1000;

    public static byte[] Build()
    {
        var tables = new List<(string Tag, byte[] Data)>();

        // head: unitsPerEm at 18, indexToLocFormat (0 = short offsets) at 50.
        byte[] head = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), UnitsPerEm);
        tables.Add(("head", head));

        // hhea: ascender at 4, descender at 6, numberOfHMetrics at 34.
        byte[] hhea = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(hhea.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), 800);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), -200);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), 2);
        tables.Add(("hhea", hhea));

        // maxp 0.5: numGlyphs at 4 — .notdef and the square.
        byte[] maxp = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(maxp.AsSpan(0), 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), 2);
        tables.Add(("maxp", maxp));

        // hmtx: both glyphs advance a full em.
        byte[] hmtx = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(0), UnitsPerEm);
        BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(4), UnitsPerEm);
        tables.Add(("hmtx", hmtx));

        // cmap: one (3,1) subtable, format 6, mapping 'X' to glyph 1.
        byte[] cmap = new byte[4 + 8 + 12];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(12), 6);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(14), 12);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(18), 'X');
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(22), 1);
        tables.Add(("cmap", cmap));

        // glyf: glyph 0 is empty; glyph 1 is one clockwise contour (0,-200) (0,800) (1000,800)
        // (1000,-200), every point on the curve and every coordinate a 16-bit delta.
        byte[] glyf = new byte[36];
        BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(0), 1);
        BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(2), 0);
        BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(4), -200);
        BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(6), UnitsPerEm);
        BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(8), 800);
        BinaryPrimitives.WriteUInt16BigEndian(glyf.AsSpan(10), 3);
        BinaryPrimitives.WriteUInt16BigEndian(glyf.AsSpan(12), 0);
        for (int i = 0; i < 4; i++)
            glyf[14 + i] = 0x01;
        short[] deltas = [0, 0, 1000, 0, -200, 1000, 0, -1000];
        for (int i = 0; i < deltas.Length; i++)
            BinaryPrimitives.WriteInt16BigEndian(glyf.AsSpan(18 + (i * 2)), deltas[i]);
        tables.Add(("glyf", glyf));

        // loca (short): glyph 0 spans nothing, glyph 1 the whole glyf table.
        byte[] loca = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(4), (ushort)(glyf.Length / 2));
        tables.Add(("loca", loca));

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
