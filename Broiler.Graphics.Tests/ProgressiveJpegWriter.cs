using System;
using System.Collections.Generic;
using System.IO;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Throwaway generator that emits a 4:2:0 <b>progressive</b> JPEG (SOF2) using
/// spectral selection, successive approximation, <b>optimal Huffman tables</b>,
/// and cross-block <b>EOB runs</b> (EOBn) — so the progressive decoder is exercised
/// across all of its entropy paths, including EOB-run skipping. Output is validated
/// by an independent decoder (System.Drawing) before being trusted as a fixture.
/// </summary>
internal static class ProgressiveJpegWriter
{
    private sealed class Comp
    {
        public int Id;
        public int H;
        public int V;
        public int DcId;   // JPEG table id (0 = luma, 1 = chroma)
        public int AcId;
        public int OutDc;  // sink table index: 0=dcLuma 1=acLuma 2=dcChroma 3=acChroma
        public int OutAc;
        public int AllocBlocksPerLine;
        public int RealBlocksPerLine;
        public int RealBlocksPerColumn;
        public int[] Coeff = [];
        public int Pred;
    }

    /// <summary>Routes Huffman symbols/bits either to a frequency histogram (gather) or the bitstream (write).</summary>
    private sealed class Sink
    {
        private readonly int[][]? _freq;
        private readonly JpegBitWriter? _writer;
        private readonly JpegHuffmanTable[]? _tables;

        public Sink(int[][] freq) => _freq = freq;
        public Sink(JpegBitWriter writer, JpegHuffmanTable[] tables) => (_writer, _tables) = (writer, tables);

        public bool Gathering => _writer is null;

        public void Symbol(int table, int symbol)
        {
            if (_writer != null)
                _writer.WriteSymbol(_tables![table], symbol);
            else
                _freq![table][symbol]++;
        }

        public void Bits(int code, int size)
        {
            if (_writer != null && size > 0)
                _writer.WriteBits(code, size);
        }
    }

    public static byte[] Encode(BPixelBuffer src, int quality = 75)
    {
        int width = src.Width, height = src.Height;
        int[] qLuma = JpegTables.BuildQuantTable(JpegTables.LuminanceQuant, quality);
        int[] qChroma = JpegTables.BuildQuantTable(JpegTables.ChrominanceQuant, quality);

        int mcusX = (width + 15) / 16, mcusY = (height + 15) / 16;
        int yW = mcusX * 16, yH = mcusY * 16, cW = mcusX * 8, cH = mcusY * 8;

        byte[] planeY = new byte[yW * yH];
        byte[] planeCb = new byte[cW * cH];
        byte[] planeCr = new byte[cW * cH];
        BuildPlanes(src, width, height, yW, yH, cW, cH, planeY, planeCb, planeCr);

        var y = new Comp { Id = 1, H = 2, V = 2, DcId = 0, AcId = 0, OutDc = 0, OutAc = 1 };
        var cb = new Comp { Id = 2, H = 1, V = 1, DcId = 1, AcId = 1, OutDc = 2, OutAc = 3 };
        var cr = new Comp { Id = 3, H = 1, V = 1, DcId = 1, AcId = 1, OutDc = 2, OutAc = 3 };
        Comp[] comps = [y, cb, cr];

        SetupCoeff(y, mcusX, mcusY, width, height);
        SetupCoeff(cb, mcusX, mcusY, width, height);
        SetupCoeff(cr, mcusX, mcusY, width, height);
        ComputeCoeff(y, planeY, yW, qLuma);
        ComputeCoeff(cb, planeCb, cW, qChroma);
        ComputeCoeff(cr, planeCr, cW, qChroma);

        // Pass 1: gather symbol frequencies for the four tables.
        var freq = new int[4][];
        for (int i = 0; i < 4; i++)
            freq[i] = new int[256];
        var gather = new Sink(freq);
        RunAllScans(gather, comps, mcusX, mcusY);

        // Build optimal tables from the statistics.
        var bits = new byte[4][];
        var vals = new byte[4][];
        var tables = new JpegHuffmanTable[4];
        for (int i = 0; i < 4; i++)
        {
            (bits[i], vals[i]) = JpegOptimalHuffman.Generate(freq[i]);
            tables[i] = JpegHuffmanTable.Build(bits[i], vals[i]);
        }

        // Pass 2: write headers and the entropy-coded scans.
        using var ms = new MemoryStream();
        WriteHeaders(ms, width, height, qLuma, qChroma, bits, vals);
        WriteAllScans(ms, comps, tables, mcusX, mcusY);
        ms.WriteByte(0xFF);
        ms.WriteByte(JpegTables.MarkerEoi);
        return ms.ToArray();
    }

