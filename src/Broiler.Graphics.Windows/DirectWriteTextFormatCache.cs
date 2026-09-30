using System;
using System.Collections.Generic;
using System.Threading;
using Broiler.Graphics.Text;
using Broiler.Native.Windows.Direct2D;
using static Broiler.Native.Windows.Direct2D.DWriteNative;
using static Broiler.Native.Windows.Direct2D.DirectWriteTextMetricsProviderApi;

namespace Broiler.Graphics.Windows;

/// <summary>
/// Bounded cache for device-independent DirectWrite text formats (<c>IDWriteTextFormat</c>).
/// Immutable formats are reused across drawing and measurement passes to avoid repeated factory allocations.
/// </summary>
internal sealed class DirectWriteTextFormatCache : IDisposable
{
    private const int MaxCapacity = 256;
    private readonly object _lock = new();
    private readonly Dictionary<BFontStyle, ComPtr> _cache = new();
    private long _hits;
    private long _misses;

    public long FormatCacheHits => Volatile.Read(ref _hits);
    public long FormatCacheMisses => Volatile.Read(ref _misses);

    public int CachedFormatCount
    {
        get
        {
            lock (_lock)
                return _cache.Count;
        }
    }

    public ComPtr GetOrCreate(IntPtr factory, BFontStyle font)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(font, out ComPtr? existing))
            {
                Interlocked.Increment(ref _hits);
                return existing;
            }

            if (_cache.Count >= MaxCapacity)
                ClearLocked();

            ComPtr format = CreateTextFormat(factory, font);
            _cache[font] = format;
            Interlocked.Increment(ref _misses);
            return format;
        }
    }

    private static ComPtr CreateTextFormat(IntPtr factory, BFontStyle font)
    {
        CreateTextFormatProc createTextFormat =
            ComVtable.Method<CreateTextFormatProc>(factory, DWriteNative.VtblCreateTextFormat);
        int hr = createTextFormat(
            factory,
            DirectWriteText.ResolveFontFamily(font.FamilyName),
            IntPtr.Zero,
            DWriteConversions.ToDWrite(font.Weight),
            DWriteConversions.ToDWrite(font.Slant),
            DWriteNative.DWRITE_FONT_STRETCH.NORMAL,
            DirectWriteText.ToFontSize(font.Size),
            DirectWriteText.CurrentLocaleName(),
            out IntPtr textFormat);
        NativeMethods.ThrowIfFailed(hr, "IDWriteFactory::CreateTextFormat");
        return new ComPtr(textFormat);
    }

    private void ClearLocked()
    {
        foreach (ComPtr format in _cache.Values)
            format.Dispose();
        _cache.Clear();
    }

    public void Dispose()
    {
        lock (_lock)
            ClearLocked();
    }
}
