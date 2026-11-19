// Windows SDK 10.0.26100.0 (ShObjIdl_core.h)
// IID_IShellFolder = 000214E6-0000-0000-C000-000000000046
// Microsoft Learn: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellfolder

using System.Runtime.InteropServices;

namespace Berth.Desktop.Interop;

/// <summary>COM vtable declaration for <c>IShellFolder</c> (vtbl slots 3 through 12; <c>GetDisplayNameOf</c> sits at vtable slot 11).</summary>
[ComImport]
[Guid("000214E6-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    void ParseDisplayName(
        nint hwnd,
        nint pbc,
        [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
        nint pchEaten,
        out nint ppidl,
        nint pdwAttributes);

    [PreserveSig]
    int EnumObjects(
        nint hwnd,
        uint grfFlags,
        [MarshalAs(UnmanagedType.Interface)] out IEnumIDList? ppenumIDList);

    void BindToObject(
        nint pidl,
        nint pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void BindToStorage(
        nint pidl,
        nint pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [PreserveSig]
    int CompareIDs(nint lParam, nint pidl1, nint pidl2);

    void CreateViewObject(
        nint hwndOwner,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetAttributesOf(
        uint cidl,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[]? apidl,
        ref uint rgfInOut);

    void GetUIObjectOf(
        nint hwndOwner,
        uint cidl,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] apidl,
        in Guid riid,
        nint rgfReserved,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetDisplayNameOf(nint pidl, uint uFlags, out StrRet pName);

    void SetNameOf(
        nint hwnd,
        nint pidl,
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        uint uFlags,
        out nint ppidlOut);
}
