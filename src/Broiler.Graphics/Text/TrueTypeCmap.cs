using System;
using System.Collections.Generic;
using static Broiler.Graphics.Text.FontBinaryReader;

namespace Broiler.Graphics.Text;

/// <summary>Chooses and decodes TrueType character-to-glyph mappings.</summary>
internal sealed class TrueTypeCmap(byte[] data, uint tableOffset)
{
    private readonly byte[] _data = data;

    private int ReadUInt16(uint offset) => ReadU16(_data, (int)offset);
    private int ReadInt16(uint offset) => (short)ReadU16(_data, (int)offset);

    internal CmapLookup? Parse()
    {
        uint cmap = tableOffset;
        if (cmap == 0)
            return null;

        int numTables = ReadUInt16(cmap + 2);
        uint bestOffset = 0;
        int bestScore = -1;
        for (int i = 0; i < numTables; i++)
        {
            uint rec = cmap + 4 + (uint)(i * 8);
            int platformId = ReadUInt16(rec);
            int encodingId = ReadUInt16(rec + 2);
            uint subOffset = cmap + ReadU32(_data, (int)rec + 4);

            // Prefer full Unicode (3,10) > BMP Unicode (3,1) > Unicode (0,*) > symbol (3,0).
            int score = (platformId, encodingId) switch
            {
                (3, 10) => 5,
                (0, 6) => 4,
                (0, 4) => 4,
                (3, 1) => 3,
                (0, _) => 2,
                (3, 0) => 1,
                _ => 0,
            };
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = subOffset;
            }
        }

        if (bestOffset == 0)
            return null;

        int format = ReadUInt16(bestOffset);
        return format switch
        {
            0 => ParseCmapFormat0(bestOffset),
            4 => ParseCmapFormat4(bestOffset),
            6 => ParseCmapFormat6(bestOffset),
            12 => ParseCmapFormat12(bestOffset),
            _ => null,
        };
    }

    private CmapLookup ParseCmapFormat0(uint offset)
    {
        var map = new Dictionary<int, int>(256);
        for (int i = 0; i < 256; i++)
            map[i] = _data[offset + 6 + i];
        return new CmapLookup(map, null);
    }

    private CmapLookup ParseCmapFormat6(uint offset)
    {
        int first = ReadUInt16(offset + 6);
        int count = ReadUInt16(offset + 8);
        var map = new Dictionary<int, int>(count);
        for (int i = 0; i < count; i++)
            map[first + i] = ReadUInt16(offset + 10 + (uint)(i * 2));
        return new CmapLookup(map, null);
    }

    private CmapLookup ParseCmapFormat4(uint offset)
    {
        int segCountX2 = ReadUInt16(offset + 6);
        int segCount = segCountX2 / 2;
        uint endCodes = offset + 14;
        uint startCodes = endCodes + (uint)segCountX2 + 2; // +2 reservedPad
        uint idDeltas = startCodes + (uint)segCountX2;
        uint idRangeOffsets = idDeltas + (uint)segCountX2;

        var map = new Dictionary<int, int>();
        for (int s = 0; s < segCount; s++)
        {
            int end = ReadUInt16(endCodes + (uint)(s * 2));
            int start = ReadUInt16(startCodes + (uint)(s * 2));
            int idDelta = ReadInt16(idDeltas + (uint)(s * 2));
            int idRangeOffset = ReadUInt16(idRangeOffsets + (uint)(s * 2));

            if (start == 0xFFFF)
                continue;

            for (int code = start; code <= end && code != 0xFFFF; code++)
            {
                int glyph;
                if (idRangeOffset == 0)
                {
                    glyph = (code + idDelta) & 0xFFFF;
                }
                else
                {
                    uint glyphAddr = idRangeOffsets + (uint)(s * 2) + (uint)idRangeOffset + (uint)((code - start) * 2);
                    if (glyphAddr + 1 >= _data.Length)
                        continue;
                    int g = ReadUInt16(glyphAddr);
                    glyph = g == 0 ? 0 : (g + idDelta) & 0xFFFF;
                }
                if (glyph != 0)
                    map[code] = glyph;
            }
        }

        return new CmapLookup(map, null);
    }

    private CmapLookup ParseCmapFormat12(uint offset)
    {
        uint nGroups = ReadU32(_data, (int)offset + 12);
        var groups = new List<(uint start, uint end, uint startGlyph)>((int)Math.Min(nGroups, 100000));
        uint baseAddr = offset + 16;

        for (uint i = 0; i < nGroups; i++)
        {
            uint g = baseAddr + i * 12;

            if (g + 12 > _data.Length)
                break;

            uint startChar = ReadU32(_data, (int)g);
            uint endChar = ReadU32(_data, (int)g + 4);
            uint startGlyph = ReadU32(_data, (int)g + 8);

            groups.Add((startChar, endChar, startGlyph));
        }

        return new CmapLookup(null, groups);
    }

    /// <summary>Resolved cmap supporting either a sparse map or format-12 ranges.</summary>
    internal sealed class CmapLookup(Dictionary<int, int>? map, List<(uint start, uint end, uint startGlyph)>? groups)
    {
        public int Map(int codepoint)
        {
            if (map != null)
                return map.TryGetValue(codepoint, out int g) ? g : 0;

            if (groups != null)
            {
                uint cp = (uint)codepoint;

                foreach (var (start, end, startGlyph) in groups)
                {
                    if (cp >= start && cp <= end)
                        return (int)(startGlyph + (cp - start));
                }
            }

            return 0;
        }
    }
}
