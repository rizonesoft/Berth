using System.Windows;
using System.Windows.Interop;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Window;

namespace Berth.Tests.Window;

public sealed class WindowPlacementRuleTests
{
    private static readonly MonitorInfo Primary100 = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo Secondary100 = new(1920, 0, 2560, 1440, 100, false);
    private static readonly MonitorInfo Secondary150 = new(1920, 0, 2560, 1440, 150, false);

    [Fact]
    public void ResolveAcceptsVisibleAndExact100By100BoundaryAndRejects99OverlapOffScreenDisconnectedAndTooSmall()
    {
        // Visible on primary.
        var visible = new WindowPlacement(100, 100, 980, 640);
        Assert.Equal(visible, WindowPlacementRule.Resolve(visible, [Primary100], 760, 520));

        // Exactly 100x100 overlap on right/bottom edge -> accepted.
        var exact100 = new WindowPlacement(1820, 980, 980, 640);
        Assert.Equal(exact100, WindowPlacementRule.Resolve(exact100, [Primary100], 760, 520));

        // 99x100 overlap -> rejected.
        var overlap99X = new WindowPlacement(1821, 980, 980, 640);
        Assert.Null(WindowPlacementRule.Resolve(overlap99X, [Primary100], 760, 520));

        // 100x99 overlap -> rejected.
        var overlap99Y = new WindowPlacement(1820, 981, 980, 640);
        Assert.Null(WindowPlacementRule.Resolve(overlap99Y, [Primary100], 760, 520));

        // Entirely off-screen -> rejected.
        var offScreen = new WindowPlacement(5000, 5000, 980, 640);
        Assert.Null(WindowPlacementRule.Resolve(offScreen, [Primary100], 760, 520));

        // On disconnected secondary monitor's coordinates -> rejected when only primary is connected.
        var onSecondary = new WindowPlacement(2100, 200, 980, 640);
        Assert.Equal(onSecondary, WindowPlacementRule.Resolve(onSecondary, [Primary100, Secondary100], 760, 520));
        Assert.Null(WindowPlacementRule.Resolve(onSecondary, [Primary100], 760, 520));

        // Smaller than minimum -> rejected.
        Assert.Null(WindowPlacementRule.Resolve(new WindowPlacement(100, 100, 759, 640), [Primary100], 760, 520));
        Assert.Null(WindowPlacementRule.Resolve(new WindowPlacement(100, 100, 980, 519), [Primary100], 760, 520));

        // Extreme signed coordinates that would overflow 32-bit X + Width -> handled in 64-bit without throwing.
        var extremePos = new WindowPlacement(int.MaxValue - 10, 100, 980, 640);
        Assert.Null(WindowPlacementRule.Resolve(extremePos, [Primary100], 760, 520));
        var extremeNeg = new WindowPlacement(int.MinValue + 10, 100, 980, 640);
        Assert.Null(WindowPlacementRule.Resolve(extremeNeg, [Primary100], 760, 520));
    }

