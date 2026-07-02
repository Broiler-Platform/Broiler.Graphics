#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Broiler.Graphics;

/// <summary>
/// Parser and Type 2 charstring interpreter for the <c>CFF </c> table, so that
/// CFF/PostScript-outline OpenType fonts (sfnt flavor <c>OTTO</c>, common for
/// .otf and many .woff web fonts) can be rasterised alongside glyf fonts.
/// Produces glyph outlines as flattened contours in font design units (y-up),
/// matching <see cref="TrueTypeFont.GetGlyphContours"/>.
/// </summary>
public sealed class CffFont
{
    private readonly byte[] _data;
    private readonly Range[] _charStrings;
    private readonly Range[] _globalSubrs;
    private Range[] _localSubrs;            // non-CID fonts
    private Range[][] _fdLocalSubrs;        // CID fonts: per-FD local subrs
    private byte[] _fdSelect;               // CID fonts: glyph → FD index
    private readonly int _globalBias;
    private readonly int _localBias;
    private readonly double _unitScale;              // charstring units → font units

    public bool Ok { get; }

    private readonly struct Range(int start, int end)
    {
        public int Start { get; } = start; 
        public int End { get; } = end;
    }

    public CffFont(byte[] data, int cffOffset, int unitsPerEm)
    {
        _data = data;
        try
        {
            int hdrSize = data[cffOffset + 2];
            int p = cffOffset + hdrSize;

            p = SkipIndex(p);                       // Name INDEX
            var topDicts = ReadIndex(ref p);        // Top DICT INDEX
            var stringIndex = ReadIndex(ref p);     // String INDEX (unused)
            _globalSubrs = ReadIndex(ref p);        // Global Subr INDEX
            _globalBias = Bias(_globalSubrs.Length);

            var top = ParseDict(topDicts[0]);

            // FontMatrix[0] (default 0.001) maps charstring units to em; combine
            // with unitsPerEm so output is in the font's design units.
            double fontMatrix00 = top.TryGetValue(1207, out var fm) && fm.Length > 0 ? fm[0] : 0.001;
            _unitScale = fontMatrix00 * unitsPerEm;
            if (_unitScale <= 0 || double.IsNaN(_unitScale))
                _unitScale = 1.0;

            int charStringsOff = top.TryGetValue(17, out var cs) ? cffOffset + (int)cs[0] : 0;
            int q = charStringsOff;
            _charStrings = ReadIndex(ref q);

            bool isCid = top.ContainsKey(1230); // ROS operator → CID-keyed
            if (isCid)
            {
                ParseCidPrivate(top, cffOffset);
            }
            else if (top.TryGetValue(18, out var priv) && priv.Length == 2)
            {
                _localSubrs = ParsePrivateLocalSubrs(cffOffset + (int)priv[1], (int)priv[0]);
            }

            _localBias = Bias(_localSubrs?.Length ?? 0);
            Ok = _charStrings != null && _charStrings.Length > 0;
        }
        catch
        {
            Ok = false;
        }
    }

    /// <summary>Returns the glyph outline as flattened contours in font units (y-up).</summary>
    public List<PointF[]> GetGlyphOutline(int gid)
    {
        var contours = new List<PointF[]>();
        if (!Ok || gid < 0 || gid >= _charStrings.Length)
            return contours;

        Range[] local = _localSubrs;
        int localBias = _localBias;
        if (_fdSelect != null && _fdLocalSubrs != null && gid < _fdSelect.Length)
        {
            int fd = _fdSelect[gid];
            if (fd >= 0 && fd < _fdLocalSubrs.Length)
            {
                local = _fdLocalSubrs[fd];
                localBias = Bias(local?.Length ?? 0);
            }
        }

        var interp = new Type2Interpreter(this, local, localBias);
        interp.Execute(_charStrings[gid]);
        interp.Finish();

        foreach (var contour in interp.Contours)
            if (contour.Count >= 2)
                contours.Add(contour.ToArray());
        return contours;
    }

    // ── CID Private/FD parsing ───────────────────────────────────────────────

    private void ParseCidPrivate(Dictionary<int, double[]> top, int cffOffset)
    {
        if (!top.TryGetValue(1236, out var fdArrayOp) || !top.TryGetValue(1237, out var fdSelectOp))
            return;

        int fp = cffOffset + (int)fdArrayOp[0];
        var fdDicts = ReadIndex(ref fp);
        var perFd = new Range[fdDicts.Length][];
        for (int i = 0; i < fdDicts.Length; i++)
        {
            var fd = ParseDict(fdDicts[i]);
            if (fd.TryGetValue(18, out var priv) && priv.Length == 2)
                perFd[i] = ParsePrivateLocalSubrs(cffOffset + (int)priv[1], (int)priv[0]);
        }
        _fdLocalSubrs = perFd;
        _fdSelect = ParseFdSelect(cffOffset + (int)fdSelectOp[0], _charStrings.Length);
    }

