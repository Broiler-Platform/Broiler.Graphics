using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Broiler.Graphics.WebAssembly;

/// <summary>
/// The direct-Canvas 2D presenter: the browser-owned half of the Phase 5 rendering path.
/// It plans each <see cref="BRenderList"/> into a batched op stream
/// (<see cref="CanvasFramePlanner"/>) and submits it in a single interop call per frame,
/// replacing the CPU raster-to-<c>ImageData</c> path that missed the Phase 2 frame-rate gate.
/// <para>
/// Image resources are realized synchronously: <see cref="CreateImage(BPixelBuffer)"/> uploads
/// decoded RGBA into a reusable per-resource canvas keyed by id (no <c>createImageBitmap</c>
/// promise), so a synchronous present never silently omits an image. Encoded-image decoding is
/// intentionally not offered here; per the Phase 5 codec decision the application performs a
/// bounded <c>Broiler.Media</c> decode and passes a <see cref="BPixelBuffer"/>.
/// </para>
/// <para>
/// If the planner ever reports that a frame is not natively representable it is presented
/// whole through the CPU raster fallback (<see cref="BImageRenderer"/> + putImageData). With the
/// bounding-box transform policy no current command triggers this; it is a forward-compatibility
/// safety net.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserCanvasRenderer : IDisposable
{
    private readonly CanvasFramePlanner _planner = new();
    private readonly BImageRenderer _resources = new();
    private readonly HashSet<ulong> _liveImages = [];

    private BImageSurface? _fallbackSurface;
    private long _frameIndex;
    private bool _disposed;

    /// <summary>Imports the replay module from <paramref name="moduleUrl"/>. Await before any other call.</summary>
    public static Task LoadModuleAsync(string moduleUrl) => CanvasInterop.LoadModuleAsync(moduleUrl);

    /// <summary>Binds the presenter to the canvas identified by <paramref name="canvasSelector"/>.</summary>
    public void Initialize(string canvasSelector)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CanvasInterop.Initialize(canvasSelector);
    }

    /// <summary>Total frames presented (native path or fallback), for diagnostics.</summary>
    public long FrameIndex => _frameIndex;

    /// <summary>Uploads a decoded pixel buffer as a reusable image resource.</summary>
    public BImageHandle CreateImage(BPixelBuffer pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pixels);

        BImageHandle handle = _resources.CreateImage(pixels);
        CanvasInterop.UploadImage(handle.Handle.Id, pixels.Width, pixels.Height, pixels.Rgba);
        _liveImages.Add(handle.Handle.Id);
        return handle;
    }

    /// <summary>Releases an image resource created by <see cref="CreateImage(BPixelBuffer)"/>.</summary>
    public void ReleaseImage(BImageHandle image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_liveImages.Remove(image.Handle.Id))
            return;

        CanvasInterop.ReleaseImage(image.Handle.Id);
        _resources.ReleaseImage(image);
    }

    /// <summary>
    /// Plans and presents one frame. <paramref name="backingWidth"/>/<paramref name="backingHeight"/>
    /// are the device-pixel backing size (typically <c>Ceiling(css * dpiScale)</c>);
    /// <paramref name="cssWidth"/>/<paramref name="cssHeight"/> are the logical CSS box.
    /// </summary>
    public CanvasPresentResult PresentFrame(
        BRenderList renderList,
        int backingWidth,
        int backingHeight,
        double dpiScale,
        double cssWidth,
        double cssHeight,
        BColor clearColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderList);

        CanvasFrame frame = _planner.Plan(renderList, backingWidth, backingHeight, dpiScale, clearColor);

        bool usedFallback = frame.RequiresCpuFallback;
        if (usedFallback)
        {
            PresentCpuFallback(renderList, backingWidth, backingHeight, dpiScale, clearColor, cssWidth, cssHeight);
        }
        else
        {
            CanvasInterop.PresentFrame(
                frame.Stream,
                frame.StreamLength,
                CopyStrings(frame.Strings),
                backingWidth,
                backingHeight,
                frame.ClearColorArgb,
                cssWidth,
                cssHeight,
                frame.OpCount);
        }

        _frameIndex++;
        return new CanvasPresentResult(frame.OpCount, frame.StreamLength, usedFallback);
    }

    private void PresentCpuFallback(
        BRenderList renderList,
        int backingWidth,
        int backingHeight,
        double dpiScale,
        BColor clearColor,
        double cssWidth,
        double cssHeight)
    {
        EnsureFallbackSurface(cssWidth, cssHeight, dpiScale);
        BImageSurface surface = _fallbackSurface!;
        _resources.Render(surface, renderList, new BFrameContext(clearColor, _frameIndex, BRenderOptions.Default));
        byte[] rgba = surface.Bitmap.ToPixelBuffer(copy: false).Rgba;
        CanvasInterop.PresentImageData(backingWidth, backingHeight, rgba, cssWidth, cssHeight);
    }

    private void EnsureFallbackSurface(double cssWidth, double cssHeight, double dpiScale)
    {
        var size = new BSize(cssWidth, cssHeight);
        if (_fallbackSurface is null)
        {
            _fallbackSurface = (BImageSurface)_resources.CreateSurface(
                new BSurfaceDescriptor(size, dpiScale, BPixelFormat.Rgba8, EnableTransparency: false));
            return;
        }

        if (!_fallbackSurface.Size.Equals(size) || !_fallbackSurface.DpiScale.Equals(dpiScale))
            _fallbackSurface.Resize(size, dpiScale);
    }

    private static string[] CopyStrings(IReadOnlyList<string> strings)
    {
        if (strings.Count == 0)
            return [];

        var copy = new string[strings.Count];
        for (int i = 0; i < strings.Count; i++)
            copy[i] = strings[i];

        return copy;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _fallbackSurface?.Dispose();
        _fallbackSurface = null;
        _resources.Dispose();
        _liveImages.Clear();
        CanvasInterop.Dispose();
    }
}

/// <summary>Per-frame diagnostics returned by <see cref="BrowserCanvasRenderer.PresentFrame"/>.</summary>
public readonly record struct CanvasPresentResult(int OpCount, int StreamLength, bool UsedCpuFallback);
