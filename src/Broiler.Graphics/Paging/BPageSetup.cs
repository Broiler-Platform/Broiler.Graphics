using Broiler.Graphics.Geometry;
using System;

namespace Broiler.Graphics.Paging;

/// <summary>Which way up a sheet is printed.</summary>
public enum BPageOrientation
{
    /// <summary>The short edge along the top.</summary>
    Portrait = 0,

    /// <summary>The long edge along the top.</summary>
    Landscape = 1,
}

/// <summary>The space a page keeps clear along each edge, in points.</summary>
public readonly record struct BPageMargins(double Left, double Top, double Right, double Bottom)
{
    /// <summary>No margin at all: content may run to the edge of the sheet.</summary>
    public static BPageMargins None => default;

    /// <summary>The same margin on every edge.</summary>
    public static BPageMargins Uniform(double margin) => new(margin, margin, margin, margin);

    /// <summary>True when every edge is finite and not negative.</summary>
    public bool IsValid =>
        IsNonNegativeFinite(Left) && IsNonNegativeFinite(Top) && IsNonNegativeFinite(Right) && IsNonNegativeFinite(Bottom);

    /// <summary>Each edge the larger of this margin and <paramref name="minimum"/>'s.</summary>
    public BPageMargins AtLeast(BPageMargins minimum) => new(
        Math.Max(Left, minimum.Left), Math.Max(Top, minimum.Top), Math.Max(Right, minimum.Right), Math.Max(Bottom, minimum.Bottom));

    private static bool IsNonNegativeFinite(double value) => double.IsFinite(value) && value >= 0;
}

/// <summary>
/// What a paginator needs to know about the sheet it is filling: the paper, which way up it
/// goes, and the margins. Everything is in points, and nothing here knows about a printer, a
/// screen or a resolution - the same setup paginates a preview, a print job and a PDF the
/// same way.
/// </summary>
public sealed record BPageSetup
{
    private readonly BPaperSize _paper;
    private readonly BPageMargins _margins;

    public BPageSetup(BPaperSize paper, BPageOrientation orientation, BPageMargins margins)
    {
        Paper = paper;
        Orientation = orientation;
        Margins = margins;
    }

    /// <summary>A4 portrait with one-inch margins, the page a document that states none gets.</summary>
    public static BPageSetup Default { get; } = new(BPaperSize.A4, BPageOrientation.Portrait, BPageMargins.Uniform(72));

    // The checks sit in the init accessors so a `with` expression cannot step around them.
    public BPaperSize Paper
    {
        get => _paper;
        init => _paper = value.Name is null ? throw new ArgumentException("A page setup needs a paper size.", nameof(Paper)) : value;
    }

    public BPageOrientation Orientation { get; init; }

    public BPageMargins Margins
    {
        get => _margins;
        init => _margins = value.IsValid ? value : throw new ArgumentOutOfRangeException(nameof(Margins), "Margins must be finite and not negative.");
    }

    /// <summary>The page as laid out, in points: the paper turned the way the orientation says.</summary>
    public BSize PageSize => Paper.Oriented(Orientation);

    /// <summary>The area inside the margins, in page coordinates. Empty when the margins meet.</summary>
    public BRect ContentBox
    {
        get
        {
            BSize size = PageSize;
            double width = Math.Max(0, size.Width - Margins.Left - Margins.Right);
            double height = Math.Max(0, size.Height - Margins.Top - Margins.Bottom);
            return new BRect(Margins.Left, Margins.Top, width, height);
        }
    }

    /// <summary>
    /// The setup a page of <paramref name="pageSize"/> describes: its sheet from the catalogue
    /// (or a custom one), turned to match.
    /// </summary>
    public static BPageSetup ForPageSize(BSize pageSize, BPageMargins margins)
    {
        BPaperSize paper = BPaperSize.FromSize(pageSize);
        BPageOrientation orientation = pageSize.Width > pageSize.Height ? BPageOrientation.Landscape : BPageOrientation.Portrait;
        return new BPageSetup(paper, orientation, margins);
    }
}
