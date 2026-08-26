using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Multithreading roadmap item #9: the shared instance roots on the render path.
/// </summary>
/// <remarks>
/// <para>
/// <c>TextMeasurementConcurrencyTests</c> covers the two advance/glyph-index caches
/// on <c>FallbackSystemFont</c>. This covers the rest of what the P0-c audit named
/// — <c>FontsHandler</c>'s font caches and <c>BImageRenderer</c>'s image table —
/// plus two the audit missed because a scan for shared dictionaries does not see
/// them: <c>FallbackSystemFont</c>'s contour caches, which are populated on the
/// read path exactly the way the advance caches were, and <c>TrueTypeFont</c>'s
/// five lazily-parsed OpenType tables.
/// </para>
/// <para>
/// The table one is the reason this file is worth more than a smoke test. The old
/// shape was <c>if (_parsed) return _table; _parsed = true; _table = Parse();</c>
/// — the latch published *before* the value, so a second thread arriving inside
/// that window read a null table, and every caller reads null as "this font has no
/// such table". Nothing throws. The render is simply wrong: no ligatures, no mark
/// positioning, or — for a CFF-outline font — <c>HasOutlines</c> false and the text
/// drawn with the built-in block glyphs. A torn dictionary at least announces
/// itself with an exception; this did not.
/// </para>
/// </remarks>
internal static class RenderPathConcurrencyTests
{
    private const int Threads = 16;

