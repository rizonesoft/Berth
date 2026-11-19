// Windows SDK 10.0.26100.0 (ShObjIdl_core.h)
// IID_IFolderView = CDE725B0-CCC9-4519-917E-325D72FAB4CE
// Microsoft Learn: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview

using System.Runtime.InteropServices;

namespace Berth.Desktop.Interop;

/// <summary>COM vtable declaration for <c>IFolderView</c> (vtbl slots 3 through 16).</summary>
[ComImport]
[Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView
{
    void GetCurrentViewMode(out uint pViewMode);

    void SetCurrentViewMode(uint viewMode);

    void GetFolder(in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void Item(int iItemIndex, out nint ppidl);

    void ItemCount(uint uFlags, out int pcItems);

    void Items(uint uFlags, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    void GetSelectionMarkedItem(out int piItem);

    void GetFocusedItem(out int piItem);

    void GetItemPosition(nint pidl, out NativePoint ppt);

    void GetSpacing(ref NativePoint ppt);

    void GetDefaultSpacing(out NativePoint ppt);

    [PreserveSig]
    int GetAutoArrange();

    void SelectItem(int iItem, uint dwFlags);

    void SelectAndPositionItems(
        uint cidl,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] apidl,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] NativePoint[]? apt,
        uint dwFlags);
}
