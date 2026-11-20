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
    [LibraryImport("user32.dll")]
    private static partial nint SetThreadDpiAwarenessContext(nint dpiContext);

    [LibraryImport("user32.dll")]
    private static partial nint GetThreadDpiAwarenessContext();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AreDpiAwarenessContextsEqual(nint dpiContextA, nint dpiContextB);

    [Fact]
    public void LiveSystemMonitorsReadsPhysicalMonitorsInOrderWithOnePrimaryAndValidScale()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        nint perMonitorV2 = new(-4);
        nint previousContext = SetThreadDpiAwarenessContext(perMonitorV2);
        try
        {
            Assert.True(
                AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), perMonitorV2),
                "Expected exact DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (-4) on the live test thread.");

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
            [new nint(-4), api.InitialContext],
            api.DpiContextHistory);
    }

    [Fact]
    public unsafe void ToRawMonitorInfoReadsRcMonitorNotRcWork()
    {
        var nativeInfo = new SystemMonitors.MonitorInfoExW
        {
            CbSize = (uint)sizeof(SystemMonitors.MonitorInfoExW),
            Monitor = new SystemMonitors.NativeRect { Left = -1920, Top = -240, Right = 0, Bottom = 840 },
            Work = new SystemMonitors.NativeRect { Left = -1920, Top = -200, Right = -40, Bottom = 800 },
            Flags = SystemMonitors.MonitorInfoPrimaryFlag,
        };
        const string device = @"\\.\DISPLAY2";
        for (int i = 0; i < device.Length; i++)
        {
            nativeInfo.DeviceName[i] = device[i];
        }

        SystemMonitors.RawMonitorInfo raw = SystemMonitors.ToRawMonitorInfo(in nativeInfo);

        Assert.Equal(-1920, raw.MonitorLeft);
        Assert.Equal(-240, raw.MonitorTop);
        Assert.Equal(0, raw.MonitorRight);
        Assert.Equal(840, raw.MonitorBottom);
        Assert.Equal(-200, raw.WorkTop);
        Assert.Equal(-40, raw.WorkRight);
        Assert.Equal(device, raw.DeviceName);
    }

    [Fact]
    public void ReadExcludesMirroringPseudoMonitor()
    {
        nint visibleHandle = new(1);
        nint mirrorHandle = new(2);
        var api = new FakeNativeMonitorApi(
            handles: [mirrorHandle, visibleHandle],
            adapterStates: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                [@"\\.\DISPLAY1"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
                [@"\\.\DISPLAYV1"] = SystemMonitors.DisplayDeviceAttachedToDesktop | SystemMonitors.DisplayDeviceMirroringDriver,
            },
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>
            {
                [visibleHandle] = new(0, 0, 1920, 1080, 0, 0, 1920, 1040, SystemMonitors.MonitorInfoPrimaryFlag, @"\\.\DISPLAY1"),
                [mirrorHandle] = new(-1000, 0, 0, 1000, -1000, 0, 0, 1000, 0u, @"\\.\DISPLAYV1"),
            },
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>
            {
                [visibleHandle] = (0, 96u),
                [mirrorHandle] = (0, 96u),
            });

        IReadOnlyList<MonitorInfo> result = new SystemMonitors(api).Read();

        Assert.Single(result);
        Assert.Equal(new MonitorInfo(0, 0, 1920, 1080, 100, Primary: true), result[0]);
    }

    [Fact]
    public void ReadRetriesOnMidReadTopologyChangeAndThrowsOnPersistentMismatch()
    {
        nint primaryHandle = new(1);
        nint secondaryHandle = new(2);
        int adapterReads = 0;
        var transientApi = new FakeNativeMonitorApi(
            handles: [primaryHandle, secondaryHandle],
            adapterStatesProvider: () =>
            {
                adapterReads++;
                return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
                {
                    [@"\\.\DISPLAY1"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
                    [@"\\.\DISPLAY2"] = adapterReads == 1 ? 0u : SystemMonitors.DisplayDeviceAttachedToDesktop,
                };
            },
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>
            {
                [primaryHandle] = new(0, 0, 2560, 1440, 0, 0, 2560, 1400, SystemMonitors.MonitorInfoPrimaryFlag, @"\\.\DISPLAY1"),
                [secondaryHandle] = new(2560, 0, 4480, 1080, 2560, 0, 4480, 1040, 0u, @"\\.\DISPLAY2"),
            },
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>
            {
                [primaryHandle] = (0, 120u),
                [secondaryHandle] = (0, 96u),
            });

        IReadOnlyList<MonitorInfo> retried = new SystemMonitors(transientApi).Read();
        Assert.Equal(2, adapterReads);
        Assert.Equal(2, retried.Count);

        var persistentApi = new FakeNativeMonitorApi(
            handles: [primaryHandle, secondaryHandle],
            adapterStates: new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
            {
                [@"\\.\DISPLAY1"] = SystemMonitors.DisplayDeviceAttachedToDesktop,
                [@"\\.\DISPLAY2"] = 0u,
            },
            infos: new Dictionary<nint, SystemMonitors.RawMonitorInfo>
            {
                [primaryHandle] = new(0, 0, 2560, 1440, 0, 0, 2560, 1400, SystemMonitors.MonitorInfoPrimaryFlag, @"\\.\DISPLAY1"),
                [secondaryHandle] = new(2560, 0, 4480, 1080, 2560, 0, 4480, 1040, 0u, @"\\.\DISPLAY2"),
            },
            dpis: new Dictionary<nint, (int Hr, uint DpiX)>
            {
                [primaryHandle] = (0, 120u),
                [secondaryHandle] = (0, 96u),
            });

        Assert.Throws<DesktopUnavailableException>(() => new SystemMonitors(persistentApi).Read());
    }

    [Fact]
    public void ReadThrowsDesktopUnavailableExceptionWhenDpiContextSwitchFails()
    {
        nint first = new(1);
        var api = new FakeNativeMonitorApi(
            handles: [first],
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
            setDpiContextResult: nint.Zero);

        Assert.Throws<DesktopUnavailableException>(() => new SystemMonitors(api).Read());
        Assert.Empty(api.VisitedHandles);
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
        IReadOnlyDictionary<string, uint>? adapterStates = null,
        IReadOnlyDictionary<nint, SystemMonitors.RawMonitorInfo>? infos = null,
        IReadOnlyDictionary<nint, (int Hr, uint DpiX)>? dpis = null,
        int missingInfoWin32Error = 1461,
        int? enumReturnFalseError = null,
        Func<IReadOnlyDictionary<string, uint>>? adapterStatesProvider = null,
        nint? setDpiContextResult = null) : SystemMonitors.INativeMonitorApi
    {
        public nint InitialContext { get; } = new(-2);
        public nint[] Handles { get; set; } = handles;
        public List<nint> VisitedHandles { get; } = [];
        public List<nint> DpiContextHistory { get; } = [];

        public nint SetThreadDpiAwarenessContext(nint dpiContext)
        {
            DpiContextHistory.Add(dpiContext);
            return setDpiContextResult ?? InitialContext;
        }

        public IReadOnlyDictionary<string, uint> ReadAdapterStateFlags() =>
            adapterStatesProvider is not null
                ? adapterStatesProvider()
                : adapterStates ?? new Dictionary<string, uint>();

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
            if (infos is not null && infos.TryGetValue(hMonitor, out info))
            {
                win32Error = 0;
                return true;
            }

            info = default;
            win32Error = missingInfoWin32Error;
            return false;
        }

        public int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY)
        {
            Assert.Equal(SystemMonitors.MdtEffectiveDpi, dpiType);
            (int hr, uint x) = dpis![hMonitor];
            dpiX = x;
            dpiY = x;
            return hr;
        }
    }
}
