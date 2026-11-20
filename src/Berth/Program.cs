using System.IO;
using Berth.App;
using Berth.Cli;
using Berth.Core;

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
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        Func<string, string?> readEnv = getenv ?? Environment.GetEnvironmentVariable;
        BerthPaths? paths = null;
        Log? log = null;
        int exitCode;

        try
        {
            CliArguments parsed = CliArguments.Parse(args);

            if (parsed.Error is not null)
            {
                exitCode = CliExit.Usage;
            }
            else if (parsed.Command == CliCommand.Version)
            {
                output.WriteLine("Berth " + AppInfo.Version);
                exitCode = CliExit.Done;
            }
            else if (parsed.Command == CliCommand.Save)
            {
                TimeProvider effectiveClock = clock ?? TimeProvider.System;
                paths = BerthPaths.FromEnvironment(readEnv, folder);
                log = new Log(paths, effectiveClock);
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

            if (paths is null && !string.IsNullOrWhiteSpace(readEnv("BERTH_DATA")))
            {
                paths = BerthPaths.FromEnvironment(readEnv, folder);
                log ??= new Log(paths, clock ?? TimeProvider.System);
            }

            if (paths is not null)
            {
                TestModeSwitches.Apply(paths, readEnv);
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
