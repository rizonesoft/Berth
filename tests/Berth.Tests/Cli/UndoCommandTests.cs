using Berth.App;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.Cli;

public sealed class UndoCommandTests
{
    private static readonly MonitorInfo[] TwoMonitors =
    [
        new(0, 0, 2560, 1440, 100, true),
        new(2560, 0, 1920, 1080, 100, false),
    ];

    private static readonly IconPosition[] SavedIcons =
    [
        new("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin", 76, 2),
        new("C:\\Users\\Test\\Desktop\\Notes.txt", "Notes", 76, 106),
        new("C:\\Users\\Test\\Desktop\\Work.lnk", "Work", 76, 210),
    ];

    private static readonly IconPosition[] MovedBeforeRestore =
    [
        new("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin", 400, 12),
        new("C:\\Users\\Test\\Desktop\\Notes.txt", "Notes", 520, 116),
        new("C:\\Users\\Test\\Desktop\\Work.lnk", "Work", 640, 220),
    ];

    [Fact]
    public void RestoreThenUndoReturnsIconsToPreRestorePositionsDeletesSnapshotAndSecondUndoIsRefused()
    {
        using var data = new TestData();
        (LayoutService service, FakeDesktopIcons desktop, BerthPaths paths) = CreateRestoredHarness(data.Root);

        // Fresh writers for the undo invocation.
        var undoOut = new StringWriter();
        var undoErr = new StringWriter();
        int undoCode = new CliRunner(service, undoOut, undoErr).Run(CliArguments.Parse(["undo"]));

        Assert.Equal(0, undoCode);
        Assert.Equal($"Put 3 icons back.{Environment.NewLine}", undoOut.ToString());
        Assert.Equal(string.Empty, undoErr.ToString());
        Assert.Equal(MovedBeforeRestore, ((IDesktopIcons)desktop).ReadIcons());
        Assert.False(File.Exists(paths.UndoFile), "undo.json must be deleted after a full undo.");

        // Second undo in a row must be refused with exit 3.
        var secondOut = new StringWriter();
        var secondErr = new StringWriter();
        int secondCode = new CliRunner(service, secondOut, secondErr).Run(CliArguments.Parse(["undo"]));

        Assert.Equal(3, secondCode);
        Assert.Equal(string.Empty, secondOut.ToString());
        Assert.Equal($"There is nothing to undo.{Environment.NewLine}", secondErr.ToString());
    }

