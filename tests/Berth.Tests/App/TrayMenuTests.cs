using System.ComponentModel;
using System.Windows.Forms;
using Berth.App;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.Fakes;

namespace Berth.Tests.App;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class TrayMenuTests(AppFixture appFixture)
{
    private static readonly MonitorInfo PrimaryMonitor = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo SecondaryMonitor = new(1920, 0, 2560, 1440, 100, false);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public void MenuHasSpecOrderSeparatorBoldDefaultAndSystemRenderer()
    {
        using var env = new TestHarness(appFixture);
        appFixture.Invoke(() =>
        {
            ContextMenuStrip strip = env.Menu.Strip;
            Assert.Equal(ToolStripRenderMode.System, strip.RenderMode);
            Assert.Equal(6, strip.Items.Count);
            Assert.Equal("Save layout", strip.Items[0].Text);
            Assert.Equal("Restore layout", strip.Items[1].Text);
            Assert.Equal("Restore automatically", strip.Items[2].Text);
            Assert.IsType<ToolStripSeparator>(strip.Items[3]);
            Assert.Equal("Open Berth", strip.Items[4].Text);
            Assert.True(strip.Items[4].Font.Bold);
            Assert.Equal("Exit", strip.Items[5].Text);

            var autoItem = Assert.IsType<ToolStripMenuItem>(strip.Items[2]);
            Assert.False(autoItem.CheckOnClick);
        });
    }

    [Fact]
    public async Task OpeningRefreshesRestoreEnabledAndAutoCheckedOnSameMenuInstance()
    {
        using var env = new TestHarness(appFixture);
        CancellationToken ct = TestContext.Current.CancellationToken;

        // 1. No saved layout, default setting (false).
        await env.OpenAndWaitAsync(ct);
        appFixture.Invoke(() =>
        {
            Assert.False(env.Menu.RestoreItem.Enabled);
            Assert.False(env.Menu.RestoreAutomaticallyItem.Checked);
        });

        // 2. Save a layout for current arrangement; auto arrange off -> Restore enabled.
        OperationResult saved = env.Service.Save("Docked");
        Assert.Equal(OperationKind.Saved, saved.Kind);
        await env.OpenAndWaitAsync(ct);
        appFixture.Invoke(() =>
        {
            Assert.True(env.Menu.RestoreItem.Enabled);
            Assert.False(env.Menu.RestoreAutomaticallyItem.Checked);
        });

        // 3. Auto arrange on -> Restore disabled.
        env.Desktop.SetAutoArrange(true);
        await env.OpenAndWaitAsync(ct);
        Assert.False(appFixture.Invoke(() => env.Menu.RestoreItem.Enabled));

        // 4. Auto arrange throws DesktopUnavailableException -> Restore disabled.
        env.Desktop.SetAutoArrange(false);
        env.Desktop.Unavailable = true;
        await env.OpenAndWaitAsync(ct);
        Assert.False(appFixture.Invoke(() => env.Menu.RestoreItem.Enabled));

        // 5. Desktop recovers, auto arrange off, setting persisted true -> Restore enabled and checked.
        env.Desktop.Unavailable = false;
        env.SettingsStore.Save(Settings.Default with { RestoreAutomatically = true });
        await env.OpenAndWaitAsync(ct);
        appFixture.Invoke(() =>
        {
            Assert.True(env.Menu.RestoreItem.Enabled);
            Assert.True(env.Menu.RestoreAutomaticallyItem.Checked);
        });

        // 6. Corrupt settings.json -> reopening resets check mark to effective default (false).
        File.WriteAllText(env.SettingsStore.FilePath, "{ broken json");
        await env.OpenAndWaitAsync(ct);
        Assert.False(appFixture.Invoke(() => env.Menu.RestoreAutomaticallyItem.Checked));

        // 7. Arrangement changes to one without a saved layout -> Restore disabled on the same menu instance.
        env.Monitors.Monitors = [PrimaryMonitor, SecondaryMonitor];
        await env.OpenAndWaitAsync(ct);
        Assert.False(appFixture.Invoke(() => env.Menu.RestoreItem.Enabled));
    }

