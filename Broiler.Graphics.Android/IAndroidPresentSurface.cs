namespace Broiler.Graphics.Android;

/// <summary>
/// The surface contract <see cref="AndroidOpenGlEsRenderer"/> presents through, mirroring
/// <c>ILinuxOpenGlPresentSurface</c>.
/// </summary>
internal interface IAndroidPresentSurface : IBroilerSurface
{
    BSurfaceDescriptor Descriptor { get; }

    void Present(BBitmap bitmap, bool vsync);

    BBitmap ReadToBitmap();
}
