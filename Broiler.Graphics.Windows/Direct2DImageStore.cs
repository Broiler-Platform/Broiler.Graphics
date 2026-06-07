using System;
using System.Collections.Generic;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

/// <summary>
/// Maps <see cref="BImageHandle"/> ids to decoded image data and their lazily-created
/// Direct2D bitmaps. Decoded pixels are kept in the Direct2D-native layout (BGRA,
/// premultiplied alpha) so the GPU upload is a straight memcpy. The actual
/// <c>ID2D1Bitmap</c> is created on first draw, from whichever device context is
/// available — bitmaps created through an <c>ID2D1DeviceContext</c> are owned by the
/// device and may be drawn by any of its contexts.
/// </summary>
internal sealed class Direct2DImageStore : IDisposable
{
    private readonly Dictionary<ulong, Direct2DImage> _entries = [];
    private ulong _nextId;
    private bool _disposed;

    /// <summary>Stores decoded pixels and returns a handle that carries the pixel size.</summary>
    public BImageHandle Add(BPixelBuffer pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ulong id = ++_nextId;
        _entries[id] = new Direct2DImage(pixels.Width, pixels.Height, ToBgraPremultiplied(pixels));
        return BImageHandle.FromId(id, new BSize(pixels.Width, pixels.Height));
    }

    /// <summary>Resolves a handle to its stored image, throwing if it is not known here.</summary>
    public Direct2DImage Get(BImageHandle image)
    {
        if (!image.IsValid)
            throw new ArgumentException("The handle does not refer to a valid image resource.", nameof(image));
        if (!_entries.TryGetValue(image.Handle.Id, out Direct2DImage? entry))
            throw new ArgumentException($"Image #{image.Handle.Id} was not created by this renderer.", nameof(image));
        return entry;
    }

    /// <summary>Releases the image's GPU bitmap and forgets it. Returns false if it was unknown.</summary>
    public bool Remove(BImageHandle image)
    {
        if (!image.IsValid || !_entries.Remove(image.Handle.Id, out Direct2DImage? entry))
            return false;
        entry.Dispose();
        return true;
    }

    public int Count => _entries.Count;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (Direct2DImage entry in _entries.Values)
            entry.Dispose();
        _entries.Clear();
    }

    /// <summary>
    /// Converts a straight-alpha RGBA buffer into Direct2D's native B8G8R8A8 with
    /// premultiplied alpha (channels scaled by alpha, byte order swapped to BGRA).
    /// </summary>
    internal static byte[] ToBgraPremultiplied(BPixelBuffer pixels)
    {
        byte[] src = pixels.Rgba;
        byte[] dst = new byte[src.Length];
        for (int i = 0; i < src.Length; i += 4)
        {
            int a = src[i + 3];
            dst[i] = (byte)((src[i + 2] * a + 127) / 255);     // B
            dst[i + 1] = (byte)((src[i + 1] * a + 127) / 255); // G
            dst[i + 2] = (byte)((src[i] * a + 127) / 255);     // R
            dst[i + 3] = (byte)a;                              // A
        }
        return dst;
    }
}

/// <summary>One stored image: its size, premultiplied BGRA pixels, and (once drawn) its D2D bitmap.</summary>
internal sealed class Direct2DImage : IDisposable
{
    private readonly ComPtr _bitmap = new(); // ID2D1Bitmap, created lazily

    public Direct2DImage(int width, int height, byte[] bgraPremultiplied)
    {
        Width = width;
        Height = height;
        BgraPremultiplied = bgraPremultiplied;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] BgraPremultiplied { get; }

    /// <summary>
    /// Returns the image's <c>ID2D1Bitmap</c>, uploading the pixels through
    /// <paramref name="deviceContext"/> (an <c>ID2D1DeviceContext</c>) on first use.
    /// </summary>
    public unsafe IntPtr EnsureBitmap(IntPtr deviceContext)
    {
        if (!_bitmap.IsNull)
            return _bitmap.Pointer;
        if (deviceContext == IntPtr.Zero)
            throw new InvalidOperationException("No Direct2D device context is available to upload the image.");

        IntPtr* vtbl = *(IntPtr**)(void*)deviceContext;
        var createBitmap = (delegate* unmanaged[Stdcall]<
            IntPtr, D2DNative.D2D1_SIZE_U, void*, uint, D2DNative.D2D1_BITMAP_PROPERTIES*, IntPtr*, int>)
            vtbl[D2DNative.VtblCreateBitmap];

        var size = new D2DNative.D2D1_SIZE_U { Width = (uint)Width, Height = (uint)Height };
        var properties = new D2DNative.D2D1_BITMAP_PROPERTIES
        {
            PixelFormat = new D2DNative.D2D1_PIXEL_FORMAT
            {
                Format = DxgiNative.DXGI_FORMAT.B8G8R8A8_UNORM,
                AlphaMode = D2DNative.D2D1_ALPHA_MODE.PREMULTIPLIED,
            },
            DpiX = 96f,
            DpiY = 96f,
        };

        IntPtr bitmap;
        fixed (byte* data = BgraPremultiplied)
        {
            int hr = createBitmap(deviceContext, size, data, (uint)(Width * 4), &properties, &bitmap);
            NativeMethods.ThrowIfFailed(hr, "ID2D1DeviceContext::CreateBitmap");
        }
        _bitmap.Attach(bitmap);
        return bitmap;
    }

    public void Dispose() => _bitmap.Dispose();
}
