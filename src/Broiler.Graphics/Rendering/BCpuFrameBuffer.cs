using Broiler.Graphics.Imaging;
using Broiler.Graphics.RenderList;
using System;

namespace Broiler.Graphics.Rendering;

/// <summary>A presentation surface owns its reusable CPU raster target.</summary>
internal interface ICpuRenderSurface
{
    BCpuFrameBuffer CpuFrame { get; }
}

internal sealed class BCpuFrameBuffer : IDisposable
{
    private BImageSurface? _surface;

    /// <summary>Returns borrowed pixels, valid until the next render or disposal.</summary>
    internal BBitmap Render(BImageRenderer renderer, BSurfaceDescriptor descriptor,
        BRenderList renderList, BFrameContext frameContext)
    {
        if (_surface is null)
            _surface = new BImageSurface(descriptor);
        else if (_surface.Size != descriptor.Size || _surface.DpiScale != descriptor.DpiScale)
            _surface.Resize(descriptor.Size, descriptor.DpiScale);

        renderer.Render(_surface, renderList, frameContext);
        return _surface.Bitmap;
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
    }
}
