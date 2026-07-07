using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Linux.Vulkan;

public sealed class LinuxVulkanRenderer : IBroilerRenderer
{
    private readonly BImageRenderer _cpuRenderer = new();
    private readonly LinuxVulkanRendererOptions _options;
    private bool _disposed;

    public LinuxVulkanRenderer()
        : this(LinuxVulkanRendererOptions.Default)
    {
    }

    public LinuxVulkanRenderer(LinuxVulkanRendererOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        BImageCodec.UseManagedIfUnset();
    }

    public static IReadOnlyList<LinuxNativeLibraryStatus> CheckDependencies() =>
        LinuxNativeLibraryProbe.Check(
        [
            LinuxGraphicsDependencies.Vulkan,
            LinuxGraphicsDependencies.WaylandClient,
            LinuxGraphicsDependencies.Xcb,
        ]);

    public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new LinuxVulkanSurface(descriptor, _options);
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

        if (surface is not ILinuxVulkanPresentSurface vulkanSurface)
            throw new ArgumentException("Surface was not created by this renderer.", nameof(surface));

        renderList.Validate();
        using BBitmap frame = _cpuRenderer.RenderToImage(renderList, vulkanSurface.Descriptor, frameContext);
        vulkanSurface.Present(frame, frameContext.Options.VSync);
    }

    public BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderList);

        using LinuxVulkanSurface surface = new(descriptor, _options);
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
