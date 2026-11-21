using System.Windows;
using System.Windows.Threading;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Window;
using Microsoft.Win32;

namespace Berth.App;

/// <summary>Tray application entry point (SPEC §17): claims the single instance, attaches crash handling, and runs <see cref="BerthApplication"/>.</summary>
internal static class TrayApp
{
    private static readonly TimeSpan CrashDisposeTimeout = TimeSpan.FromMilliseconds(500);

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
            paths: paths,
            getenv: getenv);
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
        Action<int>? exitProcess = null,
        Func<string, string?>? getenv = null,
        Composition? composition = null,
        Action<TrayMenu>? onTrayMenuCreated = null,
        Func<IMainWindow>? createMainWindow = null,
        Action? onInstanceActivatedHandled = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(createInstance);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(showCrashMessage);

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

        BerthApplication? activeApp = null;
        TrayIcon? activeTrayIcon = null;
        Action<int> effectiveExit = exitProcess ?? Environment.Exit;
        var crashHandler = new CrashHandler(
            log,
            showCrashMessage,
            exitProcess: CreateCrashExitAction(
                () => activeApp?.Dispatcher,
                () => activeTrayIcon,
                effectiveExit));
        try
        {
            if (runPrimary is not null)
            {
                return runPrimary(instance, crashHandler);
            }

            var app = new BerthApplication();
            activeApp = app;
            using IDisposable crashAttachment = crashHandler.Attach(app);

            TaskbarTheme effectiveTheme = taskbarTheme ?? new TaskbarTheme();
            TimeProvider effectiveClock = clock ?? TimeProvider.System;
            TimeZoneInfo effectiveZone = zone ?? TimeZoneInfo.Local;
            BerthPaths effectivePaths = composition?.Paths
                ?? paths
                ?? BerthPaths.FromEnvironment(getenv ?? Environment.GetEnvironmentVariable);
            using Composition? ownedComposition = composition is null
                ? Composition.Create(effectivePaths, log, getenv, effectiveClock)
                : null;
            Composition effectiveComposition = composition ?? ownedComposition!;
            IMonitors effectiveMonitors = monitors ?? effectiveComposition.Monitors;
            var settingsStore = new SettingsStore(effectiveComposition.Paths);

            void MarshalToUi(Action action, Action? onAborted)
            {
                if (app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                {
                    onAborted?.Invoke();
                    return;
                }

                if (app.Dispatcher.CheckAccess())
                {
                    action();
                    return;
                }

                DispatcherOperation op = app.Dispatcher.BeginInvoke(action);
                if (onAborted is not null)
                {
                    op.Aborted += (_, _) => onAborted();
                    if (op.Status == DispatcherOperationStatus.Aborted)
                    {
                        onAborted();
                    }
                }
            }

            Layout? initialActive = TrayIcon.FindActiveLayout(effectiveComposition.Store, effectiveMonitors);

            using var trayIcon = new TrayIcon(
                effectiveTheme,
                initialActive,
                effectiveClock.GetUtcNow(),
                effectiveZone,
                marshal: action => MarshalToUi(action, onAborted: null));
            activeTrayIcon = trayIcon;

            using var trayMenu = new TrayMenu(
                effectiveComposition.Service,
                effectiveComposition.Store,
                effectiveMonitors,
                effectiveComposition.Desktop,
                settingsStore,
                effectiveComposition.Log,
                MarshalToUi);

            Func<IMainWindow> effectiveCreateMainWindow = createMainWindow
                ?? (() => CreateProductionMainWindow(app, settingsStore, effectiveComposition, effectiveMonitors, effectiveClock, trayMenu));
            IMainWindow? mainWindow = null;
            bool firstWindowOpenedNotified = false;
            bool exitRequested = false;

            void OpenMainWindow()
            {
                if (exitRequested || app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                mainWindow ??= effectiveCreateMainWindow();
                mainWindow.BringToFront();
                if (!firstWindowOpenedNotified)
                {
                    firstWindowOpenedNotified = true;
                    onWindowOpened?.Invoke();
                }
            }

            trayIcon.OpenRequested += OpenMainWindow;
            trayMenu.OpenRequested += OpenMainWindow;

            trayMenu.OperationFinished += _ =>
            {
                trayIcon.TrySetTooltip(trayMenu.LastObservedActiveLayout, effectiveClock.GetUtcNow(), effectiveZone);
            };

            trayMenu.ExitRequested += () =>
            {
                exitRequested = true;
                trayMenu.BeginDrain(() =>
                {
                    if (!app.Dispatcher.HasShutdownStarted && !app.Dispatcher.HasShutdownFinished)
                    {
                        mainWindow?.CloseForShutdown();
                        trayIcon.Dispose();
                        app.Shutdown(0);
                    }
                });
            };

            trayIcon.SetContextMenu(trayMenu.Strip);
            onTrayIconCreated?.Invoke(trayIcon);
            onTrayMenuCreated?.Invoke(trayMenu);

            void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) =>
                HandleUserPreferenceChanged(effectiveTheme, e.Category);

            void OnInstanceActivated() => MarshalToUi(
                () =>
                {
                    OpenMainWindow();
                    onInstanceActivatedHandled?.Invoke();
                },
                onAborted: onInstanceActivatedHandled);

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            try
            {
                // Reconcile immediately after subscribing so any theme flip between initial read and subscribe is never missed.
                effectiveTheme.OnSettingChange("ImmersiveColorSet");

                app.Startup += (_, _) =>
                {
                    if (openWindow)
                    {
                        OpenMainWindow();
                    }

                    onAppStartup?.Invoke(app);
                };

                app.InitializeTheme();
                // Subscribe after theme initialization so any replayed pending activation creates the window on the STA
                // dispatcher with all theme tokens already loaded.
                instance.Activated += OnInstanceActivated;
                try
                {
                    return app.Run();
                }
                finally
                {
                    instance.Activated -= OnInstanceActivated;
                }
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

    internal static MainWindow CreateProductionMainWindow(
        BerthApplication app,
        SettingsStore settingsStore,
        Composition composition,
        IMonitors monitors,
        TimeProvider clock,
        TrayMenu? trayMenu = null)
    {
        var viewModel = new MainViewModel(
            composition.Store,
            composition.Desktop,
            monitors,
            clock,
            marshalToUiAsync: action =>
            {
                if (app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished)
                {
                    return Task.CompletedTask;
                }

                if (app.Dispatcher.CheckAccess())
                {
                    action();
                    return Task.CompletedTask;
                }

                return app.Dispatcher.InvokeAsync(action).Task;
            },
            service: composition.Service);

        double pollSeconds = app.TryFindResource("Berth.VisiblePollSeconds") is double s && s > 0d ? s : 5d;
        var poller = new DesktopPoller(
            viewModel.RefreshAsync,
            new DispatcherPollerTimer(app.Dispatcher),
            TimeSpan.FromSeconds(pollSeconds),
            onError: ex => composition.Log.FailWithDetails("Desktop poll failed.", ex.ToString()));

        var window = new MainWindow(
            settingsStore,
            monitors,
            composition.Log,
            boundsInterop: null,
            viewModel: viewModel,
            poller: poller);

        if (trayMenu is not null)
        {
            viewModel.Detail.SetExternalBusy(trayMenu.IsOperationBusy);
            void OnTrayBusyChanged(bool busy) => viewModel.Detail.SetExternalBusy(busy);
            void OnTrayCompleted(WindowOperationKind kind, OperationResult result) =>
                _ = viewModel.OnExternalOperationFinishedAsync(kind, result);
            trayMenu.OperationBusyChanged += OnTrayBusyChanged;
            trayMenu.OperationCompleted += OnTrayCompleted;
            window.Closed += (_, _) =>
            {
                trayMenu.OperationBusyChanged -= OnTrayBusyChanged;
                trayMenu.OperationCompleted -= OnTrayCompleted;
            };
        }

        return window;
    }

    internal static void HandleUserPreferenceChanged(TaskbarTheme theme, UserPreferenceCategory category)
    {
        if (category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            theme.OnSettingChange("ImmersiveColorSet");
        }
    }

    /// <summary>
    /// Gives the owning UI dispatcher a bounded opportunity to dispose the tray icon cleanly before falling back
    /// to direct disposal and exiting the process, preventing a ghost tray icon on background-thread crashes.
    /// </summary>
    internal static Action<int> CreateCrashExitAction(
        Func<Dispatcher?> getDispatcher,
        Func<TrayIcon?> getTrayIcon,
        Action<int> exitProcess) =>
        code =>
        {
            try
            {
                TrayIcon? icon = getTrayIcon();
                if (icon is not null)
                {
                    Dispatcher? dispatcher = getDispatcher();
                    if (dispatcher is not null
                        && !dispatcher.HasShutdownStarted
                        && !dispatcher.HasShutdownFinished
                        && !dispatcher.CheckAccess())
                    {
                        using var cts = new CancellationTokenSource(CrashDisposeTimeout);
                        try
                        {
                            dispatcher.Invoke(icon.Dispose, DispatcherPriority.Send, cts.Token);
                        }
                        catch (Exception)
                        {
                            icon.Dispose();
                        }
                    }
                    else
                    {
                        icon.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // Best-effort tray icon removal before process exit so no ghost icon remains.
            }
            finally
            {
                exitProcess(code);
            }
        };
}
