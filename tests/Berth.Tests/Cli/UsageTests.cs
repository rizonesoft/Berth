using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;

namespace Berth.Tests.Cli;

public sealed class UsageTests
{
    private static readonly MonitorInfo[] OneMonitor = [new(0, 0, 1920, 1080, 100, true)];

    [Fact]
    public void UsageTextHasExactFirstLineHeadingsAllEightCommandRowsAndAllFourExitCodes()
    {
        string[] lines = Usage.Text.Split(["\r\n", "\n"], StringSplitOptions.None);
        Assert.Equal("Berth saves the positions of your desktop icons and puts them back.", lines[0]);

        int usageHeading = Array.IndexOf(lines, "Usage:");
        int exitHeading = Array.IndexOf(lines, "Exit codes:");
        Assert.True(usageHeading > 0, "Expected 'Usage:' heading.");
        Assert.True(exitHeading > usageHeading, "Expected 'Exit codes:' heading after 'Usage:'.");

        string[] commandRows = lines[(usageHeading + 1)..exitHeading]
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.Equal(8, commandRows.Length);
        Assert.Contains(commandRows, l => l.StartsWith("  berth  ", StringComparison.Ordinal));
        Assert.Contains(commandRows, l => l.StartsWith("  berth save  ", StringComparison.Ordinal));
        Assert.Contains(commandRows, l => l.StartsWith("  berth save --name <name>  ", StringComparison.Ordinal));
        Assert.Contains(commandRows, l => l.StartsWith("  berth restore  ", StringComparison.Ordinal));
        Assert.Contains(commandRows, l => l.StartsWith("  berth undo  ", StringComparison.Ordinal));
        Assert.Contains(commandRows, l => l.StartsWith("  berth list  ", StringComparison.Ordinal));
        Assert.Contains(commandRows, l => l.StartsWith("  berth --help, -h, /?  ", StringComparison.Ordinal));
        Assert.Contains(commandRows, l => l.StartsWith("  berth --version  ", StringComparison.Ordinal));

        string[] exitRows = lines[(exitHeading + 1)..]
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.Equal(4, exitRows.Length);
        Assert.Contains(exitRows, l => l.StartsWith("  0  ", StringComparison.Ordinal));
        Assert.Contains(exitRows, l => l.StartsWith("  1  ", StringComparison.Ordinal));
        Assert.Contains(exitRows, l => l.StartsWith("  2  ", StringComparison.Ordinal));
        Assert.Contains(exitRows, l => l.StartsWith("  3  ", StringComparison.Ordinal));
    }

    [Fact]
    public void HelpAndVersionFormsWorkIdenticallyThroughCliRunnerAndProgramRunWithoutTouchingStorageOrDesktop()
    {
        using var data = new TestData();
        LayoutService service = CreateUnusedService(data.Root);

        // Every SPEC §13 help form: --help, -h, /?.
        foreach (string flag in new[] { "--help", "-h", "/?" })
        {
            var runnerOut = new StringWriter();
            var runnerErr = new StringWriter();
            int runnerCode = new CliRunner(service, runnerOut, runnerErr).Run(CliArguments.Parse([flag]));
            Assert.Equal(0, runnerCode);
            Assert.Equal(Usage.Text + Environment.NewLine, runnerOut.ToString());
            Assert.Equal(string.Empty, runnerErr.ToString());

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
        LayoutService service = CreateUnusedService(data.Root);

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

    private static LayoutService CreateUnusedService(string root)
    {
        BerthPaths paths = BerthPaths.ForRoot(root);
        return new LayoutService(
            new FakeDesktopIcons(),
            new FakeMonitors(OneMonitor),
            new LayoutStore(paths),
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(1)),
            new Log(paths, TimeProvider.System),
            TimeProvider.System);
    }
}
