using System;
using System.Collections.Generic;
using System.Drawing;

namespace Broiler.Graphics;

/// <summary>
/// Abstraction for image loading handlers, letting layout/paint consume a
/// resolved <see cref="RImage"/> without a direct dependency on the concrete
/// <c>ImageLoadHandler</c> implementation.
/// </summary>
public interface IImageLoadHandler : IDisposable
{
    /// <summary>
    /// The loaded image, or null if not yet loaded or failed.
    /// </summary>
    RImage Image { get; }

    /// <summary>
    /// The sub-rectangle of the image to use, or <see cref="RectangleF.Empty"/> for the entire image.
    /// </summary>
    RectangleF Rectangle { get; }

    /// <summary>
    /// Initiates image loading from the specified source.
    /// </summary>
    void LoadImage(string src, Dictionary<string, string> attributes, Uri baseUrl);
}
