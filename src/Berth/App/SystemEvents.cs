using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Berth.App;

/// <summary>
/// Hidden, unowned top-level Win32 window that receives shell broadcasts (<c>WM_DISPLAYCHANGE</c>,
/// <c>TaskbarCreated</c>, and <c>WM_SETTINGCHANGE</c>) which Windows does not deliver to message-only windows.
/// </summary>
internal sealed partial class SystemEvents : IDisposable
{
    internal const int WmSettingChange = 0x001A;
    internal const int WmDisplayChange = 0x007E;

    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreatedMessage;
    private bool _disposed;

    public SystemEvents()
        : this(registerWindowMessage: null)
    {
    }

    internal SystemEvents(Func<string, uint>? registerWindowMessage)
    {
        // Register TaskbarCreated BEFORE allocating the HWND so a registration failure throws cleanly without leaking a window.
        uint registered = registerWindowMessage is not null
            ? registerWindowMessage("TaskbarCreated")
            : RegisterWindowMessageW("TaskbarCreated");
        if (registered == 0)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Failed to register the TaskbarCreated window message.");
        }

        _taskbarCreatedMessage = registered;

        var parameters = new HwndSourceParameters("Berth.SystemEvents")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            ParentWindow = IntPtr.Zero,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow | WsExNoActivate,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        Handle = _source.Handle;
    }

    /// <summary>The underlying top-level Win32 window handle.</summary>
    public nint Handle { get; }

    /// <summary>The registered Win32 message id for <c>TaskbarCreated</c>.</summary>
    internal uint TaskbarCreatedMessage => _taskbarCreatedMessage;

    /// <summary>Raised when <c>WM_DISPLAYCHANGE</c> arrives.</summary>
    public event Action? DisplayChanged;

    /// <summary>Raised when the registered <c>TaskbarCreated</c> broadcast arrives.</summary>
    public event Action? ExplorerRestarted;

    /// <summary>Raised when <c>WM_SETTINGCHANGE</c> arrives, carrying the area string from <c>lParam</c> (or <see langword="null"/>).</summary>
    public event Action<string?>? SettingChanged;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed)
        {
            return IntPtr.Zero;
        }

        if (msg == WmDisplayChange)
        {
            DisplayChanged?.Invoke();
        }
        else if (unchecked((uint)msg) == _taskbarCreatedMessage)
        {
            ExplorerRestarted?.Invoke();
        }
        else if (msg == WmSettingChange)
        {
            string? area = lParam != IntPtr.Zero ? Marshal.PtrToStringUni(lParam) : null;
            SettingChanged?.Invoke(area);
        }

        return IntPtr.Zero;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string lpString);
}