    [Fact]
    public void ResolveForDipsMinimumUsesTargetMonitorScaleOnMixedDpiMonitors()
    {
        // Primary is 100%, secondary is 150%: 760x520 DIPs at 150% is 1140x780 physical px.
        // A 1000x700 placement on the 150% secondary monitor MUST be rejected even though primary is 100%.
        var tooSmallAt150 = new WindowPlacement(2100, 100, 1000, 700);
        Assert.Null(WindowPlacementRule.ResolveForDipsMinimum(tooSmallAt150, [Primary100, Secondary150], 760d, 520d));

        // 1140x780 on the 150% secondary monitor is accepted.
        var exactMinAt150 = new WindowPlacement(2100, 100, 1140, 780);
        Assert.Equal(exactMinAt150, WindowPlacementRule.ResolveForDipsMinimum(exactMinAt150, [Primary100, Secondary150], 760d, 520d));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void WindowPixelsRoundTripsPhysicalAndDipsWithoutDrift(double scale)
    {
        var originalDips = new Rect(120, 80, 980, 640);
        WindowPlacement physical = WindowPixels.ToPhysical(originalDips, scale);
        Rect roundTripped = WindowPixels.ToDips(physical, scale);
        WindowPlacement physicalAgain = WindowPixels.ToPhysical(roundTripped, scale);

        Assert.Equal(physical, physicalAgain);
        Assert.Equal(originalDips.X, roundTripped.X, precision: 6);
        Assert.Equal(originalDips.Y, roundTripped.Y, precision: 6);
        Assert.Equal(originalDips.Width, roundTripped.Width, precision: 6);
        Assert.Equal(originalDips.Height, roundTripped.Height, precision: 6);
    }

    [Fact]
    public void WorkspaceToScreenConvertsNonzeroLeftAndTopTaskbarOffsetsToScreenCoordinates()
    {
        // Simulate a monitor at (0, 0) with a 64px left taskbar and 48px top taskbar: rcWork starts at (64, 48).
        // A window whose physical GetWindowRect is (200, 180, 980, 640) has rcNormalPosition = (136, 132, 980, 640).
        var rcNormal = new WindowPlacement(136, 132, 980, 640);
        WindowPlacement screen = NativeWindowBoundsInterop.WorkspaceToScreen(
            rcNormal,
            workLeft: 64,
            workTop: 48,
            monitorLeft: 0,
            monitorTop: 0);

        Assert.Equal(new WindowPlacement(200, 180, 980, 640), screen);
    }
}

[Collection(DesktopCollectionDefinition.Name)]
public sealed class WindowPlacementTests(AppFixture appFixture)
{
    [Fact]
    public void StoredPlacementAppliesViaSetWindowPosAndNullOrOffScreenCentersOnPrimaryWorkArea()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        var log = new Log(paths, TimeProvider.System);
        var monitors = new SystemMonitors();
        IReadOnlyList<MonitorInfo> currentMonitors = monitors.Read();
        MonitorInfo primary = currentMonitors.FirstOrDefault(m => m.Primary) ?? currentMonitors[0];

        Assert.True(NativeWindowBoundsInterop.Instance.TryGetPrimaryWorkArea(out WindowPlacement primaryWork, out _));

        // 1. Stored valid placement scaled to primary monitor DPI -> GetWindowRect matches.
        double primaryScale = primary.Scale > 0 ? primary.Scale / 100.0 : 1.0;
        var stored = new WindowPlacement(
            primaryWork.X + 80,
            primaryWork.Y + 60,
            (int)Math.Round(980 * primaryScale),
            (int)Math.Round(640 * primaryScale));
        store.Save(Settings.Default with { Window = stored });

        appFixture.Invoke(() =>
        {
            var win = new MainWindow(store, monitors, log);
            try
            {
                win.Show();
                IntPtr hwnd = new WindowInteropHelper(win).Handle;
                Assert.True(NativeWindowBoundsInterop.Instance.TryGetWindowRect(hwnd, out WindowPlacement actual));
                Assert.Equal(stored, actual);
            }
            finally
            {
                win.CloseForShutdown();
            }
        });

        // 2. Null placement -> centred on primary work area.
        store.Save(Settings.Default with { Window = null });
        WindowPlacement expectedCentered = WindowPlacementRule.CenterOnPrimary(currentMonitors, primaryWork, 980d, 640d)!;

        appFixture.Invoke(() =>
        {
            var win = new MainWindow(store, monitors, log);
            try
            {
                win.Show();
                IntPtr hwnd = new WindowInteropHelper(win).Handle;
                Assert.True(NativeWindowBoundsInterop.Instance.TryGetWindowRect(hwnd, out WindowPlacement actual));
                Assert.Equal(expectedCentered, actual);
            }
            finally
            {
                win.CloseForShutdown();
            }
        });

        // 3. Off-screen placement -> rejected and centred on primary work area.
        store.Save(Settings.Default with { Window = new WindowPlacement(primary.X + 90000, primary.Y + 90000, 980, 640) });

        appFixture.Invoke(() =>
        {
            var win = new MainWindow(store, monitors, log);
            try
            {
                win.Show();
                IntPtr hwnd = new WindowInteropHelper(win).Handle;
                Assert.True(NativeWindowBoundsInterop.Instance.TryGetWindowRect(hwnd, out WindowPlacement actual));
                Assert.Equal(expectedCentered, actual);
            }
            finally
            {
                win.CloseForShutdown();
            }
        });
    }

