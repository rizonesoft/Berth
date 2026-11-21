using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Berth.App;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using Microsoft.Win32;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class SettingsTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task HostedSwitchesToggleOnAndOffPersistToDiskAndIsolatedRegistryAndSyncToTrayMenu()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);

        string testKeyPath = $@"Software\Berth.Tests\{Guid.NewGuid():N}";
        string testExePath = @"C:\Program Files\Berth\berth.exe";
        var runValue = new RunValue(testKeyPath, "Berth", testExePath);

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        MainWindow? window = null;
        TrayMenu? trayMenu = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w);
                SettingsPanel panel = w.LayoutsPaneControl.SettingsPanelControl;

                Assert.Equal($"Berth {AppInfo.Version}", panel.VersionCaptionText.Text);
                Assert.Equal("Restore automatically", AutomationProperties.GetName(panel.RestoreAutomaticallySwitch));
                Assert.Equal("Start with Windows", AutomationProperties.GetName(panel.StartWithWindowsSwitch));
                Assert.True(panel.RestoreAutomaticallySwitch.ActualHeight >= 32d);
                Assert.True(panel.StartWithWindowsSwitch.ActualHeight >= 32d);

                // SPEC §11: no "On" or "Off" text inside SettingsPanel.
                TextBlock[] allText = [.. FindVisualChildren<TextBlock>(panel)];
                Assert.DoesNotContain(allText, tb => tb.Text is "On" or "Off");

                Assert.False(panel.RestoreAutomaticallySwitch.IsChecked);
                Assert.False(panel.StartWithWindowsSwitch.IsChecked);

                // 1. Toggle Restore automatically ON via UI Automation.
                ToggleSwitch(panel.RestoreAutomaticallySwitch);
                return w;
            });

            await vm.Settings!.LastRestoreTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(settingsStore.Load().Value.RestoreAutomatically);
            Assert.True(appFixture.Invoke(() => window!.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch.IsChecked == true));

            // Tray menu reads the persisted automatic-restore choice when opened!
            trayMenu = appFixture.Invoke(() =>
            {
                var menu = new TrayMenu(
                    service,
                    layoutStore,
                    monitors,
                    desktop,
                    settingsStore,
                    log,
                    (action, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(action));
                menu.RefreshState();
                return menu;
            });
            await trayMenu!.LastRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(appFixture.Invoke(() => ((System.Windows.Forms.ToolStripMenuItem)trayMenu.Strip.Items[2]).Checked));

            // 2. Toggle Restore automatically OFF via UI Automation.
            appFixture.Invoke(() => ToggleSwitch(window!.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch));
            await vm.Settings.LastRestoreTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(settingsStore.Load().Value.RestoreAutomatically);

            // 3. Toggle Start with Windows ON via UI Automation -> isolated registry key holds quoted path!
            appFixture.Invoke(() => ToggleSwitch(window!.LayoutsPaneControl.SettingsPanelControl.StartWithWindowsSwitch));
            await vm.Settings.LastStartTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(runValue.IsOn);
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(testKeyPath, writable: false))
            {
                Assert.NotNull(key);
                Assert.Equal($"\"{testExePath}\"", key!.GetValue("Berth"));
            }

            // 4. Reopen a fresh MainViewModel -> reads persisted StartWithWindows = true from registry!
            var reopened = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
            await reopened.RefreshAsync();
            Assert.False(reopened.Settings!.RestoreAutomatically);
            Assert.True(reopened.Settings.StartWithWindows);

            // 5. Toggle Start with Windows OFF via UI Automation -> value removed!
            appFixture.Invoke(() => ToggleSwitch(window!.LayoutsPaneControl.SettingsPanelControl.StartWithWindowsSwitch));
            await vm.Settings.LastStartTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(runValue.IsOn);

            string[] logLines = File.ReadAllLines(paths.LogFile);
            Assert.Single(logLines, l => l.Contains("INFO Restore automatically turned on."));
            Assert.Single(logLines, l => l.Contains("INFO Restore automatically turned off."));
            Assert.Single(logLines, l => l.Contains("INFO Start with Windows turned on."));
            Assert.Single(logLines, l => l.Contains("INFO Start with Windows turned off."));
        }
        finally
        {
            if (window is not null)
            {
                appFixture.Invoke(() => window.CloseForShutdown());
            }

            if (trayMenu is not null)
            {
                appFixture.Invoke(() => trayMenu.Dispose());
            }

            Registry.CurrentUser.DeleteSubKeyTree(testKeyPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task FailedOnToOffWritesSnapHostedSwitchesBackLogSingleFailAndDeliverWhenBusyClears()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(Settings.Default with { RestoreAutomatically = true });

        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        var fakeRegistry = new FakeRunRegistry { StoredValue = "\"C:\\Berth\\berth.exe\"" };
        var runValue = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", fakeRegistry);

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        Assert.True(vm.Settings!.RestoreAutomatically);
        Assert.True(vm.Settings.StartWithWindows);

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w);
                Assert.True(w.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch.IsChecked);
                Assert.True(w.LayoutsPaneControl.SettingsPanelControl.StartWithWindowsSwitch.IsChecked);
                return w;
            });

            // 1. Corrupt settings.json, then try On -> Off through hosted switch -> snaps back to On, bytes untouched, 1 FAIL!
            byte[] corruptBytes = "{ corrupt json"u8.ToArray();
            File.WriteAllBytes(paths.SettingsFile, corruptBytes);
            appFixture.Invoke(() => ToggleSwitch(window!.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch));
            await vm.Settings.LastRestoreTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(vm.Settings.RestoreAutomatically);
            Assert.True(appFixture.Invoke(() => window!.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch.IsChecked == true));
            Assert.Equal(corruptBytes, File.ReadAllBytes(paths.SettingsFile));
            Assert.Equal(Messages.UnreadableSettings, vm.Status.Sentence);
            Assert.Single(File.ReadAllLines(paths.LogFile), l => l.Contains("FAIL") && l.Contains(Messages.UnreadableSettings));

            // 2. Deny registry access on On -> Off while status line is busy ("Working") -> snaps back to On, delivers §18 error once idle!
            fakeRegistry.ThrowOnWriteOrDelete = new UnauthorizedAccessException("Access is denied.");
            appFixture.Invoke(() => vm.Detail.SetExternalBusy(true));
            Assert.Equal("Working", vm.Status.Sentence);

            appFixture.Invoke(() => ToggleSwitch(window!.LayoutsPaneControl.SettingsPanelControl.StartWithWindowsSwitch));
            await vm.Settings.LastStartTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.True(vm.Settings.StartWithWindows);
            Assert.True(appFixture.Invoke(() => window!.LayoutsPaneControl.SettingsPanelControl.StartWithWindowsSwitch.IsChecked == true));
            Assert.Equal("Working", vm.Status.Sentence);

            // Clearing busy delivers the queued §18 failure message!
            appFixture.Invoke(() => vm.Detail.SetExternalBusy(false));
            Assert.Equal("Berth could not change the Start with Windows setting: Access is denied.", vm.Status.Sentence);
            Assert.Single(File.ReadAllLines(paths.LogFile), l => l.Contains("FAIL") && l.Contains("Access is denied."));

            // 3. Real window operation completing (SaveAsync -> OperationCompleted) must NOT overwrite a queued §18 failure!
            var gatedDesktop = new GatedDesktopIcons(desktop);
            LayoutService gatedService = CreateService(paths, layoutStore, gatedDesktop, monitors, log);
            var opVm = CreateViewModel(layoutStore, gatedDesktop, monitors, gatedService, settingsStore, runValue, changes, log);
            await opVm.RefreshAsync();
            Assert.True(opVm.Settings!.StartWithWindows);

            gatedDesktop.ArmGate();
            Task<OperationResult?> saveTask = opVm.Detail.SaveAsync();
            await gatedDesktop.EnteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal("Working", opVm.Status.Sentence);

            opVm.Settings.StartWithWindows = false;
            await opVm.Settings.LastStartTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(opVm.Settings.StartWithWindows);
            Assert.Equal("Working", opVm.Status.Sentence);

            gatedDesktop.ReleaseGate();
            await saveTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal("Berth could not change the Start with Windows setting: Access is denied.", opVm.Status.Sentence);

            // 4. Denied registry read after a failed disable must NOT overwrite On with Off on the next refresh!
            fakeRegistry.ThrowOnRead = new UnauthorizedAccessException("Access is denied.");
            await opVm.RefreshAsync();
            Assert.True(opVm.Settings.StartWithWindows);
        }
        finally
        {
            if (window is not null)
            {
                appFixture.Invoke(() => window.CloseForShutdown());
            }
        }
    }

    [Fact]
    public async Task ExternalChangesRefreshWithoutWritingAndStalePollDoesNotOverwriteUserWrite()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        var fakeRegistry = new FakeRunRegistry();
        var runValue = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", fakeRegistry);

        var monitors = new FakeMonitors(MonSingle);
        var innerDesktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        var desktop = new GatedDesktopIcons(innerDesktop);
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        Assert.False(vm.Settings!.RestoreAutomatically);
        Assert.False(vm.Settings.StartWithWindows);

        // 1. External edits to settings.json and registry update switches on RefreshAsync with ZERO log lines written!
        settingsStore.Save(Settings.Default with { RestoreAutomatically = true });
        fakeRegistry.StoredValue = "\"C:\\Berth\\berth.exe\"";
        await vm.RefreshAsync();

        Assert.True(vm.Settings.RestoreAutomatically);
        Assert.True(vm.Settings.StartWithWindows);
        Assert.False(File.Exists(paths.LogFile));

        // 2. Stale poll: poll captures RestoreAutomatically = true, then user turns it OFF, then poll finishes -> stays OFF!
        desktop.ArmGate();
        Task stalePoll = vm.RefreshAsync();
        await desktop.EnteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        vm.Settings.RestoreAutomatically = false;
        await vm.Settings.LastRestoreTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(vm.Settings.RestoreAutomatically);

        desktop.ReleaseGate();
        await stalePoll.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(vm.Settings.RestoreAutomatically);

        // 3. Poll started DURING an in-flight write (captures post-entry epoch, reads Off before write persists On)
        // must be discarded after the write completes!
        var gatedRegistry = new FakeRunRegistry();
        gatedRegistry.ArmWriteGate();
        var gatedRun = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", gatedRegistry);
        var midWriteVm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, gatedRun, changes, log);
        await midWriteVm.RefreshAsync();
        Assert.False(midWriteVm.Settings!.StartWithWindows);

        midWriteVm.Settings.StartWithWindows = true;
        await gatedRegistry.EnteredWriteGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Capture epochs WHILE the write is in flight (after entry increment, before persistence)!
        (long midRestoreEpoch, long midStartEpoch) = midWriteVm.Settings.CaptureEpochs();
        (_, bool? midReadStart) = midWriteVm.Settings.ReadSources();
        Assert.False(midReadStart);

        gatedRegistry.ReleaseWriteGate();
        await midWriteVm.Settings.LastStartTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(midWriteVm.Settings.StartWithWindows);

        // Delivering that mid-write poll observation now must NOT undo the verified On state!
        midWriteVm.Settings.ApplyRefreshed(null, midRestoreEpoch, midReadStart, midStartEpoch);
        Assert.True(midWriteVm.Settings.StartWithWindows);

        // 4. Two rapid denied toggles (Off -> On -> Off) must roll back to last confirmed Off, never unconfirmed On!
        var rapidRegistry = new FakeRunRegistry { ThrowOnWriteOrDelete = new UnauthorizedAccessException("Access is denied.") };
        rapidRegistry.ArmWriteGate();
        var rapidRun = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", rapidRegistry);
        var rapidVm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, rapidRun, changes, log);
        await rapidVm.RefreshAsync();
        Assert.False(rapidVm.Settings!.StartWithWindows);

        rapidVm.Settings.StartWithWindows = true;
        await rapidRegistry.EnteredWriteGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        rapidVm.Settings.StartWithWindows = false;
        rapidRegistry.ReleaseWriteGate();
        await rapidVm.Settings.LastStartTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(rapidVm.Settings.StartWithWindows);

        // 5. Failed restore write that discovers an external On value via read-back retains On as rollback baseline
        // for a subsequent failed toggle when settings.json becomes unreadable (before any refresh)!
        using var data2 = new TestData();
        BerthPaths paths2 = BerthPaths.ForRoot(data2.Root);
        var layoutStore2 = new LayoutStore(paths2);
        var settingsStore2 = new SettingsStore(paths2);
        var log2 = new Log(paths2, new FixedClock(FixedNow));
        var changes2 = new SettingChanges(settingsStore2, log2);
        var vm2 = CreateViewModel(layoutStore2, innerDesktop, monitors, service, settingsStore2, runValue, changes2, log2);
        await vm2.RefreshAsync();
        Assert.False(vm2.Settings!.RestoreAutomatically);

        // External writer saves On, then locks settings.json so a write fails while read-back discovers On!
        settingsStore2.Save(Settings.Default with { RestoreAutomatically = true });
        using (var lockStream = new FileStream(paths2.SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            vm2.Settings.RestoreAutomatically = true;
            await vm2.Settings.LastRestoreTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(vm2.Settings.RestoreAutomatically);
        }

        // Now corrupt settings.json and toggle Off before any refresh -> must snap back to discovered On!
        File.WriteAllBytes(paths2.SettingsFile, "{ corrupt json"u8.ToArray());
        vm2.Settings.RestoreAutomatically = false;
        await vm2.Settings.LastRestoreTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(vm2.Settings.RestoreAutomatically);
    }

    [Fact]
    [Trait("Category", "Render")]
    public async Task RendersBothSwitchStatesInLightAndDarkAndVerifiesOppositeThumbPositionsAndHighContrast150()
    {
        string outputDir = ResolveRenderOutputDirectory();
        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonSingle]),
            "Laptop",
            new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero),
            [MonSingle],
            [new IconPosition("id-1", "Notes.txt", 400, 300)],
            null);

        await RenderSwitchStateAsync("switches-on-off", outputDir, layout, restoreAuto: true, startWithWindows: false);
        await RenderSwitchStateAsync("switches-off-on", outputDir, layout, restoreAuto: false, startWithWindows: true);
    }

    private async Task RenderSwitchStateAsync(
        string name,
        string outputDir,
        Layout layout,
        bool restoreAuto,
        bool startWithWindows)
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        layoutStore.Save(layout);
        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(Settings.Default with { RestoreAutomatically = restoreAuto });
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        var fakeRegistry = new FakeRunRegistry
        {
            StoredValue = startWithWindows ? "\"C:\\Berth\\berth.exe\"" : null,
        };
        var runValue = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", fakeRegistry);

        var monitors = new FakeMonitors([.. layout.Monitors]);
        var desktop = new FakeDesktopIcons([.. layout.Icons]);
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        string lightPath = Path.Combine(outputDir, $"{name}-light.png");
        string darkPath = Path.Combine(outputDir, $"{name}-dark.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                MeasureAndArrange(window);
                SettingsPanel panel = window.LayoutsPaneControl.SettingsPanelControl;

                // Verify visible switch track and opposite thumb positions (On knob is further right than Off knob).
                Border restoreKnob = (Border)panel.RestoreAutomaticallySwitch.Template.FindName("Knob", panel.RestoreAutomaticallySwitch);
                Border startKnob = (Border)panel.StartWithWindowsSwitch.Template.FindName("Knob", panel.StartWithWindowsSwitch);
                double restoreKnobX = restoreKnob.TransformToAncestor(panel.RestoreAutomaticallySwitch).Transform(new System.Windows.Point(0, 0)).X;
                double startKnobX = startKnob.TransformToAncestor(panel.StartWithWindowsSwitch).Transform(new System.Windows.Point(0, 0)).X;
                if (restoreAuto && !startWithWindows)
                {
                    Assert.True(restoreKnobX > startKnobX);
                }
                else if (!restoreAuto && startWithWindows)
                {
                    Assert.True(startKnobX > restoreKnobX);
                }

                WindowRenderer.Render(window.RootGrid, 980, 640, lightPath, RenderTheme.Light);
                WindowRenderer.Render(window.RootGrid, 980, 640, darkPath, RenderTheme.Dark);

                // High Contrast & 150% DPI layout check.
                var berthApp = (BerthApplication)System.Windows.Application.Current;
                ColorTheme saved = berthApp.CurrentColorTheme;
                try
                {
                    berthApp.ApplyColorTheme(ColorTheme.HighContrast);
                    VisualTreeHelper.SetRootDpi(window.RootGrid, new DpiScale(1.5, 1.5));
                    MeasureAndArrange(window, 760, 520);
                    Assert.True(panel.RestoreAutomaticallySwitch.ActualHeight >= 32d);
                    Assert.True(panel.StartWithWindowsSwitch.ActualHeight >= 32d);
                    Assert.True(panel.VersionCaptionText.ActualHeight > 0d);
                    Assert.True(panel.RestoreAutomaticallySwitch.Focusable && panel.RestoreAutomaticallySwitch.IsTabStop);
                    Assert.True(panel.StartWithWindowsSwitch.Focusable && panel.StartWithWindowsSwitch.IsTabStop);
                    Border hcTrack = (Border)panel.RestoreAutomaticallySwitch.Template.FindName("Track", panel.RestoreAutomaticallySwitch);
                    Border hcKnob = (Border)panel.RestoreAutomaticallySwitch.Template.FindName("Knob", panel.RestoreAutomaticallySwitch);
                    Assert.NotNull(hcTrack.BorderBrush);
                    Assert.NotNull(hcKnob.Background);
                    Assert.NotNull(panel.VersionCaptionText.Foreground);
                }
                finally
                {
                    berthApp.ApplyColorTheme(saved);
                }
            }
            finally
            {
                window.CloseForShutdown();
            }
        });

        AssertValidPng(lightPath);
        AssertValidPng(darkPath);
    }

    private static LayoutService CreateService(BerthPaths paths, LayoutStore store, IDesktopIcons desktop, IMonitors monitors, Log log) =>
        new(
            desktop,
            monitors,
            store,
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(5)),
            log,
            new FixedClock(FixedNow));

    private MainViewModel CreateViewModel(
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors,
        LayoutService service,
        SettingsStore settingsStore,
        RunValue runValue,
        SettingChanges settingChanges,
        Log log) =>
        new(
            store,
            desktop,
            monitors,
            new FixedClock(FixedNow),
            marshalToUiAsync: action =>
            {
                appFixture.Invoke(action);
                return Task.CompletedTask;
            },
            service: service,
            timeZone: TimeZoneInfo.Utc,
            log: log,
            settingsStore: settingsStore,
            runValue: runValue,
            settingChanges: settingChanges);

    private static void ToggleSwitch(ToggleButton toggle)
    {
        var peer = new ToggleButtonAutomationPeer(toggle);
        IToggleProvider provider = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
        provider.Toggle();
        toggle.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
    }

    private static void MeasureAndArrange(MainWindow window, double width = 980, double height = 640)
    {
        window.RootGrid.Measure(new WpfSize(width, height));
        window.RootGrid.Arrange(new Rect(0, 0, width, height));
        window.RootGrid.UpdateLayout();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string ResolveRenderOutputDirectory()
    {
        string? envDir = Environment.GetEnvironmentVariable("BERTH_RENDER_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            Directory.CreateDirectory(envDir);
            return envDir;
        }

        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Berth.slnx")))
            {
                string outDir = Path.Combine(dir, "build", "renders");
                Directory.CreateDirectory(outDir);
                return outDir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Berth.slnx above " + AppContext.BaseDirectory);
    }

    private static void AssertValidPng(string path)
    {
        Assert.True(File.Exists(path), $"Expected render output at {path}");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0);
        using var ms = new MemoryStream(bytes);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Single(decoder.Frames);
        Assert.Equal(980, decoder.Frames[0].PixelWidth);
        Assert.Equal(640, decoder.Frames[0].PixelHeight);
    }

    private sealed class FakeRunRegistry : IRunRegistry
    {
        private TaskCompletionSource? _pendingWriteGate;
        private TaskCompletionSource? _activeWriteGate;

        public string? StoredValue { get; set; }

        public Exception? ThrowOnRead { get; set; }

        public Exception? ThrowOnWriteOrDelete { get; set; }

        public TaskCompletionSource EnteredWriteGate { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ArmWriteGate()
        {
            EnteredWriteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeWriteGate = tcs;
            _pendingWriteGate = tcs;
        }

        public void ReleaseWriteGate() => _activeWriteGate?.TrySetResult();

        public bool TryReadValue(string keyPath, string valueName, out string? value, out RegistryValueKind kind)
        {
            if (ThrowOnRead is not null)
            {
                throw ThrowOnRead;
            }

            if (StoredValue is null)
            {
                value = null;
                kind = RegistryValueKind.Unknown;
                return false;
            }

            value = StoredValue;
            kind = RegistryValueKind.String;
            return true;
        }

        public void WriteStringValue(string keyPath, string valueName, string value)
        {
            TaskCompletionSource? gate = Interlocked.Exchange(ref _pendingWriteGate, null);
            if (gate is not null)
            {
                EnteredWriteGate.TrySetResult();
                gate.Task.GetAwaiter().GetResult();
            }

            if (ThrowOnWriteOrDelete is not null)
            {
                throw ThrowOnWriteOrDelete;
            }

            StoredValue = value;
        }

        public void DeleteValue(string keyPath, string valueName)
        {
            if (ThrowOnWriteOrDelete is not null)
            {
                throw ThrowOnWriteOrDelete;
            }

            StoredValue = null;
        }
    }

    private sealed class GatedDesktopIcons(FakeDesktopIcons inner) : IDesktopIcons
    {
        private TaskCompletionSource? _pendingGate;
        private TaskCompletionSource? _activeGate;

        public TaskCompletionSource EnteredGate { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ArmGate()
        {
            EnteredGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeGate = tcs;
            _pendingGate = tcs;
        }

        public void ReleaseGate() => _activeGate?.TrySetResult();

        public IReadOnlyList<IconPosition> ReadIcons()
        {
            TaskCompletionSource? gate = Interlocked.Exchange(ref _pendingGate, null);
            if (gate is not null)
            {
                EnteredGate.TrySetResult();
                gate.Task.GetAwaiter().GetResult();
            }

            return ((IDesktopIcons)inner).ReadIcons();
        }

        public void SetPositions(IReadOnlyList<IconPosition> positions) => ((IDesktopIcons)inner).SetPositions(positions);

        public void SetPositions(IReadOnlyList<IconPosition> positions, Action beforeMove) => ((IDesktopIcons)inner).SetPositions(positions, beforeMove);

        public bool AutoArrange => ((IDesktopIcons)inner).AutoArrange;

        public bool TryTurnAutoArrangeOff() => ((IDesktopIcons)inner).TryTurnAutoArrangeOff();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
