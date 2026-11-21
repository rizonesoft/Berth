using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Berth.App;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using WpfButton = System.Windows.Controls.Button;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class NoticesTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonDualA = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonDualB = new(2560, 0, 1920, 1080, 100, false);
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public void OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations()
    {
        var okDesktop = new DesktopState([], [MonSingle], AutoArrange: false, Unavailable: false, FixedNow);
        Assert.Empty(NoticesViewModel.For(okDesktop, []));

        var autoArrangeDesktop = okDesktop with { AutoArrange = true };
        IReadOnlyList<Notice> autoOnly = NoticesViewModel.For(autoArrangeDesktop, []);
        Assert.Single(autoOnly);
        Assert.Equal(NoticeKind.AutoArrange, autoOnly[0].Kind);
        Assert.False(autoOnly[0].IsError);
        Assert.Equal("Auto arrange icons is on. Windows ignores saved positions while it is.", autoOnly[0].Text);
        Assert.Equal("Turn it off", autoOnly[0].ButtonText);

        var unavailableDesktop = okDesktop with { Unavailable = true };
        IReadOnlyList<Notice> unavailOnly = NoticesViewModel.For(unavailableDesktop, []);
        Assert.Single(unavailOnly);
        Assert.Equal(NoticeKind.DesktopUnavailable, unavailOnly[0].Kind);
        Assert.True(unavailOnly[0].IsError);
        Assert.Equal("Berth cannot reach the desktop. Explorer may be restarting.", unavailOnly[0].Text);
        Assert.Equal("Retry", unavailOnly[0].ButtonText);

        var bad = new UnreadableFile("corrupt.json", "invalid JSON");
        IReadOnlyList<Notice> unreadableOnly = NoticesViewModel.For(okDesktop, [bad], @"C:\Temp\layouts");
        Assert.Single(unreadableOnly);
        Assert.Equal(NoticeKind.UnreadableFile, unreadableOnly[0].Kind);
        Assert.True(unreadableOnly[0].IsError);
        Assert.Equal("The layout file corrupt.json could not be read. It was left untouched.", unreadableOnly[0].Text);
        Assert.Equal("Open folder", unreadableOnly[0].ButtonText);
        Assert.Equal(@"C:\Temp\layouts\corrupt.json", unreadableOnly[0].FilePath);

        // Auto arrange + unreadable file -> error FIRST, warning SECOND!
        IReadOnlyList<Notice> stacked = NoticesViewModel.For(autoArrangeDesktop, [bad], @"C:\Temp\layouts");
        Assert.Equal(2, stacked.Count);
        Assert.Equal(NoticeKind.UnreadableFile, stacked[0].Kind);
        Assert.True(stacked[0].IsError);
        Assert.Equal(NoticeKind.AutoArrange, stacked[1].Kind);
        Assert.False(stacked[1].IsError);

        // ShellFolderOpener quotes paths containing spaces and commas cleanly.
        Assert.Equal(
            "/select,\"C:\\My Layouts\\desk, primary\\bad.json\"",
            ShellFolderOpener.BuildArguments(@"C:\My Layouts\desk, primary\bad.json"));
    }

    [Fact]
    public async Task TurnAutoArrangeOffClearsNoticeWhenOffAndShowsSpec18MessageWhenStillOn()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100))
        {
            KeepsAutoArrange = true,
        };
        desktop.SetAutoArrange(true);
        var log = new Log(paths, new FixedClock(FixedNow));
        LayoutService service = CreateService(paths, store, desktop, monitors, log);
        var vm = CreateViewModel(store, desktop, monitors, service, log: log);
        await vm.RefreshAsync();

        Assert.Single(vm.Notices.Items);
        Assert.Equal(NoticeKind.AutoArrange, vm.Notices.Items[0].Kind);

        // 1. Read-back still says on -> notice stays, SPEC §18 message appears in status line and log!
        vm.Notices.Items[0].ActionCommand.Execute(null);
        await vm.Notices.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Single(vm.Notices.Items);
        Assert.Equal(Messages.AutoArrangeWouldNotTurnOff, vm.Status.Sentence);
        Assert.Contains(Messages.AutoArrangeWouldNotTurnOff, File.ReadAllText(paths.LogFile));

        // 2. Allow turning off -> notice disappears and Restore becomes enabled!
        desktop.KeepsAutoArrange = false;
        vm.Notices.Items[0].ActionCommand.Execute(null);
        await vm.Notices.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Empty(vm.Notices.Items);
        Assert.True(vm.Detail.CanRestore);
        Assert.Null(vm.Detail.RestoreToolTip);
    }

    [Fact]
    public async Task RetryWaitsForInFlightStalePollAndClearsUnavailableNoticeImmediately()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));

        var monitors = new FakeMonitors(MonSingle);
        var inner = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100)) { Unavailable = true };
        var desktop = new GatedDesktopIcons(inner);
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        Assert.Single(vm.Notices.Items);
        Assert.Equal(NoticeKind.DesktopUnavailable, vm.Notices.Items[0].Kind);

        // Hold an older unavailable poll in flight while desktop recovers, then click Retry!
        desktop.ArmGate();
        Task stalePoll = vm.RefreshAsync();
        await desktop.EnteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        inner.Unavailable = false;
        vm.Notices.Items[0].ActionCommand.Execute(null);
        Task retryTask = vm.Notices.LastActionTask;

        desktop.ReleaseGate();
        await Task.WhenAll(stalePoll, retryTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Empty(vm.Notices.Items);
        Assert.True(vm.Detail.CanRestore);
        Assert.True(vm.Detail.CanSave);
    }

    [Fact]
    public async Task OpenFolderPassesExactSecondFilePathAndHostedControlsRespectDisablesAndTooltips()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        Directory.CreateDirectory(paths.LayoutsFolder);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));

        string badA = Path.Combine(paths.LayoutsFolder, "a-bad.json");
        string badB = Path.Combine(paths.LayoutsFolder, "z-very-long-monitor-arrangement-corrupt-layout-file-name.json");
        File.WriteAllText(badA, "{ not json");
        File.WriteAllText(badB, "{ also bad");

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        desktop.SetAutoArrange(true);
        var opener = new FakeFolderOpener();
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service, folderOpener: opener);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w, width: 760, height: 560);

                // Expect 3 stacked bars: 2 errors (a-bad, z-very-long...) then 1 warning (auto arrange)!
                ItemsControl items = w.NoticesControl.NoticesItems;
                Assert.Equal(3, items.Items.Count);

                string[] expectedTexts =
                [
                    "The layout file a-bad.json could not be read. It was left untouched.",
                    "The layout file z-very-long-monitor-arrangement-corrupt-layout-file-name.json could not be read. It was left untouched.",
                    NoticesViewModel.AutoArrangeText,
                ];
                string[] expectedButtons = ["Open folder", "Open folder", "Turn it off"];
                string[] expectedSeverities = ["Error", "Error", "Warning"];

                for (int idx = 0; idx < 3; idx++)
                {
                    ContentPresenter container = (ContentPresenter)items.ItemContainerGenerator.ContainerFromIndex(idx);
                    Border bar = FindVisualChildren<Border>(container).First();
                    TextBlock[] texts = [.. FindVisualChildren<TextBlock>(bar)];
                    WpfButton button = FindVisualChildren<WpfButton>(bar).Single();
                    TextBlock sentenceTb = texts.Single(tb => tb.Text == expectedTexts[idx]);

                    Assert.Contains(texts, tb => AutomationProperties.GetName(tb) == expectedSeverities[idx]);
                    Assert.Equal(expectedButtons[idx], button.Content);
                    Assert.True(button.ActualHeight >= 32d);

                    Rect barBounds = new(0, 0, bar.ActualWidth, bar.ActualHeight);
                    Rect sentenceBounds = sentenceTb.TransformToAncestor(bar)
                        .TransformBounds(new Rect(0, 0, sentenceTb.ActualWidth, sentenceTb.ActualHeight));
                    Rect buttonBounds = button.TransformToAncestor(bar)
                        .TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    Assert.True(sentenceBounds.Bottom <= barBounds.Height + 0.5d);
                    Assert.True(buttonBounds.Right <= barBounds.Right + 0.5d);
                    Assert.True(sentenceBounds.Right <= buttonBounds.Left + 0.5d);
                }

                ContentPresenter secondContainer = (ContentPresenter)items.ItemContainerGenerator.ContainerFromIndex(1);
                WpfButton secondButton = FindVisualChildren<WpfButton>(secondContainer).Single();
                InvokeButton(secondButton);
                Assert.Equal([badB], opener.OpenedPaths);

                // Auto arrange disables Restore with tooltip repeating notice, while Save stays enabled!
                Assert.False(w.DetailHeaderControl.RestoreButton.IsEnabled);
                Assert.Equal(NoticesViewModel.AutoArrangeText, w.DetailHeaderControl.RestoreButton.ToolTip);
                Assert.True(w.DetailHeaderControl.SaveButton.IsEnabled);

                return w;
            });

            // Save still executes under auto arrange, and completing Save MUST NOT re-enable Restore!
            await vm.Detail.SaveAsync();
            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                Assert.False(window!.DetailHeaderControl.RestoreButton.IsEnabled);
                Assert.True(window!.DetailHeaderControl.SaveButton.IsEnabled);
            });

            // Desktop unavailable disables BOTH Restore and Save!
            desktop.Unavailable = true;
            await vm.RefreshAsync();
            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                Assert.False(window!.DetailHeaderControl.RestoreButton.IsEnabled);
                Assert.False(window!.DetailHeaderControl.SaveButton.IsEnabled);
                Assert.Equal(NoticesViewModel.DesktopUnavailableText, window!.DetailHeaderControl.RestoreButton.ToolTip);
                Assert.Equal(NoticesViewModel.DesktopUnavailableText, window!.DetailHeaderControl.SaveButton.ToolTip);
            });
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
    public async Task TurnAutoArrangeOffHandlesUnavailableReadbackBusyExclusionAndOpenFolderFailureLogsAndShowsStatus()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        Directory.CreateDirectory(paths.LayoutsFolder);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));
        File.WriteAllText(Path.Combine(paths.LayoutsFolder, "bad.json"), "{ invalid");

        var monitors = new FakeMonitors(MonSingle);
        var inner = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        inner.SetAutoArrange(true);
        var desktop = new DisappearingAfterTurnOffDesktopIcons(inner);
        var log = new Log(paths, new FixedClock(FixedNow));
        var throwingOpener = new ThrowingFolderOpener("Explorer launch blocked");
        LayoutService service = CreateService(paths, store, desktop, monitors, log);
        var vm = CreateViewModel(store, desktop, monitors, service, folderOpener: throwingOpener, log: log);
        await vm.RefreshAsync();

        // 1. Open folder throwing logs FAIL and shows §18 unexpected-error sentence in status line.
        NoticeItem unreadableNotice = vm.Notices.Items.Single(i => i.Kind == NoticeKind.UnreadableFile);
        unreadableNotice.ActionCommand.Execute(null);
        Assert.Contains("Something went wrong: Explorer launch blocked.", vm.Status.Sentence);
        Assert.Contains("Explorer launch blocked", File.ReadAllText(paths.LogFile));

        // 2. While external busy is active, Turn it off cannot execute.
        appFixture.Invoke(() => vm.Detail.SetExternalBusy(true));
        NoticeItem autoNotice = vm.Notices.Items.Single(i => i.Kind == NoticeKind.AutoArrange);
        Assert.False(autoNotice.ActionCommand.CanExecute(null));
        appFixture.Invoke(() => vm.Detail.SetExternalBusy(false));
        Assert.True(autoNotice.ActionCommand.CanExecute(null));

        // 3. Explorer disappears between TryTurnAutoArrangeOff and read-back -> unreachable notice appears and status reports it.
        desktop.GoUnavailableOnTurnOff = true;
        autoNotice.ActionCommand.Execute(null);
        await vm.Notices.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Contains(vm.Notices.Items, i => i.Kind == NoticeKind.DesktopUnavailable);
        Assert.Equal(NoticesViewModel.DesktopUnavailableText, vm.Status.Sentence);

        // 4. Unexpected exception from TryTurnAutoArrangeOff does not fault task, logs FAIL, and shows §18 message.
        desktop.GoUnavailableOnTurnOff = false;
        inner.Unavailable = false;
        inner.SetAutoArrange(true);
        await vm.RefreshAsync();
        desktop.UnexpectedExceptionOnTurnOff = new InvalidOperationException("Shell COM fault");
        vm.Notices.Items.Single(i => i.Kind == NoticeKind.AutoArrange).ActionCommand.Execute(null);
        await vm.Notices.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains("Something went wrong: Shell COM fault.", vm.Status.Sentence);
        Assert.Contains("Shell COM fault", File.ReadAllText(paths.LogFile));
    }

    [Fact]
    public async Task TraySaveIsBlockedWhileTurnAutoArrangeOffIsRunning()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(MonSingle);
        var inner = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        inner.SetAutoArrange(true);
        var desktop = new GatedTurnOffDesktopIcons(inner);
        var log = new Log(paths, new FixedClock(FixedNow));
        var settingsStore = new SettingsStore(paths);
        LayoutService service = CreateService(paths, store, desktop, monitors, log);

        TrayMenu? trayMenu = null;
        MainWindow? window = null;
        MainViewModel? vm = null;
        bool trayFinished = false;
        try
        {
            (window, trayMenu, vm) = appFixture.Invoke(() =>
            {
                var menu = new TrayMenu(
                    service,
                    store,
                    monitors,
                    desktop,
                    settingsStore,
                    log,
                    (action, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(action));
                menu.OperationFinished += _ => trayFinished = true;
                var composition = Composition.ForTest(
                    paths,
                    log,
                    monitors,
                    desktop,
                    store,
                    new OperationLock(paths, TimeSpan.FromSeconds(5)),
                    service);
                MainWindow w = TrayApp.CreateProductionMainWindow(
                    (BerthApplication)System.Windows.Application.Current,
                    settingsStore,
                    composition,
                    monitors,
                    new FixedClock(FixedNow),
                    menu);
                return (w, menu, Assert.IsType<MainViewModel>(w.DataContext));
            });

            await vm!.RefreshAsync();

            desktop.ArmGate();
            appFixture.Invoke(() => vm.Notices.Items.Single(i => i.Kind == NoticeKind.AutoArrange).ActionCommand.Execute(null));
            await desktop.EnteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // While Turn it off is in flight, invoking tray Save MUST NOT start a tray operation!
            appFixture.Invoke(() => trayMenu!.Strip.Items[0].PerformClick()); // "Save layout"
            Assert.False(trayMenu!.IsOperationBusy);
            Assert.False(trayFinished);
            Assert.Empty(store.LoadAll().Layouts);

            desktop.ReleaseGate();
            await vm.Notices.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Empty(vm.Notices.Items);
            Assert.False(trayFinished);
        }
        finally
        {
            desktop.ReleaseGate();
            appFixture.Invoke(() =>
            {
                window?.CloseForShutdown();
                trayMenu?.Dispose();
            });
        }
    }

    [Fact]
    [Trait("Category", "Render")]
    public async Task RendersAllFiveSpec19NoticeStatesInLightAndDark()
    {
        string outputDir = ResolveRenderOutputDirectory();
        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonDualA, MonDualB]),
            "Docked",
            new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero),
            [MonDualA, MonDualB],
            [
                new IconPosition("id-1", "Notes.txt", 400, 300),
                new IconPosition("id-2", "Report.docx", 1400, 700),
            ],
            null);

        await RenderNoticeAsync("notice-none", outputDir, layout, autoArrange: false, unavailable: false, unreadableFile: null);
        await RenderNoticeAsync("notice-auto-arrange", outputDir, layout, autoArrange: true, unavailable: false, unreadableFile: null);
        await RenderNoticeAsync("notice-unreachable", outputDir, layout, autoArrange: false, unavailable: true, unreadableFile: null);
        await RenderNoticeAsync("notice-unreadable", outputDir, layout, autoArrange: false, unavailable: false, unreadableFile: "corrupt-layout.json");
        await RenderNoticeAsync("notice-two", outputDir, layout, autoArrange: true, unavailable: false, unreadableFile: "corrupt-layout.json");
    }

    private async Task RenderNoticeAsync(
        string name,
        string outputDir,
        Layout layout,
        bool autoArrange,
        bool unavailable,
        string? unreadableFile)
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        store.Save(layout);
        if (unreadableFile is not null)
        {
            File.WriteAllText(Path.Combine(paths.LayoutsFolder, unreadableFile), "{ broken");
        }

        var monitors = new FakeMonitors([.. layout.Monitors]);
        var desktop = new FakeDesktopIcons([.. layout.Icons])
        {
            Unavailable = unavailable,
        };
        desktop.SetAutoArrange(autoArrange);
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        string lightPath = Path.Combine(outputDir, $"{name}-light.png");
        string darkPath = Path.Combine(outputDir, $"{name}-dark.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                MeasureAndArrange(window);
                WindowRenderer.Render(window.RootGrid, 980, 640, lightPath, RenderTheme.Light);
                WindowRenderer.Render(window.RootGrid, 980, 640, darkPath, RenderTheme.Dark);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });

        AssertValidPng(lightPath);
        AssertValidPng(darkPath);
    }

    private static LayoutService CreateService(
        BerthPaths paths,
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors,
        Log? log = null) =>
        new(
            desktop,
            monitors,
            store,
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(5)),
            log ?? new Log(paths, new FixedClock(FixedNow)),
            new FixedClock(FixedNow));

    private MainViewModel CreateViewModel(
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors,
        LayoutService service,
        IFolderOpener? folderOpener = null,
        Log? log = null) =>
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
            folderOpener: folderOpener ?? new FakeFolderOpener(),
            log: log);

    private static void SaveLayout(LayoutStore store, MonitorInfo[] monitors, string name, DateTimeOffset savedAt, params IconPosition[] icons)
    {
        string key = ArrangementKey.From(monitors);
        store.Save(new Layout(Layout.CurrentSchemaVersion, key, name, savedAt, [.. monitors], [.. icons], null));
    }

    private static void InvokeButton(WpfButton button)
    {
        var peer = new ButtonAutomationPeer(button);
        IInvokeProvider provider = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
        provider.Invoke();
        button.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Input);
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

    private sealed class FakeFolderOpener : IFolderOpener
    {
        public List<string> OpenedPaths { get; } = [];

        public void OpenSelectFile(string fullPath) => OpenedPaths.Add(fullPath);
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

    private sealed class ThrowingFolderOpener(string message) : IFolderOpener
    {
        public void OpenSelectFile(string fullPath) => throw new InvalidOperationException(message);
    }

    private sealed class DisappearingAfterTurnOffDesktopIcons(FakeDesktopIcons inner) : IDesktopIcons
    {
        public bool GoUnavailableOnTurnOff { get; set; }

        public Exception? UnexpectedExceptionOnTurnOff { get; set; }

        public IReadOnlyList<IconPosition> ReadIcons() => ((IDesktopIcons)inner).ReadIcons();

        public void SetPositions(IReadOnlyList<IconPosition> positions) => ((IDesktopIcons)inner).SetPositions(positions);

        public void SetPositions(IReadOnlyList<IconPosition> positions, Action beforeMove) => ((IDesktopIcons)inner).SetPositions(positions, beforeMove);

        public bool AutoArrange => ((IDesktopIcons)inner).AutoArrange;

        public bool TryTurnAutoArrangeOff()
        {
            if (UnexpectedExceptionOnTurnOff is { } ex)
            {
                throw ex;
            }

            if (GoUnavailableOnTurnOff)
            {
                inner.Unavailable = true;
                throw new DesktopUnavailableException("Explorer disappeared");
            }

            return ((IDesktopIcons)inner).TryTurnAutoArrangeOff();
        }
    }

    private sealed class GatedTurnOffDesktopIcons(FakeDesktopIcons inner) : IDesktopIcons
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

        public IReadOnlyList<IconPosition> ReadIcons() => ((IDesktopIcons)inner).ReadIcons();

        public void SetPositions(IReadOnlyList<IconPosition> positions) => ((IDesktopIcons)inner).SetPositions(positions);

        public void SetPositions(IReadOnlyList<IconPosition> positions, Action beforeMove) => ((IDesktopIcons)inner).SetPositions(positions, beforeMove);

        public bool AutoArrange => ((IDesktopIcons)inner).AutoArrange;

        public bool TryTurnAutoArrangeOff()
        {
            TaskCompletionSource? gate = Interlocked.Exchange(ref _pendingGate, null);
            if (gate is not null)
            {
                EnteredGate.TrySetResult();
                gate.Task.GetAwaiter().GetResult();
            }

            return ((IDesktopIcons)inner).TryTurnAutoArrangeOff();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