    [Fact]
    public void HidingSavesRestoredNormalBoundsForMaximizedWindowAndPreservesRestoreAutomatically()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        var log = new Log(paths, TimeProvider.System);
        var monitors = new SystemMonitors();
        Assert.True(NativeWindowBoundsInterop.Instance.TryGetPrimaryWorkArea(out WindowPlacement primaryWork, out _));

        var initial = new WindowPlacement(primaryWork.X + 70, primaryWork.Y + 50, 1000, 660);
        store.Save(Settings.Default with { Window = initial, RestoreAutomatically = false });

        WindowPlacement observedBeforeMaximize = null!;
        appFixture.Invoke(() =>
        {
            var win = new MainWindow(store, monitors, log);
            try
            {
                win.Show();
                IntPtr hwnd = new WindowInteropHelper(win).Handle;
                Assert.True(NativeWindowBoundsInterop.Instance.TryGetWindowRect(hwnd, out observedBeforeMaximize));

                // Flip RestoreAutomatically on disk AFTER opening so hide must preserve the latest disk value.
                _ = new SettingsStore(paths).Update(s => s with { RestoreAutomatically = true });

                // Maximize then hide: settings.json must save the restored normal bounds, not maximized bounds.
                win.WindowState = WindowState.Maximized;
                win.Close();
                Assert.False(win.IsVisible);
            }
            finally
            {
                win.CloseForShutdown();
            }
        });

