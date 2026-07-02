#nullable disable
using System;
using System.IO;
using System.IO.Compression;

namespace Broiler.Graphics;

/// <summary>
/// Decodes a WOFF 1.0 container into a raw sfnt (TrueType/OpenType) byte array
/// so it can be parsed by <see cref="TrueTypeFont"/>.  WOFF stores each sfnt
/// table individually, optionally zlib-compressed; this reconstructs the sfnt
/// table directory and table data.  WOFF2 (Brotli + transformed glyf) is not
/// supported.
/// </summary>
public static class WoffDecoder
{
    /// <summary>True when <paramref name="data"/> starts with the WOFF 1.0 signature.</summary>
    public static bool IsWoff(byte[] data) =>
        data != null && data.Length >= 4 &&
        data[0] == (byte)'w' && data[1] == (byte)'O' && data[2] == (byte)'F' && data[3] == (byte)'F';

    /// <summary>
    /// Decodes WOFF 1.0 bytes to sfnt bytes, or returns <c>null</c> if the input
    /// is not WOFF 1.0 or is malformed.
    /// </summary>
    public static byte[] Decode(byte[] woff)
    {
        if (!IsWoff(woff) || woff.Length < 44)
            return null;

        try
        {
            uint flavor = U32(woff, 4);
            int numTables = U16(woff, 12);
            if (numTables <= 0)
                return null;

            // Table directory begins immediately after the 44-byte header.
            var tags = new uint[numTables];
            var checksums = new uint[numTables];
            var data = new byte[numTables][];
            int dir = 44;
            for (int i = 0; i < numTables; i++)
            {
                if (dir + 20 > woff.Length)
                    return null;
                tags[i] = U32(woff, dir);
                uint offset = U32(woff, dir + 4);
                uint compLength = U32(woff, dir + 8);
                uint origLength = U32(woff, dir + 12);
                checksums[i] = U32(woff, dir + 16);
                dir += 20;

                if (offset + compLength > woff.Length)
                    return null;

                data[i] = compLength < origLength
                    ? Inflate(woff, (int)offset, (int)compLength, (int)origLength)
                    : Slice(woff, (int)offset, (int)origLength);
                if (data[i] == null)
                    return null;
            }

            // Output sfnt: 12-byte header + 16-byte record per table + 4-aligned
            // table data, with records sorted by tag (sfnt requirement).
            var orderByTag = new int[numTables];
            for (int i = 0; i < numTables; i++)
                orderByTag[i] = i;
            Array.Sort(orderByTag, (a, b) => tags[a].CompareTo(tags[b]));

            int headerSize = 12;
            int recordSize = 16 * numTables;
            var outOffset = new int[numTables];
            int cursor = headerSize + recordSize;
            foreach (int i in orderByTag)
            {
                outOffset[i] = cursor;
                cursor += Align4(data[i].Length);
            }

            var sfnt = new byte[cursor];

            // Offset table.
            WriteU32(sfnt, 0, flavor);
            WriteU16(sfnt, 4, (ushort)numTables);
            int maxPow2 = 1, entrySelector = 0;
            while (maxPow2 * 2 <= numTables)
            {
                maxPow2 *= 2;
                entrySelector++;
            }
            int searchRange = maxPow2 * 16;
            WriteU16(sfnt, 6, (ushort)searchRange);
            WriteU16(sfnt, 8, (ushort)entrySelector);
            WriteU16(sfnt, 10, (ushort)(numTables * 16 - searchRange));

            // Table records + data.
            int rec = 12;
            foreach (int i in orderByTag)
            {
                WriteU32(sfnt, rec, tags[i]);
                WriteU32(sfnt, rec + 4, checksums[i]);
                WriteU32(sfnt, rec + 8, (uint)outOffset[i]);
                WriteU32(sfnt, rec + 12, (uint)data[i].Length);
                rec += 16;
                Array.Copy(data[i], 0, sfnt, outOffset[i], data[i].Length);
            }

            return sfnt;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Inflate(byte[] src, int offset, int length, int origLength)
    {
        using var ms = new MemoryStream(src, offset, length);
        using var z = new ZLibStream(ms, CompressionMode.Decompress);
        var output = new byte[origLength];
        int read = 0;
        while (read < origLength)
        {
            int r = z.Read(output, read, origLength - read);
            if (r <= 0)
                break;
            read += r;
        }
        return read == origLength ? output : null;
    }

    private static byte[] Slice(byte[] src, int offset, int length)
    {
        var dst = new byte[length];
        Array.Copy(src, offset, dst, 0, length);
        return dst;
    }

    private static int Align4(int n) => (n + 3) & ~3;

    private static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    private static uint U32(byte[] d, int o) =>
        ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

    private static void WriteU16(byte[] d, int o, ushort v)
    {
        d[o] = (byte)(v >> 8);
        d[o + 1] = (byte)v;
    }

    private static void WriteU32(byte[] d, int o, uint v)
    {
        d[o] = (byte)(v >> 24);
        d[o + 1] = (byte)(v >> 16);
        d[o + 2] = (byte)(v >> 8);
        d[o + 3] = (byte)v;
    }
}
