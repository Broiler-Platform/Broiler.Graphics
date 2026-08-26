using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Linux.OpenGL;

public sealed record LinuxOpenGlNativeReplayInspection(
    bool IsSupported,
    string Diagnostic,
    int NativeOperationCount);

public static class LinuxOpenGlNativeReplay
{
    public static LinuxOpenGlNativeReplayInspection Inspect(
        BRenderList renderList,
        BSurfaceDescriptor descriptor,
        BFrameContext frameContext)
    {
        ArgumentNullException.ThrowIfNull(renderList);

        return TryCreatePlan(renderList, descriptor, frameContext, out LinuxOpenGlNativeReplayPlan? plan, out string diagnostic)
            ? new LinuxOpenGlNativeReplayInspection(true, diagnostic, plan.NativeOperationCount)
            : new LinuxOpenGlNativeReplayInspection(false, diagnostic, 0);
    }

    internal static bool TryCreatePlan(
        BRenderList renderList,
        BSurfaceDescriptor descriptor,
        BFrameContext frameContext,
        out LinuxOpenGlNativeReplayPlan? plan,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(renderList);
        renderList.Validate();

        double dpiScale = IsPositiveFinite(descriptor.DpiScale) ? descriptor.DpiScale : 1.0;
        int pixelWidth = ToPixelDimension(descriptor.Size.Width, dpiScale, nameof(descriptor));
        int pixelHeight = ToPixelDimension(descriptor.Size.Height, dpiScale, nameof(descriptor));
        var fullClip = new PixelRect(0, 0, pixelWidth, pixelHeight);
        var clipStack = new Stack<PixelRect>();
        PixelRect currentClip = fullClip;
        var operations = new List<LinuxOpenGlNativeReplayOperation>();

        foreach (BRenderCommand command in renderList.Commands)
        {
            switch (command)
            {
                case BRenderCommand.FillRect fill:
                    if (!TryAddFill(fill.Rect, fill.Color, dpiScale, currentClip, operations, out diagnostic))
                    {
                        plan = null;
                        return false;
                    }

                    break;

                case BRenderCommand.StrokeRect stroke:
                    if (!TryAddStroke(stroke.Rect, stroke.Color, stroke.Thickness, dpiScale, currentClip, operations, out diagnostic))
                    {
                        plan = null;
                        return false;
                    }

                    break;

                case BRenderCommand.PushClip clip:
                    clipStack.Push(currentClip);
                    currentClip = currentClip.Intersect(ToPixelRect(clip.Rect, dpiScale, pixelWidth, pixelHeight));
                    break;

                case BRenderCommand.PopClip:
                    currentClip = clipStack.Pop();
                    break;

                case BRenderCommand.FillRoundedRect:
                    diagnostic = "OpenGL native replay does not yet support rounded rectangles; using CPU-present fallback.";
                    plan = null;
                    return false;

                case BRenderCommand.StrokeRoundedRect:
                    diagnostic = "OpenGL native replay does not yet support rounded rectangle strokes; using CPU-present fallback.";
                    plan = null;
                    return false;

                case BRenderCommand.DrawImage:
                    diagnostic = "OpenGL native replay does not yet support image draw commands; using CPU-present fallback.";
                    plan = null;
                    return false;

                case BRenderCommand.DrawText:
                    diagnostic = "OpenGL native replay does not yet support text commands; using CPU-present fallback.";
                    plan = null;
                    return false;

                case BRenderCommand.PushTransform:
                case BRenderCommand.PopTransform:
                    diagnostic = "OpenGL native replay does not yet support render-list transforms; using CPU-present fallback.";
                    plan = null;
                    return false;

                default:
                    diagnostic = "OpenGL native replay encountered an unknown render command; using CPU-present fallback.";
                    plan = null;
                    return false;
            }
        }

        plan = new LinuxOpenGlNativeReplayPlan(pixelWidth, pixelHeight, frameContext.ClearColor, operations);
        diagnostic = $"OpenGL native replay supports this render list with {plan.NativeOperationCount} GPU clear operation(s).";
        return true;
    }

