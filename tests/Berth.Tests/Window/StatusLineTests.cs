using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
public sealed class StatusLineTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonDualA = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonDualB = new(2560, 0, 1920, 1080, 100, false);
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        IconPosition[] saved =
        [
            new("id-1", "One", 100, 100),
            new("id-2", "Two", 200, 200),
            new("id-3", "Three", 300, 300),
        ];
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, saved);

        IconPosition[] moved =
        [
            new("id-1", "One", 900, 100),
            new("id-2", "Two", 900, 200),
            new("id-3", "Three", 900, 300),
        ];
        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(moved);
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w);
                StatusLine status = w.StatusLineControl;

                Assert.Equal(10d, status.StatusSquare.ActualWidth, precision: 2);
                Assert.Equal(10d, status.StatusSquare.ActualHeight, precision: 2);
                Assert.Equal("3 icons have moved since this layout was saved.", status.SentenceText.Text);
                Assert.Equal("Checked 09:14", status.RightTimeText.Text);
                Assert.Equal(Visibility.Visible, status.RightTimeText.Visibility);
                Assert.Equal(Visibility.Collapsed, status.ActionButton.Visibility);
                return w;
            });

            // Restore via DetailHeader -> status shows "Restored 3 icons." and "Undo" button (Checked time hidden).
            appFixture.Invoke(() => InvokeButton(window!.DetailHeaderControl.RestoreButton));
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                StatusLine status = window!.StatusLineControl;
                Assert.Equal("Restored 3 icons.", status.SentenceText.Text);
                Assert.Equal(Visibility.Collapsed, status.RightTimeText.Visibility);
                Assert.Equal(Visibility.Visible, status.ActionButton.Visibility);
                Assert.Equal("Undo", status.ActionButton.Content);
                Assert.Equal("Undo", AutomationProperties.GetName(status.ActionButton));
                Assert.True(status.ActionButton.ActualHeight >= 32d);
            });

            // A changed comparison clears sticky Undo and MUST restore RightTimeText visibility (Checked 09:14).
            ((IDesktopIcons)desktop).SetPositions(
            [
                new("id-1", "One", 700, 100),
                new("id-2", "Two", 200, 200),
                new("id-3", "Three", 300, 300),
            ]);
            await vm.RefreshAsync();
            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                StatusLine status = window!.StatusLineControl;
                Assert.Equal("1 icon has moved since this layout was saved.", status.SentenceText.Text);
                Assert.Equal(Visibility.Visible, status.RightTimeText.Visibility);
                Assert.Equal("Checked 09:14", status.RightTimeText.Text);
                Assert.Equal(Visibility.Collapsed, status.ActionButton.Visibility);
            });

            // Move icons back to [900, 900, 900], Restore again, then click Undo -> puts icons back at 900.
            ((IDesktopIcons)desktop).SetPositions(moved);
            await vm.RefreshAsync();
            appFixture.Invoke(() => InvokeButton(window!.DetailHeaderControl.RestoreButton));
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                StatusLine status = window!.StatusLineControl;
                Assert.Equal(Visibility.Visible, status.ActionButton.Visibility);
                InvokeButton(status.ActionButton);
            });

            await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            IReadOnlyList<IconPosition> afterUndo = ((IDesktopIcons)desktop).ReadIcons();
            Assert.Equal([900, 900, 900], afterUndo.Select(i => i.X));

            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                StatusLine status = window!.StatusLineControl;
                Assert.Equal("Put 3 icons back where they were before the restore.", status.SentenceText.Text);
                Assert.Equal(Visibility.Collapsed, status.ActionButton.Visibility);
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
    public async Task PartialUndoOffersUndoAgainAndRetryRepeatsRefusedSaveAfterDesktopRecovers()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 500, 500));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        // Restore cleanly, then set SnapBy = 10 so first Undo fails to place -> offers Undo again!
        await vm.Detail.RestoreAsync();
        Assert.Equal(StatusAction.Undo, vm.Status.Action);

        desktop.SnapBy = 10;
        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Put 0 of 1 icon back. 1 did not stay where it was.", vm.Status.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Status.Action);

        // Clear SnapBy -> second Undo succeeds and Undo button disappears!
        desktop.SnapBy = 0;
        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Put 1 icon back where it was before the restore.", vm.Status.Sentence);
        Assert.Equal(StatusAction.None, vm.Status.Action);

        // Now make desktop unavailable and attempt Save -> refused with Retry; make desktop available and click Retry!
        desktop.Unavailable = true;
        await vm.Detail.SaveAsync();
        Assert.Equal("Berth cannot reach the desktop. Explorer may be restarting.", vm.Status.Sentence);
        Assert.Equal(StatusAction.Retry, vm.Status.Action);

        desktop.Unavailable = false;
        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Saved 1 icon. The previous save is kept.", vm.Status.Sentence);
        Assert.Equal(StatusAction.None, vm.Status.Action);
    }

    [Fact]
    public async Task TrayRestoreShowsWorkingThenStickyResultWithUndoAndExternalUndoDeletionRemovesButton()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));

        var monitors = new FakeMonitors(MonSingle);
        var innerDesktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 500, 500));
        var desktop = new GatedDesktopIcons(innerDesktop);
        var log = new Log(paths, new FixedClock(FixedNow));
        var settingsStore = new SettingsStore(paths);
        LayoutService service = CreateService(paths, store, desktop, monitors);

        TrayMenu? trayMenu = null;
        MainWindow? window = null;
        MainViewModel? vm = null;
        var trayDone = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                var boundVm = Assert.IsType<MainViewModel>(w.DataContext);
                menu.OperationFinished += res => trayDone.TrySetResult(res);
                return (w, menu, boundVm);
            });

            await vm!.RefreshAsync();

            // Start blocked tray Restore -> open window immediately shows "Working"!
            desktop.ArmGate();
            appFixture.Invoke(() => trayMenu!.Strip.Items[1].PerformClick()); // "Restore layout"
            await desktop.EnteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            appFixture.Invoke(() => Assert.Equal("Working", window!.StatusLineControl.SentenceText.Text));

            // Release tray Restore -> status line shows "Restored 1 icon." with Undo and survives an unchanged poll!
            desktop.ReleaseGate();
            await trayDone.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Wait briefly for OnExternalOperationFinishedAsync to complete its refresh and publish result.
            for (int i = 0; i < 40 && vm.Status.Sentence != "Restored 1 icon."; i++)
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }

            Assert.Equal("Restored 1 icon.", vm.Status.Sentence);
            Assert.Equal(StatusAction.Undo, vm.Status.Action);

            // Unchanged poll keeps sticky sentence & Undo.
            await vm.RefreshAsync();
            Assert.Equal("Restored 1 icon.", vm.Status.Sentence);
            Assert.Equal(StatusAction.Undo, vm.Status.Action);

            // Simulate another Berth process consuming undo.json -> unchanged poll preserves sentence but drops dead Undo!
            File.Delete(paths.UndoFile);
            await vm.RefreshAsync();
            Assert.Equal("Restored 1 icon.", vm.Status.Sentence);
            Assert.Equal(StatusAction.None, vm.Status.Action);

            // Failed tray Save offers Retry in the open window and Retry executes Save after desktop recovers.
            innerDesktop.Unavailable = true;
            var trayFailDone = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            trayMenu!.OperationFinished += res =>
            {
                if (res.Kind == OperationKind.Refused)
                {
                    trayFailDone.TrySetResult(res);
                }
            };
            appFixture.Invoke(() => trayMenu.Strip.Items[0].PerformClick()); // "Save layout"
            await trayFailDone.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            for (int i = 0; i < 40 && vm.Status.Action != StatusAction.Retry; i++)
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }

            Assert.Equal("Berth cannot reach the desktop. Explorer may be restarting.", vm.Status.Sentence);
            Assert.Equal(StatusAction.Retry, vm.Status.Action);

            innerDesktop.Unavailable = false;
            vm.Status.ActionCommand.Execute(null);
            await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal("Saved 1 icon. The previous save is kept.", vm.Status.Sentence);
            Assert.Equal(StatusAction.None, vm.Status.Action);
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
    public async Task StatusLineWrapsLongSentenceCleanlyAtMinWidthHighContrastAnd150PercentScale()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));
        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                void AssertBoundsAtMinWidth(string expectedActionText)
                {
                    MeasureAndArrange(window, width: 760, height: 560);
                    StatusLine status = window.StatusLineControl;
                    Rect cardBounds = new(0, 0, status.StatusCard.ActualWidth, status.StatusCard.ActualHeight);
                    Rect sentenceBounds = status.SentenceText
                        .TransformToAncestor(status.StatusCard)
                        .TransformBounds(new Rect(0, 0, status.SentenceText.ActualWidth, status.SentenceText.ActualHeight));
                    Rect buttonBounds = status.ActionButton
                        .TransformToAncestor(status.StatusCard)
                        .TransformBounds(new Rect(0, 0, status.ActionButton.ActualWidth, status.ActionButton.ActualHeight));

                    Assert.True(sentenceBounds.Width > 0d && sentenceBounds.Height > 0d);
                    Assert.True(buttonBounds.Width > 0d && buttonBounds.Height >= 32d);
                    Assert.True(sentenceBounds.Left >= 0d && sentenceBounds.Top >= 0d);
                    Assert.True(sentenceBounds.Bottom <= cardBounds.Height + 0.5d);
                    Assert.True(buttonBounds.Top >= 0d && buttonBounds.Bottom <= cardBounds.Height + 0.5d);
                    Assert.True(sentenceBounds.Right <= buttonBounds.Left + 0.5d);
                    Assert.Equal(Visibility.Visible, status.ActionButton.Visibility);
                    Assert.Equal(expectedActionText, status.ActionButton.Content);
                    Assert.True(status.ActionButton.Focusable);
                    Assert.True(System.Windows.Input.KeyboardNavigation.GetIsTabStop(status.ActionButton));
                }

                // 1. Long partial-restore with Undo at 760 DIP in Light, High Contrast, and 144 DPI (150% scale).
                vm.Status.SetResult(OperationResult.Restored("Laptop", 42, 38, 2, 2), canUndo: true);
                AssertBoundsAtMinWidth("Undo");

                // 2. Long §18 write failure with Retry at 760 DIP in High Contrast and 144 DPI (150% scale).
                var retryReq = new WindowOperationRequest(WindowOperationKind.Save, () => OperationResult.Saved("Laptop", 1, false));
                vm.Status.SetResult(
                    OperationResult.Failed(
                        FailureKind.WriteFailed,
                        @"C:\Users\Owner\AppData\Local\Berth\layouts\long-monitor-arrangement-key.json",
                        "Access to the path is denied by another process"),
                    canUndo: false,
                    retryRequest: retryReq);

                string hcPath = Path.Combine(data.Root, "status-min-hc.png");
                string dpiPath = Path.Combine(data.Root, "status-min-150.png");
                var berthApp = (BerthApplication)System.Windows.Application.Current;
                ColorTheme savedTheme = berthApp.CurrentColorTheme;
                try
                {
                    berthApp.ApplyColorTheme(ColorTheme.HighContrast);
                    AssertBoundsAtMinWidth("Retry");
                    WindowRenderer.Render(window.RootGrid, 760, 560, hcPath, theme: null);
                }
                finally
                {
                    berthApp.ApplyColorTheme(savedTheme);
                }

                VisualTreeHelper.SetRootDpi(window.RootGrid, new DpiScale(1.5, 1.5));
                AssertBoundsAtMinWidth("Retry");
                WindowRenderer.Render(window.RootGrid, 760, 560, dpiPath, RenderTheme.Light);
                Assert.True(new FileInfo(hcPath).Length > 0);
                Assert.True(new FileInfo(dpiPath).Length > 0);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    [Fact]
    public async Task MidPlacementDesktopLossOffersUndoNotRetryAndRestoresPreRestorePositions()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        IconPosition[] saved =
        [
            new("id-1", "One", 100, 100),
            new("id-2", "Two", 200, 200),
        ];
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, saved);

        IconPosition[] beforeRestore =
        [
            new("id-1", "One", 800, 100),
            new("id-2", "Two", 800, 200),
        ];
        var monitors = new FakeMonitors(MonSingle);
        var desktop = new InterruptingDesktopIcons(new FakeDesktopIcons(beforeRestore));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        // Interrupt placement after moving icon 1 -> undo.json preserved; window MUST offer Undo, not Retry!
        desktop.FailAfterFirstMove = true;
        await vm.Detail.RestoreAsync();
        Assert.Equal(Messages.DesktopLostDuringRestore, vm.Status.Sentence);
        Assert.Equal(StatusAction.Undo, vm.Status.Action);

        // Explorer recovers; invoking the offered action puts both icons back at [800, 800]!
        desktop.FailAfterFirstMove = false;
        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        IReadOnlyList<IconPosition> afterUndo = ((IDesktopIcons)desktop).ReadIcons();
        Assert.Equal([800, 800], afterUndo.Select(i => i.X));
        Assert.Equal("Put 2 icons back where they were before the restore.", vm.Status.Sentence);
        Assert.Equal(StatusAction.None, vm.Status.Action);
    }

    [Fact]
    public async Task SupersededTrayCompletionDoesNotReplaceWorkingDuringNewerOperation()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new GatedDesktopIcons(new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100)));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        // Hold the first tray completion's post-operation refresh open while a newer operation starts.
        desktop.ArmGate();
        Task staleCompletion = vm.OnExternalOperationFinishedAsync(
            WindowOperationKind.Restore,
            OperationResult.Restored("Laptop", 1, 1, 0, 0));
        await desktop.EnteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Newer operation starts -> Detail.IsBusy becomes true and status shows "Working".
        appFixture.Invoke(() => vm.Detail.SetExternalBusy(true));
        Assert.Equal("Working", vm.Status.Sentence);

        // Release the older completion's refresh -> it MUST be discarded and NOT overwrite "Working"!
        desktop.ReleaseGate();
        await staleCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("Working", vm.Status.Sentence);
        Assert.Equal(StatusAction.None, vm.Status.Action);
    }

    [Fact]
    [Trait("Category", "Render")]
    public async Task RendersAllSpec8StatusRowsInLightAndDark()
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
                new IconPosition("id-3", "Budget.xlsx", 2100, 400),
                new IconPosition("id-4", "Slides.pptx", 3000, 300),
                new IconPosition("id-5", "Archive.zip", 3600, 700),
            ],
            null);

        IconPosition[] movedAndMissingIcons =
        [
            new("id-1", "Notes.txt", 700, 500),
            new("id-2", "Report.docx", 1700, 900),
            new("id-3", "Budget.xlsx", 1900, 800),
        ];
        IconPosition[] movedIcons =
        [
            new("id-1", "Notes.txt", 700, 500),
            new("id-2", "Report.docx", 1700, 900),
            new("id-3", "Budget.xlsx", 1900, 800),
            new("id-4", "Slides.pptx", 3000, 300),
            new("id-5", "Archive.zip", 3600, 700),
        ];
        IconPosition[] missingIcons =
        [
            new("id-1", "Notes.txt", 400, 300),
            new("id-2", "Report.docx", 1400, 700),
            new("id-3", "Budget.xlsx", 2100, 400),
        ];

        await RenderStatusAsync("status-working", outputDir, layout, vm => vm.Status.SetBusy(), "Working");
        await RenderStatusAsync("status-restored", outputDir, layout, vm => vm.Status.SetResult(OperationResult.Restored("Docked", 42, 42, 0, 0), canUndo: true), "Restored 42 icons.");
        await RenderStatusAsync("status-restored-partial", outputDir, layout, vm => vm.Status.SetResult(OperationResult.Restored("Docked", 42, 38, 2, 2), canUndo: true), "Restored 38 of 42 icons. 2 did not stay where they were saved. 2 are no longer on the desktop.");
        await RenderStatusAsync("status-undone", outputDir, layout, vm => vm.Status.SetResult(OperationResult.Undone(42, 42, 0, 0), canUndo: false), "Put 42 icons back where they were before the restore.");
        await RenderStatusAsync("status-saved", outputDir, layout, vm => vm.Status.SetResult(OperationResult.Saved("Docked", 42, false)), "Saved 42 icons.");
        await RenderStatusAsync("status-saved-replaced", outputDir, layout, vm => vm.Status.SetResult(OperationResult.Saved("Docked", 42, true)), "Saved 42 icons. The previous save is kept.");
        await RenderStatusAsync("status-reverted", outputDir, layout, vm => vm.Status.SetResult(OperationResult.Reverted("Docked", new DateTimeOffset(2026, 11, 12, 8, 0, 0, TimeSpan.Zero))), "Reverted to the save from 12 November.");
        await RenderStatusAsync("status-refused-retry", outputDir, layout, vm => vm.Status.SetResult(OperationResult.Refused(Refusal.DesktopUnavailable), retryRequest: new WindowOperationRequest(WindowOperationKind.Save, () => OperationResult.Saved("Docked", 5, false))), "Berth cannot reach the desktop. Explorer may be restarting.");
        await RenderStatusAsync("status-refused-no-retry", outputDir, layout, vm => vm.Status.SetResult(OperationResult.Refused(Refusal.AutoArrange)), "Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again.");
        await RenderStatusAsync("status-in-place", outputDir, layout, _ => { }, "Every icon is at its saved position.");
        await RenderStatusAsync("status-moved-and-missing", outputDir, layout, _ => { }, "3 icons have moved and 2 are no longer on the desktop.", currentDesktopIcons: movedAndMissingIcons);
        await RenderStatusAsync("status-moved", outputDir, layout, _ => { }, "3 icons have moved since this layout was saved.", currentDesktopIcons: movedIcons);
        await RenderStatusAsync("status-missing", outputDir, layout, _ => { }, "2 saved icons are no longer on the desktop.", currentDesktopIcons: missingIcons);
        await RenderStatusAsync("status-unsaved", outputDir, layout, _ => { }, "This monitor arrangement has no saved layout yet.", currentDesktopIcons: [new("id-1", "Notes.txt", 400, 300)], currentMonitors: [MonSingle], saveLayout: false);
        await RenderStatusAsync("status-other", outputDir, layout, vm => vm.Selected = vm.Rows.First(r => !r.IsUnsaved), "Saved 12 November for another monitor arrangement.", currentDesktopIcons: [new("id-1", "Notes.txt", 400, 300)], currentMonitors: [MonSingle]);
    }

    private async Task RenderStatusAsync(
        string name,
        string outputDir,
        Layout layout,
        Action<MainViewModel> configureStatus,
        string expectedSentence,
        IconPosition[]? currentDesktopIcons = null,
        MonitorInfo[]? currentMonitors = null,
        bool saveLayout = true)
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        if (saveLayout)
        {
            store.Save(layout);
        }

        var monitors = new FakeMonitors(currentMonitors ?? [.. layout.Monitors]);
        var desktop = new FakeDesktopIcons(currentDesktopIcons ?? [.. layout.Icons]);
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
                configureStatus(vm);
                MeasureAndArrange(window);
                Assert.Equal(expectedSentence, window.StatusLineControl.SentenceText.Text);
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

    private static LayoutComparison MakeComparison(int inPlace, int moved, int missing)
    {
        var icons = new List<ComparedIcon>();
        for (int i = 0; i < inPlace; i++)
        {
            var p = new IconPosition($"ip-{i}", $"IP{i}", 10 + i, 10);
            icons.Add(new ComparedIcon(p, IconState.InPlace, p, 1, false));
        }

        for (int i = 0; i < moved; i++)
        {
            var s = new IconPosition($"mv-{i}", $"MV{i}", 100 + i, 10);
            var c = new IconPosition($"mv-{i}", $"MV{i}", 500 + i, 10);
            icons.Add(new ComparedIcon(s, IconState.Moved, c, 1, false));
        }

        for (int i = 0; i < missing; i++)
        {
            var s = new IconPosition($"ms-{i}", $"MS{i}", 200 + i, 10);
            icons.Add(new ComparedIcon(s, IconState.Missing, null, 1, false));
        }

        return new LayoutComparison(icons, inPlace, moved, missing);
    }

    private static LayoutService CreateService(BerthPaths paths, LayoutStore store, IDesktopIcons desktop, IMonitors monitors) =>
        new(
            desktop,
            monitors,
            store,
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(5)),
            new Log(paths, new FixedClock(FixedNow)),
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

    private sealed class InterruptingDesktopIcons(FakeDesktopIcons inner) : IDesktopIcons
    {
        public bool FailAfterFirstMove { get; set; }

        public IReadOnlyList<IconPosition> ReadIcons() => ((IDesktopIcons)inner).ReadIcons();

        public void SetPositions(IReadOnlyList<IconPosition> positions) => SetPositions(positions, () => { });

        public void SetPositions(IReadOnlyList<IconPosition> positions, Action beforeMove)
        {
            beforeMove();
            if (FailAfterFirstMove && positions.Count > 0)
            {
                ((IDesktopIcons)inner).SetPositions([positions[0]]);
                throw new DesktopUnavailableException("Explorer disappeared mid-placement.");
            }

            ((IDesktopIcons)inner).SetPositions(positions);
        }

        public bool AutoArrange => ((IDesktopIcons)inner).AutoArrange;

        public bool TryTurnAutoArrangeOff() => ((IDesktopIcons)inner).TryTurnAutoArrangeOff();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
