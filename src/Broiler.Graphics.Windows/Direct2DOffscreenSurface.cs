using System;
using System.Runtime.InteropServices;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

internal sealed class Direct2DOffscreenSurface : IDirect2DSurface
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDeviceContextProc(
        IntPtr self,
        D2DNative.D2D1_DEVICE_CONTEXT_OPTIONS options,
        out IntPtr deviceContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateBitmap1Proc(
        IntPtr self,
        D2DNative.D2D1_SIZE_U size,
        IntPtr sourceData,
        uint pitch,
        ref D2DNative.D2D1_BITMAP_PROPERTIES1 bitmapProperties,
        out IntPtr bitmap);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CopyFromBitmapProc(
        IntPtr self,
        IntPtr destinationPoint,
        IntPtr bitmap,
        IntPtr sourceRect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapProc(
        IntPtr self,
        D2DNative.D2D1_MAP_OPTIONS options,
        out D2DNative.D2D1_MAPPED_RECT mappedRect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UnmapProc(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetTargetProc(IntPtr self, IntPtr image);

    private readonly Direct2DDevice _device;
    private readonly ComPtr _d2dContext = new();
    private readonly ComPtr _targetBitmap = new();

    private BSize _size;
    private double _dpiScale;
    private BPixelFormat _pixelFormat;
    private bool _enableTransparency;
    private bool _disposed;

    internal Direct2DOffscreenSurface(Direct2DDevice device, BSurfaceDescriptor descriptor)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _size = ValidateSize(descriptor.Size);
        _dpiScale = NormalizeDpiScale(descriptor.DpiScale);
        _pixelFormat = descriptor.PixelFormat;
        _enableTransparency = descriptor.EnableTransparency;

        try
        {
            CreateDeviceContext();
            CreateTargetBitmap();
        }
        catch
        {
            ReleaseNativeResources();
            throw;
        }
    }

    public BSize Size => _size;

    public double DpiScale => _dpiScale;

    public IntPtr Context => _d2dContext.Pointer;

    public void Resize(BSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _size = ValidateSize(size);
        _dpiScale = NormalizeDpiScale(dpiScale);
        ReleaseTargetBitmap();
        CreateTargetBitmap();
    }

    public void Present(bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public BBitmap ReadToBitmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using ComPtr readback = CreateBitmap(
            D2DNative.D2D1_BITMAP_OPTIONS.CPU_READ | D2DNative.D2D1_BITMAP_OPTIONS.CANNOT_DRAW);

        CopyFromBitmapProc copyFromBitmap =
            ComVtable.Method<CopyFromBitmapProc>(readback.Pointer, D2DNative.VtblBitmapCopyFromBitmap);
        int hr = copyFromBitmap(readback.Pointer, IntPtr.Zero, _targetBitmap.Pointer, IntPtr.Zero);
        NativeMethods.ThrowIfFailed(hr, "ID2D1Bitmap::CopyFromBitmap");

        MapProc map = ComVtable.Method<MapProc>(readback.Pointer, D2DNative.VtblBitmap1Map);
        UnmapProc unmap = ComVtable.Method<UnmapProc>(readback.Pointer, D2DNative.VtblBitmap1Unmap);

        hr = map(readback.Pointer, D2DNative.D2D1_MAP_OPTIONS.READ, out D2DNative.D2D1_MAPPED_RECT mapped);
        NativeMethods.ThrowIfFailed(hr, "ID2D1Bitmap1::Map");

        try
        {
            return CopyMappedPixels(mapped);
        }
        finally
        {
            NativeMethods.ThrowIfFailed(unmap(readback.Pointer), "ID2D1Bitmap1::Unmap");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseNativeResources();
    }

    private void CreateDeviceContext()
    {
        CreateDeviceContextProc createContext =
            ComVtable.Method<CreateDeviceContextProc>(_device.D2DDevice.Pointer, D2DNative.VtblCreateDeviceContext);
        int hr = createContext(
            _device.D2DDevice.Pointer,
            D2DNative.D2D1_DEVICE_CONTEXT_OPTIONS.NONE,
            out IntPtr context);
        NativeMethods.ThrowIfFailed(hr, "ID2D1Device::CreateDeviceContext");
        _d2dContext.Attach(context);
    }

    private void CreateTargetBitmap()
    {
        using ComPtr bitmap = CreateBitmap(D2DNative.D2D1_BITMAP_OPTIONS.TARGET);
        _targetBitmap.Attach(bitmap.Pointer);
        bitmap.AddRef();

        Direct2DSurface.SetDpi(_d2dContext.Pointer, Dpi);
        SetTarget(_d2dContext.Pointer, _targetBitmap.Pointer);
    }

    private ComPtr CreateBitmap(D2DNative.D2D1_BITMAP_OPTIONS options)
    {
        CreateBitmap1Proc createBitmap =
            ComVtable.Method<CreateBitmap1Proc>(_d2dContext.Pointer, D2DNative.VtblCreateBitmap1);

        var size = new D2DNative.D2D1_SIZE_U { Width = PixelWidth, Height = PixelHeight };
        var properties = new D2DNative.D2D1_BITMAP_PROPERTIES1
        {
            PixelFormat = new D2DNative.D2D1_PIXEL_FORMAT
            {
                Format = ToDxgiFormat(_pixelFormat),
                AlphaMode = _enableTransparency
                    ? D2DNative.D2D1_ALPHA_MODE.PREMULTIPLIED
                    : D2DNative.D2D1_ALPHA_MODE.IGNORE,
            },
            DpiX = Dpi,
            DpiY = Dpi,
            BitmapOptions = options,
            ColorContext = IntPtr.Zero,
        };

        int hr = createBitmap(_d2dContext.Pointer, size, IntPtr.Zero, 0, ref properties, out IntPtr bitmap);
        NativeMethods.ThrowIfFailed(hr, "ID2D1DeviceContext::CreateBitmap");
        return new ComPtr(bitmap);
    }

    private BBitmap CopyMappedPixels(D2DNative.D2D1_MAPPED_RECT mapped)
    {
        int width = checked((int)PixelWidth);
        int height = checked((int)PixelHeight);
        int rowBytes = checked(width * BPixelBuffer.BytesPerPixel);
        byte[] row = new byte[rowBytes];
        byte[] rgba = new byte[checked(rowBytes * height)];

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(IntPtr.Add(mapped.Bits, checked((int)(y * mapped.Pitch))), row, 0, rowBytes);
            int dst = y * rowBytes;

            for (int src = 0; src < rowBytes; src += 4)
            {
                byte a = row[src + 3];
                if (_pixelFormat == BPixelFormat.Bgra8)
                {
                    rgba[dst++] = Unpremultiply(row[src + 2], a);
                    rgba[dst++] = Unpremultiply(row[src + 1], a);
                    rgba[dst++] = Unpremultiply(row[src], a);
                }
                else
                {
                    rgba[dst++] = Unpremultiply(row[src], a);
                    rgba[dst++] = Unpremultiply(row[src + 1], a);
                    rgba[dst++] = Unpremultiply(row[src + 2], a);
                }

                rgba[dst++] = a;
            }
        }

        return new BBitmap(width, height, rgba, takeOwnership: true);
    }

    private void ReleaseNativeResources()
    {
        ReleaseTargetBitmap();
        _d2dContext.Dispose();
    }

    private void ReleaseTargetBitmap()
    {
        if (!_d2dContext.IsNull)
            SetTarget(_d2dContext.Pointer, IntPtr.Zero);

        _targetBitmap.Release();
    }

    private uint PixelWidth => ToPixelDimension(_size.Width, _dpiScale, nameof(Size));

    private uint PixelHeight => ToPixelDimension(_size.Height, _dpiScale, nameof(Size));

    private float Dpi => (float)(96.0 * _dpiScale);

    private static void SetTarget(IntPtr context, IntPtr target)
    {
        SetTargetProc setTarget = ComVtable.Method<SetTargetProc>(context, D2DNative.VtblSetTarget);
        setTarget(context, target);
    }

    private static byte Unpremultiply(byte value, byte alpha)
    {
        if (alpha == 0)
            return 0;
        if (alpha == 255)
            return value;

        return (byte)Math.Clamp(((value * 255) + (alpha / 2)) / alpha, 0, 255);
    }

    private static BSize ValidateSize(BSize size)
    {
        if (!IsPositiveFinite(size.Width) || !IsPositiveFinite(size.Height))
            throw new ArgumentOutOfRangeException(nameof(size), "Surface size must be positive and finite.");

        return size;
    }

    private static double NormalizeDpiScale(double dpiScale)
    {
        if (!IsPositiveFinite(dpiScale))
            return 1.0;

        return dpiScale;
    }

    private static bool IsPositiveFinite(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static uint ToPixelDimension(double dip, double dpiScale, string name)
    {
        double pixels = Math.Ceiling(dip * dpiScale);
        if (pixels <= 0 || pixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Surface pixel dimensions are outside the supported range.");

        return (uint)pixels;
    }

    private static DxgiNative.DXGI_FORMAT ToDxgiFormat(BPixelFormat pixelFormat) => pixelFormat switch
    {
        BPixelFormat.Bgra8 => DxgiNative.DXGI_FORMAT.B8G8R8A8_UNORM,
        BPixelFormat.Rgba8 => DxgiNative.DXGI_FORMAT.R8G8B8A8_UNORM,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unsupported pixel format."),
    };
}
