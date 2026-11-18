using System.Text.Json;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.Core;

public sealed class LayoutEditTests
{
    private static IconPosition Icon(string name, int x, int y) => new(@"C:\Desk\" + name, name, x, y);

    private static readonly IconPosition A = Icon("a.txt", 10, 20);
    private static readonly IconPosition B = Icon("b.txt", 300, 40);

    private static LayoutService EditService(Rig rig, Action? beforeWrite = null, Action? afterWrite = null) =>
        new(new ThrowingDesktopIcons(), new ThrowingMonitors(), new LayoutStore(rig.Paths), rig.Paths, rig.Gate, new Log(rig.Paths, rig.Clock), rig.Clock)
        {
            BeforeWrite = beforeWrite,
            AfterWrite = afterWrite,
        };

    private static byte[] SeedUndo(Rig rig)
    {
        BerthPaths.EnsureFolderFor(rig.Paths.UndoFile);
        JsonFile.Write(
            rig.Paths.UndoFile,
            new UndoSnapshot(UndoSnapshot.CurrentSchemaVersion, rig.Key, DateTimeOffset.UnixEpoch, [A]),
            BerthJson.Default.UndoSnapshot);
        return File.ReadAllBytes(rig.Paths.UndoFile);
    }

    private static void SeedTwoSaves(Rig rig, string name, DateTimeOffset t1, IconPosition[] first, DateTimeOffset t2, IconPosition[] second)
    {
        rig.Clock.Now = t1;
        rig.Desktop = new FakeDesktopIcons(first);
        Assert.Equal(OperationKind.Saved, rig.Service().Save(name).Kind);
        rig.Clock.Now = t2;
        rig.Desktop = new FakeDesktopIcons(second);
        Assert.Equal(OperationKind.Saved, rig.Service().Save().Kind);
        rig.Clock.Now = new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero);
    }

