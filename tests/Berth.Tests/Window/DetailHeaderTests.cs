using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class DetailHeaderTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo MonDualA = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonDualB = new(2560, 0, 1920, 1080, 100, false);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonDualA, MonDualB], "Docked", FixedNow, new IconPosition("id-1", "One", 10, 20));

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            // 1. Active saved layout.
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w);
                DetailHeader header = w.DetailHeaderControl;

                Assert.Equal(DetailKind.Active, vm.Detail.Kind);
                Assert.Equal("Docked", header.TitleText.Text);
                Assert.Equal("2560 \u00D7 1440 and 1920 \u00D7 1080 \u00B7 saved today, 09:14", header.SubtitleText.Text);
                Assert.Equal(Visibility.Visible, header.RestoreButton.Visibility);
                Assert.Equal(Visibility.Visible, header.SaveButton.Visibility);
                Assert.Equal(Visibility.Visible, header.MoreButton.Visibility);
                Assert.Equal(Visibility.Collapsed, header.OtherArrangementText.Visibility);

                // Verify SPEC §6 button order: Restore, Save current layout, More, and Fluent AccentButtonStyle on Save.
                WpfButton[] buttons = [.. header.ActionsPanel.Children.OfType<WpfButton>()];
                Assert.Equal([header.RestoreButton, header.SaveButton, header.MoreButton], buttons);
                Assert.Same(System.Windows.Application.Current.TryFindResource("AccentButtonStyle"), header.SaveButton.Style);
                return w;
            });

            // 2. Switch to an unsaved connected arrangement (distinct single monitor 1920 x 1080).
            monitors.Monitors = [MonSingle];
            await vm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                DetailHeader header = window!.DetailHeaderControl;

                // Select "This arrangement" (row 0) -> Unsaved kind.
                vm.Selected = vm.Rows[0];
                MeasureAndArrange(window);
                Assert.Equal(DetailKind.Unsaved, vm.Detail.Kind);
                Assert.Equal("Not saved yet", header.TitleText.Text);
                Assert.Equal("1920 \u00D7 1080", header.SubtitleText.Text);
                Assert.Equal(Visibility.Collapsed, header.RestoreButton.Visibility);
                Assert.Equal(Visibility.Visible, header.SaveButton.Visibility);
                Assert.Equal(Visibility.Collapsed, header.MoreButton.Visibility);
                Assert.Equal(Visibility.Collapsed, header.OtherArrangementText.Visibility);

                // Select "Docked" (row 1) -> Other kind (stored dual monitors, NOT connected single monitor).
                vm.Selected = vm.Rows[1];
                MeasureAndArrange(window);
                Assert.Equal(DetailKind.Other, vm.Detail.Kind);
                Assert.Equal("Docked", header.TitleText.Text);
                Assert.Equal("2560 \u00D7 1440 and 1920 \u00D7 1080 \u00B7 saved today, 09:14", header.SubtitleText.Text);
                Assert.Equal(Visibility.Collapsed, header.RestoreButton.Visibility);
                Assert.Equal(Visibility.Collapsed, header.SaveButton.Visibility);
                Assert.Equal(Visibility.Visible, header.MoreButton.Visibility);
                Assert.Equal(Visibility.Visible, header.OtherArrangementText.Visibility);
                Assert.Equal(DetailViewModel.OtherArrangementSentence, header.OtherArrangementText.Text);

                WpfPoint titleBottom = header.TitleText.TranslatePoint(new WpfPoint(0, header.TitleText.ActualHeight), header);
                WpfPoint otherTop = header.OtherArrangementText.TranslatePoint(new WpfPoint(0, 0), header);
                Assert.True(otherTop.Y >= titleBottom.Y, $"Expected OtherArrangementText below header ({otherTop.Y} >= {titleBottom.Y}).");
            });

            // 3. Losing monitors with no saved layouts -> DetailKind.None, empty text, all actions hidden, no crash, then recovers.
            using var emptyData = new TestData();
            BerthPaths emptyPaths = BerthPaths.ForRoot(emptyData.Root);
            var emptyStore = new LayoutStore(emptyPaths);
            var failingMonitors = new FakeMonitors(MonSingle);
            var emptyService = CreateService(emptyPaths, emptyStore, desktop, failingMonitors);
            var noneVm = CreateViewModel(emptyStore, desktop, failingMonitors, emptyService);
            await noneVm.RefreshAsync();
            Assert.Equal(DetailKind.Unsaved, noneVm.Detail.Kind);

            appFixture.Invoke(() =>
            {
                window!.DataContext = noneVm;
                MeasureAndArrange(window);
                Assert.Equal("Not saved yet", window.DetailHeaderControl.TitleText.Text);
            });

            failingMonitors.Unavailable = true;
            await noneVm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                DetailHeader header = window!.DetailHeaderControl;
                Assert.Equal(DetailKind.None, noneVm.Detail.Kind);
                Assert.Equal(string.Empty, header.TitleText.Text);
                Assert.Equal(string.Empty, header.SubtitleText.Text);
                Assert.Equal(Visibility.Collapsed, header.RestoreButton.Visibility);
                Assert.Equal(Visibility.Collapsed, header.SaveButton.Visibility);
                Assert.Equal(Visibility.Collapsed, header.MoreButton.Visibility);
                Assert.False(noneVm.Detail.CanSave);
                Assert.False(noneVm.Detail.CanRestore);
            });

            failingMonitors.Unavailable = false;
            await noneVm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                DetailHeader header = window!.DetailHeaderControl;
                Assert.Equal(DetailKind.Unsaved, noneVm.Detail.Kind);
                Assert.Equal("Not saved yet", header.TitleText.Text);
                Assert.Equal("1920 \u00D7 1080", header.SubtitleText.Text);
                Assert.True(noneVm.Detail.CanSave);
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
    public async Task BoundSaveAndRestoreButtonsWriteLayoutMoveFakeDesktopDisableAllThreeWhileBusyAndPreventReentry()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var innerDesktop = new FakeDesktopIcons(new IconPosition("id-notes", "Notes.txt", 140, 260));
        var desktop = new GatedDesktopIcons(innerDesktop);
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        var startedEvents = new List<WindowOperationKind>();
        var finishedEvents = new List<(WindowOperationKind Kind, OperationResult Result, DetailKind KindAtFinish, bool IsBusyAtFinish)>();
        vm.Detail.OperationStarted += kind => startedEvents.Add(kind);
        vm.Detail.OperationFinished += (kind, res) => finishedEvents.Add((kind, res, vm.Detail.Kind, vm.Detail.IsBusy));

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w);
                return w;
            });

            // Gate the Save read so we can inspect busy state on the live UI dispatcher.
            desktop.ArmGate();
            appFixture.Invoke(() => InvokeButton(window!.DetailHeaderControl.SaveButton));

            await desktop.EnteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            appFixture.Invoke(() =>
            {
                DetailHeader header = window!.DetailHeaderControl;
                Assert.Equal([WindowOperationKind.Save], startedEvents);
                Assert.Empty(finishedEvents);
                Assert.True(vm.Detail.IsBusy);
                Assert.False(header.RestoreButton.IsEnabled);
                Assert.False(header.SaveButton.IsEnabled);
                Assert.False(header.MoreButton.IsEnabled);

                // Re-entrant Save while busy must NOT reach LayoutService.
                int beforeCalls = desktop.ReadCount;
                vm.Detail.SaveCommand.Execute(null);
                Assert.Equal(beforeCalls, desktop.ReadCount);
            });

            desktop.ReleaseGate();
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Verify layout file written with exact icon coordinates, Active selected, and OperationFinished fired after selection.
            string expectedKey = ArrangementKey.From([MonDualA, MonDualB]);
            Layout? saved = store.LoadAll().Layouts.FirstOrDefault(l => l.Key == expectedKey);
            Assert.NotNull(saved);
            Assert.Single(saved.Icons);
            Assert.Equal("id-notes", saved.Icons[0].Id);
            Assert.Equal(140, saved.Icons[0].X);
            Assert.Equal(260, saved.Icons[0].Y);

            Assert.Single(finishedEvents);
            Assert.Equal(WindowOperationKind.Save, finishedEvents[0].Kind);
            Assert.Equal(OperationKind.Saved, finishedEvents[0].Result.Kind);
            Assert.Equal(DetailKind.Active, finishedEvents[0].KindAtFinish);
            Assert.False(finishedEvents[0].IsBusyAtFinish);

            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                DetailHeader header = window!.DetailHeaderControl;
                Assert.False(vm.Detail.IsBusy);
                Assert.Equal(DetailKind.Active, vm.Detail.Kind);
                Assert.True(header.RestoreButton.IsEnabled);
                Assert.True(header.SaveButton.IsEnabled);
                Assert.True(header.MoreButton.IsEnabled);
            });

            // Move the fake icon away, then invoke the bound Restore button and verify it moves back.
            ((IDesktopIcons)innerDesktop).SetPositions([new IconPosition("id-notes", "Notes.txt", 900, 500)]);
            desktop.ArmGate();
            appFixture.Invoke(() => InvokeButton(window!.DetailHeaderControl.RestoreButton));

            await desktop.EnteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            appFixture.Invoke(() =>
            {
                DetailHeader header = window!.DetailHeaderControl;
                Assert.True(vm.Detail.IsBusy);
                Assert.False(header.RestoreButton.IsEnabled);
                Assert.False(header.SaveButton.IsEnabled);
                Assert.False(header.MoreButton.IsEnabled);
            });

            desktop.ReleaseGate();
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            IconPosition restoredIcon = Assert.Single(((IDesktopIcons)innerDesktop).ReadIcons());
            Assert.Equal(140, restoredIcon.X);
            Assert.Equal(260, restoredIcon.Y);
            Assert.Equal(2, finishedEvents.Count);
            Assert.Equal(WindowOperationKind.Restore, finishedEvents[1].Kind);
            Assert.Equal(OperationKind.Restored, finishedEvents[1].Result.Kind);

            // Refusal releases buttons, and notice overrides stay composed with busy state after completion.
            innerDesktop.SetAutoArrange(true);
            appFixture.Invoke(() =>
            {
                vm.Detail.SetNoticeOverrides(
                    extraCanRestore: () => true,
                    restoreToolTip: null,
                    extraCanSave: () => true,
                    saveToolTip: null);
                InvokeButton(window!.DetailHeaderControl.RestoreButton);
            });
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(OperationKind.Refused, finishedEvents[2].Result.Kind);
            Assert.Equal(Refusal.AutoArrange, finishedEvents[2].Result.Refusal);
            Assert.False(vm.Detail.IsBusy);

            // Now set notice override disabling Restore; after Save completes, Restore MUST remain disabled!
            appFixture.Invoke(() =>
            {
                vm.Detail.SetNoticeOverrides(
                    extraCanRestore: () => false,
                    restoreToolTip: "Auto arrange icons is on.",
                    extraCanSave: () => true,
                    saveToolTip: null);
                Assert.False(window!.DetailHeaderControl.RestoreButton.IsEnabled);
                Assert.True(window!.DetailHeaderControl.SaveButton.IsEnabled);
                InvokeButton(window!.DetailHeaderControl.SaveButton);
            });
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            appFixture.Invoke(() =>
            {
                Assert.False(vm.Detail.IsBusy);
                Assert.False(window!.DetailHeaderControl.RestoreButton.IsEnabled);
                Assert.True(window!.DetailHeaderControl.SaveButton.IsEnabled);
            });
        }
        finally
        {
            desktop.ReleaseGate();
            if (window is not null)
            {
                appFixture.Invoke(() => window.CloseForShutdown());
            }
        }
    }

    [Fact]
    public async Task FirstSaveWinsOverPreSavePollRaceAndDiscardedRefreshRetainsSavedArrangementKey()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        LayoutService service = CreateService(paths, store, desktop, monitors);

        // Hold the FIRST poll's UI application open AFTER it captured the old unsaved snapshot.
        var pollCapturedPreSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowPollApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int marshalCount = 0;

        var vm = new MainViewModel(
            store,
            desktop,
            monitors,
            new FixedClock(FixedNow),
            marshalToUiAsync: async action =>
            {
                int call = Interlocked.Increment(ref marshalCount);
                if (call == 1)
                {
                    pollCapturedPreSave.TrySetResult();
                    await allowPollApply.Task.ConfigureAwait(false);
                }

                appFixture.Invoke(action);
            },
            service: service,
            timeZone: TimeZoneInfo.Utc);

        // Start a poll while no layout is saved; pause it right before ApplySnapshot.
        Task stalePoll = vm.RefreshAsync();
        await pollCapturedPreSave.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Start a Save directly while the stale poll is still in flight; Save writes the file, then waits for stalePoll.
        Task<OperationResult?> saveTask = vm.Detail.RunOperationAsync(
            WindowOperationKind.Save,
            () => service.Save(),
            res => res.Done ? res.ArrangementKey : null);

        // Release the stale pre-save poll -> RefreshAfterOperationAsync must run a fresh post-save capture afterwards.
        allowPollApply.TrySetResult();
        await Task.WhenAll(stalePoll, saveTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(DetailKind.Active, vm.Detail.Kind);
        Assert.Equal("1 monitor", vm.Detail.Title);
        Assert.False(vm.Selected!.IsUnsaved);

        // Part B: Save arrangement A, next refresh is discarded (3 unstable monitor reads), then monitors switch to B
        // and refresh succeeds -> A's retained ArrangementKey must select A as Other!
        using var dataB = new TestData();
        BerthPaths pathsB = BerthPaths.ForRoot(dataB.Root);
        var storeB = new LayoutStore(pathsB);
        var flipMonitors = new UnstableOnceMonitors(MonDualA, MonSingle);
        var desktopB = new FakeDesktopIcons(new IconPosition("id-a", "Alpha", 50, 60));
        LayoutService serviceB = CreateService(pathsB, storeB, desktopB, flipMonitors);
        var vmB = CreateViewModel(storeB, desktopB, flipMonitors, serviceB);

        // Initial stable read on MonDualA (unsaved).
        await vmB.RefreshAsync();
        Assert.Equal(DetailKind.Unsaved, vmB.Detail.Kind);

        // Save A; arm 3 unstable monitor reads right AFTER Save writes so Save succeeds while the immediate post-save
        // refresh is discarded (returns null).
        LayoutService serviceB2 = new(
            desktopB,
            flipMonitors,
            storeB,
            pathsB,
            new OperationLock(pathsB, TimeSpan.FromSeconds(5)),
            new Log(pathsB, new FixedClock(FixedNow)),
            new FixedClock(FixedNow))
        {
            BeforeWrite = flipMonitors.ArmUnstableNextCapture,
        };
        var vmB2 = CreateViewModel(storeB, desktopB, flipMonitors, serviceB2);
        await vmB2.RefreshAsync();
        Assert.Equal(DetailKind.Unsaved, vmB2.Detail.Kind);

        OperationResult? saveRes = await vmB2.Detail.SaveAsync();
        Assert.NotNull(saveRes);
        Assert.True(saveRes.Done);
        Assert.False(vmB2.Detail.IsBusy);

        // Now connect B (MonSingle) and run a stable refresh: retained ArrangementKey of A must select A as Other!
        flipMonitors.SwitchToSecond();
        await vmB2.RefreshAsync();
        string keyA = ArrangementKey.From([MonDualA]);
        Assert.Equal(keyA, vmB2.Selected?.Key);
        Assert.Equal(DetailKind.Other, vmB2.Detail.Kind);
        Assert.Equal("1 monitor", vmB2.Detail.Title);
    }

    [Fact]
    public async Task MoreMenuListsThreeItemsInSpecOrderAndEnablesRevertOnlyWithPreviousSave()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);

        // First save -> no previous save yet.
        Assert.True(service.Save("Docked").Done);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w);
                DetailHeader header = w.DetailHeaderControl;

                header.MoreButton.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent));
                WpfContextMenu menu = Assert.IsType<WpfContextMenu>(header.MoreButton.ContextMenu);
                WpfMenuItem[] items = [.. menu.Items.OfType<WpfMenuItem>()];
                Assert.Equal(3, items.Length);
                Assert.Equal(["Rename", "Revert to previous save", "Delete"], items.Select(i => (string)i.Header));
                Assert.False(header.RevertMenuItem.IsEnabled);
                menu.IsOpen = false;
                return w;
            });

            // Second save -> now has a previous save!
            Assert.True(service.Save().Done);
            await vm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                DetailHeader header = window!.DetailHeaderControl;
                header.MoreButton.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent));
                Assert.True(header.RevertMenuItem.IsEnabled);
                header.MoreButton.ContextMenu!.IsOpen = false;
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
    public async Task MinWidthLongNameTrimsWithTooltipWrapsSubtitleAndMeets32PxControlHeight()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string longName = new('W', 40);
        SaveLayout(store, [MonDualA, MonDualB], longName, FixedNow, new IconPosition("id-1", "One", 10, 20));

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                MeasureAndArrange(window, 760, 520);
                DetailHeader header = window.DetailHeaderControl;

                Assert.Equal(TextTrimming.CharacterEllipsis, header.TitleText.TextTrimming);
                Assert.Equal(longName, header.TitleText.ToolTip);
                Assert.Equal(TextWrapping.Wrap, header.SubtitleText.TextWrapping);
                Assert.Equal(TextWrapping.Wrap, header.OtherArrangementText.TextWrapping);

                Assert.True(header.RestoreButton.ActualHeight >= 32d);
                Assert.True(header.SaveButton.ActualHeight >= 32d);
                Assert.True(header.MoreButton.ActualHeight >= 32d);
                Assert.True(header.MoreButton.ActualWidth >= 32d);

                Assert.Equal("Restore", AutomationProperties.GetName(header.RestoreButton));
                Assert.Equal("Save current layout", AutomationProperties.GetName(header.SaveButton));
                Assert.Equal("More options", AutomationProperties.GetName(header.MoreButton));
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    [Fact]
    [Trait("Category", "Render")]
    public async Task RendersAllFourSpec19HeaderStatesInLightAndDark()
    {
        string outputDir = ResolveRenderOutputDirectory();
        using var baselineData = new TestData();
        string emptyLightPath = Path.Combine(baselineData.Root, "empty-light.png");
        string emptyDarkPath = Path.Combine(baselineData.Root, "empty-dark.png");

        appFixture.Invoke(() =>
        {
            var emptyWin = new MainWindow();
            try
            {
                emptyWin.LayoutsHost.Content = null;
                emptyWin.DetailHost.Content = null;
                WindowRenderer.Render(emptyWin.RootGrid, 980, 640, emptyLightPath, RenderTheme.Light);
                WindowRenderer.Render(emptyWin.RootGrid, 980, 640, emptyDarkPath, RenderTheme.Dark);
            }
            finally
            {
                emptyWin.CloseForShutdown();
            }
        });

        byte[] emptyLightBytes = File.ReadAllBytes(emptyLightPath);
        byte[] emptyDarkBytes = File.ReadAllBytes(emptyDarkPath);

        // 1. header-active
        await RenderHeaderStateAsync(
            "header-active",
            outputDir,
            emptyLightBytes,
            emptyDarkBytes,
            currentMonitors: [MonDualA, MonDualB],
            seedLayouts: store =>
            {
                SaveLayout(store, [MonDualA, MonDualB], "Docked", FixedNow, new IconPosition("id-1", "One", 10, 20));
                SaveLayout(store, [MonSingle], "Laptop", FixedNow.AddDays(-1), new IconPosition("id-2", "Two", 30, 40));
            },
            selectIndex: 0,
            holdBusy: false,
            assertBeforeCapture: header =>
            {
                Assert.Equal("Docked", header.TitleText.Text);
                Assert.Equal("2560 \u00D7 1440 and 1920 \u00D7 1080 \u00B7 saved today, 09:14", header.SubtitleText.Text);
                Assert.Equal(Visibility.Visible, header.RestoreButton.Visibility);
                Assert.Equal(Visibility.Visible, header.SaveButton.Visibility);
                Assert.Equal(Visibility.Visible, header.MoreButton.Visibility);
                Assert.True(header.RestoreButton.IsEnabled);
                Assert.True(header.SaveButton.IsEnabled);
                Assert.True(header.MoreButton.IsEnabled);
            });

        // 2. header-unsaved
        await RenderHeaderStateAsync(
            "header-unsaved",
            outputDir,
            emptyLightBytes,
            emptyDarkBytes,
            currentMonitors: [MonSingle],
            seedLayouts: store => SaveLayout(store, [MonDualA, MonDualB], "Docked", FixedNow, new IconPosition("id-1", "One", 10, 20)),
            selectIndex: 0,
            holdBusy: false,
            assertBeforeCapture: header =>
            {
                Assert.Equal("Not saved yet", header.TitleText.Text);
                Assert.Equal("1920 \u00D7 1080", header.SubtitleText.Text);
                Assert.Equal(Visibility.Collapsed, header.RestoreButton.Visibility);
                Assert.Equal(Visibility.Visible, header.SaveButton.Visibility);
                Assert.Equal(Visibility.Collapsed, header.MoreButton.Visibility);
            });

        // 3. header-other
        await RenderHeaderStateAsync(
            "header-other",
            outputDir,
            emptyLightBytes,
            emptyDarkBytes,
            currentMonitors: [MonSingle],
            seedLayouts: store => SaveLayout(store, [MonDualA, MonDualB], "Docked", FixedNow, new IconPosition("id-1", "One", 10, 20)),
            selectIndex: 1,
            holdBusy: false,
            assertBeforeCapture: header =>
            {
                Assert.Equal("Docked", header.TitleText.Text);
                Assert.Equal("2560 \u00D7 1440 and 1920 \u00D7 1080 \u00B7 saved today, 09:14", header.SubtitleText.Text);
                Assert.Equal(Visibility.Collapsed, header.RestoreButton.Visibility);
                Assert.Equal(Visibility.Collapsed, header.SaveButton.Visibility);
                Assert.Equal(Visibility.Visible, header.MoreButton.Visibility);
                Assert.Equal(Visibility.Visible, header.OtherArrangementText.Visibility);
                Assert.Equal(DetailViewModel.OtherArrangementSentence, header.OtherArrangementText.Text);
            });

        // 4. header-busy
        await RenderHeaderStateAsync(
            "header-busy",
            outputDir,
            emptyLightBytes,
            emptyDarkBytes,
            currentMonitors: [MonDualA, MonDualB],
            seedLayouts: store => SaveLayout(store, [MonDualA, MonDualB], "Docked", FixedNow, new IconPosition("id-1", "One", 10, 20)),
            selectIndex: 0,
            holdBusy: true,
            assertBeforeCapture: header =>
            {
                Assert.Equal("Docked", header.TitleText.Text);
                Assert.Equal(Visibility.Visible, header.RestoreButton.Visibility);
                Assert.Equal(Visibility.Visible, header.SaveButton.Visibility);
                Assert.Equal(Visibility.Visible, header.MoreButton.Visibility);
                Assert.False(header.RestoreButton.IsEnabled);
                Assert.False(header.SaveButton.IsEnabled);
                Assert.False(header.MoreButton.IsEnabled);
            });
    }

    private async Task RenderHeaderStateAsync(
        string stateName,
        string outputDir,
        byte[] emptyLightBytes,
        byte[] emptyDarkBytes,
        MonitorInfo[] currentMonitors,
        Action<LayoutStore> seedLayouts,
        int selectIndex,
        bool holdBusy,
        Action<DetailHeader> assertBeforeCapture)
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        seedLayouts(store);

        var monitors = new FakeMonitors(currentMonitors);
        var innerDesktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var desktop = new GatedDesktopIcons(innerDesktop);
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        string lightPath = Path.Combine(outputDir, $"{stateName}-light.png");
        string darkPath = Path.Combine(outputDir, $"{stateName}-dark.png");

        Task<OperationResult?>? busyTask = null;
        if (holdBusy)
        {
            desktop.ArmGate();
            busyTask = vm.Detail.SaveAsync();
            await desktop.EnteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        try
        {
            appFixture.Invoke(() =>
            {
                var window = new MainWindow { DataContext = vm };
                try
                {
                    MeasureAndArrange(window);
                    if (selectIndex > 0)
                    {
                        var pane = (LayoutsPane)window.LayoutsHost.Content;
                        pane.LayoutsList.SelectedIndex = selectIndex;
                        MeasureAndArrange(window);
                    }

                    assertBeforeCapture(window.DetailHeaderControl);
                    WindowRenderer.Render(window.RootGrid, 980, 640, lightPath, RenderTheme.Light);
                    WindowRenderer.Render(window.RootGrid, 980, 640, darkPath, RenderTheme.Dark);
                }
                finally
                {
                    window.CloseForShutdown();
                }
            });
        }
        finally
        {
            if (holdBusy)
            {
                desktop.ReleaseGate();
                if (busyTask is not null)
                {
                    await busyTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                }
            }
        }

        AssertValidDistinctPng(lightPath, emptyLightBytes);
        AssertValidDistinctPng(darkPath, emptyDarkBytes);
    }

    private static LayoutService CreateService(
        BerthPaths paths,
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors) =>
        new(
            desktop,
            monitors,
            store,
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(5)),
            new Log(paths, new FixedClock(FixedNow)),
            new FixedClock(FixedNow));

    private MainViewModel CreateViewModel(
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors,
        LayoutService service) =>
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

    private static void SaveLayout(
        LayoutStore store,
        MonitorInfo[] monitors,
        string name,
        DateTimeOffset savedAt,
        params IconPosition[] icons)
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

        string repoRoot = FindRepoRoot();
        string dir = Path.Combine(repoRoot, "build", "renders");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Berth.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Berth.slnx above " + AppContext.BaseDirectory);
    }

    private static void AssertValidDistinctPng(string path, byte[] emptyBaselineBytes)
    {
        Assert.True(File.Exists(path), $"Expected render output at {path}");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0);
        Assert.False(bytes.AsSpan().SequenceEqual(emptyBaselineBytes), $"Render {path} matched the empty frame baseline.");

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
        private int _readCount;

        public TaskCompletionSource EnteredGate { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount => Volatile.Read(ref _readCount);

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
            Interlocked.Increment(ref _readCount);
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

    private sealed class UnstableOnceMonitors(MonitorInfo first, MonitorInfo second) : IMonitors
    {
        private int _unstableRemaining;
        private bool _flip;
        private bool _useSecond;

        public void ArmUnstableNextCapture() => _unstableRemaining = 10;

        public void SwitchToSecond()
        {
            _unstableRemaining = 0;
            _useSecond = true;
        }

        public IReadOnlyList<MonitorInfo> Read()
        {
            if (_unstableRemaining > 0)
            {
                _unstableRemaining--;
                _flip = !_flip;
                return _flip ? [first] : [second];
            }

            return _useSecond ? [second] : [first];
        }
    }
}
