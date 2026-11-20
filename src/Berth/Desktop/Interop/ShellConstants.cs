// Windows SDK 10.0.26100.0 (WinError.h, ExDisp.h, ShlGuid.h, ShlObj_core.h, ShObjIdl_core.h)

namespace Berth.Desktop.Interop;

/// <summary>Documented COM CLSIDs, SIDs, HRESULTs, and shell flags used to query and position desktop icons.</summary>
internal static class ShellConstants
{
    /// <summary>WinError.h / Win32 Common HRESULT Values: operation completed successfully.</summary>
    public const int S_OK = 0;

    /// <summary>WinError.h / IShellWindows::FindWindowSW: completed successfully, or matching window not found.</summary>
    public const int S_FALSE = 1;

    /// <summary>ExDisp.h (ShellWindows coclass): https://learn.microsoft.com/en-us/windows/win32/api/exdisp/nn-exdisp-shellwindows.</summary>
    public static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    /// <summary>ShlGuid.h (IShellBrowser top-level browser service): https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellbrowser.</summary>
    public static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    /// <summary>ShlObj_core.h (<c>CSIDL</c>): https://learn.microsoft.com/en-us/windows/win32/shell/csidl.</summary>
    public const int CSIDL_DESKTOP = 0x0000;

    /// <summary>ExDisp.h (<c>ShellWindowTypeConstants</c>): https://learn.microsoft.com/en-us/windows/win32/api/exdisp/ne-exdisp-shellwindowtypeconstants.</summary>
    public const int SWC_DESKTOP = 0x00000008;

    /// <summary>ExDisp.h (<c>ShellWindowFindWindowOptions</c>): https://learn.microsoft.com/en-us/windows/win32/api/exdisp/ne-exdisp-shellwindowfindwindowoptions.</summary>
    public const int SWFO_NEEDDISPATCH = 0x00000001;

    /// <summary>ShObjIdl_core.h (<c>_SVGIO</c>): https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio.</summary>
    public const uint SVGIO_SELECTION = 0x00000001u;

    /// <summary>ShObjIdl_core.h (<c>_SVGIO</c>): https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio.</summary>
    public const uint SVGIO_ALLVIEW = 0x00000002u;

    /// <summary>ShObjIdl_core.h (<c>_SVSIF</c>): https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svsif.</summary>
    public const uint SVSI_POSITIONITEM = 0x00000080u;

    /// <summary>ShObjIdl_core.h (<c>_SVSIF</c>): https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svsif.</summary>
    public const uint SVSI_NOSTATECHANGE = 0x80000000u;

    /// <summary>ShObjIdl_core.h (<c>_SHGDNF</c>): https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_shgdnf.</summary>
    public const uint SHGDN_NORMAL = 0x0000u;

    /// <summary>ShObjIdl_core.h (<c>_SHGDNF</c>): https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_shgdnf.</summary>
    public const uint SHGDN_FORPARSING = 0x8000u;

    /// <summary>ShObjIdl_core.h (<c>FOLDERFLAGS</c>): https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderflags.</summary>
    public const uint FWF_AUTOARRANGE = 0x00000001u;
}
