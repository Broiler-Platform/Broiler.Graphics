using System;
using System.Threading;

namespace Broiler.Graphics.Resources;

/// <summary>Allocates image IDs shared by all renderer instances and backends.</summary>
internal static class BResourceIds
{
    private static long _nextImageId;

    internal static ulong NextImageId()
    {
        long id = Interlocked.Increment(ref _nextImageId);
        // The browser replay stream transports IDs as JavaScript numbers.
        if (id <= 0 || id > 9_007_199_254_740_991L)
            throw new InvalidOperationException("The image resource ID space is exhausted.");

        return (ulong)id;
    }
}
