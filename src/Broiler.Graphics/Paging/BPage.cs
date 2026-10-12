using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Broiler.Graphics.Paging;

/// <summary>
/// One fixed page: its size in points and what is drawn on it, recorded once and replayed by
/// every consumer - a print preview at any zoom, a printer at its own resolution, a PDF.
/// </summary>
/// <remarks>
/// <para>
/// A page owns everything it draws with. Its images are carried as pixels, keyed by the
/// handle its commands name, rather than as handles into some renderer: a page laid out
/// once is printed through a renderer that did not exist when it was laid out, so a backend
/// handle in a page would name nothing by then. <see cref="BPageImageCache"/> uploads the
/// pixels to whichever target replays the page and rewrites the handles as it goes.
/// </para>
/// <para>
/// Immutable. The commands are copied out of the builder's list when the page is built, and
/// the image pixels are copied in when they are added, so no later change to either reaches
/// a page already handed to a print job.
/// </para>
/// </remarks>
public sealed class BPage
{
    private readonly BRenderCommand[] _commands;
    private readonly ReadOnlyDictionary<BImageHandle, BPixelBuffer> _images;

    internal BPage(BSize size, BRenderCommand[] commands, Dictionary<BImageHandle, BPixelBuffer> images)
    {
        Size = size;
        _commands = commands;
        _images = new ReadOnlyDictionary<BImageHandle, BPixelBuffer>(images);
        Commands = Array.AsReadOnly(_commands);
    }

    /// <summary>The page in points: its width and height on the sheet.</summary>
    public BSize Size { get; }

    /// <summary>What the page draws, in page coordinates (points from its top-left corner).</summary>
    public IReadOnlyList<BRenderCommand> Commands { get; }

    /// <summary>
    /// The images the page draws, by the handle its <see cref="BRenderCommand.DrawImage"/>
    /// commands name. The pixels belong to the page; do not modify them.
    /// </summary>
    public IReadOnlyDictionary<BImageHandle, BPixelBuffer> Images => _images;

    /// <summary>
    /// Appends the page's commands to <paramref name="target"/>, rewriting each image handle
    /// through <paramref name="resolveImage"/>. An image the resolver answers with
    /// <see cref="BImageHandle.Invalid"/> is left out rather than drawn with a handle the
    /// target does not know.
    /// </summary>
    /// <remarks>
    /// Wrap the call in <see cref="BRenderList.PushTransform"/> to place or scale the page,
    /// and in <see cref="BRenderList.PushClip"/> to keep it inside its sheet: the page's own
    /// commands are balanced, so neither stack is disturbed.
    /// </remarks>
    public void AppendTo(BRenderList target, Func<BImageHandle, BImageHandle> resolveImage)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(resolveImage);

        foreach (BRenderCommand command in _commands)
        {
            if (command is BRenderCommand.DrawImage image)
            {
                BImageHandle resolved = resolveImage(image.Image);
                if (!resolved.IsValid)
                    continue;

                target.Append(image with { Image = resolved });
                continue;
            }

            target.Append(command);
        }
    }
}

/// <summary>
/// Records one <see cref="BPage"/>: draw into <see cref="Content"/> in points, add every image
/// the page draws through <see cref="AddImage"/>, then <see cref="Build"/>.
/// </summary>
public sealed class BPageBuilder
{
    private readonly Dictionary<BImageHandle, BPixelBuffer> _images = [];

    public BPageBuilder(BSize size)
    {
        if (!double.IsFinite(size.Width) || size.Width <= 0 || !double.IsFinite(size.Height) || size.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "A page must have a positive, finite size.");

        Size = size;
    }

    /// <summary>The page size in points.</summary>
    public BSize Size { get; }

    /// <summary>The list to draw the page into, in points from its top-left corner.</summary>
    public BRenderList Content { get; } = new();

    /// <summary>
    /// Adds an image to the page and returns the handle to draw it with. The pixels are
    /// copied, so the caller may reuse or change its buffer afterwards.
    /// </summary>
    public BImageHandle AddImage(BPixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var copy = new BPixelBuffer(pixels.Width, pixels.Height, (byte[])pixels.Rgba.Clone());
        BImageHandle handle = BImageHandle.FromId(BResourceIds.NextImageId(), new BSize(pixels.Width, pixels.Height));
        _images[handle] = copy;
        return handle;
    }

    /// <summary>
    /// Freezes what has been drawn into a page. Throws when the clip or transform stacks are
    /// unbalanced, or when a command draws an image that was not added to this builder - a
    /// page must not reach for a renderer's images, which will not exist where it is printed.
    /// </summary>
    public BPage Build()
    {
        Content.Validate();

        var commands = new BRenderCommand[Content.Count];
        var used = new Dictionary<BImageHandle, BPixelBuffer>();
        for (int i = 0; i < commands.Length; i++)
        {
            BRenderCommand command = Content.Commands[i];
            if (command is BRenderCommand.DrawImage image)
            {
                if (!_images.TryGetValue(image.Image, out BPixelBuffer? pixels))
                {
                    throw new InvalidOperationException(
                        $"The page draws {image.Image}, which was not added with {nameof(AddImage)}.");
                }

                used[image.Image] = pixels;
            }

            commands[i] = command;
        }

        return new BPage(Size, commands, used);
    }
}
