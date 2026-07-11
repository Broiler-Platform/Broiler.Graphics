using System;
using System.Collections.Generic;

namespace Broiler.Graphics.WebAssembly;

/// <summary>
/// Plans a <see cref="BRenderList"/> into a batched Canvas 2D replay stream
/// (<see cref="CanvasFrame"/>). This is the platform-neutral heart of the direct-Canvas
/// backend: it contains no DOM/JS/Canvas type and is fully unit-testable off the browser.
/// <para>
/// The planner owns the logical clip and transform stacks itself, so the browser side
/// never mirrors Broiler's stack semantics. Broiler validates clip and transform stacks
/// as <i>independent</i> stacks and permits interleavings such as
/// <c>PushClip, PushTransform, PopClip, draw, PopTransform</c>; the planner resolves each
/// drawing command to an absolute device rectangle plus the current intersected clip, so
/// that a clip pop never disturbs an active transform (and vice versa). This is the
/// managed-side equivalent of "reconstruct Canvas state as required" and avoids
/// implementing clip/transform pops as naive Canvas <c>save</c>/<c>restore</c>.
/// </para>
/// <para>
/// Transform semantics follow <see cref="CanvasTransformPolicy"/> (axis-aligned
/// bounding-box emulation), keeping the CPU renderer a pixel-exact oracle.
/// </para>
/// A single planner instance is reused across frames; its buffers grow to a steady size
/// and are then reused with no per-frame full-frame allocation.
/// </summary>
public sealed class CanvasFramePlanner
{
    private readonly List<ClipState> _clipStack = [];
    private readonly Stack<BMatrix3x2> _transformStack = new();
    private readonly List<string> _strings = [];

    private double[] _stream = new double[256];
    private int _length;
    private int _opCount;

    private BMatrix3x2 _current = BMatrix3x2.Identity;
    private BMatrix3x2 _pixelScale = BMatrix3x2.Identity;

    private ClipState _emittedClip = ClipState.None;
    private bool _fallback;

    /// <summary>
    /// Plans <paramref name="renderList"/> for a surface with the given backing pixel size
    /// and device-pixel ratio. The render list is validated first (its clip/transform stacks
    /// must balance). The returned frame is a view over planner-owned buffers and is valid
    /// only until the next <see cref="Plan"/> call.
    /// </summary>
    public CanvasFrame Plan(
        BRenderList renderList,
        int backingWidth,
        int backingHeight,
        double dpiScale,
        BColor clearColor)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        renderList.Validate();

        Reset(dpiScale);

        foreach (BRenderCommand command in renderList.Commands)
            Dispatch(command);

