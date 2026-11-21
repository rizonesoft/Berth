using System.Diagnostics;
using System.Windows.Automation;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.Cli;
using Berth.Tests.Desktop;

namespace Berth.Tests.Smoke;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
[Trait("Category", "Smoke")]
public sealed class WindowSmokeTests
{
    [Fact]
    public void StartFindsWindowDisposesCleanlyAndKillsProcessTreeOnTimeout()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        string prefix = $"berth-smoke-lifecycle-{Guid.NewGuid():N}";
        int runningPid;
        using (var data = new TestData())
        {
            using RunningApp app = RunningApp.Start(data, prefix);
            runningPid = app.ProcessId;
            Assert.True(runningPid > 0);
            Assert.Equal("Berth", app.Window.Current.Name);
            Assert.True(RunningApp.IsProcessAlive(runningPid));
        }

        Assert.False(
            WaitForProcessExit(runningPid, TimeSpan.FromSeconds(5)),
            $"Process {runningPid} was still alive after RunningApp.Dispose().");

        // Separate fresh TestData guarantees first-run startup; 1 ms timeout must throw and kill the child tree.
        int timedOutPid = 0;
        using (var timeoutData = new TestData())
        {
            Assert.Throws<TimeoutException>(() =>
                RunningApp.Start(
                    timeoutData,
                    prefix,
                    windowWaitTimeout: TimeSpan.FromMilliseconds(1),
                    onStarted: pid => timedOutPid = pid));
        }

