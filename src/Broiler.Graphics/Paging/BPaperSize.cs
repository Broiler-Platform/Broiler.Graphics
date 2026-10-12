using Broiler.Graphics.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Broiler.Graphics.Paging;

/// <summary>
/// A sheet of paper, named and measured in points (1/72 inch), always stated portrait: the
/// width is never greater than the height. Which way up a page is printed is the business of
/// <see cref="BPageSetup.Orientation"/>, not of the sheet.
/// </summary>
/// <remarks>
/// <para>
/// One catalogue for every consumer. Before it, the Documents CLI held its sizes in
/// millimetres, the HTML codec in points and the WPT runner in CSS pixels, each with its own
/// list of names; a page laid out by one and printed by another had to agree on A4 three
/// times over.
/// </para>
/// <para>
/// ISO sizes are defined in millimetres and the US sizes in inches; both are converted to
/// points once, here, so a size read back from a printer driver (tenths of a millimetre) or
/// from CUPS (hundredths) compares against the same numbers with <see cref="Matches"/>.
/// </para>
/// </remarks>
public readonly record struct BPaperSize
{
    /// <summary>Points per inch.</summary>
    public const double PointsPerInch = 72.0;

    /// <summary>Millimetres per inch.</summary>
    public const double MillimetresPerInch = 25.4;

    /// <summary>
    /// How far apart two sizes may be, in points, and still name the same sheet. Drivers round
    /// A4 to whole tenths of a millimetre and Letter to whole hundredths of an inch; half a
    /// point (0.18 mm) covers both without mistaking B5 for anything else.
    /// </summary>
    public const double MatchTolerance = 0.5;

    public BPaperSize(string name, double width, double height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!double.IsFinite(width) || width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Paper width must be positive and finite.");
        if (!double.IsFinite(height) || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Paper height must be positive and finite.");

        Name = name;
        Width = Math.Min(width, height);
        Height = Math.Max(width, height);
    }

    /// <summary>The paper's name, as a person would read it: <c>A4</c>, <c>Letter</c>.</summary>
    public string Name { get; }

    /// <summary>The short edge, in points.</summary>
    public double Width { get; }

    /// <summary>The long edge, in points.</summary>
    public double Height { get; }

    /// <summary>The sheet as a portrait size in points.</summary>
    public BSize Size => new(Width, Height);

    public static BPaperSize A3 { get; } = FromMillimetres("A3", 297, 420);

    public static BPaperSize A4 { get; } = FromMillimetres("A4", 210, 297);

    public static BPaperSize A5 { get; } = FromMillimetres("A5", 148, 210);

    public static BPaperSize A6 { get; } = FromMillimetres("A6", 105, 148);

    public static BPaperSize B4 { get; } = FromMillimetres("B4", 250, 353);

    public static BPaperSize B5 { get; } = FromMillimetres("B5", 176, 250);

    public static BPaperSize JisB4 { get; } = FromMillimetres("JIS B4", 257, 364);

    public static BPaperSize JisB5 { get; } = FromMillimetres("JIS B5", 182, 257);

    public static BPaperSize Letter { get; } = FromInches("Letter", 8.5, 11);

    public static BPaperSize Legal { get; } = FromInches("Legal", 8.5, 14);

    /// <summary>11 × 17 inches; CSS calls the landscape sheet <c>ledger</c>.</summary>
    public static BPaperSize Tabloid { get; } = FromInches("Tabloid", 11, 17);

    public static BPaperSize Executive { get; } = FromInches("Executive", 7.25, 10.5);

    public static BPaperSize Statement { get; } = FromInches("Statement", 5.5, 8.5);

    /// <summary>The catalogue, ISO first, in the order a paper list shows them.</summary>
    public static IReadOnlyList<BPaperSize> Standard { get; } =
    [
        A3, A4, A5, A6, B4, B5, JisB4, JisB5, Letter, Legal, Tabloid, Executive, Statement,
    ];

    private static readonly Dictionary<string, BPaperSize> ByName = BuildNameIndex();

    /// <summary>A sheet measured in millimetres.</summary>
    public static BPaperSize FromMillimetres(string name, double width, double height) =>
        new(name, MillimetresToPoints(width), MillimetresToPoints(height));

    /// <summary>A sheet measured in inches.</summary>
    public static BPaperSize FromInches(string name, double width, double height) =>
        new(name, width * PointsPerInch, height * PointsPerInch);

    /// <summary>
    /// A sheet of a size the catalogue does not hold, named for its dimensions so a paper
    /// list can still show it.
    /// </summary>
    public static BPaperSize Custom(double width, double height) =>
        new(string.Format(CultureInfo.InvariantCulture, "{0:0.#} × {1:0.#} mm",
            PointsToMillimetres(Math.Min(width, height)), PointsToMillimetres(Math.Max(width, height))),
            width, height);

    public static double MillimetresToPoints(double millimetres) => millimetres * PointsPerInch / MillimetresPerInch;

    public static double PointsToMillimetres(double points) => points * MillimetresPerInch / PointsPerInch;

    /// <summary>
    /// Finds a catalogue sheet by name, ignoring case, spaces and hyphens, so <c>a4</c>,
    /// <c>JIS-B5</c> and <c>jis b5</c> all resolve. <c>ledger</c> is Tabloid, since the two are
    /// one sheet turned.
    /// </summary>
    public static bool TryFind(string? name, out BPaperSize paper)
    {
        paper = default;
        return !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(NormalizeName(name), out paper);
    }

    /// <summary>
    /// Finds the catalogue sheet a size in points is, in either orientation, or returns false
    /// when it is none of them.
    /// </summary>
    public static bool TryMatch(BSize size, out BPaperSize paper)
    {
        foreach (BPaperSize candidate in Standard)
        {
            if (candidate.Matches(size))
            {
                paper = candidate;
                return true;
            }
        }

        paper = default;
        return false;
    }

    /// <summary>The catalogue sheet a size is, or a <see cref="Custom"/> one.</summary>
    public static BPaperSize FromSize(BSize size) => TryMatch(size, out BPaperSize paper) ? paper : Custom(size.Width, size.Height);

    /// <summary>True when <paramref name="size"/> is this sheet, portrait or landscape.</summary>
    public bool Matches(BSize size)
    {
        double shortEdge = Math.Min(size.Width, size.Height);
        double longEdge = Math.Max(size.Width, size.Height);
        return Math.Abs(shortEdge - Width) <= MatchTolerance && Math.Abs(longEdge - Height) <= MatchTolerance;
    }

    /// <summary>The page size this sheet gives when printed the way <paramref name="orientation"/> says.</summary>
    public BSize Oriented(BPageOrientation orientation) =>
        orientation == BPageOrientation.Landscape ? new BSize(Height, Width) : new BSize(Width, Height);

    public override string ToString() => Name;

    private static Dictionary<string, BPaperSize> BuildNameIndex()
    {
        var index = new Dictionary<string, BPaperSize>(StringComparer.OrdinalIgnoreCase);
        foreach (BPaperSize paper in Standard)
            index[NormalizeName(paper.Name)] = paper;

        index["ledger"] = Tabloid;
        return index;
    }

    private static string NormalizeName(string name) =>
        name.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
}
