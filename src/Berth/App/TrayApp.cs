using System.Windows;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Microsoft.Win32;

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
        BerthPaths paths = BerthPaths.FromEnvironment(getenv);
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
            runPrimary: null,
            paths: paths);
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
        Action<BerthApplication>? onAppStartup = null,
        BerthPaths? paths = null,
        IMonitors? monitors = null,
        TimeProvider? clock = null,
        TimeZoneInfo? zone = null,
        TaskbarTheme? taskbarTheme = null,
        Action<TrayIcon>? onTrayIconCreated = null,
        Action<int>? exitProcess = null)
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

        TrayIcon? activeTrayIcon = null;
        Action<int> effectiveExit = exitProcess ?? Environment.Exit;
        var crashHandler = new CrashHandler(
            log,
            showCrashMessage,
            exitProcess: code =>
            {
                try
                {
                    activeTrayIcon?.Dispose();
                }
                catch (Exception)
                {
                    // Best-effort tray icon removal before process exit so no ghost icon remains.
                }
                finally
                {
                    effectiveExit(code);
                }
            });
        try
        {
            if (runPrimary is not null)
            {
                return runPrimary(instance, crashHandler);
            }

            var app = new BerthApplication();
            using IDisposable crashAttachment = crashHandler.Attach(app);

            TaskbarTheme effectiveTheme = taskbarTheme ?? new TaskbarTheme();
            TimeProvider effectiveClock = clock ?? TimeProvider.System;
            TimeZoneInfo effectiveZone = zone ?? TimeZoneInfo.Local;
            Layout? initialActive = paths is not null
                ? TrayIcon.FindActiveLayout(new LayoutStore(paths), monitors ?? new SystemMonitors())
                : null;

            using var trayIcon = new TrayIcon(
                effectiveTheme,
                initialActive,
                effectiveClock.GetUtcNow(),
                effectiveZone,
                marshal: action =>
                {
                    if (!app.Dispatcher.HasShutdownStarted && !app.Dispatcher.HasShutdownFinished)
                    {
                        if (app.Dispatcher.CheckAccess())
                        {
                            action();
                        }
                        else
                        {
                            _ = app.Dispatcher.BeginInvoke(action);
                        }
                    }
                });
            activeTrayIcon = trayIcon;
            onTrayIconCreated?.Invoke(trayIcon);

            void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                {
                    effectiveTheme.OnSettingChange("ImmersiveColorSet");
                }
            }

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            try
            {
                if (onAppStartup is not null)
                {
                    app.Startup += (_, _) => onAppStartup(app);
                }

                app.InitializeTheme();
                return app.Run();
            }
            finally
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            }
        }
        catch (Exception ex)
        {
            crashHandler.Handle(ex, "TrayApp.Startup", showDialog: true);
            return CliExit.Error;
        }
    }
}
