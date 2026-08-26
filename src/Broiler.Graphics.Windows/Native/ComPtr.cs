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
/// Calls go through <see cref="ComVtable"/>, which resolves each slot to a cached managed delegate —
/// no <c>unsafe</c> context required.
/// </remarks>
internal sealed class ComPtr : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceProc(IntPtr self, ref Guid iid, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint AddRefProc(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseProc(IntPtr self);

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

        return ComVtable.Method<AddRefProc>(_ptr, 1)(_ptr);
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

        // Copy to a mutable local so it can be passed by ref (marshalled as the [in] riid pointer).
        Guid localIid = iid;
        return ComVtable.Method<QueryInterfaceProc>(_ptr, 0)(_ptr, ref localIid, out result);
    }

    /// <summary>Calls <c>IUnknown::Release</c> and clears the pointer. Returns the new reference count.</summary>
    public uint Release()
    {
        if (_ptr == IntPtr.Zero)
            return 0;

        uint count = ComVtable.Method<ReleaseProc>(_ptr, 2)(_ptr);
        _ptr = IntPtr.Zero;
        return count;
    }

    public void Dispose() => Release();
}
