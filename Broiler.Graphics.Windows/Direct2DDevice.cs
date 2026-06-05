using System;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

/// <summary>
/// Owns the long-lived backend GPU objects shared by all surfaces: the D3D11 device (with BGRA
/// support for D2D interop), the DXGI factory, the Direct2D factory + device, and the DirectWrite
/// factory. This is the single place native devices are created and released.
/// </summary>
/// <remarks>
/// The native handles are kept as internal <see cref="ComPtr"/> wrappers and never exposed publicly.
/// <see cref="Initialize"/> is where the bootstrap sequence lives; the COM-vtable call sites are
/// stubbed with explicit TODOs so they can be filled in incrementally.
/// </remarks>
internal sealed class Direct2DDevice : IDisposable
{
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

    /// <summary>
    /// Bootstraps the device stack: D3D11 device → DXGI device/factory → D2D factory/device →
    /// DirectWrite factory. Each step is isolated so a failure can be mapped to a clear error.
    /// </summary>
    internal void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            return;

        // Step 1: create the D3D11 device with BGRA support (required for Direct2D interop).
        // TODO: call NativeMethods.D3D11CreateDevice with D3D11_CREATE_DEVICE_FLAG.BGRA_SUPPORT,
        //       a feature-level array, and HARDWARE driver type; Attach results to _d3dDevice /_d3dContext.
        //
        //   int hr = NativeMethods.D3D11CreateDevice(IntPtr.Zero, D3D11Native.D3D_DRIVER_TYPE.HARDWARE,
        //       IntPtr.Zero, (uint)D3D11Native.D3D11_CREATE_DEVICE_FLAG.BGRA_SUPPORT,
        //       IntPtr.Zero, 0, D3D11Native.D3D11_SDK_VERSION,
        //       out IntPtr device, out _, out IntPtr context);
        //   NativeMethods.ThrowIfFailed(hr, "D3D11CreateDevice");
        //   _d3dDevice.Attach(device);
        //   _d3dContext.Attach(context);

        // Step 2: QueryInterface the D3D device for IDXGIDevice, then create the DXGI factory.
        // TODO: _d3dDevice.QueryInterface(DxgiNative.IID_IDXGIDevice, out IntPtr dxgiDevice);
        //       NativeMethods.CreateDXGIFactory1(DxgiNative.IID_IDXGIFactory2, out IntPtr factory);

        // Step 3: create the Direct2D factory and a D2D device bound to the DXGI device.
        // TODO: NativeMethods.D2D1CreateFactory(D2DNative.D2D1_FACTORY_TYPE.SINGLE_THREADED,
        //           D2DNative.IID_ID2D1Factory1, IntPtr.Zero, out IntPtr d2dFactory);
        //       then call ID2D1Factory1::CreateDevice(dxgiDevice, &d2dDevice) through its vtable.

        // Step 4: create the DirectWrite factory.
        // TODO: NativeMethods.DWriteCreateFactory(DWriteNative.DWRITE_FACTORY_TYPE.SHARED,
        //           DWriteNative.IID_IDWriteFactory, out IntPtr dwrite);
        //       _dwriteFactory.Attach(dwrite);

        _initialized = true;
    }

    internal bool IsInitialized => _initialized;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Release in reverse creation order.
        _dwriteFactory.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
        _dxgiFactory.Dispose();
        _dxgiDevice.Dispose();
        _d3dContext.Dispose();
        _d3dDevice.Dispose();
    }
}