    private static OperationResult Run(LayoutService service, string operation, string key) => operation switch
    {
        "Rename" => service.Rename(key, "Office"),
        "Revert" => service.Revert(key),
        "Delete" => service.Delete(key),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static string[] LinesAfter(Rig rig, int beforeCount) => [.. rig.LogLines().Skip(beforeCount)];

    [Fact]
    public void NoPreviousSaveIsADistinctRefusalFromNoLayout()
    {
        OperationResult missing = OperationResult.Refused(Refusal.NoLayout);
        OperationResult noPrevious = OperationResult.Refused(Refusal.NoPreviousSave);

        Assert.NotEqual(missing, noPrevious);
        Assert.Equal((OperationKind.Refused, Refusal.NoPreviousSave, false), (noPrevious.Kind, noPrevious.Refusal, noPrevious.Done));
    }

    [Fact]
    public void RenameStoresTrimmedNameAndPreservesOtherFields()
    {
        using var rig = new Rig();
        DateTimeOffset t1 = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);
        DateTimeOffset t2 = t1.AddHours(1);
        IconPosition[] first = [A];
        IconPosition[] second = [A with { X = 120 }, B];
        SeedTwoSaves(rig, "Work", t1, first, t2, second);
        byte[] undoBefore = SeedUndo(rig);
        int logged = rig.LogLines().Length;

        OperationResult result = EditService(rig).Rename(rig.Key, "  Docked ");

        Assert.Equal(OperationResult.Renamed("Docked"), result);
        Layout stored = new LayoutStore(rig.Paths).Find(rig.Key)!;
        Assert.Equal("Docked", stored.Name);
        Assert.Equal(rig.Key, stored.Key);
        Assert.Equal(t2, stored.SavedAt);
        Assert.Equal([Rig.Left, Rig.Main], stored.Monitors);
        Assert.Equal(second, stored.Icons);
        Assert.Equal(new PreviousSave(t1, [.. first]), stored.Previous);
        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(rig.File)))
        {
            Assert.Equal("Docked", doc.RootElement.GetProperty("name").GetString());
        }

        Assert.Equal(undoBefore, File.ReadAllBytes(rig.Paths.UndoFile));
        Assert.Equal(["2026-11-15T07:14:03Z INFO Rename: Renamed \"Docked\""], LinesAfter(rig, logged));
    }

    [Fact]
    public void RenameValidatesErrorsIgnoresBrokenUnheldFilesAndAllowsOwnNameInAnotherCase()
    {
        using var rig = new Rig(A);
        rig.Service().Save("Docked");
        string dockedKey = rig.Key;
        byte[] dockedBytes = File.ReadAllBytes(rig.Paths.LayoutFile(dockedKey));

        rig.Monitors.Monitors = [Rig.Main];
        rig.Service().Save("Laptop");
        string laptopKey = rig.Key;
        string laptopFile = rig.Paths.LayoutFile(laptopKey);
        byte[] laptopBytes = File.ReadAllBytes(laptopFile);

        // An unrelated non-held damaged layout file must not block Rename.
        File.WriteAllText(rig.Paths.LayoutFile("0123456789abcdef"), "{");

        // Empty and TooLong refuse before scanning other layouts, even when another layout is held open.
        using (new FileStream(rig.Paths.LayoutFile(dockedKey), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(OperationResult.Refused(Refusal.InvalidName, nameError: NameError.Empty), EditService(rig).Rename(laptopKey, "   "));
            Assert.Equal(OperationResult.Refused(Refusal.InvalidName, nameError: NameError.TooLong), EditService(rig).Rename(laptopKey, new string('n', LayoutRules.MaxNameLength + 1)));
            Assert.Equal(OperationResult.Refused(Refusal.InvalidName, nameError: NameError.TooLong), EditService(rig).Rename(laptopKey, new string('n', 39) + "\U0001F600"));
        }

        // Case-insensitive clash against another layout refuses and leaves both files untouched.
        int logged = rig.LogLines().Length;
        OperationResult taken = EditService(rig).Rename(laptopKey, "  DOCKED ");
        Assert.Equal(OperationResult.Refused(Refusal.InvalidName, nameError: NameError.Taken), taken);
        Assert.Equal(dockedBytes, File.ReadAllBytes(rig.Paths.LayoutFile(dockedKey)));
        Assert.Equal(laptopBytes, File.ReadAllBytes(laptopFile));
        Assert.Equal(["2026-11-15T07:14:03Z WARN Rename: Refused, InvalidName (Taken)"], LinesAfter(rig, logged));

        // While another layout is held open by another program, a name needing LoadAll refuses with UnreadableLayout.
        using (new FileStream(rig.Paths.LayoutFile(dockedKey), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            OperationResult held = EditService(rig).Rename(laptopKey, "Office");
            Assert.Equal((OperationKind.Refused, Refusal.UnreadableLayout, dockedKey + ".json"), (held.Kind, held.Refusal, held.Path));
        }

        Assert.Equal(laptopBytes, File.ReadAllBytes(laptopFile));

        // Renaming to its own name with different case is allowed; 40 UTF-16 code units with an emoji trimmed is allowed.
        Assert.Equal(OperationResult.Renamed("LAPTOP"), EditService(rig).Rename(laptopKey, "  LAPTOP "));
        Assert.Equal("LAPTOP", new LayoutStore(rig.Paths).Find(laptopKey)!.Name);

        string fortyWithEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 20));
        Assert.Equal(40, fortyWithEmoji.Length);
        Assert.Equal(OperationResult.Renamed(fortyWithEmoji), EditService(rig).Rename(laptopKey, "  " + fortyWithEmoji + "  "));
        Assert.Equal(fortyWithEmoji, new LayoutStore(rig.Paths).Find(laptopKey)!.Name);
    }

    [Fact]
    public void DoubleRevertSwapsAndReadsBackBothStatesWithoutMovingIcons()
    {
        using var rig = new Rig();
        DateTimeOffset t1 = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);
        DateTimeOffset t2 = t1.AddHours(2);
        IconPosition[] first = [A with { X = 11, Y = 22 }];
        IconPosition[] second = [B with { X = 333, Y = 444 }];
        SeedTwoSaves(rig, "Docked", t1, first, t2, second);
        string dockedKey = rig.Key;
        string dockedFile = rig.File;
        byte[] startBytes = File.ReadAllBytes(dockedFile);
        byte[] undoBefore = SeedUndo(rig);

        // A held unrelated layout never blocks Revert.
        rig.Monitors.Monitors = [Rig.Main];
        rig.Service().Save("Other");
        string otherFile = rig.File;
        byte[] otherBytes = File.ReadAllBytes(otherFile);

        OperationResult firstRevert;
        int logged = rig.LogLines().Length;
        using (new FileStream(otherFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            firstRevert = EditService(rig).Revert(dockedKey);
        }

        Assert.Equal(OperationResult.Reverted("Docked", t1), firstRevert);
        Layout afterFirst = new LayoutStore(rig.Paths).Find(dockedKey)!;
        Assert.Equal("Docked", afterFirst.Name);
        Assert.Equal(t1, afterFirst.SavedAt);
        Assert.Equal(first, afterFirst.Icons);
        Assert.Equal(new PreviousSave(t2, [.. second]), afterFirst.Previous);
        Assert.Equal(["2026-11-15T07:14:03Z INFO Revert: Reverted \"Docked\", 2026-11-15T07:00:00Z"], LinesAfter(rig, logged));

        OperationResult secondRevert = EditService(rig).Revert(dockedKey);

        Assert.Equal(OperationResult.Reverted("Docked", t2), secondRevert);
        Layout afterSecond = new LayoutStore(rig.Paths).Find(dockedKey)!;
        Assert.Equal("Docked", afterSecond.Name);
        Assert.Equal(t2, afterSecond.SavedAt);
        Assert.Equal(second, afterSecond.Icons);
        Assert.Equal(new PreviousSave(t1, [.. first]), afterSecond.Previous);
        Assert.Equal(startBytes, File.ReadAllBytes(dockedFile));
        Assert.Equal(otherBytes, File.ReadAllBytes(otherFile));
        Assert.Equal(undoBefore, File.ReadAllBytes(rig.Paths.UndoFile));
    }

    [Fact]
    public void RevertWithoutPreviousSaveRefusesWithNoPreviousSave()
    {
        using var rig = new Rig(A);
        rig.Service().Save("Single");
        byte[] before = File.ReadAllBytes(rig.File);
        int logged = rig.LogLines().Length;

        OperationResult result = EditService(rig).Revert(rig.Key);

        Assert.Equal(OperationResult.Refused(Refusal.NoPreviousSave), result);
        Assert.Equal(before, File.ReadAllBytes(rig.File));
        Assert.Equal(["2026-11-15T07:14:03Z WARN Revert: Refused, NoPreviousSave"], LinesAfter(rig, logged));
    }

    [Fact]
    public void DeleteRemovesOnlyTargetLayoutAndLeavesUndoAndOthersUntouched()
    {
        using var rig = new Rig(A);
        rig.Service().Save("Docked");
        string dockedKey = rig.Key;
        string dockedFile = rig.File;
        byte[] undoBefore = SeedUndo(rig);

        rig.Monitors.Monitors = [Rig.Main];
        rig.Service().Save("Laptop");
        string laptopFile = rig.File;
        byte[] laptopBytes = File.ReadAllBytes(laptopFile);

        OperationResult result;
        int logged = rig.LogLines().Length;
        using (new FileStream(laptopFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = EditService(rig).Delete(dockedKey);
        }

        Assert.Equal(OperationResult.Deleted("Docked"), result);
        Assert.False(File.Exists(dockedFile));
        Assert.Null(new LayoutStore(rig.Paths).Find(dockedKey));
        Assert.Equal(laptopBytes, File.ReadAllBytes(laptopFile));
        Assert.Equal(undoBefore, File.ReadAllBytes(rig.Paths.UndoFile));
        Assert.Equal(["2026-11-15T07:14:03Z INFO Delete: Deleted \"Docked\""], LinesAfter(rig, logged));
    }

    [Theory]
    [InlineData("keyOverload")]
    [InlineData("snapshotOverload")]
    public void StoreDeleteHoldsExclusiveHandleAfterByteCheckSoReplacementIsDenied(string overload)
    {
        using var rig = new Rig(A);
        rig.Service().Save("Docked");
        string targetKey = rig.Key;
        string targetFile = rig.File;

        rig.Monitors.Monitors = [Rig.Main];
        rig.Service().Save("Other");
        string otherFile = rig.File;
        byte[] otherBytes = File.ReadAllBytes(otherFile);

        var store = new LayoutStore(rig.Paths);
        LayoutSnapshot snapshot = store.Snapshot(targetKey);
        string competingFile = Path.Combine(rig.Data.Root, "competing.json");
        File.WriteAllText(competingFile, "{");
        byte[] competingBytes = File.ReadAllBytes(competingFile);

        bool checkCalled = false;
        void VerifyLockedWhileChecked()
        {
            checkCalled = true;
            Assert.ThrowsAny<IOException>(() => File.Open(targetFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            Exception moveError = Assert.ThrowsAny<Exception>(() => File.Move(competingFile, targetFile, overwrite: true));
            Assert.True(moveError is IOException or UnauthorizedAccessException, moveError.GetType().FullName);
            Assert.Throws<UnreadableLayoutException>(() => new LayoutStore(rig.Paths).Save(snapshot.Layout! with { Name = "Rival" }));
        }

        if (overload == "keyOverload")
        {
            Assert.True(store.Delete(targetKey, VerifyLockedWhileChecked));
        }
        else
        {
            store.Delete(snapshot, VerifyLockedWhileChecked);
        }

        Assert.True(checkCalled);
        Assert.False(File.Exists(targetFile));
        Assert.Equal(competingBytes, File.ReadAllBytes(competingFile));
        Assert.Equal(otherBytes, File.ReadAllBytes(otherFile));
    }

    [Fact]
    public void DeleteExpectedRefusesMismatchedBytesLinksAndMisCasedFiles()
    {
        using var data = new TestData();
        string file = Path.Combine(data.Root, "exact.json");
        File.WriteAllText(file, "first");
        byte[] stale = "other"u8.ToArray();
        Assert.Throws<TargetChangedException>(() => JsonFile.DeleteExpected(file, stale, exactName: true));
        Assert.Equal("first", File.ReadAllText(file));

        string misCased = Path.Combine(data.Root, "EXACT.JSON");
        byte[] currentBytes = File.ReadAllBytes(file);
        Assert.Throws<TargetChangedException>(() => JsonFile.DeleteExpected(misCased, currentBytes, exactName: true));
        Assert.True(File.Exists(file));

        string linkTarget = Path.Combine(data.Root, "linked.json");
        File.WriteAllText(linkTarget, "keep");
        string link = Path.Combine(data.Root, "sym.json");
        File.CreateSymbolicLink(link, linkTarget);
        byte[] linkTargetBytes = File.ReadAllBytes(linkTarget);
        Assert.Throws<TargetChangedException>(() => JsonFile.DeleteExpected(link, linkTargetBytes, exactName: true));
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal(linkTargetBytes, File.ReadAllBytes(linkTarget));

        File.Delete(linkTarget);
        Assert.Throws<TargetChangedException>(() => JsonFile.DeleteExpected(link, linkTargetBytes, exactName: true));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    [Theory]
    [InlineData("Rename")]
    [InlineData("Revert")]
    [InlineData("Delete")]
    public void NullArgumentsReturnUnexpectedAndInvalidOrUnknownKeysRefuseWithNoLayout(string operation)
    {
        using var rig = new Rig(A);
        rig.Service().Save("Docked");
        byte[] before = File.ReadAllBytes(rig.File);
        int logged = rig.LogLines().Length;

        OperationResult nullKey = Run(EditService(rig), operation, null!);
        Assert.Equal(OperationKind.Failed, nullKey.Kind);
        Assert.Equal(FailureKind.Unexpected, nullKey.Failure);
        Assert.IsType<ArgumentNullException>(nullKey.Error);
        string[] nullLines = LinesAfter(rig, logged);
        Assert.StartsWith($"2026-11-15T07:14:03Z FAIL {operation}: Failed", nullLines[0], StringComparison.Ordinal);
        Assert.Contains(nullLines.Skip(1), line => line.StartsWith("  System.ArgumentNullException:", StringComparison.Ordinal));
        Assert.Contains(nullLines.Skip(1), line => line.StartsWith("  ", StringComparison.Ordinal) && line.Contains("LayoutService", StringComparison.Ordinal));

        if (operation == "Rename")
        {
            int renameNullLogged = rig.LogLines().Length;
            OperationResult nullName = EditService(rig).Rename(rig.Key, null!);
            Assert.Equal(OperationKind.Failed, nullName.Kind);
            Assert.Equal(FailureKind.Unexpected, nullName.Failure);
            Assert.IsType<ArgumentNullException>(nullName.Error);
            Assert.StartsWith("2026-11-15T07:14:03Z FAIL Rename: Failed", LinesAfter(rig, renameNullLogged)[0], StringComparison.Ordinal);
        }

        foreach (string badKey in new[] { "", "   ", "not-a-key", "../escape", rig.Key.ToUpperInvariant(), "0123456789abcdef" })
        {
            int beforeRefusal = rig.LogLines().Length;
            OperationResult refused = Run(EditService(rig), operation, badKey);
            Assert.Equal(OperationResult.Refused(Refusal.NoLayout), refused);
            Assert.Equal([$"2026-11-15T07:14:03Z WARN {operation}: Refused, NoLayout"], LinesAfter(rig, beforeRefusal));
        }

        Assert.Equal(before, File.ReadAllBytes(rig.File));
    }

    [Theory]
    [InlineData("Rename", "brace")]
    [InlineData("Rename", "misCased")]
    [InlineData("Rename", "danglingLink")]
    [InlineData("Rename", "liveLink")]
    [InlineData("Rename", "locked")]
    [InlineData("Revert", "brace")]
    [InlineData("Revert", "misCased")]
    [InlineData("Revert", "danglingLink")]
    [InlineData("Revert", "liveLink")]
    [InlineData("Revert", "locked")]
    [InlineData("Delete", "brace")]
    [InlineData("Delete", "misCased")]
    [InlineData("Delete", "danglingLink")]
    [InlineData("Delete", "liveLink")]
    [InlineData("Delete", "locked")]
    public void UnreadableMisCasedAndLinkTargetsRefuseWithoutTouchingFiles(string operation, string kind)
    {
        using var rig = new Rig(A);
        DateTimeOffset t1 = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);
        SeedTwoSaves(rig, "Docked", t1, [A], t1.AddHours(1), [B]);
        string key = rig.Key;
        string path = rig.File;
        string realPath = path;
        byte[]? targetBytes = null;
        string linkTarget = Path.Combine(rig.Data.Root, "target.json");

        switch (kind)
        {
            case "brace":
                File.WriteAllText(path, "{");
                targetBytes = File.ReadAllBytes(path);
                break;
            case "misCased":
                realPath = Path.Combine(rig.Paths.LayoutsFolder, key.ToUpperInvariant() + ".json");
                File.Move(path, realPath);
                targetBytes = File.ReadAllBytes(realPath);
                break;
            case "danglingLink":
                File.Delete(path);
                File.WriteAllText(linkTarget, "{");
                File.CreateSymbolicLink(path, linkTarget);
                File.Delete(linkTarget);
                break;
            case "liveLink":
                File.Move(path, linkTarget);
                File.CreateSymbolicLink(path, linkTarget);
                targetBytes = File.ReadAllBytes(linkTarget);
                break;
            case "locked":
                targetBytes = File.ReadAllBytes(path);
                break;
        }

        int logged = rig.LogLines().Length;
        OperationResult result;
        using (kind == "locked" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None) : null)
        {
            result = Run(EditService(rig), operation, key);
        }

        string expectedName = kind == "misCased" ? Path.GetFileName(realPath) : Path.GetFileName(path);
        Assert.Equal((OperationKind.Refused, Refusal.UnreadableLayout, expectedName), (result.Kind, result.Refusal, result.Path));
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        if (kind == "danglingLink")
        {
            Assert.NotNull(new FileInfo(path).LinkTarget);
        }
        else if (kind == "liveLink")
        {
            Assert.NotNull(new FileInfo(path).LinkTarget);
            Assert.Equal(targetBytes, File.ReadAllBytes(linkTarget));
        }
        else
        {
            Assert.Equal(targetBytes, File.ReadAllBytes(realPath));
        }

        string added = Assert.Single(LinesAfter(rig, logged));
        Assert.StartsWith($"2026-11-15T07:14:03Z WARN {operation}: Refused, UnreadableLayout, {expectedName}: ", added, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Rename")]
    [InlineData("Revert")]
    [InlineData("Delete")]
    public void ReadOnlyLayoutFailsWithWriteFailedAndLeavesFileUnchanged(string operation)
    {
        using var rig = new Rig(A);
        DateTimeOffset t1 = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);
        SeedTwoSaves(rig, "Docked", t1, [A], t1.AddHours(1), [B]);
        byte[] before = File.ReadAllBytes(rig.File);
        int logged = rig.LogLines().Length;
        File.SetAttributes(rig.File, FileAttributes.ReadOnly);
        OperationResult result;
        try
        {
            result = Run(EditService(rig), operation, rig.Key);
        }
        finally
        {
            File.SetAttributes(rig.File, FileAttributes.Normal);
        }

        Assert.Equal((OperationKind.Failed, FailureKind.WriteFailed, rig.File), (result.Kind, result.Failure, result.Path));
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Equal(before, File.ReadAllBytes(rig.File));
        string added = Assert.Single(LinesAfter(rig, logged));
        Assert.StartsWith($"2026-11-15T07:14:03Z FAIL {operation}: Failed ", added, StringComparison.Ordinal);
        Assert.Contains(rig.File, added, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Rename", "edited")]
    [InlineData("Rename", "damaged")]
    [InlineData("Rename", "missing")]
    [InlineData("Revert", "edited")]
    [InlineData("Revert", "damaged")]
    [InlineData("Revert", "missing")]
    [InlineData("Delete", "edited")]
    [InlineData("Delete", "damaged")]
    [InlineData("Delete", "missing")]
    public void ConcurrentChangesDuringBeforeWriteNeverOverwriteOrDeleteArrivingData(string operation, string change)
    {
        using var rig = new Rig(A);
        DateTimeOffset t1 = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);
        SeedTwoSaves(rig, "Docked", t1, [A], t1.AddHours(1), [B]);
        string aside = Path.Combine(rig.Data.Root, "moved.json");
        byte[]? kept = null;

        LayoutService service = EditService(rig, beforeWrite: () =>
        {
            if (change == "edited")
            {
                var other = new LayoutStore(rig.Paths);
                other.Save(other.Find(rig.Key)! with { Name = "Edited elsewhere" });
                kept = File.ReadAllBytes(rig.File);
            }
            else if (change == "damaged")
            {
                File.WriteAllText(rig.File, "{");
                kept = File.ReadAllBytes(rig.File);
            }
            else
            {
                kept = File.ReadAllBytes(rig.File);
                File.Move(rig.File, aside);
            }
        });

        int logged = rig.LogLines().Length;
        OperationResult result = Run(service, operation, rig.Key);
        string added = Assert.Single(LinesAfter(rig, logged));

        if (change == "damaged")
        {
            Assert.Equal((OperationKind.Refused, Refusal.UnreadableLayout, Path.GetFileName(rig.File)), (result.Kind, result.Refusal, result.Path));
            Assert.Equal(kept, File.ReadAllBytes(rig.File));
            Assert.StartsWith($"2026-11-15T07:14:03Z WARN {operation}: Refused, UnreadableLayout", added, StringComparison.Ordinal);
        }
        else if (change == "edited")
        {
            Assert.Equal((OperationKind.Failed, FailureKind.WriteFailed, rig.File), (result.Kind, result.Failure, result.Path));
            Assert.Equal(kept, File.ReadAllBytes(rig.File));
            Assert.StartsWith($"2026-11-15T07:14:03Z FAIL {operation}: Failed", added, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal((OperationKind.Failed, FailureKind.WriteFailed, rig.File), (result.Kind, result.Failure, result.Path));
            Assert.Equal(kept, File.ReadAllBytes(aside));
            Assert.StartsWith($"2026-11-15T07:14:03Z FAIL {operation}: Failed", added, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Rename", "mismatch")]
    [InlineData("Rename", "unreadable")]
    [InlineData("Revert", "mismatch")]
    [InlineData("Revert", "unreadable")]
    [InlineData("Delete", "stillPresent")]
    [InlineData("Delete", "unreadable")]
    public void PostOperationReadBackFailureReportsWriteFailed(string operation, string tamper)
    {
        using var rig = new Rig(A);
        DateTimeOffset t1 = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);
        SeedTwoSaves(rig, "Docked", t1, [A], t1.AddHours(1), [B]);
        Layout baseline = new LayoutStore(rig.Paths).Find(rig.Key)!;

        LayoutService service = EditService(rig, afterWrite: () =>
        {
            if (tamper == "mismatch")
            {
                new LayoutStore(rig.Paths).Save(new LayoutStore(rig.Paths).Find(rig.Key)! with { Name = "Tampered" });
            }
            else if (tamper == "stillPresent")
            {
                new LayoutStore(rig.Paths).Save(baseline);
            }
            else
            {
                File.WriteAllText(rig.File, "{");
            }
        });

        int logged = rig.LogLines().Length;
        OperationResult result = Run(service, operation, rig.Key);

        Assert.Equal((OperationKind.Failed, FailureKind.WriteFailed, rig.File), (result.Kind, result.Failure, result.Path));
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        string added = Assert.Single(LinesAfter(rig, logged));
        Assert.StartsWith($"2026-11-15T07:14:03Z FAIL {operation}: Failed", added, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Rename")]
    [InlineData("Revert")]
    [InlineData("Delete")]
    public void UnreadableLayoutsFolderFailsWithReadFailed(string operation)
    {
        using var rig = new Rig(A);
        Directory.CreateDirectory(rig.Paths.DataRoot);
        File.WriteAllText(rig.Paths.LayoutsFolder, "file where folder belongs");
        int logged = rig.LogLines().Length;

        OperationResult result = Run(EditService(rig), operation, rig.Key);

        Assert.Equal((OperationKind.Failed, FailureKind.ReadFailed, rig.Paths.LayoutsFolder), (result.Kind, result.Failure, result.Path));
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        Assert.Equal("file where folder belongs", File.ReadAllText(rig.Paths.LayoutsFolder));
        string added = Assert.Single(LinesAfter(rig, logged));
        Assert.StartsWith($"2026-11-15T07:14:03Z FAIL {operation}: Failed, {rig.Paths.LayoutsFolder}: ", added, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Rename")]
    [InlineData("Revert")]
    [InlineData("Delete")]
    public void BusyLockRefusesWithoutReadingOrModifyingFiles(string operation)
    {
        using var rig = new Rig(A);
        DateTimeOffset t1 = new(2026, 11, 15, 7, 0, 0, TimeSpan.Zero);
        SeedTwoSaves(rig, "Docked", t1, [A], t1.AddHours(1), [B]);
        byte[] before = File.ReadAllBytes(rig.File);
        int logged = rig.LogLines().Length;
        using var holder = new MutexHolder(rig.Gate.MutexName);

        OperationResult result = Run(EditService(rig), operation, rig.Key);

        Assert.Equal(OperationResult.Refused(Refusal.Busy), result);
        Assert.Equal(before, File.ReadAllBytes(rig.File));
        Assert.Equal([$"2026-11-15T07:14:03Z WARN {operation}: Refused, Busy"], LinesAfter(rig, logged));
    }

    [Theory]
    [InlineData("Rename")]
    [InlineData("Revert")]
    [InlineData("Delete")]
    public void UnopenableLockReturnsUnexpectedFailureAndLogsStack(string operation)
    {
        using var rig = new Rig(A);
        using var squatter = new EventWaitHandle(false, EventResetMode.ManualReset, rig.Gate.MutexName);
        int logged = rig.LogLines().Length;

        OperationResult result = Run(EditService(rig), operation, rig.Key);

        Assert.Equal(OperationKind.Failed, result.Kind);
        Assert.Equal(FailureKind.Unexpected, result.Failure);
        Assert.NotNull(result.Error);
        string[] lines = LinesAfter(rig, logged);
        Assert.StartsWith($"2026-11-15T07:14:03Z FAIL {operation}: Failed", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines.Skip(1), line => line.StartsWith("  ", StringComparison.Ordinal) && line.Contains("OperationLock", StringComparison.Ordinal));
    }

    private sealed class ThrowingDesktopIcons : IDesktopIcons
    {
        public bool AutoArrange => throw new InvalidOperationException("Layout edits must never touch the desktop.");

        public IReadOnlyList<IconPosition> ReadIcons() =>
            throw new InvalidOperationException("Layout edits must never read desktop icons.");

        public void SetPositions(IReadOnlyList<IconPosition> icons) =>
            throw new InvalidOperationException("Layout edits must never move desktop icons.");

        public bool TryTurnAutoArrangeOff() =>
            throw new InvalidOperationException("Layout edits must never change AutoArrange.");
    }

    private sealed class ThrowingMonitors : IMonitors
    {
        public IReadOnlyList<MonitorInfo> Read() =>
            throw new InvalidOperationException("Layout edits must never read monitors.");
    }
}
