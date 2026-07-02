using System;
using System.Runtime.InteropServices;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

internal interface IDirect2DSurface : IBroilerSurface
{
    IntPtr Context { get; }

    void Present(bool vsync);
}

/// <summary>
/// A Direct2D-backed drawable surface. Owns the DXGI swap chain and the Direct2D device-context /
/// target bitmap derived from it. Public members are safe; native handles stay internal.
/// </summary>
internal sealed class Direct2DSurface : IDirect2DSurface
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateSwapChainForCompositionProc(
        IntPtr self,
        IntPtr device,
        ref DxgiNative.DXGI_SWAP_CHAIN_DESC1 desc,
        IntPtr restrictToOutput,
        out IntPtr swapChain);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateSwapChainForHwndProc(
        IntPtr self,
        IntPtr device,
        IntPtr hwnd,
        ref DxgiNative.DXGI_SWAP_CHAIN_DESC1 desc,
        IntPtr fullscreenDesc,
        IntPtr restrictToOutput,
        out IntPtr swapChain);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDeviceContextProc(
        IntPtr self,
        D2DNative.D2D1_DEVICE_CONTEXT_OPTIONS options,
        out IntPtr deviceContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBufferProc(IntPtr self, uint buffer, ref Guid riid, out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ResizeBuffersProc(
        IntPtr self,
        uint bufferCount,
        uint width,
        uint height,
        DxgiNative.DXGI_FORMAT format,
        uint flags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateBitmapFromDxgiSurfaceProc(
        IntPtr self,
        IntPtr dxgiSurface,
        ref D2DNative.D2D1_BITMAP_PROPERTIES1 bitmapProperties,
        out IntPtr bitmap);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetTargetProc(IntPtr self, IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetDpiProc(IntPtr self, float dpiX, float dpiY);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PresentProc(IntPtr self, uint syncInterval, uint flags);

    private const uint BufferCount = 2;

    private readonly Direct2DDevice _device;
    private readonly IntPtr _hwnd;

    // --- Native object slots (where each per-surface DirectX object lives) ---
    private readonly ComPtr _swapChain = new();     // IDXGISwapChain1
    private readonly ComPtr _d2dContext = new();    // ID2D1DeviceContext
    private readonly ComPtr _targetBitmap = new();  // ID2D1Bitmap1 (bound to the back buffer)

    private BSize _size;
    private double _dpiScale;
    private readonly BPixelFormat _pixelFormat;
    private readonly bool _enableTransparency;
    private bool _disposed;

    internal Direct2DSurface(Direct2DDevice device, BSurfaceDescriptor descriptor, IntPtr hwnd = default)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _hwnd = hwnd;
        _size = ValidateSize(descriptor.Size);
        _dpiScale = NormalizeDpiScale(descriptor.DpiScale);
        _pixelFormat = descriptor.PixelFormat;
        _enableTransparency = descriptor.EnableTransparency;

        try
        {
            CreateSwapChainResources();
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

        ReleaseSizeDependentResources();

        ResizeBuffersProc resize =
            ComVtable.Method<ResizeBuffersProc>(_swapChain.Pointer, DxgiNative.VtblResizeBuffers);
        int hr = resize(
            _swapChain.Pointer,
            BufferCount,
            PixelWidth,
            PixelHeight,
            ToDxgiFormat(_pixelFormat),
            0);
        ThrowIfDeviceLost(hr, "IDXGISwapChain1::ResizeBuffers");
        NativeMethods.ThrowIfFailed(hr, "IDXGISwapChain1::ResizeBuffers");

        CreateSizeDependentResources();
    }

    private void CreateSwapChainResources()
    {
        var desc = new DxgiNative.DXGI_SWAP_CHAIN_DESC1
        {
            Width = PixelWidth,
            Height = PixelHeight,
            Format = ToDxgiFormat(_pixelFormat),
            Stereo = 0,
            SampleDesc = new DxgiNative.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            BufferUsage = DxgiNative.DXGI_USAGE_RENDER_TARGET_OUTPUT,
            BufferCount = BufferCount,
            Scaling = DxgiNative.DXGI_SCALING.STRETCH,
            SwapEffect = DxgiNative.DXGI_SWAP_EFFECT.FLIP_SEQUENTIAL,
            AlphaMode = _enableTransparency
                ? DxgiNative.DXGI_ALPHA_MODE.PREMULTIPLIED
                : DxgiNative.DXGI_ALPHA_MODE.IGNORE,
            Flags = 0,
        };

        int hr;
        if (_hwnd == IntPtr.Zero)
        {
            CreateSwapChainForCompositionProc createSwapChain =
                ComVtable.Method<CreateSwapChainForCompositionProc>(
                    _device.DxgiFactory.Pointer,
                    DxgiNative.VtblCreateSwapChainForComposition);
            hr = createSwapChain(
                _device.DxgiFactory.Pointer,
                _device.D3DDevice.Pointer,
                ref desc,
                IntPtr.Zero,
                out IntPtr swapChain);
            NativeMethods.ThrowIfFailed(hr, "IDXGIFactory2::CreateSwapChainForComposition");
            _swapChain.Attach(swapChain);
        }
        else
        {
            CreateSwapChainForHwndProc createSwapChain =
                ComVtable.Method<CreateSwapChainForHwndProc>(
                    _device.DxgiFactory.Pointer,
                    DxgiNative.VtblCreateSwapChainForHwnd);
            hr = createSwapChain(
                _device.DxgiFactory.Pointer,
                _device.D3DDevice.Pointer,
                _hwnd,
                ref desc,
                IntPtr.Zero,
                IntPtr.Zero,
                out IntPtr swapChain);
            NativeMethods.ThrowIfFailed(hr, "IDXGIFactory2::CreateSwapChainForHwnd");
            _swapChain.Attach(swapChain);
        }

        CreateDeviceContextProc createContext =
            ComVtable.Method<CreateDeviceContextProc>(_device.D2DDevice.Pointer, D2DNative.VtblCreateDeviceContext);
        hr = createContext(
            _device.D2DDevice.Pointer,
            D2DNative.D2D1_DEVICE_CONTEXT_OPTIONS.NONE,
            out IntPtr context);
        NativeMethods.ThrowIfFailed(hr, "ID2D1Device::CreateDeviceContext");
        _d2dContext.Attach(context);

        CreateSizeDependentResources();
    }

    private void CreateSizeDependentResources()
    {
        using var backBuffer = new ComPtr();
        Guid surfaceId = DxgiNative.IID_IDXGISurface;
        GetBufferProc getBuffer = ComVtable.Method<GetBufferProc>(_swapChain.Pointer, DxgiNative.VtblGetBuffer);
        int hr = getBuffer(_swapChain.Pointer, 0, ref surfaceId, out IntPtr surface);
        NativeMethods.ThrowIfFailed(hr, "IDXGISwapChain1::GetBuffer");
        backBuffer.Attach(surface);

        float dpi = Dpi;
        var properties = new D2DNative.D2D1_BITMAP_PROPERTIES1
        {
            PixelFormat = new D2DNative.D2D1_PIXEL_FORMAT
            {
                Format = ToDxgiFormat(_pixelFormat),
                AlphaMode = _enableTransparency
                    ? D2DNative.D2D1_ALPHA_MODE.PREMULTIPLIED
                    : D2DNative.D2D1_ALPHA_MODE.IGNORE,
            },
            DpiX = dpi,
            DpiY = dpi,
            BitmapOptions = D2DNative.D2D1_BITMAP_OPTIONS.TARGET |
                            D2DNative.D2D1_BITMAP_OPTIONS.CANNOT_DRAW,
            ColorContext = IntPtr.Zero,
        };

        CreateBitmapFromDxgiSurfaceProc createBitmap =
            ComVtable.Method<CreateBitmapFromDxgiSurfaceProc>(
                _d2dContext.Pointer,
                D2DNative.VtblCreateBitmapFromDxgiSurface);
        hr = createBitmap(_d2dContext.Pointer, backBuffer.Pointer, ref properties, out IntPtr targetBitmap);
        NativeMethods.ThrowIfFailed(hr, "ID2D1DeviceContext::CreateBitmapFromDxgiSurface");
        _targetBitmap.Attach(targetBitmap);

        SetDpi(_d2dContext.Pointer, dpi);
        SetTarget(_d2dContext.Pointer, _targetBitmap.Pointer);
    }

    private void ReleaseSizeDependentResources()
    {
        if (!_d2dContext.IsNull)
            SetTarget(_d2dContext.Pointer, IntPtr.Zero);

        _targetBitmap.Release();
    }

    /// <summary>Presents the current back buffer. Maps DXGI device-removed errors to a Core exception.</summary>
    public void Present(bool vsync)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PresentProc present = ComVtable.Method<PresentProc>(_swapChain.Pointer, DxgiNative.VtblPresent);
        int hr = present(_swapChain.Pointer, vsync ? 1u : 0u, 0);
        ThrowIfDeviceLost(hr, "IDXGISwapChain1::Present");
        NativeMethods.ThrowIfFailed(hr, "IDXGISwapChain1::Present");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ReleaseNativeResources();
    }

    private void ReleaseNativeResources()
    {
        ReleaseSizeDependentResources();
        _d2dContext.Dispose();
        _swapChain.Dispose();
    }

    private uint PixelWidth => ToPixelDimension(_size.Width, _dpiScale, nameof(Size));

    private uint PixelHeight => ToPixelDimension(_size.Height, _dpiScale, nameof(Size));

    private float Dpi => (float)(96.0 * _dpiScale);

    private static void SetTarget(IntPtr context, IntPtr target)
    {
        SetTargetProc setTarget = ComVtable.Method<SetTargetProc>(context, D2DNative.VtblSetTarget);
        setTarget(context, target);
    }

    internal static void SetDpi(IntPtr context, float dpi)
    {
        SetDpiProc setDpi = ComVtable.Method<SetDpiProc>(context, D2DNative.VtblSetDpi);
        setDpi(context, dpi, dpi);
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
        if (pixels <= 0 || pixels > uint.MaxValue)
            throw new ArgumentOutOfRangeException(name, "Surface pixel dimensions are outside the supported range.");

        return (uint)pixels;
    }

    private static DxgiNative.DXGI_FORMAT ToDxgiFormat(BPixelFormat pixelFormat) => pixelFormat switch
    {
        BPixelFormat.Bgra8 => DxgiNative.DXGI_FORMAT.B8G8R8A8_UNORM,
        BPixelFormat.Rgba8 => DxgiNative.DXGI_FORMAT.R8G8B8A8_UNORM,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unsupported pixel format."),
    };

    private static void ThrowIfDeviceLost(int hr, string what)
    {
        if (hr == DxgiNative.DXGI_ERROR_DEVICE_REMOVED || hr == DxgiNative.DXGI_ERROR_DEVICE_RESET)
            throw new BDeviceLostException($"{what} failed because the graphics device was lost.", hr);
    }
}
