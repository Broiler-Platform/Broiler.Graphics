namespace Broiler.Graphics;

/// <summary>Font slant.</summary>
public enum BFontSlant
{
    Normal = 0,
    Italic = 1,
    Oblique = 2,
}

/// <summary>
/// Common font weights, matching CSS/DirectWrite numeric values.
/// </summary>
public enum BFontWeight
{
    Thin = 100,
    Light = 300,
    Normal = 400,
    Medium = 500,
    SemiBold = 600,
    Bold = 700,
    Black = 900,
}

/// <summary>
/// An immutable description of a font: family, size (in layout units), weight and slant.
/// </summary>
public sealed record BFontStyle(
    string FamilyName,
    double SizeInPixels,
    BFontWeight Weight = BFontWeight.Normal,
    BFontSlant Slant = BFontSlant.Normal)
{
    /// <summary>A reasonable default used when no font is specified.</summary>
    public static BFontStyle Default { get; } = new("sans-serif", 16.0);
}
