using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.Core;

public sealed class RestoreTests
{
    private const string Auto = "automatic, display change";

    private static IconPosition Icon(string name, int x, int y) => new(@"C:\Desk\" + name, name, x, y);

    private static readonly IconPosition A = Icon("a.txt", 10, 20), B = Icon("b.txt", 300, 40), C = Icon("c.txt", 900, 700);

    /// <summary>A rig whose current arrangement has a layout of <paramref name="saved"/>, then a desktop of <paramref name="now"/>.</summary>
    private static Rig Saved(IconPosition[] saved, params IconPosition[] now)
    {
        var rig = new Rig(saved);
        Assert.Equal(OperationKind.Saved, rig.Service().Save().Kind);
        rig.Desktop = new FakeDesktopIcons(now);
        return rig;
    }

    private static byte[] SeedUndo(Rig rig)
    {
        BerthPaths.EnsureFolderFor(rig.Paths.UndoFile);
        JsonFile.Write(rig.Paths.UndoFile, new UndoSnapshot(1, "0123456789abcdef", DateTimeOffset.UnixEpoch, [Icon("old.txt", 1, 1)]), BerthJson.Default.UndoSnapshot);
        return File.ReadAllBytes(rig.Paths.UndoFile);
    }

    private static UndoSnapshot ReadUndo(Rig rig) => BerthJson.Parse(File.ReadAllBytes(rig.Paths.UndoFile), BerthJson.Default.UndoSnapshot);

    private static string[] LinesAfter(Rig rig, int count) => [.. rig.LogLines().Skip(count)];

    [Fact]
    public void AnUnreachableDesktopIsRefusedFirstAndMovesNothing()
    {
        using Rig rig = Saved([A, B], A with { X = 1 }, B);
        byte[] undo = SeedUndo(rig);
        rig.Desktop.Unavailable = true;
        rig.Desktop.SetAutoArrange(true);

        Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable), rig.Service().Restore());

