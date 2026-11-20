using System.Runtime.InteropServices;
using Berth.Core.Model;

namespace Berth.Window;

/// <summary>
/// Seam around Win32 physical-pixel window placement calls so tests can verify native failure handling, nonzero
/// taskbar work-area offsets, and restored-placement updates for non-normal windows deterministically.
/// </summary>
internal interface IWindowBoundsInterop
{
    bool TryGetWindowRect(IntPtr hwnd, out WindowPlacement rect);

    bool TryGetNormalPlacement(IntPtr hwnd, out WindowPlacement screenRect);

    bool TrySetWindowRect(IntPtr hwnd, WindowPlacement rect);

    bool TrySetNormalPlacement(IntPtr hwnd, WindowPlacement screenRect);

    bool TryGetPrimaryWorkArea(out WindowPlacement workArea, out double scale);
}

internal sealed partial class NativeWindowBoundsInterop : IWindowBoundsInterop
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int MdtEffectiveDpi = 0;

    public static NativeWindowBoundsInterop Instance { get; } = new();

    public bool TryGetWindowRect(IntPtr hwnd, out WindowPlacement rect)
    {
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out NativeRect native))
        {
            rect = FromNativeRect(native);
            return rect.Width > 0 && rect.Height > 0;
        }

        rect = null!;
        return false;
    }

    public bool TryGetNormalPlacement(IntPtr hwnd, out WindowPlacement screenRect)
    {
        if (hwnd == IntPtr.Zero)
        {
            screenRect = null!;
            return false;
        }

        var placement = new NativeWindowPlacement
        {
            Length = Marshal.SizeOf<NativeWindowPlacement>(),
        };

        if (!GetWindowPlacement(hwnd, ref placement))
        {
            screenRect = null!;
            return false;
        }

        WindowPlacement workspaceRect = FromNativeRect(placement.NormalPosition);
        if (workspaceRect.Width <= 0 || workspaceRect.Height <= 0)
        {
            screenRect = null!;
            return false;
        }

        IntPtr hMonitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (hMonitor == IntPtr.Zero)
        {
            screenRect = null!;
            return false;
        }

        var monitorInfo = new NativeMonitorInfo
        {
            Size = Marshal.SizeOf<NativeMonitorInfo>(),
        };

        if (!GetMonitorInfoW(hMonitor, ref monitorInfo))
        {
            screenRect = null!;
            return false;
        }

        screenRect = WorkspaceToScreen(
            workspaceRect,
            monitorInfo.Work.Left,
            monitorInfo.Work.Top,
            monitorInfo.Monitor.Left,
            monitorInfo.Monitor.Top);
        return true;
    }

    public bool TrySetWindowRect(IntPtr hwnd, WindowPlacement rect)
    {
        ArgumentNullException.ThrowIfNull(rect);
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        return SetWindowPos(hwnd, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height, SwpNoZOrder | SwpNoActivate);
    }

    public bool TrySetNormalPlacement(IntPtr hwnd, WindowPlacement screenRect)
    {
        ArgumentNullException.ThrowIfNull(screenRect);
        if (hwnd == IntPtr.Zero || screenRect.Width <= 0 || screenRect.Height <= 0)
        {
            return false;
        }

        var placement = new NativeWindowPlacement
        {
            Length = Marshal.SizeOf<NativeWindowPlacement>(),
        };

        if (!GetWindowPlacement(hwnd, ref placement))
        {
            return false;
        }

        var targetNative = new NativeRect
        {
            Left = screenRect.X,
            Top = screenRect.Y,
            Right = screenRect.X + screenRect.Width,
            Bottom = screenRect.Y + screenRect.Height,
        };

        IntPtr hMonitor = MonitorFromRect(ref targetNative, MonitorDefaultToNearest);
        if (hMonitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new NativeMonitorInfo
        {
            Size = Marshal.SizeOf<NativeMonitorInfo>(),
        };

        if (!GetMonitorInfoW(hMonitor, ref monitorInfo))
        {
            return false;
        }

        WindowPlacement workspaceRect = ScreenToWorkspace(
            screenRect,
            monitorInfo.Work.Left,
            monitorInfo.Work.Top,
            monitorInfo.Monitor.Left,
            monitorInfo.Monitor.Top);

        placement.NormalPosition = new NativeRect
        {
            Left = workspaceRect.X,
            Top = workspaceRect.Y,
            Right = workspaceRect.X + workspaceRect.Width,
            Bottom = workspaceRect.Y + workspaceRect.Height,
        };

        return SetWindowPlacement(hwnd, ref placement);
    }

    public bool TryGetPrimaryWorkArea(out WindowPlacement workArea, out double scale)
    {
        IntPtr hMonitor = MonitorFromPoint(new NativePoint(0, 0), MonitorDefaultToPrimary);
        if (hMonitor != IntPtr.Zero)
        {
            var monitorInfo = new NativeMonitorInfo
            {
                Size = Marshal.SizeOf<NativeMonitorInfo>(),
            };

            if (GetMonitorInfoW(hMonitor, ref monitorInfo))
            {
                workArea = FromNativeRect(monitorInfo.Work);
                if (workArea.Width > 0 && workArea.Height > 0)
                {
                    scale = GetDpiForMonitor(hMonitor, MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0
                        ? dpiX / 96.0
                        : 1.0;
                    return true;
                }
            }
        }

        workArea = null!;
        scale = 1.0;
        return false;
    }

    /// <summary>
    /// Converts Win32 <c>WINDOWPLACEMENT.rcNormalPosition</c> (workspace coordinates relative to a monitor's work area)
    /// to virtual-screen physical pixels.
    /// </summary>
    internal static WindowPlacement WorkspaceToScreen(
        WindowPlacement workspaceRect,
        int workLeft,
        int workTop,
        int monitorLeft,
        int monitorTop)
    {
        ArgumentNullException.ThrowIfNull(workspaceRect);
        int offsetX = workLeft - monitorLeft;
        int offsetY = workTop - monitorTop;
        return new WindowPlacement(
            workspaceRect.X + offsetX,
            workspaceRect.Y + offsetY,
            workspaceRect.Width,
            workspaceRect.Height);
    }

    /// <summary>
    /// Converts virtual-screen physical pixels to Win32 <c>WINDOWPLACEMENT.rcNormalPosition</c> workspace coordinates.
    /// </summary>
    internal static WindowPlacement ScreenToWorkspace(
        WindowPlacement screenRect,
        int workLeft,
        int workTop,
        int monitorLeft,
        int monitorTop)
    {
        ArgumentNullException.ThrowIfNull(screenRect);
        int offsetX = workLeft - monitorLeft;
        int offsetY = workTop - monitorTop;
        return new WindowPlacement(
            screenRect.X - offsetX,
            screenRect.Y - offsetY,
            screenRect.Width,
            screenRect.Height);
    }

    private static WindowPlacement FromNativeRect(NativeRect r) =>
        new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowPlacement(IntPtr hWnd, ref NativeWindowPlacement lpwndpl);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPlacement(IntPtr hWnd, ref NativeWindowPlacement lpwndpl);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromRect(ref NativeRect lprc, uint dwFlags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr hMonitor, ref NativeMonitorInfo lpmi);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
