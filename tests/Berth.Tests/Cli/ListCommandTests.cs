using System.Collections.Immutable;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.Cli;

public sealed class ListCommandTests
{
    private static readonly TimeZoneInfo UtcPlusTwo =
        TimeZoneInfo.CreateCustomTimeZone("BerthTest+02", TimeSpan.FromHours(2), "UTC+2", "UTC+2");

    private static readonly MonitorInfo[] ArrangementA =
    [
        new(0, 0, 2560, 1440, 100, true),
        new(2560, 0, 1920, 1080, 100, false),
    ];

    private static readonly MonitorInfo[] ArrangementB =
    [
        new(0, 0, 1920, 1080, 125, true),
    ];

    private static readonly MonitorInfo[] ArrangementC =
    [
        new(0, 0, 3840, 2160, 150, true),
    ];

    private static readonly MonitorInfo[] ArrangementUnmatched =
    [
        new(0, 0, 1280, 720, 100, true),
    ];

    [Fact]
    public void ListSingleActiveLayoutMatchesSpec13ExampleExactLine()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        // 2026-11-15T07:14:03Z in UTC+02:00 is 2026-11-15 09:14.
        Layout docked = MakeLayout(ArrangementA, "Docked", new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), 42);
        store.Save(docked);

        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = CliRunner.RunList(store, paths, new FakeMonitors(ArrangementA), log: null, output, error, UtcPlusTwo);

        Assert.Equal(0, exitCode);
        Assert.Equal($"* Docked  2 monitors  42 icons  2026-11-15 09:14{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void ListAlignsUnequalNamesAndSanitizesControlCharactersBeforeMeasuring()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var when = new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero);

        // Control char \b expands to \u0008 (6 chars), making "AB\u0008C" (9 chars) wider than "Docked" (6 chars).
        Layout docked = MakeLayout(ArrangementA, "Docked", when, 42);
        Layout controlName = MakeLayout(ArrangementB, "AB\bC", when, 1);
        Layout longerName = MakeLayout(ArrangementC, "Home Office Desk", when, 5);
        store.Save(docked);
        store.Save(controlName);
        store.Save(longerName);

        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = CliRunner.RunList(store, paths, new FakeMonitors(ArrangementA), log: null, output, error, UtcPlusTwo);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());

        string expected =
            $"* Docked            2 monitors  42 icons  2026-11-15 09:14{Environment.NewLine}" +
            $"  AB\\u0008C         1 monitor  1 icon  2026-11-15 09:14{Environment.NewLine}" +
            $"  Home Office Desk  1 monitor  5 icons  2026-11-15 09:14{Environment.NewLine}";
        Assert.Equal(expected, output.ToString());
    }

    [Fact]
    public void ListOrdersActiveFirstThenByNameAndHandlesNoMatchAndUnavailableMonitors()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var when = new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero);

        // Active name "Zeta" is alphabetically last; inactive "alpha" and "Beta" differ from key order.
        store.Save(MakeLayout(ArrangementA, "Zeta", when, 2));
        store.Save(MakeLayout(ArrangementB, "Beta", when, 1));
        store.Save(MakeLayout(ArrangementC, "alpha", when, 3));

        // 1) Matching active arrangement ("Zeta" first with '* ', then "alpha", then "Beta").
        var outMatch = new StringWriter();
        var errMatch = new StringWriter();
        int codeMatch = CliRunner.RunList(store, paths, new FakeMonitors(ArrangementA), log: null, outMatch, errMatch, UtcPlusTwo);
        Assert.Equal(0, codeMatch);
        Assert.Equal(string.Empty, errMatch.ToString());
        Assert.Equal(
            $"* Zeta   2 monitors  2 icons  2026-11-15 09:14{Environment.NewLine}" +
            $"  alpha  1 monitor  3 icons  2026-11-15 09:14{Environment.NewLine}" +
            $"  Beta   1 monitor  1 icon  2026-11-15 09:14{Environment.NewLine}",
            outMatch.ToString());

        // 2) Unmatched arrangement (no '*' prefix, sorted by name: alpha, Beta, Zeta).
        var outNone = new StringWriter();
        var errNone = new StringWriter();
        int codeNone = CliRunner.RunList(store, paths, new FakeMonitors(ArrangementUnmatched), log: null, outNone, errNone, UtcPlusTwo);
        Assert.Equal(0, codeNone);
        Assert.Equal(string.Empty, errNone.ToString());
        Assert.Equal(
            $"  alpha  1 monitor  3 icons  2026-11-15 09:14{Environment.NewLine}" +
            $"  Beta   1 monitor  1 icon  2026-11-15 09:14{Environment.NewLine}" +
            $"  Zeta   2 monitors  2 icons  2026-11-15 09:14{Environment.NewLine}",
            outNone.ToString());

        // 3) Monitor read throws DesktopUnavailableException -> still lists all with two-space prefix and exit 0.
        var unavailableMonitors = new FakeMonitors(ArrangementA) { Unavailable = true };
        var outUnavail = new StringWriter();
        var errUnavail = new StringWriter();
        int codeUnavail = CliRunner.RunList(store, paths, unavailableMonitors, log: null, outUnavail, errUnavail, UtcPlusTwo);
        Assert.Equal(0, codeUnavail);
        Assert.Equal(string.Empty, errUnavail.ToString());
        Assert.Equal(outNone.ToString(), outUnavail.ToString());
    }

    [Fact]
    public void ListEmptyFolderWritesNoLayoutsSavedYetAndReturnsExit0()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = CliRunner.RunList(new LayoutStore(paths), paths, new FakeMonitors(ArrangementA), log: null, output, error, UtcPlusTwo);

        Assert.Equal(0, exitCode);
        Assert.Equal($"No layouts saved yet.{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void ListReportsUnreadableFilesToStderrStillListsReadableAndHandlesCorruptOnly()
    {
        // 1) One readable + one corrupt file -> one line on each stream, exit 0.
        using (var data = new TestData())
        {
            BerthPaths paths = BerthPaths.ForRoot(data.Root);
            var store = new LayoutStore(paths);
            store.Save(MakeLayout(ArrangementA, "Docked", new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), 42));
            string badPath = Path.Combine(paths.LayoutsFolder, "1111222233334444.json");
            byte[] badBytes = "{ corrupt"u8.ToArray();
            File.WriteAllBytes(badPath, badBytes);

            var output = new StringWriter();
            var error = new StringWriter();
            int code = CliRunner.RunList(store, paths, new FakeMonitors(ArrangementA), log: null, output, error, UtcPlusTwo);

            Assert.Equal(0, code);
            Assert.Equal($"* Docked  2 monitors  42 icons  2026-11-15 09:14{Environment.NewLine}", output.ToString());
            Assert.Equal($"The layout file 1111222233334444.json could not be read. It was left untouched.{Environment.NewLine}", error.ToString());
            Assert.Equal(badBytes, File.ReadAllBytes(badPath));
        }

        // 2) Zero readable + two corrupt files -> both reported on stderr, "No layouts saved yet." on stdout, exit 0, bytes intact.
        using (var data = new TestData())
        {
            BerthPaths paths = BerthPaths.ForRoot(data.Root);
            Directory.CreateDirectory(paths.LayoutsFolder);
            string bad1 = Path.Combine(paths.LayoutsFolder, "aaaa1111bbbb2222.json");
            string bad2 = Path.Combine(paths.LayoutsFolder, "cccc3333dddd4444.json");
            byte[] bytes1 = "not-json-1"u8.ToArray();
            byte[] bytes2 = "not-json-2"u8.ToArray();
            File.WriteAllBytes(bad1, bytes1);
            File.WriteAllBytes(bad2, bytes2);

            var output = new StringWriter();
            var error = new StringWriter();
            int code = CliRunner.RunList(new LayoutStore(paths), paths, new FakeMonitors(ArrangementA), log: null, output, error, UtcPlusTwo);

            Assert.Equal(0, code);
            Assert.Equal($"No layouts saved yet.{Environment.NewLine}", output.ToString());
            Assert.Equal(
                $"The layout file aaaa1111bbbb2222.json could not be read. It was left untouched.{Environment.NewLine}" +
                $"The layout file cccc3333dddd4444.json could not be read. It was left untouched.{Environment.NewLine}",
                error.ToString());
            Assert.Equal(bytes1, File.ReadAllBytes(bad1));
            Assert.Equal(bytes2, File.ReadAllBytes(bad2));
        }
    }

    [Fact]
    public void ListFolderReadFailureWritesSpec18ReadFailedLogsAndReturnsExit1()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        Directory.CreateDirectory(paths.DataRoot);
        File.WriteAllText(paths.LayoutsFolder, "not-a-directory");
        var log = new Log(paths, TimeProvider.System);

        var output = new StringWriter();
        var error = new StringWriter();
        int code = CliRunner.RunList(new LayoutStore(paths), paths, new FakeMonitors(ArrangementA), log, output, error, UtcPlusTwo);

        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output.ToString());
        string errText = error.ToString();
        Assert.StartsWith($"Berth could not read {paths.LayoutsFolder}: ", errText, StringComparison.Ordinal);
        Assert.EndsWith($".{Environment.NewLine}", errText, StringComparison.Ordinal);
        Assert.True(File.Exists(paths.LogFile));
        Assert.Contains($"FAIL Berth could not read {paths.LayoutsFolder}: ", File.ReadAllText(paths.LogFile), StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramRunRoutesListWithoutConstructingMutationComposition()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        new LayoutStore(paths).Save(MakeLayout(ArrangementA, "Docked", new DateTimeOffset(2026, 11, 15, 7, 14, 3, TimeSpan.Zero), 42));

        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = global::Berth.Program.Run(
            ["list"],
            output,
            error,
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            createComposition: (_, _) => throw new InvalidOperationException("list must not construct mutation composition"),
            createMonitors: () => new FakeMonitors(ArrangementA),
            zone: UtcPlusTwo);

        Assert.Equal(0, exitCode);
        Assert.Equal($"* Docked  2 monitors  42 icons  2026-11-15 09:14{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    private static Layout MakeLayout(MonitorInfo[] monitors, string name, DateTimeOffset savedAt, int iconCount)
    {
        var icons = ImmutableArray.CreateBuilder<IconPosition>(iconCount);
        for (int i = 0; i < iconCount; i++)
        {
            icons.Add(new IconPosition($"C:\\Users\\Test\\Desktop\\Item{i}.txt", $"Item{i}", i * 80, 10));
        }

        return new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From(monitors),
            name,
            savedAt,
            [.. monitors],
            icons.MoveToImmutable(),
            Previous: null);
    }
}
