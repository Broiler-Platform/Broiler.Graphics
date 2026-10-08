using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
    private readonly Dictionary<(BFontStyle Font, float? Baseline), ComPtr> _cache = new();
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

    /// <summary>
    /// The format for <paramref name="font"/>, with its first line's baseline
    /// <paramref name="baseline"/> below the layout box's top when that is given
    /// (<see cref="BTextRun.Baseline"/>).
    /// </summary>
    public ComPtr GetOrCreate(IntPtr factory, BFontStyle font, double? baseline = null)
    {
        var key = (font, baseline is double value ? (float?)(float)value : null);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out ComPtr? existing))
            {
                Interlocked.Increment(ref _hits);
                return existing;
            }

            if (_cache.Count >= MaxCapacity)
                ClearLocked();

            ComPtr format = CreateTextFormat(factory, font);
            if (key.Item2 is float stated)
                SetBaseline(format, font, stated);

            _cache[key] = format;
            Interlocked.Increment(ref _misses);
            return format;
        }
    }

    /// <summary>
    /// IDWriteTextFormat::SetLineSpacing with DWRITE_LINE_SPACING_METHOD_UNIFORM: every line is
    /// <c>lineSpacing</c> tall with its baseline <c>baseline</c> below the line's top, so the first
    /// line's baseline lies exactly where the layout put it. The run is one line, so the spacing
    /// only has to leave room for the descent below that baseline.
    /// </summary>
    private static void SetBaseline(ComPtr format, BFontStyle font, float baseline)
    {
        float lineSpacing = Math.Max(baseline, 0f) + (float)Math.Max(font.Size, 1.0);
        try
        {
            int hr = ComVtable.Method<SetLineSpacingProc>(format.Pointer, VtblSetLineSpacing)(
                format.Pointer, DwriteLineSpacingMethodUniform, lineSpacing, Math.Max(baseline, 0f));
            NativeMethods.ThrowIfFailed(hr, "IDWriteTextFormat::SetLineSpacing");
        }
        catch
        {
            format.Dispose();
            throw;
        }
    }

    // IDWriteTextFormat (dwrite.h): IUnknown's three slots, then SetTextAlignment, SetParagraphAlignment,
    // SetWordWrapping, SetReadingDirection, SetFlowDirection, SetIncrementalTabStop, SetTrimming and
    // SetLineSpacing, the eighth: slot 10. Broiler.Native.Windows does not declare it yet.
    private const int VtblSetLineSpacing = 10;

    // DWRITE_LINE_SPACING_METHOD_UNIFORM.
    private const int DwriteLineSpacingMethodUniform = 1;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetLineSpacingProc(IntPtr textFormat, int lineSpacingMethod, float lineSpacing, float baseline);

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
