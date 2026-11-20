using System.ComponentModel;
using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;

namespace Berth.Tests.Desktop;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed partial class SystemMonitorsTests
{
    private const int DpiAwarenessPerMonitorAware = 2;

    [LibraryImport("user32.dll")]
    private static partial nint SetThreadDpiAwarenessContext(nint dpiContext);

    [LibraryImport("user32.dll")]
    private static partial nint GetThreadDpiAwarenessContext();

    [LibraryImport("user32.dll")]
    private static partial int GetAwarenessFromDpiAwarenessContext(nint value);

    [Fact]
    public void LiveSystemMonitorsReadsPhysicalMonitorsInOrderWithOnePrimaryAndValidScale()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        nint previousContext = SetThreadDpiAwarenessContext(SystemMonitors.DpiAwarenessContextPerMonitorAwareV2);
        try
        {
            int awareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
            Assert.Equal(DpiAwarenessPerMonitorAware, awareness);

            var monitors = new SystemMonitors();
            Assert.IsAssignableFrom<IMonitors>(monitors);
            IReadOnlyList<MonitorInfo> result = monitors.Read();

            Assert.NotEmpty(result);
            Assert.Equal(1, result.Count(m => m.Primary));
            foreach (MonitorInfo monitor in result)
            {
                Assert.True(monitor.Width > 0, $"Expected positive monitor width, got {monitor.Width}.");
                Assert.True(monitor.Height > 0, $"Expected positive monitor height, got {monitor.Height}.");
                Assert.InRange(monitor.Scale, 100, 500);
            }

            Assert.Equal(MonitorNumbering.Order(result), result);
        }
        finally
        {
            if (previousContext != nint.Zero)
            {
                _ = SetThreadDpiAwarenessContext(previousContext);
            }
        }
    }
}

public sealed class UnitSystemMonitorsTests
{
    [Theory]
    [InlineData(96u, 100)]
    [InlineData(120u, 125)]
    [InlineData(144u, 150)]
    [InlineData(168u, 175)]
    [InlineData(192u, 200)]
    [InlineData(240u, 250)]
    [InlineData(480u, 500)]
    [InlineData(108u, 113)]
    public void ScaleFromDpiRoundsToNearestWholePercentage(uint dpi, int expectedScale)
    {
        Assert.Equal(expectedScale, SystemMonitors.ScaleFromDpi(dpi));
    }