    // ---- Scan driving ---------------------------------------------------

    private static void RunAllScans(Sink sink, Comp[] comps, int mcusX, int mcusY)
    {
        DcScan(sink, comps, mcusX, mcusY, ah: 0, al: 1);
        DcScan(sink, comps, mcusX, mcusY, ah: 1, al: 0);
        foreach (Comp c in comps) AcScan(sink, c, ah: 0, al: 1);
        foreach (Comp c in comps) AcScan(sink, c, ah: 1, al: 0);
    }

    private static void WriteAllScans(MemoryStream ms, Comp[] comps, JpegHuffmanTable[] tables, int mcusX, int mcusY)
    {
        WriteDcScan(ms, comps, tables, mcusX, mcusY, ah: 0, al: 1);
        WriteDcScan(ms, comps, tables, mcusX, mcusY, ah: 1, al: 0);
        foreach (Comp c in comps) WriteAcScan(ms, c, tables, ah: 0, al: 1);
        foreach (Comp c in comps) WriteAcScan(ms, c, tables, ah: 1, al: 0);
    }

    private static void WriteDcScan(
        MemoryStream ms, Comp[] comps, JpegHuffmanTable[] tables, int mcusX, int mcusY, int ah, int al)
    {
        WriteSosHeader(ms, comps, ss: 0, se: 0, ah: ah, al: al);
        var writer = new JpegBitWriter(ms);
        DcScan(new Sink(writer, tables), comps, mcusX, mcusY, ah, al);
        writer.FlushToByte();
    }

    private static void WriteAcScan(MemoryStream ms, Comp c, JpegHuffmanTable[] tables, int ah, int al)
    {
        WriteSosHeader(ms, [c], ss: 1, se: 63, ah: ah, al: al);
        var writer = new JpegBitWriter(ms);
        AcScan(new Sink(writer, tables), c, ah, al);
        writer.FlushToByte();
    }

    // ---- Entropy coding -------------------------------------------------

    private static void DcScan(Sink sink, Comp[] comps, int mcusX, int mcusY, int ah, int al)
    {
        foreach (Comp c in comps)
            c.Pred = 0;

        for (int my = 0; my < mcusY; my++)
            for (int mx = 0; mx < mcusX; mx++)
                foreach (Comp c in comps)
                    for (int vy = 0; vy < c.V; vy++)
                        for (int hx = 0; hx < c.H; hx++)
                        {
                            int bx = mx * c.H + hx, by = my * c.V + vy;
                            int v = c.Coeff[(by * c.AllocBlocksPerLine + bx) * 64];
                            if (ah == 0)
                            {
                                int t = v >> al; // arithmetic point transform
                                int diff = t - c.Pred;
                                c.Pred = t;
                                int size = JpegTables.Magnitude(diff);
                                sink.Symbol(c.OutDc, size);
                                sink.Bits(JpegTables.ToBitPattern(diff, size), size);
                            }
                            else
                            {
                                sink.Bits((v >> al) & 1, 1); // refinement bit (not Huffman coded)
                            }
                        }
    }

    private static void AcScan(Sink sink, Comp c, int ah, int al)
    {
        int eobrun = 0;
        var pendingCorrections = new List<int>();

        void EmitEobrun()
        {
            if (eobrun == 0)
                return;
            int nbits = BitLength(eobrun) - 1; // floor(log2(eobrun))
            sink.Symbol(c.OutAc, nbits << 4);
            if (nbits > 0)
                sink.Bits(eobrun & ((1 << nbits) - 1), nbits);
            foreach (int bit in pendingCorrections)
                sink.Bits(bit, 1);
            pendingCorrections.Clear();
            eobrun = 0;
        }

        for (int by = 0; by < c.RealBlocksPerColumn; by++)
            for (int bx = 0; bx < c.RealBlocksPerLine; bx++)
            {
                int offset = (by * c.AllocBlocksPerLine + bx) * 64;
                if (ah == 0)
                    AcFirst(sink, c, offset, al, ref eobrun, EmitEobrun);
                else
                    AcRefine(sink, c, offset, al, ref eobrun, pendingCorrections, EmitEobrun);
            }

        EmitEobrun();
    }

