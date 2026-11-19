// Windows SDK 10.0.26100.0 (ShObjIdl_core.h)
// IID_IFolderView2 = 1AF3A467-214F-4298-908E-06B03E0B39F9
// Microsoft Learn: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview2

using System.Runtime.InteropServices;

namespace Berth.Desktop.Interop;

/// <summary>
/// COM vtable declaration for <c>IFolderView2</c> (all 14 <c>IFolderView</c> slots flattened verbatim before the
/// <c>IFolderView2</c> methods through <c>SetCurrentFolderFlags</c> and <c>GetCurrentFolderFlags</c>).
/// </summary>
[ComImport]
[Guid("1AF3A467-214F-4298-908E-06B03E0B39F9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView2
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

    void SetGroupBy(nint key, [MarshalAs(UnmanagedType.Bool)] bool fAscending);

    void GetGroupBy(nint pkey, [MarshalAs(UnmanagedType.Bool)] out bool pfAscending);

    void SetViewProperty(nint pidl, nint propkey, nint propvar);

    void GetViewProperty(nint pidl, nint propkey, nint ppropvar);

    void SetTileViewProperties(nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);

    void SetExtendedTileViewProperties(nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);

    void SetText(int iType, [MarshalAs(UnmanagedType.LPWStr)] string pwszText);

    void SetCurrentFolderFlags(uint dwMask, uint dwFlags);

    void GetCurrentFolderFlags(out uint pdwFlags);
}
