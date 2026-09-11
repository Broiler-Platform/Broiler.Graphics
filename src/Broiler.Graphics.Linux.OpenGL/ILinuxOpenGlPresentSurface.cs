using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;

namespace Broiler.Graphics.Linux.OpenGL;

internal interface ILinuxOpenGlPresentSurface : IBroilerSurface
{
    BSurfaceDescriptor Descriptor { get; }

    bool TryReplayNative(BRenderList renderList, BFrameContext frameContext, bool vsync, out string diagnostic);

    void Present(BBitmap bitmap, bool vsync);

    BBitmap ReadToBitmap();
}