    private static void AcFirst(Sink sink, Comp c, int offset, int al, ref int eobrun, Action emitEobrun)
    {
        int run = 0;
        for (int k = 1; k <= 63; k++)
        {
            int t = PointTransform(c.Coeff[offset + JpegTables.ZigZag[k]], al);
            if (t == 0)
            {
                run++;
                continue;
            }
            emitEobrun(); // a coefficient breaks any pending EOB run
            while (run > 15)
            {
                sink.Symbol(c.OutAc, 0xF0); // ZRL
                run -= 16;
            }
            int size = JpegTables.Magnitude(t);
            sink.Symbol(c.OutAc, (run << 4) | size);
            sink.Bits(JpegTables.ToBitPattern(t, size), size);
            run = 0;
        }
        if (run > 0) // trailing zeros → this block ends with an EOB; accumulate the run
            eobrun++;
    }

    private static void AcRefine(
        Sink sink, Comp c, int offset, int al, ref int eobrun, List<int> pendingCorrections, Action emitEobrun)
    {
        int eob = 0;
        for (int k = 1; k <= 63; k++)
            if ((Math.Abs(c.Coeff[offset + JpegTables.ZigZag[k]]) >> al) == 1)
                eob = k;

        int run = 0;
        var blockCorrections = new List<int>();
        for (int k = 1; k <= 63; k++)
        {
            int v = c.Coeff[offset + JpegTables.ZigZag[k]];
            int a = Math.Abs(v) >> al;
            if (a == 0)
            {
                run++;
                continue;
            }
            while (run > 15 && k <= eob)
            {
                emitEobrun();
                sink.Symbol(c.OutAc, 0xF0); // ZRL
                FlushList(sink, blockCorrections);
                run -= 16;
            }
            if (a > 1)
            {
                blockCorrections.Add(a & 1); // correction bit for an already-significant coefficient
                continue;
            }
            // a == 1: newly significant coefficient
            emitEobrun();
            sink.Symbol(c.OutAc, (run << 4) | 1);
            sink.Bits(v >= 0 ? 1 : 0, 1); // sign bit
            FlushList(sink, blockCorrections);
            run = 0;
        }
        if (run > 0 || blockCorrections.Count > 0)
        {
            eobrun++;
            pendingCorrections.AddRange(blockCorrections); // carried until the EOB run is flushed
        }
    }

    private static void FlushList(Sink sink, List<int> bits)
    {
        foreach (int b in bits)
            sink.Bits(b, 1);
        bits.Clear();
    }

    private static int BitLength(int x)
    {
        int n = 0;
        while (x > 0)
        {
            n++;
            x >>= 1;
        }
        return n;
    }

    /// <summary>Toward-zero point transform (preserves sign), matching the decoder's <c>value &lt;&lt; Al</c>.</summary>
    private static int PointTransform(int v, int al) => v < 0 ? -((-v) >> al) : v >> al;

    // ---- Coefficient + plane preparation --------------------------------

    private static void SetupCoeff(Comp c, int mcusX, int mcusY, int w, int h)
    {
        c.AllocBlocksPerLine = mcusX * c.H;
        int samplesPerLine = (w * c.H + 1) / 2;   // hMax = vMax = 2 for 4:2:0
        int samplesPerColumn = (h * c.V + 1) / 2;
        c.RealBlocksPerLine = (samplesPerLine + 7) / 8;
        c.RealBlocksPerColumn = (samplesPerColumn + 7) / 8;
        c.Coeff = new int[c.AllocBlocksPerLine * (mcusY * c.V) * 64];
    }

    private static void ComputeCoeff(Comp c, byte[] plane, int planeWidth, int[] quant)
    {
        int blocksPerLine = c.AllocBlocksPerLine;
        int blocksPerColumn = c.Coeff.Length / 64 / blocksPerLine;
        var block = new double[64];
        for (int by = 0; by < blocksPerColumn; by++)
            for (int bx = 0; bx < blocksPerLine; bx++)
            {
                for (int yy = 0; yy < 8; yy++)
                {
                    int row = (by * 8 + yy) * planeWidth + bx * 8;
                    for (int xx = 0; xx < 8; xx++)
                        block[yy * 8 + xx] = plane[row + xx] - 128.0;
                }
                JpegDct.Forward(block);
                int offset = (by * blocksPerLine + bx) * 64;
                for (int i = 0; i < 64; i++)
                    c.Coeff[offset + i] = (int)Math.Round(block[i] / quant[i]);
            }
    }

