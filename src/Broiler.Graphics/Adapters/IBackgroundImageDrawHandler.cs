using System.Drawing;

namespace Broiler.Graphics;

/// <summary>
/// Read-only view of the background-image CSS properties that drawing handlers
/// require, decoupling the handler from any concrete box/style representation.
/// </summary>
public interface IBackgroundRenderData
{
    string BackgroundPosition { get; }
    string BackgroundRepeat { get; }
}

/// <summary>
/// Abstraction for background-image drawing handlers, letting the rendering
/// pipeline invoke background painting without a direct dependency on the
/// concrete <c>BackgroundImageDrawHandler</c> implementation.
/// </summary>
public interface IBackgroundImageDrawHandler
{
    /// <summary>
    /// Draws a background image within the specified rectangle.
    /// </summary>
    void DrawBackgroundImage(RGraphics g, IBackgroundRenderData box, IImageLoadHandler imageHandler, RectangleF rectangle);
}
