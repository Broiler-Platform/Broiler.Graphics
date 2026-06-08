using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Windows.Native;

/// <summary>
/// P/Invoke entry points for the DirectX factory-creation functions. These are the only DLL exports
/// the backend needs to bootstrap; every other call goes through COM vtables (see <see cref="ComPtr"/>).
/// All members are <c>internal</c>. <see cref="LibraryImport"/> is used for AOT/trimming friendliness.
/// </summary>
internal static partial class NativeMethods
{
    // ---- d3d11.dll -------------------------------------------------------------------------------

    /// <summary>
    /// Creates a Direct3D 11 device used as the backing device for Direct2D interop.
    /// The backend passes a null adapter and feature-level list so the runtime chooses the default
    /// hardware/WARP capabilities for the installed Direct3D runtime.
    /// </summary>
    [LibraryImport("d3d11.dll")]
    internal static partial int D3D11CreateDevice(
        IntPtr pAdapter,
        D3D11Native.D3D_DRIVER_TYPE driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out D3D11Native.D3D_FEATURE_LEVEL pFeatureLevel,
        out IntPtr ppImmediateContext);

    // ---- dxgi.dll --------------------------------------------------------------------------------

    /// <summary>Creates a DXGI 1.1 factory. <paramref name="riid"/> is typically IID_IDXGIFactory1.</summary>
    [LibraryImport("dxgi.dll")]
    internal static partial int CreateDXGIFactory1(
        in Guid riid,
        out IntPtr ppFactory);

    // ---- d2d1.dll --------------------------------------------------------------------------------

    /// <summary>
    /// Creates a Direct2D factory. The options blob is optional (pass <see cref="IntPtr.Zero"/>).
    /// </summary>
    [LibraryImport("d2d1.dll")]
    internal static partial int D2D1CreateFactory(
        D2DNative.D2D1_FACTORY_TYPE factoryType,
        in Guid riid,
        IntPtr pFactoryOptions,
        out IntPtr ppIFactory);

    // ---- dwrite.dll ------------------------------------------------------------------------------

    /// <summary>Creates a DirectWrite factory. <paramref name="iid"/> is IID_IDWriteFactory.</summary>
    [LibraryImport("dwrite.dll")]
    internal static partial int DWriteCreateFactory(
        DWriteNative.DWRITE_FACTORY_TYPE factoryType,
        in Guid iid,
        out IntPtr factory);

    /// <summary>Returns <c>true</c> for a successful HRESULT (S_OK and other non-negative codes).</summary>
    internal static bool Succeeded(int hr) => hr >= 0;

    /// <summary>Throws a <see cref="MarshalDirectiveException"/>-free wrapper if the HRESULT is a failure.</summary>
    internal static void ThrowIfFailed(int hr, string what)
    {
        if (hr < 0)
            throw new InvalidOperationException($"{what} failed with HRESULT 0x{hr:X8}.");
    }
}
