using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.Core;

/// <summary>A clock a test moves on.</summary>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A layout service over fakes on its own temporary root.</summary>
internal sealed class Rig : IDisposable
{
    public static readonly MonitorInfo Left = new(-1920, 0, 1920, 1080, 100, false);
    public static readonly MonitorInfo Main = new(0, 0, 2560, 1440, 100, true);

    public Rig(params IconPosition[] icons)
    {
        Paths = BerthPaths.ForRoot(Data.Root);
        Desktop = new FakeDesktopIcons(icons);
        Monitors = new FakeMonitors(Left, Main);
        Gate = new OperationLock(Paths, TimeSpan.FromMilliseconds(200));
    }

    public TestData Data { get; } = new();

    public BerthPaths Paths { get; }

    public FakeDesktopIcons Desktop { get; set; }

    public FakeMonitors Monitors { get; }

    public OperationLock Gate { get; }

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 15, 7, 14, 3, 500, TimeSpan.Zero));

    public Action? BeforeWrite { get; set; }

    public string Key => ArrangementKey.From(Monitors.Monitors);

    public string File => Paths.LayoutFile(Key);

    public LayoutService Service() =>
        new(Desktop, Monitors, new LayoutStore(Paths), Paths, Gate, new Log(Paths, Clock), Clock) { BeforeWrite = BeforeWrite };

    /// <summary>The layout as a fresh store reads it from disk.</summary>
    public Layout? Stored() => new LayoutStore(Paths).Find(Key);

    public string[] LogLines() => System.IO.File.Exists(Paths.LogFile) ? System.IO.File.ReadAllLines(Paths.LogFile) : [];

    public void Dispose() => Data.Dispose();
}

