// Windows SDK 10.0.26100.0 (ShObjIdl_core.h)
// IID_IShellView = 000214E3-0000-0000-C000-000000000046
// Microsoft Learn: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellview

using System.Runtime.InteropServices;

namespace Berth.Desktop.Interop;

/// <summary>COM vtable declaration for <c>IShellView</c> (<c>IOleWindow</c> slots flattened first).</summary>
[ComImport]
[Guid("000214E3-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellView
{
    void GetWindow(out nint phwnd);

    void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);

    [PreserveSig]
    int TranslateAccelerator(nint pmsg);

    void EnableModeless([MarshalAs(UnmanagedType.Bool)] bool fEnable);

    void UIActivate(uint uState);

    void Refresh();

    void CreateViewWindow(
        [MarshalAs(UnmanagedType.Interface)] IShellView? psvPrevious,
        nint pfs,
        [MarshalAs(UnmanagedType.Interface)] IShellBrowser? psb,
        nint prcView,
        out nint phWnd);

    void DestroyViewWindow();

    void GetCurrentInfo(nint pfs);

    void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam);

    void SaveViewState();

    void SelectItem(nint pidlItem, uint uFlags);

    void GetItemObject(
        uint uItem,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);
}
