// Windows SDK 10.0.26100.0 (WinUser.h, WinGdi.h, ShellScalingApi.h)
// EnumDisplayMonitors: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors
// GetMonitorInfoW: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmonitorinfow
// EnumDisplayDevicesW: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaydevicesw
// GetDpiForMonitor: https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor
// SetThreadDpiAwarenessContext: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddpiawarenesscontext

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Desktop;

/// <summary>
/// Reads the connected desktop monitors (physical pixel bounds, effective scale percentage, and primary flag)
/// through Win32 display enumeration APIs, returning them in <see cref="MonitorNumbering.Order"/> order.
/// </summary>
internal sealed unsafe partial class SystemMonitors : IMonitors
{
    internal const uint MonitorInfoPrimaryFlag = 0x00000001u;
    internal const uint DisplayDeviceAttachedToDesktop = 0x00000001u;
    internal const uint DisplayDeviceMirroringDriver = 0x00000008u;
    internal const int MdtEffectiveDpi = 0;
    internal static readonly nint DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    private readonly INativeMonitorApi _api;

    public SystemMonitors()
        : this(Win32MonitorApi.Instance)
    {
    }

    internal SystemMonitors(INativeMonitorApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    public IReadOnlyList<MonitorInfo> Read()
    {
        nint previousContext = _api.SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        if (previousContext == nint.Zero)
        {
            throw new DesktopUnavailableException("Could not enter per-monitor v2 DPI awareness context.");
        }

        try
        {
            return ReadWithTopologyRetry();
        }
        finally
        {
            _ = _api.SetThreadDpiAwarenessContext(previousContext);
        }
    }

    private IReadOnlyList<MonitorInfo> ReadWithTopologyRetry()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            IReadOnlyDictionary<string, uint> adapterStates = _api.ReadAdapterStateFlags();
            var raw = new List<MonitorInfo>();
            Exception? callbackError = null;
            bool topologyMismatch = false;

            bool enumerated = _api.EnumerateMonitors(hMonitor =>
            {
                try
                {
                    if (!_api.TryGetMonitorInfo(hMonitor, out RawMonitorInfo info, out int win32Error))
                    {
                        callbackError = new Win32Exception(win32Error);
                        return false;
                    }

                    DisplayClassification classification = ClassifyDisplay(info.DeviceName, adapterStates);
                    if (classification == DisplayClassification.MirroringPseudoMonitor)
                    {
                        return true;
                    }

                    if (classification == DisplayClassification.DetachedTopologyMismatch)
                    {
                        topologyMismatch = true;
                        return false;
                    }

                    // GetDpiForMonitor(MDT_EFFECTIVE_DPI) returns the monitor's effective DPI directly for an HMONITOR
                    // without requiring an HWND (CLI and tray startup have no window). Microsoft's remark recommends
                    // GetDpiForWindow for window-scoped DPI; here we intentionally read monitor-level scale under
                    // PerMonitorV2 awareness so bounds and scale match SPEC §14.
                    int dpiHr = _api.GetDpiForMonitor(hMonitor, MdtEffectiveDpi, out uint dpiX, out _);
                    if (dpiHr != 0)
                    {
                        callbackError = Marshal.GetExceptionForHR(dpiHr)
                            ?? new InvalidOperationException($"GetDpiForMonitor failed with HRESULT 0x{dpiHr:X8}.");
                        return false;
                    }

                    if (dpiX == 0)
                    {
                        callbackError = new InvalidOperationException("GetDpiForMonitor returned 0 DPI.");
                        return false;
                    }

                    int width = checked(info.MonitorRight - info.MonitorLeft);
                    int height = checked(info.MonitorBottom - info.MonitorTop);
                    int scale = ScaleFromDpi(dpiX);
                    bool primary = (info.Flags & MonitorInfoPrimaryFlag) != 0;

                    raw.Add(new MonitorInfo(info.MonitorLeft, info.MonitorTop, width, height, scale, primary));
                    return true;
                }
                catch (Exception ex)
                {
                    callbackError = ex;
                    return false;
                }
            }, out int enumError);

            if (topologyMismatch)
            {
                if (attempt == 0)
                {
                    continue;
                }

                throw new DesktopUnavailableException("Display topology changed while reading monitors.");
            }

            if (callbackError is not null)
            {
                throw callbackError is DesktopUnavailableException unavailable
                    ? unavailable
                    : new DesktopUnavailableException("Could not read monitor information from Windows.", callbackError);
            }

            if (!enumerated)
            {
                throw new DesktopUnavailableException(
                    "Windows failed to enumerate display monitors.",
                    new Win32Exception(enumError));
            }

            return Normalize(raw);
        }

