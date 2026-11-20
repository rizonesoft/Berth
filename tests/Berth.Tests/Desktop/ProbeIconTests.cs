using System.Diagnostics;
using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Desktop.Interop;
using Berth.Tests.Fakes;

namespace Berth.Tests.Desktop;

public sealed class ProbeIconUnitTests
{
    [Fact]
    public void ProbeIconDeletesOnlyOwnedFileOnDisposeMidTestExceptionReadFailureTimeoutAndPreservesCollisionAndDecoy()
    {
        using var tempDesktop = new TestData();
        string decoyPath = Path.Combine(tempDesktop.Root, "berth-test-probe-decoy.txt");
        File.WriteAllText(decoyPath, "decoy-bytes");

        // 1) Normal dispose removes only the created file; double Dispose is idempotent; decoy untouched.
        string prefix1 = $"berth-test-probe-{Guid.NewGuid():N}";
        string path1 = Path.Combine(tempDesktop.Root, $"{prefix1}.txt");
        var fake1 = new FakeDesktopIcons(new IconPosition(path1, prefix1, 100, 120));
        ProbeIcon probe1 = ProbeIcon.CreateInFolder(fake1, tempDesktop.Root, prefix1, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
        Assert.True(File.Exists(path1));
        probe1.Dispose();
        probe1.Dispose();
        Assert.False(File.Exists(path1));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));

