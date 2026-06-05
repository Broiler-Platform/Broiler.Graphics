using System;

namespace Broiler.Graphics.Windows.Native;

/// <summary>
/// Direct3D 11 enums and constants needed to create the backing device for Direct2D interop.
/// Internal-only. We only need enough to call <see cref="NativeMethods.D3D11CreateDevice"/>.
/// </summary>
internal static class D3D11Native
{
    internal enum D3D_DRIVER_TYPE : uint
    {
        UNKNOWN = 0,
        HARDWARE = 1,
        REFERENCE = 2,
        NULL = 3,
        SOFTWARE = 4,
        WARP = 5,
    }

    internal enum D3D_FEATURE_LEVEL : uint
    {
        LEVEL_9_1 = 0x9100,
        LEVEL_9_2 = 0x9200,
        LEVEL_9_3 = 0x9300,
        LEVEL_10_0 = 0xa000,
        LEVEL_10_1 = 0xa100,
        LEVEL_11_0 = 0xb000,
        LEVEL_11_1 = 0xb100,
    }

    [Flags]
    internal enum D3D11_CREATE_DEVICE_FLAG : uint
    {
        NONE = 0,
        SINGLETHREADED = 0x1,
        DEBUG = 0x2,
        BGRA_SUPPORT = 0x20, // required for Direct2D interop
    }

    /// <summary>The value to pass for the SDKVersion parameter of D3D11CreateDevice.</summary>
    internal const uint D3D11_SDK_VERSION = 7;
}
