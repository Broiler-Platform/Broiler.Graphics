using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Broiler.Graphics.Paging;

/// <summary>A run of laid-out pages: how many there are, and each one by its zero-based index.</summary>
/// <remarks>
/// Implementations may produce pages on demand. A consumer asks for each page it needs and
/// keeps none longer than it must, so a long document need not hold every page's commands
/// and images at once.
/// </remarks>
public interface IBPageSource
{
    /// <summary>The number of pages.</summary>
    int PageCount { get; }

    /// <summary>The page at <paramref name="index"/>, counting from zero.</summary>
    BPage GetPage(int index);
}

/// <summary>
/// Something that can be laid out into pages, again for each page setup asked of it: a
/// document, a web page, a source file. A print dialog paginates it once for its preview and
/// again whenever the paper or orientation changes.
/// </summary>
public interface IBPagedDocument
{
    /// <summary>The title a print job, or a PDF, is given.</summary>
    string Title { get; }

    /// <summary>
    /// The page the document asks for - the size a DOCX states, an <c>@page</c> rule - which a
    /// print dialog starts from. A document that states none answers <see cref="BPageSetup.Default"/>.
    /// </summary>
    BPageSetup PreferredSetup { get; }

    /// <summary>Lays the document out on pages of <paramref name="setup"/>.</summary>
    IBPageSource Paginate(BPageSetup setup, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns pages into a file a spooler or a reader accepts - a PDF for CUPS, for a browser's
/// print, for "save as PDF".
/// </summary>
/// <remarks>
/// The contract lives beside the pages, and the writers live with the formats they write:
/// a host that needs one is given it by the application that composed both, so the host
/// does not take a dependency on a document codec to print.
/// </remarks>
public interface IBPageDocumentWriter
{
    /// <summary>The media type written, such as <c>application/pdf</c>.</summary>
    string MediaType { get; }

    /// <summary>The file extension of what is written, with its dot.</summary>
    string FileExtension { get; }

    /// <summary>Writes <paramref name="pages"/>, in order, to <paramref name="output"/>.</summary>
    void Write(IEnumerable<BPage> pages, string title, Stream output, CancellationToken cancellationToken = default);
}

/// <summary>A page source over pages that already exist.</summary>
public sealed class BPageCollection : IBPageSource, IReadOnlyList<BPage>
{
    private readonly BPage[] _pages;

    public BPageCollection(IEnumerable<BPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        _pages = [.. pages];
        foreach (BPage page in _pages)
            ArgumentNullException.ThrowIfNull(page, nameof(pages));
    }

    public static BPageCollection Empty { get; } = new([]);

    public int PageCount => _pages.Length;

    public int Count => _pages.Length;

    public BPage this[int index] => _pages[index];

    public BPage GetPage(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _pages.Length);
        return _pages[index];
    }

    public IEnumerator<BPage> GetEnumerator() => ((IEnumerable<BPage>)_pages).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Helpers over <see cref="IBPageSource"/>.</summary>
public static class BPageSourceExtensions
{
    /// <summary>
    /// The pages of <paramref name="source"/> whose zero-based indexes are listed, in the order
    /// listed, produced one at a time as the sequence is walked. Indexes outside the source are
    /// skipped.
    /// </summary>
    public static IEnumerable<BPage> Select(this IBPageSource source, IEnumerable<int> indexes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(indexes);

        foreach (int index in indexes)
        {
            if (index >= 0 && index < source.PageCount)
                yield return source.GetPage(index);
        }
    }

    /// <summary>Every page of <paramref name="source"/>, in order, produced one at a time.</summary>
    public static IEnumerable<BPage> All(this IBPageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        for (int i = 0; i < source.PageCount; i++)
            yield return source.GetPage(i);
    }
}
