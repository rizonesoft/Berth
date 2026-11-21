using System.Windows;
using System.Windows.Media.Imaging;
using Berth.App;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.App;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class AutoRestoreWindowTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonA = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo MonB1 = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonB2 = new(2560, 0, 1920, 1080, 100, false);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public void OpenWindowShowsAutomaticRestoreResultUndoMapRefreshBusyStateTooltipAndHiddenGuard()
    {
        using var data = new TestData();
        string instanceName = "Berth.Tests.AutoRestoreWindow." + Guid.NewGuid().ToString("N");
        string reportFile = Path.Combine(data.Root, "report.txt");

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.AutoRestoreWindowScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Isolated child exited {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = ReadReport(reportFile);

        Assert.Equal(string.Empty, report["workerError"]);
        Assert.Equal("True", report["idleDuringSettle"]);
        Assert.Equal("Working", report["workingSentence"]);
        Assert.Equal("True", report["restoreDisabledWhileWorking"]);
        Assert.Equal("True", report["saveDisabledWhileWorking"]);

        Assert.Equal("Restored 3 icons.", report["restoredSentence"]);
        Assert.Equal("True", report["undoVisible"]);
        Assert.Equal("Docked", report["selectedAfterRestore"]);
        Assert.Equal("3", report["inPlaceAfterRestore"]);
        Assert.Equal("0", report["movedAfterRestore"]);

        Assert.Equal("Restored 3 icons.", report["sentenceAfterUnchangedRefresh"]);
        Assert.Equal("True", report["undoStillVisibleAfterUnchangedRefresh"]);

        Assert.Equal("Put 3 icons back where they were before the restore.", report["sentenceAfterUndo"]);
        Assert.Equal("900:120,900:240,900:360", report["undoPositions"]);

        Assert.Contains("Docked", report["tooltipAfterB"], StringComparison.Ordinal);
        Assert.Contains("Docked Renamed", report["tooltipAfterRenameWithoutDisplayChange"], StringComparison.Ordinal);
        Assert.Equal("Berth: no layout for this monitor arrangement", report["tooltipAfterUnsavedC"]);
        Assert.Equal("False", report["cLayoutCreated"]);
        Assert.Equal("True", report["silentForC"]);
        Assert.Equal("0", report["balloonCount"]);

        Assert.Equal("True", report["hiddenDesktopRestored"]);
        Assert.NotEqual("Restored 3 icons.", report["hiddenReopenSentence"]);
        Assert.Equal("False", report["hiddenReopenUndoVisible"]);
    }

    [Fact]
    public void TrayOnlyNeverOpensWindowAndOpeningMidRestoreShowsWorkingThenRecovers()
    {
        using var data = new TestData();
        string instanceName = "Berth.Tests.AutoRestoreMidOpen." + Guid.NewGuid().ToString("N");
        string reportFile = Path.Combine(data.Root, "report.txt");

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.AutoRestoreMidOpenAndTrayOnlyScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Isolated child exited {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = ReadReport(reportFile);

        Assert.Equal(string.Empty, report["workerError"]);
        Assert.Equal("0", report["windowsAfterTrayOnlyRestore"]);
        Assert.Equal("True", report["midOpenSawBusy"]);
        Assert.Equal("True", report["midOpenRestoreDisabled"]);
        Assert.Equal("Restored 3 icons.", report["midOpenRecoveredSentence"]);
        Assert.Equal("True", report["midOpenRecoveredUndo"]);
    }

    [Fact]
    public async Task AutomaticRestoreActionMatrixCoversMissingPartialRetryableAutoArrangeSilentNoLayoutAndSecondPartialUndo()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string key = ArrangementKey.From([MonA]);
        IconPosition[] saved =
        [
            new("id-1", "One", 100, 100),
            new("id-2", "Two", 200, 100),
            new("id-3", "Three", 300, 100),
        ];
        store.Save(new Layout(Layout.CurrentSchemaVersion, key, "Laptop", FixedNow, [MonA], [.. saved], null));

        var settingsStore = new SettingsStore(paths);
        _ = settingsStore.Update(s => s with { RestoreAutomatically = true });

        var monitors = new FakeMonitors(MonA);
        var desktop = new FakeDesktopIcons(
            new IconPosition("id-1", "One", 600, 100),
            new IconPosition("id-2", "Two", 600, 200)); // id-3 missing
        var log = new Log(paths, new FixedClock(FixedNow));
        var service = CreateService(paths, store, desktop, monitors, log);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        // 1. Missing icon: success sentence with missing count and Undo offered.
        OperationResult missingResult = service.Restore("automatic, display change");
        await vm.OnExternalOperationFinishedAsync(WindowOperationKind.Restore, missingResult);
        Assert.Equal("Restored 2 of 3 icons. 1 is no longer on the desktop.", vm.Status.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Status.Action);

        // 2. Partial not-placed restore: SPEC §8 partial sentence with Undo, and partial Undo keeps Undo offered for second attempt.
        ((IDesktopIcons)desktop).SetPositions(
        [
            new("id-1", "One", 700, 100),
            new("id-2", "Two", 700, 200),
            new("id-3", "Three", 700, 300),
        ]);
        desktop.SnapBy = 15; // 100/200/300 won't match snapped values -> NotPlaced
        OperationResult partialResult = service.Restore("automatic, display change");
        Assert.Equal(OperationKind.Failed, partialResult.Kind);
        Assert.Equal(FailureKind.NotPlaced, partialResult.Failure);
        await vm.OnExternalOperationFinishedAsync(WindowOperationKind.Restore, partialResult);
        Assert.Equal(
            "Restored 0 of 3 icons. 2 did not stay where they were saved. 1 is no longer on the desktop.",
            vm.Status.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Status.Action);

        // First Undo while SnapBy still prevents exact placement -> partial Undo offers Undo a second time!
        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(StatusAction.Undo, vm.Status.Action);

        // Clear SnapBy -> second Undo succeeds cleanly.
        desktop.SnapBy = 0;
        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Put 2 icons back where they were before the restore.", vm.Status.Sentence);
        Assert.Equal(StatusAction.None, vm.Status.Action);

        // 3. Auto-arrange refusal: SPEC §18 sentence, NO Retry button.
        desktop.SetAutoArrange(true);
        OperationResult autoArrangeResult = service.Restore("automatic, display change");
        await vm.OnExternalOperationFinishedAsync(WindowOperationKind.Restore, autoArrangeResult);
        Assert.Equal(Messages.Refusal(OperationResult.Refused(Refusal.AutoArrange)), vm.Status.Sentence);
        Assert.Equal(StatusAction.None, vm.Status.Action);

        // 4. Retryable refusal (DesktopUnavailable): Retry button offered and actually reattempts restore!
        desktop.SetAutoArrange(false);
        desktop.Unavailable = true;
        OperationResult unavailableResult = service.Restore("automatic, display change");
        await vm.OnExternalOperationFinishedAsync(WindowOperationKind.Restore, unavailableResult);
        Assert.Equal(Messages.Refusal(OperationResult.Refused(Refusal.DesktopUnavailable)), vm.Status.Sentence);
        Assert.Equal(StatusAction.Retry, vm.Status.Action);

        desktop.Unavailable = false;
        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Restored 2 of 3 icons. 1 is no longer on the desktop.", vm.Status.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Status.Action);

        // 5. Silent NoLayout: routing layer ignores silent=true so existing status is NOT overwritten.
        string beforeSilent = vm.Status.Sentence;
        var timers = new ManualTimerFactory();
        using var autoRestore = new AutoRestore(
            service,
            settingsStore,
            log,
            timers,
            TimeSpan.FromSeconds(3),
            new FixedClock(FixedNow),
            runOffUiThread: work => work());

        bool silentObserved = false;
        bool routedToWindow = false;
        autoRestore.Finished += (res, reason, silent) =>
        {
            _ = reason;
            silentObserved = silent;
            if (!silent)
            {
                routedToWindow = true;
                _ = vm.OnExternalOperationFinishedAsync(WindowOperationKind.Restore, res);
            }
        };

        monitors.Monitors = [new MonitorInfo(0, 0, 1280, 720, 100, true)];
        autoRestore.Trigger("display change");
        timers.FireLast();
        Assert.True(silentObserved);
        Assert.False(routedToWindow);
        Assert.Equal(beforeSilent, vm.Status.Sentence);
    }

    [Fact]
    public async Task SupersededExternalCompletionAndFailedRefreshDoNotClobberSelectionOrOfferStaleUndo()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string keyA = ArrangementKey.From([MonA]);
        string keyB = ArrangementKey.From([MonB1, MonB2]);
        store.Save(new Layout(Layout.CurrentSchemaVersion, keyA, "Laptop", FixedNow, [MonA], [new("a-1", "A", 100, 100)], null));
        store.Save(new Layout(Layout.CurrentSchemaVersion, keyB, "Docked", FixedNow, [MonB1, MonB2], [new("b-1", "B", 200, 200)], null));

        var monitors = new FakeMonitors(MonA);
        var innerDesktop = new FakeDesktopIcons(new IconPosition("a-1", "A", 100, 100));
        using var blockingDesktop = new BlockingReadDesktopIcons(innerDesktop);
        var log = new Log(paths, new FixedClock(FixedNow));
        var service = CreateService(paths, store, innerDesktop, monitors, log);
        var vm = CreateViewModel(store, blockingDesktop, monitors, service);
        await vm.RefreshAsync();

        // Start a blocked refresh so OnExternalOperationFinishedAsync waits on it.
        blockingDesktop.ArmReadGate();
        Task blockedPoll = vm.RefreshAsync();
        Assert.True(blockingDesktop.EnteredReadGate.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        // Queue a delayed automatic restore for B while user selects A and runs a newer operation.
        OperationResult delayedB = OperationResult.Restored("Docked", 1, 1, 0, 0, "automatic, display change", keyB);
        Task delayedCompletion = vm.OnExternalOperationFinishedAsync(WindowOperationKind.Restore, delayedB);

        // User selects A explicitly and starts a newer Save result.
        LayoutRow rowA = vm.Rows.First(r => r.Key == keyA);
        appFixture.Invoke(() => vm.Selected = rowA);
        OperationResult newerSave = OperationResult.Saved("Laptop", 1, true, keyA);
        Task newerCompletion = vm.OnExternalOperationFinishedAsync(WindowOperationKind.Save, newerSave);

        blockingDesktop.ReleaseReadGate();
        await Task.WhenAll(blockedPoll, delayedCompletion, newerCompletion);

        // Newer selection (Laptop) and newer status (Saved) must win over delayed B completion.
        Assert.Equal("Laptop", vm.Selected?.Name);
        Assert.Equal("Saved 1 icon. The previous save is kept.", vm.Status.Sentence);

        // Now verify failed refresh never offers Undo from a stale _lastCanUndo!
        // First make _lastCanUndo true via a real restore.
        ((IDesktopIcons)innerDesktop).SetPositions([new("a-1", "A", 500, 500)]);
        OperationResult realRestore = service.Restore("automatic, display change");
        await vm.OnExternalOperationFinishedAsync(WindowOperationKind.Restore, realRestore);
        Assert.Equal(StatusAction.Undo, vm.Status.Action);

        // Now make refresh fail by throwing from ReadIcons; result must still appear, WITHOUT stale Undo.
        blockingDesktop.ThrowOnRead = true;
        OperationResult anotherRestore = OperationResult.Restored("Laptop", 1, 1, 0, 0, "automatic, display change", keyA);
        await vm.OnExternalOperationFinishedAsync(WindowOperationKind.Restore, anotherRestore);
        Assert.Equal("Restored 1 icon.", vm.Status.Sentence);
        Assert.Equal(StatusAction.None, vm.Status.Action);
    }

    [Fact]
    [Trait("Category", "Render")]
    public async Task RenderStatusAutoRestoredInLightAndDark()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string key = ArrangementKey.From([MonB1, MonB2]);
        IconPosition[] saved =
        [
            new("id-1", "Recycle Bin", 76, 120),
            new("id-2", "Notes.txt", 76, 260),
            new("id-3", "Budget.xlsx", 2680, 140),
        ];
        IconPosition[] moved =
        [
            new("id-1", "Recycle Bin", 900, 120),
            new("id-2", "Notes.txt", 900, 260),
            new("id-3", "Budget.xlsx", 3200, 140),
        ];
        store.Save(new Layout(Layout.CurrentSchemaVersion, key, "Docked", FixedNow, [MonB1, MonB2], [.. saved], null));
        var settingsStore = new SettingsStore(paths);
        _ = settingsStore.Update(s => s with { RestoreAutomatically = true });

        var monitors = new FakeMonitors(MonB1, MonB2);
        var desktop = new FakeDesktopIcons(moved);
        var log = new Log(paths, new FixedClock(FixedNow));
        var service = CreateService(paths, store, desktop, monitors, log);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        var timers = new ManualTimerFactory();
        using var autoRestore = new AutoRestore(
            service,
            settingsStore,
            log,
            timers,
            TimeSpan.FromSeconds(3),
            new FixedClock(FixedNow),
            runOffUiThread: work => work());

        Task? routedTask = null;
        autoRestore.Finished += (res, _, silent) =>
        {
            if (!silent)
            {
                routedTask = vm.OnExternalOperationFinishedAsync(WindowOperationKind.Restore, res);
            }
        };

        autoRestore.Trigger("display change");
        timers.FireLast();
        Assert.NotNull(routedTask);
        await routedTask!;

        string outputDir = ResolveRenderOutputDirectory();
        string lightPath = Path.Combine(outputDir, "status-auto-restored-light.png");
        string darkPath = Path.Combine(outputDir, "status-auto-restored-dark.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                window.RootGrid.Measure(new WpfSize(980, 640));
                window.RootGrid.Arrange(new Rect(0, 0, 980, 640));
                window.RootGrid.UpdateLayout();

                Assert.Equal("Restored 3 icons.", window.StatusLineControl.SentenceText.Text);
                Assert.Equal(Visibility.Visible, window.StatusLineControl.ActionButton.Visibility);
                Assert.Equal("Undo", window.StatusLineControl.ActionButton.Content);
                Assert.Equal(3, vm.Comparison?.InPlace);
                Assert.Equal(0, vm.Comparison?.Moved);

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

    private static Dictionary<string, string> ReadReport(string reportFile) =>
        File.ReadAllLines(reportFile)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);

    private static LayoutService CreateService(
        BerthPaths paths,
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors,
        Log log) =>
        new(
            desktop,
            monitors,
            store,
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(5)),
            log,
            new FixedClock(FixedNow));

    private MainViewModel CreateViewModel(LayoutStore store, IDesktopIcons desktop, IMonitors monitors, LayoutService service) =>
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
            timeZone: TimeZoneInfo.Utc);

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
    }

    private sealed class ManualTimerFactory : ITimerFactory
    {
        private Action? _lastCallback;

        public IOneShotTimer Create(TimeSpan dueTime, Action callback)
        {
            _lastCallback = callback;
            return new Handle(() => _lastCallback = null);
        }

        public void FireLast() => _lastCallback?.Invoke();

        private sealed class Handle(Action onCancel) : IOneShotTimer
        {
            public void Cancel() => onCancel();
            public void Dispose() => onCancel();
        }
    }

    private sealed class BlockingReadDesktopIcons(FakeDesktopIcons inner) : IDesktopIcons, IDisposable
    {
        private readonly ManualResetEventSlim _readGate = new(false);
        private int _gateArmed;
        public ManualResetEventSlim EnteredReadGate { get; } = new(false);
        public bool ThrowOnRead { get; set; }

        public void ArmReadGate()
        {
            EnteredReadGate.Reset();
            _readGate.Reset();
            Volatile.Write(ref _gateArmed, 1);
        }

        public void ReleaseReadGate() => _readGate.Set();

        public IReadOnlyList<IconPosition> ReadIcons()
        {
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("Simulated read failure.");
            }

            if (Interlocked.Exchange(ref _gateArmed, 0) == 1)
            {
                EnteredReadGate.Set();
                _readGate.Wait(TimeSpan.FromSeconds(5));
            }

            return ((IDesktopIcons)inner).ReadIcons();
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons) => ((IDesktopIcons)inner).SetPositions(icons);
        public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove) => ((IDesktopIcons)inner).SetPositions(icons, beforeMove);
        public bool AutoArrange => ((IDesktopIcons)inner).AutoArrange;
        public bool TryTurnAutoArrangeOff() => ((IDesktopIcons)inner).TryTurnAutoArrangeOff();

        public void Dispose()
        {
            _readGate.Set();
            _readGate.Dispose();
            EnteredReadGate.Dispose();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
