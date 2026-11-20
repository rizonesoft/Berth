using System.IO;
using Berth.App;
using Berth.Cli;
using Berth.Core;
using Berth.Desktop;

namespace Berth;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) =>
        Run(args, Console.Out, Console.Error);

    internal static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        Func<string, string?>? getenv = null,
        Func<Environment.SpecialFolder, string>? folder = null,
        Func<BerthPaths, Log, Composition>? createComposition = null,
        TimeProvider? clock = null,
        Func<IMonitors>? createMonitors = null,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        Func<string, string?> readEnv = getenv ?? Environment.GetEnvironmentVariable;
        TimeProvider effectiveClock = clock ?? TimeProvider.System;
        BerthPaths? paths = null;
        Log? log = null;
        int exitCode;

        try
        {
            CliArguments parsed = CliArguments.Parse(args);

            // Apply SPEC §14 test-mode switches before publishing any command output so a failing test-report write
            // never leaves a success line on stdout alongside an error exit code.
            if (!string.IsNullOrWhiteSpace(readEnv("BERTH_DATA")))
            {
                paths = BerthPaths.FromEnvironment(readEnv, folder);
                log = new Log(paths, effectiveClock);
                TestModeSwitches.Apply(paths, readEnv);
            }

            if (CliRunner.TryRunStandalone(parsed, output, error, out int standaloneExit))
            {
                exitCode = standaloneExit;
            }
            else if (parsed.Command == CliCommand.List)
            {
                paths ??= BerthPaths.FromEnvironment(readEnv, folder);
                log ??= new Log(paths, effectiveClock);
                IMonitors monitors = createMonitors is not null ? createMonitors() : new SystemMonitors();
                exitCode = CliRunner.RunList(
                    new LayoutStore(paths),
                    paths,
                    monitors,
                    log,
                    output,
                    error,
                    zone ?? TimeZoneInfo.Local);
            }
            else if (parsed.Command is CliCommand.Save or CliCommand.Restore or CliCommand.Undo)
            {
                paths ??= BerthPaths.FromEnvironment(readEnv, folder);
                log ??= new Log(paths, effectiveClock);
                using Composition app = createComposition is not null
                    ? createComposition(paths, log)
                    : Composition.Create(paths, log, readEnv, effectiveClock);
                var runner = new CliRunner(app.Service, output, error);
                exitCode = runner.Run(parsed);
            }
            else
            {
                exitCode = CliExit.Usage;
            }
        }
        catch (Exception ex)
        {
            string message = Messages.Unexpected(ex.Message);
            log?.Fail(message, ex);
            error.WriteLine(message);
            exitCode = CliExit.Error;
        }

        return exitCode;
    }
}