public sealed class SaveTests
{
    private static IconPosition Icon(string name, int x, int y) => new(@"C:\Desk\" + name, name, x, y);

    private static readonly IconPosition[] Three = [Icon("a.txt", 10, 20), Icon("b.txt", -1800, 40), Icon("c.txt", 900, 700)];

    [Fact]
    public void AFirstSaveOnAFreshProfileWritesTheArrangementsLayout()
    {
        using var rig = new Rig(Three);
        Assert.False(Directory.Exists(rig.Paths.LayoutsFolder));
        Assert.False(Directory.Exists(rig.Paths.LogFolder));

        OperationResult result = rig.Service().Save();

        Assert.Equal(OperationResult.Saved("2 monitors", 3, replacedEarlierSave: false), result);
        Layout stored = rig.Stored()!;
        Assert.Equal(rig.Key, stored.Key);
        Assert.Equal("2 monitors", stored.Name);
        Assert.Equal([Rig.Left, Rig.Main], stored.Monitors);
        Assert.Equal(Three, stored.Icons);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), stored.SavedAt);
        Assert.Null(stored.Previous);
        Assert.Equal(["2026-11-15T07:14:03Z INFO Save: Saved \"2 monitors\", 3 icons"], rig.LogLines());
    }

    [Fact]
    public void ASecondArrangementWithAsManyMonitorsGetsTheNextName()
    {
        using var rig = new Rig(Three);
        rig.Service().Save();
        string first = rig.File;
        rig.Monitors.Monitors = [Rig.Main, new MonitorInfo(2560, 0, 1920, 1080, 100, false)];

        OperationResult result = rig.Service().Save();

        Assert.Equal("2 monitors (2)", result.LayoutName);
        Assert.NotEqual(first, rig.File);
        LayoutSet all = new LayoutStore(rig.Paths).LoadAll();
        Assert.Empty(all.Unreadable);
        Assert.Equal(["2 monitors", "2 monitors (2)"], all.Layouts.Select(l => l.Name).Order());
    }

    [Fact]
    public void AnEmptyDesktopSavesNoIcons()
    {
        using var rig = new Rig();

        OperationResult result = rig.Service().Save();

        Assert.Equal((OperationKind.Saved, 0), (result.Kind, result.Total));
        Assert.Empty(rig.Stored()!.Icons);
    }

    [Fact]
    public void ALaterSaveKeepsTheNameAndMovesTheEarlierSaveIntoPrevious()
    {
        using var rig = new Rig(Three);
        DateTimeOffset t1 = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero), t2 = t1.AddHours(1), t3 = t1.AddHours(2);
        IconPosition[] second = [.. Three.Select(i => i with { X = i.X + 5 })];
        IconPosition[] third = [.. Three.Select(i => i with { Y = i.Y + 7 })];

        rig.Clock.Now = t1;
        Assert.False(rig.Service().Save("Desk").ReplacedEarlierSave);
        rig.Clock.Now = t2;
        rig.Desktop = new FakeDesktopIcons(second);
        OperationResult result = rig.Service().Save();

        Assert.True(result.ReplacedEarlierSave);
        Layout stored = rig.Stored()!;
        Assert.Equal(("Desk", t2), (stored.Name, stored.SavedAt));
        Assert.Equal(second, stored.Icons);
        Assert.Equal(new PreviousSave(t1, [.. Three]), stored.Previous);

        rig.Clock.Now = t3;
        rig.Desktop = new FakeDesktopIcons(third);
        rig.Service().Save();

        stored = rig.Stored()!;
        Assert.Equal(third, stored.Icons);
        Assert.Equal(new PreviousSave(t2, [.. second]), stored.Previous);
    }

    [Fact]
    public void AValidEditBetweenTheReadAndTheWriteIsKept()
    {
        using var rig = new Rig(Three);
        rig.Service().Save();
        byte[]? edited = null;
        rig.BeforeWrite = () =>
        {
            var other = new LayoutStore(rig.Paths);
            other.Save(other.Find(rig.Key)! with { Name = "Edited elsewhere" });
            edited = System.IO.File.ReadAllBytes(rig.File);
        };

        OperationResult result = rig.Service().Save();

        Assert.Equal(OperationKind.Failed, result.Kind);
        Assert.Equal(rig.File, result.Path);
        Assert.Equal(edited, System.IO.File.ReadAllBytes(rig.File));
    }

    [Fact]
    public void AFileArrivingBetweenTheReadAndTheWriteIsKept()
    {
        using var rig = new Rig(Three);
        byte[]? arrived = null;
        rig.BeforeWrite = () =>
        {
            new LayoutStore(rig.Paths).Save(Layouts.Make("From sync", [Rig.Left, Rig.Main], []));
            arrived = System.IO.File.ReadAllBytes(rig.File);
        };

        OperationResult result = rig.Service().Save();

        Assert.Equal(OperationKind.Failed, result.Kind);
        Assert.Equal(arrived, System.IO.File.ReadAllBytes(rig.File));
    }

    [Theory]
    [InlineData("desktop")]
    [InlineData("monitors")]
    public void AnUnreachableDesktopIsARefusalThatWritesNothing(string unavailable)
    {
        using var rig = new Rig(Three);
        if (unavailable == "desktop")
        {
            rig.Desktop.Unavailable = true;
        }
        else
        {
            rig.Monitors.Unavailable = true;
        }

        OperationResult result = rig.Service().Save();

        Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable), result);
        Assert.False(Directory.Exists(rig.Paths.LayoutsFolder));
        Assert.Equal(["2026-11-15T07:14:03Z WARN Save: Refused, DesktopUnavailable"], rig.LogLines());
    }

    [Theory]
    [InlineData("   ", NameError.Empty)]
    [InlineData("12345678901234567890123456789012345678901", NameError.TooLong)]
    public void AnInvalidNameIsARefusalThatWritesNothing(string name, NameError error)
    {
        using var rig = new Rig(Three);

        OperationResult result = rig.Service().Save(name);

        Assert.Equal(OperationResult.Refused(Refusal.InvalidName, nameError: error), result);
        Assert.False(System.IO.File.Exists(rig.File));
    }

    [Fact]
    public void AnotherArrangementsNameInAnotherCaseIsTakenAndOwnNameIsNot()
    {
        using var rig = new Rig(Three);
        rig.Service().Save("Docked");
        string docked = rig.File;
        byte[] before = System.IO.File.ReadAllBytes(docked);
        rig.Monitors.Monitors = [Rig.Main];
        rig.Service().Save("Laptop");
        byte[] laptop = System.IO.File.ReadAllBytes(rig.File);

        Assert.Equal(OperationResult.Refused(Refusal.InvalidName, nameError: NameError.Taken), rig.Service().Save("DOCKED"));
        Assert.Equal(before, System.IO.File.ReadAllBytes(docked));
        Assert.Equal(laptop, System.IO.File.ReadAllBytes(rig.File));

        Assert.Equal(OperationKind.Saved, rig.Service().Save("LAPTOP").Kind);
        Assert.Equal("LAPTOP", rig.Stored()!.Name);
    }

    [Fact]
    public void AnUnreadableLayoutIsARefusalThatKeepsIt()
    {
        using var rig = new Rig(Three);
        BerthPaths.EnsureFolderFor(rig.File);
        System.IO.File.WriteAllText(rig.File, "{");

        OperationResult result = rig.Service().Save();

        Assert.Equal((OperationKind.Refused, Refusal.UnreadableLayout, Path.GetFileName(rig.File)), (result.Kind, result.Refusal, result.Path));
        Assert.Equal("{", System.IO.File.ReadAllText(rig.File));
    }

    [Fact]
    public void AWriteThatFailsIsAFailureThatKeepsTheFile()
    {
        using var rig = new Rig(Three);
        rig.Service().Save();
        byte[] before = System.IO.File.ReadAllBytes(rig.File);
        int logged = rig.LogLines().Length;
        System.IO.File.SetAttributes(rig.File, FileAttributes.ReadOnly);
        try
        {
            rig.Desktop = new FakeDesktopIcons(Icon("new.txt", 1, 1));

            OperationResult result = rig.Service().Save();

            Assert.Equal((OperationKind.Failed, rig.File), (result.Kind, result.Path));
            Assert.NotNull(result.Detail);
        }
        finally
        {
            System.IO.File.SetAttributes(rig.File, FileAttributes.Normal);
        }

        Assert.Equal(before, System.IO.File.ReadAllBytes(rig.File));

        // The failed call added exactly one event: its FAIL line naming the file.
        string added = Assert.Single(rig.LogLines().Skip(logged));
        Assert.StartsWith("2026-11-15T07:14:03Z FAIL Save: Failed \"2 monitors\", " + rig.File + ": ", added, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutsThatCannotBeReadAreAFailure()
    {
        using var rig = new Rig(Three);
        Directory.CreateDirectory(rig.Paths.DataRoot);
        System.IO.File.WriteAllText(rig.Paths.LayoutsFolder, "a file where the layouts folder belongs");

        OperationResult result = rig.Service().Save();

        Assert.Equal((OperationKind.Failed, rig.Paths.LayoutsFolder), (result.Kind, result.Path));
        Assert.Equal("a file where the layouts folder belongs", System.IO.File.ReadAllText(rig.Paths.LayoutsFolder));
        Assert.StartsWith("2026-11-15T07:14:03Z FAIL Save: Failed, " + rig.Paths.LayoutsFolder + ": ", Assert.Single(rig.LogLines()), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnexpectedErrorIsAFailureLoggedWithItsStack()
    {
        // Two icons whose parsing names differ only in case break the layout rules, which nothing expects here.
        using var rig = new Rig(Icon("a.txt", 1, 1), new IconPosition(@"C:\DESK\A.TXT", "A.TXT", 2, 2));

        OperationResult result = rig.Service().Save();

        Assert.Equal(OperationKind.Failed, result.Kind);
        Assert.IsType<ArgumentException>(result.Error);
        Assert.Equal(result.Error!.Message, result.Detail);
        string[] lines = rig.LogLines();
        Assert.StartsWith("2026-11-15T07:14:03Z FAIL Save: Failed", lines[0], StringComparison.Ordinal);
        Assert.Equal("  System.ArgumentException: " + result.Error.Message.Split('\n')[0].TrimEnd('\r'), lines[1]);
        Assert.Contains(lines, line => line.StartsWith("  ", StringComparison.Ordinal) && line.Contains("LayoutStore", StringComparison.Ordinal));
        Assert.False(System.IO.File.Exists(rig.File));
    }

    [Fact]
    public void ABusyLockIsARefusalThatReadsNothing()
    {
        using var rig = new Rig(Three);
        using var holder = new MutexHolder(rig.Gate.MutexName);

        OperationResult result = rig.Service().Save();

        Assert.Equal(OperationResult.Refused(Refusal.Busy), result);
        Assert.Equal((0, 0), (rig.Desktop.Reads, rig.Monitors.Reads));
        Assert.False(Directory.Exists(rig.Paths.LayoutsFolder));
        Assert.Equal(["2026-11-15T07:14:03Z WARN Save: Refused, Busy"], rig.LogLines());
    }

    [Fact]
    public void AnIconReadAcrossADisplayChangeIsTakenAgain()
    {
        // The displays change after the monitors are read and before the icons are: the icons belong to the new
        // arrangement, so the capture is taken again and saved under that arrangement's key.
        using var rig = new Rig(Three);
        rig.Monitors.AfterRead = read =>
        {
            if (read == 1)
            {
                rig.Monitors.Monitors = [Rig.Main];
            }
        };

        OperationResult result = rig.Service().Save();

        Assert.Equal(("1 monitor", OperationKind.Saved), (result.LayoutName, result.Kind));
        Assert.Equal([Rig.Main], rig.Stored()!.Monitors);
        Assert.False(System.IO.File.Exists(rig.Paths.LayoutFile(ArrangementKey.From([Rig.Left, Rig.Main]))));
    }

    [Fact]
    public void DisplaysThatKeepChangingAreARefusal()
    {
        using var rig = new Rig(Three);
        rig.Monitors.AfterRead = read => rig.Monitors.Monitors = read % 2 == 0 ? [Rig.Left, Rig.Main] : [Rig.Main];

        Assert.Equal(OperationResult.Refused(Refusal.DesktopUnavailable), rig.Service().Save());
        Assert.False(Directory.Exists(rig.Paths.LayoutsFolder));
    }

    [Fact]
    public void ALayoutAnotherProgramHoldsStopsANameBeingChosen()
    {
        using var rig = new Rig(Three);
        rig.Service().Save("Docked");
        string docked = rig.File;
        rig.Monitors.Monitors = [Rig.Main];

        OperationResult result;
        using (new FileStream(docked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = rig.Service().Save("Docked");
        }

        Assert.Equal((OperationKind.Refused, Refusal.UnreadableLayout, Path.GetFileName(docked)), (result.Kind, result.Refusal, result.Path));
        Assert.False(System.IO.File.Exists(rig.File));
    }

    [Fact]
    public void ARepeatSaveDoesNotReadTheOtherLayouts()
    {
        // Another arrangement's file held by another program would make a full scan wait; a repeat save keeps its
        // name and never looks.
        using var rig = new Rig(Three);
        rig.Service().Save();
        rig.Monitors.Monitors = [Rig.Main];
        rig.Service().Save();
        string other = rig.File;
        rig.Monitors.Monitors = [Rig.Left, Rig.Main];

        OperationResult result;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using (new FileStream(other, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = rig.Service().Save();
        }

        Assert.Equal(OperationKind.Saved, result.Kind);
        Assert.True(clock.Elapsed < JsonFile.LiveSaveWait, $"{clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ALockThatCannotBeOpenedIsAFailureNotAThrow()
    {
        // An event, not a mutex, holds the lock's name: opening the mutex fails before any work, inside the boundary.
        using var rig = new Rig(Three);
        using var squatter = new EventWaitHandle(false, EventResetMode.ManualReset, rig.Gate.MutexName);

        OperationResult result = rig.Service().Save();

        Assert.Equal(OperationKind.Failed, result.Kind);
        Assert.NotNull(result.Error);
        Assert.Single(rig.LogLines(), line => line.Contains(" FAIL Save: Failed", StringComparison.Ordinal));
    }

    [Fact]
    public void SavingWorksWhileAutoArrangeIsOn()
    {
        using var rig = new Rig(Three);
        rig.Desktop.SetAutoArrange(true);

        Assert.Equal(OperationKind.Saved, rig.Service().Save().Kind);
    }

    [Fact]
    public void AGivenNameIsStoredTrimmedOnAFirstAndALaterSave()
    {
        using var rig = new Rig(Three);

        rig.Service().Save("  Docked ");
        Assert.Equal("Docked", rig.Stored()!.Name);

        rig.Service().Save("  Office ");
        Assert.Equal("Office", rig.Stored()!.Name);
    }
}

public sealed class OperationResultTests
{
    [Fact]
    public void EachFactoryGivesItsKindAndPayload()
    {
        DateTimeOffset at = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);

        Assert.Equal((OperationKind.Saved, "D", 3, true), Of(OperationResult.Saved("D", 3, true)));
        Assert.Equal((OperationKind.Reverted, "D", at), (OperationResult.Reverted("D", at).Kind, OperationResult.Reverted("D", at).LayoutName, OperationResult.Reverted("D", at).SavedAt!.Value));
        Assert.Equal((OperationKind.Renamed, "D"), (OperationResult.Renamed("D").Kind, OperationResult.Renamed("D").LayoutName));
        Assert.Equal((OperationKind.Deleted, "D"), (OperationResult.Deleted("D").Kind, OperationResult.Deleted("D").LayoutName));
        OperationResult refused = OperationResult.Refused(Refusal.InvalidName, "D", NameError.Taken, "p", "d");
        Assert.Equal((OperationKind.Refused, Refusal.InvalidName, NameError.Taken, "p", "d"), (refused.Kind, refused.Refusal, refused.NameError, refused.Path, refused.Detail));
        var error = new InvalidOperationException("x");
        OperationResult failed = OperationResult.Failed("p", "d", "D", error);
        Assert.Equal((OperationKind.Failed, "p", "d", "D"), (failed.Kind, failed.Path, failed.Detail, failed.LayoutName));
        Assert.Same(error, failed.Error);
        Assert.Throws<ArgumentException>(() => OperationResult.Refused(Refusal.None));
    }

    [Fact]
    public void ARestoreOrUndoThatLeftIconsOutOfPlaceFailsAndKeepsItsCounts()
    {
        OperationResult restored = OperationResult.Restored("D", 10, 6, 0, 4, "automatic, display change");
        Assert.Equal((OperationKind.Restored, 10, 6, 0, 4, "automatic, display change"), (restored.Kind, restored.Total, restored.InPlace, restored.NotPlaced, restored.Missing, restored.Origin));

        OperationResult partial = OperationResult.Restored("D", 10, 6, 3, 1);
        Assert.Equal((OperationKind.Failed, "D", 10, 6, 3, 1), (partial.Kind, partial.LayoutName, partial.Total, partial.InPlace, partial.NotPlaced, partial.Missing));

        Assert.Equal(OperationKind.Undone, OperationResult.Undone(5, 5, 0, 0).Kind);
        OperationResult undo = OperationResult.Undone(5, 3, 2, 0);
        Assert.Equal((OperationKind.Failed, 5, 3, 2, 0), (undo.Kind, undo.Total, undo.InPlace, undo.NotPlaced, undo.Missing));
    }

    private static (OperationKind, string?, int, bool) Of(OperationResult r) => (r.Kind, r.LayoutName, r.Total, r.ReplacedEarlierSave);
}
