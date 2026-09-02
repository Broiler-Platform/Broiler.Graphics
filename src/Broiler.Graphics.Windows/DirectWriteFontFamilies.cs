using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Broiler.Graphics.Windows.Native;

namespace Broiler.Graphics.Windows;

/// <summary>
/// The families in DirectWrite's system font collection, for <see cref="BSystemFonts"/>.
/// </summary>
/// <remarks>
/// <para>
/// The platform-neutral fallback reads family names out of the font files themselves, which is the
/// only thing possible without a host font service — but on Windows it names families this backend
/// cannot then draw. DirectWrite groups faces into weight-stretch-style families: "Arial Black" and
/// "Calibri Light" are not families, they are the Black face of Arial and the Light face of
/// Calibri, and <c>IDWriteFactory::CreateTextFormat</c> asked for either name finds nothing and
/// silently substitutes its default. Measured on Windows 11: of the 270 names the file scan
/// produces, around 60 resolve to Segoe UI rather than to themselves.
/// </para>
/// <para>
/// So the collection is enumerated instead. A font dialog then lists the ~180 names this backend
/// really has, and the weight the user picks alongside one selects the face inside it — which is
/// the model DirectWrite is built around anyway.
/// </para>
/// </remarks>
internal static class DirectWriteFontFamilies
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetSystemFontCollectionProc(IntPtr self, out IntPtr collection, [MarshalAs(UnmanagedType.Bool)] bool checkForUpdates);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint GetFontFamilyCountProc(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFontFamilyProc(IntPtr self, uint index, out IntPtr family);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFamilyNamesProc(IntPtr self, out IntPtr names);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int FindLocaleNameProc(
        IntPtr self,
        [MarshalAs(UnmanagedType.LPWStr)] string localeName,
        out uint index,
        [MarshalAs(UnmanagedType.Bool)] out bool exists);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetStringLengthProc(IntPtr self, uint index, out uint length);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetStringProc(IntPtr self, uint index, IntPtr buffer, uint size);

    /// <summary>
    /// Offers this backend's font list, unless the application has already chosen one. Reports
    /// whether the registration took.
    /// </summary>
    internal static bool UseIfUnset() => BSystemFonts.UseIfUnset(Enumerate);

    /// <summary>
    /// Every family in the system collection. Empty when DirectWrite is unavailable, which leaves
    /// the caller on its own fallback rather than on an exception raised while a dialog opens.
    /// </summary>
    private static IEnumerable<string> Enumerate()
    {
        var families = new List<string>();
        try
        {
            int hr = NativeMethods.DWriteCreateFactory(
                DWriteNative.DWRITE_FACTORY_TYPE.SHARED,
                DWriteNative.IID_IDWriteFactory,
                out IntPtr factoryPointer);
            if (!NativeMethods.Succeeded(hr))
                return families;

            using ComPtr factory = new(factoryPointer);
            GetSystemFontCollectionProc getCollection =
                ComVtable.Method<GetSystemFontCollectionProc>(factory.Pointer, DWriteNative.VtblGetSystemFontCollection);
            // No update check: this is a list for a picker, and asking the font cache to refresh
            // can block for as long as it takes to rebuild.
            hr = getCollection(factory.Pointer, out IntPtr collectionPointer, false);
            if (!NativeMethods.Succeeded(hr) || collectionPointer == IntPtr.Zero)
                return families;

            using ComPtr collection = new(collectionPointer);
            uint count = ComVtable
                .Method<GetFontFamilyCountProc>(collection.Pointer, DWriteNative.VtblGetFontFamilyCount)(collection.Pointer);
            GetFontFamilyProc getFamily =
                ComVtable.Method<GetFontFamilyProc>(collection.Pointer, DWriteNative.VtblGetFontFamily);

            string locale = DirectWriteText.CurrentLocaleName();
            for (uint index = 0; index < count; index++)
            {
                hr = getFamily(collection.Pointer, index, out IntPtr familyPointer);
                if (!NativeMethods.Succeeded(hr) || familyPointer == IntPtr.Zero)
                    continue;

                using ComPtr family = new(familyPointer);
                if (TryReadFamilyName(family.Pointer, locale) is { Length: > 0 } name)
                    families.Add(name);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            // A box without DirectWrite is a box that cannot run this backend at all, but a font
            // list is not the place to discover that.
            return families;
        }

        return families;
    }

    private static string? TryReadFamilyName(IntPtr family, string locale)
    {
        int hr = ComVtable.Method<GetFamilyNamesProc>(family, DWriteNative.VtblGetFamilyNames)(family, out IntPtr namesPointer);
        if (!NativeMethods.Succeeded(hr) || namesPointer == IntPtr.Zero)
            return null;

        using ComPtr names = new(namesPointer);

        // The user's own locale first, then en-us, then whatever the font lists first — the order
        // DirectWrite's own samples recommend, and the one that keeps a CJK family readable to the
        // person who installed it.
        uint index = 0;
        if (!TryFindLocale(names.Pointer, locale, out index) &&
            !TryFindLocale(names.Pointer, "en-us", out index))
        {
            index = 0;
        }

        hr = ComVtable.Method<GetStringLengthProc>(names.Pointer, DWriteNative.VtblGetStringLength)(names.Pointer, index, out uint length);
        if (!NativeMethods.Succeeded(hr) || length == 0 || length > 512)
            return null;

        // The buffer has to hold the terminating null DirectWrite writes past the reported length.
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)(length + 1) * sizeof(char)));
        try
        {
            hr = ComVtable.Method<GetStringProc>(names.Pointer, DWriteNative.VtblGetString)(names.Pointer, index, buffer, length + 1);
            return NativeMethods.Succeeded(hr) ? Marshal.PtrToStringUni(buffer, (int)length) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryFindLocale(IntPtr names, string locale, out uint index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(locale))
            return false;

        int hr = ComVtable.Method<FindLocaleNameProc>(names, DWriteNative.VtblFindLocaleName)(
            names,
            locale,
            out uint found,
            out bool exists);
        if (!NativeMethods.Succeeded(hr) || !exists)
            return false;

        index = found;
        return true;
    }
}
