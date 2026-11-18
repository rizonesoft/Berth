using System.Collections.Immutable;
using System.Text;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.Core;

public sealed class JsonFileTests
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

        // The staged copy is no more readable than the restricted target, so a crash cannot expose it either.
        Assert.Equal(before, stagedRules);
        var after = new FileInfo(path).GetAccessControl();
        Assert.True(after.AreAccessRulesProtected);
        Assert.Equal(before, Rules(after));
        Assert.True(JsonFile.TryRead(path, BerthJson.Default.Layout, out Layout? read, out _));
        Assert.Equal("Desk", read!.Name);
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
