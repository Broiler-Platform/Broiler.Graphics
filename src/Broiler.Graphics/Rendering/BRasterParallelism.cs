using System;
using System.Threading.Tasks;

namespace Broiler.Graphics;

/// <summary>
/// The thread budget <see cref="BCanvas"/> spends on scanline bands, and the partitioner its
/// primitives go through. Multithreading roadmap item #3 — the port of item #4's partitioner to
/// this copy of the rasterizer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why bands, and why inside the primitive.</b> Every fill in <see cref="BCanvas"/> is a
/// <c>for y { for x { BlendPixel } }</c> over a rectangle whose rows write disjoint pixels and read
/// only state that is fixed for the duration of the call — the clip list, the transform, the layer
/// stack and the source bitmap are all settled before the loop starts and none of them is touched
/// until it ends. Splitting the <c>y</c> range is therefore the whole change: no locks, no
/// reordering, and no arithmetic that depends on which rows a thread happens to own.
/// </para>
/// <para>
/// <b>Identical output at any thread count is the point.</b> Each row computes its pixels from the
/// input geometry alone, so a row's result does not depend on whether another row has run yet. The
/// exit gate — a single-threaded setting reproducing the parallel image exactly — is checkable by
/// comparing bytes, which is what <c>RasterBandParallelismTests</c> does.
/// </para>
/// <para>
/// <b>The area threshold is not a micro-optimisation, and this copy's callers are why.</b> A page
/// of text is thousands of glyph fills a few hundred pixels each; handing those to the scheduler
/// would cost more than drawing them. Only a fill large enough to amortise the dispatch is split,
/// so the common small primitive takes exactly the code path it took before — the same loop on the
/// same thread. Item #4's measurement is the evidence: on three of five corpus pages *no* fill
/// reached the threshold, so the feature was inert there rather than slow. The consumers of this
/// copy (<see cref="BImageRenderer"/>, and through it Broiler.UI and the Writer) draw widget
/// chrome and text, which is the same shape.
/// </para>
/// <para>
/// <b>Budget, and who else is spending it.</b> The default is one thread per core, overridable
/// with <c>BROILER_RASTER_THREADS</c> — deliberately the same variable item #4's copy reads, so a
/// host dials down "the managed rasterizer" once rather than once per assembly. A host that
/// already runs several renders at once should set it down accordingly; N workers each spawning N
/// raster threads is N² threads competing for N cores, which is slower than either alone.
/// </para>
/// </remarks>
internal static class BRasterParallelism
{
    /// <summary>Environment variable that overrides the default thread budget.</summary>
    internal const string ThreadsEnvironmentVariable = "BROILER_RASTER_THREADS";

    /// <summary>Environment variable overriding <see cref="MinimumParallelArea"/>.</summary>
    internal const string MinimumAreaEnvironmentVariable = "BROILER_RASTER_MIN_AREA";

    /// <summary>Environment variable overriding <see cref="MinimumBandArea"/>.</summary>
    internal const string MinimumBandAreaEnvironmentVariable = "BROILER_RASTER_MIN_BAND";

    /// <summary>
    /// Pixels a fill must cover before it is split at all, and pixels a single band must be worth.
    /// </summary>
    /// <remarks>
    /// <b>Carried over from item #4's sweep rather than re-guessed.</b> Both rasterizers have the
    /// same per-pixel cost — the same blend, the same clip test — so the point at which a split
    /// starts paying is the same, and a second set of constants would only be a second thing to
    /// keep in step. They stay settable so the sweep can be re-run when that per-pixel cost
    /// changes; see the raster-scaling mode of <c>Broiler.Graphics.Benchmarks</c>.
    /// </remarks>
    internal static int MinimumParallelArea { get; set; } = ReadConfiguredArea(MinimumAreaEnvironmentVariable, 2048);

    /// <inheritdoc cref="MinimumParallelArea"/>
    internal static int MinimumBandArea { get; set; } = ReadConfiguredArea(MinimumBandAreaEnvironmentVariable, 1024);

    /// <summary>Environment variable overriding <see cref="MinimumBandCount"/>.</summary>
    internal const string MinimumBandCountEnvironmentVariable = "BROILER_RASTER_MIN_BANDS";

    /// <summary>
    /// Bands a split must be worth before it is taken; a fill that can only be cut this few ways
    /// runs inline instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three, because two is measurably worse than one.</b> On the <c>canvas</c> scene of
    /// <c>--graphics-raster-scaling</c> — thirteen surface-sized fills, 100% of the area splittable,
    /// so the best case band parallelism has — a budget of two threads measured <b>437.7 ms</b>
    /// against <b>362.9 ms</b> for the sequential path, reproducing to a tenth of a millisecond
    /// across separate processes. Three bands measured 297.9 ms and four 270.4 ms, so the loss is
    /// specific to the two-way split and not a general cost of splitting: a fill pays a join at the
    /// end of every band, and cutting one fill in half buys one band's worth of overlap to pay for
    /// it, which on the evidence does not cover the bill.
    /// </para>
    /// <para>
    /// <b>Whose regression this is.</b> On a host with four cores the budget is four and this floor
    /// never fires. It fires on a two-core host, which without it would run <em>slower</em> than
    /// with the parallelism switched off — the one outcome a performance feature must not have. It
    /// is settable so the sweep that chose it can be re-run; <c>1</c> restores the unguarded
    /// behaviour.
    /// </para>
    /// <para>
    /// The sibling partitioner in <c>Broiler.HTML.Image</c> has no such floor and shows the same
    /// inversion where it can be seen (corpus <c>paint</c> page, 660.9 ms at one thread against
    /// 735.7 at two). Fixing it there is a change to a rasterizer whose exit gate is a full WPT run,
    /// so it is recorded as a follow-up rather than folded in here.
    /// </para>
    /// </remarks>
    internal static int MinimumBandCount { get; set; } = ReadConfiguredArea(MinimumBandCountEnvironmentVariable, 3);

