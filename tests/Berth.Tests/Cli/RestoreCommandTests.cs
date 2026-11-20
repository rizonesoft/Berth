using Berth.App;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.Cli;

public sealed class RestoreCommandTests
{
    private static readonly MonitorInfo[] TwoMonitors =
    [
        new(0, 0, 2560, 1440, 100, true),
        new(2560, 0, 1920, 1080, 100, false),
    ];

    private static readonly IconPosition[] ThreeIcons =
    [
        new("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin", 76, 2),
        new("C:\\Users\\Test\\Desktop\\Notes.txt", "Notes", 76, 106),
        new("C:\\Users\\Test\\Desktop\\Work.lnk", "Work", 76, 210),
    ];

    [Fact]
    public void RestoreFullWritesExactLineToStdoutAndReturnsExit0()
    {
        using var data = new TestData();
        (LayoutService service, FakeDesktopIcons desktop, _) = CreateSavedHarness(data.Root);

        // Move icons away from saved positions before restoring.
        ((IDesktopIcons)desktop).SetPositions([ThreeIcons[0] with { X = 400, Y = 500 }]);

        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = new CliRunner(service, output, error).Run(CliArguments.Parse(["restore"]));

        Assert.Equal(CliExit.Done, exitCode);
        Assert.Equal($"Restored 3 icons from \"2 monitors\".{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(ThreeIcons[0], ((IDesktopIcons)desktop).ReadIcons()[0]);
    }

    [Fact]
    public void RestorePartialWithMissingIconWritesExactLineToStdoutAndReturnsExit0()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(TwoMonitors);
        var initialDesktop = new FakeDesktopIcons(ThreeIcons);
        var gate = new OperationLock(paths, TimeSpan.FromSeconds(1));
        var log = new Log(paths, clock);
        Assert.True(new LayoutService(initialDesktop, monitors, store, paths, gate, log, clock).Save().Done);

        // Desktop now has only 2 of the 3 saved icons (Work.lnk removed).
        var desktopWithMissing = new FakeDesktopIcons(
            ThreeIcons[0] with { X = 500, Y = 500 },
            ThreeIcons[1] with { X = 600, Y = 600 });
        var service = new LayoutService(desktopWithMissing, monitors, store, paths, gate, log, clock);

        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = new CliRunner(service, output, error).Run(CliArguments.Parse(["restore"]));

        Assert.Equal(CliExit.Done, exitCode);
        Assert.Equal($"Restored 2 of 3 icons from \"2 monitors\". 1 is no longer on the desktop.{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void RestoreNotPlacedWritesExactLineToStderrWithEmptyStdoutAndReturnsExit1()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(TwoMonitors);
        var gate = new OperationLock(paths, TimeSpan.FromSeconds(1));
        var log = new Log(paths, clock);
        Assert.True(new LayoutService(new FakeDesktopIcons(ThreeIcons), monitors, store, paths, gate, log, clock).Save().Done);

        // 1) All 3 icons snapped by 1 pixel.
        var allSnapped = new FakeDesktopIcons(ThreeIcons) { SnapBy = 1 };
        var outAll = new StringWriter();
        var errAll = new StringWriter();
        int codeAll = new CliRunner(new LayoutService(allSnapped, monitors, store, paths, gate, log, clock), outAll, errAll)
            .Run(CliArguments.Parse(["restore"]));

        Assert.Equal(CliExit.Error, codeAll);
        Assert.Equal(string.Empty, outAll.ToString());
        Assert.Equal($"Restored 0 of 3 icons from \"2 monitors\". 3 did not stay where they were saved.{Environment.NewLine}", errAll.ToString());

        // 2) Combined case: 1 missing, 1 snapped by 1 pixel (via SnapIds), 1 in place -> placement failure takes precedence (exit 1).
        var combined = new FakeDesktopIcons(ThreeIcons[0], ThreeIcons[1])
        {
            SnapBy = 1,
            SnapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ThreeIcons[1].Id },
        };
        var outCombined = new StringWriter();
        var errCombined = new StringWriter();
        int codeCombined = new CliRunner(new LayoutService(combined, monitors, store, paths, gate, log, clock), outCombined, errCombined)
            .Run(CliArguments.Parse(["restore"]));

        Assert.Equal(CliExit.Error, codeCombined);
        Assert.Equal(string.Empty, outCombined.ToString());
        Assert.Equal(
            $"Restored 1 of 3 icons from \"2 monitors\". 1 did not stay where it was saved. 1 is no longer on the desktop.{Environment.NewLine}",
            errCombined.ToString());
    }

    [Fact]
    public void RestoreRefusalsWriteExactSpec18MessagesToStderrWithEmptyStdoutAndReturnExit3()
    {
        // 1) Auto arrange is on.
        using (var data = new TestData())
        {
            (LayoutService service, FakeDesktopIcons desktop, _) = CreateSavedHarness(data.Root);
            desktop.SetAutoArrange(true);
            var output = new StringWriter();
            var error = new StringWriter();

            int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["restore"]));

            Assert.Equal(CliExit.Refused, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal(
                $"Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again.{Environment.NewLine}",
                error.ToString());
        }

