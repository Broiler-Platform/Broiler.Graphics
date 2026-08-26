using System.Drawing;

namespace Broiler.Graphics.Adapters;

/// <summary>
/// Interface for resolving color names to color values.
/// Breaks the circular dependency between CSS parsing and the adapter layer.
/// </summary>
public interface IColorResolver
{
    /// <summary>
    /// Resolves a color name to its <see cref="Color"/> value.
    /// </summary>
    BColor GetColor(string colorName);

    /// <summary>
    /// Checks whether a font family is available.
    /// </summary>
    bool IsFontExists(string family);
}
