using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Desktop.Interop;
using Berth.Tests.Fakes;

namespace Berth.Tests.Desktop;

public sealed class UnavailableTests
{
    private static readonly IReadOnlyList<MonitorInfo> SingleMonitor =
        [new(0, 0, 1920, 1080, 100, Primary: true)];

    private static int TriggerNullReference()
    {
        string? s = null;
        return s!.Length;
    }

    private static DesktopView TriggerNullReferenceView()
    {
        string? s = null;
        _ = s!.Length;
        return null!;
    }

    private static Exception[] AllMappedShellExceptions() =>
    [
        Marshal.GetExceptionForHR(unchecked((int)0x800706BA))!,
        new InvalidCastException("QI failed"),
        new InvalidComObjectException("RCW separated"),
        new UnauthorizedAccessException("Desktop access denied"),
        new FileNotFoundException("Shell item vanished"),
        new DirectoryNotFoundException("Desktop folder vanished"),
    ];

    [Fact]
    public void DesktopViewOpenMapsAllSixShellExceptionsNullReturnAndPreservesAlreadyMappedAndAbsentFolderView2()
    {
        Sta.Run(() =>
        {
            foreach (Exception original in AllMappedShellExceptions())
            {
                DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() =>
                    DesktopView.Open(() => throw original, _ => { }));
                Assert.Same(original, thrown.InnerException);
            }

            DesktopUnavailableException nullThrown = Assert.Throws<DesktopUnavailableException>(() =>
                DesktopView.Open(() => null, _ => { }));
            Assert.Null(nullThrown.InnerException);

            var existing = new DesktopUnavailableException("Already mapped");
            DesktopUnavailableException sameThrown = Assert.Throws<DesktopUnavailableException>(() =>
                DesktopView.Open(() => throw existing, _ => { }));
            Assert.Same(existing, sameThrown);

            // Programming bugs propagate unwrapped.
            Assert.Throws<NullReferenceException>(() => DesktopView.Open(() => TriggerNullReference(), _ => { }));
            var invalidOp = new InvalidOperationException("State bug in Berth");
            Assert.Same(invalidOp, Assert.Throws<InvalidOperationException>(() => DesktopView.Open(() => throw invalidOp, _ => { })));

            // Absent optional IFolderView2 still succeeds.
            var viewWithoutV2 = new MinimalFolderView();
            using DesktopView opened = DesktopView.Open(
                () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(viewWithoutV2))),
                _ => { });
            Assert.NotNull(opened.View);
            Assert.Null(opened.View2);
        });
    }

    [Fact]
    public void ShellDesktopIconsMapsAllSixFactoryExceptionsNullViewAndAlreadyMappedAcrossEveryMember()
    {
        var monitors = new FixedMonitors(SingleMonitor);
        var sampleIcons = new IconPosition[] { new(@"C:\Desktop\A.txt", "berth-test-a", 10, 20) };

        foreach (Exception original in AllMappedShellExceptions())
        {
            using var shell = new ShellDesktopIcons(() => throw original, monitors: monitors);
            var scoped = new ScopedDesktopIcons(shell, "berth-test-");

            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => shell.ReadIcons()).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => scoped.ReadIcons()).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => shell.SetPositions(sampleIcons)).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => shell.SetPositions(sampleIcons, () => { })).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => scoped.SetPositions(sampleIcons)).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => scoped.SetPositions(sampleIcons, () => { })).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => _ = shell.AutoArrange).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => shell.TryTurnAutoArrangeOff()).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => shell.RunWithView(_ => 1)).InnerException);
        }

        // Null view from factory -> DesktopUnavailableException with null InnerException.
        using (var nullShell = new ShellDesktopIcons(() => null!, monitors: monitors))
        {
            var scoped = new ScopedDesktopIcons(nullShell, "berth-test-");
            Assert.Null(Assert.Throws<DesktopUnavailableException>(() => nullShell.ReadIcons()).InnerException);
            Assert.Null(Assert.Throws<DesktopUnavailableException>(() => scoped.ReadIcons()).InnerException);
            Assert.Null(Assert.Throws<DesktopUnavailableException>(() => nullShell.SetPositions(sampleIcons)).InnerException);
            Assert.Null(Assert.Throws<DesktopUnavailableException>(() => nullShell.SetPositions(sampleIcons, () => { })).InnerException);
            Assert.Null(Assert.Throws<DesktopUnavailableException>(() => _ = nullShell.AutoArrange).InnerException);
            Assert.Null(Assert.Throws<DesktopUnavailableException>(() => nullShell.TryTurnAutoArrangeOff()).InnerException);
            Assert.Null(Assert.Throws<DesktopUnavailableException>(() => nullShell.RunWithView(_ => 1)).InnerException);
        }

        // Already-mapped DesktopUnavailableException passes through unchanged.
        var existing = new DesktopUnavailableException("Already mapped");
        using (var existingShell = new ShellDesktopIcons(() => throw existing, monitors: monitors))
        {
            Assert.Same(existing, Assert.Throws<DesktopUnavailableException>(() => existingShell.ReadIcons()));
            Assert.Same(existing, Assert.Throws<DesktopUnavailableException>(() => existingShell.SetPositions(sampleIcons)));
            Assert.Same(existing, Assert.Throws<DesktopUnavailableException>(() => _ = existingShell.AutoArrange));
            Assert.Same(existing, Assert.Throws<DesktopUnavailableException>(() => existingShell.TryTurnAutoArrangeOff()));
            Assert.Same(existing, Assert.Throws<DesktopUnavailableException>(() => existingShell.RunWithView(_ => 1)));
        }
    }

    [Fact]
    public void ShellDesktopIconsMapsPostAcquisitionShellFailuresAndPreservesProgrammingBugsAndBeforeMoveExceptions()
    {
        var monitors = new FixedMonitors(SingleMonitor);
        Exception comError = Marshal.GetExceptionForHR(unchecked((int)0x800706BA))!;

        // Post-acquisition failures across ALL SIX mapped shell exceptions on ReadIcons, SetPositions, AutoArrange, TryTurnAutoArrangeOff.
        foreach (Exception original in AllMappedShellExceptions())
        {
            using var shell = new ShellDesktopIcons(
                openView: () =>
                {
                    var view = new ThrowingFolderView(original);
                    return DesktopView.Open(() => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(view))), _ => { });
                },
                monitors: monitors);

            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => shell.ReadIcons()).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => shell.SetPositions([new(@"C:\A.txt", "A", 1, 2)])).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => _ = shell.AutoArrange).InnerException);
            Assert.Same(original, Assert.Throws<DesktopUnavailableException>(() => shell.TryTurnAutoArrangeOff()).InnerException);
        }

        // View-release failure (InvalidComObjectException) maps to DesktopUnavailableException with exact release count 1,
        // preserves an in-flight exception when work already threw, and never double-releases intermediates in DesktopView.Open.
        {
            var releaseError = new InvalidComObjectException("View RCW separated on release");
            int viewReleases = 0;
            var view = new HealthyFolderView([(@"C:\Desktop\A.txt", "A", 10, 20)]);
            using var releaseShell = new ShellDesktopIcons(
                openView: () => DesktopView.Open(
                    () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(view))),
                    obj =>
                    {
                        if (ReferenceEquals(obj, view))
                        {
                            viewReleases++;
                            throw releaseError;
                        }
                    }),
                monitors: monitors);
            Assert.Same(releaseError, Assert.Throws<DesktopUnavailableException>(() => releaseShell.ReadIcons()).InnerException);
            Assert.Equal(1, viewReleases);

            // When work throws COMException AND release throws InvalidComObjectException, original COMException is preserved.
            int failingViewReleases = 0;
            var failingView = new ThrowingFolderView(comError);
            using var bothFailShell = new ShellDesktopIcons(
                openView: () => DesktopView.Open(
                    () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(failingView))),
                    obj =>
                    {
                        if (ReferenceEquals(obj, failingView))
                        {
                            failingViewReleases++;
                            throw releaseError;
                        }
                    }),
                monitors: monitors);
            Assert.Same(comError, Assert.Throws<DesktopUnavailableException>(() => bothFailShell.ReadIcons()).InnerException);
            Assert.Equal(1, failingViewReleases);

            // Intermediate release throwing in DesktopView.Open releases that object exactly once (never double-released in finally).
            Sta.Run(() =>
            {
                int shellWindowsReleases = 0;
                var sw = new StubShellWindows(new StubServiceProvider(new StubShellBrowser(new MinimalFolderView())));
                DesktopUnavailableException openThrown = Assert.Throws<DesktopUnavailableException>(() =>
                    DesktopView.Open(
                        () => sw,
                        obj =>
                        {
                            if (ReferenceEquals(obj, sw))
                            {
                                shellWindowsReleases++;
                                throw releaseError;
                            }
                        }));
                Assert.Same(releaseError, openThrown.InnerException);
                Assert.Equal(1, shellWindowsReleases);

                // Acquisition throws UnauthorizedAccessException AND releasing dispatchObj in finally throws InvalidComObjectException:
                // original UnauthorizedAccessException is preserved and dispatchObj is released once.
                var queryError = new UnauthorizedAccessException("QueryService denied");
                int dispatchReleases = 0;
                var throwingSp = new ThrowingServiceProvider(queryError);
                DesktopUnavailableException queryThrown = Assert.Throws<DesktopUnavailableException>(() =>
                    DesktopView.Open(
                        () => new StubShellWindows(throwingSp),
                        obj =>
                        {
                            if (ReferenceEquals(obj, throwingSp))
                            {
                                dispatchReleases++;
                                throw releaseError;
                            }
                        }));
                Assert.Same(queryError, queryThrown.InnerException);
                Assert.Equal(1, dispatchReleases);
            });

            // RunWithView: work throws UnauthorizedAccessException AND view disposal throws InvalidComObjectException ->
            // exact work exception reaches the caller and view is released once.
            int runWithViewReleases = 0;
            var runWithViewTarget = new HealthyFolderView([(@"C:\Desktop\A.txt", "A", 10, 20)]);
            var workError = new UnauthorizedAccessException("Caller work denied");
            using var runWithViewShell = new ShellDesktopIcons(
                openView: () => DesktopView.Open(
                    () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(runWithViewTarget))),
                    obj =>
                    {
                        if (ReferenceEquals(obj, runWithViewTarget))
                        {
                            runWithViewReleases++;
                            throw releaseError;
                        }
                    }),
                monitors: monitors);
            Assert.Same(workError, Assert.Throws<UnauthorizedAccessException>(() => runWithViewShell.RunWithView<int>(_ => throw workError)));
            Assert.Equal(1, runWithViewReleases);
        }

        // Programming bugs (NullReferenceException and InvalidOperationException) propagate unwrapped.
        var invalidOp = new InvalidOperationException("Internal state bug");
        using (var bugShell = new ShellDesktopIcons(() => TriggerNullReferenceView(), monitors: monitors))
        {
            Assert.Throws<NullReferenceException>(() => bugShell.ReadIcons());
        }

        using (var bugShell2 = new ShellDesktopIcons(() => throw invalidOp, monitors: monitors))
        {
            Assert.Same(invalidOp, Assert.Throws<InvalidOperationException>(() => bugShell2.ReadIcons()));
        }

        // Paired RunWithView UnauthorizedAccessException: factory wraps, work propagates unwrapped.
        var accessDenied = new UnauthorizedAccessException("Access denied");
        using (var factoryDenied = new ShellDesktopIcons(() => throw accessDenied, monitors: monitors))
        {
            Assert.Same(accessDenied, Assert.Throws<DesktopUnavailableException>(() => factoryDenied.RunWithView(_ => 1)).InnerException);
        }

        using (var workDenied = new ShellDesktopIcons(
            openView: () =>
            {
                var view = new HealthyFolderView([(@"C:\Desktop\Probe.txt", "berth-test-1", 10, 20)]);
                return DesktopView.Open(() => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(view))), _ => { });
            },
            monitors: monitors))
        {
            Assert.Same(accessDenied, Assert.Throws<UnauthorizedAccessException>(() => workDenied.RunWithView<int>(_ => throw accessDenied)));
        }

        // beforeMove throwing UnauthorizedAccessException escapes unwrapped on BOTH empty and matched-icon requests
        // for BOTH public ShellDesktopIcons and ScopedDesktopIcons, with SelectAndPositionItems never called.
        var healthyView = new HealthyFolderView([(@"C:\Desktop\Probe.txt", "berth-test-1", 10, 20)]);
        using var moveShell = new ShellDesktopIcons(
            openView: () => DesktopView.Open(() => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(healthyView))), _ => { }),
            monitors: monitors);
        var scoped = new ScopedDesktopIcons(moveShell, "berth-test-");
        var matched = new IconPosition[] { new(@"C:\Desktop\Probe.txt", "berth-test-1", 100, 200) };

        Assert.Same(accessDenied, Assert.Throws<UnauthorizedAccessException>(() => moveShell.SetPositions([], () => throw accessDenied)));
        Assert.Same(accessDenied, Assert.Throws<UnauthorizedAccessException>(() => moveShell.SetPositions(matched, () => throw accessDenied)));
        Assert.Same(accessDenied, Assert.Throws<UnauthorizedAccessException>(() => scoped.SetPositions([], () => throw accessDenied)));
        Assert.Same(accessDenied, Assert.Throws<UnauthorizedAccessException>(() => scoped.SetPositions(matched, () => throw accessDenied)));
        Assert.Equal(0, healthyView.SelectAndPositionCalls);
    }

    [Fact]
    public void ShellDesktopIconsRecoversAfterFactoryFailureAndMidCallDisconnectWithExactStaRelease()
    {
        var monitors = new FixedMonitors(SingleMonitor);
        Exception comError = Marshal.GetExceptionForHR(unchecked((int)0x800706BA))!;

        // 1) Factory fails once, then returns healthy view across every member.
        int factoryAttempt = 0;
        var healthy = new HealthyFolderView([(@"C:\Desktop\A.txt", "A", 10, 20)]);
        using (var shell = new ShellDesktopIcons(
            openView: () =>
            {
                factoryAttempt++;
                if (factoryAttempt % 2 == 1)
                {
                    throw comError;
                }

                return DesktopView.Open(() => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(healthy))), _ => { });
            },
            monitors: monitors))
        {
            Assert.Throws<DesktopUnavailableException>(() => shell.ReadIcons());
            Assert.Single(shell.ReadIcons());

            Assert.Throws<DesktopUnavailableException>(() => shell.SetPositions([new(@"C:\Desktop\A.txt", "A", 30, 40)]));
            shell.SetPositions([new(@"C:\Desktop\A.txt", "A", 30, 40)]);
            Assert.Equal(1, healthy.SelectAndPositionCalls);

            Assert.Throws<DesktopUnavailableException>(() => _ = shell.AutoArrange);
            Assert.False(shell.AutoArrange);

            Assert.Throws<DesktopUnavailableException>(() => shell.TryTurnAutoArrangeOff());
            Assert.True(shell.TryTurnAutoArrangeOff());
        }

        // 2) Mid-call disconnect: view 1 opens cleanly, throws COMException on GetFolder, releases once on STA worker,
        //    then call 2 on the same ShellDesktopIcons opens distinct view 2 and succeeds.
        int openCount = 0;
        var view1 = new ThrowingFolderView(comError);
        var view2 = new HealthyFolderView([(@"C:\Desktop\B.txt", "B", 50, 60)]);
        int view1Releases = 0;
        int view2Releases = 0;
        int? workerThreadId = null;
        int? view1ReleaseThreadId = null;
        int? view2ReleaseThreadId = null;

        using (var shell = new ShellDesktopIcons(
            openView: () =>
            {
                workerThreadId = Environment.CurrentManagedThreadId;
                Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
                openCount++;
                IShellView current = openCount == 1 ? view1 : view2;
                return DesktopView.Open(
                    () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(current))),
                    obj =>
                    {
                        if (ReferenceEquals(obj, view1))
                        {
                            view1Releases++;
                            view1ReleaseThreadId = Environment.CurrentManagedThreadId;
                        }
                        else if (ReferenceEquals(obj, view2))
                        {
                            view2Releases++;
                            view2ReleaseThreadId = Environment.CurrentManagedThreadId;
                        }
                    });
            },
            monitors: monitors))
        {
            DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() => shell.ReadIcons());
            Assert.Same(comError, thrown.InnerException);
            Assert.Equal(1, view1Releases);
            Assert.Equal(workerThreadId, view1ReleaseThreadId);

            IReadOnlyList<IconPosition> recovered = shell.ReadIcons();
            IconPosition only = Assert.Single(recovered);
            Assert.Equal(@"C:\Desktop\B.txt", only.Id);
            Assert.Equal(2, openCount);
            Assert.Equal(1, view1Releases);
            Assert.Equal(1, view2Releases);
            Assert.Equal(workerThreadId, view2ReleaseThreadId);
        }
    }

    [Fact]
    public void LayoutServiceDistinguishesPreBeforeMoveUnavailabilityFromPlacementFailureForRestoreAndUndo()
    {
        var savedIcon = new IconPosition(@"C:\Desktop\A.txt", "A", 10, 20);
        var currentIcon = new IconPosition(@"C:\Desktop\A.txt", "A", 300, 400);
        var priorUndoIcon = new IconPosition(@"C:\Desktop\A.txt", "A", 777, 888);
        string key = ArrangementKey.From(SingleMonitor);

        // Helper to seed a fresh TestData root with one saved layout and one distinct prior undo.json.
        (TestData Data, BerthPaths Paths, LayoutStore Store, OperationLock Lock, Log Log, byte[] PriorUndo) Seed()
        {
            var data = new TestData();
            BerthPaths paths = BerthPaths.ForRoot(data.Root);
            var store = new LayoutStore(paths);
            var layout = new Layout(
                Layout.CurrentSchemaVersion,
                key,
                "Single",
                DateTimeOffset.UtcNow,
                [.. SingleMonitor],
                [savedIcon],
                Previous: null);
            store.Save(layout, new LayoutSnapshot(key, null, null));

            var priorUndo = new UndoSnapshot(UndoSnapshot.CurrentSchemaVersion, key, DateTimeOffset.UtcNow.AddMinutes(-10), [priorUndoIcon]);
            BerthPaths.EnsureFolderFor(paths.UndoFile);
            JsonFile.Write(paths.UndoFile, priorUndo, BerthJson.Default.UndoSnapshot);
            byte[] priorBytes = File.ReadAllBytes(paths.UndoFile);
            return (data, paths, store, new OperationLock(paths, TimeSpan.FromSeconds(5)), new Log(paths, TimeProvider.System), priorBytes);
        }

        // 1) Restore: monitor read throws DesktopUnavailableException at each of the 3 CheckArrangement sites
        //    (call 3 = line 233 after AutoArrange, call 4 = line 270 after staging, call 5 = line 279 inside beforeMove).
        //    Each must refuse with Refusal.DesktopUnavailable, 0 placements, and byte-identical prior undo.json.
        foreach (int failOnMonitorCall in new[] { 3, 4, 5 })
        {
            var (data, paths, store, opLock, log, priorBytes) = Seed();
            using (data)
            {
                int reads = 0;
                var monitors = new ScriptedMonitors(() =>
                {
                    reads++;
                    return reads == failOnMonitorCall
                        ? throw new DesktopUnavailableException($"Monitor lost on read {reads}.")
                        : SingleMonitor;
                });
                var desktop = new FakeDesktopIcons(currentIcon);
                var service = new LayoutService(desktop, monitors, store, paths, opLock, log, TimeProvider.System);

                OperationResult result = service.Restore();
                Assert.Equal(OperationKind.Refused, result.Kind);
                Assert.Equal(Refusal.DesktopUnavailable, result.Refusal);
                Assert.Empty(desktop.Calls);
                Assert.Equal(priorBytes, File.ReadAllBytes(paths.UndoFile));
            }
        }

        // 2) Restore: SetPositions fails BEFORE beforeMove (view acquisition fails) -> Refused(DesktopUnavailable), prior undo intact.
        {
            var (data, paths, store, opLock, log, priorBytes) = Seed();
            using (data)
            {
                var desktop = new PreBeforeMoveFailDesktop(currentIcon);
                var service = new LayoutService(desktop, new FixedMonitors(SingleMonitor), store, paths, opLock, log, TimeProvider.System);

                OperationResult result = service.Restore();
                Assert.Equal(OperationKind.Refused, result.Kind);
                Assert.Equal(Refusal.DesktopUnavailable, result.Refusal);
                Assert.Equal(0, desktop.Placements);
                Assert.Equal(priorBytes, File.ReadAllBytes(paths.UndoFile));
            }
        }

        // 3) Restore: SetPositions fails AFTER beforeMove (placement fails) -> Failed(DesktopUnavailable, DesktopLostDuringRestore), new undo retained.
        {
            var (data, paths, store, opLock, log, priorBytes) = Seed();
            using (data)
            {
                var desktop = new FakeDesktopIcons(currentIcon) { UnavailableOnSet = true };
                var service = new LayoutService(desktop, new FixedMonitors(SingleMonitor), store, paths, opLock, log, TimeProvider.System);

                OperationResult result = service.Restore();
                Assert.Equal(OperationKind.Failed, result.Kind);
                Assert.Equal(FailureKind.DesktopUnavailable, result.Failure);
                Assert.Equal(Messages.DesktopLostDuringRestore, result.Detail);
                Assert.NotEqual(priorBytes, File.ReadAllBytes(paths.UndoFile));
            }
        }

        // 4) Undo: monitor read throws DesktopUnavailableException at CheckArrangement (call 3, line 395) -> Refused(DesktopUnavailable), 0 placements, prior undo intact.
        {
            var (data, paths, store, opLock, log, priorBytes) = Seed();
            using (data)
            {
                int reads = 0;
                var monitors = new ScriptedMonitors(() =>
                {
                    reads++;
                    return reads == 3
                        ? throw new DesktopUnavailableException("Monitor lost before undo placement.")
                        : SingleMonitor;
                });
                var desktop = new FakeDesktopIcons(currentIcon);
                var service = new LayoutService(desktop, monitors, store, paths, opLock, log, TimeProvider.System);

                OperationResult result = service.Undo();
                Assert.Equal(OperationKind.Refused, result.Kind);
                Assert.Equal(Refusal.DesktopUnavailable, result.Refusal);
                Assert.Empty(desktop.Calls);
                Assert.Equal(priorBytes, File.ReadAllBytes(paths.UndoFile));
            }
        }

        // 5) Undo: SetPositions fails BEFORE beforeMove -> Refused(DesktopUnavailable), 0 placements, prior undo intact.
        {
            var (data, paths, store, opLock, log, priorBytes) = Seed();
            using (data)
            {
                var desktop = new PreBeforeMoveFailDesktop(currentIcon);
                var service = new LayoutService(desktop, new FixedMonitors(SingleMonitor), store, paths, opLock, log, TimeProvider.System);

                OperationResult result = service.Undo();
                Assert.Equal(OperationKind.Refused, result.Kind);
                Assert.Equal(Refusal.DesktopUnavailable, result.Refusal);
                Assert.Equal(0, desktop.Placements);
                Assert.Equal(priorBytes, File.ReadAllBytes(paths.UndoFile));
            }
        }

        // 6) Undo: SetPositions fails AFTER beforeMove -> Failed(DesktopUnavailable, DesktopLostDuringUndo), undo snapshot retained.
        {
            var (data, paths, store, opLock, log, priorBytes) = Seed();
            using (data)
            {
                var desktop = new FakeDesktopIcons(currentIcon) { UnavailableOnSet = true };
                var service = new LayoutService(desktop, new FixedMonitors(SingleMonitor), store, paths, opLock, log, TimeProvider.System);

                OperationResult result = service.Undo();
                Assert.Equal(OperationKind.Failed, result.Kind);
                Assert.Equal(FailureKind.DesktopUnavailable, result.Failure);
                Assert.Equal(Messages.DesktopLostDuringUndo, result.Detail);
                Assert.Equal(priorBytes, File.ReadAllBytes(paths.UndoFile));
            }
        }
    }

    private sealed class FixedMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitors
    {
        public IReadOnlyList<MonitorInfo> Read() => monitors;
    }

    private sealed class ScriptedMonitors(Func<IReadOnlyList<MonitorInfo>> read) : IMonitors
    {
        public IReadOnlyList<MonitorInfo> Read() => read();
    }

    private sealed class PreBeforeMoveFailDesktop(params IconPosition[] icons) : IDesktopIcons
    {
        public int Placements { get; private set; }
        public bool AutoArrange => false;
        public IReadOnlyList<IconPosition> ReadIcons() => icons;
        public void SetPositions(IReadOnlyList<IconPosition> requested) => throw new DesktopUnavailableException("View open failed.");
        public void SetPositions(IReadOnlyList<IconPosition> requested, Action beforeMove) => throw new DesktopUnavailableException("View open failed before beforeMove.");
        public bool TryTurnAutoArrangeOff() => false;
    }

    private sealed class MinimalFolderView : IShellView, IFolderView
    {
        public void GetWindow(out nint phwnd) => phwnd = nint.Zero;
        public void ContextSensitiveHelp(bool fEnterMode) { }
        public int TranslateAccelerator(nint pmsg) => 0;
        public void EnableModeless(bool fEnable) { }
        public void UIActivate(uint uState) { }
        public void Refresh() { }
        public void CreateViewWindow(IShellView? psvPrevious, nint pfs, IShellBrowser? psb, nint prcView, out nint phWnd) => phWnd = nint.Zero;
        public void DestroyViewWindow() { }
        public void GetCurrentInfo(nint pfs) { }
        public void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam) { }
        public void SaveViewState() { }
        public void SelectItem(nint pidlItem, uint uFlags) { }
        public void GetItemObject(uint uItem, in Guid riid, out object ppv) => ppv = new object();
        public void GetCurrentViewMode(out uint viewMode) => viewMode = 0;
        public void SetCurrentViewMode(uint viewMode) { }
        public void GetFolder(in Guid riid, out object ppv) => ppv = new object();
        public void Item(int iItemIndex, out nint ppidl) => ppidl = nint.Zero;
        public void ItemCount(uint uFlags, out int pcItems) => pcItems = 0;
        public void Items(uint uFlags, in Guid riid, out object ppv) => ppv = new object();
        public void GetSelectionMarkedItem(out int piItem) => piItem = -1;
        public void GetFocusedItem(out int piItem) => piItem = -1;
        public void GetItemPosition(nint pidl, out NativePoint ppt) => ppt = default;
        public void GetSpacing(ref NativePoint ppt) { }
        public void GetDefaultSpacing(out NativePoint ppt) => ppt = default;
        public int GetAutoArrange() => ShellConstants.S_FALSE;
        public void SelectItem(int iItem, uint dwFlags) { }
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) { }
    }

    private sealed class ThrowingFolderView(Exception error) : IShellView, IFolderView, IFolderView2
    {
        public void GetWindow(out nint phwnd) => phwnd = nint.Zero;
        public void ContextSensitiveHelp(bool fEnterMode) { }
        public int TranslateAccelerator(nint pmsg) => 0;
        public void EnableModeless(bool fEnable) { }
        public void UIActivate(uint uState) { }
        public void Refresh() { }
        public void CreateViewWindow(IShellView? psvPrevious, nint pfs, IShellBrowser? psb, nint prcView, out nint phWnd) => phWnd = nint.Zero;
        public void DestroyViewWindow() { }
        public void GetCurrentInfo(nint pfs) { }
        public void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam) { }
        public void SaveViewState() { }
        public void SelectItem(nint pidlItem, uint uFlags) { }
        public void GetItemObject(uint uItem, in Guid riid, out object ppv) => ppv = new object();
        public void GetCurrentViewMode(out uint viewMode) => viewMode = 0;
        public void SetCurrentViewMode(uint viewMode) { }
        public void GetFolder(in Guid riid, out object ppv) => throw error;
        public void Item(int iItemIndex, out nint ppidl) => throw error;
        public void ItemCount(uint uFlags, out int pcItems) => throw error;
        public void Items(uint uFlags, in Guid riid, out object ppv) => throw error;
        public void GetSelectionMarkedItem(out int piItem) => throw error;
        public void GetFocusedItem(out int piItem) => throw error;
        public void GetItemPosition(nint pidl, out NativePoint ppt) => throw error;
        public void GetSpacing(ref NativePoint ppt) => throw error;
        public void GetDefaultSpacing(out NativePoint ppt) => throw error;
        public int GetAutoArrange() => throw error;
        public void SelectItem(int iItem, uint dwFlags) => throw error;
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) => throw error;
        public void SetGroupBy(nint key, bool fAscending) { }
        public void GetGroupBy(nint pkey, out bool pfAscending) => pfAscending = false;
        public void SetViewProperty(nint pidl, nint propkey, nint propvar) { }
        public void GetViewProperty(nint pidl, nint propkey, nint propvar) { }
        public void SetTileViewProperties(nint pidl, string pszPropList) { }
        public void SetExtendedTileViewProperties(nint pidl, string pszPropList) { }
        public void SetText(int iType, string pwszText) { }
        public void SetCurrentFolderFlags(uint dwMask, uint dwFlags) => throw error;
        public void GetCurrentFolderFlags(out uint pdwFlags) => throw error;
    }

    private sealed class HealthyFolderView((string Id, string Name, int X, int Y)[] items) : IShellView, IFolderView, IFolderView2
    {
        private uint _flags;
        public int SelectAndPositionCalls { get; private set; }
        public void GetWindow(out nint phwnd) => phwnd = nint.Zero;
        public void ContextSensitiveHelp(bool fEnterMode) { }
        public int TranslateAccelerator(nint pmsg) => 0;
        public void EnableModeless(bool fEnable) { }
        public void UIActivate(uint uState) { }
        public void Refresh() { }
        public void CreateViewWindow(IShellView? psvPrevious, nint pfs, IShellBrowser? psb, nint prcView, out nint phWnd) => phWnd = nint.Zero;
        public void DestroyViewWindow() { }
        public void GetCurrentInfo(nint pfs) { }
        public void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam) { }
        public void SaveViewState() { }
        public void SelectItem(nint pidlItem, uint uFlags) { }
        public void GetItemObject(uint uItem, in Guid riid, out object ppv) => ppv = new object();
        public void GetCurrentViewMode(out uint viewMode) => viewMode = 0;
        public void SetCurrentViewMode(uint viewMode) { }
        public void GetFolder(in Guid riid, out object ppv) => ppv = new HealthyShellFolder(items);
        public void Item(int iItemIndex, out nint ppidl) => ppidl = nint.Zero;
        public void ItemCount(uint uFlags, out int pcItems) => pcItems = items.Length;
        public void Items(uint uFlags, in Guid riid, out object ppv) => ppv = new HealthyEnumIdList(items.Length);
        public void GetSelectionMarkedItem(out int piItem) => piItem = -1;
        public void GetFocusedItem(out int piItem) => piItem = -1;
        public void GetItemPosition(nint pidl, out NativePoint ppt)
        {
            int idx = Marshal.ReadInt32(pidl);
            ppt = new NativePoint { X = items[idx].X, Y = items[idx].Y };
        }
        public void GetSpacing(ref NativePoint ppt) { }
        public void GetDefaultSpacing(out NativePoint ppt) => ppt = default;
        public int GetAutoArrange() => (_flags & ShellConstants.FWF_AUTOARRANGE) != 0 ? ShellConstants.S_OK : ShellConstants.S_FALSE;
        public void SelectItem(int iItem, uint dwFlags) { }
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) => SelectAndPositionCalls++;
        public void SetGroupBy(nint key, bool fAscending) { }
        public void GetGroupBy(nint pkey, out bool pfAscending) => pfAscending = false;
        public void SetViewProperty(nint pidl, nint propkey, nint propvar) { }
        public void GetViewProperty(nint pidl, nint propkey, nint propvar) { }
        public void SetTileViewProperties(nint pidl, string pszPropList) { }
        public void SetExtendedTileViewProperties(nint pidl, string pszPropList) { }
        public void SetText(int iType, string pwszText) { }
        public void SetCurrentFolderFlags(uint dwMask, uint dwFlags) => _flags = (_flags & ~dwMask) | (dwFlags & dwMask);
        public void GetCurrentFolderFlags(out uint pdwFlags) => pdwFlags = _flags;
    }

    private sealed class HealthyShellFolder((string Id, string Name, int X, int Y)[] items) : IShellFolder
    {
        public void ParseDisplayName(nint hwnd, nint pbc, string pszDisplayName, nint pchEaten, out nint ppidl, nint pdwAttributes) => ppidl = nint.Zero;
        public int EnumObjects(nint hwnd, uint grfFlags, out IEnumIDList? ppenumIDList) { ppenumIDList = new HealthyEnumIdList(items.Length); return ShellConstants.S_OK; }
        public void BindToObject(nint pidl, nint pbc, in Guid riid, out object ppv) => ppv = this;
        public void BindToStorage(nint pidl, nint pbc, in Guid riid, out object ppv) => ppv = this;
        public int CompareIDs(nint lParam, nint pidl1, nint pidl2) => 0;
        public void CreateViewObject(nint hwndOwner, in Guid riid, out object ppv) => ppv = this;
        public void GetAttributesOf(uint cidl, nint[]? apidl, ref uint rgfInOut) { }
        public void GetUIObjectOf(nint hwndOwner, uint cidl, nint[] apidl, in Guid riid, nint rgfReserved, out object ppv) => ppv = this;
        public void GetDisplayNameOf(nint pidl, uint uFlags, out StrRet pName)
        {
            int idx = Marshal.ReadInt32(pidl);
            string text = uFlags == ShellConstants.SHGDN_FORPARSING ? items[idx].Id : items[idx].Name;
            pName = new StrRet { uType = 0u, pOleStr = Marshal.StringToCoTaskMemUni(text) };
        }
        public void SetNameOf(nint hwnd, nint pidl, string pszName, uint uFlags, out nint ppidlOut) => ppidlOut = nint.Zero;
    }

    private sealed class HealthyEnumIdList(int count) : IEnumIDList
    {
        private int _index;
        public int Next(uint celt, nint[] rgelt, out uint pceltFetched)
        {
            if (_index >= count)
            {
                pceltFetched = 0;
                return ShellConstants.S_FALSE;
            }

            nint ptr = Marshal.AllocCoTaskMem(sizeof(int));
            Marshal.WriteInt32(ptr, _index++);
            rgelt[0] = ptr;
            pceltFetched = 1;
            return ShellConstants.S_OK;
        }
        public int Skip(uint celt) => ShellConstants.S_OK;
        public void Reset() => _index = 0;
        public void Clone(out IEnumIDList ppenum) => ppenum = new HealthyEnumIdList(count);
    }

    private sealed class StubShellWindows(object dispatch) : IShellWindows
    {
        public int get_Count() => 1;
        public object? Item(object? index = null) => null;
        public object _NewEnum() => throw new NotImplementedException();
        public void Register(object? pid, int hwnd, int swClass, out int plCookie) => plCookie = 0;
        public void RegisterPending(int lThreadId, ref object? pvarloc, ref object? pvarlocRoot, int swClass, out int plCookie) => plCookie = 0;
        public void Revoke(int lCookie) { }
        public void OnNavigate(int lCookie, ref object? pvarloc) { }
        public void OnActivated(int lCookie, bool fActive) { }
        public int FindWindowSW(ref object? pvarLoc, ref object? pvarLocRoot, int swClass, out int phwnd, int swfwOptions, out object? ppdispOut)
        {
            phwnd = 1;
            ppdispOut = dispatch;
            return ShellConstants.S_OK;
        }
        public void OnCreated(int lCookie, object? punk) { }
        public void ProcessAttachDetach(bool fAttach) { }
    }

    private sealed class StubServiceProvider(object browser) : Berth.Desktop.Interop.IServiceProvider
    {
        public void QueryService(in Guid guidService, in Guid riid, out object? ppvObject) => ppvObject = browser;
    }

    private sealed class ThrowingServiceProvider(Exception error) : Berth.Desktop.Interop.IServiceProvider
    {
        public void QueryService(in Guid guidService, in Guid riid, out object? ppvObject) => throw error;
    }

    private sealed class StubShellBrowser(IShellView view) : IShellBrowser
    {
        public void GetWindow(out nint phwnd) => phwnd = nint.Zero;
        public void ContextSensitiveHelp(bool fEnterMode) { }
        public void InsertMenusSB(nint hmenuShared, nint lpMenuWidths) { }
        public void SetMenuSB(nint hmenuShared, nint holemenuRes, nint hwndActiveObject) { }
        public void RemoveMenusSB(nint hmenuShared) { }
        public void SetStatusTextSB(string? pszStatusText) { }
        public void EnableModelessSB(bool fEnable) { }
        public int TranslateAcceleratorSB(nint pmsg, ushort wID) => 0;
        public void BrowseObject(nint pidl, uint wFlags) { }
        public void GetViewStateStream(uint grfMode, out object? ppStrm) => ppStrm = null;
        public void GetControlWindow(uint id, out nint phwnd) => phwnd = nint.Zero;
        public void SendControlMsg(uint id, uint uMsg, nuint wParam, nint lParam, out nint pret) => pret = nint.Zero;
        public void QueryActiveShellView(out IShellView ppshv) => ppshv = view;
        public void OnViewWindowActive(IShellView? pshv) { }
        public void SetToolbarItems(nint lpButtons, uint nButtons, uint uFlags) { }
    }
}