    private static int _maxDegreeOfParallelism = ReadConfiguredDegree();

    [ThreadStatic] private static long _inlineFills;
    [ThreadStatic] private static long _splitFills;
    [ThreadStatic] private static long _inlineArea;
    [ThreadStatic] private static long _splitArea;

    /// <summary>
    /// Whether to count what the partitioner decided. Off by default and read once per fill, so the
    /// hot path pays a predictable branch and nothing else.
    /// </summary>
    /// <remarks>
    /// Exists because the interesting question about band parallelism is not how fast a split fill
    /// is — that is arithmetic — but <em>how much of a real render's raster is in fills big enough
    /// to split at all</em>. Without this the answer is a guess, and a scaling table that shows no
    /// speedup cannot distinguish "the threads did not help" from "no fill ever reached the
    /// threshold". Those call for opposite next steps, which is exactly why the counter is here —
    /// and on this copy of the rasterizer it is what turns "the port did nothing" into a claim
    /// about the content rather than about the port.
    /// </remarks>
    internal static bool CollectDiagnostics { get; set; }

    /// <summary>
    /// Fills taken inline, fills split into bands, and the pixel area of each, counted on the
    /// calling thread since the last reset.
    /// </summary>
    /// <remarks>
    /// <b>Per thread, which is also what makes the counters usable.</b> The decision is taken on
    /// the thread that issues the fill — the bands themselves never count — so a thread's totals
    /// describe exactly the renders that thread drove. Process-wide counters would need
    /// interlocked writes on a path that runs thousands of times per render, and would still be
    /// wrong for anyone measuring one render while another test renders alongside it.
    /// </remarks>
    internal static (long InlineFills, long SplitFills, long InlineArea, long SplitArea) Diagnostics =>
        (_inlineFills, _splitFills, _inlineArea, _splitArea);

    /// <summary>Zeroes the calling thread's counters.</summary>
    internal static void ResetDiagnostics()
    {
        _inlineFills = 0;
        _splitFills = 0;
        _inlineArea = 0;
        _splitArea = 0;
    }

    /// <summary>
    /// Maximum threads a single fill may use. <c>1</c> is the sequential rasterizer — not an
    /// approximation of it, the same loop with one band.
    /// </summary>
    internal static int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => _maxDegreeOfParallelism = Math.Max(1, value);
    }

    private static int ReadConfiguredArea(string variable, int fallback)
    {
        string? configured = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(configured) && int.TryParse(configured, out int area) && area > 0
            ? area
            : fallback;
    }

    private static int ReadConfiguredDegree()
    {
        string? configured = Environment.GetEnvironmentVariable(ThreadsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured) &&
            int.TryParse(configured, out int threads) &&
            threads > 0)
        {
            return threads;
        }

        return Environment.ProcessorCount;
    }

    /// <summary>
    /// Runs <paramref name="band"/> over contiguous, inclusive row bands covering
    /// <c>[minY, maxY]</c> — in parallel when the fill is large enough and the target tolerates
    /// concurrent pixel writes, inline on the calling thread otherwise.
    /// </summary>
    /// <param name="rowWidth">
    /// Pixels in one row of the fill, used to decide whether there is enough work to split. Callers
    /// pass the clipped width, not the primitive's nominal one, so a wide rectangle that is mostly
    /// off-surface or clipped away is judged on what it will actually draw.
    /// </param>
    /// <param name="concurrentWritesAllowed">
    /// Whether the destination can take pixel writes from several threads. False forces the inline
    /// path.
    /// </param>
    internal static void ForEachBand(int minY, int maxY, int rowWidth, bool concurrentWritesAllowed, Action<int, int> band)
    {
        ArgumentNullException.ThrowIfNull(band);

        int rows = maxY - minY + 1;
        if (rows <= 0)
            return;

        int threads = BandCount(rows, rowWidth, concurrentWritesAllowed);
        if (CollectDiagnostics)
        {
            long area = (long)rows * Math.Max(0, rowWidth);
            if (threads <= 1)
            {
                _inlineFills++;
                _inlineArea += area;
            }
            else
            {
                _splitFills++;
                _splitArea += area;
            }
        }

        if (threads <= 1)
        {
            band(minY, maxY);
            return;
        }

        int rowsPerBand = (rows + threads - 1) / threads;
        Parallel.For(
            0,
            threads,
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            i =>
            {
                int from = minY + (i * rowsPerBand);
                int to = Math.Min(from + rowsPerBand - 1, maxY);
                if (from <= to)
                    band(from, to);
            });
    }

    /// <summary>How many bands this fill is worth, which is <c>1</c> whenever it should stay inline.</summary>
    private static int BandCount(int rows, int rowWidth, bool concurrentWritesAllowed)
    {
        if (!concurrentWritesAllowed || _maxDegreeOfParallelism <= 1 || rowWidth <= 0)
            return 1;

        long area = (long)rows * rowWidth;
        if (area < MinimumParallelArea)
            return 1;

        int affordable = (int)Math.Min(int.MaxValue, area / Math.Max(1, MinimumBandArea));
        int bands = Math.Max(1, Math.Min(Math.Min(_maxDegreeOfParallelism, rows), affordable));
        return bands < MinimumBandCount ? 1 : bands;
    }
}
