using System;

namespace Broiler.Graphics.Windows.Native;

/// <summary>
/// DirectWrite interface IDs and enums needed to create a factory and text formats. Internal-only.
/// </summary>
internal static class DWriteNative
{
    // ---- Interface IIDs --------------------------------------------------------------------------

    /// <summary>IID_IDWriteFactory.</summary>
    internal static readonly Guid IID_IDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    // ---- Enums -----------------------------------------------------------------------------------

    internal enum DWRITE_FACTORY_TYPE : uint
    {
        SHARED = 0,
        ISOLATED = 1,
    }

    internal enum DWRITE_FONT_WEIGHT : uint
    {
        THIN = 100,
        LIGHT = 300,
        NORMAL = 400,
        MEDIUM = 500,
        SEMI_BOLD = 600,
        BOLD = 700,
        BLACK = 900,
    }

    internal enum DWRITE_FONT_STYLE : uint
    {
        NORMAL = 0,
        OBLIQUE = 1,
        ITALIC = 2,
    }

    internal enum DWRITE_FONT_STRETCH : uint
    {
        NORMAL = 5,
    }

    /// <summary>Maps a Core <see cref="BFontWeight"/> to the DirectWrite enum.</summary>
    internal static DWRITE_FONT_WEIGHT ToDWrite(BFontWeight weight) => (DWRITE_FONT_WEIGHT)(uint)weight;

    /// <summary>Maps a Core <see cref="BFontSlant"/> to the DirectWrite enum.</summary>
    internal static DWRITE_FONT_STYLE ToDWrite(BFontSlant slant) => slant switch
    {
        BFontSlant.Italic => DWRITE_FONT_STYLE.ITALIC,
        BFontSlant.Oblique => DWRITE_FONT_STYLE.OBLIQUE,
        _ => DWRITE_FONT_STYLE.NORMAL,
    };
}