        // 2) Mid-test exception still removes the probe file; decoy untouched.
        string prefix2 = $"berth-test-probe-{Guid.NewGuid():N}";
        string path2 = Path.Combine(tempDesktop.Root, $"{prefix2}.txt");
        var fake2 = new FakeDesktopIcons(new IconPosition(path2, prefix2, 100, 120));
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using ProbeIcon _ = ProbeIcon.CreateInFolder(fake2, tempDesktop.Root, prefix2, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
            Assert.True(File.Exists(path2));
            throw new InvalidOperationException("Simulated mid-test failure.");
        }));
        Assert.False(File.Exists(path2));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));

        // 3) Forced startup timeout deletes created file; decoy untouched.
        string prefix3 = $"berth-test-probe-{Guid.NewGuid():N}";
        string path3 = Path.Combine(tempDesktop.Root, $"{prefix3}.txt");
        var emptyFake = new FakeDesktopIcons();
        Assert.Throws<TimeoutException>(() =>
            ProbeIcon.CreateInFolder(emptyFake, tempDesktop.Root, prefix3, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(15)));
        Assert.False(File.Exists(path3));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));

        // 4) Post-creation ReadIcons() failure deletes created file; decoy untouched.
        string prefix4 = $"berth-test-probe-{Guid.NewGuid():N}";
        string path4 = Path.Combine(tempDesktop.Root, $"{prefix4}.txt");
        var failingFake = new FakeDesktopIcons { Unavailable = true };
        Assert.Throws<DesktopUnavailableException>(() =>
            ProbeIcon.CreateInFolder(failingFake, tempDesktop.Root, prefix4, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)));
        Assert.False(File.Exists(path4));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));

        // 5) Exact-name collision rejects creation and NEVER deletes the pre-existing file.
        string collisionPrefix = $"berth-test-probe-{Guid.NewGuid():N}";
        string collisionPath = Path.Combine(tempDesktop.Root, $"{collisionPrefix}.txt");
        File.WriteAllText(collisionPath, "pre-existing-owner-bytes");
        Assert.Throws<IOException>(() =>
            ProbeIcon.CreateInFolder(emptyFake, tempDesktop.Root, collisionPrefix, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)));
        Assert.True(File.Exists(collisionPath));
        Assert.Equal("pre-existing-owner-bytes", File.ReadAllText(collisionPath));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));
    }
}

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed partial class ProbeIconTests
{
    private const uint FwfSnapToGrid = 0x00080000u;

    [Fact]
    public void RealDesktopRoundTripMovesProbePreservesSelectionAndRunsFullLayoutServiceCycle()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var log = new Log(paths, TimeProvider.System);
        var monitors = new SystemMonitors();
        using var shell = new ShellDesktopIcons(monitors, log);

        if (shell.AutoArrange)
        {
            Assert.Skip("Desktop Auto Arrange is enabled; real-desktop probe positioning requires Auto Arrange off.");
        }

        (bool snapToGrid, int spacingX, int spacingY) = ReadGridInfo(shell);
        IReadOnlyList<MonitorInfo> currentMonitors = monitors.Read();
        string arrangementKey = ArrangementKey.From(currentMonitors);

        string probeFilePath;
        using (ProbeIcon probe = ProbeIcon.Create(shell))
        {
            probeFilePath = probe.FilePath;
            IconPosition initial = probe.Icon;
            HashSet<string> selectionBaseline = ReadSelectionIds(shell);

            // Part A: move probe in-bounds by at least max(200, spacingX * 2 + 1) alongside an unknown Id.
            int stepX = snapToGrid ? Math.Max(200, spacingX * 2 + 1) : 200;
            (int target1X, int target1Y) = ChooseInBoundsTarget(initial.X, initial.Y, stepX, 0, currentMonitors);
            probe.ScopedDesktop.SetPositions(
            [
                initial with { X = target1X, Y = target1Y },
                new IconPosition(@"C:\NonExistent\berth-unknown-probe-id.txt", "Unknown", 40, 40),
            ]);

            IconPosition posA = WaitForPosition(
                probe.ScopedDesktop,
                initial.Id,
                candidate => IsWithinAcceptance(candidate.X, candidate.Y, target1X, target1Y, snapToGrid, spacingX, spacingY)
                             && !IsWithinAcceptance(candidate.X, candidate.Y, initial.X, initial.Y, snapToGrid, spacingX, spacingY));

            bool exact1 = posA.X == target1X && posA.Y == target1Y;
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Move 1: initial=({initial.X},{initial.Y}), requested=({target1X},{target1Y}), landed=({posA.X},{posA.Y}), snapToGrid={snapToGrid}, spacing=({spacingX},{spacingY}), mode={(exact1 ? "exact" : "grid-snapped")}");

            Assert.True(
                IsWithinAcceptance(posA.X, posA.Y, target1X, target1Y, snapToGrid, spacingX, spacingY),
                $"Probe landed at ({posA.X},{posA.Y}), outside expected ({target1X},{target1Y}) tolerance ({spacingX},{spacingY}).");
            Assert.True(
                selectionBaseline.SetEquals(ReadSelectionIds(shell)),
                "Desktop selection identities changed after Move 1.");

            // Part B: full LayoutService cycle (Save -> Move -> Restore -> Undo) using Explorer's reported positions.
            var store = new LayoutStore(paths);
            var opLock = new OperationLock(paths, TimeSpan.FromSeconds(5));
            var service = new LayoutService(probe.ScopedDesktop, monitors, store, paths, opLock, log, TimeProvider.System);

            OperationResult saveResult = service.Save();
            Assert.Equal(OperationKind.Saved, saveResult.Kind);
            Assert.Equal(1, saveResult.Total);

            Layout? savedLayout = store.Snapshot(arrangementKey).Layout;
            Assert.NotNull(savedLayout);
            Assert.Equal(arrangementKey, savedLayout.Key);
            IconPosition savedIcon = Assert.Single(savedLayout.Icons);
            Assert.Equal(initial.Id, savedIcon.Id, StringComparer.OrdinalIgnoreCase);
            Assert.Equal((posA.X, posA.Y), (savedIcon.X, savedIcon.Y));

            // Move probe to a distinct second in-bounds position outside posA's acceptance region.
            int stepY = snapToGrid ? Math.Max(200, spacingY * 2 + 1) : 200;
            (int target2X, int target2Y) = ChooseInBoundsTarget(posA.X, posA.Y, 0, stepY, currentMonitors);
            probe.ScopedDesktop.SetPositions([posA with { X = target2X, Y = target2Y }]);

            IconPosition posB = WaitForPosition(
                probe.ScopedDesktop,
                initial.Id,
                candidate => IsWithinAcceptance(candidate.X, candidate.Y, target2X, target2Y, snapToGrid, spacingX, spacingY)
                             && !IsWithinAcceptance(candidate.X, candidate.Y, posA.X, posA.Y, snapToGrid, spacingX, spacingY));

            Assert.False(
                IsWithinAcceptance(posB.X, posB.Y, posA.X, posA.Y, snapToGrid, spacingX, spacingY),
                $"Move 2 landed at ({posB.X},{posB.Y}), still within posA ({posA.X},{posA.Y}) acceptance region.");
            Assert.True(
                selectionBaseline.SetEquals(ReadSelectionIds(shell)),
                "Desktop selection identities changed after Move 2.");

            // Restore: returns probe to exact Explorer-reported posA and writes undo.json with posB.
            OperationResult restoreResult = service.Restore();
            Assert.Equal(OperationKind.Restored, restoreResult.Kind);
            Assert.Equal(1, restoreResult.InPlace);
            Assert.Equal(0, restoreResult.NotPlaced);
            Assert.Equal(0, restoreResult.Missing);
            Assert.True(service.CanUndo);
            Assert.True(File.Exists(paths.UndoFile));

            IconPosition afterRestore = Assert.Single(probe.ScopedDesktop.ReadIcons());
            Assert.Equal((posA.X, posA.Y), (afterRestore.X, afterRestore.Y));
            Assert.True(
                selectionBaseline.SetEquals(ReadSelectionIds(shell)),
                "Desktop selection identities changed after Restore.");

            UndoSnapshot? undoSnapshot = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(paths.UndoFile),
                BerthJson.Default.UndoSnapshot);
            Assert.NotNull(undoSnapshot);
            Assert.Equal(arrangementKey, undoSnapshot.Key);
            IconPosition undoIcon = Assert.Single(undoSnapshot.Icons);
            Assert.Equal(initial.Id, undoIcon.Id, StringComparer.OrdinalIgnoreCase);
            Assert.Equal((posB.X, posB.Y), (undoIcon.X, undoIcon.Y));

            // Undo: returns probe to exact Explorer-reported posB and deletes undo.json.
            OperationResult undoResult = service.Undo();
            Assert.Equal(OperationKind.Undone, undoResult.Kind);
            Assert.Equal(1, undoResult.InPlace);
            Assert.Equal(0, undoResult.NotPlaced);
            Assert.Equal(0, undoResult.Missing);
            Assert.False(File.Exists(paths.UndoFile));
            Assert.False(service.CanUndo);

            IconPosition afterUndo = Assert.Single(probe.ScopedDesktop.ReadIcons());
            Assert.Equal((posB.X, posB.Y), (afterUndo.X, afterUndo.Y));
            Assert.True(
                selectionBaseline.SetEquals(ReadSelectionIds(shell)),
                "Desktop selection identities changed after Undo.");
        }

        Assert.False(File.Exists(probeFilePath), "ProbeIcon.Dispose left the probe file on the desktop.");
    }

    private static (bool SnapToGrid, int SpacingX, int SpacingY) ReadGridInfo(ShellDesktopIcons shell) =>
        shell.RunWithView(desktop =>
        {
            bool snapToGrid = false;
            if (desktop.View2 is not null)
            {
                desktop.View2.GetCurrentFolderFlags(out uint flags);
                snapToGrid = (flags & FwfSnapToGrid) != 0;
            }

            NativePoint spacing = default;
            desktop.View.GetSpacing(ref spacing);
            if (spacing.X <= 0 || spacing.Y <= 0)
            {
                desktop.View.GetDefaultSpacing(out spacing);
            }

            Assert.True(spacing.X > 0 && spacing.Y > 0, $"Expected positive desktop grid spacing, got ({spacing.X},{spacing.Y}).");
            return (snapToGrid, spacing.X, spacing.Y);
        });

    private static HashSet<string> ReadSelectionIds(ShellDesktopIcons shell) =>
        shell.RunWithView(desktop =>
        {
            object? folderObj = null;
            object? enumObj = null;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                desktop.View.ItemCount(ShellConstants.SVGIO_SELECTION, out int selectedCount);
                if (selectedCount == 0)
                {
                    return ids;
                }

                Guid folderIid = typeof(IShellFolder).GUID;
                desktop.View.GetFolder(in folderIid, out folderObj);
                var folder = (IShellFolder)folderObj!;

                Guid enumIid = typeof(IEnumIDList).GUID;
                try
                {
                    desktop.View.Items(ShellConstants.SVGIO_SELECTION, in enumIid, out enumObj);
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x80070490))
                {
                    return ids;
                }

                var enumerator = (IEnumIDList)enumObj!;

                var batch = new nint[1];
                while (true)
                {
                    batch[0] = nint.Zero;
                    int hr = enumerator.Next(1, batch, out uint fetched);
                    if (hr < 0 || fetched == 0)
                    {
                        break;
                    }

                    nint pidl = batch[0];
                    try
                    {
                        folder.GetDisplayNameOf(pidl, ShellConstants.SHGDN_FORPARSING, out StrRet parsingRet);
                        string id = ShellDesktopIcons.DecodeStrRet(ref parsingRet, pidl, Marshal.FreeCoTaskMem);
                        if (!string.IsNullOrEmpty(id))
                        {
                            ids.Add(id);
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pidl);
                    }
                }

                return ids;
            }
            finally
            {
                if (enumObj is not null && Marshal.IsComObject(enumObj))
                {
                    _ = Marshal.ReleaseComObject(enumObj);
                }

                if (folderObj is not null && Marshal.IsComObject(folderObj))
                {
                    _ = Marshal.ReleaseComObject(folderObj);
                }
            }
        });

    private static (int X, int Y) ChooseInBoundsTarget(
        int startX,
        int startY,
        int deltaX,
        int deltaY,
        IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo host = monitors.FirstOrDefault(m =>
            startX >= m.X && startX < m.X + m.Width && startY >= m.Y && startY < m.Y + m.Height)
            ?? monitors.First(m => m.Primary);

        const int margin = 120;
        int minX = host.X + margin;
        int maxX = host.X + host.Width - margin;
        int minY = host.Y + margin;
        int maxY = host.Y + host.Height - margin;

        int targetX = startX + deltaX;
        if (deltaX != 0 && (targetX > maxX || targetX < minX))
        {
            targetX = startX - deltaX;
        }

        int targetY = startY + deltaY;
        if (deltaY != 0 && (targetY > maxY || targetY < minY))
        {
            targetY = startY - deltaY;
        }

        targetX = Math.Clamp(targetX, minX, maxX);
        targetY = Math.Clamp(targetY, minY, maxY);
        return (targetX, targetY);
    }

    private static bool IsWithinAcceptance(
        int actualX,
        int actualY,
        int targetX,
        int targetY,
        bool snapToGrid,
        int spacingX,
        int spacingY)
    {
        if (!snapToGrid)
        {
            return actualX == targetX && actualY == targetY;
        }

        return Math.Abs(actualX - targetX) <= spacingX && Math.Abs(actualY - targetY) <= spacingY;
    }

    private static IconPosition WaitForPosition(
        ScopedDesktopIcons scoped,
        string expectedId,
        Func<IconPosition, bool> predicate)
    {
        var elapsed = Stopwatch.StartNew();
        IconPosition last = Assert.Single(scoped.ReadIcons());
        while (elapsed.Elapsed < TimeSpan.FromSeconds(2))
        {
            if (string.Equals(last.Id, expectedId, StringComparison.OrdinalIgnoreCase) && predicate(last))
            {
                return last;
            }

            Thread.Sleep(50);
            last = Assert.Single(scoped.ReadIcons());
        }

        return last;
    }
}
