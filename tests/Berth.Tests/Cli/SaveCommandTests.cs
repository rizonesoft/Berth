using Berth.App;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.Desktop;
using Berth.Tests.Fakes;

namespace Berth.Tests.Cli;

public sealed class SaveCommandTests
{
    private static readonly MonitorInfo[] TwoMonitors =
    [
        new(0, 0, 2560, 1440, 100, true),
        new(2560, 0, 1920, 1080, 100, false),
    ];

    private static readonly IconPosition[] ThreeIcons =
    [
        new("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin", 76, 2),
        new("C:\\Users\\Test\\Desktop\\Notes.txt", "Notes", 76, 106),
        new("C:\\Users\\Test\\Desktop\\Work.lnk", "Work", 76, 210),
    ];

    [Fact]
    public void SaveDefaultNameWritesExactLineToStdoutAndPersistsLayout()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var desktop = new FakeDesktopIcons(ThreeIcons);
        var monitors = new FakeMonitors(TwoMonitors);
        var store = new LayoutStore(paths);
        var service = new LayoutService(desktop, monitors, store, paths, new OperationLock(paths, TimeSpan.FromSeconds(1)), new Log(paths, clock), clock);
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new CliRunner(service, output, error);

        int exitCode = runner.Run(CliArguments.Parse(["save"]));

        Assert.Equal(CliExit.Done, exitCode);
        Assert.Equal($"Saved 3 icons to \"2 monitors\".{Environment.NewLine}", output.ToString());
        Assert.Equal(string.Empty, error.ToString());

