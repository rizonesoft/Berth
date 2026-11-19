// Windows SDK 10.0.26100.0 (ExDisp.h)
// IID_IShellWindows = 85CB6900-4D95-11CF-960C-0080C7F4EE85
// Microsoft Learn: https://learn.microsoft.com/en-us/windows/win32/api/exdisp/nn-exdisp-ishellwindows

using System.Runtime.InteropServices;

namespace Berth.Desktop.Interop;

/// <summary>COM vtable declaration for <c>IShellWindows</c> (<c>InterfaceIsDual</c>; <c>FindWindowSW</c> sits at vtable slot 15).</summary>
[ComImport]
[Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IShellWindows
{
    int get_Count();

    [return: MarshalAs(UnmanagedType.IDispatch)]
    object? Item([MarshalAs(UnmanagedType.Struct)] object index);

    [return: MarshalAs(UnmanagedType.IUnknown)]
    object _NewEnum();

    void Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int plCookie);

    void RegisterPending(
        int lThreadId,
        [In, MarshalAs(UnmanagedType.Struct)] ref object? pvarloc,
        [In, MarshalAs(UnmanagedType.Struct)] ref object? pvarlocRoot,
        int swClass,
        out int plCookie);

    void Revoke(int lCookie);

    void OnNavigate(int lCookie, [In, MarshalAs(UnmanagedType.Struct)] ref object? pvarLoc);

    void OnActivated(int lCookie, [MarshalAs(UnmanagedType.VariantBool)] bool fActive);

    [PreserveSig]
    int FindWindowSW(
        [In, MarshalAs(UnmanagedType.Struct)] ref object? pvarLoc,
        [In, MarshalAs(UnmanagedType.Struct)] ref object? pvarLocRoot,
        int swClass,
        out int phwnd,
        int swfwOptions,
        [MarshalAs(UnmanagedType.IDispatch)] out object? ppdispOut);

    void OnCreated(int lCookie, [MarshalAs(UnmanagedType.IUnknown)] object punk);

    void ProcessAttachDetach([MarshalAs(UnmanagedType.VariantBool)] bool fAttach);
}