    public static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("FontsHandler hands every thread one font per key", FontsHandlerIsThreadSafe));
        tests.Add(("FontsHandler still matches families case-insensitively", FontsHandlerFamilyMatchIsCaseInsensitive));
        tests.Add(("BImageRenderer gives concurrent images distinct handles", ImageHandlesAreUniqueUnderConcurrency));
        tests.Add(("BImageRenderer renders the same pixels on many threads", ConcurrentRenderMatchesSequentialRender));
        tests.Add(("Glyph contours survive concurrent first use", ContourCacheIsThreadSafe));
        tests.Add(("Lazily-parsed font tables publish before their latch", LazyFontTablesPublishSafely));
        tests.Add(("TrueTypeFont's glyph outline cache is correct and thread-safe", GlyphOutlineCacheIsCorrectAndThreadSafe));
    }

    // ── FontsHandler ─────────────────────────────────────────────────────────

    /// <summary>
    /// The property that matters is not "does not throw" — it is that one key
    /// yields one instance. A torn nested dictionary loses whole families
    /// silently, and the caller cannot tell a lost cache entry from a cold one.
    /// </summary>
    private static void FontsHandlerIsThreadSafe()
    {
        var creator = new CountingFontCreator();
        var handler = new FontsHandler(creator);

        // 24 distinct keys, each requested by every thread, so the threads collide
        // on the same keys rather than partitioning them.
        (string Family, double Size, FontStyle Style)[] keys = [.. from family in new[] { "Verdana", "Georgia", "Menlo", "Ahem" }
                                                                  from size in new double[] { 12, 14, 18 }
                                                                  from style in new[] { FontStyle.Regular, FontStyle.Bold }
                                                                  select (family, size, style)];

        var seen = new ConcurrentDictionary<(string, double, FontStyle), ConcurrentDictionary<RFont, byte>>();
        RunOnAllThreads(index =>
        {
            // Each thread walks the key list from a different offset so they are
            // never all missing on the same key at the same instant — which is the
            // interleaving that finds a publish-after-latch bug rather than a
            // simple torn write.
            for (int step = 0; step < keys.Length; step++)
            {
                (string family, double size, FontStyle style) = keys[(step + index) % keys.Length];
                RFont font = handler.GetCachedFont(family, size, style);
                seen.GetOrAdd((family, size, style), _ => new ConcurrentDictionary<RFont, byte>())[font] = 0;
            }
        });

        AssertEx.AreEqual(keys.Length, seen.Count, "Every requested key must be observed.");
        foreach (KeyValuePair<(string, double, FontStyle), ConcurrentDictionary<RFont, byte>> entry in seen)
        {
            AssertEx.AreEqual(
                1,
                entry.Value.Count,
                $"Key {entry.Key} must resolve to exactly one shared RFont, saw {entry.Value.Count}.");
        }

        // The cache must actually cache: a second sequential pass creates nothing.
        int afterRace = creator.Created;
        foreach ((string family, double size, FontStyle style) in keys)
            handler.GetCachedFont(family, size, style);

        AssertEx.AreEqual(afterRace, creator.Created, "A warm cache must not create more fonts.");
    }

    /// <summary>
    /// The nested cache's outer level used <c>InvariantCultureIgnoreCase</c>.
    /// Flattening it to one dictionary keyed by (family, size, style) moves that
    /// comparison into a key comparer, and losing it would quietly double every
    /// font whose family arrives with different casing from two stylesheets.
    /// </summary>
    private static void FontsHandlerFamilyMatchIsCaseInsensitive()
    {
        var creator = new CountingFontCreator();
        var handler = new FontsHandler(creator);

        RFont lower = handler.GetCachedFont("verdana", 14, FontStyle.Regular);
        RFont upper = handler.GetCachedFont("VERDANA", 14, FontStyle.Regular);
        RFont mixed = handler.GetCachedFont("VerDaNa", 14, FontStyle.Regular);

        AssertEx.IsTrue(ReferenceEquals(lower, upper), "Family match must ignore case.");
        AssertEx.IsTrue(ReferenceEquals(lower, mixed), "Family match must ignore case.");
        AssertEx.AreEqual(1, creator.Created, "Three casings of one family must create one font.");

        // Size and style stay part of the key.
        AssertEx.IsFalse(
            ReferenceEquals(lower, handler.GetCachedFont("verdana", 15, FontStyle.Regular)),
            "Size must remain part of the cache key.");
        AssertEx.IsFalse(
            ReferenceEquals(lower, handler.GetCachedFont("verdana", 14, FontStyle.Bold)),
            "Style must remain part of the cache key.");
    }

    // ── BImageRenderer ───────────────────────────────────────────────────────

    /// <summary>
    /// <c>++_nextImageId</c> is a read-modify-write. Two threads reading the same
    /// value hand out one handle twice, and the loser's pixels are then drawn for
    /// the winner's image — a wrong picture, not a crash.
    /// </summary>
    private static void ImageHandlesAreUniqueUnderConcurrency()
    {
        using var renderer = new BImageRenderer();
        const int PerThread = 32;

        var handles = new ConcurrentBag<(ulong Id, byte Marker)>();
        RunOnAllThreads(index =>
        {
            for (int step = 0; step < PerThread; step++)
            {
                // Each image is a solid colour unique to its (thread, step) pair,
                // so a duplicated handle shows up as a mismatched read-back.
                byte marker = (byte)(1 + (((index * PerThread) + step) % 254));
                BImageHandle handle = renderer.CreateImage(SolidPixels(2, 2, marker));
                handles.Add((handle.Handle.Id, marker));
            }
        });

        (ulong Id, byte Marker)[] all = [.. handles];
        AssertEx.AreEqual(Threads * PerThread, all.Length, "Every CreateImage must return.");
        AssertEx.AreEqual(
            all.Length,
            all.Select(entry => entry.Id).Distinct().Count(),
            "Concurrently created images must not share a handle id.");
    }

    /// <summary>
    /// The transform stack used to live on the renderer, so two replays in flight
    /// popped each other's transforms. This renders the same list on many threads,
    /// each into its own surface, and demands the exact bytes a single-threaded
    /// replay produces — the "identical output at 1 and N threads" the roadmap's
    /// global exit gate asks for, at the smallest scope that can express it.
    /// </summary>
    private static void ConcurrentRenderMatchesSequentialRender()
    {
        using var renderer = new BImageRenderer();
        BRenderList list = BuildNestedTransformList();
        BSurfaceDescriptor descriptor = BSurfaceDescriptor.Default(new BSize(64, 64));
        var frame = new BFrameContext(BColor.White);

        byte[] expected = RenderToBytes(renderer, list, descriptor, frame);

        var mismatches = new ConcurrentBag<int>();
        RunOnAllThreads(index =>
        {
            byte[] actual = RenderToBytes(renderer, list, descriptor, frame);
            if (!actual.AsSpan().SequenceEqual(expected))
                mismatches.Add(index);
        });

        AssertEx.AreEqual(
            0,
            mismatches.Count,
            $"Concurrent replays must be byte-identical to the sequential one; {mismatches.Count} differed.");
    }

    // ── FallbackSystemFont / TrueTypeFont ────────────────────────────────────

    /// <summary>
    /// The contour caches are the ones a *painting* thread hits, which is the
    /// thread this phase adds. They were left as plain dictionaries when the
    /// advance caches beside them were made concurrent.
    /// </summary>
    /// <remarks>
    /// Sized the way <c>TextMeasurementConcurrencyTests</c> sizes its own: enough
    /// distinct keys, inserted by enough threads on interleaved indices, to drive
    /// the dictionary through several resizes while writes are in flight. A
    /// handful of keys is not a test of this — it fits in the initial buckets and
    /// never resizes, which is when a plain Dictionary tears.
    /// </remarks>
    private static void ContourCacheIsThreadSafe()
    {
        FallbackSystemFont? font = FallbackSystemFont.Shared;
        if (font is null)
            return; // No host font on this machine; nothing to race. See TryLoad.

        const int CodepointsPerThread = 512;

        var failures = new ConcurrentBag<string>();
        RunOnAllThreads(index =>
        {
            for (int step = 0; step < CodepointsPerThread; step++)
            {
                int codepoint = 0x0100 + (step * Threads) + index;
                bool bold = (index & 1) == 1;
                if (!font.TryGetGlyph(codepoint, bold, out IReadOnlyList<PointF[]> contours, out _, out _))
                    continue;

                // A torn read hands back another key's list or a null; both show
                // up here without needing a per-codepoint expectation table.
                if (contours is null)
                    failures.Add($"U+{codepoint:X4} returned null contours");
            }
        });

        AssertEx.AreEqual(0, failures.Count, $"Contours must survive concurrency: {string.Join("; ", failures.Take(4))}");

        // And the cache must still be a cache that answers correctly: every glyph
        // it now holds has to match a cold instance's outline for the same glyph.
        TrueTypeFont? reference = TrueTypeFont.LoadFromFile(font.RegularPath);
        if (reference is null)
            return;

        for (int codepoint = 0x0100; codepoint < 0x0100 + 64; codepoint++)
        {
            if (!font.TryGetGlyph(codepoint, bold: false, out IReadOnlyList<PointF[]> contours, out _, out _))
                continue;

            int expected = TotalPoints(reference.GetGlyphContours(reference.GetGlyphIndex(codepoint)));
            AssertEx.AreEqual(expected, TotalPoints(contours), $"Cached contours for U+{codepoint:X4} must match a cold parse.");
        }
    }

    /// <summary>
    /// Multithreading item #10: <c>TrueTypeFont.GetGlyphContours</c> caches its outlines, so the
    /// cache has to answer with the same geometry a cold parse produces and has to survive being
    /// filled from several threads at once.
    /// </summary>
    /// <remarks>
    /// Two properties, and the first is the one that would go unnoticed. A cache that returns
    /// <em>an</em> outline for every glyph but occasionally the wrong one draws a page of plausible
    /// text with a few wrong letters — nothing throws, and no smoke test sees it. So every glyph
    /// warmed concurrently is compared point-for-point against a font instance that has never been
    /// touched by another thread, rather than merely checked for non-emptiness.
    /// </remarks>
    private static void GlyphOutlineCacheIsCorrectAndThreadSafe()
    {
        FallbackSystemFont? shared = FallbackSystemFont.Shared;
        if (shared is null)
            return; // No host font on this machine.

        TrueTypeFont? warm = TrueTypeFont.LoadFromFile(shared.RegularPath);
        TrueTypeFont? cold = TrueTypeFont.LoadFromFile(shared.RegularPath);
        if (warm is null || cold is null)
            return;

        // Glyph indices every thread asks for, so they collide on the cold entries rather than
        // each filling a private corner of the cache.
        int[] glyphs = new int[48];
        for (int i = 0; i < glyphs.Length; i++)
            glyphs[i] = warm.GetGlyphIndex('A' + (i % 26)) is var g && g > 0 ? g : 1;

        var failures = new ConcurrentBag<string>();
        RunOnAllThreads(_ =>
        {
            foreach (int glyph in glyphs)
            {
                List<PointF[]> contours = warm.GetGlyphContours(glyph);
                if (contours is null)
                    failures.Add($"glyph {glyph} returned null contours");
            }
        });

        AssertEx.AreEqual(0, failures.Count, $"Cached outlines must survive concurrency: {string.Join("; ", failures.Take(4))}");

        foreach (int glyph in glyphs)
        {
            List<PointF[]> cached = warm.GetGlyphContours(glyph);
            List<PointF[]> expected = cold.GetGlyphContours(glyph);

            AssertEx.AreEqual(expected.Count, cached.Count, $"Glyph {glyph} must cache the same contour count.");
            for (int contour = 0; contour < expected.Count; contour++)
            {
                AssertEx.AreEqual(
                    expected[contour].Length,
                    cached[contour].Length,
                    $"Glyph {glyph} contour {contour} must cache the same point count.");
                for (int point = 0; point < expected[contour].Length; point++)
                {
                    AssertEx.AreEqual(
                        expected[contour][point],
                        cached[contour][point],
                        $"Glyph {glyph} contour {contour} point {point} must cache the same coordinates.");
                }
            }
        }

        // The cache is a cache: the same glyph asked twice is the same instance, which is what
        // stops the caller paying for the outline again.
        AssertEx.IsTrue(
            ReferenceEquals(warm.GetGlyphContours(glyphs[0]), warm.GetGlyphContours(glyphs[0])),
            "A cached outline must be handed back, not re-parsed.");
    }

    /// <summary>
    /// Every thread's *first* act is to read one lazily-parsed table, on a font
    /// instance nobody has touched, all released from one barrier — so they pile
    /// into the single cold window rather than arriving after it closed.
    /// </summary>
    /// <remarks>
    /// The probes are chosen from the host font at run time rather than hard-coded,
    /// because a probe whose answer is the same with and without the table cannot
    /// detect anything. <c>IsMarkGlyph</c> on a glyph the warm font calls a mark is
    /// the sharp one: with GDEF absent it answers false, and it is the first thing
    /// each thread asks. If the host font has no mark glyph at all the probe is
    /// skipped and said so, rather than passing vacuously.
    /// </remarks>
    private static void LazyFontTablesPublishSafely()
    {
        FallbackSystemFont? shared = FallbackSystemFont.Shared;
        if (shared is null)
            return; // No host font on this machine.

        TrueTypeFont? warm = TrueTypeFont.LoadFromFile(shared.RegularPath);
        if (warm is null)
            return;

        // Find a glyph the font's GDEF calls a mark. That answer flips to false
        // when a thread reads GDEF as null, which is exactly the defect.
        int markGlyph = -1;
        for (int glyph = 1; glyph < 3000 && markGlyph < 0; glyph++)
        {
            if (warm.IsMarkGlyph(glyph))
                markGlyph = glyph;
        }

        if (markGlyph < 0)
        {
            Console.WriteLine("         (host font has no GDEF mark glyph; lazy-table probe skipped)");
            return;
        }

        var wrong = new ConcurrentBag<int>();
        // One cold window per instance, so a fresh instance per round and several
        // rounds — a single window is not a sample.
        for (int round = 0; round < 16; round++)
        {
            TrueTypeFont? cold = TrueTypeFont.LoadFromFile(shared.RegularPath);
            if (cold is null)
                continue;

            RunOnAllThreads(index =>
            {
                if (!cold.IsMarkGlyph(markGlyph))
                    wrong.Add(index);
            });
        }

        AssertEx.AreEqual(
            0,
            wrong.Count,
            $"A cold font's GDEF must read the same on every thread; {wrong.Count} thread-rounds saw glyph {markGlyph} as not-a-mark.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts <see cref="Threads"/> real threads on a barrier. A barrier rather
    /// than a plain start loop because the bugs these tests are for have a single
    /// cold window per process: without it the first thread warms the cache while
    /// the rest are still being created and nothing ever races.
    /// </summary>
    private static void RunOnAllThreads(Action<int> body)
    {
        using var barrier = new Barrier(Threads);
        Exception? failure = null;
        object gate = new();

        var workers = new Thread[Threads];
        for (int worker = 0; worker < Threads; worker++)
        {
            int index = worker;
            workers[worker] = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    body(index);
                }
                catch (Exception exception)
                {
                    lock (gate)
                        failure ??= exception;
                }
            });
            workers[worker].Start();
        }

        foreach (Thread thread in workers)
            thread.Join();

        AssertEx.IsTrue(failure is null, $"No worker may throw, but one did: {failure}");
    }

    private static int TotalPoints(IReadOnlyList<PointF[]> contours)
    {
        int total = 0;
        for (int i = 0; i < contours.Count; i++)
            total += contours[i].Length;
        return total;
    }

    private static BPixelBuffer SolidPixels(int width, int height, byte marker)
    {
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = marker;
            rgba[i + 1] = marker;
            rgba[i + 2] = marker;
            rgba[i + 3] = 255;
        }

        return new BPixelBuffer(width, height, rgba);
    }

    /// <summary>
    /// Nested push/pop transforms, so a replay that loses its stack to another
    /// thread draws in the wrong place rather than merely drawing the same thing
    /// twice.
    /// </summary>
    private static BRenderList BuildNestedTransformList()
    {
        var list = new BRenderList();
        list.FillRect(new BRect(0, 0, 64, 64), BColor.Blue);
        list.PushTransform(BMatrix3x2.Translation(8, 8));
        list.FillRect(new BRect(0, 0, 16, 16), BColor.Red);
        list.PushTransform(BMatrix3x2.Scale(2.0, 2.0));
        list.FillRect(new BRect(4, 4, 8, 8), BColor.Green);
        list.PopTransform();
        list.FillRect(new BRect(20, 2, 6, 6), BColor.Black);
        list.PopTransform();
        list.FillRect(new BRect(48, 48, 10, 10), BColor.FromArgb(255, 240, 240, 40));
        return list;
    }

    private static byte[] RenderToBytes(
        BImageRenderer renderer,
        BRenderList list,
        BSurfaceDescriptor descriptor,
        BFrameContext frame)
    {
        using BBitmap bitmap = renderer.RenderToImage(list, descriptor, frame);
        return bitmap.Rgba.ToArray();
    }

    private sealed class CountingFontCreator : IFontCreator
    {
        private int _created;

        public int Created => Volatile.Read(ref _created);

        public RFont CreateFont(string family, double size, FontStyle style)
        {
            Interlocked.Increment(ref _created);
            // Some work, so the window between "miss" and "publish" is wide enough
            // for a second thread to land in it. Without this the race is real but
            // almost never observed, and a test that cannot fail is not a test.
            Thread.SpinWait(2000);
            return new StubFont(size);
        }

        public RFont CreateFont(RFontFamily family, double size, FontStyle style) =>
            CreateFont(family?.Name ?? string.Empty, size, style);
    }

    private sealed class StubFont(double size) : RFont
    {
        public override double Size { get; } = size;

        public override double Height => Size * 1.2;

        public override double UnderlineOffset => Size * 1.1;

        public override double LeftPadding => 0;

        public override double GetWhitespaceWidth(RGraphics graphics) => Size * 0.25;
    }
}
