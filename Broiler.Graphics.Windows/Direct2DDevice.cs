using System;
using System.Runtime.InteropServices;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

/// <summary>
/// Owns the long-lived backend GPU objects shared by all surfaces: the D3D11 device (with BGRA
/// support for D2D interop), the DXGI factory, the Direct2D factory + device, and the DirectWrite
/// factory. This is the single place native devices are created and released.
/// </summary>
/// <remarks>
/// The native handles are kept as internal <see cref="ComPtr"/> wrappers and never exposed publicly.
/// <see cref="Initialize"/> performs the full DirectX bootstrap and all COM calls are routed through
/// cached vtable delegates.
/// </remarks>
internal sealed class Direct2DDevice : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateD2DDeviceProc(IntPtr self, IntPtr dxgiDevice, out IntPtr d2dDevice);

    // --- Native object slots (where each DirectX object lives) ---
    private readonly ComPtr _d3dDevice = new();          // ID3D11Device
    private readonly ComPtr _d3dContext = new();         // ID3D11DeviceContext
    private readonly ComPtr _dxgiDevice = new();         // IDXGIDevice
    private readonly ComPtr _dxgiFactory = new();        // IDXGIFactory2
    private readonly ComPtr _d2dFactory = new();         // ID2D1Factory1
    private readonly ComPtr _d2dDevice = new();          // ID2D1Device
    private readonly ComPtr _dwriteFactory = new();      // IDWriteFactory

    private bool _initialized;
    private bool _disposed;

    internal ComPtr D2DFactory => _d2dFactory;
    internal ComPtr D2DDevice => _d2dDevice;
    internal ComPtr DxgiFactory => _dxgiFactory;
    internal ComPtr DWriteFactory => _dwriteFactory;
    internal ComPtr D3DDevice => _d3dDevice;

    /// <summary>
    /// Bootstraps the device stack: D3D11 device → DXGI device/factory → D2D factory/device →
    /// DirectWrite factory. Each step is isolated so a failure can be mapped to a clear error.
    /// </summary>
    internal void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            return;

        try
        {
            CreateD3DDevice();

            int hr = _d3dDevice.QueryInterface(DxgiNative.IID_IDXGIDevice, out IntPtr dxgiDevice);
            NativeMethods.ThrowIfFailed(hr, "ID3D11Device::QueryInterface(IDXGIDevice)");
            _dxgiDevice.Attach(dxgiDevice);

            hr = NativeMethods.CreateDXGIFactory1(DxgiNative.IID_IDXGIFactory2, out IntPtr dxgiFactory);
            NativeMethods.ThrowIfFailed(hr, "CreateDXGIFactory1(IDXGIFactory2)");
            _dxgiFactory.Attach(dxgiFactory);

            hr = NativeMethods.D2D1CreateFactory(
                D2DNative.D2D1_FACTORY_TYPE.SINGLE_THREADED,
                D2DNative.IID_ID2D1Factory1,
                IntPtr.Zero,
                out IntPtr d2dFactory);
            NativeMethods.ThrowIfFailed(hr, "D2D1CreateFactory(ID2D1Factory1)");
            _d2dFactory.Attach(d2dFactory);

            CreateD2DDeviceProc createDevice =
                ComVtable.Method<CreateD2DDeviceProc>(_d2dFactory.Pointer, D2DNative.VtblCreateDevice);
            hr = createDevice(_d2dFactory.Pointer, _dxgiDevice.Pointer, out IntPtr d2dDevice);
            NativeMethods.ThrowIfFailed(hr, "ID2D1Factory1::CreateDevice");
            _d2dDevice.Attach(d2dDevice);

            hr = NativeMethods.DWriteCreateFactory(
                DWriteNative.DWRITE_FACTORY_TYPE.SHARED,
                DWriteNative.IID_IDWriteFactory,
                out IntPtr dwrite);
            NativeMethods.ThrowIfFailed(hr, "DWriteCreateFactory(IDWriteFactory)");
            _dwriteFactory.Attach(dwrite);

            _initialized = true;
        }
        catch
        {
            ReleaseNativeObjects();
            throw;
        }
    }

    internal bool IsInitialized => _initialized;

    private void CreateD3DDevice()
    {
        const uint flags = (uint)D3D11Native.D3D11_CREATE_DEVICE_FLAG.BGRA_SUPPORT;

        int hr = NativeMethods.D3D11CreateDevice(
            IntPtr.Zero,
            D3D11Native.D3D_DRIVER_TYPE.HARDWARE,
            IntPtr.Zero,
            flags,
            IntPtr.Zero,
            0,
            D3D11Native.D3D11_SDK_VERSION,
            out IntPtr device,
            out _,
            out IntPtr context);

        if (!NativeMethods.Succeeded(hr))
        {
            hr = NativeMethods.D3D11CreateDevice(
                IntPtr.Zero,
                D3D11Native.D3D_DRIVER_TYPE.WARP,
                IntPtr.Zero,
                flags,
                IntPtr.Zero,
                0,
                D3D11Native.D3D11_SDK_VERSION,
                out device,
                out _,
                out context);
        }

        NativeMethods.ThrowIfFailed(hr, "D3D11CreateDevice");
        _d3dDevice.Attach(device);
        _d3dContext.Attach(context);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ReleaseNativeObjects();
    }

    private void ReleaseNativeObjects()
    {
        _dwriteFactory.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
        _dxgiFactory.Dispose();
        _dxgiDevice.Dispose();
        _d3dContext.Dispose();
        _d3dDevice.Dispose();
        _initialized = false;
    }
}
