// Windows SDK 10.0.26100.0 (ShObjIdl_core.h)
// IID_IShellBrowser = 000214E2-0000-0000-C000-000000000046
// Microsoft Learn: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellbrowser

using System.Runtime.InteropServices;

namespace Berth.Desktop.Interop;

/// <summary>
/// COM vtable declaration for <c>IShellBrowser</c> (<c>IOleWindow</c> slots flattened first; <c>QueryActiveShellView</c> at vtable slot 15).
/// </summary>
[ComImport]
[Guid("000214E2-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowser
{
    void GetWindow(out nint phwnd);

    void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);

    void InsertMenusSB(nint hmenuShared, nint lpMenuWidths);

    void SetMenuSB(nint hmenuShared, nint holemenuRes, nint hwndActiveObject);

    void RemoveMenusSB(nint hmenuShared);

    void SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string? pszStatusText);

    void EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool fEnable);

    [PreserveSig]
    int TranslateAcceleratorSB(nint pmsg, ushort wID);

    void BrowseObject(nint pidl, uint wFlags);

    void GetViewStateStream(uint grfMode, [MarshalAs(UnmanagedType.Interface)] out object? ppStrm);

    void GetControlWindow(uint id, out nint phwnd);

    void SendControlMsg(uint id, uint uMsg, nuint wParam, nint lParam, out nint pret);

    void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView ppshv);

    void OnViewWindowActive([MarshalAs(UnmanagedType.Interface)] IShellView? pshv);

    void SetToolbarItems(nint lpButtons, uint nButtons, uint uFlags);
}
