using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Paging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Coverage for the page model printing and PDF export share: the paper catalogue, page
/// setups, frozen pages that carry their own images, and banded rasterization.
/// </summary>
internal static class PagingTests
{
    internal static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("Paper: ISO and US sizes convert to points", PaperSizesAreInPoints));
        tests.Add(("Paper: names resolve ignoring case, spaces and hyphens", PaperNamesResolve));
        tests.Add(("Paper: a size matches its sheet in either orientation", PaperMatchesEitherOrientation));
        tests.Add(("Paper: a sheet is always stored portrait", PaperIsStoredPortrait));
        tests.Add(("Page setup: landscape turns the sheet and the content box follows", SetupLandscapeTurnsSheet));
        tests.Add(("Page setup: a with expression cannot set invalid margins", SetupRejectsInvalidMargins));
        tests.Add(("Page: later drawing into the builder does not reach a built page", PageIsFrozen));
        tests.Add(("Page: drawing an image the page does not hold is refused", PageRefusesForeignImage));
        tests.Add(("Page: added pixels are copied", PageCopiesImagePixels));
        tests.Add(("Render list: Append holds a command to the drawing method's checks", AppendValidatesCommands));
        tests.Add(("Image cache: an image is uploaded once however often its page is replayed", CacheUploadsOnce));
        tests.Add(("Image cache: clearing releases what was uploaded", CacheReleasesOnClear));
        tests.Add(("Rasterizer: page pixel size follows the resolution without an extra row", RasterPixelSize));
        tests.Add(("Rasterizer: bands stitch into exactly the whole page", BandsStitchIntoWholePage));
        tests.Add(("Rasterizer: a page's own image is drawn at the printer's resolution", RasterDrawsPageImage));
        tests.Add(("Page source: Select walks the requested pages lazily and skips missing ones", SourceSelectIsLazy));
    }

    private static void PaperSizesAreInPoints()
    {
        AssertClose(595.276, BPaperSize.A4.Width, 0.001);
        AssertClose(841.89, BPaperSize.A4.Height, 0.001);
        AssertClose(612, BPaperSize.Letter.Width, 1e-9);
        AssertClose(792, BPaperSize.Letter.Height, 1e-9);
        AssertClose(1224, BPaperSize.Tabloid.Height, 1e-9);
        AssertClose(210, BPaperSize.PointsToMillimetres(BPaperSize.A4.Width), 1e-9);
    }

    private static void PaperNamesResolve()
    {
        AssertEx.IsTrue(BPaperSize.TryFind("a4", out BPaperSize a4));
        AssertEx.AreEqual(BPaperSize.A4, a4);
        AssertEx.IsTrue(BPaperSize.TryFind("JIS-B5", out BPaperSize jis));
        AssertEx.AreEqual(BPaperSize.JisB5, jis);
        AssertEx.IsTrue(BPaperSize.TryFind(" jis b4 ", out BPaperSize jisB4));
        AssertEx.AreEqual(BPaperSize.JisB4, jisB4);
        AssertEx.IsTrue(BPaperSize.TryFind("ledger", out BPaperSize ledger));
        AssertEx.AreEqual(BPaperSize.Tabloid, ledger);
        AssertEx.IsFalse(BPaperSize.TryFind("folio", out _));
        AssertEx.IsFalse(BPaperSize.TryFind(null, out _));
    }

    private static void PaperMatchesEitherOrientation()
    {
        AssertEx.IsTrue(BPaperSize.TryMatch(new BSize(792, 612), out BPaperSize letter));
        AssertEx.AreEqual(BPaperSize.Letter, letter);

        // A4 as a driver reports it: 2100 x 2970 tenths of a millimetre.
        AssertEx.IsTrue(BPaperSize.TryMatch(new BSize(BPaperSize.MillimetresToPoints(210.0), BPaperSize.MillimetresToPoints(297.0)), out BPaperSize a4));
        AssertEx.AreEqual(BPaperSize.A4, a4);

        AssertEx.IsFalse(BPaperSize.TryMatch(new BSize(500, 700), out _));
        BPaperSize custom = BPaperSize.FromSize(new BSize(700, 500));
        AssertEx.AreEqual(500.0, custom.Width);
        AssertEx.IsTrue(custom.Name.Contains("mm", StringComparison.Ordinal), custom.Name);
    }

    private static void PaperIsStoredPortrait()
    {
        var sheet = new BPaperSize("Wide", 800, 400);
        AssertEx.AreEqual(400.0, sheet.Width);
        AssertEx.AreEqual(800.0, sheet.Height);
        AssertEx.AreEqual(new BSize(800, 400), sheet.Oriented(BPageOrientation.Landscape));
    }

    private static void SetupLandscapeTurnsSheet()
    {
        var setup = new BPageSetup(BPaperSize.Letter, BPageOrientation.Landscape, new BPageMargins(10, 20, 30, 40));
        AssertEx.AreEqual(new BSize(792, 612), setup.PageSize);
        AssertEx.AreEqual(new BRect(10, 20, 792 - 40, 612 - 60), setup.ContentBox);

        BPageSetup fromSize = BPageSetup.ForPageSize(new BSize(842, 595), BPageMargins.None);
        AssertEx.AreEqual(BPaperSize.A4, fromSize.Paper);
        AssertEx.AreEqual(BPageOrientation.Landscape, fromSize.Orientation);
    }

    private static void SetupRejectsInvalidMargins()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => _ = BPageSetup.Default with { Margins = new BPageMargins(-1, 0, 0, 0) });
        AssertThrows<ArgumentOutOfRangeException>(() => _ = new BPageSetup(BPaperSize.A4, BPageOrientation.Portrait, BPageMargins.Uniform(double.NaN)));
        AssertThrows<ArgumentException>(() => _ = BPageSetup.Default with { Paper = default });
    }

    private static void PageIsFrozen()
    {
        var builder = new BPageBuilder(new BSize(100, 100));
        builder.Content.FillRect(new BRect(0, 0, 10, 10), BColor.Red);
        BPage page = builder.Build();

        builder.Content.FillRect(new BRect(10, 10, 10, 10), BColor.Blue);
        AssertEx.AreEqual(1, page.Commands.Count);
        AssertEx.AreEqual(2, builder.Build().Commands.Count);
    }

    private static void PageRefusesForeignImage()
    {
        var builder = new BPageBuilder(new BSize(100, 100));
        BImageHandle foreign = BImageHandle.FromId(ulong.MaxValue - 7, new BSize(1, 1));
        builder.Content.DrawImage(foreign, new BRect(0, 0, 1, 1), new BRect(0, 0, 10, 10));
        AssertThrows<InvalidOperationException>(() => builder.Build());

        var unbalanced = new BPageBuilder(new BSize(100, 100));
        unbalanced.Content.PushClip(new BRect(0, 0, 10, 10));
        AssertThrows<InvalidOperationException>(() => unbalanced.Build());
    }

    private static void PageCopiesImagePixels()
    {
        byte[] rgba = [255, 0, 0, 255];
        var builder = new BPageBuilder(new BSize(10, 10));
        BImageHandle handle = builder.AddImage(new BPixelBuffer(1, 1, rgba));
        builder.Content.DrawImage(handle, new BRect(0, 0, 1, 1), new BRect(0, 0, 10, 10));
        BPage page = builder.Build();

        rgba[0] = 0;
        AssertEx.AreEqual((byte)255, page.Images[handle].Rgba[0]);
        AssertEx.AreEqual(1, page.Images.Count);
    }

    private static void AppendValidatesCommands()
    {
        var list = new BRenderList();
        list.Append(new BRenderCommand.FillRoundedRect(new BRect(0, 0, 4, 4), BColor.Red, 0, 2));
        AssertEx.IsTrue(list.Commands[0] is BRenderCommand.FillRect, "A zero-radius rounded fill is recorded as a plain fill.");

        AssertThrows<ArgumentOutOfRangeException>(() =>
            list.Append(new BRenderCommand.StrokeRect(new BRect(0, 0, 4, 4), BColor.Red, -1)));

        var other = new BRenderList();
        other.PushTransform(BMatrix3x2.Translation(1, 2));
        other.FillRect(new BRect(0, 0, 1, 1), BColor.Blue);
        other.PopTransform();
        list.Append(other);
        AssertEx.AreEqual(4, list.Count);
        list.Validate();

        AssertThrows<ArgumentException>(() => list.Append(list));
    }

    private static void CacheUploadsOnce()
    {
        BPage page = PageWithImage(out _);
        int uploads = 0;
        using var cache = new BPageImageCache(pixels =>
        {
            uploads++;
            return BImageHandle.FromId(1000 + (ulong)uploads, new BSize(pixels.Width, pixels.Height));
        });

        var first = new BRenderList();
        cache.AppendPage(page, first);
        var second = new BRenderList();
        cache.AppendPage(page, second);

        AssertEx.AreEqual(1, uploads);
        var drawn = (BRenderCommand.DrawImage)second.Commands.Single(c => c is BRenderCommand.DrawImage);
        AssertEx.AreEqual(1001UL, drawn.Image.Handle.Id);
    }

    private static void CacheReleasesOnClear()
    {
        BPage page = PageWithImage(out _);
        var released = new List<BImageHandle>();
        var cache = new BPageImageCache(pixels => BImageHandle.FromId(77, new BSize(pixels.Width, pixels.Height)), released.Add);
        cache.AppendPage(page, new BRenderList());
        AssertEx.AreEqual(1, cache.Count);

        cache.Dispose();
        AssertEx.AreEqual(1, released.Count);
        AssertEx.AreEqual(77UL, released[0].Handle.Id);
        AssertThrows<ObjectDisposedException>(() => cache.AppendPage(page, new BRenderList()));
    }

    private static void RasterPixelSize()
    {
        AssertEx.AreEqual((2550, 3300), BPageRasterizer.GetPixelSize(BPaperSize.Letter.Size, 300));
        AssertEx.AreEqual((2481, 3508), BPageRasterizer.GetPixelSize(BPaperSize.A4.Size, 300));
        AssertThrows<ArgumentOutOfRangeException>(() => BPageRasterizer.GetPixelSize(BPaperSize.A4.Size, 0));
        AssertThrows<ArgumentOutOfRangeException>(() => BPageRasterizer.GetPixelSize(BPaperSize.A4.Size, 5000));
    }

    /// <summary>
    /// A fill that crosses several band boundaries, and an edge that falls inside a band:
    /// whatever the band height, the stitched rows must be the rows of the page rendered whole.
    /// </summary>
    private static void BandsStitchIntoWholePage()
    {
        var builder = new BPageBuilder(new BSize(72, 72));
        builder.Content.FillRect(new BRect(6.3, 5.1, 40.2, 50.7), BColor.Red);
        builder.Content.FillTriangle(new BPoint(10, 60), new BPoint(60, 10), new BPoint(60, 60), BColor.Blue);
        BPage page = builder.Build();

        using BBitmap whole = BPageRasterizer.Render(page, 144, BColor.White);
        AssertEx.AreEqual(144, whole.Width);
        AssertEx.AreEqual(144, whole.Height);

        foreach (int bandHeight in new[] { 1, 7, 50, 143, 144, 500 })
        {
            var stitched = new byte[whole.Width * whole.Height * 4];
            int bands = 0;
            int nextTop = 0;
            BPageRasterizer.RenderBands(page, 144, BColor.White, bandHeight, band =>
            {
                AssertEx.AreEqual(nextTop, band.Top, $"Band {bands} starts where the last ended.");
                AssertEx.AreEqual(whole.Width, band.Pixels.Width);
                band.Pixels.Rgba.CopyTo(stitched.AsSpan(band.Top * whole.Width * 4));
                nextTop = band.Top + band.Pixels.Height;
                bands++;
            });

            AssertEx.AreEqual(whole.Height, nextTop, $"Band height {bandHeight} covers every row.");
            AssertEx.AreEqual((whole.Height + bandHeight - 1) / bandHeight, bands);
            AssertEx.IsTrue(whole.Rgba.SequenceEqual(stitched), $"Band height {bandHeight} reproduces the page.");
        }
    }

    private static void RasterDrawsPageImage()
    {
        BPage page = PageWithImage(out _);
        using BBitmap raster = BPageRasterizer.Render(page, 216, BColor.White);

        // The 2x2 image is drawn over (10, 10)-(30, 30) pt; at 216 dpi that is (30, 30)-(90, 90).
        AssertEx.AreEqual(BColor.White, raster.GetPixel(10, 10));
        BColor inside = raster.GetPixel(40, 40);
        AssertEx.IsTrue(inside.R > 200 && inside.G < 60 && inside.B < 60, $"Expected the image's red, got {inside}.");
    }

    private static void SourceSelectIsLazy()
    {
        int produced = 0;
        var source = new CountingSource(() => produced++);

        IEnumerable<BPage> selection = source.Select([2, 9, 0]);
        AssertEx.AreEqual(0, produced, "Nothing is laid out until the sequence is walked.");

        BPage[] pages = [.. selection];
        AssertEx.AreEqual(2, pages.Length);
        AssertEx.AreEqual(2, produced);
        AssertEx.AreEqual(3, source.All().Count());
    }

    private static BPage PageWithImage(out BImageHandle handle)
    {
        var builder = new BPageBuilder(new BSize(40, 40));
        byte[] red = [.. Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 4).SelectMany(p => p)];
        handle = builder.AddImage(new BPixelBuffer(2, 2, red));
        builder.Content.DrawImage(handle, new BRect(0, 0, 2, 2), new BRect(10, 10, 20, 20));
        return builder.Build();
    }

    private static void AssertClose(double expected, double actual, double tolerance)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new AssertException($"Expected {expected} within {tolerance}, but was {actual}.");
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new AssertException($"Expected {typeof(TException).Name}, but {ex.GetType().Name} was thrown: {ex.Message}");
        }

        throw new AssertException($"Expected {typeof(TException).Name}, but nothing was thrown.");
    }

    private sealed class CountingSource(Action onProduce) : IBPageSource
    {
        public int PageCount => 3;

        public BPage GetPage(int index)
        {
            onProduce();
            return new BPageBuilder(new BSize(10, 10)).Build();
        }
    }
}
