using System.Runtime.InteropServices;
using Berth.Core.Model;

namespace Berth.Window;

/// <summary>
/// Seam around Win32 physical-pixel window placement calls so tests can verify native failure handling and nonzero
/// taskbar work-area offsets deterministically.
/// </summary>
internal interface IWindowBoundsInterop
{
    bool TryGetWindowRect(IntPtr hwnd, out WindowPlacement rect);

    bool TryGetNormalPlacement(IntPtr hwnd, out WindowPlacement screenRect);

    bool TrySetWindowRect(IntPtr hwnd, WindowPlacement rect);

    bool TryGetPrimaryWorkArea(out WindowPlacement workArea);
}

internal sealed partial class NativeWindowBoundsInterop : IWindowBoundsInterop
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const uint MonitorDefaultToNearest = 0x00000002;

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

    public bool TryGetPrimaryWorkArea(out WindowPlacement workArea)
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
                return workArea.Width > 0 && workArea.Height > 0;
            }
        }

        workArea = null!;
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

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr hMonitor, ref NativeMonitorInfo lpmi);

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
