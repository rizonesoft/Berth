using Berth.App;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.App;

public sealed class AutoRestoreTests
{
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FiveStaggeredTriggersInBurstProduceExactlyOneRestoreAfterLastDeadline()
    {
        using var data = new TestData();
        (LayoutService service, SettingsStore settings, Log log, FakeDesktopIcons desktop, _, BerthPaths paths) = CreateFixture(data, restoreAuto: true, seedLayout: true);
        var clock = new ManualClock(BaseTime);
        var timers = new FakeTimerFactory(clock);
        var finished = new List<(OperationResult Result, string Reason, bool Silent)>();

        using var auto = new AutoRestore(service, settings, log, timers, TimeSpan.FromSeconds(3), clock, SynchronousRunner);
        auto.Finished += (r, reason, silent) => finished.Add((r, reason, silent));

        // Stagger 5 triggers at t=0, 1, 2, 3, 4s (first deadline would have been t=3s; last deadline is t=7s).
        for (int i = 0; i < 5; i++)
        {
            auto.Trigger("display change");
            if (i < 4)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                timers.FireDue();
            }
        }

        // At t=4s (past the 1st trigger's t=3s deadline!), zero restores must have run!
        Assert.Empty(finished);
        Assert.Empty(desktop.Calls);

        // Advance to t=6.9s -> still zero restores.
        clock.Advance(TimeSpan.FromSeconds(2.9));
        timers.FireDue();
        Assert.Empty(finished);

        // Advance past t=7s (3s after the 5th trigger) -> exactly ONE restore!
        clock.Advance(TimeSpan.FromSeconds(0.2));
        timers.FireDue();