        Assert.Empty(rig.Desktop.Calls);
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
    }

    [Fact]
    public void NoLayoutIsRefusedBeforeAutoArrange()
    {
        using var rig = new Rig(A, B);
        rig.Desktop.SetAutoArrange(true);

        Assert.Equal(OperationResult.Refused(Refusal.NoLayout), rig.Service().Restore());

        Assert.Empty(rig.Desktop.Calls);
        Assert.False(File.Exists(rig.Paths.UndoFile));

        // An earlier undo survives the refusal byte for byte.
        byte[] undo = SeedUndo(rig);
        Assert.Equal(OperationResult.Refused(Refusal.NoLayout), rig.Service().Restore());
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
    }

    [Fact]
    public void AnUnreadableLayoutIsItsOwnRefusalAndIsKept()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        byte[] undo = SeedUndo(rig);
        File.WriteAllText(rig.File, "{");

        OperationResult result = rig.Service().Restore();

        Assert.Equal((OperationKind.Refused, Refusal.UnreadableLayout, Path.GetFileName(rig.File)), (result.Kind, result.Refusal, result.Path));
        Assert.NotNull(result.Detail);
        Assert.Equal("{", File.ReadAllText(rig.File));
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        Assert.Empty(rig.Desktop.Calls);
    }

    [Fact]
    public void AutoArrangeIsRefusedAndMovesNothing()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        byte[] undo = SeedUndo(rig);
        rig.Desktop.SetAutoArrange(true);

        Assert.Equal(OperationResult.Refused(Refusal.AutoArrange, "2 monitors"), rig.Service().Restore());

        Assert.Empty(rig.Desktop.Calls);
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
    }

    [Fact]
    public void ADesktopLostWhileReadingAutoArrangeIsUnreachable()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        int reads = rig.Monitors.Reads;

        // Lost just after the restore's capture (its second monitor read), before auto arrange is read.
        rig.Monitors.AfterRead = read =>
        {
            if (read == reads + 2)
            {
                rig.Desktop.Unavailable = true;
            }
        };

        Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable, "2 monitors"), rig.Service().Restore());
        Assert.False(File.Exists(rig.Paths.UndoFile));

        // Again with an earlier undo, which survives byte for byte.
        rig.Desktop.Unavailable = false;
        byte[] undo = SeedUndo(rig);
        reads = rig.Monitors.Reads;
        Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable, "2 monitors"), rig.Service().Restore());
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
    }

    [Fact]
    public void ADisplayChangeDuringTheCaptureRestoresTheNewArrangement()
    {
        // Each arrangement has its own layout; the displays change between the monitors and the icons.
        using var rig = new Rig(A);
        rig.Service().Save("Docked");
        rig.Monitors.Monitors = [Rig.Main];
        rig.Desktop = new FakeDesktopIcons(B);
        rig.Service().Save("Laptop");
        rig.Monitors.Monitors = [Rig.Left, Rig.Main];
        rig.Desktop = new FakeDesktopIcons(A with { X = 5 }, B with { X = 5 });
        int reads = rig.Monitors.Reads;
        rig.Monitors.AfterRead = read =>
        {
            if (read == reads + 1)
            {
                rig.Monitors.Monitors = [Rig.Main];
            }
        };

        OperationResult result = rig.Service().Restore();

        Assert.Equal(("Laptop", OperationKind.Restored), (result.LayoutName, result.Kind));
        Assert.Equal([B], Assert.Single(rig.Desktop.Calls));
        Assert.Equal(ArrangementKey.From([Rig.Main]), ReadUndo(rig).Key);
    }

    [Fact]
    public void ADisplayChangeBeforeTheWayBackIsWrittenStartsOver()
    {
        // Undocked after the capture and before the undo write: the restore starts over on the new arrangement.
        using var rig = new Rig(A);
        rig.Service().Save("Docked");
        rig.Monitors.Monitors = [Rig.Main];
        rig.Desktop = new FakeDesktopIcons(B);
        rig.Service().Save("Laptop");
        rig.Monitors.Monitors = [Rig.Left, Rig.Main];
        rig.Desktop = new FakeDesktopIcons(A with { X = 5 }, B with { X = 5 });
        int reads = rig.Monitors.Reads;
        rig.Monitors.AfterRead = read =>
        {
            if (read == reads + 2)
            {
                rig.Monitors.Monitors = [Rig.Main];
            }
        };

        OperationResult result = rig.Service().Restore();

        Assert.Equal(("Laptop", OperationKind.Restored), (result.LayoutName, result.Kind));
        Assert.Equal([B], Assert.Single(rig.Desktop.Calls));
        Assert.Equal(ArrangementKey.From([Rig.Main]), ReadUndo(rig).Key);
    }

    [Fact]
    public void ADisplayChangeAfterTheWayBackIsWrittenMovesNothing()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        byte[] undo = SeedUndo(rig);
        rig.Desktop.BeforeSet = () => throw new InvalidOperationException("nothing may move");
        int reads = rig.Monitors.Reads;
        rig.Monitors.AfterRead = read =>
        {
            if (read == reads + 3)
            {
                rig.Monitors.Monitors = [Rig.Main];
            }
        };

        int logged = rig.LogLines().Length;

        OperationResult result = rig.Service().Restore();

        Assert.Equal((OperationKind.Failed, FailureKind.DisplaysChanged, "2 monitors"), (result.Kind, result.Failure, result.LayoutName));
        Assert.Empty(rig.Desktop.Calls);
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        Assert.False(File.Exists(rig.Paths.UndoFile + ".next"));
        Assert.Equal(["2026-11-15T07:14:03Z FAIL The displays changed before any icon was moved."], LinesAfter(rig, logged));
    }

    [Fact]
    public void AFolderThatCannotBeReadIsAReadFailure()
    {
        using var rig = new Rig(A);
        Directory.CreateDirectory(rig.Paths.DataRoot);
        File.WriteAllText(rig.Paths.LayoutsFolder, "a file where the layouts folder belongs");

        OperationResult result = rig.Service().Restore();

        Assert.Equal((OperationKind.Failed, FailureKind.ReadFailed, rig.Paths.LayoutsFolder), (result.Kind, result.Failure, result.Path));
        Assert.Empty(rig.Desktop.Calls);
        string line = Assert.Single(rig.LogLines());
        Assert.StartsWith("2026-11-15T07:14:03Z FAIL Berth could not read " + rig.Paths.LayoutsFolder + ": ", line, StringComparison.Ordinal);
        Assert.EndsWith(".", line, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplaysThatKeepChangingAreRefusedAndKeepTheEarlierUndo()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        byte[] undo = SeedUndo(rig);
        rig.Monitors.AfterRead = read => rig.Monitors.Monitors = read % 2 == 0 ? [Rig.Left, Rig.Main] : [Rig.Main];

        Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable), rig.Service().Restore());
        Assert.Empty(rig.Desktop.Calls);
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
    }

    [Fact]
    public void TheWayBackIsOnDiskBeforeTheFirstMove()
    {
        IconPosition unsaved = Icon("new.txt", 50, 50);
        using Rig rig = Saved([A, B], A with { X = 1 }, B with { Y = 2 }, unsaved);
        SeedUndo(rig);
        rig.Clock.Now = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        UndoSnapshot? seen = null;
        rig.Desktop.BeforeSet = () => seen = ReadUndo(rig);

        rig.Service().Restore();

        Assert.Equal(
            new UndoSnapshot(1, rig.Key, new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero), [A with { X = 1 }, B with { Y = 2 }, unsaved]),
            seen);
    }

    [Fact]
    public void OnlySavedIconsStillOnTheDesktopAreMovedToTheirSavedPositions()
    {
        // The live ids differ in case, a live icon carries a saved icon's name, and every icon starts elsewhere.
        IconPosition liveA = new(@"c:\DESK\A.TXT", "b.txt", 600, 600);
        IconPosition liveB = new(@"C:\DESK\b.TXT", "a.txt", 700, 700);
        IconPosition unsaved = new(@"C:\Desk\other.txt", "c.txt", 800, 800);
        using Rig rig = Saved([A, B, C], liveA, liveB, unsaved);

        OperationResult result = rig.Service().Restore();

        Assert.Equal([A, B], Assert.Single(rig.Desktop.Calls));
        Assert.Equal((3, 2, 0, 1), (result.Total, result.InPlace, result.NotPlaced, result.Missing));
    }

    [Fact]
    public void CountsComeFromTheDesktopReadBack()
    {
        using Rig rig = Saved([A, B, C], A with { X = 1 }, B with { X = 2 });

        Assert.Equal(OperationResult.Restored("2 monitors", 3, 2, 0, 1), rig.Service().Restore());
    }

    [Fact]
    public void IconsWindowsMovedMakeTheRestoreAFailureWithItsCounts()
    {
        using Rig rig = Saved([A, B, C], A, B, C);
        rig.Desktop.SnapBy = 1;

        OperationResult result = rig.Service().Restore();

        Assert.Equal((OperationKind.Failed, FailureKind.NotPlaced, 3, 0, 3, 0), (result.Kind, result.Failure, result.Total, result.InPlace, result.NotPlaced, result.Missing));
    }

    [Fact]
    public void OneInPlaceOneMovedAndOneMissingAreCountedTogether()
    {
        using Rig rig = Saved([A, B, C], A with { X = 1 }, B with { X = 2 });
        rig.Desktop.SnapBy = 3;
        rig.Desktop.SnapIds = new HashSet<string>([B.Id]);

        OperationResult result = rig.Service().Restore();

        Assert.Equal((OperationKind.Failed, "2 monitors", 3, 1, 1, 1), (result.Kind, result.LayoutName, result.Total, result.InPlace, result.NotPlaced, result.Missing));
    }

    [Fact]
    public void AnEmptyLayoutAndAnAllMissingLayoutAreDone()
    {
        using (Rig empty = Saved([], A))
        {
            Assert.Equal(OperationResult.Restored("2 monitors", 0, 0, 0, 0), empty.Service().Restore());
        }

        using Rig gone = Saved([A, B]);
        Assert.Equal(OperationResult.Restored("2 monitors", 2, 0, 0, 2), gone.Service().Restore());
    }

    [Fact]
    public void AnUnreadableUndoFileIsReplaced()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        BerthPaths.EnsureFolderFor(rig.Paths.UndoFile);
        File.WriteAllText(rig.Paths.UndoFile, "{");

        Assert.Equal(OperationKind.Restored, rig.Service().Restore().Kind);
        Assert.Equal([A with { X = 1 }], ReadUndo(rig).Icons);
    }

    [Fact]
    public void AnUndoThatCannotBeWrittenMovesNothing()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        byte[] undo = SeedUndo(rig);
        OperationResult result;
        using (new FileStream(rig.Paths.UndoFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = rig.Service().Restore();
        }

        Assert.Equal((OperationKind.Failed, FailureKind.WriteFailed, rig.Paths.UndoFile), (result.Kind, result.Failure, result.Path));
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Empty(rig.Desktop.Calls);
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
    }

    [Fact]
    public void ADesktopLostWhilePlacingIsAFailureWithoutCountsThatKeepsTheWayBack()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        int logged = rig.LogLines().Length;
        rig.Desktop.UnavailableOnSet = true;

        OperationResult result = rig.Service().Restore();

        Assert.Equal((OperationKind.Failed, FailureKind.DesktopUnavailable, 0, 0, 0, 0), (result.Kind, result.Failure, result.Total, result.InPlace, result.NotPlaced, result.Missing));
        Assert.Equal([A with { X = 1 }], ReadUndo(rig).Icons);
        Assert.Equal(["2026-11-15T07:14:03Z FAIL The desktop could not be reached while the icons were being placed. Undo puts back what was there."], LinesAfter(rig, logged));
    }

    [Fact]
    public void ADesktopLostWhileReadingBackIsAFailureWithoutCounts()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        int logged = rig.LogLines().Length;
        rig.Desktop.BeforeSet = () => rig.Desktop.Unavailable = true;

        OperationResult result = rig.Service().Restore();

        Assert.Equal((OperationKind.Failed, 0), (result.Kind, result.Total));
        Assert.Equal([A with { X = 1 }], ReadUndo(rig).Icons);
        Assert.Equal(["2026-11-15T07:14:03Z FAIL The desktop could not be reached while the icons were being placed. Undo puts back what was there."], LinesAfter(rig, logged));
    }

    [Fact]
    public void AnUnexpectedErrorIsAFailureLoggedWithItsDetails()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        int logged = rig.LogLines().Length;
        rig.Desktop.BeforeSet = () => throw new InvalidOperationException("boom");

        OperationResult result = rig.Service().Restore();

        Assert.Equal((OperationKind.Failed, "boom"), (result.Kind, result.Detail));
        string[] lines = LinesAfter(rig, logged);
        Assert.Equal("2026-11-15T07:14:03Z FAIL Something went wrong: boom. The details are in the log.", lines[0]);
        Assert.Equal("  System.InvalidOperationException: boom", lines[1]);
        Assert.All(lines.Skip(1), line => Assert.StartsWith("  ", line, StringComparison.Ordinal));
    }

    [Fact]
    public void ABusyLockIsRefusedWithItsOriginAndChangesNothing()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        byte[] undo = SeedUndo(rig), layout = File.ReadAllBytes(rig.File);
        int logged = rig.LogLines().Length;
        using var holder = new MutexHolder(rig.Gate.MutexName);

        OperationResult result = rig.Service().Restore(Auto);

        Assert.Equal(OperationResult.Refused(Refusal.Busy) with { Origin = Auto }, result);
        Assert.Empty(rig.Desktop.Calls);
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        Assert.Equal(layout, File.ReadAllBytes(rig.File));
        Assert.Equal(["2026-11-15T07:14:03Z WARN Berth is busy with another save or restore. Try again in a moment. (automatic, display change)"], LinesAfter(rig, logged));
    }

    [Fact]
    public void EachRestoreLogsOneEventAndAnAutomaticOneSaysSo()
    {
        using Rig rig = Saved([A, B], A with { X = 1 }, B);
        int logged = rig.LogLines().Length;

        Assert.Equal(Auto, rig.Service().Restore(Auto).Origin);
        Assert.Null(rig.Service().Restore().Origin);
        rig.Desktop.SetAutoArrange(true);
        rig.Service().Restore(Auto);
        rig.Desktop.SetAutoArrange(false);
        rig.Desktop.SnapBy = 1;
        Assert.Equal((OperationKind.Failed, Auto), (rig.Service().Restore(Auto) is var failed ? failed.Kind : default, failed.Origin));

        Assert.Equal(
            [
                "2026-11-15T07:14:03Z INFO Restored 2 icons from \"2 monitors\". (automatic, display change)",
                "2026-11-15T07:14:03Z INFO Restored 2 icons from \"2 monitors\".",
                "2026-11-15T07:14:03Z WARN Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again. (automatic, display change)",
                "2026-11-15T07:14:03Z FAIL Restored 0 of 2 icons from \"2 monitors\". 2 did not stay where they were saved. (automatic, display change)",
            ],
            LinesAfter(rig, logged));
    }

    [Fact]
    public void ABlankOriginFailsBeforeTouchingDesktopOrUndoAndLogsFailure()
    {
        using Rig rig = Saved([A], A with { X = 1 });
        byte[] undo = SeedUndo(rig);
        int readsBefore = rig.Desktop.Reads;
        int monitorReadsBefore = rig.Monitors.Reads;
        int logged = rig.LogLines().Length;

        OperationResult result = rig.Service().Restore(" ");

        Assert.Equal(OperationKind.Failed, result.Kind);
        Assert.Equal(FailureKind.Unexpected, result.Failure);
        Assert.IsType<ArgumentException>(result.Error);
        Assert.Equal((readsBefore, monitorReadsBefore), (rig.Desktop.Reads, rig.Monitors.Reads));
        Assert.Empty(rig.Desktop.Calls);
        Assert.Equal(undo, File.ReadAllBytes(rig.Paths.UndoFile));
        string[] lines = LinesAfter(rig, logged);
        Assert.StartsWith("2026-11-15T07:14:03Z FAIL Something went wrong: ", lines[0], StringComparison.Ordinal);
        Assert.EndsWith(". The details are in the log.", lines[0], StringComparison.Ordinal);
        Assert.Contains("ArgumentException", lines[1], StringComparison.Ordinal);
    }
}