    private static void BuildPlanes(
        BPixelBuffer src, int width, int height, int yW, int yH, int cW, int cH,
        byte[] planeY, byte[] planeCb, byte[] planeCr)
    {
        byte[] rgba = src.Rgba;
        for (int yy = 0; yy < yH; yy++)
        {
            int sy = Math.Min(yy, height - 1);
            for (int xx = 0; xx < yW; xx++)
            {
                int sx = Math.Min(xx, width - 1);
                int p = (sy * width + sx) * 4;
                planeY[yy * yW + xx] = (byte)Math.Clamp(
                    (int)Math.Round(0.299 * rgba[p] + 0.587 * rgba[p + 1] + 0.114 * rgba[p + 2]), 0, 255);
            }
        }
        for (int cy = 0; cy < cH; cy++)
            for (int cx = 0; cx < cW; cx++)
            {
                double cbSum = 0, crSum = 0;
                for (int dy = 0; dy < 2; dy++)
                {
                    int sy = Math.Min(cy * 2 + dy, height - 1);
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = Math.Min(cx * 2 + dx, width - 1);
                        int p = (sy * width + sx) * 4;
                        double r = rgba[p], g = rgba[p + 1], b = rgba[p + 2];
                        cbSum += -0.168736 * r - 0.331264 * g + 0.5 * b + 128.0;
                        crSum += 0.5 * r - 0.418688 * g - 0.081312 * b + 128.0;
                    }
                }
                planeCb[cy * cW + cx] = (byte)Math.Clamp((int)Math.Round(cbSum / 4.0), 0, 255);
                planeCr[cy * cW + cx] = (byte)Math.Clamp((int)Math.Round(crSum / 4.0), 0, 255);
            }
    }

    // ---- Markers --------------------------------------------------------

    private static void WriteHeaders(
        MemoryStream ms, int width, int height, int[] qLuma, int[] qChroma, byte[][] bits, byte[][] vals)
    {
        ms.WriteByte(0xFF); ms.WriteByte(JpegTables.MarkerSoi);
        WriteMarker(ms, JpegTables.MarkerApp0, [
            (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);

        byte[] dqt = new byte[65 * 2];
        WriteQuant(dqt, 0, 0, qLuma);
        WriteQuant(dqt, 65, 1, qChroma);
        WriteMarker(ms, JpegTables.MarkerDqt, dqt);

        WriteMarker(ms, JpegTables.MarkerSof2, [
            8,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
            3,
            1, 0x22, 0,
            2, 0x11, 1,
            3, 0x11, 1]);

        using var dht = new MemoryStream();
        AppendHuffman(dht, 0x00, bits[0], vals[0]); // DC luma
        AppendHuffman(dht, 0x10, bits[1], vals[1]); // AC luma
        AppendHuffman(dht, 0x01, bits[2], vals[2]); // DC chroma
        AppendHuffman(dht, 0x11, bits[3], vals[3]); // AC chroma
        WriteMarker(ms, JpegTables.MarkerDht, dht.ToArray());
    }

    private static void WriteSosHeader(MemoryStream ms, Comp[] comps, int ss, int se, int ah, int al)
    {
        var payload = new List<byte> { (byte)comps.Length };
        foreach (Comp c in comps)
        {
            payload.Add((byte)c.Id);
            payload.Add((byte)((c.DcId << 4) | c.AcId));
        }
        payload.Add((byte)ss);
        payload.Add((byte)se);
        payload.Add((byte)((ah << 4) | al));
        WriteMarker(ms, JpegTables.MarkerSos, payload.ToArray());
    }

    private static void WriteQuant(byte[] dest, int offset, int tableId, int[] quant)
    {
        dest[offset] = (byte)tableId;
        for (int k = 0; k < 64; k++)
            dest[offset + 1 + k] = (byte)quant[JpegTables.ZigZag[k]];
    }

    private static void AppendHuffman(Stream s, byte classAndId, byte[] bits, byte[] values)
    {
        s.WriteByte(classAndId);
        s.Write(bits, 0, bits.Length);
        s.Write(values, 0, values.Length);
    }

    private static void WriteMarker(Stream s, byte marker, ReadOnlySpan<byte> payload)
    {
        s.WriteByte(0xFF);
        s.WriteByte(marker);
        int length = payload.Length + 2;
        s.WriteByte((byte)(length >> 8));
        s.WriteByte((byte)length);
        s.Write(payload);
    }
}
