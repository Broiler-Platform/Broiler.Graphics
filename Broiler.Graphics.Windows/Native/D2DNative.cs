using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Windows.Native;

/// <summary>
/// Direct2D interface IDs, enums and value structures. Internal-only.
/// The structures mirror the native D2D1 layout so they can be passed blittably to COM methods once
/// the vtable call sites are filled in.
/// </summary>
internal static class D2DNative
{
    // ---- Interface IIDs --------------------------------------------------------------------------

    /// <summary>IID_ID2D1Factory.</summary>
    internal static readonly Guid IID_ID2D1Factory = new("06152247-6f50-465a-9245-118bfd3b6007");

    /// <summary>IID_ID2D1Factory1 (device-based API).</summary>
    internal static readonly Guid IID_ID2D1Factory1 = new("bb12d362-daee-4b9a-aa1d-14ba401cfa1f");

    /// <summary>IID_ID2D1Device.</summary>
    internal static readonly Guid IID_ID2D1Device = new("47dd575d-ac05-4cdd-8049-9b02cd16f44c");

    /// <summary>IID_ID2D1DeviceContext.</summary>
    internal static readonly Guid IID_ID2D1DeviceContext = new("e8f7fe7a-191c-466d-ad95-975678bda998");

    // ---- Vtable slots ----------------------------------------------------------------------------
    // ID2D1DeviceContext inherits ID2D1RenderTarget, which inherits ID2D1Resource (GetFactory) and
    // IUnknown. Slots: 0-2 IUnknown, 3 GetFactory, then the render-target methods begin at 4.

    /// <summary>ID2D1RenderTarget::CreateBitmap (first render-target method).</summary>
    internal const int VtblCreateBitmap = 4;

    /// <summary>ID2D1RenderTarget::CreateSolidColorBrush.</summary>
    internal const int VtblCreateSolidColorBrush = 8;

    /// <summary>ID2D1RenderTarget::DrawRectangle.</summary>
    internal const int VtblDrawRectangle = 16;

    /// <summary>ID2D1RenderTarget::FillRectangle.</summary>
    internal const int VtblFillRectangle = 17;

    /// <summary>ID2D1RenderTarget::DrawBitmap.</summary>
    internal const int VtblDrawBitmap = 26;

    /// <summary>ID2D1RenderTarget::DrawText.</summary>
    internal const int VtblDrawText = 27;

    /// <summary>ID2D1RenderTarget::SetTransform.</summary>
    internal const int VtblSetTransform = 30;

    /// <summary>ID2D1RenderTarget::SetAntialiasMode.</summary>
    internal const int VtblSetAntialiasMode = 32;

    /// <summary>ID2D1RenderTarget::SetTextAntialiasMode.</summary>
    internal const int VtblSetTextAntialiasMode = 34;

    /// <summary>ID2D1RenderTarget::PushAxisAlignedClip.</summary>
    internal const int VtblPushAxisAlignedClip = 45;

    /// <summary>ID2D1RenderTarget::PopAxisAlignedClip.</summary>
    internal const int VtblPopAxisAlignedClip = 46;

    /// <summary>ID2D1RenderTarget::Clear.</summary>
    internal const int VtblClear = 47;

    /// <summary>ID2D1RenderTarget::BeginDraw.</summary>
    internal const int VtblBeginDraw = 48;

    /// <summary>ID2D1RenderTarget::EndDraw.</summary>
    internal const int VtblEndDraw = 49;

    /// <summary>ID2D1RenderTarget::SetDpi.</summary>
    internal const int VtblSetDpi = 51;

    /// <summary>ID2D1DeviceContext::CreateBitmapFromDxgiSurface.</summary>
    internal const int VtblCreateBitmapFromDxgiSurface = 62;

    /// <summary>ID2D1DeviceContext::SetTarget.</summary>
    internal const int VtblSetTarget = 74;

    /// <summary>ID2D1Device::CreateDeviceContext.</summary>
    internal const int VtblCreateDeviceContext = 4;

    /// <summary>ID2D1Factory1::CreateDevice.</summary>
    internal const int VtblCreateDevice = 17;

    /// <summary>Direct2D signals this from EndDraw when target resources must be recreated.</summary>
    internal const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);

    // ---- Enums -----------------------------------------------------------------------------------

    internal enum D2D1_FACTORY_TYPE : uint
    {
        SINGLE_THREADED = 0,
        MULTI_THREADED = 1,
    }

    internal enum D2D1_ALPHA_MODE : uint
    {
        UNKNOWN = 0,
        PREMULTIPLIED = 1,
        STRAIGHT = 2,
        IGNORE = 3,
    }

    internal enum D2D1_ANTIALIAS_MODE : uint
    {
        PER_PRIMITIVE = 0,
        ALIASED = 1,
    }

    internal enum D2D1_TEXT_ANTIALIAS_MODE : uint
    {
        DEFAULT = 0,
        CLEARTYPE = 1,
        GRAYSCALE = 2,
        ALIASED = 3,
    }

    [Flags]
    internal enum D2D1_DRAW_TEXT_OPTIONS : uint
    {
        NONE = 0,
        CLIP = 0x00000002,
    }

    internal enum D2D1_BITMAP_INTERPOLATION_MODE : uint
    {
        NEAREST_NEIGHBOR = 0,
        LINEAR = 1,
    }

    [Flags]
    internal enum D2D1_BITMAP_OPTIONS : uint
    {
        NONE = 0,
        TARGET = 0x00000001,
        CANNOT_DRAW = 0x00000002,
        CPU_READ = 0x00000004,
        GDI_COMPATIBLE = 0x00000008,
    }

    [Flags]
    internal enum D2D1_DEVICE_CONTEXT_OPTIONS : uint
    {
        NONE = 0,
        ENABLE_MULTITHREADED_OPTIMIZATIONS = 1,
    }

    // ---- Value structures ------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct D2D1_SIZE_U
    {
        public uint Width;
        public uint Height;
    }

    /// <summary>A DXGI format paired with how its alpha channel is interpreted.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct D2D1_PIXEL_FORMAT
    {
        public DxgiNative.DXGI_FORMAT Format;
        public D2D1_ALPHA_MODE AlphaMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D2D1_BITMAP_PROPERTIES
    {
        public D2D1_PIXEL_FORMAT PixelFormat;
        public float DpiX;
        public float DpiY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D2D1_BITMAP_PROPERTIES1
    {
        public D2D1_PIXEL_FORMAT PixelFormat;
        public float DpiX;
        public float DpiY;
        public D2D1_BITMAP_OPTIONS BitmapOptions;
        public IntPtr ColorContext;
    }

    /// <summary>Direct2D uses 32-bit floats and premultiplied colors at the GPU level.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct D2D1_COLOR_F
    {
        public float R;
        public float G;
        public float B;
        public float A;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D2D1_POINT_2F
    {
        public float X;
        public float Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D2D1_RECT_F
    {
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;
    }

    /// <summary>Direct2D's 3x2 transform (row-major, translation in the last row).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct D2D1_MATRIX_3X2_F
    {
        public float M11;
        public float M12;
        public float M21;
        public float M22;
        public float Dx;
        public float Dy;
    }
}
