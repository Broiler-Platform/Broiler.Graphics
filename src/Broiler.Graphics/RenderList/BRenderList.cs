using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Text;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.RenderList;

/// <summary>
/// An ordered, mutable recording of draw commands with a read-only command view.
/// Do not modify a list while a backend is validating or replaying it. Concurrent replay
/// is supported when no caller modifies the recording.
/// </summary>
public sealed class BRenderList
{
    private readonly List<BRenderCommand> _commands;
    private readonly IReadOnlyList<BRenderCommand> _commandView;
    private volatile bool _validated = true;

    public BRenderList(int capacity = 0)
    {
        _commands = capacity > 0 ? new List<BRenderCommand>(capacity) : [];
        _commandView = _commands.AsReadOnly();
    }

    /// <summary>The recorded commands in the exact order they were issued.</summary>
    public IReadOnlyList<BRenderCommand> Commands => _commandView;

    public int Count => _commands.Count;

    public void Clear()
    {
        _commands.Clear();
        _validated = true;
    }

    private void Add(BRenderCommand command)
    {
        _validated = false;
        _commands.Add(command);
    }

    public void FillRect(BRect rect, BColor color) => Add(new BRenderCommand.FillRect(rect, color));

    public void StrokeRect(BRect rect, BColor color, double thickness)
    {
        if (thickness < 0)
            throw new ArgumentOutOfRangeException(nameof(thickness), "Stroke thickness must be non-negative.");

        Add(new BRenderCommand.StrokeRect(rect, color, thickness));
    }

    public void FillRoundedRect(BRect rect, BColor color, double radiusX, double radiusY)
    {
        if (radiusX < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusX), "Corner radius must be non-negative.");

        if (radiusY < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusY), "Corner radius must be non-negative.");

        if (radiusX == 0 || radiusY == 0)
        {
            FillRect(rect, color);
            return;
        }

        Add(new BRenderCommand.FillRoundedRect(rect, color, radiusX, radiusY));
    }

    public void StrokeRoundedRect(BRect rect, BColor color, double radiusX, double radiusY, double thickness)
    {
        if (radiusX < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusX), "Corner radius must be non-negative.");

        if (radiusY < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusY), "Corner radius must be non-negative.");

        if (thickness < 0)
            throw new ArgumentOutOfRangeException(nameof(thickness), "Stroke thickness must be non-negative.");

        if (radiusX == 0 || radiusY == 0)
        {
            StrokeRect(rect, color, thickness);
            return;
        }

        Add(new BRenderCommand.StrokeRoundedRect(rect, color, radiusX, radiusY, thickness));
    }

    /// <summary>
    /// Records a filled triangle. A degenerate triangle - one whose corners are collinear, so it
    /// has no area - is dropped rather than recorded, because backends disagree about what to do
    /// with a zero-area fill and none of them would draw anything useful.
    /// </summary>
    public void FillTriangle(BPoint a, BPoint b, BPoint c, BColor color)
    {
        double twiceArea = ((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y));
        if (double.IsNaN(twiceArea) || Math.Abs(twiceArea) < 1e-9)
            return;

        Add(new BRenderCommand.FillTriangle(a, b, c, color));
    }

    public void DrawText(BTextRun text, BPoint origin)
    {
        ArgumentNullException.ThrowIfNull(text);
        Add(new BRenderCommand.DrawText(text, origin));
    }

    // Kept, without a sampling, for callers built against the four-parameter method, which a new
    // optional parameter on the same method would no longer bind to: Broiler.HTML 0.1.0-preview.24
    // stopped drawing pages with a MissingMethodException.
    public void DrawImage(BImageHandle image, BRect source, BRect destination, double opacity = 1.0) =>
        DrawImage(image, source, destination, opacity, BImageSampling.Linear);

    public void DrawImage(BImageHandle image, BRect source, BRect destination, double opacity,
        BImageSampling sampling)
    {
        if (opacity is < 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be within [0, 1].");

        Add(new BRenderCommand.DrawImage(image, source, destination, opacity, sampling));
    }

    public void PushClip(BRect rect) => Add(new BRenderCommand.PushClip(rect));

    public void PopClip() => Add(new BRenderCommand.PopClip());

    public void PushTransform(BMatrix3x2 transform) => Add(new BRenderCommand.PushTransform(transform));

    public void PopTransform() => Add(new BRenderCommand.PopTransform());

    /// <summary>
    /// Records a command that was built elsewhere - one replayed out of another list, or out
    /// of a <see cref="Paging.BPage"/>.
    /// </summary>
    /// <remarks>
    /// The command goes through the same method a caller drawing it directly would use, so it
    /// is held to the same checks and normalizations: a record constructed by hand with a
    /// negative stroke cannot enter a list this way, and a zero-radius rounded rectangle is
    /// recorded as the plain rectangle it draws.
    /// </remarks>
    public void Append(BRenderCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (command)
        {
            case BRenderCommand.FillRect c:
                FillRect(c.Rect, c.Color);
                break;
            case BRenderCommand.StrokeRect c:
                StrokeRect(c.Rect, c.Color, c.Thickness);
                break;
            case BRenderCommand.FillRoundedRect c:
                FillRoundedRect(c.Rect, c.Color, c.RadiusX, c.RadiusY);
                break;
            case BRenderCommand.StrokeRoundedRect c:
                StrokeRoundedRect(c.Rect, c.Color, c.RadiusX, c.RadiusY, c.Thickness);
                break;
            case BRenderCommand.FillTriangle c:
                FillTriangle(c.A, c.B, c.C, c.Color);
                break;
            case BRenderCommand.DrawText c:
                DrawText(c.Text, c.Origin);
                break;
            case BRenderCommand.DrawImage c:
                DrawImage(c.Image, c.Source, c.Destination, c.Opacity, c.Sampling);
                break;
            case BRenderCommand.PushClip c:
                PushClip(c.Rect);
                break;
            case BRenderCommand.PopClip:
                PopClip();
                break;
            case BRenderCommand.PushTransform c:
                PushTransform(c.Transform);
                break;
            case BRenderCommand.PopTransform:
                PopTransform();
                break;
            default:
                throw new ArgumentException($"Unknown render command {command.GetType().Name}.", nameof(command));
        }
    }

    /// <summary>Records every command of <paramref name="other"/>, in order, through <see cref="Append(BRenderCommand)"/>.</summary>
    public void Append(BRenderList other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
            throw new ArgumentException("A list cannot be appended to itself.", nameof(other));

        foreach (BRenderCommand command in other.Commands)
            Append(command);
    }

    /// <summary>
    /// Verifies that the clip and transform stacks are balanced and never underflow. Throws
    /// <see cref="InvalidOperationException"/> if a Pop has no matching Push, or if any stack is
    /// left non-empty at the end of the list. Successful validation is cached until the
    /// next mutation. Backends should call this before replay.
    /// </summary>
    public void Validate()
    {
        if (_validated)
            return;

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

        _validated = true;
    }
}
