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

    // ---- Value structures ------------------------------------------------------------------------

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
