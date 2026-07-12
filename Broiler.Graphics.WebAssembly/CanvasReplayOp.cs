namespace Broiler.Graphics.WebAssembly;

/// <summary>
/// Op codes for the batched Canvas 2D replay stream produced by
/// <see cref="CanvasFramePlanner"/>. Each op is a self-describing record in a flat
/// <see cref="double"/> stream: the op code followed by a fixed number of operands
/// (see <see cref="Arity"/>). The JavaScript replay module mirrors these exact
/// numeric values; keep the two in sync.
/// <para>
/// All geometry in the stream is already in backing (device) pixels with an
/// identity Canvas transform: the planner bakes the Broiler transform and DPI
/// scale into axis-aligned device rectangles (see <see cref="CanvasTransformPolicy"/>),
/// and clips are resolved to absolute device rectangles. The replay module therefore
/// never manipulates the Canvas transform matrix and never mirrors Broiler's clip or
/// transform stacks.
/// </para>
/// </summary>
internal static class CanvasReplayOp
{
    /// <summary>Set the current clip to an absolute device rectangle: <c>x, y, w, h</c>.</summary>
    internal const int SetClip = 1;

    /// <summary>Clear the clip back to the full surface (no operands).</summary>
    internal const int ClearClip = 2;

    /// <summary>Fill a device rectangle with a solid color: <c>x, y, w, h, argb</c>.</summary>
    internal const int FillRect = 3;

    /// <summary>Stroke a device rectangle outline: <c>x, y, w, h, thickness, argb</c>.</summary>
    internal const int StrokeRect = 4;

    /// <summary>Fill a rounded device rectangle: <c>x, y, w, h, rx, ry, argb</c>.</summary>
    internal const int FillRoundedRect = 5;

    /// <summary>Stroke a rounded device rectangle: <c>x, y, w, h, rx, ry, thickness, argb</c>.</summary>
    internal const int StrokeRoundedRect = 6;

    /// <summary>
    /// Blit an image resource: <c>imageId, sx, sy, sw, sh, dx, dy, dw, dh, opacity</c>.
    /// Source is in image pixels; destination is a device rectangle.
    /// </summary>
    internal const int DrawImage = 7;

    /// <summary>
    /// Fill text: <c>baselineX, baselineY, fontPx, weight, italic, argb, stringIndex</c>.
    /// The string is carried out of band in the plan's string table. <c>italic</c> is
    /// 0 or 1; <c>weight</c> is the CSS numeric weight.
    /// </summary>
    internal const int DrawText = 8;

    /// <summary>Number of operands (excluding the op code) for a given op.</summary>
    internal static int Arity(int op) => op switch
    {
        SetClip => 4,
        ClearClip => 0,
        FillRect => 5,
        StrokeRect => 6,
        FillRoundedRect => 7,
        StrokeRoundedRect => 8,
        DrawImage => 10,
        DrawText => 7,
        _ => -1,
    };
}
