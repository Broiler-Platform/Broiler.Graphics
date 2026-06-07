using System;

namespace Broiler.Graphics;

/// <summary>
/// Creates surfaces and replays render lists onto them. A renderer owns a backend device and must be
/// disposed. The interface is fully safe and platform-neutral; implementations live in backends such
/// as <c>Broiler.Graphics.Windows</c>.
/// </summary>
public interface IBroilerRenderer : IDisposable
{
    /// <summary>Creates a new surface from the descriptor. The caller owns and must dispose it.</summary>
    IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor);

    /// <summary>
    /// Decodes encoded image bytes (PNG/JPEG/BMP/…) through the active
    /// <see cref="BImageCodec"/> and uploads them as a backend image resource,
    /// returning a handle to use in <see cref="BRenderCommand.DrawImage"/>.
    /// </summary>
    BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage);

    /// <summary>Uploads an already-decoded pixel buffer as a backend image resource.</summary>
    BImageHandle CreateImage(BPixelBuffer pixels);

    /// <summary>Releases an image resource created by <see cref="CreateImage(BPixelBuffer)"/>.</summary>
    void ReleaseImage(BImageHandle image);

    /// <summary>
    /// Replays <paramref name="renderList"/> onto <paramref name="surface"/> using
    /// <paramref name="frameContext"/>. Implementations should call
    /// <see cref="BRenderList.Validate"/> before replay. May throw <see cref="BDeviceLostException"/>
    /// if the GPU device was reset.
    /// </summary>
    void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext);
}
