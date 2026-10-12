using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using System;
using System.Threading;

namespace Broiler.Graphics.Paging;

/// <summary>
/// One horizontal strip of a rasterized page: <see cref="Pixels"/> holds the page's pixel rows
/// from <see cref="Top"/> to <see cref="Top"/> + <c>Pixels.Height</c>, the full page width.
/// </summary>
/// <remarks>The bitmap belongs to the rasterizer and is disposed when the callback returns; copy it to keep it.</remarks>
public readonly struct BPageBand(int top, BBitmap pixels, int pageWidth, int pageHeight)
{
    /// <summary>The first page pixel row the band holds.</summary>
    public int Top { get; } = top;

    /// <summary>The band's pixels, straight-alpha RGBA.</summary>
    public BBitmap Pixels { get; } = pixels;

    /// <summary>The whole page's width in pixels, which is also the band's.</summary>
    public int PageWidth { get; } = pageWidth;

    /// <summary>The whole page's height in pixels.</summary>
    public int PageHeight { get; } = pageHeight;

    /// <summary>True for the band that holds the page's last row.</summary>
    public bool IsLast => Top + Pixels.Height >= PageHeight;
}

/// <summary>
/// Rasterizes pages at a printer's resolution through the software renderer, a band of rows
/// at a time.
/// </summary>
/// <remarks>
/// <para>
/// Banding is what makes this usable for printing. A <see cref="BBitmap"/> is one array of
/// four bytes a pixel, so an A4 page at 600 dpi is 139 MB in one piece and an A3 page at
/// 1200 dpi passes the two-gigabyte array limit outright. A band of a few hundred rows is a
/// few megabytes whatever the page, and a printer is fed in strips anyway.
/// </para>
/// <para>
/// Each band replays the whole page translated up by the band's top, into a surface exactly
/// as tall as the band. The page's coordinates are points and the surface scale is DPI/72,
/// so a band boundary falls on a whole pixel row and adjacent bands meet without a seam or
/// an overlap.
/// </para>
/// </remarks>
public static class BPageRasterizer
{
    /// <summary>Rows per band when the caller does not choose.</summary>
    public const int DefaultBandHeight = 512;

    /// <summary>The highest resolution accepted, in dots per inch.</summary>
    public const double MaximumDpi = 2400;

    /// <summary>The pixel size of <paramref name="pageSize"/> (in points) at <paramref name="dpi"/>.</summary>
    public static (int Width, int Height) GetPixelSize(BSize pageSize, double dpi)
    {
        double scale = ValidateDpi(dpi) / BPaperSize.PointsPerInch;
        return (ToPixels(pageSize.Width * scale), ToPixels(pageSize.Height * scale));
    }

    /// <summary>
    /// Rasterizes <paramref name="page"/> whole. Prefer <see cref="RenderBands"/> for printing;
    /// this is for previews and tests, where the page is small.
    /// </summary>
    public static BBitmap Render(BPage page, double dpi, BColor background, IBroilerRenderer? renderer = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        (_, int height) = GetPixelSize(page.Size, dpi);

        BBitmap? result = null;
        RenderBands(page, dpi, background, height, band => result = band.Pixels.Copy(), renderer);
        return result!;
    }

    /// <summary>
    /// Rasterizes <paramref name="page"/> at <paramref name="dpi"/>, handing each band of at
    /// most <paramref name="bandHeight"/> rows to <paramref name="onBand"/>, top to bottom.
    /// </summary>
    /// <param name="renderer">The renderer to use; a new software renderer when null.</param>
    public static void RenderBands(BPage page, double dpi, BColor background, int bandHeight, Action<BPageBand> onBand,
        IBroilerRenderer? renderer = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(onBand);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandHeight);

        double scale = ValidateDpi(dpi) / BPaperSize.PointsPerInch;
        (int pageWidth, int pageHeight) = GetPixelSize(page.Size, dpi);

        BImageRenderer? owned = renderer is null ? new BImageRenderer() : null;
        IBroilerRenderer target = renderer ?? owned!;
        try
        {
            using BPageImageCache images = BPageImageCache.For(target);
            var frame = new BFrameContext(background, 0, BRenderOptions.Default);

            for (int top = 0; top < pageHeight; top += bandHeight)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int rows = Math.Min(bandHeight, pageHeight - top);
                var list = new BRenderList();
                list.PushTransform(BMatrix3x2.Translation(0, -top / scale));
                images.AppendPage(page, list);
                list.PopTransform();

                var descriptor = new BSurfaceDescriptor(new BSize(ToLogical(pageWidth, scale), ToLogical(rows, scale)), scale);
                using BBitmap band = target.RenderToImage(list, descriptor, frame);
                onBand(new BPageBand(top, band, pageWidth, pageHeight));
            }
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private static double ValidateDpi(double dpi)
    {
        if (!double.IsFinite(dpi) || dpi <= 0 || dpi > MaximumDpi)
            throw new ArgumentOutOfRangeException(nameof(dpi), $"Resolution must be positive and at most {MaximumDpi} dpi.");

        return dpi;
    }

    // Partial pixels round up, as a surface rounds them, but a dimension that is a whole
    // number of pixels up to rounding error is that number, not one more: Letter's 612 pt at
    // 300 dpi is 2550 pixels even when the product comes out a hair above it.
    private static int ToPixels(double pixels)
    {
        double rounded = Math.Ceiling(pixels - 1e-6);
        if (rounded < 1 || rounded > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(pixels), "The page is too large to rasterize at this resolution.");

        return (int)rounded;
    }

    // The surface rounds its logical size up to whole pixels; a hair under the exact quotient
    // keeps that rounding from adding a row.
    private static double ToLogical(int pixels, double scale) => (pixels - 1e-4) / scale;
}
