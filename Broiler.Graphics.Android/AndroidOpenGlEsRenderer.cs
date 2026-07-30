using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Android;

/// <summary>
/// The Android <see cref="IBroilerRenderer"/>: rasterizes on the CPU and presents through
/// EGL/OpenGL ES.
/// </summary>
/// <remarks>
/// Frames are produced by the shared <see cref="BImageRenderer"/>, exactly as on Linux and Windows,
/// and the GPU only uploads and blits the result. So Android inherits Broiler's rendering behavior
/// unchanged and adds no new layout, paint, or conformance surface — what is Android-specific here
/// is presentation, not rendering.
/// </remarks>
public sealed class AndroidOpenGlEsRenderer : IBroilerRenderer
{
    private readonly BImageRenderer _cpuRenderer = new();
    private readonly AndroidOpenGlEsRendererOptions _options;
    private bool _disposed;

    public AndroidOpenGlEsRenderer()
        : this(AndroidOpenGlEsRendererOptions.Default)
    {
    }

    public AndroidOpenGlEsRenderer(AndroidOpenGlEsRendererOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Reports whether the EGL, OpenGL ES, and native-window libraries can be loaded.</summary>
    public static IReadOnlyList<AndroidNativeLibraryStatus> CheckDependencies() =>
        AndroidGraphicsDependencies.CheckPresentationBaseline();

    /// <summary>Creates an off-screen surface. Used by <see cref="RenderToImage"/> and by tests.</summary>
    public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new AndroidOpenGlEsSurface(descriptor, _options);
    }

    /// <summary>
    /// Creates an on-screen surface. It is not presentable until the host attaches a native window
    /// from its <c>surfaceCreated</c> callback.
    /// </summary>
    public AndroidOpenGlEsWindowSurface CreateWindowSurface(BSurfaceDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new AndroidOpenGlEsWindowSurface(descriptor, _options);
    }

    public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _cpuRenderer.CreateImage(encodedImage);
    }

    public BImageHandle CreateImage(BPixelBuffer pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _cpuRenderer.CreateImage(pixels);
    }

    public void ReleaseImage(BImageHandle image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cpuRenderer.ReleaseImage(image);
    }

    public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(renderList);

        if (surface is not IAndroidPresentSurface presentSurface)
            throw new ArgumentException("Surface was not created by this renderer.", nameof(surface));

        renderList.Validate();

        using BBitmap frame = _cpuRenderer.RenderToImage(renderList, presentSurface.Descriptor, frameContext);
        presentSurface.Present(frame, frameContext.Options.VSync);
    }

    public BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderList);

        using AndroidOpenGlEsSurface surface = new(descriptor, _options);
        Render(surface, renderList, frameContext);
        return surface.ReadToBitmap();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cpuRenderer.Dispose();
    }
}
