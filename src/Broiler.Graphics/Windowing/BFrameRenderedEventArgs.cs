using System;

namespace Broiler.Graphics.Windowing;

/// <summary>
/// Event arguments providing frame timing and index diagnostics.
/// </summary>
public sealed class BFrameRenderedEventArgs(long frameIndex, TimeSpan duration) : EventArgs
{
    /// <summary>The zero-based index of the rendered frame.</summary>
    public long FrameIndex { get; } = frameIndex;

    /// <summary>The wall-clock duration of the frame rendering pass.</summary>
    public TimeSpan Duration { get; } = duration;
}
