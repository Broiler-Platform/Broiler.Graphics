#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Broiler.Graphics;

/// <summary>
/// Minimal managed TrueType/OpenType (glyf-outline) font parser.  Reads the
/// tables required to measure and rasterise text: <c>head</c>, <c>maxp</c>,
/// <c>hhea</c>, <c>hmtx</c>, <c>cmap</c>, <c>loca</c> and <c>glyf</c>.
/// <para>
/// Only the glyf outline format is supported (the common case for .ttf and
/// many .otf files).  CFF/PostScript outlines are not handled; such fonts
/// load but produce no glyph contours.
/// </para>
/// </summary>
public sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, uint> _tables;
    private readonly uint _glyfOffset;
    private readonly uint[] _loca;          // glyph data offsets into glyf, length numGlyphs + 1
    private readonly int _numGlyphs;
    private readonly int _numHMetrics;
    private readonly uint _hmtxOffset;
    private CmapLookup? _cmap;
    private GsubTable? _gsub;
    private bool _gsubParsed;
    private GposTable? _gpos;
    private bool _gposParsed;
    private ClassDefTable? _gdefClasses;
    private bool _gdefParsed;
    private CffFont? _cff;
    private bool _cffParsed;

    public int UnitsPerEm { get; }
    public int Ascender { get; }
    public int Descender { get; }       // typically negative

    /// <summary>True when the font has rasterisable outlines (glyf or CFF).</summary>
    public bool HasOutlines => (_glyfOffset != 0 && _loca.Length > 1) || GetCff() != null;

    private TrueTypeFont(byte[] data, Dictionary<string, uint> tables)
    {
        _data = data;
        _tables = tables;

        uint head = tables.GetValueOrDefault("head");
        UnitsPerEm = head != 0 ? ReadUInt16(head + 18) : 1000;
        if (UnitsPerEm <= 0) UnitsPerEm = 1000;
        int indexToLocFormat = head != 0 ? ReadInt16(head + 50) : 0;

        uint hhea = tables.GetValueOrDefault("hhea");
        Ascender = hhea != 0 ? ReadInt16(hhea + 4) : (int)(UnitsPerEm * 0.8);
        Descender = hhea != 0 ? ReadInt16(hhea + 6) : -(int)(UnitsPerEm * 0.2);
        _numHMetrics = hhea != 0 ? ReadUInt16(hhea + 34) : 0;

        uint maxp = tables.GetValueOrDefault("maxp");
        _numGlyphs = maxp != 0 ? ReadUInt16(maxp + 4) : 0;

        _hmtxOffset = tables.GetValueOrDefault("hmtx");
        _glyfOffset = tables.GetValueOrDefault("glyf");

        uint locaOffset = tables.GetValueOrDefault("loca");
        _loca = ReadLoca(locaOffset, indexToLocFormat, _numGlyphs);
    }

    /// <summary>Parses a font file's bytes; returns <c>null</c> when the data is not a recognised sfnt container.</summary>
    public static TrueTypeFont? Load(byte[] data)
    {
        if (data == null || data.Length < 12)
            return null;

        // WOFF 1.0 container: decode to a raw sfnt first (e.g. @font-face .woff).
        if (WoffDecoder.IsWoff(data))
        {
            data = WoffDecoder.Decode(data);
            if (data == null || data.Length < 12)
                return null;
        }

        uint version = ReadU32(data, 0);
        // 0x00010000 = TrueType, 'true'/'typ1' = legacy Mac, 'OTTO' = CFF-based OpenType.
        // 'ttcf' = font collection (use the first face).
        uint tableDirOffset = 0;
        if (version == 0x74746366u) // 'ttcf'
        {
            if (data.Length < 16) return null;
            tableDirOffset = ReadU32(data, 12); // offset of first font
        }

        int numTables = ReadU16(data, (int)tableDirOffset + 4);
        var tables = new Dictionary<string, uint>(numTables, StringComparer.Ordinal);
        int recordBase = (int)tableDirOffset + 12;
        for (int i = 0; i < numTables; i++)
        {
            int rec = recordBase + i * 16;
            if (rec + 16 > data.Length) break;
            string tag = System.Text.Encoding.ASCII.GetString(data, rec, 4);
            uint offset = ReadU32(data, rec + 8);
            tables[tag] = offset;
        }

        if (!tables.ContainsKey("head") || !tables.ContainsKey("maxp"))
            return null;

        return new TrueTypeFont(data, tables);
    }

    /// <summary>Maps a Unicode code point to a glyph index (0 = .notdef / missing).</summary>
    public int GetGlyphIndex(int codepoint)
    {
        _cmap ??= BuildCmap();
        return _cmap?.Map(codepoint) ?? 0;
    }

    /// <summary>Horizontal advance for a glyph, in font design units.</summary>
    public int GetAdvanceWidth(int glyphIndex)
    {
        if (_hmtxOffset == 0 || _numHMetrics == 0)
            return UnitsPerEm / 2;

        int index = Math.Min(glyphIndex, _numHMetrics - 1);
        return ReadUInt16(_hmtxOffset + (uint)(index * 4));
    }

    /// <summary>
    /// Returns the glyph outline as a list of closed contours in font design
    /// units (y axis points up, per the TrueType convention).  Quadratic
    /// segments are flattened to polylines.  Empty glyphs (e.g. spaces) return
    /// an empty list.
    /// </summary>
    public List<PointF[]> GetGlyphContours(int glyphIndex)
    {
        // glyf outlines take precedence; otherwise use CFF (PostScript) outlines.
        if (_glyfOffset != 0 && _loca.Length > 1)
        {
            var result = new List<PointF[]>();
            AppendGlyphContours(glyphIndex, result, 0f, 0f, 1f, 0f, 0f, 1f, 0);
            return result;
        }

        var cff = GetCff();
        return cff != null ? cff.GetGlyphOutline(glyphIndex) : [];
    }

    private CffFont? GetCff()
    {
        if (_cffParsed)
            return _cff;
        _cffParsed = true;
        uint cffOffset = _tables.GetValueOrDefault("CFF ");
        if (cffOffset != 0)
        {
            try
            {
                var font = new CffFont(_data, (int)cffOffset, UnitsPerEm);
                if (font.Ok)
                    _cff = font;
            }
            catch { _cff = null; }
        }
        return _cff;
    }

    // ── Glyph outline parsing ─────────────────────────────────────────────

    private void AppendGlyphContours(
        int glyphIndex, List<PointF[]> output,
        float dx, float dy, float a, float b, float c, float d, int depth)
    {
        if (depth > 8 || glyphIndex < 0 || glyphIndex >= _numGlyphs || _glyfOffset == 0)
            return;

        uint start = _glyfOffset + _loca[glyphIndex];
        uint end = _glyfOffset + _loca[glyphIndex + 1];
        if (end <= start)
            return; // empty glyph

        int numberOfContours = ReadInt16(start);
        uint p = start + 10; // skip numberOfContours(2) + bbox(8)

        if (numberOfContours < 0)
        {
            AppendCompositeGlyph(p, output, dx, dy, a, b, c, d, depth);
            return;
        }

        // Simple glyph.
        var endPts = new int[numberOfContours];
        for (int i = 0; i < numberOfContours; i++)
        {
            endPts[i] = ReadUInt16(p);
            p += 2;
        }

        int numPoints = numberOfContours > 0 ? endPts[numberOfContours - 1] + 1 : 0;
        if (numPoints <= 0)
            return;

        int instructionLength = ReadUInt16(p);
        p += 2 + (uint)instructionLength;

        // Flags (with repeat compression).
        var flags = new byte[numPoints];
        for (int i = 0; i < numPoints;)
        {
            byte flag = _data[p++];
            flags[i++] = flag;
            if ((flag & 0x08) != 0) // REPEAT_FLAG
            {
                int repeat = _data[p++];
                for (int r = 0; r < repeat && i < numPoints; r++)
                    flags[i++] = flag;
            }
        }

        // X coordinates (deltas).
        var xs = new int[numPoints];
        int x = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte flag = flags[i];
            if ((flag & 0x02) != 0) // X_SHORT_VECTOR
            {
                int delta = _data[p++];
                x += (flag & 0x10) != 0 ? delta : -delta; // POSITIVE_X_SHORT
            }
            else if ((flag & 0x10) == 0) // not SAME → signed 16-bit delta
            {
                x += ReadInt16(p);
                p += 2;
            }
            xs[i] = x;
        }

        // Y coordinates (deltas).
        var ys = new int[numPoints];
        int y = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte flag = flags[i];
            if ((flag & 0x04) != 0) // Y_SHORT_VECTOR
            {
                int delta = _data[p++];
                y += (flag & 0x20) != 0 ? delta : -delta; // POSITIVE_Y_SHORT
            }
            else if ((flag & 0x20) == 0)
            {
                y += ReadInt16(p);
                p += 2;
            }
            ys[i] = y;
        }

        // Split into contours and flatten.
        int pointStart = 0;
        for (int ci = 0; ci < numberOfContours; ci++)
        {
            int pointEnd = endPts[ci];
            int count = pointEnd - pointStart + 1;
            if (count > 0)
            {
                var pts = new List<(PointF p, bool on)>(count);
                for (int i = pointStart; i <= pointEnd; i++)
                {
                    float tx = a * xs[i] + c * ys[i] + dx;
                    float ty = b * xs[i] + d * ys[i] + dy;
                    pts.Add((new PointF(tx, ty), (flags[i] & 0x01) != 0));
                }

                var flat = FlattenContour(pts);
                if (flat.Length >= 2)
                    output.Add(flat);
            }
            pointStart = pointEnd + 1;
        }
    }

    private void AppendCompositeGlyph(
        uint p, List<PointF[]> output,
        float pdx, float pdy, float pa, float pb, float pc, float pd, int depth)
    {
        const int ARG_1_AND_2_ARE_WORDS = 0x0001;
        const int ARGS_ARE_XY_VALUES = 0x0002;
        const int WE_HAVE_A_SCALE = 0x0008;
        const int MORE_COMPONENTS = 0x0020;
        const int WE_HAVE_AN_X_AND_Y_SCALE = 0x0040;
        const int WE_HAVE_A_TWO_BY_TWO = 0x0080;

        while (true)
        {
            int flags = ReadUInt16(p); p += 2;
            int componentGlyph = ReadUInt16(p); p += 2;

            float arg1, arg2;
            if ((flags & ARG_1_AND_2_ARE_WORDS) != 0)
            {
                arg1 = ReadInt16(p); p += 2;
                arg2 = ReadInt16(p); p += 2;
            }
            else
            {
                arg1 = (sbyte)_data[p++];
                arg2 = (sbyte)_data[p++];
            }

            float a = 1f, b = 0f, c = 0f, d = 1f;
            if ((flags & WE_HAVE_A_SCALE) != 0)
            {
                a = d = ReadF2Dot14(p); p += 2;
            }
            else if ((flags & WE_HAVE_AN_X_AND_Y_SCALE) != 0)
            {
                a = ReadF2Dot14(p); p += 2;
                d = ReadF2Dot14(p); p += 2;
            }
            else if ((flags & WE_HAVE_A_TWO_BY_TWO) != 0)
            {
                a = ReadF2Dot14(p); p += 2;
                b = ReadF2Dot14(p); p += 2;
                c = ReadF2Dot14(p); p += 2;
                d = ReadF2Dot14(p); p += 2;
            }

            // Offset (only XY-value args are supported; point matching is ignored).
            float dx = 0f, dy = 0f;
            if ((flags & ARGS_ARE_XY_VALUES) != 0)
            {
                dx = arg1;
                dy = arg2;
            }

            // Compose child transform with parent transform.
            float ca = pa * a + pc * b;
            float cb = pb * a + pd * b;
            float cc = pa * c + pc * d;
            float cd = pb * c + pd * d;
            float cdx = pa * dx + pc * dy + pdx;
            float cdy = pb * dx + pd * dy + pdy;

            AppendGlyphContours(componentGlyph, output, cdx, cdy, ca, cb, cc, cd, depth + 1);

            if ((flags & MORE_COMPONENTS) == 0)
                break;
        }
    }

    private static PointF[] FlattenContour(List<(PointF p, bool on)> raw)
    {
        int len = raw.Count;
        if (len == 0)
            return Array.Empty<PointF>();

        // Insert implied on-curve points between consecutive off-curve points.
        var norm = new List<(PointF p, bool on)>(len * 2);
        for (int i = 0; i < len; i++)
        {
            var cur = raw[i];
            norm.Add(cur);
            var next = raw[(i + 1) % len];
            if (!cur.on && !next.on)
                norm.Add((Midpoint(cur.p, next.p), true));
        }

        int firstOn = norm.FindIndex(q => q.on);
        if (firstOn < 0)
            return Array.Empty<PointF>(); // degenerate (all off-curve)

        // Rotate so iteration starts on an on-curve point.
        int count = norm.Count;
        var output = new List<PointF>(count * 4);
        PointF startPoint = norm[firstOn].p;
        output.Add(startPoint);

        int idx = 1;
        PointF last = startPoint;
        while (idx <= count)
        {
            var q = norm[(firstOn + idx) % count];
            if (q.on)
            {
                output.Add(q.p);
                last = q.p;
                idx++;
            }
            else
            {
                PointF control = q.p;
                PointF endPoint = norm[(firstOn + idx + 1) % count].p;
                FlattenQuadratic(output, last, control, endPoint);
                last = endPoint;
                idx += 2;
            }
        }

        return output.ToArray();
    }

    private static void FlattenQuadratic(List<PointF> output, PointF p0, PointF c, PointF p1)
    {
        const int segments = 8;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float mt = 1f - t;
            float x = mt * mt * p0.X + 2f * mt * t * c.X + t * t * p1.X;
            float y = mt * mt * p0.Y + 2f * mt * t * c.Y + t * t * p1.Y;
            output.Add(new PointF(x, y));
        }
    }

    private static PointF Midpoint(PointF a, PointF b) =>
        new((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);

    // ── cmap parsing ──────────────────────────────────────────────────────

    private CmapLookup? BuildCmap()
    {
        uint cmap = _tables.GetValueOrDefault("cmap");
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
            if (g + 12 > _data.Length) break;
            uint startChar = ReadU32(_data, (int)g);
            uint endChar = ReadU32(_data, (int)g + 4);
            uint startGlyph = ReadU32(_data, (int)g + 8);
            groups.Add((startChar, endChar, startGlyph));
        }
        return new CmapLookup(null, groups);
    }

    // ── Low-level readers (big-endian) ────────────────────────────────────

    private uint[] ReadLoca(uint locaOffset, int format, int numGlyphs)
    {
        if (locaOffset == 0 || numGlyphs <= 0)
            return Array.Empty<uint>();

        var loca = new uint[numGlyphs + 1];
        if (format == 0) // short: offsets are stored /2
        {
            for (int i = 0; i <= numGlyphs; i++)
                loca[i] = (uint)ReadUInt16(locaOffset + (uint)(i * 2)) * 2u;
        }
        else // long
        {
            for (int i = 0; i <= numGlyphs; i++)
                loca[i] = ReadU32(_data, (int)locaOffset + i * 4);
        }
        return loca;
    }

    private int ReadUInt16(uint offset) => ReadU16(_data, (int)offset);
    private int ReadInt16(uint offset) => (short)ReadU16(_data, (int)offset);
    private float ReadF2Dot14(uint offset) => (short)ReadU16(_data, (int)offset) / 16384f;

    private static int ReadU16(byte[] data, int offset)
    {
        if (offset < 0 || offset + 1 >= data.Length) return 0;
        return (data[offset] << 8) | data[offset + 1];
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        if (offset < 0 || offset + 3 >= data.Length) return 0;
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
             | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    public static TrueTypeFont? LoadFromFile(string path)
    {
        try
        {
            return File.Exists(path) ? Load(File.ReadAllBytes(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // ── GSUB (glyph substitution) ─────────────────────────────────────────

    /// <summary>
    /// Apply a single-substitution GSUB feature (e.g. <c>"init"</c>, <c>"medi"</c>,
    /// <c>"fina"</c>, <c>"isol"</c>, <c>"liga"</c>) to one glyph, returning the
    /// substituted glyph or the input glyph when the feature does not apply.
    /// </summary>
    public int ApplySingleSubstitution(string featureTag, int glyph)
    {
        var g = GetGsub();
        return g != null ? g.ApplySingle(featureTag, glyph) : glyph;
    }

    /// <summary>
    /// Try to substitute a ligature (GSUB type 4) for the glyph sequence starting
    /// at <paramref name="pos"/> under the given feature (e.g. <c>"rlig"</c>,
    /// <c>"liga"</c>).  On success returns the ligature glyph and the number of
    /// input glyphs it consumes.
    /// </summary>
    public bool TryApplyLigature(string featureTag, IReadOnlyList<int> glyphs, int pos, out int ligature, out int componentCount)
    {
        var g = GetGsub();
        if (g != null)
            return g.TryLigate(featureTag, glyphs, pos, out ligature, out componentCount);
        ligature = 0;
        componentCount = 0;
        return false;
    }

    private GsubTable? GetGsub()
    {
        if (_gsubParsed)
            return _gsub;
        _gsubParsed = true;
        try { _gsub = ParseGsub(); }
        catch { _gsub = null; }
        return _gsub;
    }

    private GsubTable? ParseGsub()
    {
        uint gsub = _tables.GetValueOrDefault("GSUB");
        if (gsub == 0)
            return null;

        uint scriptListOff = gsub + (uint)ReadUInt16(gsub + 4);
        uint featureListOff = gsub + (uint)ReadUInt16(gsub + 6);
        uint lookupListOff = gsub + (uint)ReadUInt16(gsub + 8);

        // Collect the feature indices referenced by every script/langsys; we
        // only ever query features by tag, so gathering all is harmless and
        // robust across fonts that place Arabic features under 'arab', 'DFLT',
        // or a default langsys.
        var featureIndices = new HashSet<int>();
        CollectAllScriptFeatures(scriptListOff, featureIndices);

        var table = new GsubTable();
        int featureCount = ReadUInt16(featureListOff);
        foreach (int fi in featureIndices)
        {
            if (fi < 0 || fi >= featureCount)
                continue;
            uint frec = featureListOff + 2 + (uint)(fi * 6);
            string tag = ReadTag(frec);
            uint featOff = featureListOff + (uint)ReadUInt16(frec + 4);
            int lookupCount = ReadUInt16(featOff + 2);
            if (!table.FeatureLookups.TryGetValue(tag, out var list))
                table.FeatureLookups[tag] = list = [];
            for (int k = 0; k < lookupCount; k++)
                list.Add(ReadUInt16(featOff + 4 + (uint)(k * 2)));
        }

        int lookupCountTotal = ReadUInt16(lookupListOff);
        table.Lookups = [.. new object?[lookupCountTotal]];
        var needed = new HashSet<int>();
        foreach (var kv in table.FeatureLookups)
            foreach (int li in kv.Value)
                needed.Add(li);
        foreach (int li in needed)
        {
            if (li < 0 || li >= lookupCountTotal)
                continue;
            uint lookupOff = lookupListOff + (uint)ReadUInt16(lookupListOff + 2 + (uint)(li * 2));
            table.Lookups[li] = ParseLookup(lookupOff);
        }

        return table.FeatureLookups.Count > 0 ? table : null;
    }

    private void CollectAllScriptFeatures(uint scriptListOff, HashSet<int> featureIndices)
    {
        int scriptCount = ReadUInt16(scriptListOff);
        for (int s = 0; s < scriptCount; s++)
        {
            uint srec = scriptListOff + 2 + (uint)(s * 6);
            uint scriptOff = scriptListOff + (uint)ReadUInt16(srec + 4);

            uint defaultLangSys = (uint)ReadUInt16(scriptOff);
            if (defaultLangSys != 0)
                CollectLangSysFeatures(scriptOff + defaultLangSys, featureIndices);

            int langSysCount = ReadUInt16(scriptOff + 2);
            for (int l = 0; l < langSysCount; l++)
            {
                uint lrec = scriptOff + 4 + (uint)(l * 6);
                uint langSysOff = scriptOff + (uint)ReadUInt16(lrec + 4);
                CollectLangSysFeatures(langSysOff, featureIndices);
            }
        }
    }

    private void CollectLangSysFeatures(uint langSysOff, HashSet<int> featureIndices)
    {
        int required = ReadUInt16(langSysOff + 2);
        if (required != 0xFFFF)
            featureIndices.Add(required);
        int count = ReadUInt16(langSysOff + 4);
        for (int i = 0; i < count; i++)
            featureIndices.Add(ReadUInt16(langSysOff + 6 + (uint)(i * 2)));
    }

    private object? ParseLookup(uint lookupOff)
    {
        int type = ReadUInt16(lookupOff);
        int subCount = ReadUInt16(lookupOff + 4);

        if (type == 1)
        {
            var map = new Dictionary<int, int>();
            for (int s = 0; s < subCount; s++)
                ParseSingleSubst(lookupOff + (uint)ReadUInt16(lookupOff + 6 + (uint)(s * 2)), map);
            return new SingleSubst(map);
        }
        if (type == 4)
        {
            var sets = new Dictionary<int, List<(int[] rest, int lig)>>();
            for (int s = 0; s < subCount; s++)
                ParseLigatureSubst(lookupOff + (uint)ReadUInt16(lookupOff + 6 + (uint)(s * 2)), sets);
            return new LigatureSubst(sets);
        }
        if (type == 7)
        {
            // Extension substitution: each subtable indirects to a real subtable
            // (with 32-bit offset) whose own type is given inline.  Unwrap to the
            // supported single/ligature forms.
            Dictionary<int, int>? singleMap = null;
            Dictionary<int, List<(int[] rest, int lig)>>? ligSets = null;
            for (int s = 0; s < subCount; s++)
            {
                uint sub = lookupOff + (uint)ReadUInt16(lookupOff + 6 + (uint)(s * 2));
                int extType = ReadUInt16(sub + 2);
                uint extOff = sub + ReadU32(_data, (int)sub + 4);
                if (extType == 1)
                    ParseSingleSubst(extOff, singleMap ??= []);
                else if (extType == 4)
                    ParseLigatureSubst(extOff, ligSets ??= []);
            }
            if (singleMap != null)
                return new SingleSubst(singleMap);
            if (ligSets != null)
                return new LigatureSubst(ligSets);
        }

        // Types 2,3,5,6,8 (multiple/alternate/contextual/chaining/reverse) are
        // not needed for the supported Arabic-joining + ligature shaping.
        return null;
    }

    private void ParseSingleSubst(uint subOff, Dictionary<int, int> map)
    {
        int format = ReadUInt16(subOff);
        var coverage = ParseCoverage(subOff + (uint)ReadUInt16(subOff + 2));
        if (format == 1)
        {
            int delta = ReadInt16(subOff + 4);
            foreach (int g in coverage)
                map[g] = (g + delta) & 0xFFFF;
        }
        else if (format == 2)
        {
            int glyphCount = ReadUInt16(subOff + 4);
            for (int i = 0; i < coverage.Count && i < glyphCount; i++)
                map[coverage[i]] = ReadUInt16(subOff + 6 + (uint)(i * 2));
        }
    }

    private void ParseLigatureSubst(uint subOff, Dictionary<int, List<(int[] rest, int lig)>> sets)
    {
        if (ReadUInt16(subOff) != 1)
            return; // only LigatureSubstFormat1
        var coverage = ParseCoverage(subOff + (uint)ReadUInt16(subOff + 2));
        int ligSetCount = ReadUInt16(subOff + 4);
        for (int i = 0; i < ligSetCount && i < coverage.Count; i++)
        {
            uint ligSetOff = subOff + (uint)ReadUInt16(subOff + 6 + (uint)(i * 2));
            int ligCount = ReadUInt16(ligSetOff);
            int firstGlyph = coverage[i];
            if (!sets.TryGetValue(firstGlyph, out var lst))
                sets[firstGlyph] = lst = [];
            for (int j = 0; j < ligCount; j++)
            {
                uint ligOff = ligSetOff + (uint)ReadUInt16(ligSetOff + 2 + (uint)(j * 2));
                int ligGlyph = ReadUInt16(ligOff);
                int compCount = ReadUInt16(ligOff + 2);
                var rest = new int[Math.Max(0, compCount - 1)];
                for (int k = 0; k < rest.Length; k++)
                    rest[k] = ReadUInt16(ligOff + 4 + (uint)(k * 2));
                lst.Add((rest, ligGlyph));
            }
        }
    }

    private List<int> ParseCoverage(uint covOff)
    {
        var list = new List<int>();
        int fmt = ReadUInt16(covOff);
        if (fmt == 1)
        {
            int count = ReadUInt16(covOff + 2);
            for (int i = 0; i < count; i++)
                list.Add(ReadUInt16(covOff + 4 + (uint)(i * 2)));
        }
        else if (fmt == 2)
        {
            int rangeCount = ReadUInt16(covOff + 2);
            for (int r = 0; r < rangeCount; r++)
            {
                uint rec = covOff + 4 + (uint)(r * 6);
                int start = ReadUInt16(rec);
                int end = ReadUInt16(rec + 2);
                for (int g = start; g <= end && g >= 0; g++)
                    list.Add(g);
            }
        }
        return list;
    }

    private string ReadTag(uint offset)
    {
        if (offset + 4 > _data.Length)
            return string.Empty;
        return System.Text.Encoding.ASCII.GetString(_data, (int)offset, 4);
    }

    // ── GDEF / GPOS (glyph classes + mark positioning) ───────────────────────

    /// <summary>Whether <paramref name="glyph"/> is a mark per the GDEF glyph-class table.</summary>
    public bool IsMarkGlyph(int glyph)
    {
        if (!_gdefParsed)
        {
            _gdefParsed = true;
            try { _gdefClasses = ParseGdefClasses(); }
            catch { _gdefClasses = null; }
        }
        return _gdefClasses != null && _gdefClasses.GetClass(glyph) == 3; // 3 = Mark
    }

    /// <summary>
    /// GPOS mark-to-base (type 4): the offset (font units) to add to a mark glyph
    /// so its attachment anchor coincides with the base glyph's anchor.
    /// </summary>
    public bool TryGetMarkToBaseAnchor(int baseGlyph, int markGlyph, out int dx, out int dy)
    {
        var g = GetGpos();
        if (g != null)
            return g.TryMarkBase(baseGlyph, markGlyph, out dx, out dy);
        dx = dy = 0;
        return false;
    }

    /// <summary>
    /// GPOS mark-to-mark (type 6): the offset (font units) to attach
    /// <paramref name="attachingMark"/> onto an already-placed <paramref name="baseMark"/>.
    /// </summary>
    public bool TryGetMarkToMarkAnchor(int baseMark, int attachingMark, out int dx, out int dy)
    {
        var g = GetGpos();
        if (g != null)
            return g.TryMarkMark(baseMark, attachingMark, out dx, out dy);
        dx = dy = 0;
        return false;
    }

    private GposTable? GetGpos()
    {
        if (_gposParsed)
            return _gpos;
        _gposParsed = true;
        try { _gpos = ParseGpos(); }
        catch { _gpos = null; }
        return _gpos;
    }

    private GposTable? ParseGpos()
    {
        uint gpos = _tables.GetValueOrDefault("GPOS");
        if (gpos == 0)
            return null;

        uint scriptListOff = gpos + (uint)ReadUInt16(gpos + 4);
        uint featureListOff = gpos + (uint)ReadUInt16(gpos + 6);
        uint lookupListOff = gpos + (uint)ReadUInt16(gpos + 8);

        var featureIndices = new HashSet<int>();
        CollectAllScriptFeatures(scriptListOff, featureIndices);

        var markLookups = new List<int>();
        var mkmkLookups = new List<int>();
        int featureCount = ReadUInt16(featureListOff);
        foreach (int fi in featureIndices)
        {
            if (fi < 0 || fi >= featureCount)
                continue;
            uint frec = featureListOff + 2 + (uint)(fi * 6);
            string tag = ReadTag(frec);
            if (tag != "mark" && tag != "mkmk")
                continue;
            uint featOff = featureListOff + (uint)ReadUInt16(frec + 4);
            int lookupCount = ReadUInt16(featOff + 2);
            var dst = tag == "mark" ? markLookups : mkmkLookups;
            for (int k = 0; k < lookupCount; k++)
                dst.Add(ReadUInt16(featOff + 4 + (uint)(k * 2)));
        }

        var table = new GposTable();
        int lookupCountTotal = ReadUInt16(lookupListOff);
        foreach (int li in markLookups)
            ParseGposLookup(lookupListOff, lookupCountTotal, li, expectMarkBase: true, table);
        foreach (int li in mkmkLookups)
            ParseGposLookup(lookupListOff, lookupCountTotal, li, expectMarkBase: false, table);

        return table.MarkBase.Count > 0 || table.MarkMark.Count > 0 ? table : null;
    }

    private void ParseGposLookup(uint lookupListOff, int lookupCountTotal, int li, bool expectMarkBase, GposTable table)
    {
        if (li < 0 || li >= lookupCountTotal)
            return;
        uint lookupOff = lookupListOff + (uint)ReadUInt16(lookupListOff + 2 + (uint)(li * 2));
        int type = ReadUInt16(lookupOff);
        int subCount = ReadUInt16(lookupOff + 4);

        for (int s = 0; s < subCount; s++)
        {
            uint sub = lookupOff + (uint)ReadUInt16(lookupOff + 6 + (uint)(s * 2));
            int effectiveType = type;
            uint effectiveSub = sub;
            if (type == 9) // Extension positioning: unwrap.
            {
                effectiveType = ReadUInt16(sub + 2);
                effectiveSub = sub + ReadU32(_data, (int)sub + 4);
            }

            if (effectiveType == 4)
                table.MarkBase.Add(ParseMarkBasePos(effectiveSub));
            else if (effectiveType == 6)
                table.MarkMark.Add(ParseMarkMarkPos(effectiveSub));
        }
    }

    private MarkArrayData ParseMarkArray(uint markArrayOff, List<int> coverage)
    {
        var map = new Dictionary<int, (int cls, int x, int y)>();
        int markCount = ReadUInt16(markArrayOff);
        for (int i = 0; i < markCount && i < coverage.Count; i++)
        {
            uint rec = markArrayOff + 2 + (uint)(i * 4);
            int cls = ReadUInt16(rec);
            int anchorRel = ReadUInt16(rec + 2);
            (int x, int y) = anchorRel != 0 ? ReadAnchor(markArrayOff + (uint)anchorRel) : (0, 0);
            map[coverage[i]] = (cls, x, y);
        }
        return new MarkArrayData(map);
    }

    private Dictionary<int, (int x, int y)[]> ParseBaseArray(uint baseArrayOff, List<int> coverage, int markClassCount)
    {
        var map = new Dictionary<int, (int x, int y)[]>();
        int baseCount = ReadUInt16(baseArrayOff);
        for (int i = 0; i < baseCount && i < coverage.Count; i++)
        {
            uint recBase = baseArrayOff + 2 + (uint)(i * markClassCount * 2);
            var anchors = new (int x, int y)[markClassCount];
            for (int c = 0; c < markClassCount; c++)
            {
                int anchorRel = ReadUInt16(recBase + (uint)(c * 2));
                anchors[c] = anchorRel != 0 ? ReadAnchor(baseArrayOff + (uint)anchorRel) : (0, 0);
            }
            map[coverage[i]] = anchors;
        }
        return map;
    }

    private MarkBasePos ParseMarkBasePos(uint subOff)
    {
        var markCov = ParseCoverage(subOff + (uint)ReadUInt16(subOff + 2));
        var baseCov = ParseCoverage(subOff + (uint)ReadUInt16(subOff + 4));
        int markClassCount = ReadUInt16(subOff + 6);
        var marks = ParseMarkArray(subOff + (uint)ReadUInt16(subOff + 8), markCov);
        var bases = ParseBaseArray(subOff + (uint)ReadUInt16(subOff + 10), baseCov, markClassCount);
        return new MarkBasePos(marks, bases);
    }

    private MarkMarkPos ParseMarkMarkPos(uint subOff)
    {
        var mark1Cov = ParseCoverage(subOff + (uint)ReadUInt16(subOff + 2));
        var mark2Cov = ParseCoverage(subOff + (uint)ReadUInt16(subOff + 4));
        int markClassCount = ReadUInt16(subOff + 6);
        var mark1 = ParseMarkArray(subOff + (uint)ReadUInt16(subOff + 8), mark1Cov);
        var mark2 = ParseBaseArray(subOff + (uint)ReadUInt16(subOff + 10), mark2Cov, markClassCount);
        return new MarkMarkPos(mark1, mark2);
    }

    private (int x, int y) ReadAnchor(uint anchorOff)
    {
        // Formats 1/2/3 all begin with format(2) + xCoordinate(2) + yCoordinate(2);
        // anchor-point (fmt 2) and device tables (fmt 3) are ignored.
        return (ReadInt16(anchorOff + 2), ReadInt16(anchorOff + 4));
    }

    private ClassDefTable? ParseGdefClasses()
    {
        uint gdef = _tables.GetValueOrDefault("GDEF");
        if (gdef == 0)
            return null;
        int classDefRel = ReadUInt16(gdef + 4);
        if (classDefRel == 0)
            return null;
        return ParseClassDef(gdef + (uint)classDefRel);
    }

    private ClassDefTable ParseClassDef(uint off)
    {
        var map = new Dictionary<int, int>();
        int format = ReadUInt16(off);
        if (format == 1)
        {
            int startGlyph = ReadUInt16(off + 2);
            int count = ReadUInt16(off + 4);
            for (int i = 0; i < count; i++)
                map[startGlyph + i] = ReadUInt16(off + 6 + (uint)(i * 2));
        }
        else if (format == 2)
        {
            int rangeCount = ReadUInt16(off + 2);
            for (int r = 0; r < rangeCount; r++)
            {
                uint rec = off + 4 + (uint)(r * 6);
                int start = ReadUInt16(rec);
                int end = ReadUInt16(rec + 2);
                int cls = ReadUInt16(rec + 4);
                for (int g = start; g <= end && g >= 0; g++)
                    map[g] = cls;
            }
        }
        return new ClassDefTable(map);
    }

    private sealed class GposTable
    {
        public List<MarkBasePos> MarkBase { get; } = [];
        public List<MarkMarkPos> MarkMark { get; } = [];

        public bool TryMarkBase(int baseGlyph, int markGlyph, out int dx, out int dy)
        {
            foreach (var mb in MarkBase)
                if (mb.Try(baseGlyph, markGlyph, out dx, out dy))
                    return true;
            dx = dy = 0;
            return false;
        }

        public bool TryMarkMark(int baseMark, int attachingMark, out int dx, out int dy)
        {
            foreach (var mm in MarkMark)
                if (mm.Try(baseMark, attachingMark, out dx, out dy))
                    return true;
            dx = dy = 0;
            return false;
        }
    }

    private sealed class MarkArrayData(Dictionary<int, (int cls, int x, int y)> marks)
    {
        public bool TryGet(int glyph, out int cls, out int x, out int y)
        {
            if (marks.TryGetValue(glyph, out var m))
            {
                (cls, x, y) = m;
                return true;
            }
            cls = x = y = 0;
            return false;
        }
    }

    private sealed class MarkBasePos(MarkArrayData marks, Dictionary<int, (int x, int y)[]> bases)
    {
        public bool Try(int baseGlyph, int markGlyph, out int dx, out int dy)
        {
            dx = dy = 0;
            if (!marks.TryGet(markGlyph, out int cls, out int mx, out int my))
                return false;
            if (!bases.TryGetValue(baseGlyph, out var anchors) || cls >= anchors.Length)
                return false;
            dx = anchors[cls].x - mx;
            dy = anchors[cls].y - my;
            return true;
        }
    }

    private sealed class MarkMarkPos(MarkArrayData mark1, Dictionary<int, (int x, int y)[]> mark2)
    {
        public bool Try(int baseMark, int attachingMark, out int dx, out int dy)
        {
            dx = dy = 0;
            if (!mark1.TryGet(attachingMark, out int cls, out int mx, out int my))
                return false;
            if (!mark2.TryGetValue(baseMark, out var anchors) || cls >= anchors.Length)
                return false;
            dx = anchors[cls].x - mx;
            dy = anchors[cls].y - my;
            return true;
        }
    }

    private sealed class ClassDefTable(Dictionary<int, int> classes)
    {
        public int GetClass(int glyph) => classes.TryGetValue(glyph, out int c) ? c : 0;
    }

    /// <summary>Parsed subset of GSUB used for Arabic-joining + ligature shaping.</summary>
    private sealed class GsubTable
    {
        public Dictionary<string, List<int>> FeatureLookups { get; } = new(StringComparer.Ordinal);
        public List<object?> Lookups { get; set; } = [];

        public int ApplySingle(string tag, int glyph)
        {
            if (!FeatureLookups.TryGetValue(tag, out var indices))
                return glyph;
            foreach (int i in indices)
                if (i >= 0 && i < Lookups.Count && Lookups[i] is SingleSubst ss && ss.TryMap(glyph, out int s))
                    return s;
            return glyph;
        }

        public bool TryLigate(string tag, IReadOnlyList<int> glyphs, int pos, out int lig, out int count)
        {
            if (FeatureLookups.TryGetValue(tag, out var indices))
                foreach (int i in indices)
                    if (i >= 0 && i < Lookups.Count && Lookups[i] is LigatureSubst ls
                        && ls.TryLigate(glyphs, pos, out lig, out count))
                        return true;
            lig = 0;
            count = 0;
            return false;
        }
    }

    private sealed class SingleSubst
    {
        private readonly Dictionary<int, int> _map;
        public SingleSubst(Dictionary<int, int> map) => _map = map;
        public bool TryMap(int glyph, out int substitute) => _map.TryGetValue(glyph, out substitute);
    }

    private sealed class LigatureSubst(Dictionary<int, List<(int[] rest, int lig)>> sets)
    {
        public bool TryLigate(IReadOnlyList<int> glyphs, int pos, out int lig, out int count)
        {
            lig = 0;
            count = 0;
            if (!sets.TryGetValue(glyphs[pos], out var ligs))
                return false;
            foreach (var (rest, l) in ligs)
            {
                bool ok = true;
                for (int k = 0; k < rest.Length; k++)
                {
                    if (pos + 1 + k >= glyphs.Count || glyphs[pos + 1 + k] != rest[k])
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    lig = l;
                    count = rest.Length + 1;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Resolved cmap supporting either a sparse map or format-12 ranges.</summary>
    private sealed class CmapLookup(Dictionary<int, int>? map, List<(uint start, uint end, uint startGlyph)>? groups)
    {
        public int Map(int codepoint)
        {
            if (map != null)
                return map.TryGetValue(codepoint, out int g) ? g : 0;

            if (groups != null)
            {
                uint cp = (uint)codepoint;
                foreach (var grp in groups)
                {
                    if (cp >= grp.start && cp <= grp.end)
                        return (int)(grp.startGlyph + (cp - grp.start));
                }
            }

            return 0;
        }
    }
}
