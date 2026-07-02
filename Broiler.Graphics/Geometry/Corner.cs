namespace Broiler.Graphics;

/// <summary>
/// Identifies one corner of an axis-aligned rectangle. Used by path builders
/// when arcing a rounded corner. Platform-neutral geometry primitive, shared by
/// the higher-level rendering adapters.
/// </summary>
public enum Corner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
