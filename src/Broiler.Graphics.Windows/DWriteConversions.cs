using Broiler.Graphics.Text;
using Broiler.Native.Windows.Direct2D;

namespace Broiler.Graphics.Windows;

internal static class DWriteConversions
{
    /// <summary>Maps a Core <see cref="BFontWeight"/> to the DirectWrite enum.</summary>
    internal static DWriteNative.DWRITE_FONT_WEIGHT ToDWrite(BFontWeight weight) => (DWriteNative.DWRITE_FONT_WEIGHT)(uint)weight;

    /// <summary>Maps a Core <see cref="BFontSlant"/> to the DirectWrite enum.</summary>
    internal static DWriteNative.DWRITE_FONT_STYLE ToDWrite(BFontSlant slant) => slant switch
    {
        BFontSlant.Italic => DWriteNative.DWRITE_FONT_STYLE.ITALIC,
        BFontSlant.Oblique => DWriteNative.DWRITE_FONT_STYLE.OBLIQUE,
        _ => DWriteNative.DWRITE_FONT_STYLE.NORMAL,
    };
}
