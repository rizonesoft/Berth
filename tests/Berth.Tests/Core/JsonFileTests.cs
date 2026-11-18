using System.Collections.Immutable;
using System.Text;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Core;

public sealed partial class JsonFileTests
{
    private static readonly Layout Sample = new(
        1,
        "c7f017905ce518e6",
        "Docked",
        new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero),
        [new MonitorInfo(0, 0, 2560, 1440, 100, true)],
        [new IconPosition("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin", 76, 2)],
        null);

    private static string[] Temporaries(string folder) => Directory.GetFiles(folder, "*.tmp-*");

    /// <summary>Who may do what to a file, and whether it ignores its folder's rules: what decides who can read it.</summary>
    private static string Rules(System.Security.AccessControl.FileSecurity security) =>
        $"protected={security.AreAccessRulesProtected};" + string.Join(
            ";",
            security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(System.Security.Principal.SecurityIdentifier))
                .Cast<System.Security.AccessControl.FileSystemAccessRule>()
                .Select(r => $"{r.IdentityReference}:{r.AccessControlType}:{r.FileSystemRights}")
                .Order(StringComparer.Ordinal));

    [Fact]
    public void AWriteCanBeReadBackAndLeavesNoTemporaryFile()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");

        JsonFile.Write(path, Sample, BerthJson.Default.Layout);

        Assert.True(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? read, out string? reason), reason);
        Assert.Equal(Sample, read);
        Assert.Null(reason);
        Assert.Empty(Temporaries(data.Root));
    }

    [Fact]
    public void WritingOverAnExistingFileReplacesIt()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);

        Layout renamed = Sample with { Name = "Desk" };
        JsonFile.Write(path, renamed, BerthJson.Default.Layout);

        Assert.True(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? read, out _));
        Assert.Equal("Desk", read!.Name);
        Assert.Empty(Temporaries(data.Root));
    }

    [Fact]
    public void ACorruptedReadBackIsRefusedAndTheOldFileStays()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);
        byte[] before = File.ReadAllBytes(path);

        // Flip one byte of the flushed temporary file: only the writer's own read-back can notice.
        var error = Assert.Throws<IOException>(() => JsonFile.Write(path, Sample with { Name = "Desk" }, BerthJson.Default.Layout, temp =>
        {
            byte[] bytes = File.ReadAllBytes(temp);
            bytes[^2] ^= 0x01;
            File.WriteAllBytes(temp, bytes);
        }));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Temporaries(data.Root));
    }

    [Fact]
    public void AReplaceThatFailsAfterVerificationLeavesTheOldFileAndNoTemporary()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);
        byte[] before = File.ReadAllBytes(path);

        // Held with no sharing at all: Windows can rename over a file another handle only reads.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Windows reports a target it cannot replace as denied access; both are what Write documents.
            Exception error = Assert.ThrowsAny<Exception>(() => JsonFile.Write(path, Sample with { Name = "Desk" }, BerthJson.Default.Layout));
            Assert.True(error is IOException or UnauthorizedAccessException, error.GetType().FullName);
        }

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Temporaries(data.Root));
    }

    [Fact]
    public void AValueThatCannotBeSerialisedLeavesNothing()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        Layout broken = Sample with { Icons = default(ImmutableArray<IconPosition>) };

        Assert.ThrowsAny<Exception>(() => JsonFile.Write(path, broken, BerthJson.Default.Layout));

        Assert.False(File.Exists(path));
        Assert.Empty(Temporaries(data.Root));
    }

    [Fact]
    public void WriteOverKeepsAFileThatChangedSinceItWasRead()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);
        byte[] read = File.ReadAllBytes(path);
        byte[] arrived = "arrived from elsewhere"u8.ToArray();

        Assert.Throws<TargetChangedException>(() =>
            JsonFile.WriteOver(path, Sample with { Name = "Desk" }, BerthJson.Default.Layout, read, _ => File.WriteAllBytes(path, arrived)));

        Assert.Equal(arrived, File.ReadAllBytes(path));
        Assert.Empty(Temporaries(data.Root));
    }

    [Fact]
    public void ASecondArrivalDuringTheRollbackIsKeptBesideTheFile()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);
        byte[] read = File.ReadAllBytes(path);
        byte[] first = "first arrival"u8.ToArray();
        byte[] second = "second arrival"u8.ToArray();

        Assert.Throws<TargetChangedException>(() => JsonFile.WriteOver(
            path, Sample with { Name = "Desk" }, BerthJson.Default.Layout, read,
            afterFlush: null,
            afterCheck: () => File.WriteAllBytes(path, first),
            afterReplace: () => File.WriteAllBytes(path, second)));

        // The first arrival is back in place, and the second, which the rollback displaced, is kept beside it.
        Assert.Equal(first, File.ReadAllBytes(path));
        string keptFile = Assert.Single(Directory.GetFiles(data.Root, "layout.json" + JsonFile.KeptSuffix + "*"));
        Assert.Equal(second, File.ReadAllBytes(keptFile));
        Assert.Empty(Temporaries(data.Root));
    }

    [Fact]
    public void AFileThatChangedBeforeTheLastCheckIsNotReplacedAtAll()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);
        byte[] read = File.ReadAllBytes(path);
        byte[] arrived = "arrived while the temporary file was written"u8.ToArray();
        bool replaced = false;

        Assert.Throws<TargetChangedException>(() => JsonFile.WriteOver(
            path, Sample with { Name = "Desk" }, BerthJson.Default.Layout, read,
            afterFlush: _ => File.WriteAllBytes(path, arrived),
            afterCheck: null,
            afterReplace: () => replaced = true));

        Assert.False(replaced);
        Assert.Equal(arrived, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(data.Root, "*" + JsonFile.KeptSuffix + "*"));
        Assert.Empty(Temporaries(data.Root));
    }

    [Fact]
    public void AnOversizedArrivalIsKeptAndToldApartByItsLength()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);
        byte[] read = File.ReadAllBytes(path);

        Assert.Throws<TargetChangedException>(() => JsonFile.WriteOver(
            path, Sample with { Name = "Desk" }, BerthJson.Default.Layout, read,
            afterFlush: null,
            afterCheck: () => { using var big = new FileStream(path, FileMode.Create); big.SetLength(JsonFile.MaxBytes * 4); },
            afterReplace: null));

        Assert.Equal(JsonFile.MaxBytes * 4, new FileInfo(path).Length);
        Assert.Empty(Directory.GetFiles(data.Root, "*" + JsonFile.KeptSuffix + "*"));
    }

    [Fact]
    public void AKeptVersionIsNeverCleanedUp()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        string kept = path + JsonFile.KeptSuffix + Guid.NewGuid().ToString("N");
        File.WriteAllText(kept, "kept");
        File.SetLastWriteTimeUtc(kept, DateTime.UtcNow - TimeSpan.FromDays(30));

        JsonFile.Write(path, Sample, BerthJson.Default.Layout);

        Assert.Equal("kept", File.ReadAllText(kept));
    }
    private sealed class SimulatedCrash : Exception
    {
    }

    [Fact]
    public void ACrashAfterTheReplaceIsFinishedByTheNextReadWhenTheFileHadChanged()
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        store.Save(new Settings(1, false, null));
        byte[] read = File.ReadAllBytes(paths.SettingsFile);
        byte[] arrived = "{ damaged by a sync"u8.ToArray();

        // The process ends between the replace and its check.
        Assert.Throws<SimulatedCrash>(() => JsonFile.WriteOver(
            paths.SettingsFile, new Settings(1, true, null), BerthJson.Default.Settings, read,
            afterFlush: null,
            afterCheck: () => File.WriteAllBytes(paths.SettingsFile, arrived),
            afterReplace: () => throw new SimulatedCrash()));
        Assert.True(File.Exists(paths.SettingsFile + JsonFile.PendingSuffix));

        SettingsLoad load = new SettingsStore(BerthPaths.ForRoot(data.Root)).Load();

        Assert.True(load.Exists);
        Assert.False(string.IsNullOrWhiteSpace(load.Unreadable));
        Assert.Equal(arrived, File.ReadAllBytes(paths.SettingsFile));
        Assert.False(File.Exists(paths.SettingsFile + JsonFile.PendingSuffix));
        string keptSave = Assert.Single(Directory.GetFiles(paths.DataRoot, "settings.json" + JsonFile.KeptSuffix + "*"));
        Assert.Contains("\"restoreAutomatically\": true", File.ReadAllText(keptSave), StringComparison.Ordinal);
    }

    [Fact]
    public void AReaderDuringALiveSaveLeavesItsNoteAlone()
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        new SettingsStore(paths).Save(new Settings(1, false, null));
        byte[] read = File.ReadAllBytes(paths.SettingsFile);
        byte[] arrived = "{ damaged by a sync"u8.ToArray();
        bool noteSurvivedTheReader = false;

        Assert.Throws<SimulatedCrash>(() => JsonFile.WriteOver(
            paths.SettingsFile, new Settings(1, true, null), BerthJson.Default.Settings, read,
            afterFlush: null,
            afterCheck: () =>
            {
                File.WriteAllBytes(paths.SettingsFile, arrived);

                // Another reader comes along after this save wrote its note and before it replaces the file.
                _ = new SettingsStore(BerthPaths.ForRoot(data.Root)).Load();
                noteSurvivedTheReader = File.Exists(paths.SettingsFile + JsonFile.PendingSuffix);
            },
            afterReplace: () => throw new SimulatedCrash()));

        Assert.True(noteSurvivedTheReader);
        Assert.False(string.IsNullOrWhiteSpace(new SettingsStore(paths).Load().Unreadable));
        Assert.Equal(arrived, File.ReadAllBytes(paths.SettingsFile));
    }

    [Theory]
    [InlineData(@"..\outside.txt")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("other.json.berth-replaced-0123456789abcdef0123456789abcdef")]
    [InlineData("settings.json.berth-replaced-xyz")]
    public void ANoteThatIsNotBerthsOwnTouchesNoOtherFile(string named)
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        new SettingsStore(paths).Save(new Settings(1, true, null));
        byte[] settings = File.ReadAllBytes(paths.SettingsFile);
        string outside = Path.Combine(data.Root, "outside.txt");
        File.WriteAllText(outside, "the user's own file");
        string other = Path.Combine(paths.DataRoot, "other.json.berth-replaced-0123456789abcdef0123456789abcdef");
        File.WriteAllText(other, "another target's kept file");
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("anything"u8));
        File.WriteAllText(paths.SettingsFile + JsonFile.PendingSuffix, named + "\n" + digest);

        SettingsLoad load = new SettingsStore(paths).Load();

        Assert.Equal(new Settings(1, true, null), load.Value);
        Assert.Equal(settings, File.ReadAllBytes(paths.SettingsFile));
        Assert.Equal("the user's own file", File.ReadAllText(outside));
        Assert.Equal("another target's kept file", File.ReadAllText(other));
        Assert.False(File.Exists(paths.SettingsFile + JsonFile.PendingSuffix));
    }
    [Fact]
    public void AReaderBetweenTheReplaceAndItsCheckNeverSeesTheProposedSave()
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        new SettingsStore(paths).Save(new Settings(1, false, null));
        byte[] read = File.ReadAllBytes(paths.SettingsFile);
        byte[] arrived = "{ damaged by a sync"u8.ToArray();
        SettingsLoad? seen = null;

        Assert.Throws<TargetChangedException>(() => JsonFile.WriteOver(
            paths.SettingsFile, new Settings(1, true, null), BerthJson.Default.Settings, read,
            afterFlush: null,
            afterCheck: () => File.WriteAllBytes(paths.SettingsFile, arrived),
            afterReplace: () => seen = new SettingsStore(BerthPaths.ForRoot(data.Root)).Load()));

        // The proposed save was in place, then taken back: the reader must not have reported it as the settings.
        Assert.NotNull(seen);
        Assert.NotEqual(new Settings(1, true, null), seen.Value);
        Assert.False(string.IsNullOrWhiteSpace(seen.Unreadable));
        Assert.Equal(arrived, File.ReadAllBytes(paths.SettingsFile));
    }

    [Fact]
    public void AReaderThatCheckedBeforeTheSaveStartedNeverReturnsTheProposedSave()
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        new SettingsStore(paths).Save(new Settings(1, false, null));
        byte[] read = File.ReadAllBytes(paths.SettingsFile);
        byte[] arrived = "{ damaged by a sync"u8.ToArray();
        using var replaced = new ManualResetEventSlim();
        using var readerRead = new ManualResetEventSlim();
        Exception? writerError = null;
        var writer = new Thread(() =>
        {
            try
            {
                JsonFile.WriteOver(
                    paths.SettingsFile, new Settings(1, true, null), BerthJson.Default.Settings, read,
                    afterFlush: null,
                    afterCheck: () => File.WriteAllBytes(paths.SettingsFile, arrived),
                    afterReplace: () =>
                    {
                        replaced.Set();
                        Assert.True(readerRead.Wait(TimeSpan.FromSeconds(20)));
                    });
            }
            catch (Exception error)
            {
                writerError = error;
            }
        });

        // The reader finds no note, then the save starts and replaces the file, then the reader reads it, then the
        // save sees the damaged arrival and takes its proposal back.
        SettingsLoad load = new SettingsStore(BerthPaths.ForRoot(data.Root)).Load(
            afterRecover: () =>
            {
                if (!writer.IsAlive && !replaced.IsSet)
                {
                    writer.Start();
                    Assert.True(replaced.Wait(TimeSpan.FromSeconds(20)));
                }
            },
            afterRead: () =>
            {
                if (writer.IsAlive)
                {
                    readerRead.Set();
                    Assert.True(writer.Join(TimeSpan.FromSeconds(20)));
                }
            });

        Assert.IsType<TargetChangedException>(writerError);
        Assert.NotEqual(new Settings(1, true, null), load.Value);
        Assert.False(string.IsNullOrWhiteSpace(load.Unreadable));
        Assert.Equal(arrived, File.ReadAllBytes(paths.SettingsFile));
    }
    [Fact]
    public void AnOversizedNoteIsDiscardedUnread()
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        new SettingsStore(paths).Save(new Settings(1, true, null));
        using (var note = new FileStream(paths.SettingsFile + JsonFile.PendingSuffix, FileMode.CreateNew))
        {
            // Sparse, so it takes no disk, and past the 2 GB a byte array can hold: a reader that reads it whole fails.
            Assert.True(Native.DeviceIoControl(note.SafeFileHandle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero), $"FSCTL_SET_SPARSE failed: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
            note.SetLength(3L * 1024 * 1024 * 1024);
        }

        Assert.Equal(new SettingsLoad(new Settings(1, true, null), Exists: true, Unreadable: null), new SettingsStore(paths).Load());
        Assert.False(File.Exists(paths.SettingsFile + JsonFile.PendingSuffix));
    }
    [Fact]
    public void AReaderCannotTakeTheNoteBeforeTheWriterRemovesIt()
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        new SettingsStore(paths).Save(new Settings(1, false, null));
        byte[] read = File.ReadAllBytes(paths.SettingsFile);
        FileStream? taken = null;
        try
        {
            // Just before the note goes, a reader tries to take it as recovery does (exclusively) and keeps it.
            JsonFile.WriteOver(
                paths.SettingsFile, new Settings(1, true, null), BerthJson.Default.Settings, read,
                afterFlush: null, afterCheck: null, afterReplace: null,
                beforeNoteRemoved: () =>
                {
                    try
                    {
                        taken = new FileStream(paths.SettingsFile + JsonFile.PendingSuffix, FileMode.Open, FileAccess.Read, FileShare.None);
                    }
                    catch (IOException)
                    {
                        // The writer still holds it: the reader waits, as it should.
                    }
                });
        }
        finally
        {
            taken?.Dispose();
        }

        Assert.Null(taken);
        Assert.Equal(new Settings(1, true, null), new SettingsStore(paths).Load().Value);
        Assert.False(File.Exists(paths.SettingsFile + JsonFile.PendingSuffix));
    }
    [Fact]
    public void ReadersRacingTheNoteNeverMakeASaveFail()
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        store.Save(new Settings(1, false, null));
        using var stop = new CancellationTokenSource();
        Exception? readerError = null;
        var reader = new Thread(() =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    JsonFile.Recover(paths.SettingsFile);
                }
            }
            catch (Exception error) when (error is IOException)
            {
                // A reader that gives up on a busy note is fine; it must not break the writer.
            }
            catch (Exception error)
            {
                readerError = error;
            }
        });
        reader.Start();
        try
        {
            for (int i = 0; i < 100; i++)
            {
                store.Save(new Settings(1, i % 2 == 0, null));
            }
        }
        finally
        {
            stop.Cancel();
            Assert.True(reader.Join(TimeSpan.FromSeconds(20)));
        }

        Assert.Null(readerError);
        Assert.Equal(new Settings(1, false, null), new SettingsStore(paths).Load().Value);
        Assert.Empty(Directory.GetFiles(paths.DataRoot, "settings.json.berth-*"));
    }
    [Fact]
    public void ACrashAfterTheReplaceLeavesTheSaveWhenNothingHadChanged()
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        new SettingsStore(paths).Save(new Settings(1, false, null));
        byte[] read = File.ReadAllBytes(paths.SettingsFile);

        Assert.Throws<SimulatedCrash>(() => JsonFile.WriteOver(
            paths.SettingsFile, new Settings(1, true, null), BerthJson.Default.Settings, read,
            afterFlush: null, afterCheck: null, afterReplace: () => throw new SimulatedCrash()));

        Assert.Equal(new SettingsLoad(new Settings(1, true, null), Exists: true, Unreadable: null), new SettingsStore(paths).Load());
        Assert.Empty(Directory.GetFiles(paths.DataRoot, "settings.json.berth-*"));
    }

    [Fact]
    public void TheLayoutStoreFinishesAnInterruptedSaveToo()
    {
        using var data = new TestData();
        var paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        Layout layout = Layouts.Single();
        store.Save(layout);
        string path = paths.LayoutFile(layout.Key);
        byte[] read = File.ReadAllBytes(path);
        byte[] arrived = "{ damaged"u8.ToArray();

        Assert.Throws<SimulatedCrash>(() => JsonFile.WriteOver(
            path, layout with { Name = "Desk" }, BerthJson.Default.Layout, read,
            afterFlush: null, afterCheck: () => File.WriteAllBytes(path, arrived), afterReplace: () => throw new SimulatedCrash()));

        Assert.Throws<UnreadableLayoutException>(() => new LayoutStore(paths).Find(layout.Key));
        Assert.Equal(arrived, File.ReadAllBytes(path));
    }
    [Fact]
    public void WriteOverReplacesAFileThatIsStillWhatWasRead()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);

        JsonFile.WriteOver(path, Sample with { Name = "Desk" }, BerthJson.Default.Layout, File.ReadAllBytes(path));

        Assert.True(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? read, out _));
        Assert.Equal("Desk", read!.Name);
        Assert.Empty(Temporaries(data.Root));
    }
    [Fact]
    public void AWriteIntoAMissingFolderThrowsAndCreatesNothing()
    {
        using var data = new TestData();
        string folder = Path.Combine(data.Root, "not-there");

        Assert.ThrowsAny<IOException>(() => JsonFile.Write(Path.Combine(folder, "layout.json"), Sample, BerthJson.Default.Layout));

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void AMissingFileIsMissing()
    {
        using var data = new TestData();
        Assert.False(JsonFile.TryRead(Path.Combine(data.Root, "none.json"), BerthJson.Default.Layout, out Layout? value, out string? reason));
        Assert.Null(value);
        Assert.Equal(JsonFile.Missing, reason);
    }

    [Fact]
    public void AMissingFolderIsMissing()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "roaming", "layouts", "c7f017905ce518e6.json");
        Assert.False(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? value, out string? reason));
        Assert.Null(value);
        Assert.Equal(JsonFile.Missing, reason);
    }

    [Fact]
    public void ALockedFileIsNotMissing()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? value, out string? reason));
            Assert.Null(value);
            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.NotEqual(JsonFile.Missing, reason);
        }
    }

    [Fact]
    public void ASaveTooLargeToReadBackIsRefusedAndTheOldFileStays()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);
        byte[] before = File.ReadAllBytes(path);
        Layout huge = Sample with { Name = new string('x', (int)JsonFile.MaxBytes + 1) };

        var error = Assert.Throws<IOException>(() => JsonFile.Write(path, huge, BerthJson.Default.Layout));

        Assert.Contains("more than", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Temporaries(data.Root));
    }

    [Fact]
    public void AnOversizedFileIsRefusedBeforeItIsRead()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        using (var stream = new FileStream(path, FileMode.CreateNew))
        {
            stream.SetLength(JsonFile.MaxBytes + 1);
        }

        Assert.False(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? value, out string? reason));
        Assert.Null(value);
        Assert.Contains("more than", reason, StringComparison.Ordinal);
        Assert.Equal(JsonFile.MaxBytes + 1, new FileInfo(path).Length);
    }

    [Fact]
    public void SavingAgainKeepsTheTargetsOwnPermissions()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);

        // Restrict the file to its owner, unlike the folder it sits in.
        var restricted = new System.Security.AccessControl.FileSecurity();
        restricted.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        restricted.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(restricted);
        string before = Rules(new FileInfo(path).GetAccessControl());

        string? stagedRules = null;
        JsonFile.Write(path, Sample with { Name = "Desk" }, BerthJson.Default.Layout, temp =>
            stagedRules = Rules(new FileInfo(temp).GetAccessControl()));

        // The staged copy is the writer's alone, so a crash cannot expose it either.
        Assert.Equal(OwnerOnly(), stagedRules);
        var after = new FileInfo(path).GetAccessControl();
        Assert.True(after.AreAccessRulesProtected);
        Assert.Equal(before, Rules(after));
        Assert.True(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? read, out _));
        Assert.Equal("Desk", read!.Name);
    }

    [Fact]
    public void AFolderThatGrantsMoreThanTheTargetDoesNotReachTheStagedCopy()
    {
        using var data = new TestData();
        var everyone = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
        string shared = Path.Combine(data.Root, "shared");
        Directory.CreateDirectory(shared);
        string path = Path.Combine(shared, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);
        Assert.False(new FileInfo(path).GetAccessControl().AreAccessRulesProtected);

        // The folder now hands Everyone read access to new files, set the way an older tool or a copied ACL leaves
        // it: without passing the grant on to the file already there (SetFileSecurity does not propagate).
        System.Security.AccessControl.DirectorySecurity open = new DirectoryInfo(shared).GetAccessControl();
        open.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(everyone, System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.InheritanceFlags.ObjectInherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        Assert.True(Native.SetFileSecurityW(shared, DaclSecurityInformation, open.GetSecurityDescriptorBinaryForm()), $"SetFileSecurityW failed: {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        Assert.DoesNotContain(everyone.Value, Rules(new FileInfo(path).GetAccessControl()), StringComparison.Ordinal);
        string probe = Path.Combine(shared, "probe.txt");
        File.WriteAllText(probe, "x");
        Assert.Contains(everyone.Value, Rules(new FileInfo(probe).GetAccessControl()), StringComparison.Ordinal);

        string? stagedRules = null;
        JsonFile.Write(path, Sample with { Name = "Desk" }, BerthJson.Default.Layout, temp =>
            stagedRules = Rules(new FileInfo(temp).GetAccessControl()));

        // The staged copy is the writer's alone, not open to the folder's wider grant.
        Assert.Equal(OwnerOnly(), stagedRules);

    }

    [Fact]
    public void AnExplicitAllowOverAnInheritedDenyStillSaves()
    {
        using var data = new TestData();
        var everyone = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        string guarded = Path.Combine(data.Root, "guarded");
        Directory.CreateDirectory(guarded);
        string path = Path.Combine(guarded, "layout.json");
        JsonFile.Write(path, Sample, BerthJson.Default.Layout);

        // The layout allows its writer explicitly...
        System.Security.AccessControl.FileSecurity target = new FileInfo(path).GetAccessControl();
        target.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(identity.User!, System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(target);

        // ...while the folder denies Everyone read access to its files, the layout included through inheritance.
        System.Security.AccessControl.DirectorySecurity folder = new DirectoryInfo(guarded).GetAccessControl();
        folder.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(everyone, System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.InheritanceFlags.ObjectInherit, System.Security.AccessControl.PropagationFlags.InheritOnly, System.Security.AccessControl.AccessControlType.Deny));
        new DirectoryInfo(guarded).SetAccessControl(folder);
        string probe = Path.Combine(guarded, "probe.txt");
        File.WriteAllText(probe, "x");
        Assert.ThrowsAny<UnauthorizedAccessException>(() => File.ReadAllText(probe));
        Assert.Contains(new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>(),
            r => r.IsInherited && r.AccessControlType == System.Security.AccessControl.AccessControlType.Deny && r.IdentityReference.Equals(everyone));

        JsonFile.Write(path, Sample with { Name = "Desk" }, BerthJson.Default.Layout);

        Assert.True(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? read, out string? reason), reason);
        Assert.Equal("Desk", read!.Name);
        Assert.Empty(Temporaries(guarded));
    }

    private const int DaclSecurityInformation = 4;

    private const uint FsctlSetSparse = 0x000900C4;

    private static string OwnerOnly() =>
        $"protected=True;{System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value}:Allow:FullControl";

    private static partial class Native
    {
        [System.Runtime.InteropServices.LibraryImport("advapi32.dll", EntryPoint = "SetFileSecurityW", SetLastError = true, StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static partial bool SetFileSecurityW(string fileName, int securityInformation, byte[] securityDescriptor);

        [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static partial bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
    }
    [Fact]
    public void AStaleTemporaryLeftByACrashIsRemovedAndAFreshOneIsLeft()
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        string stale = path + ".tmp-" + Guid.NewGuid().ToString("N");
        string fresh = path + ".tmp-" + Guid.NewGuid().ToString("N");
        string backup = path + ".tmp-backup";
        string other = Path.Combine(data.Root, "settings.json.tmp-" + Guid.NewGuid().ToString("N"));
        DateTime old = DateTime.UtcNow - JsonFile.StaleAfter - TimeSpan.FromMinutes(1);
        foreach (string file in new[] { stale, fresh, backup, other })
        {
            File.WriteAllText(file, "{ \"kept\": true }");
        }

        File.SetLastWriteTimeUtc(stale, old);
        File.SetLastWriteTimeUtc(backup, old);
        File.SetLastWriteTimeUtc(other, old);
        byte[] backupBytes = File.ReadAllBytes(backup);

        JsonFile.Write(path, Sample, BerthJson.Default.Layout);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(other));
        Assert.Equal(backupBytes, File.ReadAllBytes(backup));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("""{"schemaVersion":1,"key":"c7f017905ce518e6","name":"x","savedAt":"2026-11-15T07:14:03Z","monitors":[{"x":0,"y":0,"width":1,"height":1,"scale":100,"primary":true}],"icons":[null],"previous":null}""")]
    public void ABadFileIsReportedAndLeftAsItWas(string content)
    {
        using var data = new TestData();
        string path = Path.Combine(data.Root, "layout.json");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(path);

        Assert.False(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? value, out string? reason));

        Assert.Null(value);
        Assert.False(string.IsNullOrEmpty(reason));
        Assert.NotEqual(JsonFile.Missing, reason);
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
