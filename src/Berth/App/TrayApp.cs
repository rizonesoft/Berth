using System.Windows;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.App;

/// <summary>Tray application entry point (SPEC §17): claims the single instance, attaches crash handling, and runs <see cref="BerthApplication"/>.</summary>
internal static class TrayApp
{
    public static int Run(
        bool openWindow,
        Settings settings,
        Action? onWindowOpened,
        Func<string, string?> getenv,
        Log log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(getenv);
        ArgumentNullException.ThrowIfNull(log);

        string instanceName = SingleInstance.ResolveName(getenv);
        return RunWithInstance(
            openWindow,
            settings,
            onWindowOpened,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: text =>
            {
                _ = System.Windows.MessageBox.Show(text, "Berth", MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            },
            runPrimary: null);
    }

    internal static int RunWithInstance(
        bool openWindow,
        Settings settings,
        Action? onWindowOpened,
        Func<SingleInstance> createInstance,
        Log log,
        Func<string, bool> showCrashMessage,
        Func<SingleInstance, CrashHandler, int>? runPrimary,
        Action? onBeforeReclaim = null,
        Action<BerthApplication>? onAppStartup = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(createInstance);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(showCrashMessage);

        _ = openWindow;
        _ = onWindowOpened;

        using SingleInstance instance = createInstance();
        if (!instance.TryClaim())
        {
            SecondaryOutcome outcome = instance.RunSecondary(onBeforeReclaim);
            if (outcome == SecondaryOutcome.ActivatedExisting)
            {
                return CliExit.Done;
            }

            if (outcome == SecondaryOutcome.Refused)
            {
                return CliExit.Refused;
            }
        }

        var crashHandler = new CrashHandler(log, showCrashMessage);
        try
        {
            if (runPrimary is not null)
            {
                return runPrimary(instance, crashHandler);
            }

            var app = new BerthApplication();
            using IDisposable crashAttachment = crashHandler.Attach(app);
            if (onAppStartup is not null)
            {
                app.Startup += (_, _) => onAppStartup(app);
            }

            app.InitializeTheme();
            return app.Run();
        }
        catch (Exception ex)
        {
            crashHandler.Handle(ex, "TrayApp.Startup", showDialog: true);
            return CliExit.Error;
        }
    }
}
