using System;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Windows.Native;

/// <summary>
/// Minimal owning wrapper around a COM interface pointer. Calls <c>IUnknown::Release</c> through the
/// object's vtable on disposal. <b>Internal and isolated</b>: no part of this type is exposed by the
/// public backend API. This is the single place where raw COM pointers are handled.
/// </summary>
/// <remarks>
/// The IUnknown vtable layout is fixed: slot 0 = QueryInterface, slot 1 = AddRef, slot 2 = Release.
/// We read the vtable pointer (first machine word of the object) and invoke through it.
/// </remarks>
internal sealed unsafe class ComPtr : IDisposable
{
    private IntPtr _ptr;

    public ComPtr(IntPtr ptr) => _ptr = ptr;

    public ComPtr() => _ptr = IntPtr.Zero;

    /// <summary>The raw interface pointer. Do not store beyond the lifetime of this wrapper.</summary>
    public IntPtr Pointer => _ptr;

    public bool IsNull => _ptr == IntPtr.Zero;

    /// <summary>
    /// Takes ownership of a freshly created interface pointer. The caller passes the address of a
    /// local <see cref="IntPtr"/> to the native creation function, then attaches the result here.
    /// Releases any previously held pointer first.
    /// </summary>
    public void Attach(IntPtr ptr)
    {
        if (_ptr == ptr)
            return;
        if (_ptr != IntPtr.Zero)
            Release();
        _ptr = ptr;
    }

    /// <summary>Calls <c>IUnknown::AddRef</c>. Returns the new reference count.</summary>
    public uint AddRef()
    {
        if (_ptr == IntPtr.Zero)
            return 0;

        IntPtr* vtbl = *(IntPtr**)(void*)_ptr;
        var addRef = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtbl[1];
        return addRef(_ptr);
    }

    /// <summary>
    /// Calls <c>IUnknown::QueryInterface</c> for <paramref name="iid"/>. Returns the HRESULT and, on
    /// success, the requested interface pointer in <paramref name="result"/>.
    /// </summary>
    public int QueryInterface(in Guid iid, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (_ptr == IntPtr.Zero)
            return unchecked((int)0x80004003); // E_POINTER

        IntPtr* vtbl = *(IntPtr**)(void*)_ptr;
        var queryInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtbl[0];

        // Copy to stack locals so we can take stable addresses without pinning a ref parameter.
        Guid localIid = iid;
        IntPtr local;
        int hr = queryInterface(_ptr, &localIid, &local);
        result = local;
        return hr;
    }

    /// <summary>Calls <c>IUnknown::Release</c> and clears the pointer. Returns the new reference count.</summary>
    public uint Release()
    {
        if (_ptr == IntPtr.Zero)
            return 0;

        IntPtr* vtbl = *(IntPtr**)(void*)_ptr;
        var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vtbl[2];
        uint count = release(_ptr);
        _ptr = IntPtr.Zero;
        return count;
    }

    public void Dispose() => Release();
}