        (OperationResult res, string reason, bool silent) = Assert.Single(finished);
        Assert.Equal(OperationKind.Restored, res.Kind);
        Assert.Equal("display change", reason);
        Assert.False(silent);
        Assert.Single(desktop.Calls);
        string[] logLines = File.ReadAllLines(paths.LogFile);
        Assert.Single(logLines);
        Assert.EndsWith("(automatic, display change)", logLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void CancelledStaleTimerCallbackLeavesReplacementArmedAndDoesNothing()
    {
        using var data = new TestData();
        (LayoutService service, SettingsStore settings, Log log, FakeDesktopIcons desktop, _, BerthPaths paths) = CreateFixture(data, restoreAuto: true, seedLayout: true);
        var clock = new ManualClock(BaseTime);
        var timers = new FakeTimerFactory(clock);
        var finished = new List<(OperationResult, string, bool)>();

        using var auto = new AutoRestore(service, settings, log, timers, TimeSpan.FromSeconds(3), clock, SynchronousRunner);
        auto.Finished += (r, reason, silent) => finished.Add((r, reason, silent));

        auto.Trigger("display change");
        FakeTimerFactory.FakeTimer firstTimer = timers.Created[0];
        clock.Advance(TimeSpan.FromSeconds(1));
        auto.Trigger("Explorer restart");
        FakeTimerFactory.FakeTimer secondTimer = timers.Created[1];

        Assert.True(firstTimer.Cancelled);
        // Force the stale first timer's callback to run anyway while secondTimer is pending!
        firstTimer.ForceInvokeCallback();

        Assert.Empty(finished);
        Assert.Empty(desktop.Calls);
        Assert.False(File.Exists(paths.LogFile));
        Assert.False(secondTimer.Cancelled);

        // Replacement timer still fires cleanly at its own deadline.
        clock.Advance(TimeSpan.FromSeconds(3));
        timers.FireDue();
        Assert.Single(finished);
        Assert.Equal("Explorer restart", finished[0].Item2);
    }

    [Fact]
    public void OffSettingDoesNothingAndLogsNothingWhileRuntimeFlipToOnTakesEffectImmediately()
    {
        using var data = new TestData();
        (LayoutService service, SettingsStore settings, Log log, FakeDesktopIcons desktop, _, BerthPaths paths) = CreateFixture(data, restoreAuto: false, seedLayout: true);
        var clock = new ManualClock(BaseTime);
        var timers = new FakeTimerFactory(clock);
        var finished = new List<(OperationResult, string, bool)>();

        using var auto = new AutoRestore(service, settings, log, timers, TimeSpan.FromSeconds(3), clock, SynchronousRunner);
        auto.Finished += (r, reason, silent) => finished.Add((r, reason, silent));

        auto.Trigger("display change");
        clock.Advance(TimeSpan.FromSeconds(3));
        timers.FireDue();

        Assert.Empty(finished);
        Assert.Empty(desktop.Calls);
        Assert.False(File.Exists(paths.LogFile));

        // Flip setting ON at runtime without recreating AutoRestore!
        settings.Save(Settings.Default with { RestoreAutomatically = true });
        auto.Trigger("display change");
        clock.Advance(TimeSpan.FromSeconds(3));
        timers.FireDue();

        Assert.Single(finished);
        Assert.Single(desktop.Calls);
        string[] lines = File.ReadAllLines(paths.LogFile);
        Assert.Single(lines);
        Assert.EndsWith("(automatic, display change)", lines[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoLayoutForCurrentArrangementIsSilentCreatesNoLayoutAndLeavesUndoUntouched(bool autoArrangeOn)
    {
        using var data = new TestData();
        (LayoutService service, SettingsStore settings, Log log, FakeDesktopIcons desktop, _, BerthPaths paths) = CreateFixture(data, restoreAuto: true, seedLayout: false);
        desktop.SetAutoArrange(autoArrangeOn);

        // Seed an unrelated undo.json that must remain untouched.
        BerthPaths.EnsureFolderFor(paths.UndoFile);
        File.WriteAllText(paths.UndoFile, "{\"sentinel\":true}");

        var clock = new ManualClock(BaseTime);
        var timers = new FakeTimerFactory(clock);
        var finished = new List<(OperationResult Result, string Reason, bool Silent)>();

        using var auto = new AutoRestore(service, settings, log, timers, TimeSpan.FromSeconds(3), clock, SynchronousRunner);
        auto.Finished += (r, reason, silent) => finished.Add((r, reason, silent));

        auto.Trigger("display change");
        clock.Advance(TimeSpan.FromSeconds(3));
        timers.FireDue();

        (OperationResult res, string reason, bool silent) = Assert.Single(finished);
        Assert.Equal(OperationKind.Refused, res.Kind);
        Assert.Equal(Refusal.NoLayout, res.Refusal);
        Assert.Equal("display change", reason);
        Assert.True(silent);
        Assert.Empty(desktop.Calls);
        Assert.False(Directory.Exists(paths.LayoutsFolder) && Directory.GetFiles(paths.LayoutsFolder, "*.json").Length > 0);
        Assert.Equal("{\"sentinel\":true}", File.ReadAllText(paths.UndoFile));
        string[] lines = File.ReadAllLines(paths.LogFile);
        Assert.Single(lines);
        Assert.Contains(" WARN ", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("(automatic, display change)", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void SavedLayoutWithAutoArrangeOnDeliversNonSilentAutoArrangeRefusalWithSingleWarnLine()
    {
        using var data = new TestData();
        (LayoutService service, SettingsStore settings, Log log, FakeDesktopIcons desktop, _, BerthPaths paths) = CreateFixture(data, restoreAuto: true, seedLayout: true);
        desktop.SetAutoArrange(true);

        var clock = new ManualClock(BaseTime);
        var timers = new FakeTimerFactory(clock);
        var finished = new List<(OperationResult Result, string Reason, bool Silent)>();

        using var auto = new AutoRestore(service, settings, log, timers, TimeSpan.FromSeconds(3), clock, SynchronousRunner);
        auto.Finished += (r, reason, silent) => finished.Add((r, reason, silent));

        auto.Trigger("display change");
        clock.Advance(TimeSpan.FromSeconds(3));
        timers.FireDue();

        (OperationResult res, string reason, bool silent) = Assert.Single(finished);
        Assert.Equal(OperationKind.Refused, res.Kind);
        Assert.Equal(Refusal.AutoArrange, res.Refusal);
        Assert.Equal("display change", reason);
        Assert.False(silent);
        Assert.Empty(desktop.Calls);
        string[] lines = File.ReadAllLines(paths.LogFile);
        Assert.Single(lines);
        Assert.Contains(" WARN ", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("(automatic, display change)", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MidRestoreTriggerSerializesAndMeasuresQuietTimeFromLastTriggerOnBothSidesOfDeadline()
    {
        using var data = new TestData();
        (LayoutService service, SettingsStore settings, Log log, FakeDesktopIcons desktop, _, _) = CreateFixture(data, restoreAuto: true, seedLayout: true);
        var clock = new ManualClock(BaseTime);
        var timers = new FakeTimerFactory(clock);
        var finished = new List<(OperationResult Result, string Reason, bool Silent)>();

        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int activeConcurrent = 0;
        int maxConcurrent = 0;
        bool gateNext = true;

        using var auto = new AutoRestore(
            service,
            settings,
            log,
            timers,
            TimeSpan.FromSeconds(3),
            clock,
            work => Task.Run(async () =>
            {
                int now = Interlocked.Increment(ref activeConcurrent);
                maxConcurrent = Math.Max(maxConcurrent, now);
                try
                {
                    if (gateNext)
                    {
                        gateNext = false;
                        entered.TrySetResult();
                        await gate.Task.ConfigureAwait(false);
                    }

                    await work().ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref activeConcurrent);
                }
            }));

        TaskCompletionSource firstFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        auto.Finished += (r, reason, silent) =>
        {
            lock (finished)
            {
                finished.Add((r, reason, silent));
                if (finished.Count == 1)
                {
                    firstFinished.TrySetResult();
                }
                else if (finished.Count == 2)
                {
                    secondFinished.TrySetResult();
                }
            }
        };

        // Case A: first restore finishes BEFORE the mid-restore trigger's deadline -> waits ONLY remaining time!
        auto.Trigger("display change");
        clock.Advance(TimeSpan.FromSeconds(3)); // t=3s
        timers.FireDue();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Mid-restore trigger at t=4s with 5s interval -> deadline is t=9s!
        clock.Advance(TimeSpan.FromSeconds(1)); // t=4s
        auto.Trigger("Explorer restart", TimeSpan.FromSeconds(5));

        // Jump UTC wall clock forward by +2 hours while the restore runs: monotonic quiet time must NOT be affected!
        clock.AdvanceWallClock(TimeSpan.FromHours(2));

        // First restore finishes at t=6s monotonic (3s remaining until t=9s).
        clock.Advance(TimeSpan.FromSeconds(2)); // t=6s
        gate.TrySetResult();
        await firstFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await timers.WaitForTimerCountAsync(2, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Follow-up timer must be armed for 3s (remaining to t=9s), NOT a full 5s and NOT 0s from the +2h UTC jump!
        Assert.Equal(2, timers.Created.Count);
        Assert.Equal(TimeSpan.FromSeconds(3), timers.Created[1].DueTime);

        clock.Advance(TimeSpan.FromSeconds(3)); // t=9s
        timers.FireDue();
        await secondFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, maxConcurrent);
        Assert.Equal(2, finished.Count);
        Assert.Equal("display change", finished[0].Reason);
        Assert.Equal("Explorer restart", finished[1].Reason);

        // Case B: first restore finishes AFTER the mid-restore trigger's deadline -> follow-up arms with TimeSpan.Zero!
        gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource thirdFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource fourthFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        auto.Finished += (_, _, _) =>
        {
            lock (finished)
            {
                if (finished.Count == 3)
                {
                    thirdFinished.TrySetResult();
                }
                else if (finished.Count == 4)
                {
                    fourthFinished.TrySetResult();
                }
            }
        };
        gateNext = true;
        auto.Trigger("display change");
        clock.Advance(TimeSpan.FromSeconds(3));
        timers.FireDue();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        auto.Trigger("Explorer restart", TimeSpan.FromSeconds(2));
        // Advance 5 seconds monotonic past the 2-second deadline while restore is still running!
        clock.Advance(TimeSpan.FromSeconds(5));
        gate.TrySetResult();
        await thirdFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await timers.WaitForTimerCountAsync(4, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.Zero, timers.Created[^1].DueTime);
        timers.FireDue();
        await fourthFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, maxConcurrent);
        Assert.Equal("Explorer restart", finished[3].Reason);
    }

    private static Task SynchronousRunner(Func<Task> work) => work();

    private static (LayoutService Service, SettingsStore Settings, Log Log, FakeDesktopIcons Desktop, FakeMonitors Monitors, BerthPaths Paths)
        CreateFixture(TestData data, bool restoreAuto, bool seedLayout)
    {
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var settings = new SettingsStore(paths);
        settings.Save(Settings.Default with { RestoreAutomatically = restoreAuto });
        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "Notes.txt", 900, 900));
        var log = new Log(paths, TimeProvider.System);
        var gate = new OperationLock(paths, TimeSpan.FromSeconds(5));
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, TimeProvider.System);

        if (seedLayout)
        {
            string key = ArrangementKey.From([MonSingle]);
            store.Save(new Layout(
                Layout.CurrentSchemaVersion,
                key,
                "Laptop",
                BaseTime,
                [MonSingle],
                [new IconPosition("id-1", "Notes.txt", 120, 240)],
                Previous: null));
        }

        return (service, settings, log, desktop, monitors, paths);
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        private long _monotonicTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan delta)
        {
            _now += delta;
            _monotonicTicks += delta.Ticks;
        }

        public void AdvanceWallClock(TimeSpan delta) => _now += delta;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => _monotonicTicks;
    }

    private sealed class FakeTimerFactory(ManualClock clock) : ITimerFactory
    {
        private readonly Lock _sync = new();
        private readonly List<(int Target, TaskCompletionSource Tcs)> _waiters = [];

        public List<FakeTimer> Created { get; } = [];

        public IOneShotTimer Create(TimeSpan dueTime, Action callback)
        {
            var timer = new FakeTimer(dueTime, clock.GetTimestamp() + dueTime.Ticks, callback);
            List<TaskCompletionSource>? toSignal = null;
            lock (_sync)
            {
                Created.Add(timer);
                int count = Created.Count;
                for (int i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (count >= _waiters[i].Target)
                    {
                        toSignal ??= [];
                        toSignal.Add(_waiters[i].Tcs);
                        _waiters.RemoveAt(i);
                    }
                }
            }

            if (toSignal is not null)
            {
                foreach (TaskCompletionSource tcs in toSignal)
                {
                    tcs.TrySetResult();
                }
            }

            return timer;
        }

        public Task WaitForTimerCountAsync(int expectedCount, TimeSpan timeout, CancellationToken cancellationToken)
        {
            TaskCompletionSource tcs;
            lock (_sync)
            {
                if (Created.Count >= expectedCount)
                {
                    return Task.CompletedTask;
                }

                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((expectedCount, tcs));
            }

            return tcs.Task.WaitAsync(timeout, cancellationToken);
        }

        public void FireDue()
        {
            long now = clock.GetTimestamp();
            List<FakeTimer> snapshot;
            lock (_sync)
            {
                snapshot = [.. Created];
            }

            foreach (FakeTimer timer in snapshot)
            {
                if (!timer.Cancelled && !timer.Fired && timer.DeadlineTicks <= now)
                {
                    timer.Fire();
                }
            }
        }

        public sealed class FakeTimer(TimeSpan dueTime, long deadlineTicks, Action callback) : IOneShotTimer
        {
            public TimeSpan DueTime { get; } = dueTime;
            public long DeadlineTicks { get; } = deadlineTicks;
            public bool Cancelled { get; private set; }
            public bool Fired { get; private set; }

            public void Cancel() => Cancelled = true;
            public void Dispose() => Cancelled = true;

            public void Fire()
            {
                Fired = true;
                callback();
            }

            public void ForceInvokeCallback() => callback();
        }
    }
}

[Collection(DesktopCollectionDefinition.Name)]
public sealed class TrayAutoRestoreTests
{
    [Fact]
    public void TrayAppRestoresOnDisplayChangeAndExplorerRestartHonoursRuntimeSettingOffAndDrainsCleanlyOnExit()
    {
        using var data = new TestData();
        string reportFile = Path.Combine(data.Root, "auto-restore-report.txt");
        string instanceName = $"test-autorestore-{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.AutoRestoreTrayScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Isolated child exited {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = File.ReadAllLines(reportFile)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);

        Assert.Equal(string.Empty, report["workerError"]);
        Assert.Equal("60", report["displayArmedMs"]);
        Assert.Equal("120", report["explorerArmedMs"]);
        Assert.Equal("111,222", report["posAfterDisplay"]);
        Assert.Equal("333,444", report["posAfterExplorer"]);
        Assert.Equal("2", report["finishedCountAfterOff"]);
        Assert.Equal("3", report["finishedCountAtExit"]);
        Assert.Equal("False", report["exitedBeforeGateRelease"]);
        Assert.Equal("True", report["hasDisplayLog"]);
        Assert.Equal("True", report["hasExplorerLog"]);
    }

    [Fact]
    public void DirectAppShutdownWaitsForGatedAutoRestoreBeforeDisposingComposition()
    {
        using var data = new TestData();
        string reportFile = Path.Combine(data.Root, "auto-restore-direct-shutdown-report.txt");
        string instanceName = $"test-autorestore-shutdown-{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.AutoRestoreDirectShutdownScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Isolated child exited {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = File.ReadAllLines(reportFile)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);

        Assert.Equal(string.Empty, report["workerError"]);
        Assert.Equal("True", report["restoreCompletedBeforeReturn"]);
        Assert.Equal("111,222", report["posAfterReturn"]);
    }
}