        SettingsLoad reloaded = new SettingsStore(paths).Load();
        Assert.Null(reloaded.Unreadable);
        Assert.True(reloaded.Value.RestoreAutomatically);
        Assert.Equal(observedBeforeMaximize, reloaded.Value.Window);
    }

    [Fact]
    public void HiddenWindowRevalidatesOnReopenAfterMonitorRemovalAndLeavesVisibleWindowAlone()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        var log = new Log(paths, TimeProvider.System);

        var primary = new MonitorInfo(0, 0, 1920, 1080, 100, true);
        var secondary = new MonitorInfo(1920, 0, 1920, 1080, 100, false);
        var fakeMonitors = new FakeMonitors(primary, secondary);
        var secondaryPlacement = new WindowPlacement(2050, 120, 980, 640);
        store.Save(Settings.Default with { Window = secondaryPlacement });

        var fakeInterop = new FakeWindowBoundsInterop
        {
            PrimaryWorkArea = new WindowPlacement(0, 0, 1920, 1040),
            PrimaryScale = 1.0,
            CurrentRect = secondaryPlacement,
            NormalRect = secondaryPlacement,
        };

        appFixture.Invoke(() =>
        {
            var win = new MainWindow(store, fakeMonitors, log, fakeInterop);
            try
            {
                win.Show();
                Assert.Equal(secondaryPlacement, fakeInterop.LastSetRect);

                // Activating while already visible must NOT rewrite bounds.
                int setsBeforeVisibleBring = fakeInterop.SetCalls;
                win.BringToFront();
                Assert.Equal(setsBeforeVisibleBring, fakeInterop.SetCalls);

                // Hide on secondary while maximized, undock (remove secondary monitor), then reopen via BringToFront().
                // Must repair NormalRect via TrySetNormalPlacement so restoring lands on primary!
                win.WindowState = WindowState.Maximized;
                win.Close();
                fakeMonitors.Monitors = [primary];
                win.BringToFront();

                WindowPlacement expectedPrimary = WindowPlacementRule.CenterOnPrimary([primary], fakeInterop.PrimaryWorkArea, 980d, 640d)!;
                Assert.Equal(WindowState.Maximized, win.WindowState);
                Assert.Equal(expectedPrimary, fakeInterop.LastSetNormalRect);
                Assert.Equal(expectedPrimary, fakeInterop.NormalRect);
            }
            finally
            {
                win.CloseForShutdown();
            }
        });

        // Also verify real Win32 SetWindowPlacement updates restored normal bounds on a live maximized window
        // so restoring to Normal lands at the expected GetWindowRect.
        Assert.True(NativeWindowBoundsInterop.Instance.TryGetPrimaryWorkArea(out WindowPlacement livePrimaryWork, out double liveScale));
        var liveExpected = new WindowPlacement(
            livePrimaryWork.X + 90,
            livePrimaryWork.Y + 75,
            (int)Math.Round(980 * liveScale),
            (int)Math.Round(640 * liveScale));
        appFixture.Invoke(() =>
        {
            var liveWin = new MainWindow();
            try
            {
                liveWin.Show();
                liveWin.WindowState = WindowState.Maximized;
                IntPtr hwnd = new WindowInteropHelper(liveWin).Handle;
                Assert.True(NativeWindowBoundsInterop.Instance.TrySetNormalPlacement(hwnd, liveExpected));
                liveWin.WindowState = WindowState.Normal;
                Assert.True(NativeWindowBoundsInterop.Instance.TryGetWindowRect(hwnd, out WindowPlacement actualRestored));
                Assert.Equal(liveExpected, actualRestored);
            }
            finally
            {
                liveWin.CloseForShutdown();
            }
        });
    }

    [Fact]
    public void FailedMonitorEnumerationUsesNativePrimaryWorkAreaAndDpiScale()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        var log = new Log(paths, TimeProvider.System);
        var unavailableMonitors = new FakeMonitors { Unavailable = true };
        var fakeInterop = new FakeWindowBoundsInterop
        {
            PrimaryWorkArea = new WindowPlacement(0, 0, 2880, 1560),
            PrimaryScale = 1.5,
        };

        appFixture.Invoke(() =>
        {
            var win = new MainWindow(store, unavailableMonitors, log, fakeInterop);
            try
            {
                win.Show();
                // 980x640 DIPs at 150% = 1470x960 px centered in (0, 0, 2880, 1560) -> (705, 300, 1470, 960).
                Assert.Equal(new WindowPlacement(705, 300, 1470, 960), fakeInterop.LastSetRect);
            }
            finally
            {
                win.CloseForShutdown();
            }
        });
    }

    [Fact]
    public void NativeFailureAndUnreadableOrFailingSettingsNeverBlockHideOrOpenAndLogWarn()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        var log = new Log(paths, TimeProvider.System);
        var monitors = new FakeMonitors(new MonitorInfo(0, 0, 1920, 1080, 100, true));

        // 1. Failed SetWindowPos still opens visible; failed TryGetNormalPlacement preserves existing setting and hides.
        var existing = new WindowPlacement(100, 100, 980, 640);
        store.Save(Settings.Default with { Window = existing });
        var failingInterop = new FakeWindowBoundsInterop
        {
            FailSetWindowRect = true,
            FailGetNormalPlacement = true,
        };

        appFixture.Invoke(() =>
        {
            var win = new MainWindow(store, monitors, log, failingInterop);
            try
            {
                win.Show();
                Assert.True(win.IsVisible);
                win.Close();
                Assert.False(win.IsVisible);
            }
            finally
            {
                win.CloseForShutdown();
            }
        });
        Assert.Equal(existing, store.Load().Value.Window);

        // 2. Unreadable `{` in settings.json inside a real running TrayApp: capture log lines immediately after Close(),
        // verify exact WARN + SPEC §18 message, exact raw bytes unchanged, and tray reopens the window cleanly.
        using var unreadableData = new TestData();
        BerthPaths unreadablePaths = BerthPaths.ForRoot(unreadableData.Root);
        var unreadableStore = new SettingsStore(unreadablePaths);
        BerthPaths.EnsureFolderFor(unreadableStore.FilePath);
        byte[] originalMalformedBytes = [(byte)'{'];
        File.WriteAllBytes(unreadableStore.FilePath, originalMalformedBytes);
        string unreadableReportFile = Path.Combine(unreadableData.Root, "unreadable-hide.txt");

        (int unreadableExit, string unreadableOut, string unreadableErr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.SettingsFailureHideReopenScenario,
            unreadableData.Root,
            $"Berth.Tests.UnreadableHide.{Guid.NewGuid():N}",
            reportFile: unreadableReportFile);

        Assert.True(unreadableExit == 0, $"Expected exit 0, got {unreadableExit}. stdout={unreadableOut} stderr={unreadableErr}");
        Dictionary<string, string> unreadableReport = File.ReadAllLines(unreadableReportFile)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        Assert.Equal("False", unreadableReport["visibleAfterHide"]);
        Assert.Equal("True", unreadableReport["visibleAfterTrayReopen"]);
        Assert.Equal(originalMalformedBytes, File.ReadAllBytes(unreadableStore.FilePath));
        string unreadableHideLines = unreadableReport["hideWarnLines"];
        Assert.Contains($" WARN {Messages.UnreadableSettings}", unreadableHideLines, StringComparison.Ordinal);

        // 3. Write failure (valid settings.json, directory blocking redo-note path) inside a real running TrayApp:
        // capture log lines immediately after Close(), verify exact SPEC §18 WriteFailed WARN prefix, and tray reopens.
        using var failData = new TestData();
        BerthPaths failPaths = BerthPaths.ForRoot(failData.Root);
        var failStore = new SettingsStore(failPaths);
        failStore.Save(Settings.Default);
        Directory.CreateDirectory(failStore.FilePath + ".berth-pending");
        string failReportFile = Path.Combine(failData.Root, "writefail-hide.txt");

        (int failExit, string failOut, string failErr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.SettingsFailureHideReopenScenario,
            failData.Root,
            $"Berth.Tests.WriteFailHide.{Guid.NewGuid():N}",
            reportFile: failReportFile);

        Assert.True(failExit == 0, $"Expected exit 0, got {failExit}. stdout={failOut} stderr={failErr}");
        Dictionary<string, string> failReport = File.ReadAllLines(failReportFile)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        Assert.Equal("False", failReport["visibleAfterHide"]);
        Assert.Equal("True", failReport["visibleAfterTrayReopen"]);
        string expectedWritePrefix = $" WARN Berth could not write {failStore.FilePath}: ";
        Assert.Contains(expectedWritePrefix, failReport["hideWarnLines"], StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionTrayFactoryPersistsMovedBoundsAndPreservesSettingsAcrossHideAndExit()
    {
        using var data = new TestData();
        string reportFile = Path.Combine(data.Root, "prod-placement.txt");
        string instanceName = $"Berth.Tests.ProdPlacement.{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.ProductionTrayPlacementScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = File.ReadAllLines(reportFile)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        Assert.Equal("0", report["exitCode"]);
        Assert.Equal("True", report["setSucceeded"]);
        Assert.Equal(report["target"], report["observed"]);
        Assert.Equal(report["observed"] + ",True", report["saved"]);
        Assert.Equal(report["observed"], report["reopenedFresh"]);
    }

    private sealed class FakeWindowBoundsInterop : IWindowBoundsInterop
    {
        public WindowPlacement? PrimaryWorkArea { get; set; }
        public double PrimaryScale { get; set; } = 1.0;
        public WindowPlacement? CurrentRect { get; set; }
        public WindowPlacement? NormalRect { get; set; }
        public WindowPlacement? LastSetRect { get; private set; }
        public WindowPlacement? LastSetNormalRect { get; private set; }
        public int SetCalls { get; private set; }
        public bool FailSetWindowRect { get; set; }
        public bool FailGetNormalPlacement { get; set; }

        public bool TryGetWindowRect(IntPtr hwnd, out WindowPlacement rect)
        {
            if (CurrentRect is not null)
            {
                rect = CurrentRect;
                return true;
            }

            rect = null!;
            return false;
        }

        public bool TryGetNormalPlacement(IntPtr hwnd, out WindowPlacement screenRect)
        {
            if (!FailGetNormalPlacement && NormalRect is not null)
            {
                screenRect = NormalRect;
                return true;
            }

            screenRect = null!;
            return false;
        }

        public bool TrySetWindowRect(IntPtr hwnd, WindowPlacement rect)
        {
            SetCalls++;
            if (FailSetWindowRect)
            {
                return false;
            }

            LastSetRect = rect;
            CurrentRect = rect;
            return true;
        }

        public bool TrySetNormalPlacement(IntPtr hwnd, WindowPlacement screenRect)
        {
            SetCalls++;
            if (FailSetWindowRect)
            {
                return false;
            }

            LastSetNormalRect = screenRect;
            NormalRect = screenRect;
            return true;
        }

        public bool TryGetPrimaryWorkArea(out WindowPlacement workArea, out double scale)
        {
            if (PrimaryWorkArea is not null)
            {
                workArea = PrimaryWorkArea;
                scale = PrimaryScale;
                return true;
            }

            workArea = null!;
            scale = 1.0;
            return false;
        }
    }
}