        return new CanvasFrame(
            _stream,
            _length,
            _strings,
            _opCount,
            backingWidth,
            backingHeight,
            clearColor.ToArgb(),
            _fallback);
    }

    private void Reset(double dpiScale)
    {
        _length = 0;
        _opCount = 0;
        _strings.Clear();
        _clipStack.Clear();
        _transformStack.Clear();
        _current = BMatrix3x2.Identity;
        _pixelScale = BMatrix3x2.Scale(dpiScale, dpiScale);
        _emittedClip = ClipState.None;
        _fallback = false;
    }

    private void Dispatch(BRenderCommand command)
    {
        switch (command)
        {
            case BRenderCommand.FillRect c:
                PlanFillRect(c);
                break;
            case BRenderCommand.StrokeRect c:
                PlanStrokeRect(c);
                break;
            case BRenderCommand.FillRoundedRect c:
                PlanFillRoundedRect(c);
                break;
            case BRenderCommand.StrokeRoundedRect c:
                PlanStrokeRoundedRect(c);
                break;
            case BRenderCommand.DrawText c:
                PlanDrawText(c);
                break;
            case BRenderCommand.DrawImage c:
                PlanDrawImage(c);
                break;
            case BRenderCommand.PushClip c:
                PushClip(c.Rect);
                break;
            case BRenderCommand.PopClip:
                PopClip();
                break;
            case BRenderCommand.PushTransform c:
                _transformStack.Push(_current);
                _current *= c.Transform;
                break;
            case BRenderCommand.PopTransform:
                _current = _transformStack.Pop();
                break;
            default:
                // Unreachable for the closed BRenderCommand hierarchy; kept so a future
                // command that this planner does not understand forces a whole-frame CPU
                // fallback rather than silently dropping.
                _fallback = true;
                break;
        }
    }

    private void PlanFillRect(BRenderCommand.FillRect command)
    {
        if (command.Color.A == 0)
            return;

        BRect rect = Device(command.Rect);
        if (!CanvasTransformPolicy.IsDrawable(rect))
            return;

        EnsureClip();
        EmitOp(CanvasReplayOp.FillRect);
        EmitRect(rect);
        EmitColor(command.Color);
    }

    private void PlanStrokeRect(BRenderCommand.StrokeRect command)
    {
        if (command.Color.A == 0 || command.Thickness <= 0)
            return;

        BRect rect = Device(command.Rect);
        if (!CanvasTransformPolicy.IsDrawable(rect))
            return;

        EnsureClip();
        EmitOp(CanvasReplayOp.StrokeRect);
        EmitRect(rect);
        Emit(Math.Max(1.0, command.Thickness * AverageScale()));
        EmitColor(command.Color);
    }

    private void PlanFillRoundedRect(BRenderCommand.FillRoundedRect command)
    {
        if (command.Color.A == 0)
            return;

        BRect rect = Device(command.Rect);
        if (!CanvasTransformPolicy.IsDrawable(rect))
            return;

        double scale = AverageScale();
        EnsureClip();
        EmitOp(CanvasReplayOp.FillRoundedRect);
        EmitRect(rect);
        Emit(command.RadiusX * scale);
        Emit(command.RadiusY * scale);
        EmitColor(command.Color);
    }

    private void PlanStrokeRoundedRect(BRenderCommand.StrokeRoundedRect command)
    {
        if (command.Color.A == 0 || command.Thickness <= 0)
            return;

        BRect rect = Device(command.Rect);
        if (!CanvasTransformPolicy.IsDrawable(rect))
            return;

        double scale = AverageScale();
        EnsureClip();
        EmitOp(CanvasReplayOp.StrokeRoundedRect);
        EmitRect(rect);
        Emit(command.RadiusX * scale);
        Emit(command.RadiusY * scale);
        Emit(Math.Max(1.0, command.Thickness * scale));
        EmitColor(command.Color);
    }

    private void PlanDrawText(BRenderCommand.DrawText command)
    {
        BTextRun run = command.Text;
        if (string.IsNullOrEmpty(run.Text) || run.Color.A == 0 || run.Font.SizeInPixels <= 0)
            return;

        double fontSize = Math.Max(1.0, run.Font.SizeInPixels);
        BMatrix3x2 transform = _current * _pixelScale;

        // Match the CPU renderer's baseline assumption (origin.Y + 0.8 * fontSize),
        // then bake the transform into a device-space baseline point. Route A renders
        // this through Canvas fillText with textBaseline = "alphabetic"; text is not part
        // of the CPU/Canvas pixel-checksum gate.
        BPoint baseline = transform.Transform(new BPoint(command.Origin.X, command.Origin.Y + (fontSize * 0.8)));
        if (!double.IsFinite(baseline.X) || !double.IsFinite(baseline.Y))
            return;

        int stringIndex = _strings.Count;
        _strings.Add(run.Text);

        EnsureClip();
        EmitOp(CanvasReplayOp.DrawText);
        Emit(baseline.X);
        Emit(baseline.Y);
        Emit(fontSize * AverageScale());
        Emit((double)(int)run.Font.Weight);
        Emit(run.Font.Slant == BFontSlant.Normal ? 0.0 : 1.0);
        EmitColor(run.Color);
        Emit(stringIndex);
    }

    private void PlanDrawImage(BRenderCommand.DrawImage command)
    {
        if (command.Opacity <= 0 || !command.Image.IsValid)
            return;

        BRect destination = Device(command.Destination);
        if (!CanvasTransformPolicy.IsDrawable(destination))
            return;

        BRect source = command.Source;
        if (!CanvasTransformPolicy.IsDrawable(source))
            source = new BRect(0, 0, command.Image.PixelSize.Width, command.Image.PixelSize.Height);

        EnsureClip();
        EmitOp(CanvasReplayOp.DrawImage);
        Emit(command.Image.Handle.Id);
        EmitRect(source);
        EmitRect(destination);
        Emit(Math.Min(1.0, command.Opacity));
    }

    private void PushClip(BRect rect)
    {
        BRect device = Device(rect);
        ClipState current = CurrentClip;
        ClipState next = current.HasClip ? current.Intersect(device) : ClipState.Clipped(device);
        _clipStack.Add(next);
    }

    private void PopClip()
    {
        if (_clipStack.Count > 0)
            _clipStack.RemoveAt(_clipStack.Count - 1);
    }

    private ClipState CurrentClip => _clipStack.Count > 0 ? _clipStack[^1] : ClipState.None;

    /// <summary>Emits a clip op if the current clip differs from what the replay side last applied.</summary>
    private void EnsureClip()
    {
        ClipState current = CurrentClip;
        if (current.Equals(_emittedClip))
            return;

        if (current.HasClip)
        {
            EmitOp(CanvasReplayOp.SetClip);
            EmitRect(current.Rect);
        }
        else
        {
            EmitOp(CanvasReplayOp.ClearClip);
        }

        _emittedClip = current;
    }

    private BRect Device(BRect rect) => CanvasTransformPolicy.ToDeviceAabb(_current * _pixelScale, rect);

    private double AverageScale() => CanvasTransformPolicy.AverageScale(_current * _pixelScale);

    /// <summary>Writes an op code and counts it. Operands are written with <see cref="Emit(double)"/>.</summary>
    private void EmitOp(int opCode)
    {
        EnsureCapacity(1);
        _stream[_length++] = opCode;
        _opCount++;
    }

    private void Emit(double value)
    {
        EnsureCapacity(1);
        _stream[_length++] = value;
    }

    private void EmitColor(BColor color) => Emit(color.ToArgb());

    private void EmitRect(BRect rect)
    {
        EnsureCapacity(4);
        _stream[_length++] = rect.X;
        _stream[_length++] = rect.Y;
        _stream[_length++] = rect.Width;
        _stream[_length++] = rect.Height;
    }

    private void EnsureCapacity(int additional)
    {
        int required = _length + additional;
        if (required <= _stream.Length)
            return;

        int capacity = _stream.Length * 2;
        while (capacity < required)
            capacity *= 2;

        Array.Resize(ref _stream, capacity);
    }

    /// <summary>Current intersected clip: either "no clip" (full surface) or an absolute device rectangle.</summary>
    private readonly record struct ClipState(bool HasClip, BRect Rect)
    {
        internal static ClipState None => new(false, BRect.Empty);

        internal static ClipState Clipped(BRect rect) => new(true, rect);

        internal ClipState Intersect(BRect rect) => new(true, Rect.Intersect(rect));
    }
}