    private Range[] ParsePrivateLocalSubrs(int privStart, int privLength)
    {
        var priv = ParseDict(new Range(privStart, privStart + privLength));
        if (priv.TryGetValue(19, out var subrsOp))
        {
            int sp = privStart + (int)subrsOp[0];
            return ReadIndex(ref sp);
        }
        return null;
    }

    private byte[] ParseFdSelect(int off, int nGlyphs)
    {
        var sel = new byte[nGlyphs];
        int format = _data[off];
        if (format == 0)
        {
            for (int i = 0; i < nGlyphs; i++)
                sel[i] = _data[off + 1 + i];
        }
        else if (format == 3)
        {
            int nRanges = U16(off + 1);
            int rp = off + 3;
            for (int r = 0; r < nRanges; r++)
            {
                int first = U16(rp);
                int fd = _data[rp + 2];
                int next = U16(rp + 3);
                for (int g = first; g < next && g < nGlyphs; g++)
                    sel[g] = (byte)fd;
                rp += 3;
            }
        }
        return sel;
    }

    // ── INDEX / DICT parsing ─────────────────────────────────────────────────

    private int SkipIndex(int p)
    {
        ReadIndex(ref p);
        return p;
    }

    private Range[] ReadIndex(ref int p)
    {
        int count = U16(p);
        p += 2;
        if (count == 0)
            return Array.Empty<Range>();

        int offSize = _data[p++];
        var offsets = new int[count + 1];
        for (int i = 0; i <= count; i++)
        {
            int v = 0;
            for (int b = 0; b < offSize; b++)
                v = (v << 8) | _data[p++];
            offsets[i] = v;
        }

        int dataBase = p - 1; // offsets are 1-based relative to here
        var ranges = new Range[count];
        for (int i = 0; i < count; i++)
            ranges[i] = new Range(dataBase + offsets[i], dataBase + offsets[i + 1]);

        p = dataBase + offsets[count];
        return ranges;
    }

    private Dictionary<int, double[]> ParseDict(Range range)
    {
        var dict = new Dictionary<int, double[]>();
        var operands = new List<double>();
        int p = range.Start;
        while (p < range.End)
        {
            int b0 = _data[p];
            if (b0 <= 21) // operator
            {
                int op = b0;
                p++;
                if (b0 == 12)
                {
                    op = 1200 + _data[p];
                    p++;
                }
                dict[op] = operands.ToArray();
                operands.Clear();
            }
            else if (b0 == 28)
            {
                operands.Add((short)((_data[p + 1] << 8) | _data[p + 2]));
                p += 3;
            }
            else if (b0 == 29)
            {
                operands.Add((_data[p + 1] << 24) | (_data[p + 2] << 16) | (_data[p + 3] << 8) | _data[p + 4]);
                p += 5;
            }
            else if (b0 == 30) // real number
            {
                operands.Add(ParseReal(ref p));
            }
            else if (b0 >= 32 && b0 <= 246)
            {
                operands.Add(b0 - 139);
                p++;
            }
            else if (b0 >= 247 && b0 <= 250)
            {
                operands.Add((b0 - 247) * 256 + _data[p + 1] + 108);
                p += 2;
            }
            else if (b0 >= 251 && b0 <= 254)
            {
                operands.Add(-(b0 - 251) * 256 - _data[p + 1] - 108);
                p += 2;
            }
            else
            {
                p++; // reserved
            }
        }
        return dict;
    }

    private double ParseReal(ref int p)
    {
        p++; // skip 30
        var sb = new System.Text.StringBuilder();
        bool done = false;
        while (!done && p < _data.Length)
        {
            int b = _data[p++];
            foreach (int nibble in new[] { b >> 4, b & 0x0F })
            {
                switch (nibble)
                {
                    case <= 9: sb.Append((char)('0' + nibble)); break;
                    case 0xa: sb.Append('.'); break;
                    case 0xb: sb.Append('E'); break;
                    case 0xc: sb.Append("E-"); break;
                    case 0xe: sb.Append('-'); break;
                    case 0xf: done = true; break;
                    default: break; // 0xd reserved
                }
                if (done) break;
            }
        }
        return double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
    }

    private static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

    private int U16(int o) => (_data[o] << 8) | _data[o + 1];

    // ── Type 2 charstring interpreter ────────────────────────────────────────

    private sealed class Type2Interpreter
    {
        private const int CurveSegments = 8;

        private readonly CffFont _font;
        private readonly Range[] _localSubrs;
        private readonly int _localBias;

        private readonly double[] _stack = new double[48];
        private int _sp;
        private double _x, _y;
        private int _nStems;
        private bool _haveWidth;
        private bool _open;

