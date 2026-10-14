using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Paging;

/// <summary>
/// Uploads the images pages draw to one target - a renderer, a UI host - and remembers what
/// it uploaded, so a preview that replays the same page every frame sends each image once.
/// </summary>
/// <remarks>
/// Page image handles are allocated from the process-wide resource id sequence, so one
/// cache can serve pages from any number of documents without two of them colliding.
/// Not thread-safe: use one cache per thread or per target.
/// </remarks>
public sealed class BPageImageCache : IDisposable
{
    private readonly Func<BPixelBuffer, BImageHandle> _createImage;
    private readonly Action<BImageHandle>? _releaseImage;
    private readonly Dictionary<BImageHandle, BImageHandle> _uploaded = [];
    private bool _disposed;

    /// <param name="createImage">Uploads pixels to the target and returns its handle for them.</param>
    /// <param name="releaseImage">Releases a handle <paramref name="createImage"/> returned, if the target needs it.</param>
    public BPageImageCache(Func<BPixelBuffer, BImageHandle> createImage, Action<BImageHandle>? releaseImage = null)
    {
        _createImage = createImage ?? throw new ArgumentNullException(nameof(createImage));
        _releaseImage = releaseImage;
    }

    /// <summary>A cache that uploads to <paramref name="renderer"/>.</summary>
    public static BPageImageCache For(IBroilerRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        return new BPageImageCache(renderer.CreateImage, renderer.ReleaseImage);
    }

    /// <summary>The number of images uploaded and not yet released.</summary>
    public int Count => _uploaded.Count;

    /// <summary>
    /// Appends <paramref name="page"/> to <paramref name="target"/> with its images rewritten
    /// to this cache's target, uploading any it has not seen.
    /// </summary>
    public void AppendPage(BPage page, BRenderList target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(page);
        page.AppendTo(target, handle => Resolve(page, handle));
    }

    /// <summary>
    /// The target's handle for one of <paramref name="page"/>'s images, or
    /// <see cref="BImageHandle.Invalid"/> when the page does not hold that image.
    /// </summary>
    public BImageHandle Resolve(BPage page, BImageHandle pageImage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(page);

        if (_uploaded.TryGetValue(pageImage, out BImageHandle uploaded))
            return uploaded;

        if (!page.Images.TryGetValue(pageImage, out BPixelBuffer? pixels))
            return BImageHandle.Invalid;

        uploaded = _createImage(pixels);
        _uploaded[pageImage] = uploaded;
        return uploaded;
    }

    /// <summary>Releases every image this cache uploaded.</summary>
    public void Clear()
    {
        if (_releaseImage is not null)
        {
            foreach (BImageHandle handle in _uploaded.Values)
                _releaseImage(handle);
        }

        _uploaded.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        _disposed = true;
    }
}
