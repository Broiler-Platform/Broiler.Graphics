using System;

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
