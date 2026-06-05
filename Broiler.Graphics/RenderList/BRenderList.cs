using System;
using System.Collections.Generic;

namespace Broiler.Graphics;

/// <summary>
/// An ordered, immutable-once-read recording of draw commands. Higher layers record into it; a
/// backend replays it. The list is a pure data structure with no backend dependencies.
/// </summary>
public sealed class BRenderList
{
    private readonly List<BRenderCommand> _commands;

    public BRenderList(int capacity = 0)
    {
        _commands = capacity > 0 ? new List<BRenderCommand>(capacity) : new List<BRenderCommand>();
    }

    /// <summary>The recorded commands in the exact order they were issued.</summary>
    public IReadOnlyList<BRenderCommand> Commands => _commands;

    public int Count => _commands.Count;

    public void Clear() => _commands.Clear();

    public void FillRect(BRect rect, BColor color) =>
        _commands.Add(new BRenderCommand.FillRect(rect, color));

    public void StrokeRect(BRect rect, BColor color, double thickness)
    {
        if (thickness < 0)
            throw new ArgumentOutOfRangeException(nameof(thickness), "Stroke thickness must be non-negative.");

        _commands.Add(new BRenderCommand.StrokeRect(rect, color, thickness));
    }

    public void DrawText(BTextRun text, BPoint origin)
    {
        ArgumentNullException.ThrowIfNull(text);
        _commands.Add(new BRenderCommand.DrawText(text, origin));
    }

    public void DrawImage(BImageHandle image, BRect source, BRect destination, double opacity = 1.0)
    {
        if (opacity is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be within [0, 1].");

        _commands.Add(new BRenderCommand.DrawImage(image, source, destination, opacity));
    }

    public void PushClip(BRect rect) =>
        _commands.Add(new BRenderCommand.PushClip(rect));

    public void PopClip() =>
        _commands.Add(new BRenderCommand.PopClip());

    public void PushTransform(BMatrix3x2 transform) =>
        _commands.Add(new BRenderCommand.PushTransform(transform));

    public void PopTransform() =>
        _commands.Add(new BRenderCommand.PopTransform());

    /// <summary>
    /// Verifies that the clip and transform stacks are balanced and never underflow. Throws
    /// <see cref="InvalidOperationException"/> if a Pop has no matching Push, or if any stack is
    /// left non-empty at the end of the list. Backends should call this before replay.
    /// </summary>
    public void Validate()
    {
        int clipDepth = 0;
        int transformDepth = 0;

        for (int i = 0; i < _commands.Count; i++)
        {
            switch (_commands[i])
            {
                case BRenderCommand.PushClip:
                    clipDepth++;
                    break;
                case BRenderCommand.PopClip:
                    clipDepth--;
                    if (clipDepth < 0)
                        throw new InvalidOperationException($"PopClip without matching PushClip at command index {i}.");
                    break;
                case BRenderCommand.PushTransform:
                    transformDepth++;
                    break;
                case BRenderCommand.PopTransform:
                    transformDepth--;
                    if (transformDepth < 0)
                        throw new InvalidOperationException($"PopTransform without matching PushTransform at command index {i}.");
                    break;
            }
        }

        if (clipDepth != 0)
            throw new InvalidOperationException($"Unbalanced clip stack: {clipDepth} clip(s) not popped.");

        if (transformDepth != 0)
            throw new InvalidOperationException($"Unbalanced transform stack: {transformDepth} transform(s) not popped.");
    }
}
