using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Berth.Core;

namespace Berth.Tests.Core;

/// <summary>A clock that always reads one time.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class LogTests
{
    private static readonly FixedClock At = new(new DateTimeOffset(2026, 11, 15, 9, 14, 3, TimeSpan.FromHours(2)));

    private static Log Make(TestData data, long cap = Log.Cap, Action<int>? afterFailedAttempt = null) =>
        new(BerthPaths.ForRoot(data.Root), At, cap, afterFailedAttempt);

    private static string LogFile(TestData data) => BerthPaths.ForRoot(data.Root).LogFile;

    [Fact]
    public void ALineHasTheUtcTimeTheLevelAndTheMessage()
    {
        using var data = new TestData();

        new Log(BerthPaths.ForRoot(data.Root), At).Info("hello");

        Assert.Equal("2026-11-15T07:14:03Z INFO hello\r\n", File.ReadAllText(LogFile(data)));
    }

    [Fact]
    public void EachLevelIsWrittenByItsWord()
    {
        using var data = new TestData();
        Log log = Make(data);

        log.Write(LogLevel.Info, "a");
        log.Warn("b");
        log.Write(LogLevel.Fail, "c");

        Assert.Equal(["2026-11-15T07:14:03Z INFO a", "2026-11-15T07:14:03Z WARN b", "2026-11-15T07:14:03Z FAIL c"], File.ReadAllLines(LogFile(data)));
    }

    [Fact]
    public void ALineBreakInAMessageStaysOnItsOneLine()
    {
        using var data = new TestData();

        Make(data).Info("Saved 2 icons to \"Desk\r\nTwo\".");

        Assert.Equal(["2026-11-15T07:14:03Z INFO Saved 2 icons to \"Desk\\r\\nTwo\"."], File.ReadAllLines(LogFile(data)));
    }

    [Fact]
    public void AnExceptionFollowsItsLineWithTypeMessageAndStack()
    {
        using var data = new TestData();
        Exception error;
        try
        {
            ThrowFromANamedPlace();
            throw new UnreachableException();
        }
        catch (InvalidOperationException caught)
        {
            error = caught;
        }

        Make(data).Fail("Restore failed.", error);

        string[] lines = File.ReadAllLines(LogFile(data));
        Assert.Equal("2026-11-15T07:14:03Z FAIL Restore failed.", lines[0]);
        Assert.Equal("  System.InvalidOperationException: boom", lines[1]);
        Assert.Contains(lines, line => line.StartsWith("  ", StringComparison.Ordinal) && line.Contains(nameof(ThrowFromANamedPlace), StringComparison.Ordinal));
        Assert.All(lines.Skip(1), line => Assert.StartsWith("  ", line, StringComparison.Ordinal));
    }

    [Fact]
    public void TheLogRollsByEncodedBytesAndKeepsOnlyTheFileBefore()
    {
        using var data = new TestData();
        string[] messages = ["Bürö 1", "Bürö 2", "Bürö 3", "Bürö 4", "Bürö 5"];
        Log probe = Make(data);
        int length = Encoding.UTF8.GetByteCount(probe.Record(LogLevel.Info, messages[0], null));
        Assert.Equal(26 + 8 + 2, length); // the header, "Bürö 1" in eight bytes (two umlauts of two bytes each), the CRLF
        Log log = Make(data, cap: 2 * length);
        string Line(int i) => probe.Record(LogLevel.Info, messages[i], null);

        log.Info(messages[0]);
        log.Info(messages[1]);
        Assert.Equal(2 * length, new FileInfo(LogFile(data)).Length); // an exact fit stays
        Assert.False(File.Exists(LogFile(data) + ".1"));

        log.Info(messages[2]);
        Assert.Equal(Line(0) + Line(1), File.ReadAllText(LogFile(data) + ".1"));
        Assert.Equal(Line(2), File.ReadAllText(LogFile(data)));

        log.Info(messages[3]);
        log.Info(messages[4]);
        Assert.Equal(Line(2) + Line(3), File.ReadAllText(LogFile(data) + ".1"));
        Assert.Equal(2 * length, new FileInfo(LogFile(data) + ".1").Length);
        Assert.Equal(Line(4), File.ReadAllText(LogFile(data)));
        Assert.Equal(length, new FileInfo(LogFile(data)).Length);
        Assert.Equal(["berth.log", "berth.log.1"], Directory.GetFiles(BerthPaths.ForRoot(data.Root).LogFolder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void ARecordOneByteOverTheCapRolls()
    {
        // Counted in characters the second record would still fit (its umlauts are one character but two bytes).
        using var data = new TestData();
        Log probe = Make(data);
        int length = Encoding.UTF8.GetByteCount(probe.Record(LogLevel.Info, "Bürö 1", null));
        Log log = Make(data, cap: (2 * length) - 1);

        log.Info("Bürö 1");
        log.Info("Bürö 2");

        Assert.Equal(probe.Record(LogLevel.Info, "Bürö 1", null), File.ReadAllText(LogFile(data) + ".1"));
        Assert.Equal(probe.Record(LogLevel.Info, "Bürö 2", null), File.ReadAllText(LogFile(data)));
    }

    [Fact]
    public void OneRetryWritesTheEventOnceAfterASharingViolation()
    {
        using var data = new TestData();
        BerthPaths.EnsureFolderFor(LogFile(data));
        var held = new FileStream(LogFile(data), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Log log = Make(data, afterFailedAttempt: attempt => held.Dispose());

        log.Info("hello");

        held.Dispose();
        Assert.Equal(2, log.LastAttempts);
        Assert.Equal(["2026-11-15T07:14:03Z INFO hello"], File.ReadAllLines(LogFile(data)));
    }

    [Fact]
    public void APersistentFailureReturnsAfterTwoAttempts()
    {
        using var data = new TestData();
        BerthPaths.EnsureFolderFor(LogFile(data));
        var attempts = new List<int>();
        using (new FileStream(LogFile(data), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Log log = Make(data, afterFailedAttempt: attempts.Add);

            log.Info("hello");

            Assert.Equal(2, log.LastAttempts);
        }

        Assert.Equal([1, 2], attempts);
        Assert.Equal(0, new FileInfo(LogFile(data)).Length);
    }

    [Fact]
    public void ALogCallReturnsWhenItsFolderIsReadOnly()
    {
        using var data = new TestData();
        string folder = BerthPaths.ForRoot(data.Root).LogFolder;
        Directory.CreateDirectory(folder);
        var info = new DirectoryInfo(folder);
        info.Attributes |= FileAttributes.ReadOnly;
        try
        {
            Make(data).Info("hello");
        }
        finally
        {
            info.Attributes &= ~FileAttributes.ReadOnly;
        }
    }

    private static void ThrowFromANamedPlace() => throw new InvalidOperationException("boom");
}

[Collection(DesktopCollectionDefinition.Name)]
public sealed class CrossProcessLogTests
{
    [Fact]
    public async Task TwoProcessesCrossingARollLoseNoEvent()
    {
        using var data = new TestData();
        const int Each = 20;
        CancellationToken cancel = TestContext.Current.CancellationToken;
        using HelperProcess first = HelperProcess.Start(data.Root, "log", "1000", "A");
        using HelperProcess second = HelperProcess.Start(data.Root, "log", "1000", "B");

        string[] replies = await Task.WhenAll(
            Task.Run(() => first.Send($"write {Each}"), cancel),
            Task.Run(() => second.Send($"write {Each}"), cancel));

        Assert.Equal(["done", "done"], replies);
        string folder = BerthPaths.ForRoot(data.Root).LogFolder;
        Assert.Equal(["berth.log", "berth.log.1"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        string[] lines = [.. File.ReadAllLines(Path.Combine(folder, "berth.log.1")), .. File.ReadAllLines(Path.Combine(folder, "berth.log"))];
        Assert.All(lines, line => Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ INFO [AB] \d+\z", line));
        string[] expected = [.. Enumerable.Range(1, Each).SelectMany(i => new[] { $"A {i}", $"B {i}" }).Order(StringComparer.Ordinal)];
        Assert.Equal(expected, lines.Select(line => line[26..]).Order(StringComparer.Ordinal));
        Assert.True(new FileInfo(Path.Combine(folder, "berth.log.1")).Length <= 1000);
    }
}