        var loaded = store.LoadAll();
        Assert.Empty(loaded.Unreadable);
        Layout saved = Assert.Single(loaded.Layouts);
        Assert.Equal("2 monitors", saved.Name);
        Assert.Equal(3, saved.Icons.Length);
        Assert.Null(saved.Previous);
    }

    [Fact]
    public void SaveWithExplicitNameNamesNewLayoutAndRenamesExistingKeepingPrevious()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var t1 = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 11, 20, 11, 30, 0, TimeSpan.Zero);
        var clock = new FixedClock(t1);
        var desktop = new FakeDesktopIcons(ThreeIcons);
        var monitors = new FakeMonitors(TwoMonitors);
        var store = new LayoutStore(paths);
        var service = new LayoutService(desktop, monitors, store, paths, new OperationLock(paths, TimeSpan.FromSeconds(1)), new Log(paths, clock), clock);

        var out1 = new StringWriter();
        var err1 = new StringWriter();
        int code1 = new CliRunner(service, out1, err1).Run(CliArguments.Parse(["save", "--name", "Docked"]));

        Assert.Equal(CliExit.Done, code1);
        Assert.Equal($"Saved 3 icons to \"Docked\".{Environment.NewLine}", out1.ToString());
        Assert.Equal(string.Empty, err1.ToString());

        Layout first = Assert.Single(store.LoadAll().Layouts);
        Assert.Equal("Docked", first.Name);
        Assert.Null(first.Previous);

        // Move one icon, advance the clock, and save again with a new --name=.
        ((IDesktopIcons)desktop).SetPositions([ThreeIcons[0] with { X = 300, Y = 400 }]);
        clock.Advance(t2 - t1);

        var out2 = new StringWriter();
        var err2 = new StringWriter();
        int code2 = new CliRunner(service, out2, err2).Run(CliArguments.Parse(["save", "--name=Work Desk"]));

        Assert.Equal(CliExit.Done, code2);
        Assert.Equal($"Saved 3 icons to \"Work Desk\".{Environment.NewLine}", out2.ToString());
        Assert.Equal(string.Empty, err2.ToString());

        Layout second = Assert.Single(store.LoadAll().Layouts);
        Assert.Equal("Work Desk", second.Name);
        Assert.Equal(t2, second.SavedAt);
        Assert.NotNull(second.Previous);
        Assert.Equal(t1, second.Previous.SavedAt);
        Assert.Equal(ThreeIcons[0], second.Previous.Icons[0]);
    }

    [Fact]
    public void SaveInvalidNamesReturnExit2WithExactSpecMessagesAndEmptyStdout()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var service = new LayoutService(
            new FakeDesktopIcons(ThreeIcons),
            new FakeMonitors(TwoMonitors),
            new LayoutStore(paths),
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(1)),
            new Log(paths, clock),
            clock);

        // 41-character name -> exit 2 (NOT 3).
        var outLong = new StringWriter();
        var errLong = new StringWriter();
        int codeLong = new CliRunner(service, outLong, errLong).Run(CliArguments.Parse(["save", "--name", new string('a', 41)]));
        Assert.Equal(CliExit.Usage, codeLong);
        Assert.Equal(string.Empty, outLong.ToString());
        Assert.Equal($"A name can be at most 40 characters.{Environment.NewLine}", errLong.ToString());

        // Explicit empty name (`save --name=` and `save --name ""`) -> SPEC §10 "Enter a name.", exit 2.
        foreach (string[] emptyArgs in new[] { new[] { "save", "--name=" }, new[] { "save", "--name", "" } })
        {
            var outEmpty = new StringWriter();
            var errEmpty = new StringWriter();
            int codeEmpty = new CliRunner(service, outEmpty, errEmpty).Run(CliArguments.Parse(emptyArgs));
            Assert.Equal(CliExit.Usage, codeEmpty);
            Assert.Equal(string.Empty, outEmpty.ToString());
            Assert.Equal($"Enter a name.{Environment.NewLine}", errEmpty.ToString());
        }

        // Missing value (`save --name`) is a parser usage error distinct from empty name, handled via Program.Run.
        CliArguments missingValue = CliArguments.Parse(["save", "--name"]);
        Assert.Equal("--name needs a value", missingValue.Error);
        var outMissing = new StringWriter();
        var errMissing = new StringWriter();
        int missingCode = global::Berth.Program.Run(
            ["save", "--name"],
            outMissing,
            errMissing,
            getenv: _ => null,
            folder: _ => throw new InvalidOperationException("folder must not be queried for parser errors"));
        Assert.Equal(CliExit.Usage, missingCode);
        Assert.Equal(string.Empty, outMissing.ToString());
        Assert.Equal(string.Empty, errMissing.ToString());
    }

    [Fact]
    public void SaveRefusalDesktopUnavailableReturnsExit3WithExactStderrAndEmptyStdout()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var desktop = new FakeDesktopIcons(ThreeIcons) { Unavailable = true };
        var service = new LayoutService(
            desktop,
            new FakeMonitors(TwoMonitors),
            new LayoutStore(paths),
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(1)),
            new Log(paths, clock),
            clock);
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new CliRunner(service, output, error).Run(CliArguments.Parse(["save"]));

        Assert.Equal(CliExit.Refused, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal($"Berth cannot reach the desktop. Explorer may be restarting.{Environment.NewLine}", error.ToString());
    }

    [Fact]
    public void SaveWriteFailureReturnsExit1WithExactStderrAndEmptyStdout()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        // Create a subdirectory at the target layout file path so LoadAll succeeds and the write fails cleanly.
        string targetLayoutPath = paths.LayoutFile(ArrangementKey.From(TwoMonitors));
        Directory.CreateDirectory(targetLayoutPath);
        var service = new LayoutService(
            new FakeDesktopIcons(ThreeIcons),
            new FakeMonitors(TwoMonitors),
            new LayoutStore(paths),
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(1)),
            new Log(paths, clock),
            clock);
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new CliRunner(service, output, error).Run(CliArguments.Parse(["save"]));

        Assert.Equal(CliExit.Error, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        string errText = error.ToString();
        string expectedExact = $"Berth could not write {targetLayoutPath}: Cannot create a file when that file already exists.{Environment.NewLine}";
        Assert.Equal(expectedExact, errText);
    }

    [Fact]
    public void TestIconScopeAppliesOnlyWhenBerthDataIsSetAndPrefixIsNonBlank()
    {
        using var data = new TestData();
        string fakeRoaming = Path.Combine(data.Root, "fake-roaming");
        string fakeLocal = Path.Combine(data.Root, "fake-local");
        string Folder(Environment.SpecialFolder f) => f == Environment.SpecialFolder.ApplicationData ? fakeRoaming : fakeLocal;

        var fakeDesktop = new FakeDesktopIcons(
            new IconPosition("id-probe", "probe-1234", 10, 20),
            new IconPosition("id-other", "Owner File", 100, 200));

        // 1) BERTH_DATA unset + prefix set -> NOT scoped (both icons visible).
        string? EnvUnset(string k) => k == "BERTH_TEST_ICON_PREFIX" ? "probe-" : null;
        BerthPaths unsetPaths = BerthPaths.FromEnvironment(EnvUnset, Folder);
        Assert.False(unsetPaths.TestMode);
        IDesktopIcons unscoped1 = Composition.ApplyTestIconScope(unsetPaths, fakeDesktop, EnvUnset);
        Assert.Same(fakeDesktop, unscoped1);
        Assert.Equal(2, unscoped1.ReadIcons().Count);

        // 2) BERTH_DATA whitespace + prefix set -> NOT scoped (both icons visible).
        string? EnvWhitespace(string k) => k switch
        {
            "BERTH_DATA" => "   ",
            "BERTH_TEST_ICON_PREFIX" => "probe-",
            _ => null,
        };
        BerthPaths whitespacePaths = BerthPaths.FromEnvironment(EnvWhitespace, Folder);
        Assert.False(whitespacePaths.TestMode);
        IDesktopIcons unscoped2 = Composition.ApplyTestIconScope(whitespacePaths, fakeDesktop, EnvWhitespace);
        Assert.Same(fakeDesktop, unscoped2);
        Assert.Equal(2, unscoped2.ReadIcons().Count);

        // 3) BERTH_DATA set + prefix set -> scoped (ONLY probe visible).
        string? EnvScoped(string k) => k switch
        {
            "BERTH_DATA" => data.Root,
            "BERTH_TEST_ICON_PREFIX" => "probe-",
            _ => null,
        };
        BerthPaths testPaths = BerthPaths.FromEnvironment(EnvScoped, Folder);
        Assert.True(testPaths.TestMode);
        IDesktopIcons scoped = Composition.ApplyTestIconScope(testPaths, fakeDesktop, EnvScoped);
        Assert.IsType<ScopedDesktopIcons>(scoped);
        IconPosition sole = Assert.Single(scoped.ReadIcons());
        Assert.Equal("probe-1234", sole.Name);

        // 4) BERTH_DATA set + prefix unset -> NOT scoped.
        string? EnvNoPrefix(string k) => k == "BERTH_DATA" ? data.Root : null;
        IDesktopIcons unscoped3 = Composition.ApplyTestIconScope(BerthPaths.FromEnvironment(EnvNoPrefix, Folder), fakeDesktop, EnvNoPrefix);
        Assert.Same(fakeDesktop, unscoped3);
    }

    [Fact]
    public void ProgramRunCatchesCompositionFailureLogsWithStackAndPrintsSpecUnexpectedLine()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = global::Berth.Program.Run(
            ["save"],
            output,
            error,
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            createComposition: (_, _) => throw new InvalidOperationException("composition boom"),
            clock: clock);

        Assert.Equal(CliExit.Error, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal($"Something went wrong: composition boom. The details are in the log.{Environment.NewLine}", error.ToString());

        Assert.True(File.Exists(paths.LogFile), "Expected log file to be written on composition failure.");
        string logText = File.ReadAllText(paths.LogFile);
        string[] logLines = logText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(logLines, l => l.Contains(" FAIL ", StringComparison.Ordinal));
        Assert.Contains("FAIL Something went wrong: composition boom. The details are in the log.", logText, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: composition boom", logText, StringComparison.Ordinal);
        Assert.Contains(nameof(ProgramRunCatchesCompositionFailureLogsWithStackAndPrintsSpecUnexpectedLine), logText, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramRunCatchesPathResolutionFailureForSaveWithoutBreakingVersionOrParserErrors()
    {
        // --version does not require AppData resolution when BERTH_DATA is unset.
        var verOut = new StringWriter();
        var verErr = new StringWriter();
        int verCode = global::Berth.Program.Run(
            ["--version"],
            verOut,
            verErr,
            getenv: _ => null,
            folder: _ => throw new InvalidOperationException("appdata unavailable"));
        Assert.Equal(CliExit.Done, verCode);
        Assert.Equal($"Berth {AppInfo.Version}{Environment.NewLine}", verOut.ToString());
        Assert.Equal(string.Empty, verErr.ToString());

        // save requires AppData resolution when BERTH_DATA is unset, so a folder failure prints SPEC §18 unexpected line.
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = global::Berth.Program.Run(
            ["save"],
            output,
            error,
            getenv: _ => null,
            folder: _ => throw new InvalidOperationException("appdata unavailable"));

        Assert.Equal(CliExit.Error, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal($"Something went wrong: appdata unavailable. The details are in the log.{Environment.NewLine}", error.ToString());
    }

    [Fact]
    public void ProgramRunCatchesTestReportWriteFailureInsideExceptionBoundary()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        string existingDir = Path.Combine(data.Root, "report-is-a-directory");
        Directory.CreateDirectory(existingDir);

        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = global::Berth.Program.Run(
            ["--version"],
            output,
            error,
            getenv: k => k switch
            {
                "BERTH_DATA" => data.Root,
                "BERTH_TEST_REPORT" => existingDir,
                _ => null,
            });

        Assert.Equal(CliExit.Error, exitCode);
        Assert.Equal($"Berth {AppInfo.Version}{Environment.NewLine}", output.ToString());
        Assert.Equal($"Something went wrong: Access to the path '{existingDir}' is denied. The details are in the log.{Environment.NewLine}", error.ToString());

        Assert.True(File.Exists(paths.LogFile), "Expected log file to record the test-report write failure.");
        string logText = File.ReadAllText(paths.LogFile);
        Assert.Contains("FAIL Something went wrong: Access to the path", logText, StringComparison.Ordinal);
        Assert.Contains(nameof(TestModeSwitches.Apply), logText, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan delta) => _now += delta;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed class SaveCommandProcessTests
{
    [Fact]
    public void BerthExeSaveWithProbeIconAndPrefixSavesExactlyOneProbeIcon()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var monitors = new SystemMonitors();
        var log = new Log(paths, TimeProvider.System);
        using var shell = new ShellDesktopIcons(monitors, log);
        using var probe = ProbeIcon.Create(shell);

        BerthProcessResult result = BerthProcess.Run(
            data.Root,
            ["save"],
            testIconPrefix: probe.Prefix);

        Assert.Equal(CliExit.Done, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);
        Assert.StartsWith("Saved 1 icon to \"", result.StandardOutput, StringComparison.Ordinal);
        Assert.EndsWith($"\".{Environment.NewLine}", result.StandardOutput, StringComparison.Ordinal);

        string[] layoutFiles = Directory.GetFiles(paths.LayoutsFolder, "*.json");
        Assert.Single(layoutFiles);

        var loaded = new LayoutStore(paths).LoadAll();
        Assert.Empty(loaded.Unreadable);
        Layout saved = Assert.Single(loaded.Layouts);
        IconPosition savedIcon = Assert.Single(saved.Icons);
        Assert.Equal(probe.Icon.Id, savedIcon.Id, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(probe.Icon.Name, savedIcon.Name);
        Assert.Equal(probe.Icon.X, savedIcon.X);
        Assert.Equal(probe.Icon.Y, savedIcon.Y);
    }
}
