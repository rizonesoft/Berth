using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.Cli;

public sealed class UsageTests
{
    private static readonly MonitorInfo[] OneMonitor = [new(0, 0, 1920, 1080, 100, true)];

    [Fact]
    public void UsageTextMatchesCompleteExpectedLinesForAllCommandsAndExitCodes()
    {
        string[] lines = Usage.Text.Split(["\r\n", "\n"], StringSplitOptions.None);
        string[] expected =
        [
            "Berth saves the positions of your desktop icons and puts them back.",
            string.Empty,
            "Usage:",
            "  berth                       Starts the tray app (opens the window on first run)",
            "  berth save                  Saves the desktop to the current arrangement's layout",
            "  berth save --name <name>    Saves and names (or renames) the current arrangement's layout",
            "  berth restore               Restores the current arrangement's layout",
            "  berth undo                  Puts icons back where they were before the last restore",
            "  berth list                  Lists the saved layouts",
            "  berth --help, -h, /?        Prints the usage text",
            "  berth --version             Prints the version",
            string.Empty,
            "Exit codes:",
            "  0  Done",
            "  1  An error: a write failed, an icon did not stay where it was put, or something unexpected",
            "  2  Wrong usage: an unknown command or option, or a missing or invalid --name",
            "  3  Refused: auto arrange on, no layout, nothing to undo, desktop unreachable, busy, or unreadable layout",
        ];

        Assert.Equal(expected, lines);
    }

    [Fact]
    public void HelpAndVersionFormsWorkIdenticallyThroughCliRunnerAndProgramRunWithoutTouchingStorageOrDesktop()
    {
        using var data = new TestData();
        (LayoutService service, FakeDesktopIcons desktop, FakeMonitors monitors, BerthPaths paths) = CreateWatchedService(data.Root);

        // Every SPEC §13 help form: --help, -h, /?.
        foreach (string flag in new[] { "--help", "-h", "/?" })
        {
            var runnerOut = new StringWriter();
            var runnerErr = new StringWriter();
            int runnerCode = new CliRunner(service, runnerOut, runnerErr).Run(CliArguments.Parse([flag]));
            Assert.Equal(0, runnerCode);
            Assert.Equal(Usage.Text + Environment.NewLine, runnerOut.ToString());
            Assert.Equal(string.Empty, runnerErr.ToString());
            AssertZeroDependencyAccess(desktop, monitors, paths);

            var progOut = new StringWriter();
            var progErr = new StringWriter();
            int progCode = global::Berth.Program.Run(
                [flag],
                progOut,
                progErr,
                getenv: _ => null,
                folder: _ => throw new InvalidOperationException("help must not resolve AppData"),
                createComposition: (_, _) => throw new InvalidOperationException("help must not construct composition"),
                createMonitors: () => throw new InvalidOperationException("help must not query monitors"));
            Assert.Equal(0, progCode);
            Assert.Equal(Usage.Text + Environment.NewLine, progOut.ToString());
            Assert.Equal(string.Empty, progErr.ToString());
        }

        // SPEC §13 version form: --version.
        string expectedVersion = $"Berth {AppInfo.Version}{Environment.NewLine}";
        var verRunnerOut = new StringWriter();
        var verRunnerErr = new StringWriter();
        int verRunnerCode = new CliRunner(service, verRunnerOut, verRunnerErr).Run(CliArguments.Parse(["--version"]));
        Assert.Equal(0, verRunnerCode);
        Assert.Equal(expectedVersion, verRunnerOut.ToString());
        Assert.Equal(string.Empty, verRunnerErr.ToString());
        AssertZeroDependencyAccess(desktop, monitors, paths);

        var verProgOut = new StringWriter();
        var verProgErr = new StringWriter();
        int verProgCode = global::Berth.Program.Run(
            ["--version"],
            verProgOut,
            verProgErr,
            getenv: _ => null,
            folder: _ => throw new InvalidOperationException("version must not resolve AppData"),
            createComposition: (_, _) => throw new InvalidOperationException("version must not construct composition"),
            createMonitors: () => throw new InvalidOperationException("version must not query monitors"));
        Assert.Equal(0, verProgCode);
        Assert.Equal(expectedVersion, verProgOut.ToString());
        Assert.Equal(string.Empty, verProgErr.ToString());
    }

    [Fact]
    public void UsageErrorsPrintTwoLineDiagnosticToStderrWithEmptyStdoutAndExit2ThroughBothEntryPoints()
    {
        using var data = new TestData();
        (LayoutService service, FakeDesktopIcons desktop, FakeMonitors monitors, BerthPaths paths) = CreateWatchedService(data.Root);

        (string[] Args, string ExpectedFirstLine)[] cases =
        [
            (["frobnicate"], "berth: unknown command \"frobnicate\""),
            (["save", "--name"], "berth: --name needs a value"),
            (["--nope"], "berth: unknown option \"--nope\""),
        ];

        foreach ((string[] args, string expectedFirstLine) in cases)
        {
            string expectedErr =
                $"{expectedFirstLine}{Environment.NewLine}" +
                $"Run \"berth --help\" for the commands.{Environment.NewLine}";

            var runnerOut = new StringWriter();
            var runnerErr = new StringWriter();
            int runnerCode = new CliRunner(service, runnerOut, runnerErr).Run(CliArguments.Parse(args));
            Assert.Equal(2, runnerCode);
            Assert.Equal(string.Empty, runnerOut.ToString());
            Assert.Equal(expectedErr, runnerErr.ToString());
            AssertZeroDependencyAccess(desktop, monitors, paths);

            var progOut = new StringWriter();
            var progErr = new StringWriter();
            int progCode = global::Berth.Program.Run(
                args,
                progOut,
                progErr,
                getenv: _ => null,
                folder: _ => throw new InvalidOperationException("usage error must not resolve AppData"),
                createComposition: (_, _) => throw new InvalidOperationException("usage error must not construct composition"),
                createMonitors: () => throw new InvalidOperationException("usage error must not query monitors"));
            Assert.Equal(2, progCode);
            Assert.Equal(string.Empty, progOut.ToString());
            Assert.Equal(expectedErr, progErr.ToString());
        }
    }

    private static (LayoutService Service, FakeDesktopIcons Desktop, FakeMonitors Monitors, BerthPaths Paths) CreateWatchedService(string root)
    {
        BerthPaths paths = BerthPaths.ForRoot(root);
        var desktop = new FakeDesktopIcons();
        var monitors = new FakeMonitors(OneMonitor);
        var service = new LayoutService(
            desktop,
            monitors,
            new LayoutStore(paths),
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(1)),
            new Log(paths, TimeProvider.System),
            TimeProvider.System);
        return (service, desktop, monitors, paths);
    }

    private static void AssertZeroDependencyAccess(FakeDesktopIcons desktop, FakeMonitors monitors, BerthPaths paths)
    {
        Assert.Equal(0, desktop.Reads);
        Assert.Empty(desktop.Calls);
        Assert.Equal(0, monitors.Reads);
        Assert.False(Directory.Exists(paths.LayoutsFolder));
        Assert.False(File.Exists(paths.LogFile));
    }
}
