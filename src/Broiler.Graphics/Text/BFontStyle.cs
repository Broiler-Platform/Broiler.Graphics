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
/// An immutable description of a font: family, size, weight and slant.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The size is in the logical units of the surface the text is drawn
/// on</strong>, and the name used to claim it was pixels. It never was. A
/// surface descriptor carries a scale, the replay applies it to the geometry the
/// font produces, and what a caller puts here is whatever its own coordinate
/// space measures in — points for a page laid out in points, device-independent
/// pixels for a window.
/// </para>
/// <para>
/// That is why the unit is not baked into the name. Calling it pixels invited a
/// caller measuring in points to hand its value over unconverted, which does not
/// fail: it renders text at the wrong size, in a way nothing catches and a
/// reader has to notice. Calling it points would invite the same mistake from
/// the other direction. A caller crossing between the two converts explicitly,
/// and <see cref="PointsToPixels"/> is what it converts with.
/// </para>
/// </remarks>
public sealed record BFontStyle(
    string FamilyName,
    double Size,
    BFontWeight Weight = BFontWeight.Normal,
    BFontSlant Slant = BFontSlant.Normal)
{
    /// <summary>Points per inch.</summary>
    public const double PointsPerInch = 72.0;

    /// <summary>
    /// Device-independent pixels per inch: the CSS reference density, and the
    /// unit a window's coordinate space measures in before its own DPI scale.
    /// </summary>
    public const double PixelsPerInch = 96.0;

    /// <summary>A reasonable default used when no font is specified.</summary>
    public static BFontStyle Default { get; } = new("sans-serif", 16.0);

    /// <summary>
    /// Converts a size in points to device-independent pixels, for a caller
    /// taking a document measurement into a surface that measures in them.
    /// </summary>
    public static double PointsToPixels(double points) => points * PixelsPerInch / PointsPerInch;

    /// <summary>The inverse of <see cref="PointsToPixels"/>.</summary>
    public static double PixelsToPoints(double pixels) => pixels * PointsPerInch / PixelsPerInch;
}