        public List<List<PointF>> Contours { get; } = new();
        private List<PointF> _current;

        public Type2Interpreter(CffFont font, Range[] localSubrs, int localBias)
        {
            _font = font;
            _localSubrs = localSubrs;
            _localBias = localBias;
        }

        public void Finish()
        {
            if (_open && _current != null && _current.Count > 0)
                Contours.Add(_current);
            _open = false;
        }

        public void Execute(Range cs) => Run(cs, 0);

        private bool Run(Range cs, int depth)
        {
            if (depth > 10)
                return true;
            byte[] d = _font._data;
            int p = cs.Start;
            while (p < cs.End)
            {
                int b0 = d[p++];
                if (b0 >= 32 || b0 == 28)
                {
                    // Operand.
                    double val;
                    if (b0 == 28) { val = (short)((d[p] << 8) | d[p + 1]); p += 2; }
                    else if (b0 < 247) val = b0 - 139;
                    else if (b0 < 251) { val = (b0 - 247) * 256 + d[p] + 108; p++; }
                    else if (b0 < 255) { val = -(b0 - 251) * 256 - d[p] - 108; p++; }
                    else { val = ((d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]) / 65536.0; p += 4; }
                    if (_sp < _stack.Length)
                        _stack[_sp++] = val;
                    continue;
                }

                switch (b0)
                {
                    case 1: case 3: case 18: case 23: // h/v stem (hm)
                        CountStems();
                        break;
                    case 19: case 20: // hintmask / cntrmask
                        CountStems();
                        p += (_nStems + 7) / 8;
                        break;
                    case 21: // rmoveto
                        MaybeWidth(2);
                        MoveTo(_x + _stack[0], _y + _stack[1]);
                        _sp = 0;
                        break;
                    case 22: // hmoveto
                        MaybeWidth(1);
                        MoveTo(_x + _stack[0], _y);
                        _sp = 0;
                        break;
                    case 4: // vmoveto
                        MaybeWidth(1);
                        MoveTo(_x, _y + _stack[0]);
                        _sp = 0;
                        break;
                    case 5: // rlineto
                        for (int i = 0; i + 1 < _sp; i += 2)
                            LineTo(_x + _stack[i], _y + _stack[i + 1]);
                        _sp = 0;
                        break;
                    case 6: // hlineto
                        AlternatingLines(horizontalFirst: true);
                        break;
                    case 7: // vlineto
                        AlternatingLines(horizontalFirst: false);
                        break;
                    case 8: // rrcurveto
                        for (int i = 0; i + 5 < _sp; i += 6)
                            RelCurve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        _sp = 0;
                        break;
                    case 24: // rcurveline
                    {
                        int i = 0;
                        for (; i + 5 < _sp - 2; i += 6)
                            RelCurve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        if (i + 1 < _sp)
                            LineTo(_x + _stack[i], _y + _stack[i + 1]);
                        _sp = 0;
                        break;
                    }
                    case 25: // rlinecurve
                    {
                        int i = 0;
                        for (; i + 1 < _sp - 6; i += 2)
                            LineTo(_x + _stack[i], _y + _stack[i + 1]);
                        if (i + 5 < _sp)
                            RelCurve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                        _sp = 0;
                        break;
                    }
                    case 26: VvCurveto(); break;
                    case 27: HhCurveto(); break;
                    case 30: VhHvCurveto(startHorizontal: false); break;
                    case 31: VhHvCurveto(startHorizontal: true); break;
                    case 10: // callsubr
                        if (_sp > 0)
                        {
                            int idx = (int)_stack[--_sp] + _localBias;
                            if (_localSubrs != null && idx >= 0 && idx < _localSubrs.Length)
                                if (Run(_localSubrs[idx], depth + 1)) return true;
                        }
                        break;
                    case 29: // callgsubr
                        if (_sp > 0)
                        {
                            int idx = (int)_stack[--_sp] + _font._globalBias;
                            if (_font._globalSubrs != null && idx >= 0 && idx < _font._globalSubrs.Length)
                                if (Run(_font._globalSubrs[idx], depth + 1)) return true;
                        }
                        break;
                    case 11: // return
                        return false;
                    case 14: // endchar
                        MaybeWidth(0);
                        Finish();
                        return true;
                    case 12: // escape (two-byte operators: flex family)
                    {
                        int b1 = d[p++];
                        switch (b1)
                        {
                            case 34: Hflex(); break;
                            case 35: Flex(); break;
                            case 36: Hflex1(); break;
                            case 37: Flex1(); break;
                            default: _sp = 0; break;
                        }
                        break;
                    }
                    default:
                        _sp = 0;
                        break;
                }
            }
            return false;
        }

