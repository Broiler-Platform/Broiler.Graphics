using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using System;

namespace Broiler.Graphics.WebAssembly;

/// <summary>Reusable CPU fallback with the browser's exact backing-pixel dimensions.</summary>
internal sealed class CanvasCpuFallback : IDisposable
{
    private BImageSurface? _surface;

    internal BBitmap Render(BImageRenderer renderer, BRenderList renderList,
        int backingWidth, int backingHeight, double dpiScale, double cssWidth, double cssHeight,
        BFrameContext frameContext)
    {
        var size = new BSize(cssWidth, cssHeight);
        if (_surface is null || _surface.Bitmap.Width != backingWidth || _surface.Bitmap.Height != backingHeight
            || _surface.Size != size || _surface.DpiScale != dpiScale)
        {
            // Browser backing sizes can be rounded independently of CSS size * DPR.
            var replacement = new BImageSurface(new BSurfaceDescriptor(size, dpiScale), backingWidth, backingHeight);
            _surface?.Dispose();
            _surface = replacement;
        }

        renderer.Render(_surface, renderList, frameContext);
        return _surface.Bitmap;
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
    }
}
