namespace Broiler.Graphics.Text;

/// <summary>Big-endian reads used by the permissive TrueType table parsers.</summary>
internal static class FontBinaryReader
{
    internal static int ReadU16(byte[] data, int offset)
    {
        if (offset < 0 || offset + 1 >= data.Length) return 0;
        return (data[offset] << 8) | data[offset + 1];
    }

    internal static uint ReadU32(byte[] data, int offset)
    {
        if (offset < 0 || offset + 3 >= data.Length) return 0;
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
             | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}