    [Fact]
    public void ReadThroughNativeSeamReturnsExactMultiMonitorFieldsUsingMonitorBoundsNotWorkArea()
    {
        nint primaryHandle = new(101);
        nint leftHandle = new(202);
        var api = new FakeNativeMonitorApi(
            handles: [primaryHandle, leftHandle],
            adapterStates: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                [@"\\.\DISPLAY1"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
                [@"\\.\DISPLAY2"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
            },
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>
            {
                [primaryHandle] = new(
                    MonitorLeft: 0, MonitorTop: 0, MonitorRight: 2560, MonitorBottom: 1440,
                    WorkLeft: 0, WorkTop: 0, WorkRight: 2560, WorkBottom: 1392,
                    Flags: SystemMonitors.MonitorInfoPrimaryFlag,
                    DeviceName: @"\\.\DISPLAY1"),
                [leftHandle] = new(
                    MonitorLeft: -1920, MonitorTop: -240, MonitorRight: 0, MonitorBottom: 840,
                    WorkLeft: -1920, WorkTop: -200, WorkRight: -40, WorkBottom: 800,
                    Flags: 0u,
                    DeviceName: @"\\.\DISPLAY2"),
            },
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>
            {
                [primaryHandle] = (0, 120u),
                [leftHandle] = (0, 144u),
            });

        var monitors = new SystemMonitors(api);
        Assert.IsAssignableFrom<IMonitors>(monitors);
        IReadOnlyList<MonitorInfo> result = monitors.Read();

        Assert.Equal(2, result.Count);
        Assert.Equal(new MonitorInfo(-1920, -240, 1920, 1080, 150, Primary: false), result[0]);
        Assert.Equal(new MonitorInfo(0, 0, 2560, 1440, 125, Primary: true), result[1]);
        Assert.Equal(
            [SystemMonitors.DpiAwarenessContextPerMonitorAwareV2, api.InitialContext],
            api.DpiContextHistory);
    }

    [Fact]
    public void ReadExcludesMirroringAndDetachedPseudoMonitors()
    {
        nint visibleHandle = new(1);
        nint mirrorHandle = new(2);
        nint detachedHandle = new(3);
        var api = new FakeNativeMonitorApi(
            handles: [mirrorHandle, visibleHandle, detachedHandle],
            adapterStates: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                [@"\\.\DISPLAY1"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
                [@"\\.\DISPLAYV1"] = SystemMonitors.DisplayDeviceAttachedToDesktop | SystemMonitors.DisplayDeviceMirroringDriver,
                [@"\\.\DISPLAY3"] = 0u,
            },
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>
            {
                [visibleHandle] = new(0, 0, 1920, 1080, 0, 0, 1920, 1040, SystemMonitors.MonitorInfoPrimaryFlag, @"\\.\DISPLAY1"),
                [mirrorHandle] = new(-1000, 0, 0, 1000, -1000, 0, 0, 1000, 0u, @"\\.\DISPLAYV1"),
                [detachedHandle] = new(1920, 0, 3840, 1080, 1920, 0, 3840, 1080, 0u, @"\\.\DISPLAY3"),
            },
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>
            {
                [visibleHandle] = (0, 96u),
                [mirrorHandle] = (0, 96u),
                [detachedHandle] = (0, 96u),
            });

        IReadOnlyList<MonitorInfo> result = new SystemMonitors(api).Read();

        Assert.Single(result);
        Assert.Equal(new MonitorInfo(0, 0, 1920, 1080, 100, Primary: true), result[0]);
    }

    [Fact]
    public void ReadDoesNotCacheResultsAcrossDisplayChanges()
    {
        nint first = new(1);
        nint second = new(2);
        var api = new FakeNativeMonitorApi(
            handles: [first, second],
            adapterStates: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                [@"\\.\DISPLAY1"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
                [@"\\.\DISPLAY2"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
            },
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>
            {
                [first] = new(0, 0, 2560, 1440, 0, 0, 2560, 1400, SystemMonitors.MonitorInfoPrimaryFlag, @"\\.\DISPLAY1"),
                [second] = new(2560, 0, 4480, 1080, 2560, 0, 4480, 1040, 0u, @"\\.\DISPLAY2"),
            },
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>
            {
                [first] = (0, 120u),
                [second] = (0, 96u),
            });

        var monitors = new SystemMonitors(api);
        IReadOnlyList<MonitorInfo> docked = monitors.Read();
        Assert.Equal(2, docked.Count);

        api.Handles = [first];
        IReadOnlyList<MonitorInfo> undocked = monitors.Read();
        Assert.Single(undocked);
        Assert.Equal(new MonitorInfo(0, 0, 2560, 1440, 125, Primary: true), undocked[0]);
    }

    [Fact]
    public void NormalizeSortsByXThenYAndRejectsInvalidArrangements()
    {
        MonitorInfo[] unsorted =
        [
            new(0, 1080, 1920, 1080, 100, Primary: false),
            new(0, 0, 2560, 1440, 125, Primary: true),
            new(-1920, 0, 1920, 1080, 100, Primary: false),
        ];

        IReadOnlyList<MonitorInfo> ordered = SystemMonitors.Normalize(unsorted);
        Assert.Equal([unsorted[2], unsorted[1], unsorted[0]], ordered);

        Assert.Throws<DesktopUnavailableException>(() => SystemMonitors.Normalize([]));
        Assert.Throws<DesktopUnavailableException>(() => SystemMonitors.Normalize([new(0, 0, 1920, 1080, 100, Primary: false)]));
        Assert.Throws<DesktopUnavailableException>(() => SystemMonitors.Normalize([
            new(0, 0, 1920, 1080, 100, Primary: true),
            new(1920, 0, 1920, 1080, 100, Primary: true),
        ]));
        Assert.Throws<DesktopUnavailableException>(() => SystemMonitors.Normalize([new(0, 0, 0, 1080, 100, Primary: true)]));
        Assert.Throws<DesktopUnavailableException>(() => SystemMonitors.Normalize([new(0, 0, 1920, 1080, 0, Primary: true)]));
    }

    [Fact]
    public void SecondMonitorFailureStopsEnumerationAndPreservesCauseWithoutPartialList()
    {
        nint first = new(1);
        nint second = new(2);
        nint third = new(3);
        const int errorGenFailure = 31;
        var api = new FakeNativeMonitorApi(
            handles: [first, second, third],
            adapterStates: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                [@"\\.\DISPLAY1"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
            },
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>
            {
                [first] = new(0, 0, 1920, 1080, 0, 0, 1920, 1040, SystemMonitors.MonitorInfoPrimaryFlag, @"\\.\DISPLAY1"),
            },
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>
            {
                [first] = (0, 96u),
            },
            missingInfoWin32Error: errorGenFailure);

        var monitors = new SystemMonitors(api);
        DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() => monitors.Read());

        Win32Exception inner = Assert.IsType<Win32Exception>(thrown.InnerException);
        Assert.Equal(errorGenFailure, inner.NativeErrorCode);
        Assert.Equal(2, api.VisitedHandles.Count);
        Assert.Equal([first, second], api.VisitedHandles);
    }

    [Fact]
    public void SecondMonitorDpiFailureStopsEnumerationAndPreservesHResultError()
    {
        nint first = new(1);
        nint second = new(2);
        const int eInvalidArg = unchecked((int)0x80070057);
        var api = new FakeNativeMonitorApi(
            handles: [first, second],
            adapterStates: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                [@"\\.\DISPLAY1"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
                [@"\\.\DISPLAY2"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
            },
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>
            {
                [first] = new(0, 0, 1920, 1080, 0, 0, 1920, 1040, SystemMonitors.MonitorInfoPrimaryFlag, @"\\.\DISPLAY1"),
                [second] = new(1920, 0, 3840, 1080, 1920, 0, 3840, 1040, 0u, @"\\.\DISPLAY2"),
            },
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>
            {
                [first] = (0, 96u),
                [second] = (eInvalidArg, 0u),
            });

        DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() => new SystemMonitors(api).Read());

        Assert.IsType<ArgumentException>(thrown.InnerException);
    }

    [Fact]
    public void ZeroDpiOrEnumFailureThrowsDesktopUnavailableException()
    {
        nint first = new(1);
        var zeroDpiApi = new FakeNativeMonitorApi(
            handles: [first],
            adapterStates: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase),
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>
            {
                [first] = new(0, 0, 1920, 1080, 0, 0, 1920, 1040, SystemMonitors.MonitorInfoPrimaryFlag, @"\\.\DISPLAY1"),
            },
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>
            {
                [first] = (0, 0u),
            });

        Assert.Throws<DesktopUnavailableException>(() => new SystemMonitors(zeroDpiApi).Read());

        var enumFailApi = new FakeNativeMonitorApi(
            handles: [],
            adapterStates: new Dictionary<string, uint>(),
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>(),
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>(),
            enumReturnFalseError: 5);

        DesktopUnavailableException enumThrown = Assert.Throws<DesktopUnavailableException>(() => new SystemMonitors(enumFailApi).Read());
        Win32Exception enumInner = Assert.IsType<Win32Exception>(enumThrown.InnerException);
        Assert.Equal(5, enumInner.NativeErrorCode);
    }

