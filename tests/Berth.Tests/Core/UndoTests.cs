using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.Core;

public sealed class UndoTests
{
    private static IconPosition Icon(string name, int x, int y) => new(@"C:\Desk\" + name, name, x, y);

    private static readonly IconPosition A = Icon("a.txt", 10, 20), B = Icon("b.txt", 300, 40), C = Icon("c.txt", 900, 700);

    private static byte[] SeedUndo(Rig rig, string? key = null, params IconPosition[] icons)
    {
        BerthPaths.EnsureFolderFor(rig.Paths.UndoFile);
        JsonFile.Write(
            rig.Paths.UndoFile,
            new UndoSnapshot(UndoSnapshot.CurrentSchemaVersion, key ?? rig.Key, DateTimeOffset.UnixEpoch, [.. icons]),
            BerthJson.Default.UndoSnapshot);
        return File.ReadAllBytes(rig.Paths.UndoFile);
    }

    private static string[] LinesAfter(Rig rig, int count) => [.. rig.LogLines().Skip(count)];

    [Fact]
    public void ARestoreFollowedByAnUndoReturnsEveryIconWhereItWasAndDeletesTheSnapshot()
    {
        IconPosition savedA = A, savedB = B;
        IconPosition beforeA = A with { X = 111, Y = 222 }, beforeB = B with { X = 333, Y = 444 };
        IconPosition unsaved = Icon("new.txt", 50, 50);
        using var rig = new Rig(savedA, savedB);
        Assert.False(rig.Service().CanUndo);
        Assert.Equal(OperationKind.Saved, rig.Service().Save().Kind);

        rig.Desktop = new FakeDesktopIcons(beforeA, beforeB, unsaved);
        Assert.Equal(OperationKind.Restored, rig.Service().Restore().Kind);
        Assert.True(rig.Service().CanUndo);
        Assert.True(File.Exists(rig.Paths.UndoFile));

        // Even if the unsaved icon shifted after the restore, undo puts the whole pre-restore desktop back.
        ((IDesktopIcons)rig.Desktop).SetPositions([unsaved with { X = 999, Y = 999 }]);

        OperationResult result = rig.Service().Undo();

        Assert.Equal(OperationResult.Undone(3, 3, 0, 0), result);
        Assert.Equal([beforeA, beforeB, unsaved], ((IDesktopIcons)rig.Desktop).ReadIcons());
        Assert.False(File.Exists(rig.Paths.UndoFile));
        Assert.False(rig.Service().CanUndo);

        OperationResult second = rig.Service().Undo();
        Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), second);
        Assert.Single(rig.Desktop.Calls.Skip(2));
    }

    [Fact]
    public void AMixedRetryCasePreservesTheSnapshotAndFinishesOnAFreshService()
    {
        IconPosition liveA = new(@"c:\DESK\A.TXT", "b.txt", 600, 600);
        IconPosition liveB = new(@"C:\DESK\b.TXT", "a.txt", 700, 700);
        IconPosition desktopOnly = new(@"C:\Desk\other.txt", "c.txt", 800, 800);
        using var rig = new Rig(liveA, liveB, desktopOnly);
        byte[] undo = SeedUndo(rig, rig.Key, A, B, C);
        rig.Desktop.SnapBy = 1;
        rig.Desktop.SnapIds = new HashSet<string>([B.Id]);

        OperationResult first = rig.Service().Undo();

        Assert.Equal([A, B], Assert.Single(rig.Desktop.Calls));
        Assert.Equal(
            (OperationKind.Failed, FailureKind.NotPlaced, 3, 1, 1, 1),
            (first.Kind, first.Failure, first.Total, first.InPlace, first.NotPlaced, first.Missing));
        Assert.Equal(desktopOnly, ((IDesktopIcons)rig.Desktop).ReadIcons()[2]);
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        Assert.True(rig.Service().CanUndo);

        // Remove the obstruction and retry through a fresh service on the same data root.
        rig.Desktop.SnapBy = 0;
        rig.Desktop.SnapIds = null;
        LayoutService fresh = new(rig.Desktop, rig.Monitors, new LayoutStore(rig.Paths), rig.Paths, rig.Gate, new Log(rig.Paths, rig.Clock), rig.Clock);

        OperationResult second = fresh.Undo();

        Assert.Equal(OperationResult.Undone(3, 2, 0, 1), second);
        IconPosition[] now = [.. ((IDesktopIcons)rig.Desktop).ReadIcons()];
        Assert.Equal((A.X, A.Y), (now[0].X, now[0].Y));
        Assert.Equal((B.X, B.Y), (now[1].X, now[1].Y));
        Assert.Equal(desktopOnly, now[2]);
        Assert.False(File.Exists(rig.Paths.UndoFile));
        Assert.False(fresh.CanUndo);
        Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), fresh.Undo());
    }

    [Fact]
    public void IconsWindowsMovedMakeTheUndoAFailureAndKeepTheSnapshot()
    {
        using var rig = new Rig(A, B, C);
        byte[] undo = SeedUndo(rig, rig.Key, A, B, C);
        rig.Desktop.SnapBy = 1;

        OperationResult result = rig.Service().Undo();

        Assert.Equal(
            (OperationKind.Failed, FailureKind.NotPlaced, 3, 0, 3, 0),
            (result.Kind, result.Failure, result.Total, result.InPlace, result.NotPlaced, result.Missing));
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
    }

    [Fact]
    public void AnEmptySnapshotAndAnAllMissingSnapshotAreUndoneAndDeleteTheSnapshot()
    {
        using (var empty = new Rig(A))
        {
            SeedUndo(empty, empty.Key);
            Assert.Equal(OperationResult.Undone(0, 0, 0, 0), empty.Service().Undo());
            Assert.False(File.Exists(empty.Paths.UndoFile));
        }

        using var gone = new Rig(C);
        SeedUndo(gone, gone.Key, A, B);
        Assert.Equal(OperationResult.Undone(2, 0, 0, 2), gone.Service().Undo());
        Assert.False(File.Exists(gone.Paths.UndoFile));
    }

    [Fact]
    public void CanUndoLifecycleNeverTouchesTheDesktop()
    {
        using var rig = new Rig();
        var strict = new ThrowingDesktopIcons();
        var service = new LayoutService(strict, rig.Monitors, new LayoutStore(rig.Paths), rig.Paths, rig.Gate, new Log(rig.Paths, rig.Clock), rig.Clock);

        Assert.False(service.CanUndo);

        SeedUndo(rig, rig.Key, A);
        Assert.True(service.CanUndo);

        rig.Monitors.Monitors = [Rig.Main];
        Assert.False(service.CanUndo);

        rig.Monitors.Monitors = [Rig.Left, Rig.Main];
        rig.Monitors.Unavailable = true;
        Assert.False(service.CanUndo);

        rig.Monitors.Unavailable = false;
        Assert.True(service.CanUndo);

        // Remains true after a failed NotPlaced undo and turns false after a completed undo.
        rig.Desktop = new FakeDesktopIcons(A with { X = 1 });
        rig.Desktop.SnapBy = 1;
        Assert.Equal(OperationKind.Failed, rig.Service().Undo().Kind);
        Assert.True(service.CanUndo);

        rig.Desktop.SnapBy = 0;
        Assert.Equal(OperationKind.Undone, rig.Service().Undo().Kind);
        Assert.False(service.CanUndo);
    }

    [Theory]
    [MemberData(nameof(InvalidSnapshotCases))]
    public void InvalidOrForeignSnapshotsRefuseWithNothingToUndoAndDisableCanUndo(string kind, string content)
    {
        using var rig = new Rig(A);
        var strict = new ThrowingDesktopIcons();
        var strictService = new LayoutService(strict, rig.Monitors, new LayoutStore(rig.Paths), rig.Paths, rig.Gate, new Log(rig.Paths, rig.Clock), rig.Clock);

        byte[]? seeded = PrepareInvalidUndo(rig, kind, content);

        Assert.False(strictService.CanUndo);
        Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), rig.Service().Undo());
        Assert.Empty(rig.Desktop.Calls);
        if (seeded is not null)
        {
            Assert.Equal(seeded, File.ReadAllBytes(rig.Paths.UndoFile));
        }
    }

    [Fact]
    public void ALockedSnapshotRefusesWithNothingToUndoAndDisablesCanUndo()
    {
        using var rig = new Rig(A);
        var strict = new ThrowingDesktopIcons();
        var strictService = new LayoutService(strict, rig.Monitors, new LayoutStore(rig.Paths), rig.Paths, rig.Gate, new Log(rig.Paths, rig.Clock), rig.Clock);
        byte[] seeded = SeedUndo(rig, rig.Key, A);

        bool canUndoWhileLocked;
        OperationResult undoWhileLocked;
        using (new FileStream(rig.Paths.UndoFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            canUndoWhileLocked = strictService.CanUndo;
            undoWhileLocked = rig.Service().Undo();
        }

        Assert.False(canUndoWhileLocked);
        Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), undoWhileLocked);
        Assert.Empty(rig.Desktop.Calls);
        Assert.Equal(seeded, File.ReadAllBytes(rig.Paths.UndoFile));
    }

    [Fact]
    public void ADirectoryAtTheUndoPathRefusesWithNothingToUndoAndDisablesCanUndo()
    {
        using var rig = new Rig(A);
        var strict = new ThrowingDesktopIcons();
        var strictService = new LayoutService(strict, rig.Monitors, new LayoutStore(rig.Paths), rig.Paths, rig.Gate, new Log(rig.Paths, rig.Clock), rig.Clock);
        Directory.CreateDirectory(rig.Paths.UndoFile);

        Assert.False(strictService.CanUndo);
        Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), rig.Service().Undo());
        Assert.Empty(rig.Desktop.Calls);
        Assert.True(Directory.Exists(rig.Paths.UndoFile));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASymlinkAtTheUndoPathRefusesWithNothingToUndoAndDisablesCanUndo(bool liveTarget)
    {
        using var rig = new Rig(A);
        var strict = new ThrowingDesktopIcons();
        var strictService = new LayoutService(strict, rig.Monitors, new LayoutStore(rig.Paths), rig.Paths, rig.Gate, new Log(rig.Paths, rig.Clock), rig.Clock);
        BerthPaths.EnsureFolderFor(rig.Paths.UndoFile);
        string target = Path.Combine(rig.Data.Root, "elsewhere-undo.json");
        JsonFile.Write(target, new UndoSnapshot(1, rig.Key, DateTimeOffset.UnixEpoch, [A]), BerthJson.Default.UndoSnapshot);
        byte[] targetBytes = File.ReadAllBytes(target);
        File.CreateSymbolicLink(rig.Paths.UndoFile, target);
        if (!liveTarget)
        {
            File.Delete(target);
        }

        Assert.False(strictService.CanUndo);
        Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), rig.Service().Undo());
        Assert.Empty(rig.Desktop.Calls);
        Assert.NotNull(new FileInfo(rig.Paths.UndoFile).LinkTarget);
        if (liveTarget)
        {
            Assert.Equal(targetBytes, File.ReadAllBytes(target));
        }
    }

    [Fact]
    public void RefusalPrecedenceMatchesSpecAndPreservesAnySnapshot()
    {
        // Unreachable desktop comes before NothingToUndo and AutoArrange, even with no undo file.
        using (var rig = new Rig(A))
        {
            rig.Desktop.Unavailable = true;
            rig.Desktop.SetAutoArrange(true);
            Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable), rig.Service().Undo());
            Assert.Empty(rig.Desktop.Calls);
        }

        using (var rig = new Rig(A))
        {
            rig.Monitors.Unavailable = true;
            rig.Desktop.SetAutoArrange(true);
            byte[] undo = SeedUndo(rig, ArrangementKey.From([Rig.Main]), A);
            Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable), rig.Service().Undo());
            Assert.Empty(rig.Desktop.Calls);
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // NothingToUndo comes before AutoArrange, both for missing and foreign snapshots.
        using (var rig = new Rig(A))
        {
            rig.Desktop.SetAutoArrange(true);
            Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), rig.Service().Undo());

            byte[] foreign = SeedUndo(rig, ArrangementKey.From([Rig.Main]), A);
            Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), rig.Service().Undo());
            Assert.Empty(rig.Desktop.Calls);
            Assert.Equal(foreign, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // AutoArrange is refused when a matching snapshot exists, preserving its bytes.
        using (var rig = new Rig(A))
        {
            byte[] undo = SeedUndo(rig, rig.Key, A);
            rig.Desktop.SetAutoArrange(true);
            Assert.Equal(OperationResult.Refused(Refusal.AutoArrange), rig.Service().Undo());
            Assert.Empty(rig.Desktop.Calls);
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // Desktop lost while reading AutoArrange refuses as DesktopUnavailable and preserves the snapshot.
        using (var rig = new Rig(A))
        {
            byte[] undo = SeedUndo(rig, rig.Key, A);
            int reads = rig.Monitors.Reads;
            rig.Monitors.AfterRead = read =>
            {
                if (read == reads + 2)
                {
                    rig.Desktop.Unavailable = true;
                }
            };
            Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable), rig.Service().Undo());
            Assert.Empty(rig.Desktop.Calls);
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        }
    }

    [Fact]
    public void DisplayChangesBeforeAndDuringUndoBehaveSafely()
    {
        // Display change during Capture() recaptures and runs against the new arrangement.
        using (var rig = new Rig(A with { X = 1 }, B with { X = 2 }))
        {
            SeedUndo(rig, ArrangementKey.From([Rig.Main]), B);
            int reads = rig.Monitors.Reads;
            rig.Monitors.AfterRead = read =>
            {
                if (read == reads + 1)
                {
                    rig.Monitors.Monitors = [Rig.Main];
                }
            };

            OperationResult result = rig.Service().Undo();
            Assert.Equal(OperationResult.Undone(1, 1, 0, 0), result);
            Assert.Equal([B], Assert.Single(rig.Desktop.Calls));
            Assert.False(File.Exists(rig.Paths.UndoFile));
        }

        using (var rig = new Rig(A with { X = 1 }))
        {
            byte[] oldUndo = SeedUndo(rig, rig.Key, A);
            int reads = rig.Monitors.Reads;
            rig.Monitors.AfterRead = read =>
            {
                if (read == reads + 1)
                {
                    rig.Monitors.Monitors = [Rig.Main];
                }
            };

            Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), rig.Service().Undo());
            Assert.Empty(rig.Desktop.Calls);
            Assert.Equal(oldUndo, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // Display change at the pre-move StillThe(key) check recaptures and succeeds if displays return to the snapshot's arrangement.
        using (var rig = new Rig(A with { X = 1 }, B with { X = 2 }))
        {
            SeedUndo(rig, rig.Key, A, B);
            int reads = rig.Monitors.Reads;
            rig.Monitors.AfterRead = read =>
            {
                if (read == reads + 2)
                {
                    rig.Monitors.Monitors = [Rig.Main];
                }
                else if (read == reads + 3)
                {
                    rig.Monitors.Monitors = [Rig.Left, Rig.Main];
                }
            };

            OperationResult result = rig.Service().Undo();
            Assert.Equal(OperationResult.Undone(2, 2, 0, 0), result);
            Assert.Equal([A, B], Assert.Single(rig.Desktop.Calls));
            Assert.False(File.Exists(rig.Paths.UndoFile));
        }

        // Display change at the pre-move StillThe(key) check to another arrangement restarts on the new one and refuses NothingToUndo.
        using (var rig = new Rig(A with { X = 1 }))
        {
            byte[] oldUndo = SeedUndo(rig, rig.Key, A);
            int reads = rig.Monitors.Reads;
            rig.Monitors.AfterRead = read =>
            {
                if (read == reads + 2)
                {
                    rig.Monitors.Monitors = [Rig.Main];
                }
            };

            Assert.Equal(OperationResult.Refused(Refusal.NothingToUndo), rig.Service().Undo());
            Assert.Empty(rig.Desktop.Calls);
            Assert.Equal(oldUndo, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // Oscillating displays exhaust the three attempts and refuse DesktopUnavailable.
        using (var rig = new Rig(A with { X = 1 }))
        {
            byte[] undo = SeedUndo(rig, rig.Key, A);
            rig.Monitors.AfterRead = read => rig.Monitors.Monitors = read % 2 == 0 ? [Rig.Left, Rig.Main] : [Rig.Main];

            Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable), rig.Service().Undo());
            Assert.Empty(rig.Desktop.Calls);
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // Display change during SetPositions fails with DisplaysChanged, makes no second move, and preserves undo.json.
        using (var rig = new Rig(A with { X = 1 }))
        {
            byte[] undo = SeedUndo(rig, rig.Key, A);
            rig.Desktop.BeforeSet = () => rig.Monitors.Monitors = [Rig.Main];

            OperationResult result = rig.Service().Undo();

            Assert.Equal((OperationKind.Failed, FailureKind.DisplaysChanged, 0, 0, 0, 0), (result.Kind, result.Failure, result.Total, result.InPlace, result.NotPlaced, result.Missing));
            Assert.Single(rig.Desktop.Calls);
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // Display change during post-move ReadIcons() fails with DisplaysChanged, makes no second move, and preserves undo.json.
        using (var rig = new Rig(A with { X = 1 }))
        {
            byte[] undo = SeedUndo(rig, rig.Key, A);
            int reads = rig.Monitors.Reads;
            rig.Monitors.AfterRead = read =>
            {
                if (read == reads + 4)
                {
                    rig.Monitors.Monitors = [Rig.Main];
                }
            };

            OperationResult result = rig.Service().Undo();

            Assert.Equal((OperationKind.Failed, FailureKind.DisplaysChanged, 0, 0, 0, 0), (result.Kind, result.Failure, result.Total, result.InPlace, result.NotPlaced, result.Missing));
            Assert.Single(rig.Desktop.Calls);
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        }
    }

    [Fact]
    public void FailuresKeepTheSnapshotAndReportTheirExactKind()
    {
        // Held undo.json cannot be deleted after placement: WriteFailed with Path, Detail, and preserved bytes.
        using (var rig = new Rig(A with { X = 1 }))
        {
            byte[] undo = SeedUndo(rig, rig.Key, A);
            OperationResult result;
            using (new FileStream(rig.Paths.UndoFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                result = rig.Service().Undo();
            }

            Assert.Equal((OperationKind.Failed, FailureKind.WriteFailed, rig.Paths.UndoFile), (result.Kind, result.Failure, result.Path));
            Assert.False(string.IsNullOrWhiteSpace(result.Detail));
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // Explorer lost during placement.
        using (var rig = new Rig(A with { X = 1 }))
        {
            byte[] undo = SeedUndo(rig, rig.Key, A);
            rig.Desktop.UnavailableOnSet = true;

            OperationResult result = rig.Service().Undo();

            Assert.Equal((OperationKind.Failed, FailureKind.DesktopUnavailable, 0, 0, 0, 0), (result.Kind, result.Failure, result.Total, result.InPlace, result.NotPlaced, result.Missing));
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // Explorer lost during read-back.
        using (var rig = new Rig(A with { X = 1 }))
        {
            byte[] undo = SeedUndo(rig, rig.Key, A);
            rig.Desktop.BeforeSet = () => rig.Desktop.Unavailable = true;

            OperationResult result = rig.Service().Undo();

            Assert.Equal((OperationKind.Failed, FailureKind.DesktopUnavailable, 0, 0, 0, 0), (result.Kind, result.Failure, result.Total, result.InPlace, result.NotPlaced, result.Missing));
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        }

        // Unexpected exception: Failed with Unexpected, preserved undo.json, and multi-line FAIL log event (SPEC §18).
        using (var rig = new Rig(A with { X = 1 }))
        {
            byte[] undo = SeedUndo(rig, rig.Key, A);
            int logged = rig.LogLines().Length;
            rig.Desktop.BeforeSet = () => throw new InvalidOperationException("boom");

            OperationResult result = rig.Service().Undo();

            Assert.Equal((OperationKind.Failed, FailureKind.Unexpected, "boom"), (result.Kind, result.Failure, result.Detail));
            Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
            string[] lines = LinesAfter(rig, logged);
            Assert.Single(lines, line => !line.StartsWith("  ", StringComparison.Ordinal));
            Assert.Equal("2026-11-15T07:14:03Z FAIL Undo: Failed: boom", lines[0]);
            Assert.Equal("  System.InvalidOperationException: boom", lines[1]);
            Assert.True(lines.Length > 2);
            Assert.All(lines.Skip(1), line => Assert.StartsWith("  ", line, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ABusyLockIsRefusedAndReadsNothing()
    {
        using var rig = new Rig(A with { X = 1 });
        byte[] undo = SeedUndo(rig, rig.Key, A);
        int logged = rig.LogLines().Length;
        using var holder = new MutexHolder(rig.Gate.MutexName);

        OperationResult result = rig.Service().Undo();

        Assert.Equal(OperationResult.Refused(Refusal.Busy), result);
        Assert.Equal((0, 0), (rig.Desktop.Reads, rig.Monitors.Reads));
        Assert.Empty(rig.Desktop.Calls);
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        Assert.Equal(["2026-11-15T07:14:03Z WARN Undo: Refused, Busy"], LinesAfter(rig, logged));
    }

    [Fact]
    public void EachUndoLogsOneEventWithItsLevelAndMessage()
    {
        using var rig = new Rig(A with { X = 1 }, B with { X = 2 });
        int logged = rig.LogLines().Length;

        SeedUndo(rig, rig.Key, A, B);
        Assert.Equal(OperationKind.Undone, rig.Service().Undo().Kind);
        Assert.Equal(Refusal.NothingToUndo, rig.Service().Undo().Refusal);

        SeedUndo(rig, rig.Key, A, B);
        rig.Desktop.SnapBy = 1;
        Assert.Equal(OperationKind.Failed, rig.Service().Undo().Kind);

        Assert.Equal(
            [
                "2026-11-15T07:14:03Z INFO Undo: Undone, 2 of 2 in place, 0 not placed, 0 missing",
                "2026-11-15T07:14:03Z WARN Undo: Refused, NothingToUndo",
                "2026-11-15T07:14:03Z FAIL Undo: Failed, 0 of 2 in place, 2 not placed, 0 missing",
            ],
            LinesAfter(rig, logged));
    }

    public static TheoryData<string, string> InvalidSnapshotCases()
    {
        string foreignKey = ArrangementKey.From([Rig.Main]);
        string validKey = ArrangementKey.From([Rig.Left, Rig.Main]);
        return new TheoryData<string, string>
        {
            { "missing-folder", string.Empty },
            { "missing-file", string.Empty },
            { "text", "{" },
            { "bad-utf8", string.Empty },
            { "text", "null" },
            { "text", $$"""{"schemaVersion":2,"key":"{{validKey}}","takenAt":"2026-11-15T07:14:03Z","icons":[]}""" },
            { "text", """{"schemaVersion":1,"key":"not-a-key","takenAt":"2026-11-15T07:14:03Z","icons":[]}""" },
            { "text", $$"""{"schemaVersion":1,"key":"{{foreignKey}}","takenAt":"2026-11-15T07:14:03Z","icons":[]}""" },
            { "text", $$"""{"schemaVersion":1,"key":"{{validKey}}","takenAt":"2026-11-15T07:14:03Z","icons":null}""" },
            { "text", $$"""{"schemaVersion":1,"key":"{{validKey}}","takenAt":"2026-11-15T07:14:03Z","icons":[{"id":" ","name":"a","x":1,"y":2}]}""" },
            { "text", $$"""{"schemaVersion":1,"key":"{{validKey}}","takenAt":"2026-11-15T07:14:03Z","icons":[{"id":"C:\\Desk\\a.txt","name":"a","x":1,"y":2},{"id":"c:\\desk\\A.TXT","name":"b","x":3,"y":4}]}""" },
        };
    }

    private static byte[]? PrepareInvalidUndo(Rig rig, string kind, string content)
    {
        if (kind == "missing-folder")
        {
            return null;
        }

        BerthPaths.EnsureFolderFor(rig.Paths.UndoFile);
        if (kind == "missing-file")
        {
            return null;
        }

        if (kind == "bad-utf8")
        {
            byte[] bad = [0xFF];
            File.WriteAllBytes(rig.Paths.UndoFile, bad);
            return bad;
        }

        File.WriteAllText(rig.Paths.UndoFile, content);
        return File.ReadAllBytes(rig.Paths.UndoFile);
    }

    private sealed class ThrowingDesktopIcons : IDesktopIcons
    {
        public bool AutoArrange => throw new InvalidOperationException("CanUndo must never inspect AutoArrange.");

        public IReadOnlyList<IconPosition> ReadIcons() =>
            throw new InvalidOperationException("CanUndo must never read desktop icons.");

        public void SetPositions(IReadOnlyList<IconPosition> icons) =>
            throw new InvalidOperationException("CanUndo must never position desktop icons.");

        public bool TryTurnAutoArrangeOff() =>
            throw new InvalidOperationException("CanUndo must never change AutoArrange.");
    }
}