    [Fact]
    public void UndoPartialWithOneMissingIconWritesExactLineToStdoutDeletesSnapshotAndReturnsExit0()
    {
        using var data = new TestData();
        (_, _, BerthPaths paths) = CreateRestoredHarness(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 5, 0, TimeSpan.Zero));
        // One icon (Work.lnk) removed after restore.
        var desktopWithMissing = new FakeDesktopIcons(SavedIcons[0], SavedIcons[1]);
        var service = new LayoutService(
            desktopWithMissing,
            new FakeMonitors(TwoMonitors),
            new LayoutStore(paths),
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(1)),
            new Log(paths, clock),
            clock);

        var output = new StringWriter();
        var error = new StringWriter();
        int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["undo"]));

        Assert.Equal(0, code);
        Assert.Equal($"Put 2 of 3 icons back. 1 is no longer on the desktop.{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal([MovedBeforeRestore[0], MovedBeforeRestore[1]], ((IDesktopIcons)desktopWithMissing).ReadIcons());
        Assert.False(File.Exists(paths.UndoFile));
    }

    [Fact]
    public void UndoPlacementFailuresWriteExactLinesToStderrPreserveSnapshotBytesAndReturnExit1()
    {
        // 1) One icon fails placement (SnapBy = 1 on one targeted icon via SnapIds).
        using (var data = new TestData())
        {
            (_, _, BerthPaths paths) = CreateRestoredHarness(data.Root);
            byte[] originalSnapshot = File.ReadAllBytes(paths.UndoFile);
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 5, 0, TimeSpan.Zero));
            var desktop = new FakeDesktopIcons(SavedIcons)
            {
                SnapBy = 1,
                SnapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SavedIcons[1].Id },
            };
            var service = new LayoutService(
                desktop,
                new FakeMonitors(TwoMonitors),
                new LayoutStore(paths),
                paths,
                new OperationLock(paths, TimeSpan.FromSeconds(1)),
                new Log(paths, clock),
                clock);

            var output = new StringWriter();
            var error = new StringWriter();
            int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["undo"]));

            Assert.Equal(1, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal($"Put 2 of 3 icons back. 1 did not stay where it was.{Environment.NewLine}", error.ToString());
            Assert.True(File.Exists(paths.UndoFile));
            Assert.Equal(originalSnapshot, File.ReadAllBytes(paths.UndoFile));
        }

        // 2) Combined case: one missing, one fails placement, one back in place.
        using (var data = new TestData())
        {
            (_, _, BerthPaths paths) = CreateRestoredHarness(data.Root);
            byte[] originalSnapshot = File.ReadAllBytes(paths.UndoFile);
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 5, 0, TimeSpan.Zero));
            var desktop = new FakeDesktopIcons(SavedIcons[0], SavedIcons[1])
            {
                SnapBy = 1,
                SnapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SavedIcons[1].Id },
            };
            var service = new LayoutService(
                desktop,
                new FakeMonitors(TwoMonitors),
                new LayoutStore(paths),
                paths,
                new OperationLock(paths, TimeSpan.FromSeconds(1)),
                new Log(paths, clock),
                clock);

            var output = new StringWriter();
            var error = new StringWriter();
            int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["undo"]));

            Assert.Equal(1, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal(
                $"Put 1 of 3 icons back. 1 did not stay where it was. 1 is no longer on the desktop.{Environment.NewLine}",
                error.ToString());
            Assert.True(File.Exists(paths.UndoFile));
            Assert.Equal(originalSnapshot, File.ReadAllBytes(paths.UndoFile));
        }
    }

    [Fact]
    public async Task UndoRefusalsWriteExactSpec18MessagesToStderrWithEmptyStdoutAndReturnExit3()
    {
        // 1) Fresh data folder (nothing to undo).
        using (var data = new TestData())
        {
            BerthPaths paths = BerthPaths.ForRoot(data.Root);
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
            var service = new LayoutService(
                new FakeDesktopIcons(SavedIcons),
                new FakeMonitors(TwoMonitors),
                new LayoutStore(paths),
                paths,
                new OperationLock(paths, TimeSpan.FromSeconds(1)),
                new Log(paths, clock),
                clock);
            var output = new StringWriter();
            var error = new StringWriter();

            int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["undo"]));

            Assert.Equal(3, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal($"There is nothing to undo.{Environment.NewLine}", error.ToString());
        }

        // 2) Auto arrange is on (valid snapshot seeded).
        using (var data = new TestData())
        {
            (LayoutService service, FakeDesktopIcons desktop, _) = CreateRestoredHarness(data.Root);
            desktop.SetAutoArrange(true);
            var output = new StringWriter();
            var error = new StringWriter();

            int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["undo"]));

            Assert.Equal(3, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal(
                $"Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again.{Environment.NewLine}",
                error.ToString());
        }

        // 3) Desktop unavailable before placement.
        using (var data = new TestData())
        {
            (LayoutService service, FakeDesktopIcons desktop, _) = CreateRestoredHarness(data.Root);
            desktop.Unavailable = true;
            var output = new StringWriter();
            var error = new StringWriter();

            int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["undo"]));

            Assert.Equal(3, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal($"Berth cannot reach the desktop. Explorer may be restarting.{Environment.NewLine}", error.ToString());
        }

        // 4) Busy (operation lock held by another thread).
        using (var data = new TestData())
        {
            (_, FakeDesktopIcons desktop, BerthPaths paths) = CreateRestoredHarness(data.Root);
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 5, 0, TimeSpan.Zero));
            var gate = new OperationLock(paths, TimeSpan.Zero);
            var service = new LayoutService(
                desktop,
                new FakeMonitors(TwoMonitors),
                new LayoutStore(paths),
                paths,
                gate,
                new Log(paths, clock),
                clock);

            var acquired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? holderError = null;
            var holder = new Thread(() =>
            {
                try
                {
                    using var mutex = new Mutex(initiallyOwned: false, gate.MutexName);
                    bool gotIt = mutex.WaitOne(TimeSpan.FromSeconds(5));
                    acquired.TrySetResult(gotIt);
                    if (gotIt)
                    {
                        try
                        {
                            release.Task.GetAwaiter().GetResult();
                        }
                        finally
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                }
                catch (Exception ex)
                {
                    holderError = ex;
                    acquired.TrySetResult(false);
                }
            })
            {
                IsBackground = true,
            };
            holder.Start();
            try
            {
                bool holderAcquired = await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                Assert.True(holderAcquired, "Background thread failed to acquire the operation mutex.");

                var output = new StringWriter();
                var error = new StringWriter();

                int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["undo"]));

                Assert.Equal(3, code);
                Assert.Equal(string.Empty, output.ToString());
                Assert.Equal($"Berth is busy with another save or restore. Try again in a moment.{Environment.NewLine}", error.ToString());
            }
            finally
            {
                release.TrySetResult();
                holder.Join();
            }

            Assert.Null(holderError);
        }
    }

    [Fact]
    public void ProgramRunRoutesUndoCommandToCompositionAndCliRunner()
    {
        using var data = new TestData();
        (LayoutService service, FakeDesktopIcons desktop, BerthPaths paths) = CreateRestoredHarness(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 5, 0, TimeSpan.Zero));
        var monitors = new FakeMonitors(TwoMonitors);
        var store = new LayoutStore(paths);
        var gate = new OperationLock(paths, TimeSpan.FromSeconds(1));

        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = global::Berth.Program.Run(
            ["undo"],
            output,
            error,
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            createComposition: (p, l) => Composition.ForTest(p, l, monitors, desktop, store, gate, service),
            clock: clock);

        Assert.Equal(0, exitCode);
        Assert.Equal($"Put 3 icons back.{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(MovedBeforeRestore, ((IDesktopIcons)desktop).ReadIcons());
        Assert.False(File.Exists(paths.UndoFile));
    }

    private static (LayoutService Service, FakeDesktopIcons Desktop, BerthPaths Paths) CreateRestoredHarness(string root)
    {
        BerthPaths paths = BerthPaths.ForRoot(root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var desktop = new FakeDesktopIcons(SavedIcons);
        var monitors = new FakeMonitors(TwoMonitors);
        var store = new LayoutStore(paths);
        var gate = new OperationLock(paths, TimeSpan.FromSeconds(1));
        var log = new Log(paths, clock);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);

        // Save initial positions, move to distinct pre-restore positions, then restore through CliRunner.
        Assert.True(service.Save().Done);
        ((IDesktopIcons)desktop).SetPositions(MovedBeforeRestore);
        Assert.Equal(MovedBeforeRestore, ((IDesktopIcons)desktop).ReadIcons());

        var restoreOut = new StringWriter();
        var restoreErr = new StringWriter();
        int restoreCode = new CliRunner(service, restoreOut, restoreErr).Run(CliArguments.Parse(["restore"]));
        Assert.Equal(0, restoreCode);
        Assert.Equal(SavedIcons, ((IDesktopIcons)desktop).ReadIcons());
        Assert.True(File.Exists(paths.UndoFile));

        return (service, desktop, paths);
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => start;
    }
}
