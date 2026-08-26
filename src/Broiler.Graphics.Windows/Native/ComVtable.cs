using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Broiler.Graphics.Windows.Native;

/// <summary>
/// Safe (no <c>unsafe</c> context) access to COM interface methods through an object's vtable.
/// A COM object's first machine word points at its vtable; the entry at <c>slot</c> — counting
/// <c>IUnknown</c>'s QueryInterface/AddRef/Release as 0/1/2 — is the function pointer for a method.
/// </summary>
/// <remarks>
/// Function pointers are turned into managed delegates with
/// <see cref="Marshal.GetDelegateForFunctionPointer{TDelegate}(IntPtr)"/> and cached by pointer value.
/// Every object of a given COM class shares one vtable, so a method's pointer is stable: the delegate is
/// created once and reused for all instances. The per-call cost is two <see cref="Marshal.ReadIntPtr(IntPtr)"/>
/// reads (JIT intrinsics, no allocation) plus a concurrent-dictionary lookup — the closest fully-managed
/// equivalent of the previous <c>delegate* unmanaged</c> call sites.
/// </remarks>
internal static class ComVtable
{
    // Keyed by function pointer and delegate type. Some COM implementations reuse a function pointer
    // for methods with the same ABI signature, while call sites still ask for distinct delegate types.
    private static readonly ConcurrentDictionary<(IntPtr Function, Type DelegateType), Delegate> Delegates = new();

    /// <summary>
    /// Returns the method at <paramref name="slot"/> in <paramref name="comObject"/>'s vtable as a
    /// <typeparamref name="TDelegate"/>. <paramref name="comObject"/> must be non-null.
    /// </summary>
    internal static TDelegate Method<TDelegate>(IntPtr comObject, int slot)
        where TDelegate : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        IntPtr function = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return (TDelegate)Delegates.GetOrAdd(
            (function, typeof(TDelegate)),
            static key => Marshal.GetDelegateForFunctionPointer<TDelegate>(key.Function));
    }
}