    private sealed class FakeNativeMonitorApi(
        nint[] handles,
        IReadOnlyDictionary<string, uint> adapterStates,
        IReadOnlyDictionary<nint, SystemMonitors.RawMonitorInfo> infos,
        IReadOnlyDictionary<nint, (int Hr, uint DpiX)> dpis,
        int missingInfoWin32Error = 1461,
        int? enumReturnFalseError = null) : SystemMonitors.INativeMonitorApi
    {
        public nint InitialContext { get; } = new(-2);
        public nint[] Handles { get; set; } = handles;
        public List<nint> VisitedHandles { get; } = [];
        public List<nint> DpiContextHistory { get; } = [];

        public nint SetThreadDpiAwarenessContext(nint dpiContext)
        {
            DpiContextHistory.Add(dpiContext);
            return InitialContext;
        }

        public IReadOnlyDictionary<string, uint> ReadAdapterStateFlags() => adapterStates;

        public bool EnumerateMonitors(Func<nint, bool> onMonitor, out int win32Error)
        {
            if (enumReturnFalseError is int err)
            {
                win32Error = err;
                return false;
            }

            foreach (nint handle in Handles)
            {
                VisitedHandles.Add(handle);
                if (!onMonitor(handle))
                {
                    win32Error = 0;
                    return false;
                }
            }

            win32Error = 0;
            return true;
        }

        public bool TryGetMonitorInfo(nint hMonitor, out SystemMonitors.RawMonitorInfo info, out int win32Error)
        {
            if (infos.TryGetValue(hMonitor, out info))
            {
                win32Error = 0;
                return true;
            }

            win32Error = missingInfoWin32Error;
            return false;
        }

        public int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY)
        {
            Assert.Equal(SystemMonitors.MdtEffectiveDpi, dpiType);
            (int hr, uint x) = dpis[hMonitor];
            dpiX = x;
            dpiY = x;
            return hr;
        }
    }
}
