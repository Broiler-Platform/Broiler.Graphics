using System;

namespace Broiler.Graphics;

/// <summary>
/// Platform-neutral font style flags. Values match the historical
/// <c>System.Drawing.FontStyle</c> bit layout so existing call sites keep their
/// semantics after the move off the OS-dependent GDI+ types.
/// </summary>
[Flags]
public enum FontStyle
{
    Regular = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikeout = 8,
}
