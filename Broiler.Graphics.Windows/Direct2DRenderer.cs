using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

/// <summary>
/// The Windows / Direct2D implementation of <see cref="IBroilerRenderer"/>. Owns a
/// <see cref="Direct2DDevice"/> and replays <see cref="BRenderList"/> instances onto
/// Direct2D-backed targets.
/// </summary>
public sealed class Direct2DRenderer : IBroilerRenderer
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BeginDrawProc(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EndDrawProc(IntPtr self, IntPtr tag1, IntPtr tag2);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ClearProc(IntPtr self, in D2DNative.D2D1_COLOR_F color);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetAntialiasModeProc(IntPtr self, D2DNative.D2D1_ANTIALIAS_MODE antialiasMode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetTextAntialiasModeProc(IntPtr self, D2DNative.D2D1_TEXT_ANTIALIAS_MODE textAntialiasMode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetTransformProc(IntPtr self, in D2DNative.D2D1_MATRIX_3X2_F transform);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateSolidColorBrushProc(
        IntPtr self,
        in D2DNative.D2D1_COLOR_F color,
        IntPtr brushProperties,
        out IntPtr solidColorBrush);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FillRectangleProc(IntPtr self, in D2DNative.D2D1_RECT_F rect, IntPtr brush);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DrawRectangleProc(
        IntPtr self,
        in D2DNative.D2D1_RECT_F rect,
        IntPtr brush,
        float strokeWidth,
        IntPtr strokeStyle);

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
    private delegate void DrawTextProc(
        IntPtr self,
        [MarshalAs(UnmanagedType.LPWStr)] string text,
        uint textLength,
        IntPtr textFormat,
        in D2DNative.D2D1_RECT_F layoutRect,
        IntPtr brush,
        D2DNative.D2D1_DRAW_TEXT_OPTIONS options,
        DWriteNative.DWRITE_MEASURING_MODE measuringMode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void DrawBitmapProc(
        IntPtr self,
        IntPtr bitmap,
        in D2DNative.D2D1_RECT_F destination,
        float opacity,
        uint interpolation,
        in D2DNative.D2D1_RECT_F source);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PushAxisAlignedClipProc(
        IntPtr self,
        in D2DNative.D2D1_RECT_F clipRect,
        D2DNative.D2D1_ANTIALIAS_MODE antialiasMode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void PopAxisAlignedClipProc(IntPtr self);

    private readonly Direct2DDevice _device;
    private readonly Direct2DImageStore _images = new();
    private readonly Stack<BMatrix3x2> _transformStack = new();

    private BMatrix3x2 _currentTransform = BMatrix3x2.Identity;
    private bool _disposed;

    public Direct2DRenderer()
    {
        _device = new Direct2DDevice();
        _device.Initialize();

        // Ensure image bytes can be decoded into pixel buffers (used when materializing
        // bitmaps for DrawImage). Registering the dependency-free managed codec keeps the
        // backend self-contained, but never overrides a codec the caller chose explicitly.
        BImageCodec.UseManagedIfUnset();
    }

    public IBroilerSurface CreateSurface(BSurfaceDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Direct2DSurface(_device, descriptor);
    }

    /// <summary>Creates a swap-chain surface that presents directly to a Win32 window.</summary>
    public IBroilerSurface CreateHwndSurface(IntPtr hwnd, BSurfaceDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (hwnd == IntPtr.Zero)
            throw new ArgumentException("A non-zero HWND is required.", nameof(hwnd));

        return new Direct2DSurface(_device, descriptor, hwnd);
    }

    public BImageHandle CreateImage(ReadOnlySpan<byte> encodedImage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Decode through the active codec, then keep the pixels ready for GPU upload.
        BPixelBuffer pixels = BImageCodec.Decode(encodedImage);
        return _images.Add(pixels);
    }

    public BImageHandle CreateImage(BPixelBuffer pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pixels);
        return _images.Add(pixels);
    }

    public void ReleaseImage(BImageHandle image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _images.Remove(image);
    }

    public void Render(IBroilerSurface surface, BRenderList renderList, BFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(renderList);

        if (surface is not IDirect2DSurface d2dSurface)
            throw new ArgumentException("Surface was not created by this renderer.", nameof(surface));

        renderList.Validate();
        ResetManagedDrawingState();

        BeginDraw(d2dSurface, frameContext);
        try
        {
            foreach (BRenderCommand command in renderList.Commands)
                Dispatch(d2dSurface, command);
        }
        finally
        {
            EndDraw(d2dSurface, frameContext);
            ResetManagedDrawingState();
        }
    }

    public BBitmap RenderToImage(BRenderList renderList, BSurfaceDescriptor descriptor, BFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(renderList);

        using var surface = new Direct2DOffscreenSurface(_device, descriptor);
        Render(surface, renderList, frameContext);
        return surface.ReadToBitmap();
    }

    private static void BeginDraw(IDirect2DSurface surface, BFrameContext frame)
    {
        IntPtr context = surface.Context;

        BeginDrawProc beginDraw = ComVtable.Method<BeginDrawProc>(context, D2DNative.VtblBeginDraw);
        beginDraw(context);

        Direct2DSurface.SetDpi(context, (float)(96.0 * surface.DpiScale));
        SetAntialiasMode(context, frame.Options.Antialias
            ? D2DNative.D2D1_ANTIALIAS_MODE.PER_PRIMITIVE
            : D2DNative.D2D1_ANTIALIAS_MODE.ALIASED);
        SetTextAntialiasMode(context, ToTextAntialiasMode(frame.Options));
        SetTransform(context, BMatrix3x2.Identity);
        Clear(context, frame.ClearColor);
    }

    private static void EndDraw(IDirect2DSurface surface, BFrameContext frame)
    {
        IntPtr context = surface.Context;

        EndDrawProc endDraw = ComVtable.Method<EndDrawProc>(context, D2DNative.VtblEndDraw);
        int hr = endDraw(context, IntPtr.Zero, IntPtr.Zero);
        if (hr == D2DNative.D2DERR_RECREATE_TARGET)
            throw new BDeviceLostException("Direct2D target resources must be recreated.", hr);

        NativeMethods.ThrowIfFailed(hr, "ID2D1DeviceContext::EndDraw");
        surface.Present(frame.Options.VSync);
    }

    /// <summary>Routes a single command to its Direct2D translation. The switch is exhaustive.</summary>
    private void Dispatch(IDirect2DSurface surface, BRenderCommand command)
    {
        switch (command)
        {
            case BRenderCommand.FillRect c:
                FillRect(surface, c);
                break;
            case BRenderCommand.StrokeRect c:
                StrokeRect(surface, c);
                break;
            case BRenderCommand.DrawText c:
                DrawText(surface, c);
                break;
            case BRenderCommand.DrawImage c:
                DrawImage(surface, c);
                break;
            case BRenderCommand.PushClip c:
                PushClip(surface, c);
                break;
            case BRenderCommand.PopClip:
                PopClip(surface);
                break;
            case BRenderCommand.PushTransform c:
                PushTransform(surface, c);
                break;
            case BRenderCommand.PopTransform:
                PopTransform(surface);
                break;
            default:
                throw new NotSupportedException($"Unknown render command: {command.GetType().Name}");
        }
    }

    private void FillRect(IDirect2DSurface surface, BRenderCommand.FillRect c)
    {
        IntPtr context = surface.Context;
        using ComPtr brush = CreateSolidBrush(context, c.Color);
        D2DNative.D2D1_RECT_F rect = ToRectF(c.Rect);

        FillRectangleProc fill = ComVtable.Method<FillRectangleProc>(context, D2DNative.VtblFillRectangle);
        fill(context, in rect, brush.Pointer);
    }

    private void StrokeRect(IDirect2DSurface surface, BRenderCommand.StrokeRect c)
    {
        IntPtr context = surface.Context;
        using ComPtr brush = CreateSolidBrush(context, c.Color);
        D2DNative.D2D1_RECT_F rect = ToRectF(c.Rect);

        DrawRectangleProc draw = ComVtable.Method<DrawRectangleProc>(context, D2DNative.VtblDrawRectangle);
        draw(context, in rect, brush.Pointer, (float)c.Thickness, IntPtr.Zero);
    }

    private void DrawText(IDirect2DSurface surface, BRenderCommand.DrawText c)
    {
        if (string.IsNullOrEmpty(c.Text.Text))
            return;

        IntPtr context = surface.Context;
        using ComPtr brush = CreateSolidBrush(context, c.Text.Color);
        using ComPtr textFormat = CreateTextFormat(c.Text.Font);

        D2DNative.D2D1_RECT_F layoutRect = ToTextLayoutRect(c.Origin);
        DrawTextProc drawText = ComVtable.Method<DrawTextProc>(context, D2DNative.VtblDrawText);
        drawText(
            context,
            c.Text.Text,
            checked((uint)c.Text.Text.Length),
            textFormat.Pointer,
            in layoutRect,
            brush.Pointer,
            D2DNative.D2D1_DRAW_TEXT_OPTIONS.NONE,
            DWriteNative.DWRITE_MEASURING_MODE.NATURAL);
    }

    private void DrawImage(IDirect2DSurface surface, BRenderCommand.DrawImage c)
    {
        // Resolve the handle, upload the pixels on first use, then draw onto the context.
        Direct2DImage image = _images.Get(c.Image);
        IntPtr context = surface.Context;
        IntPtr bitmap = image.EnsureBitmap(context);

        DrawBitmap(
            context,
            bitmap,
            ToRectF(c.Destination),
            (float)c.Opacity,
            D2DNative.D2D1_BITMAP_INTERPOLATION_MODE.LINEAR,
            ToRectF(c.Source));
    }

    /// <summary>ID2D1RenderTarget::DrawBitmap via the device-context vtable.</summary>
    private static void DrawBitmap(
        IntPtr context,
        IntPtr bitmap,
        D2DNative.D2D1_RECT_F destination,
        float opacity,
        D2DNative.D2D1_BITMAP_INTERPOLATION_MODE interpolation,
        D2DNative.D2D1_RECT_F source)
    {
        DrawBitmapProc drawBitmap = ComVtable.Method<DrawBitmapProc>(context, D2DNative.VtblDrawBitmap);
        drawBitmap(context, bitmap, in destination, opacity, (uint)interpolation, in source);
    }

    private void PushClip(IDirect2DSurface surface, BRenderCommand.PushClip c)
    {
        IntPtr context = surface.Context;
        D2DNative.D2D1_RECT_F rect = ToRectF(c.Rect);

        PushAxisAlignedClipProc pushClip =
            ComVtable.Method<PushAxisAlignedClipProc>(context, D2DNative.VtblPushAxisAlignedClip);
        pushClip(context, in rect, D2DNative.D2D1_ANTIALIAS_MODE.PER_PRIMITIVE);
    }

    private void PopClip(IDirect2DSurface surface)
    {
        IntPtr context = surface.Context;
        PopAxisAlignedClipProc popClip = ComVtable.Method<PopAxisAlignedClipProc>(context, D2DNative.VtblPopAxisAlignedClip);
        popClip(context);
    }

    private void PushTransform(IDirect2DSurface surface, BRenderCommand.PushTransform c)
    {
        _transformStack.Push(_currentTransform);
        _currentTransform = _currentTransform * c.Transform;
        SetTransform(surface.Context, _currentTransform);
    }

    private void PopTransform(IDirect2DSurface surface)
    {
        _currentTransform = _transformStack.Pop();
        SetTransform(surface.Context, _currentTransform);
    }

    private static ComPtr CreateSolidBrush(IntPtr context, BColor color)
    {
        CreateSolidColorBrushProc createBrush =
            ComVtable.Method<CreateSolidColorBrushProc>(context, D2DNative.VtblCreateSolidColorBrush);
        D2DNative.D2D1_COLOR_F nativeColor = ToColorF(color);
        int hr = createBrush(context, in nativeColor, IntPtr.Zero, out IntPtr brush);
        NativeMethods.ThrowIfFailed(hr, "ID2D1DeviceContext::CreateSolidColorBrush");
        return new ComPtr(brush);
    }

    private ComPtr CreateTextFormat(BFontStyle font)
    {
        CreateTextFormatProc createTextFormat =
            ComVtable.Method<CreateTextFormatProc>(_device.DWriteFactory.Pointer, DWriteNative.VtblCreateTextFormat);
        int hr = createTextFormat(
            _device.DWriteFactory.Pointer,
            ResolveFontFamily(font.FamilyName),
            IntPtr.Zero,
            DWriteNative.ToDWrite(font.Weight),
            DWriteNative.ToDWrite(font.Slant),
            DWriteNative.DWRITE_FONT_STRETCH.NORMAL,
            ToFontSize(font.SizeInPixels),
            CurrentLocaleName(),
            out IntPtr textFormat);
        NativeMethods.ThrowIfFailed(hr, "IDWriteFactory::CreateTextFormat");
        return new ComPtr(textFormat);
    }

    private void ResetManagedDrawingState()
    {
        _transformStack.Clear();
        _currentTransform = BMatrix3x2.Identity;
    }

    private static void Clear(IntPtr context, BColor color)
    {
        ClearProc clear = ComVtable.Method<ClearProc>(context, D2DNative.VtblClear);
        D2DNative.D2D1_COLOR_F nativeColor = ToColorF(color);
        clear(context, in nativeColor);
    }

    private static void SetAntialiasMode(IntPtr context, D2DNative.D2D1_ANTIALIAS_MODE mode)
    {
        SetAntialiasModeProc setMode = ComVtable.Method<SetAntialiasModeProc>(context, D2DNative.VtblSetAntialiasMode);
        setMode(context, mode);
    }

    private static void SetTextAntialiasMode(IntPtr context, D2DNative.D2D1_TEXT_ANTIALIAS_MODE mode)
    {
        SetTextAntialiasModeProc setMode =
            ComVtable.Method<SetTextAntialiasModeProc>(context, D2DNative.VtblSetTextAntialiasMode);
        setMode(context, mode);
    }

    private static D2DNative.D2D1_TEXT_ANTIALIAS_MODE ToTextAntialiasMode(BRenderOptions options)
    {
        if (!options.Antialias)
            return D2DNative.D2D1_TEXT_ANTIALIAS_MODE.ALIASED;

        return options.SubpixelText
            ? D2DNative.D2D1_TEXT_ANTIALIAS_MODE.DEFAULT
            : D2DNative.D2D1_TEXT_ANTIALIAS_MODE.GRAYSCALE;
    }

    private static void SetTransform(IntPtr context, BMatrix3x2 transform)
    {
        D2DNative.D2D1_MATRIX_3X2_F matrix = ToMatrix(transform);
        SetTransformProc setTransform = ComVtable.Method<SetTransformProc>(context, D2DNative.VtblSetTransform);
        setTransform(context, in matrix);
    }

    private static D2DNative.D2D1_RECT_F ToRectF(BRect r) => new()
    {
        Left = (float)r.Left,
        Top = (float)r.Top,
        Right = (float)r.Right,
        Bottom = (float)r.Bottom,
    };

    private static D2DNative.D2D1_RECT_F ToTextLayoutRect(BPoint origin)
    {
        const float largeTextLayoutExtent = 1_048_576f;
        float left = (float)origin.X;
        float top = (float)origin.Y;
        return new D2DNative.D2D1_RECT_F
        {
            Left = left,
            Top = top,
            Right = left + largeTextLayoutExtent,
            Bottom = top + largeTextLayoutExtent,
        };
    }

    private static D2DNative.D2D1_COLOR_F ToColorF(BColor color) => new()
    {
        R = color.Rf,
        G = color.Gf,
        B = color.Bf,
        A = color.Af,
    };

    private static D2DNative.D2D1_MATRIX_3X2_F ToMatrix(BMatrix3x2 matrix) => new()
    {
        M11 = (float)matrix.M11,
        M12 = (float)matrix.M12,
        M21 = (float)matrix.M21,
        M22 = (float)matrix.M22,
        Dx = (float)matrix.M31,
        Dy = (float)matrix.M32,
    };

    private static float ToFontSize(double sizeInPixels)
    {
        if (sizeInPixels <= 0 || double.IsNaN(sizeInPixels) || double.IsInfinity(sizeInPixels))
            return 1.0f;

        return (float)Math.Min(sizeInPixels, float.MaxValue);
    }

    private static string ResolveFontFamily(string familyName)
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

    private static string CurrentLocaleName()
    {
        string name = CultureInfo.CurrentUICulture.Name;
        return string.IsNullOrWhiteSpace(name) ? "en-us" : name;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _images.Dispose();
        _device.Dispose();
    }
}
