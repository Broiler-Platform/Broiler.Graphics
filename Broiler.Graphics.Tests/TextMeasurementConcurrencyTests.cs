using System;
using System.Collections.Generic;
using System.Threading;

namespace Broiler.Graphics.Tests;

/// <summary>
/// Text measurement runs on whatever thread asks for it, and the fallback font is a
/// process-wide singleton with glyph and advance caches behind it.
/// </summary>
/// <remarks>
/// Those caches were plain <c>Dictionary</c> instances written without
/// synchronization, so two threads measuring at once tore the buckets and threw
/// <c>IndexOutOfRangeException</c> from inside <c>Dictionary.TryInsert</c>. It
/// surfaced as Broiler.UI's toolbar tests failing intermittently — xunit runs test
/// classes in an assembly in parallel, and several of them measure button text — with
/// stack traces that pointed at <c>FallbackSystemFont.Resolve</c> rather than at
/// anything the tests did.
/// </remarks>
internal static class TextMeasurementConcurrencyTests
{
    public static void Register(List<(string Name, Action Body)> tests)
    {
        tests.Add(("text measurement is thread safe", MeasuringFromManyThreadsDoesNotCorruptTheGlyphCache));
    }

    private static void MeasuringFromManyThreadsDoesNotCorruptTheGlyphCache()
    {
        // The caches live on a process-wide singleton, so a process gets exactly one
        // cold window in which inserts race and the dictionary resizes. A barrier
        // lines every thread up on that window instead of letting the first thread
        // warm the cache while the others are still starting.
        BFontStyle regular = new("sans-serif", 14);
        BFontStyle bold = new("sans-serif", 14, BFontWeight.Bold);

        const int Threads = 16;
        const int CodepointsPerThread = 512;

        using Barrier barrier = new(Threads);
        Exception? failure = null;
        object gate = new();

        Thread[] workers = new Thread[Threads];
        for (int worker = 0; worker < Threads; worker++)
        {
            int index = worker;
            workers[worker] = new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    for (int step = 0; step < CodepointsPerThread; step++)
                    {
                        // Interleaved so the threads insert distinct, adjacent keys
                        // into the same buckets rather than repeating one another.
                        char codepoint = (char)(0x0100 + (step * Threads) + index);
                        BTextMeasurer.Measure(codepoint.ToString(), (step & 1) == 0 ? regular : bold);
                    }
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

        AssertEx.IsTrue(
            failure is null,
            $"Concurrent text measurement must not corrupt the shared glyph cache, but threw: {failure}");
    }
}
