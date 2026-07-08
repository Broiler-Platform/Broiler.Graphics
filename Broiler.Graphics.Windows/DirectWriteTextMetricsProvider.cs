using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

internal sealed class DirectWriteTextMetricsProvider : IBTextMetricsProvider
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int CreateTextFormatProc(
        IntPtr self,
        [MarshalAs(UnmanagedType.LPWStr)] string fontFamilyName,
        IntPtr fontCollection,
        DWriteNative.DWRITE_FONT_WEIGHT fontWeight,
        DWriteNative.DWRITE_FONT_STYLE fontStyle,
        DWriteNative.DWRITE_FONT_STRETCH fontStretch,
        float fontSize,
        [MarshalAs(UnmanagedType.LPWStr)] string localeName,
        out IntPtr textFormat);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int CreateTextLayoutProc(
        IntPtr self,
        [MarshalAs(UnmanagedType.LPWStr)] string text,
        uint textLength,
        IntPtr textFormat,
        float maxWidth,
        float maxHeight,
        out IntPtr textLayout);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetMetricsProc(IntPtr self, out DWriteNative.DWRITE_TEXT_METRICS metrics);

    private static readonly Lazy<DirectWriteTextMetricsProvider?> Shared = new(TryCreate, isThreadSafe: true);

    private readonly ComPtr _factory;
    private readonly ConcurrentDictionary<BFontStyle, double> _lineHeightCache = new();

    private DirectWriteTextMetricsProvider(ComPtr factory) => _factory = factory;

    internal static bool UseIfUnset()
    {
        DirectWriteTextMetricsProvider? provider = Shared.Value;
        return provider is not null && BTextMeasurer.UseProviderIfDefault(provider);
    }

    public double MeasureAdvance(string text, BFontStyle font)
    {
        if (text.Length == 0)
            return 0;

        DWriteNative.DWRITE_TEXT_METRICS metrics = MeasureLayout(text, font);
        return Math.Round(Math.Max(0, metrics.WidthIncludingTrailingWhitespace), 2);
    }

    public double GetLineHeight(BFontStyle font) => _lineHeightCache.GetOrAdd(font, MeasureLineHeight);

    private double MeasureLineHeight(BFontStyle font)
    {
        DWriteNative.DWRITE_TEXT_METRICS metrics = MeasureLayout("Hg", font);
        double height = metrics.Height > 0 ? metrics.Height : Math.Ceiling(font.SizeInPixels * 1.25);
        return Math.Ceiling(height);
    }

    private DWriteNative.DWRITE_TEXT_METRICS MeasureLayout(string text, BFontStyle font)
    {
        using ComPtr textFormat = CreateTextFormat(font);
        using ComPtr layout = CreateTextLayout(text, textFormat.Pointer);

        GetMetricsProc getMetrics = ComVtable.Method<GetMetricsProc>(layout.Pointer, DWriteNative.VtblGetMetrics);
        int hr = getMetrics(layout.Pointer, out DWriteNative.DWRITE_TEXT_METRICS metrics);
        NativeMethods.ThrowIfFailed(hr, "IDWriteTextLayout::GetMetrics");
        return metrics;
    }

    private ComPtr CreateTextFormat(BFontStyle font)
    {
        CreateTextFormatProc createTextFormat =
            ComVtable.Method<CreateTextFormatProc>(_factory.Pointer, DWriteNative.VtblCreateTextFormat);
        int hr = createTextFormat(
            _factory.Pointer,
            DirectWriteText.ResolveFontFamily(font.FamilyName),
            IntPtr.Zero,
            DWriteNative.ToDWrite(font.Weight),
            DWriteNative.ToDWrite(font.Slant),
            DWriteNative.DWRITE_FONT_STRETCH.NORMAL,
            DirectWriteText.ToFontSize(font.SizeInPixels),
            DirectWriteText.CurrentLocaleName(),
            out IntPtr textFormat);
        NativeMethods.ThrowIfFailed(hr, "IDWriteFactory::CreateTextFormat");
        return new ComPtr(textFormat);
    }

    private ComPtr CreateTextLayout(string text, IntPtr textFormat)
    {
        CreateTextLayoutProc createTextLayout =
            ComVtable.Method<CreateTextLayoutProc>(_factory.Pointer, DWriteNative.VtblCreateTextLayout);
        int hr = createTextLayout(
            _factory.Pointer,
            text,
            checked((uint)text.Length),
            textFormat,
            DirectWriteText.LargeTextLayoutExtent,
            DirectWriteText.LargeTextLayoutExtent,
            out IntPtr textLayout);
        NativeMethods.ThrowIfFailed(hr, "IDWriteFactory::CreateTextLayout");
        return new ComPtr(textLayout);
    }

    private static DirectWriteTextMetricsProvider? TryCreate()
    {
        try
        {
            int hr = NativeMethods.DWriteCreateFactory(
                DWriteNative.DWRITE_FACTORY_TYPE.SHARED,
                DWriteNative.IID_IDWriteFactory,
                out IntPtr factory);
            if (!NativeMethods.Succeeded(hr))
                return null;

            return new DirectWriteTextMetricsProvider(new ComPtr(factory));
        }
        catch
        {
            return null;
        }
    }
}

internal static class DirectWriteText
{
    internal const float LargeTextLayoutExtent = 1_048_576f;

    internal static float ToFontSize(double sizeInPixels)
    {
        if (sizeInPixels <= 0 || double.IsNaN(sizeInPixels) || double.IsInfinity(sizeInPixels))
            return 1.0f;

        return (float)Math.Min(sizeInPixels, float.MaxValue);
    }

    internal static string ResolveFontFamily(string familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
            return "Segoe UI";

        string trimmed = familyName.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "sans-serif" => "Segoe UI",
            "serif" => "Times New Roman",
            "monospace" => "Consolas",
            "monospaced" => "Consolas",
            _ => trimmed,
        };
    }

    internal static string CurrentLocaleName()
    {
        string name = CultureInfo.CurrentUICulture.Name;
        return string.IsNullOrWhiteSpace(name) ? "en-us" : name;
    }
}
