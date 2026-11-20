using System.IO;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.App;

/// <summary>What <c>Program.Main</c> does for a parsed command line (SPEC §17).</summary>
internal enum StartupMode
{
    Command,
    TrayApp,
    UsageError,
}

/// <summary>Routing decision for one launch of <c>berth.exe</c> (SPEC §17).</summary>
internal sealed record StartupPlan(StartupMode Mode, bool OpenWindow)
{
    public static StartupPlan For(CliArguments args, bool settingsExist)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Error is not null)
        {
            return new StartupPlan(StartupMode.UsageError, OpenWindow: false);
        }

        if (args.Command == CliCommand.None)
        {
            return new StartupPlan(StartupMode.TrayApp, OpenWindow: !settingsExist);
        }

        return new StartupPlan(StartupMode.Command, OpenWindow: false);
    }
}

/// <summary>First-run persistence helper: writes <see cref="Settings.Default"/> only after the first-run window opens (SPEC §17, §18).</summary>
internal static class FirstRun
{
    public static void CompleteAfterWindowOpened(SettingsStore store, Log log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            store.TrySaveIfMissing(Settings.Default);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Fail(Messages.WriteFailed(store.FilePath, ex.Message));
        }
    }
}
