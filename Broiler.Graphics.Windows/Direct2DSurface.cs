using System;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

/// <summary>
/// A Direct2D-backed drawable surface. Owns the DXGI swap chain and the Direct2D device-context /
/// target bitmap derived from it. Public members are safe; native handles stay internal.
/// </summary>
internal sealed class Direct2DSurface : IBroilerSurface
{
    private readonly Direct2DDevice _device;

    // --- Native object slots (where each per-surface DirectX object lives) ---
    private readonly ComPtr _swapChain = new();     // IDXGISwapChain1
    private readonly ComPtr _d2dContext = new();    // ID2D1DeviceContext
    private readonly ComPtr _targetBitmap = new();  // ID2D1Bitmap1 (bound to the back buffer)

    private BSize _size;
    private double _dpiScale;
    private bool _disposed;

    internal Direct2DSurface(Direct2DDevice device, BSurfaceDescriptor descriptor)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _size = descriptor.Size;
        _dpiScale = descriptor.DpiScale <= 0 ? 1.0 : descriptor.DpiScale;

        CreateSwapChainResources(descriptor);
    }

    public BSize Size => _size;

    public double DpiScale => _dpiScale;

    internal ComPtr Context => _d2dContext;

    public void Resize(BSize size, double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _size = size;
        _dpiScale = dpiScale <= 0 ? 1.0 : dpiScale;

        // TODO: release the target bitmap, call IDXGISwapChain1::ResizeBuffers, then re-create the
        //       D2D target bitmap from the new back buffer. The swap chain object itself survives.
        ReleaseSizeDependentResources();
        CreateSizeDependentResources();
    }

    private void CreateSwapChainResources(BSurfaceDescriptor descriptor)
    {
        // TODO: build a DXGI_SWAP_CHAIN_DESC1 from `descriptor` and call
        //       IDXGIFactory2::CreateSwapChainForComposition / ForHwnd through the factory vtable,
        //       then create an ID2D1DeviceContext from _device.D2DDevice and bind the back buffer.
        //
        //   var desc = new DxgiNative.DXGI_SWAP_CHAIN_DESC1 { Width = (uint)descriptor.Size.Width, ... };
        //   ... CreateSwapChainForHwnd(...) ...; _swapChain.Attach(swapChain);
        //   ... ID2D1Device::CreateDeviceContext(...); _d2dContext.Attach(context);
        CreateSizeDependentResources();
    }

    private void CreateSizeDependentResources()
    {
        // TODO: get the back buffer (IDXGISurface) from _swapChain, create an ID2D1Bitmap1 with
        //       D2D1_BITMAP_OPTIONS_TARGET | CANNOT_DRAW, and SetTarget on the context.
    }

    private void ReleaseSizeDependentResources()
    {
        _targetBitmap.Release();
    }

    /// <summary>Presents the current back buffer. Maps DXGI device-removed errors to a Core exception.</summary>
    internal void Present(bool vsync)
    {
        // TODO: call IDXGISwapChain1::Present(vsync ? 1 : 0, 0) through the vtable and inspect HRESULT.
        //   int hr = ...Present...;
        //   if (hr == DxgiNative.DXGI_ERROR_DEVICE_REMOVED || hr == DxgiNative.DXGI_ERROR_DEVICE_RESET)
        //       throw new BDeviceLostException("Swap chain present failed.", hr);
        _ = vsync;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _targetBitmap.Dispose();
        _d2dContext.Dispose();
        _swapChain.Dispose();
    }
}