        // 2) No layout for this arrangement.
        using (var data = new TestData())
        {
            BerthPaths paths = BerthPaths.ForRoot(data.Root);
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
            var service = new LayoutService(
                new FakeDesktopIcons(ThreeIcons),
                new FakeMonitors(TwoMonitors),
                new LayoutStore(paths),
                paths,
                new OperationLock(paths, TimeSpan.FromSeconds(1)),
                new Log(paths, clock),
                clock);
            var output = new StringWriter();
            var error = new StringWriter();

            int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["restore"]));

            Assert.Equal(CliExit.Refused, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal($"This monitor arrangement has no saved layout.{Environment.NewLine}", error.ToString());
        }

        // 3) Desktop unavailable.
        using (var data = new TestData())
        {
            (LayoutService service, FakeDesktopIcons desktop, _) = CreateSavedHarness(data.Root);
            desktop.Unavailable = true;
            var output = new StringWriter();
            var error = new StringWriter();

            int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["restore"]));

            Assert.Equal(CliExit.Refused, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal($"Berth cannot reach the desktop. Explorer may be restarting.{Environment.NewLine}", error.ToString());
        }

        // 4) Busy (operation lock held by another thread).
        using (var data = new TestData())
        {
            BerthPaths paths = BerthPaths.ForRoot(data.Root);
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
            var gate = new OperationLock(paths, TimeSpan.Zero);
            var service = new LayoutService(
                new FakeDesktopIcons(ThreeIcons),
                new FakeMonitors(TwoMonitors),
                new LayoutStore(paths),
                paths,
                gate,
                new Log(paths, clock),
                clock);
            Assert.True(service.Save().Done);

            using var acquired = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            bool holderAcquired = false;
            Exception? holderError = null;
            var holder = new Thread(() =>
            {
                try
                {
                    using var mutex = new Mutex(initiallyOwned: false, gate.MutexName);
                    holderAcquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
                    acquired.Set();
                    if (holderAcquired)
                    {
                        try
                        {
                            release.Wait();
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
                    acquired.Set();
                }
            })
            {
                IsBackground = true,
            };
            holder.Start();
            try
            {
                Assert.True(acquired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Assert.Null(holderError);
                Assert.True(holderAcquired, "Background thread failed to acquire the operation mutex.");

                var output = new StringWriter();
                var error = new StringWriter();

                int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["restore"]));

                Assert.Equal(3, code);
                Assert.Equal(string.Empty, output.ToString());
                Assert.Equal($"Berth is busy with another save or restore. Try again in a moment.{Environment.NewLine}", error.ToString());
            }
            finally
            {
                release.Set();
                Assert.True(holder.Join(TimeSpan.FromSeconds(5)), "Mutex-holder thread did not exit before event disposal.");
            }
        }

        // 5) Unreadable layout file (malformed JSON left untouched).
        using (var data = new TestData())
        {
            BerthPaths paths = BerthPaths.ForRoot(data.Root);
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
            string key = ArrangementKey.From(TwoMonitors);
            string layoutPath = paths.LayoutFile(key);
            Directory.CreateDirectory(paths.LayoutsFolder);
            byte[] malformedBytes = "{ not valid json"u8.ToArray();
            File.WriteAllBytes(layoutPath, malformedBytes);

            var service = new LayoutService(
                new FakeDesktopIcons(ThreeIcons),
                new FakeMonitors(TwoMonitors),
                new LayoutStore(paths),
                paths,
                new OperationLock(paths, TimeSpan.FromSeconds(1)),
                new Log(paths, clock),
                clock);
            var output = new StringWriter();
            var error = new StringWriter();

            int code = new CliRunner(service, output, error).Run(CliArguments.Parse(["restore"]));

            Assert.Equal(CliExit.Refused, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Equal($"The layout file {key}.json could not be read. It was left untouched.{Environment.NewLine}", error.ToString());
            Assert.Equal(malformedBytes, File.ReadAllBytes(layoutPath));
        }
    }

    [Fact]
    public void ExitCodesMatchSpec13NumericContract()
    {
        Assert.Equal(0, CliExit.Done);
        Assert.Equal(1, CliExit.Error);
        Assert.Equal(2, CliExit.Usage);
        Assert.Equal(3, CliExit.Refused);
    }

    [Fact]
    public void ProgramRunRoutesRestoreCommandToCompositionAndCliRunner()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var desktop = new FakeDesktopIcons(ThreeIcons);
        var monitors = new FakeMonitors(TwoMonitors);
        var store = new LayoutStore(paths);
        var gate = new OperationLock(paths, TimeSpan.FromSeconds(1));
        var log = new Log(paths, clock);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        Assert.True(service.Save().Done);
        ((IDesktopIcons)desktop).SetPositions([ThreeIcons[0] with { X = 999, Y = 888 }]);

        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = global::Berth.Program.Run(
            ["restore"],
            output,
            error,
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            createComposition: (p, l) => Composition.ForTest(p, l, monitors, desktop, store, gate, service),
            clock: clock);

        Assert.Equal(CliExit.Done, exitCode);
        Assert.Equal($"Restored 3 icons from \"2 monitors\".{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(ThreeIcons[0], ((IDesktopIcons)desktop).ReadIcons()[0]);
    }

    private static (LayoutService Service, FakeDesktopIcons Desktop, BerthPaths Paths) CreateSavedHarness(string root)
    {
        BerthPaths paths = BerthPaths.ForRoot(root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var desktop = new FakeDesktopIcons(ThreeIcons);
        var monitors = new FakeMonitors(TwoMonitors);
        var store = new LayoutStore(paths);
        var gate = new OperationLock(paths, TimeSpan.FromSeconds(1));
        var log = new Log(paths, clock);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        Assert.True(service.Save().Done);
        return (service, desktop, paths);
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => start;
    }
}
