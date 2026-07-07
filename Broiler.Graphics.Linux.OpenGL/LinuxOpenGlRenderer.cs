using System;
using System.Collections.Generic;
using Broiler.Graphics.Linux;

namespace Broiler.Graphics.Linux.OpenGL;

public sealed class LinuxOpenGlRenderer : IBroilerRenderer
{
    private readonly BImageRenderer _cpuRenderer = new();
    private readonly LinuxOpenGlRendererOptions _options;
    private bool _disposed;

    public LinuxOpenGlRenderer()
        : this(LinuxOpenGlRendererOptions.Default)
    {
    }

    public LinuxOpenGlRenderer(LinuxOpenGlRendererOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        BImageCodec.UseManagedIfUnset();
    }

    public static IReadOnlyList<LinuxNativeLibraryStatus> CheckDependencies() =>
        LinuxNativeLibraryProbe.Check(
        [
            LinuxGraphicsDependencies.Egl,
            LinuxGraphicsDependencies.OpenGl,
            LinuxGraphicsDependencies.WaylandClient,
            LinuxGraphicsDependencies.Xcb,
            LinuxGraphicsDependencies.X11,
        ]);

    public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new LinuxOpenGlSurface(descriptor, _options);
    }

    public LinuxOpenGlX11WindowSurface CreateX11WindowSurface(
        BSurfaceDescriptor descriptor,
        string title = "Broiler.Graphics OpenGL")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new LinuxOpenGlX11WindowSurface(descriptor, _options, title);
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

        if (surface is not ILinuxOpenGlPresentSurface openGlSurface)
            throw new ArgumentException("Surface was not created by this renderer.", nameof(surface));

        renderList.Validate();
        if (openGlSurface.TryReplayNative(renderList, frameContext, frameContext.Options.VSync, out _))
            return;

        using BBitmap frame = _cpuRenderer.RenderToImage(renderList, openGlSurface.Descriptor, frameContext);
        openGlSurface.Present(frame, frameContext.Options.VSync);
    }

    public BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderList);

        using LinuxOpenGlSurface surface = new(descriptor, _options);
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
