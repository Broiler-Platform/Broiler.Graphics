using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Windows.Native;

/// <summary>
/// DXGI interface IDs, enums and structures needed to create a swap chain and present frames.
/// Internal-only. Structures use <see cref="StructLayoutAttribute"/> with sequential layout so they
/// can be marshalled blittably.
/// </summary>
internal static class DxgiNative
{
    // ---- Interface IIDs --------------------------------------------------------------------------

    /// <summary>IID_IDXGIFactory1.</summary>
    internal static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    /// <summary>IID_IDXGIFactory2 (for CreateSwapChainForHwnd).</summary>
    internal static readonly Guid IID_IDXGIFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");

    /// <summary>IID_IDXGIDevice.</summary>
    internal static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    /// <summary>IID_IDXGISurface.</summary>
    internal static readonly Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

    /// <summary>IID_IDXGISwapChain1.</summary>
    internal static readonly Guid IID_IDXGISwapChain1 = new("790a45f7-0d42-4876-983a-0a55cfe6f4aa");

    // ---- Vtable slots ----------------------------------------------------------------------------

    /// <summary>IDXGISwapChain::Present.</summary>
    internal const int VtblPresent = 8;

    /// <summary>IDXGISwapChain::GetBuffer.</summary>
    internal const int VtblGetBuffer = 9;

    /// <summary>IDXGISwapChain::ResizeBuffers.</summary>
    internal const int VtblResizeBuffers = 13;

    /// <summary>IDXGIFactory2::CreateSwapChainForHwnd.</summary>
    internal const int VtblCreateSwapChainForHwnd = 15;

    /// <summary>IDXGIFactory2::CreateSwapChainForComposition.</summary>
    internal const int VtblCreateSwapChainForComposition = 24;

    // ---- Enums -----------------------------------------------------------------------------------

    internal enum DXGI_FORMAT : uint
    {
        UNKNOWN = 0,
        R8G8B8A8_UNORM = 28,
        B8G8R8A8_UNORM = 87,
    }

    internal enum DXGI_SWAP_EFFECT : uint
    {
        DISCARD = 0,
        SEQUENTIAL = 1,
        FLIP_SEQUENTIAL = 3,
        FLIP_DISCARD = 4,
    }

    internal enum DXGI_SCALING : uint
    {
        STRETCH = 0,
        NONE = 1,
        ASPECT_RATIO_STRETCH = 2,
    }

    internal enum DXGI_ALPHA_MODE : uint
    {
        UNSPECIFIED = 0,
        PREMULTIPLIED = 1,
        STRAIGHT = 2,
        IGNORE = 3,
    }

    /// <summary>Common DXGI error codes surfaced as HRESULTs.</summary>
    internal const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    internal const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);

    /// <summary>Swap-chain buffer usage flag used for render-target back buffers.</summary>
    internal const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x00000020;

    // ---- Structures ------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_SWAP_CHAIN_DESC1
    {
        public uint Width;
        public uint Height;
        public DXGI_FORMAT Format;
        public int Stereo; // BOOL
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint BufferUsage;
        public uint BufferCount;
        public DXGI_SCALING Scaling;
        public DXGI_SWAP_EFFECT SwapEffect;
        public DXGI_ALPHA_MODE AlphaMode;
        public uint Flags;
    }
}