        throw new DesktopUnavailableException("Display topology changed while reading monitors.");
    }

    internal static IReadOnlyList<MonitorInfo> Normalize(IReadOnlyList<MonitorInfo> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Count == 0)
        {
            throw new DesktopUnavailableException("Windows reported no connected desktop monitors.");
        }

        int primaryCount = 0;
        foreach (MonitorInfo monitor in raw)
        {
            if (monitor.Width <= 0 || monitor.Height <= 0)
            {
                throw new DesktopUnavailableException(
                    $"Windows reported a monitor with non-positive size ({monitor.Width}x{monitor.Height}).");
            }

            if (monitor.Scale <= 0)
            {
                throw new DesktopUnavailableException(
                    $"Windows reported a monitor with non-positive scale ({monitor.Scale}%).");
            }

            if (monitor.Primary)
            {
                primaryCount++;
            }
        }

        if (primaryCount != 1)
        {
            throw new DesktopUnavailableException(
                $"Windows reported {primaryCount} primary monitors; expected exactly 1.");
        }

        return MonitorNumbering.Order(raw);
    }

    internal static int ScaleFromDpi(uint dpi)
    {
        if (dpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI must be positive.");
        }

        return checked((int)((dpi * 100L + 48L) / 96L));
    }

    internal enum DisplayClassification
    {
        DesktopDisplay,
        MirroringPseudoMonitor,
        DetachedTopologyMismatch,
    }

    internal static DisplayClassification ClassifyDisplay(string deviceName, IReadOnlyDictionary<string, uint> adapterStates)
    {
        if (string.IsNullOrEmpty(deviceName) || !adapterStates.TryGetValue(deviceName, out uint stateFlags))
        {
            return DisplayClassification.DesktopDisplay;
        }

        if ((stateFlags & DisplayDeviceMirroringDriver) != 0)
        {
            return DisplayClassification.MirroringPseudoMonitor;
        }

        return (stateFlags & DisplayDeviceAttachedToDesktop) != 0
            ? DisplayClassification.DesktopDisplay
            : DisplayClassification.DetachedTopologyMismatch;
    }

    internal static RawMonitorInfo ToRawMonitorInfo(in MonitorInfoExW nativeInfo)
    {
        fixed (char* deviceName = nativeInfo.DeviceName)
        {
            return new RawMonitorInfo(
                nativeInfo.Monitor.Left,
                nativeInfo.Monitor.Top,
                nativeInfo.Monitor.Right,
                nativeInfo.Monitor.Bottom,
                nativeInfo.Work.Left,
                nativeInfo.Work.Top,
                nativeInfo.Work.Right,
                nativeInfo.Work.Bottom,
                nativeInfo.Flags,
                ExtractFixedString(deviceName, 32));
        }
    }

    private static string ExtractFixedString(char* buffer, int maxLength)
    {
        int length = 0;
        while (length < maxLength && buffer[length] != '\0')
        {
            length++;
        }

        return new string(buffer, 0, length);
    }

    internal readonly record struct RawMonitorInfo(
        int MonitorLeft,
        int MonitorTop,
        int MonitorRight,
        int MonitorBottom,
        int WorkLeft,
        int WorkTop,
        int WorkRight,
        int WorkBottom,
        uint Flags,
        string DeviceName);

    internal interface INativeMonitorApi
    {
        nint SetThreadDpiAwarenessContext(nint dpiContext);

        IReadOnlyDictionary<string, uint> ReadAdapterStateFlags();

        bool EnumerateMonitors(Func<nint, bool> onMonitor, out int win32Error);

        bool TryGetMonitorInfo(nint hMonitor, out RawMonitorInfo info, out int win32Error);

        int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    }

    private sealed class Win32MonitorApi : INativeMonitorApi
    {
        public static Win32MonitorApi Instance { get; } = new();

        public nint SetThreadDpiAwarenessContext(nint dpiContext) =>
            NativeMethods.SetThreadDpiAwarenessContext(dpiContext);

        public IReadOnlyDictionary<string, uint> ReadAdapterStateFlags()
        {
            var states = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            for (uint index = 0; ; index++)
            {
                var device = new DisplayDeviceW { Cb = (uint)sizeof(DisplayDeviceW) };
                if (!NativeMethods.EnumDisplayDevicesW(null, index, ref device, 0))
                {
                    break;
                }

                string name = ExtractFixedString(device.DeviceName, 32);
                if (!string.IsNullOrEmpty(name))
                {
                    states[name] = device.StateFlags;
                }
            }

            return states;
        }

        public bool EnumerateMonitors(Func<nint, bool> onMonitor, out int win32Error)
        {
            var state = new EnumCallbackState(onMonitor);
            GCHandle handle = GCHandle.Alloc(state);
            try
            {
                bool ok = NativeMethods.EnumDisplayMonitors(
                    nint.Zero,
                    nint.Zero,
                    &MonitorEnumProc,
                    GCHandle.ToIntPtr(handle));
                win32Error = ok ? 0 : Marshal.GetLastPInvokeError();
                return ok;
            }
            finally
            {
                handle.Free();
            }
        }

        public bool TryGetMonitorInfo(nint hMonitor, out RawMonitorInfo info, out int win32Error)
        {
            var nativeInfo = new MonitorInfoExW { CbSize = (uint)sizeof(MonitorInfoExW) };
            if (!NativeMethods.GetMonitorInfoW(hMonitor, ref nativeInfo))
            {
                win32Error = Marshal.GetLastPInvokeError();
                info = default;
                return false;
            }

            win32Error = 0;
            info = ToRawMonitorInfo(in nativeInfo);
            return true;
        }

        public int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY) =>
            NativeMethods.GetDpiForMonitor(hMonitor, dpiType, out dpiX, out dpiY);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int MonitorEnumProc(nint hMonitor, nint hdcMonitor, nint lprcMonitor, nint dwData)
        {
            try
            {
                var handle = GCHandle.FromIntPtr(dwData);
                if (handle.Target is not EnumCallbackState state)
                {
                    return 0;
                }

                return state.OnMonitor(hMonitor) ? 1 : 0;
            }
            catch
            {
                return 0;
            }
        }

        private sealed class EnumCallbackState(Func<nint, bool> onMonitor)
        {
            public Func<nint, bool> OnMonitor { get; } = onMonitor;
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EnumDisplayMonitors(
            nint hdc,
            nint lprcClip,
            delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int> lpfnEnum,
            nint dwData);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetMonitorInfoW(nint hMonitor, ref MonitorInfoExW lpmi);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EnumDisplayDevicesW(
            string? lpDevice,
            uint iDevNum,
            ref DisplayDeviceW lpDisplayDevice,
            uint dwFlags);

        [LibraryImport("shcore.dll")]
        internal static partial int GetDpiForMonitor(
            nint hMonitor,
            int dpiType,
            out uint dpiX,
            out uint dpiY);

        [LibraryImport("user32.dll")]
        internal static partial nint SetThreadDpiAwarenessContext(nint dpiContext);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfoExW
    {
        public uint CbSize;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        public fixed char DeviceName[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayDeviceW
    {
        public uint Cb;
        public fixed char DeviceName[32];
        public fixed char DeviceString[128];
        public uint StateFlags;
        public fixed char DeviceID[128];
        public fixed char DeviceKey[128];
    }
}
