using System;

namespace Broiler.Graphics;

/// <summary>
/// Per-frame state handed to a renderer alongside a <see cref="BRenderList"/>. Immutable; create a
/// new instance per frame. Carries the clear color, a monotonically increasing frame index, and the
/// options in effect for this frame.
/// </summary>
public readonly record struct BFrameContext(
    BColor ClearColor,
    long FrameIndex,
    BRenderOptions Options)
{
    public BFrameContext(BColor clearColor)
        : this(clearColor, 0, BRenderOptions.Default)
    {
    }

    public static BFrameContext Default => new(BColor.White, 0, BRenderOptions.Default);

    public BFrameContext WithFrameIndex(long frameIndex) => this with { FrameIndex = frameIndex };
}
