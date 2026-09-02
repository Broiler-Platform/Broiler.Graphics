namespace Broiler.Graphics;

/// <summary>
/// Base type for all recorded draw commands. The hierarchy is closed: the private constructor means
/// only the nested command records can derive from it, so backends can exhaustively switch over them.
/// </summary>
public abstract record BRenderCommand
{
    // Private ctor: only nested types (which have access to private members of the enclosing type)
    // may derive. This keeps the command set closed and switch-exhaustive.
    private protected BRenderCommand() { }

    /// <summary>Fills a rectangle with a solid color.</summary>
    public sealed record FillRect(BRect Rect, BColor Color) : BRenderCommand;

    /// <summary>Strokes a rectangle outline with a solid color and the given thickness.</summary>
    public sealed record StrokeRect(BRect Rect, BColor Color, double Thickness) : BRenderCommand;

    /// <summary>Fills a rounded rectangle with a solid color.</summary>
    public sealed record FillRoundedRect(BRect Rect, BColor Color, double RadiusX, double RadiusY) : BRenderCommand;

    /// <summary>Strokes a rounded rectangle outline with a solid color and the given thickness.</summary>
    public sealed record StrokeRoundedRect(
        BRect Rect,
        BColor Color,
        double RadiusX,
        double RadiusY,
        double Thickness) : BRenderCommand;

    /// <summary>
    /// Fills the triangle with the given corners with a solid color, using the same antialiasing
    /// the rectangle commands get.
    /// </summary>
    /// <remarks>
    /// The one primitive in the set that is not axis-aligned, and the reason it exists: a rectangle
    /// rotated into a diagonal is not portable. Only Direct2D and the Android hardware canvas apply
    /// a true affine transform - the CPU rasterizer and, deliberately matching it, the browser
    /// planner reduce a rotated shape to its bounding box - so an arrowhead or a chevron built from
    /// a turned rectangle renders as a square on three of the five backends. A triangle carries its
    /// corners in the command, so every backend draws the same shape.
    /// </remarks>
    public sealed record FillTriangle(BPoint A, BPoint B, BPoint C, BColor Color) : BRenderCommand;

    /// <summary>Draws a text run with its top-left origin at <paramref name="Origin"/>.</summary>
    public sealed record DrawText(BTextRun Text, BPoint Origin) : BRenderCommand;

    /// <summary>Draws (a region of) an image into a destination rectangle.</summary>
    public sealed record DrawImage(
        BImageHandle Image,
        BRect Source,
        BRect Destination,
        double Opacity) : BRenderCommand;

    /// <summary>Pushes a rectangular clip onto the clip stack.</summary>
    public sealed record PushClip(BRect Rect) : BRenderCommand;

    /// <summary>Pops the most recent clip.</summary>
    public sealed record PopClip : BRenderCommand;

    /// <summary>Pushes a transform onto the transform stack (concatenated with the current one).</summary>
    public sealed record PushTransform(BMatrix3x2 Transform) : BRenderCommand;

    /// <summary>Pops the most recent transform.</summary>
    public sealed record PopTransform : BRenderCommand;
}