        Assert.True(timedOutPid > 0, "onStarted callback did not capture a positive process id.");
        Assert.False(
            WaitForProcessExit(timedOutPid, TimeSpan.FromSeconds(5)),
            $"Timed-out process {timedOutPid} was still alive after RunningApp.Start threw TimeoutException.");
    }

    [Fact]
    public void SaveMoveRestoreAndUndoProbeIconThroughUiAutomation()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var monitors = new SystemMonitors();
        var log = new Log(paths, TimeProvider.System);
        using var shell = new ShellDesktopIcons(monitors, log);

        if (shell.AutoArrange)
        {
            Assert.Skip("Desktop Auto Arrange is enabled; UI Automation restore and undo smoke requires Auto Arrange off.");
        }

        (bool snapToGrid, int spacingX, int spacingY) = ProbeIconTests.ReadGridInfo(shell);
        IReadOnlyList<MonitorInfo> currentMonitors = monitors.Read();

        string probeFilePath;
        using (ProbeIcon probe = ProbeIcon.Create(shell))
        {
            probeFilePath = probe.FilePath;

            // RunningApp is disposed BEFORE ProbeIcon so the tray app exits before the probe file is removed.
            using (RunningApp app = RunningApp.Start(data, probe.Prefix))
            {
                IconPosition savedBefore = Assert.Single(probe.ScopedDesktop.ReadIcons());

                // 1. Invoke "Save current layout" through UI Automation InvokePattern.
                app.InvokeButton("Save current layout");
                string saveStatus = app.WaitForStatus(
                    s => string.Equals(s, "Saved 1 icon.", StringComparison.Ordinal),
                    "Saved 1 icon.",
                    TimeSpan.FromSeconds(15));
                Assert.StartsWith("Saved", saveStatus, StringComparison.Ordinal);

                // Verify persisted layout file contains exactly the single probe icon with matching Id, X, Y.
                string[] layoutFiles = Directory.GetFiles(paths.LayoutsFolder, "*.json");
                Assert.Single(layoutFiles);
                var loaded = new LayoutStore(paths).LoadAll();
                Assert.Empty(loaded.Unreadable);
                Layout savedLayout = Assert.Single(loaded.Layouts);
                IconPosition savedIcon = Assert.Single(savedLayout.Icons);
                Assert.Equal(savedBefore.Id, savedIcon.Id, StringComparer.OrdinalIgnoreCase);
                Assert.Equal(savedBefore.X, savedIcon.X);
                Assert.Equal(savedBefore.Y, savedIcon.Y);

                // 2. Move the probe through ScopedDesktopIcons to a distinct observed Explorer position.
                _ = MoveProbeToDistinctPosition(
                    probe.ScopedDesktop,
                    savedBefore,
                    currentMonitors,
                    snapToGrid,
                    spacingX,
                    spacingY);

                // Wait for Berth's 5-second desktop poll to surface the exact SPEC §8 moved sentence, then read
                // the settled Explorer coordinate that Restore will snapshot for Undo.
                string movedStatus = app.WaitForStatus(
                    s => string.Equals(s, "1 icon has moved since this layout was saved.", StringComparison.Ordinal),
                    "1 icon has moved since this layout was saved.",
                    TimeSpan.FromSeconds(15));
                Assert.Equal("1 icon has moved since this layout was saved.", movedStatus);

                IconPosition movedPosition = Assert.Single(probe.ScopedDesktop.ReadIcons());
                Assert.NotEqual((savedBefore.X, savedBefore.Y), (movedPosition.X, movedPosition.Y));

                // 3. Invoke "Restore" through UI Automation and assert the probe returns to its saved position.
                app.InvokeButton("Restore");
                string restoreStatus = app.WaitForStatus(
                    s => string.Equals(s, "Restored 1 icon.", StringComparison.Ordinal),
                    "Restored 1 icon.",
                    TimeSpan.FromSeconds(15));
                Assert.StartsWith("Restored", restoreStatus, StringComparison.Ordinal);
                _ = app.WaitForButton("Undo", TimeSpan.FromSeconds(10), requireEnabled: true);

                IconPosition afterRestore = Assert.Single(probe.ScopedDesktop.ReadIcons());
                Assert.Equal((savedBefore.X, savedBefore.Y), (afterRestore.X, afterRestore.Y));

                // 4. Invoke "Undo" through UI Automation and assert the probe returns to the observed moved position.
                app.InvokeButton("Undo");
                string undoStatus = app.WaitForStatus(
                    s => string.Equals(s, "Put 1 icon back where it was before the restore.", StringComparison.Ordinal),
                    "Put 1 icon back where it was before the restore.",
                    TimeSpan.FromSeconds(15));
                Assert.Equal("Put 1 icon back where it was before the restore.", undoStatus);
                Assert.True(
                    app.WaitForButtonGone("Undo", TimeSpan.FromSeconds(5)),
                    "Undo button remained available after Undo completed.");

                IconPosition afterUndo = Assert.Single(probe.ScopedDesktop.ReadIcons());
                Assert.Equal((movedPosition.X, movedPosition.Y), (afterUndo.X, afterUndo.Y));
            }
        }

        Assert.False(File.Exists(probeFilePath), $"Probe file '{probeFilePath}' was left on the desktop after disposal.");
    }

    [Fact]
    public void SecondInstanceActivatesHiddenPrimaryWindowAndExitsZeroWithinTenSeconds()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var data = new TestData();
        string prefix = $"berth-smoke-single-{Guid.NewGuid():N}";
        string instanceName = $"test-smoke-{Guid.NewGuid():N}";

        using RunningApp primary = RunningApp.Start(data, prefix, instanceName: instanceName);
        int primaryPid = primary.ProcessId;
        Assert.Equal("Berth", primary.Window.Current.Name);

        // Close the primary window through UI Automation so it hides to the tray while primaryPid stays alive.
        primary.CloseWindow();
        Assert.True(
            RunningApp.WaitForWindowGone(primaryPid, TimeSpan.FromSeconds(10)),
            $"Primary Berth window (PID {primaryPid}) did not close/hide within 10 seconds.");
        Assert.True(RunningApp.IsProcessAlive(primaryPid), $"Primary process {primaryPid} exited when its window closed.");

        // Start a second berth.exe with the same BERTH_INSTANCE, BERTH_DATA, and nonempty probe prefix.
        int secondaryPid = 0;
        BerthProcessResult secondResult;
        using (BerthProcess secondary = BerthProcess.Start(
            data.Root,
            [],
            testIconPrefix: prefix,
            extraEnvironment: new Dictionary<string, string>
            {
                ["BERTH_INSTANCE"] = instanceName,
            }))
        {
            secondaryPid = secondary.Id;
            secondResult = secondary.WaitForExit(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(CliExit.Done, secondResult.ExitCode);
        Assert.True(
            secondResult.Elapsed <= TimeSpan.FromSeconds(10),
            $"Second berth.exe took {secondResult.Elapsed.TotalSeconds:F2}s (expected <= 10s).");

        // Primary window must reopen and be owned by the original primary PID; secondary must no longer be running.
        AutomationElement reopened = primary.RefreshWindow(TimeSpan.FromSeconds(10));
        Assert.Equal("Berth", reopened.Current.Name);
        Assert.Equal(primaryPid, reopened.Current.ProcessId);
        Assert.True(RunningApp.IsProcessAlive(primaryPid), "Primary process is no longer alive after secondary activation.");
        Assert.False(RunningApp.IsProcessAlive(secondaryPid), $"Secondary process {secondaryPid} remained alive.");
    }

    private static IconPosition MoveProbeToDistinctPosition(
        ScopedDesktopIcons scoped,
        IconPosition saved,
        IReadOnlyList<MonitorInfo> monitors,
        bool snapToGrid,
        int spacingX,
        int spacingY)
    {
        int stepX = Math.Max(200, (spacingX * 2) + 1);
        int stepY = Math.Max(200, (spacingY * 2) + 1);
        (int X, int Y)[] candidates =
        [
            ChooseInBoundsTarget(saved.X, saved.Y, stepX, 0, monitors),
            ChooseInBoundsTarget(saved.X, saved.Y, 0, stepY, monitors),
            ChooseInBoundsTarget(saved.X, saved.Y, stepX, stepY, monitors),
        ];

        IconPosition lastObserved = saved;
        foreach ((int targetX, int targetY) in candidates)
        {
            scoped.SetPositions([saved with { X = targetX, Y = targetY }]);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(5))
            {
                lastObserved = Assert.Single(scoped.ReadIcons());
                if ((lastObserved.X, lastObserved.Y) != (saved.X, saved.Y))
                {
                    if (!snapToGrid || Math.Abs(lastObserved.X - saved.X) >= spacingX || Math.Abs(lastObserved.Y - saved.Y) >= spacingY)
                    {
                        return lastObserved;
                    }
                }

                Thread.Sleep(50);
            }
        }

        throw new InvalidOperationException(
            $"Probe icon did not move from saved ({saved.X},{saved.Y}); last observed ({lastObserved.X},{lastObserved.Y}).");
    }

    private static (int X, int Y) ChooseInBoundsTarget(
        int fromX,
        int fromY,
        int deltaX,
        int deltaY,
        IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo host = monitors.FirstOrDefault(m =>
            fromX >= m.X && fromX < m.X + m.Width && fromY >= m.Y && fromY < m.Y + m.Height)
            ?? monitors.FirstOrDefault(m => m.Primary)
            ?? monitors[0];

        const int margin = 80;
        int minX = host.X + margin;
        int maxX = Math.Max(minX, host.X + host.Width - margin);
        int minY = host.Y + margin;
        int maxY = Math.Max(minY, host.Y + host.Height - margin);

        int x = deltaX == 0
            ? fromX
            : (fromX + deltaX <= maxX ? fromX + deltaX : Math.Max(minX, fromX - deltaX));
        int y = deltaY == 0
            ? fromY
            : (fromY + deltaY <= maxY ? fromY + deltaY : Math.Max(minY, fromY - deltaY));
        return (x, y);
    }

    private static bool WaitForProcessExit(int processId, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (!RunningApp.IsProcessAlive(processId))
            {
                return false;
            }

            Thread.Sleep(50);
        }

        return RunningApp.IsProcessAlive(processId);
    }
}
