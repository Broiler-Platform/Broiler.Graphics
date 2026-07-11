using System;
using System.Collections.Generic;

namespace Broiler.Graphics.WebAssembly;

/// <summary>
/// The result of planning one <c>BRenderList</c> into a batched Canvas 2D replay
/// stream. This is a view over buffers owned and reused by <see cref="CanvasFramePlanner"/>;
/// it is valid only until the next <see cref="CanvasFramePlanner.Plan"/> call. The
/// browser presenter reads it and performs a single interop submission per frame.
/// </summary>
public readonly struct CanvasFrame
{
    private readonly double[] _stream;
    private readonly List<string> _strings;

    internal CanvasFrame(
        double[] stream,
        int streamLength,
        List<string> strings,
        int opCount,
        int backingWidth,
        int backingHeight,
        uint clearColorArgb,
        bool requiresCpuFallback)
    {
        _stream = stream;
        StreamLength = streamLength;
        _strings = strings;
        OpCount = opCount;
        BackingWidth = backingWidth;
        BackingHeight = backingHeight;
        ClearColorArgb = clearColorArgb;
        RequiresCpuFallback = requiresCpuFallback;
    }

    /// <summary>The backing (device-pixel) command stream. Only the first <see cref="StreamLength"/> entries are valid.</summary>
    public double[] Stream => _stream;

    /// <summary>Number of valid entries in <see cref="Stream"/>.</summary>
    public int StreamLength { get; }

    /// <summary>Out-of-band string table referenced by <c>DrawText</c> ops via their string index.</summary>
    public IReadOnlyList<string> Strings => _strings;

    /// <summary>Number of replay ops encoded in the stream (for diagnostics and coverage assertions).</summary>
    public int OpCount { get; }

    /// <summary>Backing surface width in device pixels.</summary>
    public int BackingWidth { get; }

    /// <summary>Backing surface height in device pixels.</summary>
    public int BackingHeight { get; }

    /// <summary>Clear color packed as 0xAARRGGBB.</summary>
    public uint ClearColorArgb { get; }

    /// <summary>
    /// When true, the planner could not represent the frame natively and the caller
    /// must present the whole frame through the CPU raster fallback instead of the
    /// batched Canvas stream. With the bounding-box transform policy every current
    /// render command is representable, so this is a defensive forward-compatibility
    /// signal rather than an expected path.
    /// </summary>
    public bool RequiresCpuFallback { get; }

    /// <summary>A copy of the valid stream prefix; used by tests and non-span consumers.</summary>
    public double[] ToArray()
    {
        var copy = new double[StreamLength];
        Array.Copy(_stream, copy, StreamLength);
        return copy;
    }
}