        private void CountStems()
        {
            // An odd argument count means the optional width precedes the stems.
            if (!_haveWidth && (_sp % 2) == 1)
                _haveWidth = true;
            _nStems += _sp / 2;
            _sp = 0;
        }

        private void MaybeWidth(int expectedArgs)
        {
            if (_haveWidth)
                return;
            _haveWidth = true;
            if (_sp > expectedArgs)
            {
                // Drop the leading width operand.
                for (int i = 1; i < _sp; i++)
                    _stack[i - 1] = _stack[i];
                _sp--;
            }
        }

        private void MoveTo(double x, double y)
        {
            Finish();
            _x = x;
            _y = y;
            _current = new List<PointF> { Pt(x, y) };
            _open = true;
        }

        private void LineTo(double x, double y)
        {
            _x = x;
            _y = y;
            _current?.Add(Pt(x, y));
        }

        private void RelCurve(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            double x0 = _x, y0 = _y;
            double cx1 = x0 + dx1, cy1 = y0 + dy1;
            double cx2 = cx1 + dx2, cy2 = cy1 + dy2;
            double x3 = cx2 + dx3, y3 = cy2 + dy3;
            FlattenCubic(x0, y0, cx1, cy1, cx2, cy2, x3, y3);
            _x = x3;
            _y = y3;
        }

        private void FlattenCubic(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
        {
            for (int i = 1; i <= CurveSegments; i++)
            {
                double t = i / (double)CurveSegments;
                double mt = 1 - t;
                double a = mt * mt * mt, b = 3 * mt * mt * t, c = 3 * mt * t * t, e = t * t * t;
                _current?.Add(Pt(a * x0 + b * x1 + c * x2 + e * x3, a * y0 + b * y1 + c * y2 + e * y3));
            }
        }

        private void AlternatingLines(bool horizontalFirst)
        {
            bool horizontal = horizontalFirst;
            for (int i = 0; i < _sp; i++)
            {
                if (horizontal) LineTo(_x + _stack[i], _y);
                else LineTo(_x, _y + _stack[i]);
                horizontal = !horizontal;
            }
            _sp = 0;
        }

        private void HhCurveto()
        {
            int i = 0;
            double dy1 = 0;
            if ((_sp % 4) == 1) { dy1 = _stack[0]; i = 1; }
            for (; i + 3 < _sp; i += 4)
            {
                RelCurve(_stack[i], dy1, _stack[i + 1], _stack[i + 2], _stack[i + 3], 0);
                dy1 = 0;
            }
            _sp = 0;
        }

        private void VvCurveto()
        {
            int i = 0;
            double dx1 = 0;
            if ((_sp % 4) == 1) { dx1 = _stack[0]; i = 1; }
            for (; i + 3 < _sp; i += 4)
            {
                RelCurve(dx1, _stack[i], _stack[i + 1], _stack[i + 2], 0, _stack[i + 3]);
                dx1 = 0;
            }
            _sp = 0;
        }

        private void VhHvCurveto(bool startHorizontal)
        {
            bool horizontal = startHorizontal;
            int i = 0;
            while (i + 3 < _sp)
            {
                bool last = (_sp - i) == 5;
                double df = last ? _stack[i + 4] : 0;
                if (horizontal)
                    RelCurve(_stack[i], 0, _stack[i + 1], _stack[i + 2], df, _stack[i + 3]);
                else
                    RelCurve(0, _stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], df);
                i += 4;
                horizontal = !horizontal;
            }
            _sp = 0;
        }

        private void Flex()
        {
            if (_sp >= 12)
            {
                RelCurve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                RelCurve(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
            }
            _sp = 0;
        }

        private void Hflex()
        {
            if (_sp >= 7)
            {
                RelCurve(_stack[0], 0, _stack[1], _stack[2], _stack[3], 0);
                RelCurve(_stack[4], 0, _stack[5], -_stack[2], _stack[6], 0);
            }
            _sp = 0;
        }

        private void Hflex1()
        {
            if (_sp >= 9)
            {
                RelCurve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0);
                double dy = _stack[1] + _stack[3] + _stack[7];
                RelCurve(_stack[5], 0, _stack[6], _stack[7], _stack[8], -dy);
            }
            _sp = 0;
        }

        private void Flex1()
        {
            if (_sp >= 11)
            {
                double dx = _stack[0] + _stack[2] + _stack[4] + _stack[6] + _stack[8];
                double dy = _stack[1] + _stack[3] + _stack[5] + _stack[7] + _stack[9];
                RelCurve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                if (Math.Abs(dx) > Math.Abs(dy))
                    RelCurve(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], -dy);
                else
                    RelCurve(_stack[6], _stack[7], _stack[8], _stack[9], -dx, _stack[10]);
            }
            _sp = 0;
        }

        private PointF Pt(double x, double y) =>
            new((float)(x * _font._unitScale), (float)(y * _font._unitScale));
    }
}
