using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Window;

namespace Berth.Tests.Window;

public sealed class DesktopPollerTests
{
    [Fact]
    public async Task PollerReadsOnStartAndTickSkipsOverlapIgnoresPostStopTicksAndTriggersAndRestartsCleanly()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var timer = new FakePollerTimer();
        int reads = 0;
        var holdEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource? holdRelease = null;

        using var poller = new DesktopPoller(
            async () =>
            {
                int current = Interlocked.Increment(ref reads);
                if (holdRelease is { } gate)
                {
                    holdEntered.TrySetResult();
                    await gate.Task.ConfigureAwait(false);
                }
            },
            timer,
            TimeSpan.FromSeconds(5));

        // 1. Start reads immediately once.
        poller.Start();
        await poller.CurrentRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(1, Volatile.Read(ref reads));
        Assert.True(timer.Running);

        // 2. Normal tick reads once.
        timer.RaiseTick();
        await poller.CurrentRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(2, Volatile.Read(ref reads));

        // 3. Slow read in flight -> second tick and TriggerNow are skipped until completion.
        holdRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        timer.RaiseTick();
        await holdEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(3, Volatile.Read(ref reads));

        timer.RaiseTick();
        poller.TriggerNow();
        Assert.Equal(3, Volatile.Read(ref reads));

        holdRelease.SetResult();
        holdRelease = null;
        await poller.CurrentRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        // 4. Stop -> queued tick delivered AFTER Stop() and TriggerNow() after Stop() MUST NOT read.
        poller.Stop();
        Assert.False(timer.Running);
        timer.RaiseTickEvenWhenStopped();
        poller.TriggerNow();
        Assert.Equal(3, Volatile.Read(ref reads));

        // 5. Restart triggers an immediate read.
        poller.Start();
        await poller.CurrentRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(4, Volatile.Read(ref reads));
    }

    [Fact]
    public async Task PollerReleasesOverlapGuardAfterExceptionAndSkipsTickDuringDelayedUiApplication()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        var timer = new FakePollerTimer();
        int attempts = 0;

        using var failingPoller = new DesktopPoller(
            () =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("Transient failure");
                }

                return Task.CompletedTask;
            },
            timer,
            TimeSpan.FromSeconds(5));

        failingPoller.Start();
        await failingPoller.CurrentRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(1, Volatile.Read(ref attempts));

        // Next tick retries because finally released _inFlight.
        timer.RaiseTick();
        await failingPoller.CurrentRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Equal(2, Volatile.Read(ref attempts));

        // Delayed UI application inside MainViewModel.RefreshAsync must keep poller overlap guard active until UI finishes.
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(new MonitorInfo(0, 0, 1920, 1080, 100, true));
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var uiEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var vm = new MainViewModel(
            store,
            desktop,
            monitors,
            TimeProvider.System,
            async action =>
            {
                uiEntered.TrySetResult();
                await uiRelease.Task.ConfigureAwait(false);
                action();
            });

        var vmTimer = new FakePollerTimer();
        using var vmPoller = new DesktopPoller(vm.RefreshAsync, vmTimer, TimeSpan.FromSeconds(5));
        vmPoller.Start();
        await uiEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        int readsWhileUiPending = desktop.Reads;

        // Tick arriving while UI application is still pending must be skipped.
        vmTimer.RaiseTick();
        Assert.Equal(readsWhileUiPending, desktop.Reads);

        uiRelease.SetResult();
        await vmPoller.CurrentRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Single(vm.Rows);
    }

    private sealed class FakePollerTimer : IPollerTimer
    {
        public event EventHandler? Tick;
        public bool Running { get; private set; }

        public void Start(TimeSpan interval) => Running = true;
        public void Stop() => Running = false;
        public void Dispose() => Running = false;

        public void RaiseTick()
        {
            if (Running)
            {
                Tick?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RaiseTickEvenWhenStopped() => Tick?.Invoke(this, EventArgs.Empty);
    }
}

[Collection(DesktopCollectionDefinition.Name)]
public sealed class MainWindowPollerWiringTests(AppFixture appFixture)
{
    [Fact]
    public async Task MainWindowStartsPollerWhenShownStopsWhenHiddenResumesOnReopenAndDisposesOnShutdown()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(new MonitorInfo(0, 0, 1920, 1080, 100, true));
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var log = new Log(paths, TimeProvider.System);
        var settingsStore = new SettingsStore(paths);

        var vm = new MainViewModel(
            store,
            desktop,
            monitors,
            TimeProvider.System,
            action =>
            {
                appFixture.Invoke(action);
                return Task.CompletedTask;
            });

        var timer = new FakeWindowTimer();
        var poller = new DesktopPoller(vm.RefreshAsync, timer, TimeSpan.FromSeconds(5));
        MainWindow? window = null;

        try
        {
            window = appFixture.Invoke(() => new MainWindow(settingsStore, monitors, log, null, vm, poller));
            Assert.False(poller.IsStarted);
            Assert.Equal(0, desktop.Reads);

            // Show -> poller starts and triggers an immediate read.
            appFixture.Invoke(window.Show);
            await poller.CurrentRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.True(poller.IsStarted);
            int readsAfterShow = desktop.Reads;
            Assert.True(readsAfterShow >= 1);

            // Hide (Close) -> poller stops and further ticks do not read.
            appFixture.Invoke(window.Close);
            Assert.False(poller.IsStarted);
            timer.RaiseTickUnconditionally();
            Assert.Equal(readsAfterShow, desktop.Reads);

            // Reopen via BringToFront -> poller resumes and reads again.
            appFixture.Invoke(window.BringToFront);
            await poller.CurrentRefreshTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.True(poller.IsStarted);
            Assert.True(desktop.Reads > readsAfterShow);
        }
        finally
        {
            if (window is not null)
            {
                appFixture.Invoke(window.CloseForShutdown);
            }
        }

        Assert.False(poller.IsStarted);
    }

    private sealed class FakeWindowTimer : IPollerTimer
    {
        public event EventHandler? Tick;
        public void Start(TimeSpan interval) { }
        public void Stop() { }
        public void Dispose() { }
        public void RaiseTickUnconditionally() => Tick?.Invoke(this, EventArgs.Empty);
    }
}