    private static bool TryAddFill(
        BRect rect,
        BColor color,
        double dpiScale,
        PixelRect clip,
        List<LinuxOpenGlNativeReplayOperation> operations,
        out string diagnostic)
    {
        diagnostic = string.Empty;

        if (color.A == 0 || rect.IsEmpty)
            return true;

        if (color.A != 255)
        {
            diagnostic = "OpenGL native replay only supports transparent or opaque fill rectangles; using CPU-present fallback.";
            return false;
        }

        PixelRect pixelRect = ToUnclampedPixelRect(rect, dpiScale).Intersect(clip);
        if (!pixelRect.IsEmpty)
            operations.Add(new LinuxOpenGlNativeReplayOperation(pixelRect, color));

        return true;
    }

    private static bool TryAddStroke(
        BRect rect,
        BColor color,
        double thickness,
        double dpiScale,
        PixelRect clip,
        List<LinuxOpenGlNativeReplayOperation> operations,
        out string diagnostic)
    {
        diagnostic = string.Empty;

        if (color.A == 0 || thickness <= 0 || rect.IsEmpty)
            return true;

        if (color.A != 255)
        {
            diagnostic = "OpenGL native replay only supports transparent or opaque rectangle strokes; using CPU-present fallback.";
            return false;
        }

        double left = rect.Left * dpiScale;
        double top = rect.Top * dpiScale;
        double right = rect.Right * dpiScale;
        double bottom = rect.Bottom * dpiScale;
        double stroke = Math.Max(1.0, thickness * dpiScale);

        AddPixelFill(left, top, right - left, stroke, color, clip, operations);
        AddPixelFill(left, bottom - stroke, right - left, stroke, color, clip, operations);
        AddPixelFill(left, top, stroke, bottom - top, color, clip, operations);
        AddPixelFill(right - stroke, top, stroke, bottom - top, color, clip, operations);
        return true;
    }

    private static void AddPixelFill(
        double x,
        double y,
        double width,
        double height,
        BColor color,
        PixelRect clip,
        List<LinuxOpenGlNativeReplayOperation> operations)
    {
        PixelRect pixelRect = ToPixelRect(x, y, width, height).Intersect(clip);
        if (!pixelRect.IsEmpty)
            operations.Add(new LinuxOpenGlNativeReplayOperation(pixelRect, color));
    }

    private static PixelRect ToPixelRect(BRect rect, double dpiScale, int pixelWidth, int pixelHeight) =>
        ToUnclampedPixelRect(rect, dpiScale).Intersect(new PixelRect(0, 0, pixelWidth, pixelHeight));

    private static PixelRect ToUnclampedPixelRect(BRect rect, double dpiScale) =>
        ToPixelRect(rect.Left * dpiScale, rect.Top * dpiScale, rect.Width * dpiScale, rect.Height * dpiScale);

    private static PixelRect ToPixelRect(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
            return PixelRect.Empty;

        int left = (int)Math.Floor(x);
        int top = (int)Math.Floor(y);
        int right = (int)Math.Ceiling(x + width);
        int bottom = (int)Math.Ceiling(y + height);
        return right > left && bottom > top
            ? new PixelRect(left, top, right - left, bottom - top)
            : PixelRect.Empty;
    }

    private static int ToPixelDimension(double dip, double dpiScale, string name)
    {
        double effectiveScale = IsPositiveFinite(dpiScale) ? dpiScale : 1.0;
        double pixels = Math.Ceiling(dip * effectiveScale);
        if (pixels <= 0 || pixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Surface pixel dimensions are outside the supported range.");

        return (int)pixels;
    }

    private static bool IsPositiveFinite(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}

internal sealed class LinuxOpenGlNativeReplayPlan
{
    public LinuxOpenGlNativeReplayPlan(
        int pixelWidth,
        int pixelHeight,
        BColor clearColor,
        IReadOnlyList<LinuxOpenGlNativeReplayOperation> operations)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        ClearColor = clearColor;
        Operations = operations;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public BColor ClearColor { get; }

    public IReadOnlyList<LinuxOpenGlNativeReplayOperation> Operations { get; }

    public int NativeOperationCount => 1 + Operations.Count;
}

internal readonly record struct LinuxOpenGlNativeReplayOperation(PixelRect Rect, BColor Color);

internal readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect Empty => new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(X, other.X);
        int top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top
            ? new PixelRect(left, top, right - left, bottom - top)
            : Empty;
    }
}