    [Fact]
    public async Task OpeningReadsAutoArrangeOffUiThreadAndDiscardsSupersededOpening()
    {
        using var env = new TestHarness(appFixture);
        CancellationToken ct = TestContext.Current.CancellationToken;
        int uiThreadId = appFixture.Invoke(() => Environment.CurrentManagedThreadId);
        Assert.Equal(OperationKind.Saved, env.Service.Save("Docked").Kind);

        using var firstEntered = new ManualResetEventSlim(false);
        using var firstRelease = new ManualResetEventSlim(false);
        int callCount = 0;

        env.RecordingDesktop.BeforeAutoArrange = () =>
        {
            if (Interlocked.Increment(ref callCount) == 1)
            {
                firstEntered.Set();
                if (!firstRelease.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("First AutoArrange check was never released.");
                }
            }
        };

        // First opening blocks on AutoArrange off-thread while UI thread returns immediately with Restore disabled.
        Task firstRefresh = appFixture.Invoke(() =>
        {
            env.RaiseOpening();
            Assert.False(env.Menu.RestoreItem.Enabled);
            return env.Menu.LastRefreshTask;
        });

        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5), ct));
        Assert.NotEqual(0, env.RecordingDesktop.LastAutoArrangeThreadId);
        Assert.NotEqual(uiThreadId, env.RecordingDesktop.LastAutoArrangeThreadId);

        // Supersede with a second opening where AutoArrange is now true -> Restore must remain disabled even after first completes.
        env.Desktop.SetAutoArrange(true);
        Task secondRefresh = appFixture.Invoke(() =>
        {
            env.RaiseOpening();
            return env.Menu.LastRefreshTask;
        });
        await secondRefresh.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.False(appFixture.Invoke(() => env.Menu.RestoreItem.Enabled));

        // Release the stale first opening (which saw AutoArrange == false before the change? SetAutoArrange(false) wasn't read yet,
        // so flip back to false before releasing first so first returns canRestore=true and would wrongly enable Restore if not discarded).
        env.Desktop.SetAutoArrange(false);
        firstRelease.Set();
        await firstRefresh.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.False(appFixture.Invoke(() => env.Menu.RestoreItem.Enabled));
    }

    [Fact]
    public async Task SaveAndRestoreRunOffUiThreadDeliverCompletionOnUiThreadAndRefreshTooltip()
    {
        using var env = new TestHarness(appFixture);
        int uiThreadId = appFixture.Invoke(() => Environment.CurrentManagedThreadId);
        var theme = new TaskbarTheme(@"Software\Berth.Tests\NonExistentTrayMenuTheme");

        TrayIcon? icon = null;
        try
        {
            icon = appFixture.Invoke(() =>
            {
                var created = new TrayIcon(theme, initialActive: null, FixedNow, TimeZoneInfo.Utc);
                env.Menu.OperationFinished += _ => created.RefreshTooltip(env.Store, env.Monitors, env.Clock, TimeZoneInfo.Utc);
                return created;
            });

            Assert.Equal("Berth: no layout for this monitor arrangement", appFixture.Invoke(() => icon.TooltipText));

            // Save via menu click.
            var saveCompleted = new TaskCompletionSource<(OperationResult Result, int CallbackThreadId)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            appFixture.Invoke(() =>
            {
                void Handler(OperationResult r)
                {
                    env.Menu.OperationFinished -= Handler;
                    saveCompleted.TrySetResult((r, Environment.CurrentManagedThreadId));
                }

                env.Menu.OperationFinished += Handler;
                env.Menu.SaveItem.PerformClick();
            });

            (OperationResult saveResult, int saveCallbackThread) = await saveCompleted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.Equal(OperationKind.Saved, saveResult.Kind);
            Assert.Equal(uiThreadId, saveCallbackThread);
            Assert.NotEqual(0, env.RecordingDesktop.LastReadThreadId);
            Assert.NotEqual(uiThreadId, env.RecordingDesktop.LastReadThreadId);
            Assert.NotNull(env.Store.Find(ArrangementKey.From([PrimaryMonitor])));
            Assert.Equal("Berth: 1 monitor, saved today, 09:14", appFixture.Invoke(() => icon.TooltipText));

            // Move the fake desktop icon, then Restore via menu click.
            ((IDesktopIcons)env.Desktop).SetPositions([new IconPosition("id-1", "One", 500, 600)]);
            var restoreCompleted = new TaskCompletionSource<(OperationResult Result, int CallbackThreadId)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            appFixture.Invoke(() =>
            {
                void Handler(OperationResult r)
                {
                    env.Menu.OperationFinished -= Handler;
                    restoreCompleted.TrySetResult((r, Environment.CurrentManagedThreadId));
                }

                env.Menu.OperationFinished += Handler;
                env.Menu.RestoreItem.PerformClick();
            });

            (OperationResult restoreResult, int restoreCallbackThread) = await restoreCompleted.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.Equal(OperationKind.Restored, restoreResult.Kind);
            Assert.Equal(uiThreadId, restoreCallbackThread);
            Assert.NotEqual(0, env.RecordingDesktop.LastSetThreadId);
            Assert.NotEqual(uiThreadId, env.RecordingDesktop.LastSetThreadId);

            IReadOnlyList<IconPosition> afterRestore = ((IDesktopIcons)env.Desktop).ReadIcons();
            Assert.Equal(100, afterRestore[0].X);
            Assert.Equal(200, afterRestore[0].Y);
        }
        finally
        {
            if (icon is not null)
            {
                appFixture.Invoke(icon.Dispose);
            }
        }
    }

    [Fact]
    public async Task RestoreAutomaticallyTogglesBothWaysAndPersistsAcrossReopen()
    {
        using var env = new TestHarness(appFixture);
        CancellationToken ct = TestContext.Current.CancellationToken;

        await env.OpenAndWaitAsync(ct);
        appFixture.Invoke(() =>
        {
            Assert.False(env.Menu.RestoreAutomaticallyItem.Checked);

            // Toggle off -> on.
            env.Menu.RestoreAutomaticallyItem.PerformClick();
            Assert.True(env.Menu.RestoreAutomaticallyItem.Checked);
            Assert.True(env.SettingsStore.Load().Value.RestoreAutomatically);
        });

        // Reopen and confirm persisted on.
        await env.OpenAndWaitAsync(ct);
        appFixture.Invoke(() =>
        {
            Assert.True(env.Menu.RestoreAutomaticallyItem.Checked);

            // Toggle on -> off.
            env.Menu.RestoreAutomaticallyItem.PerformClick();
            Assert.False(env.Menu.RestoreAutomaticallyItem.Checked);
            Assert.False(env.SettingsStore.Load().Value.RestoreAutomatically);
        });

        // Reopen and confirm persisted off.
        await env.OpenAndWaitAsync(ct);
        Assert.False(appFixture.Invoke(() => env.Menu.RestoreAutomaticallyItem.Checked));

        string logText = File.ReadAllText(env.Log.FilePath);
        Assert.Contains("Restore automatically", logText, StringComparison.Ordinal);
        Assert.Contains("INFO", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreAutomaticallyLeavesCheckUnchangedWhenStoreFails()
    {
        using var env = new TestHarness(appFixture);
        BerthPaths.EnsureFolderFor(env.SettingsStore.FilePath);
        File.WriteAllText(env.SettingsStore.FilePath, "{ not valid json");

        appFixture.Invoke(() =>
        {
            Assert.False(env.Menu.RestoreAutomaticallyItem.Checked);
            env.Menu.RestoreAutomaticallyItem.PerformClick();
            Assert.False(env.Menu.RestoreAutomaticallyItem.Checked);
        });

        string logText = File.ReadAllText(env.Log.FilePath);
        Assert.Contains("FAIL", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenBerthRaisesOpenRequestedOnce()
    {
        using var env = new TestHarness(appFixture);
        int openCount = 0;
        appFixture.Invoke(() =>
        {
            env.Menu.OpenRequested += () => openCount++;
            env.Menu.OpenItem.PerformClick();
        });

        Assert.Equal(1, openCount);
    }

    [Fact]
    public async Task LateAutoArrangeLogsWarnAndDeliversRefusedWithoutException()
    {
        using var env = new TestHarness(appFixture);
        CancellationToken ct = TestContext.Current.CancellationToken;
        Assert.Equal(OperationKind.Saved, env.Service.Save("Docked").Kind);

        await env.OpenAndWaitAsync(ct);
        var completed = new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        appFixture.Invoke(() =>
        {
            Assert.True(env.Menu.RestoreItem.Enabled);

            // Force auto arrange on after Opening and before clicking Restore.
            env.Desktop.SetAutoArrange(true);
            env.Menu.OperationFinished += r => completed.TrySetResult(r);
            env.Menu.RestoreItem.PerformClick();
        });

        OperationResult result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(OperationKind.Refused, result.Kind);
        Assert.Equal(Refusal.AutoArrange, result.Refusal);

        string logText = File.ReadAllText(env.Log.FilePath);
        Assert.Contains("WARN", logText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveWriteFailureLogsOneFailAndDeliversOneFailedOnUiThread()
    {
        using var env = new TestHarness(appFixture);
        CancellationToken ct = TestContext.Current.CancellationToken;
        int uiThreadId = appFixture.Invoke(() => Environment.CurrentManagedThreadId);
        string key = ArrangementKey.From([PrimaryMonitor]);
        string layoutPath = env.Paths.LayoutFile(key);
        Directory.CreateDirectory(layoutPath); // Directory at the file path forces IOException on write.

        int completionCount = 0;
        var completed = new TaskCompletionSource<(OperationResult Result, int ThreadId)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        appFixture.Invoke(() =>
        {
            env.Menu.OperationFinished += r =>
            {
                Interlocked.Increment(ref completionCount);
                completed.TrySetResult((r, Environment.CurrentManagedThreadId));
            };
            env.Menu.SaveItem.PerformClick();
        });

        (OperationResult result, int callbackThreadId) = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        // Pump the UI dispatcher once more to verify no duplicate completion was queued.
        appFixture.Invoke(() => { });

        Assert.Equal(OperationKind.Failed, result.Kind);
        Assert.Equal(uiThreadId, callbackThreadId);
        Assert.Equal(1, Volatile.Read(ref completionCount));

        string[] failLines = File.ReadAllLines(env.Log.FilePath)
            .Where(line => line.Contains("FAIL", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(failLines);
    }

    [Fact]
    public void AttachedMenuSaveRefreshesTooltipAndExitDrainsInFlightOperationWithExitCodeZero()
    {
        using var temp = new TestData();
        string reportFile = Path.Combine(temp.Root, "menu-report.txt");
        string instanceName = $"Berth.Tests.MenuDrain.{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.MenuSaveAndExitDrainScenario,
            temp.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. stdout={stdout} stderr={stderr}");
        Assert.True(File.Exists(reportFile), "Expected child scenario report file.");

        Dictionary<string, string> report = File.ReadAllLines(reportFile)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

        Assert.Equal("True", report["attachedToNotifyIcon"]);
        Assert.Equal("Berth: no layout for this monitor arrangement", report["initialTooltip"]);
        Assert.Equal("Berth: 1 monitor, saved today, 09:14", report["tooltipAfterSave"]);
        Assert.Equal("1", report["completedCountBeforeRelease"]);
        Assert.Equal("True", report["iconVisibleBeforeRelease"]);
        Assert.Equal("False", report["exitedBeforeRelease"]);
        Assert.Equal("2", report["completedCount"]);
        Assert.Equal("False", report["visibleAfterExit"]);
        Assert.Equal("0", report["exitCode"]);
    }

    private sealed class TestHarness : IDisposable
    {
        private readonly AppFixture _appFixture;
        private readonly TestData _temp = new();

        public TestHarness(AppFixture appFixture)
        {
            _appFixture = appFixture;
            Paths = BerthPaths.ForRoot(_temp.Root);
            Clock = new FixedClock(FixedNow);
            Log = new Log(Paths, Clock);
            Monitors = new FakeMonitors(PrimaryMonitor);
            Desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 200));
            RecordingDesktop = new ThreadRecordingDesktop(Desktop);
            Store = new LayoutStore(Paths);
            SettingsStore = new SettingsStore(Paths);
            Gate = OperationLock.ForApp(Paths);
            Service = new LayoutService(RecordingDesktop, Monitors, Store, Paths, Gate, Log, Clock);
            Menu = _appFixture.Invoke(() => new TrayMenu(
                Service,
                Store,
                Monitors,
                RecordingDesktop,
                SettingsStore,
                Log,
                action => _appFixture.Invoke(action)));
        }

        public BerthPaths Paths { get; }
        public FixedClock Clock { get; }
        public Log Log { get; }
        public FakeMonitors Monitors { get; }
        public FakeDesktopIcons Desktop { get; }
        public ThreadRecordingDesktop RecordingDesktop { get; }
        public LayoutStore Store { get; }
        public SettingsStore SettingsStore { get; }
        public OperationLock Gate { get; }
        public LayoutService Service { get; }
        public TrayMenu Menu { get; }

        public void RaiseOpening()
        {
            var args = new CancelEventArgs(false);
            typeof(ContextMenuStrip)
                .GetMethod("OnOpening", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(Menu.Strip, [args]);
        }

        public async Task OpenAndWaitAsync(CancellationToken ct)
        {
            Task refreshTask = _appFixture.Invoke(() =>
            {
                RaiseOpening();
                return Menu.LastRefreshTask;
            });
            await refreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }

        public void Dispose()
        {
            _appFixture.Invoke(Menu.Dispose);
            _temp.Dispose();
        }
    }

    private sealed class ThreadRecordingDesktop(IDesktopIcons inner) : IDesktopIcons
    {
        public int LastReadThreadId { get; private set; }
        public int LastSetThreadId { get; private set; }
        public int LastAutoArrangeThreadId { get; private set; }
        public Action? BeforeAutoArrange { get; set; }

        public bool AutoArrange
        {
            get
            {
                LastAutoArrangeThreadId = Environment.CurrentManagedThreadId;
                BeforeAutoArrange?.Invoke();
                return inner.AutoArrange;
            }
        }

        public IReadOnlyList<IconPosition> ReadIcons()
        {
            LastReadThreadId = Environment.CurrentManagedThreadId;
            return inner.ReadIcons();
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons)
        {
            LastSetThreadId = Environment.CurrentManagedThreadId;
            inner.SetPositions(icons);
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove)
        {
            LastSetThreadId = Environment.CurrentManagedThreadId;
            inner.SetPositions(icons, beforeMove);
        }

        public bool TryTurnAutoArrangeOff() => inner.TryTurnAutoArrangeOff();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
