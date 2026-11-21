using System.Diagnostics;
using System.Windows.Threading;
using Berth.App;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Tests.App;

/// <summary>
/// Runs isolated single-process STA scenarios inside a fresh <c>Berth.Tests.exe --berth-tray-child &lt;scenario&gt;</c>
/// child process so tests can exercise the real <see cref="TrayApp.RunWithInstance"/> primary WPF loop
/// (<see cref="BerthApplication"/> + <see cref="CrashHandler.Attach"/>) without colliding with the test host's shared
/// <see cref="AppFixture"/>.
/// </summary>
internal static class IsolatedStaChildRunner
{
    public const string Switch = "--berth-tray-child";
    public const string DialogFileEnvVar = "BERTH_TEST_CHILD_DIALOG_FILE";
    public const string RegPathEnvVar = "BERTH_TEST_CHILD_REG_PATH";
    public const string ReportFileEnvVar = "BERTH_TEST_CHILD_REPORT_FILE";
    public const string TimerShutdownScenario = "tray-timer-shutdown";
    public const string DispatcherCrashScenario = "tray-dispatcher-crash";
    public const string PrimaryStartupTrayScenario = "tray-primary-startup";
    public const string MenuSaveAndExitDrainScenario = "tray-menu-save-and-exit-drain";
    public const string WindowHideReopenAndMenuExitScenario = "window-hide-reopen-menu-exit";
    public const string TrayAppWiringFirstRunScenario = "tray-wiring-first-run";
    public const string TrayAppWiringTrayOnlyScenario = "tray-wiring-tray-only";
    public const string ExitBlocksActivationDuringDrainScenario = "exit-blocks-activation-drain";
    public const string ProductionTrayPlacementScenario = "production-tray-placement";
    public const string SettingsFailureHideReopenScenario = "settings-failure-hide-reopen";
    public const string RenderMainWindowFrameScenario = "render-main-window-frame";
    public const string SystemEventsTrayWiringScenario = "system-events-tray-wiring";
    public const string AutoRestoreTrayScenario = "auto-restore-tray";
    public const string AutoRestoreDirectShutdownScenario = "auto-restore-direct-shutdown";
    public const string AutoRestoreBalloonScenario = "auto-restore-balloon";
    public const string TrayReaddOnExplorerRestartScenario = "tray-readd-explorer-restart";
    public const string AutoRestoreWindowScenario = "auto-restore-window";
    public const string AutoRestoreMidOpenAndTrayOnlyScenario = "auto-restore-mid-open-tray-only";

    public static int RunMain(string[] args)
    {
        if (args.Length != 1)
        {
            return 2;
        }

        string scenario = args[0];
        int exitCode = 99;
        var staThread = new Thread(() =>
        {
            exitCode = RunScenario(scenario);
        })
        {
            Name = "Berth.Tests.IsolatedStaChild",
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();
        return exitCode;
    }

    public static (int ExitCode, string StdOut, string StdErr) RunChild(
        string scenario,
        string dataRoot,
        string instanceName,
        string? dialogFile = null,
        TimeSpan? timeout = null,
        string? regPath = null,
        string? reportFile = null)
    {
        string testExe = Path.Combine(AppContext.BaseDirectory, "Berth.Tests.exe");
        if (!File.Exists(testExe))
        {
            throw new FileNotFoundException($"Could not locate Berth.Tests.exe at '{testExe}'.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = testExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(Switch);
        psi.ArgumentList.Add(scenario);
        psi.Environment["BERTH_DATA"] = dataRoot;
        psi.Environment["BERTH_INSTANCE"] = instanceName;
        if (dialogFile is not null)
        {
            psi.Environment[DialogFileEnvVar] = dialogFile;
        }

        if (regPath is not null)
        {
            psi.Environment[RegPathEnvVar] = regPath;
        }

        if (reportFile is not null)
        {
            psi.Environment[ReportFileEnvVar] = reportFile;
        }

        using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start isolated STA child.");
        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
        try
        {
            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
            if (!proc.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
            {
                proc.Kill(entireProcessTree: true);
                throw new TimeoutException($"Isolated STA child '{scenario}' did not exit within {effectiveTimeout.TotalSeconds:F1}s.");
            }

            Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(5));
            return (proc.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        catch
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private static int RunScenario(string scenario)
    {
        string dataRoot = Environment.GetEnvironmentVariable("BERTH_DATA")
            ?? throw new InvalidOperationException("BERTH_DATA must be set for isolated child.");
        string instanceName = Environment.GetEnvironmentVariable("BERTH_INSTANCE")
            ?? throw new InvalidOperationException("BERTH_INSTANCE must be set for isolated child.");
        string? dialogFile = Environment.GetEnvironmentVariable(DialogFileEnvVar);

        BerthPaths paths = BerthPaths.ForRoot(dataRoot);
        var log = new Log(paths, TimeProvider.System);

        return scenario switch
        {
            TimerShutdownScenario => TrayApp.RunWithInstance(
                openWindow: false,
                Settings.Default,
                onWindowOpened: null,
                () => new SingleInstance(instanceName),
                log,
                showCrashMessage: _ => true,
                runPrimary: null,
                onAppStartup: app =>
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        app.Shutdown(0);
                    };
                    timer.Start();
                }),

            DispatcherCrashScenario => TrayApp.RunWithInstance(
                openWindow: false,
                Settings.Default,
                onWindowOpened: null,
                () => new SingleInstance(instanceName),
                log,
                showCrashMessage: text =>
                {
                    if (!string.IsNullOrWhiteSpace(dialogFile))
                    {
                        File.WriteAllText(dialogFile, text);
                    }

                    return true;
                },
                runPrimary: null,
                onAppStartup: app =>
                {
                    _ = app.Dispatcher.BeginInvoke(
                        DispatcherPriority.Normal,
                        new Action(() => throw new InvalidOperationException("Isolated child dispatcher boom")));
                }),

            PrimaryStartupTrayScenario => RunPrimaryStartupTrayScenario(paths, log, instanceName),

            MenuSaveAndExitDrainScenario => RunMenuSaveAndExitDrainScenario(paths, log, instanceName),

            WindowHideReopenAndMenuExitScenario => RunWindowHideReopenAndMenuExitScenario(paths, log, instanceName),

            TrayAppWiringFirstRunScenario => RunTrayAppWiringScenario(paths, log, instanceName, openWindow: true),

            TrayAppWiringTrayOnlyScenario => RunTrayAppWiringScenario(paths, log, instanceName, openWindow: false),

            ExitBlocksActivationDuringDrainScenario => RunExitBlocksActivationDuringDrainScenario(paths, log, instanceName),

            ProductionTrayPlacementScenario => RunProductionTrayPlacementScenario(paths, log, instanceName),

            SettingsFailureHideReopenScenario => RunSettingsFailureHideReopenScenario(paths, log, instanceName),

            RenderMainWindowFrameScenario => RunRenderMainWindowFrameScenario(),

            SystemEventsTrayWiringScenario => RunSystemEventsTrayWiringScenario(paths, log, instanceName),

            AutoRestoreTrayScenario => RunAutoRestoreTrayScenario(paths, log, instanceName),

            AutoRestoreDirectShutdownScenario => RunAutoRestoreDirectShutdownScenario(paths, log, instanceName),

            AutoRestoreBalloonScenario => RunAutoRestoreBalloonScenario(paths, log, instanceName),

            TrayReaddOnExplorerRestartScenario => RunTrayReaddOnExplorerRestartScenario(paths, log, instanceName),

            AutoRestoreWindowScenario => RunAutoRestoreWindowScenario(paths, log, instanceName),

            AutoRestoreMidOpenAndTrayOnlyScenario => RunAutoRestoreMidOpenAndTrayOnlyScenario(paths, log, instanceName),

            _ => throw new InvalidOperationException($"Unknown scenario '{scenario}'."),
        };
    }

    private static int RunPrimaryStartupTrayScenario(BerthPaths paths, Log log, string instanceName)
    {
        string regPath = Environment.GetEnvironmentVariable(RegPathEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REG_PATH must be set.");
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(new MonitorInfo(0, 0, 1920, 1080, 100, true));
        var theme = new Berth.Desktop.TaskbarTheme(regPath);
        TrayIcon? capturedIcon = null;
        bool initialVisible = false;
        string initialTooltip = string.Empty;
        IntPtr initialHandle = IntPtr.Zero;

        return TrayApp.RunWithInstance(
            openWindow: false,
            Settings.Default,
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: new FixedChildClock(fixedNow),
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            onTrayIconCreated: icon =>
            {
                capturedIcon = icon;
                initialVisible = icon.IsVisible;
                initialTooltip = icon.TooltipText;
                initialHandle = icon.IconHandle;

                // Simulate a taskbar theme change occurring between icon creation and UserPreferenceChanged subscription.
                using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(regPath, writable: true);
                key.SetValue("SystemUsesLightTheme", 1, Microsoft.Win32.RegistryValueKind.DWord);
            },
            onAppStartup: app =>
            {
                bool reconciledIsLight = theme.IsLight;
                bool handleChanged = capturedIcon is not null && capturedIcon.IconHandle != initialHandle;
                File.WriteAllLines(
                    reportFile,
                    [
                        $"initialVisible={initialVisible}",
                        $"initialTooltip={initialTooltip}",
                        $"reconciledIsLight={reconciledIsLight}",
                        $"handleChanged={handleChanged}",
                    ]);
                app.Shutdown(0);
            });
    }

    private static int RunMenuSaveAndExitDrainScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(new MonitorInfo(0, 0, 1920, 1080, 100, true));
        using var desktop = new GatedDesktopIcons(new IconPosition("id-a", "Alpha", 100, 200));
        var store = new LayoutStore(paths);
        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(
            Environment.GetEnvironmentVariable(RegPathEnvVar)
            ?? @"Software\Berth.Tests\MissingTrayTheme");

        TrayIcon? capturedIcon = null;
        TrayMenu? capturedMenu = null;
        string initialTooltip = string.Empty;
        string tooltipAfterSave = string.Empty;
        bool attachedToNotifyIcon = false;
        int completedCount = 0;
        bool exitedBeforeRelease = false;
        bool iconVisibleBeforeRelease = false;
        int completedCountBeforeRelease = -1;
        OperationKind? secondResultKind = null;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: false,
            Settings.Default,
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayIconCreated: icon =>
            {
                capturedIcon = icon;
                initialTooltip = icon.TooltipText;
            },
            onTrayMenuCreated: menu =>
            {
                capturedMenu = menu;
                attachedToNotifyIcon = ReferenceEquals(capturedIcon?.NotifyIcon.ContextMenuStrip, menu.Strip);
            },
            onAppStartup: app =>
            {
                if (capturedIcon is null || capturedMenu is null)
                {
                    app.Shutdown(2);
                    return;
                }

                // Trigger 1: Save via the real attached menu item and observe production tooltip refresh.
                void OnFirstCompleted(OperationResult firstResult)
                {
                    _ = firstResult;
                    capturedMenu.OperationFinished -= OnFirstCompleted;
                    completedCount++;
                    tooltipAfterSave = capturedIcon.TooltipText;

                    // Trigger 2: Hold the next Save in flight, click Exit on the attached menu, verify it waits, then release.
                    desktop.ArmGate();
                    capturedMenu.OperationFinished += r =>
                    {
                        secondResultKind = r.Kind;
                        completedCount++;
                    };
                    capturedMenu.SaveItem.PerformClick();

                    _ = Task.Run(() =>
                    {
                        desktop.WaitUntilEntered(TimeSpan.FromSeconds(5));
                        app.Dispatcher.Invoke(() =>
                        {
                            capturedMenu.ExitItem.PerformClick();
                            exitedBeforeRelease = app.Dispatcher.HasShutdownStarted;
                            iconVisibleBeforeRelease = capturedIcon.IsVisible;
                            completedCountBeforeRelease = completedCount;
                        });
                        Thread.Sleep(50);
                        desktop.ReleaseGate();
                    });
                }

                capturedMenu.OperationFinished += OnFirstCompleted;
                capturedMenu.SaveItem.PerformClick();
            });

        bool visibleAfterExit = capturedIcon?.IsVisible ?? true;
        File.WriteAllLines(
            reportFile,
            [
                $"attachedToNotifyIcon={attachedToNotifyIcon}",
                $"initialTooltip={initialTooltip}",
                $"tooltipAfterSave={tooltipAfterSave}",
                $"completedCountBeforeRelease={completedCountBeforeRelease}",
                $"iconVisibleBeforeRelease={iconVisibleBeforeRelease}",
                $"completedCount={completedCount}",
                $"secondResultKind={secondResultKind}",
                $"exitedBeforeRelease={exitedBeforeRelease}",
                $"visibleAfterExit={visibleAfterExit}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private sealed class GatedDesktopIcons(params IconPosition[] initial) : IDesktopIcons, IDisposable
    {
        private readonly Berth.Tests.Fakes.FakeDesktopIcons _inner = new(initial);
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private int _gateArmed;

        public void ArmGate()
        {
            _entered.Reset();
            _release.Reset();
            Volatile.Write(ref _gateArmed, 1);
        }

        public void WaitUntilEntered(TimeSpan timeout)
        {
            if (!_entered.Wait(timeout))
            {
                throw new TimeoutException("In-flight desktop read did not start in time.");
            }
        }

        public void ReleaseGate() => _release.Set();

        public void Dispose()
        {
            _entered.Dispose();
            _release.Dispose();
        }

        public bool AutoArrange => ((IDesktopIcons)_inner).AutoArrange;

        public IReadOnlyList<IconPosition> ReadIcons()
        {
            if (Interlocked.Exchange(ref _gateArmed, 0) == 1)
            {
                _entered.Set();
                if (!_release.Wait(TimeSpan.FromSeconds(20)))
                {
                    throw new TimeoutException("In-flight desktop read gate was never released.");
                }
            }

            return ((IDesktopIcons)_inner).ReadIcons();
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons) => ((IDesktopIcons)_inner).SetPositions(icons);

        public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove) =>
            ((IDesktopIcons)_inner).SetPositions(icons, beforeMove);

        public bool TryTurnAutoArrangeOff() => ((IDesktopIcons)_inner).TryTurnAutoArrangeOff();
    }

    private static int RunWindowHideReopenAndMenuExitScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(new MonitorInfo(0, 0, 1920, 1080, 100, true));
        var desktop = new Berth.Tests.Fakes.FakeDesktopIcons(new IconPosition("id-a", "Alpha", 100, 200));
        var store = new LayoutStore(paths);
        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        Berth.Window.MainWindow? capturedWindow = null;
        TrayMenu? capturedMenu = null;
        bool visibleAfterFirstOpen = false;
        bool visibleAfterClose = true;
        bool visibleAfterMenuReopen = false;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: true,
            Settings.Default,
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayMenuCreated: menu => capturedMenu = menu,
            createMainWindow: () =>
            {
                capturedWindow = new Berth.Window.MainWindow();
                return capturedWindow;
            },
            onAppStartup: app =>
            {
                if (capturedWindow is null || capturedMenu is null)
                {
                    app.Shutdown(2);
                    return;
                }

                visibleAfterFirstOpen = capturedWindow.IsVisible;
                capturedWindow.Close();
                visibleAfterClose = capturedWindow.IsVisible;

                capturedMenu.OpenItem.PerformClick();
                visibleAfterMenuReopen = capturedWindow.IsVisible;

                capturedMenu.ExitItem.PerformClick();
            });

        File.WriteAllLines(
            reportFile,
            [
                $"visibleAfterFirstOpen={visibleAfterFirstOpen}",
                $"visibleAfterClose={visibleAfterClose}",
                $"visibleAfterMenuReopen={visibleAfterMenuReopen}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunTrayAppWiringScenario(BerthPaths paths, Log log, string instanceName, bool openWindow)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(new MonitorInfo(0, 0, 1920, 1080, 100, true));
        var desktop = new Berth.Tests.Fakes.FakeDesktopIcons(new IconPosition("id-a", "Alpha", 100, 200));
        var store = new LayoutStore(paths);
        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        int uiThreadId = Environment.CurrentManagedThreadId;
        int createCount = 0;
        int bringToFrontCount = 0;
        int onWindowOpenedCount = 0;
        bool allOnUiThread = true;
        List<string> trace = [];
        TrayIcon? capturedIcon = null;
        TrayMenu? capturedMenu = null;
        int createsAtStartup = -1;
        int callbacksAtStartup = -1;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: openWindow,
            Settings.Default,
            onWindowOpened: () =>
            {
                onWindowOpenedCount++;
                trace.Add("onWindowOpened");
                if (Environment.CurrentManagedThreadId != uiThreadId)
                {
                    allOnUiThread = false;
                }
            },
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayIconCreated: icon => capturedIcon = icon,
            onTrayMenuCreated: menu => capturedMenu = menu,
            createMainWindow: () =>
            {
                createCount++;
                trace.Add("create");
                if (Environment.CurrentManagedThreadId != uiThreadId)
                {
                    allOnUiThread = false;
                }

                return new RecordingMainWindow(
                    () =>
                    {
                        bringToFrontCount++;
                        trace.Add("bringToFront");
                        if (Environment.CurrentManagedThreadId != uiThreadId)
                        {
                            allOnUiThread = false;
                        }
                    });
            },
            onAppStartup: app =>
            {
                createsAtStartup = createCount;
                callbacksAtStartup = onWindowOpenedCount;

                if (capturedIcon is null || capturedMenu is null)
                {
                    app.Shutdown(2);
                    return;
                }

                if (openWindow)
                {
                    // First run already created and showed the window; now exercise TrayIcon, TrayMenu, and worker pipe activation.
                    typeof(System.Windows.Forms.NotifyIcon)
                        .GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(capturedIcon.NotifyIcon, [new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 0, 0, 0)]);

                    capturedMenu.OpenItem.PerformClick();

                    _ = Task.Run(() =>
                    {
                        using var second = new SingleInstance(instanceName);
                        if (!second.TryClaim())
                        {
                            _ = second.RunSecondary();
                        }

                        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                        while (DateTime.UtcNow < deadline && Volatile.Read(ref bringToFrontCount) < 4)
                        {
                            Thread.Sleep(20);
                        }

                        app.Dispatcher.BeginInvoke(() => capturedMenu.ExitItem.PerformClick());
                    });
                }
                else
                {
                    // Tray-only: send worker-thread pipe activation FIRST so first creation + BringToFront both run via STA marshalling.
                    _ = Task.Run(() =>
                    {
                        using var second = new SingleInstance(instanceName);
                        if (!second.TryClaim())
                        {
                            _ = second.RunSecondary();
                        }

                        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                        while (DateTime.UtcNow < deadline && Volatile.Read(ref bringToFrontCount) < 1)
                        {
                            Thread.Sleep(20);
                        }

                        app.Dispatcher.BeginInvoke(() =>
                        {
                            typeof(System.Windows.Forms.NotifyIcon)
                                .GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                                .Invoke(capturedIcon.NotifyIcon, [new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 0, 0, 0)]);

                            capturedMenu.OpenItem.PerformClick();
                            capturedMenu.ExitItem.PerformClick();
                        });
                    });
                }
            });

        File.WriteAllLines(
            reportFile,
            [
                $"createsAtStartup={createsAtStartup}",
                $"callbacksAtStartup={callbacksAtStartup}",
                $"createCount={createCount}",
                $"bringToFrontCount={bringToFrontCount}",
                $"onWindowOpenedCount={onWindowOpenedCount}",
                $"allOnUiThread={allOnUiThread}",
                $"trace={string.Join(",", trace)}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunExitBlocksActivationDuringDrainScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(new MonitorInfo(0, 0, 1920, 1080, 100, true));
        using var desktop = new GatedDesktopIcons(new IconPosition("id-a", "Alpha", 100, 200));
        var store = new LayoutStore(paths);
        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        Berth.Window.MainWindow? capturedWindow = null;
        TrayIcon? capturedIcon = null;
        TrayMenu? capturedMenu = null;
        bool visibleAfterDrainActivation = false;
        bool secondClaimed = true;
        SecondaryOutcome secondOutcome = SecondaryOutcome.Refused;
        string workerError = string.Empty;
        using var activationHandled = new ManualResetEventSlim(false);

        int exitCode = TrayApp.RunWithInstance(
            openWindow: true,
            Settings.Default,
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayIconCreated: icon => capturedIcon = icon,
            onTrayMenuCreated: menu => capturedMenu = menu,
            createMainWindow: () =>
            {
                capturedWindow = new Berth.Window.MainWindow();
                return capturedWindow;
            },
            onInstanceActivatedHandled: () => activationHandled.Set(),
            onAppStartup: app =>
            {
                if (capturedWindow is null || capturedIcon is null || capturedMenu is null)
                {
                    app.Shutdown(2);
                    return;
                }

                // Hide the window, then start a gated Save so Exit enters BeginDrain.
                capturedWindow.Close();
                desktop.ArmGate();
                capturedMenu.SaveItem.PerformClick();

                _ = Task.Run(() =>
                {
                    try
                    {
                        desktop.WaitUntilEntered(TimeSpan.FromSeconds(5));

                        // Select Exit while Save is in flight, then trigger a tray click.
                        app.Dispatcher.Invoke(() =>
                        {
                            capturedMenu.ExitItem.PerformClick();
                            typeof(System.Windows.Forms.NotifyIcon)
                                .GetMethod("OnMouseClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                                .Invoke(capturedIcon.NotifyIcon, [new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, 0, 0, 0)]);
                        });

                        // Trigger a second-instance pipe activation and wait deterministically for the primary UI handler to finish.
                        using var second = new SingleInstance(instanceName);
                        secondClaimed = second.TryClaim();
                        if (!secondClaimed)
                        {
                            secondOutcome = second.RunSecondary();
                        }

                        if (!activationHandled.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Primary UI dispatcher did not complete the activation handler in time.");
                        }

                        app.Dispatcher.Invoke(() => visibleAfterDrainActivation = capturedWindow.IsVisible);
                    }
                    catch (Exception ex)
                    {
                        workerError = ex.ToString();
                    }
                    finally
                    {
                        desktop.ReleaseGate();
                    }
                });
            });

        File.WriteAllLines(
            reportFile,
            [
                $"visibleAfterDrainActivation={visibleAfterDrainActivation}",
                $"secondClaimed={secondClaimed}",
                $"secondOutcome={secondOutcome}",
                $"workerError={workerError}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunProductionTrayPlacementScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        var monitors = new Berth.Desktop.SystemMonitors();
        var desktop = new Berth.Tests.Fakes.FakeDesktopIcons(new IconPosition("id-a", "Alpha", 100, 200));
        var store = new LayoutStore(paths);
        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        TrayMenu? capturedMenu = null;
        string targetRectLine = string.Empty;
        string savedRectLine = string.Empty;
        string observedRectLine = string.Empty;
        string reopenedFreshRectLine = string.Empty;
        bool setSucceeded = false;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: true,
            Settings.Default,
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayMenuCreated: menu => capturedMenu = menu,
            createMainWindow: null, // Explicitly exercise the production TrayApp window factory!
            onAppStartup: app =>
            {
                try
                {
                    Berth.Window.MainWindow? win = app.Windows.OfType<Berth.Window.MainWindow>().FirstOrDefault();
                    if (win is null || capturedMenu is null)
                    {
                        app.Shutdown(2);
                        return;
                    }

                    Berth.Window.NativeWindowBoundsInterop.Instance.TryGetPrimaryWorkArea(out WindowPlacement primaryWork, out double scale);
                    var moved = new WindowPlacement(
                        primaryWork.X + 110,
                        primaryWork.Y + 95,
                        (int)Math.Round(1020 * scale),
                        (int)Math.Round(680 * scale));
                    targetRectLine = $"{moved.X},{moved.Y},{moved.Width},{moved.Height}";

                    IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                    setSucceeded = Berth.Window.NativeWindowBoundsInterop.Instance.TrySetWindowRect(hwnd, moved);
                    if (Berth.Window.NativeWindowBoundsInterop.Instance.TryGetWindowRect(hwnd, out WindowPlacement observed))
                    {
                        observedRectLine = $"{observed.X},{observed.Y},{observed.Width},{observed.Height}";
                    }

                    // Update RestoreAutomatically on disk AFTER window opened to verify hide preserves live settings.
                    var freshStore = new SettingsStore(paths);
                    _ = freshStore.Update(s => s with { RestoreAutomatically = true });

                    // Hide the window (saves placement).
                    win.Close();
                    SettingsLoad afterHide = freshStore.Load();
                    if (afterHide.Value.Window is { } saved)
                    {
                        savedRectLine = $"{saved.X},{saved.Y},{saved.Width},{saved.Height},{afterHide.Value.RestoreAutomatically}";
                    }

                    // Open a fresh window against the same data root to prove restoration from disk.
                    var reopened = new Berth.Window.MainWindow(freshStore, monitors, log);
                    try
                    {
                        reopened.Show();
                        IntPtr reopenedHwnd = new System.Windows.Interop.WindowInteropHelper(reopened).Handle;
                        if (Berth.Window.NativeWindowBoundsInterop.Instance.TryGetWindowRect(reopenedHwnd, out WindowPlacement restored))
                        {
                            reopenedFreshRectLine = $"{restored.X},{restored.Y},{restored.Width},{restored.Height}";
                        }
                    }
                    finally
                    {
                        reopened.CloseForShutdown();
                    }

                    capturedMenu.ExitItem.PerformClick();
                }
                catch
                {
                    app.Shutdown(3);
                }
            });

        File.WriteAllLines(
            reportFile,
            [
                $"setSucceeded={setSucceeded}",
                $"target={targetRectLine}",
                $"observed={observedRectLine}",
                $"saved={savedRectLine}",
                $"reopenedFresh={reopenedFreshRectLine}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunSettingsFailureHideReopenScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(new MonitorInfo(0, 0, 1920, 1080, 100, true));
        var desktop = new Berth.Tests.Fakes.FakeDesktopIcons(new IconPosition("id-a", "Alpha", 100, 200));
        var store = new LayoutStore(paths);
        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        TrayMenu? capturedMenu = null;
        string hideWarnLines = string.Empty;
        bool visibleAfterHide = true;
        bool visibleAfterTrayReopen = false;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: true,
            Settings.Default,
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayMenuCreated: menu => capturedMenu = menu,
            createMainWindow: null,
            onAppStartup: app =>
            {
                try
                {
                    Berth.Window.MainWindow? win = app.Windows.OfType<Berth.Window.MainWindow>().FirstOrDefault();
                    if (win is null || capturedMenu is null)
                    {
                        app.Shutdown(2);
                        return;
                    }

                    // Snapshot log line count IMMEDIATELY before hide (after window is open) and capture new lines right after Close().
                    int linesBeforeHide = File.Exists(log.FilePath) ? File.ReadAllLines(log.FilePath).Length : 0;

                    win.Close();
                    visibleAfterHide = win.IsVisible;

                    string[] afterHideLines = File.Exists(log.FilePath)
                        ? File.ReadAllLines(log.FilePath).Skip(linesBeforeHide).ToArray()
                        : [];
                    hideWarnLines = string.Join(" || ", afterHideLines);

                    // Reopen through the running tray menu to prove the tray app survived the hide-save failure.
                    capturedMenu.OpenItem.PerformClick();
                    visibleAfterTrayReopen = win.IsVisible;

                    capturedMenu.ExitItem.PerformClick();
                }
                catch
                {
                    app.Shutdown(3);
                }
            });

        File.WriteAllLines(
            reportFile,
            [
                $"hideWarnLines={hideWarnLines}",
                $"visibleAfterHide={visibleAfterHide}",
                $"visibleAfterTrayReopen={visibleAfterTrayReopen}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunRenderMainWindowFrameScenario()
    {
        string outPng = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var app = new BerthApplication();
        app.InitializeTheme();
        app.Startup += (_, _) =>
        {
            var window = new Berth.Window.MainWindow
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
                ShowActivated = false,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                int width = Math.Max(1, (int)Math.Round(window.RootGrid.ActualWidth));
                int height = Math.Max(1, (int)Math.Round(window.RootGrid.ActualHeight));
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width,
                    height,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(window.RootGrid);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using FileStream fs = File.Create(outPng);
                encoder.Save(fs);
            }
            finally
            {
                window.AllowClose = true;
                window.Close();
                app.Shutdown(0);
            }
        };
        return app.Run();
    }

    private static int RunSystemEventsTrayWiringScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        MonitorInfo laptopMon = new(0, 0, 1920, 1080, 100, true);
        MonitorInfo midStartupMon = new(0, 0, 2048, 1152, 100, true);
        MonitorInfo dockedMon = new(0, 0, 2560, 1440, 100, true);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(laptopMon);
        var desktop = new Berth.Tests.Fakes.FakeDesktopIcons(new IconPosition("id-a", "Alpha", 100, 200));
        var store = new LayoutStore(paths);
        string laptopKey = ArrangementKey.From([laptopMon]);
        string midKey = ArrangementKey.From([midStartupMon]);
        string dockedKey = ArrangementKey.From([dockedMon]);
        store.Save(new Layout(Layout.CurrentSchemaVersion, laptopKey, "Laptop", fixedNow, [laptopMon], [new IconPosition("id-a", "Alpha", 100, 200)], null));
        store.Save(new Layout(Layout.CurrentSchemaVersion, midKey, "MidStartup", fixedNow, [midStartupMon], [new IconPosition("id-a", "Alpha", 200, 300)], null));
        store.Save(new Layout(Layout.CurrentSchemaVersion, dockedKey, "Docked", fixedNow, [dockedMon], [new IconPosition("id-a", "Alpha", 300, 400)], null));

        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);

        // Start in dark app + dark taskbar theme, then flip both to light AFTER startup reconciliation.
        bool fakeAppsLight = false;
        int fakeTaskbarLight = 0;
        var theme = new Berth.Desktop.TaskbarTheme(
            @"Software\Berth.Tests\SystemEventsChild",
            () => Volatile.Read(ref fakeTaskbarLight));

        TrayIcon? capturedIcon = null;
        TrayMenu? capturedMenu = null;
        SystemEvents? capturedEvents = null;
        nint savedHandle = 0;
        string initialTooltip = string.Empty;
        string tooltipAfterStartupReconcile = string.Empty;
        string tooltipAfterDisplayChange = string.Empty;
        string tooltipAfterStaleMenuCompletion = string.Empty;
        ColorTheme initialAppTheme = ColorTheme.Light;
        ColorTheme appThemeAfterSettingChange = ColorTheme.Dark;
        bool hasLightDictAfterSettingChange = false;
        IntPtr initialIconHandle = IntPtr.Zero;
        bool iconHandleChanged = false;
        bool taskbarIsLightAfterSettingChange = false;
        bool sendSettingResult = false;
        bool menuBarrierTimedOut = false;
        string workerError = string.Empty;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: false,
            Settings.Default,
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            createApplication: () => new BerthApplication(
                readHighContrast: () => false,
                readAppsUseLightTheme: () => Volatile.Read(ref fakeAppsLight)),
            onTrayIconCreated: icon =>
            {
                capturedIcon = icon;
                initialTooltip = icon.TooltipText;
                initialIconHandle = icon.IconHandle;

                // Simulate a display change happening AFTER initial tooltip read and BEFORE SystemEvents subscribes.
                monitors.Monitors = [midStartupMon];
            },
            onTrayMenuCreated: menu => capturedMenu = menu,
            onSystemEventsCreated: ev =>
            {
                capturedEvents = ev;
                savedHandle = ev.Handle;
            },
            onAppStartup: app =>
            {
                if (capturedIcon is null || capturedMenu is null || capturedEvents is null)
                {
                    app.Shutdown(2);
                    return;
                }

                initialAppTheme = app.CurrentColorTheme;

                _ = Task.Run(() =>
                {
                    using var menuSnapshotTaken = new ManualResetEventSlim(false);
                    using var releaseMenuCompletion = new ManualResetEventSlim(false);
                    using var menuFinished = new ManualResetEventSlim(false);
                    try
                    {
                        // 1. Wait for startup reconciliation to pick up MidStartup (switched during onTrayIconCreated).
                        var startupDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                        while (DateTime.UtcNow < startupDeadline)
                        {
                            string tip = app.Dispatcher.Invoke(() => capturedIcon.TooltipText);
                            if (tip.Contains("MidStartup", StringComparison.Ordinal))
                            {
                                tooltipAfterStartupReconcile = tip;
                                break;
                            }

                            Thread.Sleep(25);
                        }

                        // 2. Start a menu Save on MidStartup, hold its worker AFTER FindActiveLayout snapshots MidStartup,
                        //    deliver WM_DISPLAYCHANGE for Docked, then release the stale menu completion and verify Docked wins!
                        capturedMenu.BeforeOperationCompletionMarshal = () =>
                        {
                            menuSnapshotTaken.Set();
                            if (!releaseMenuCompletion.Wait(TimeSpan.FromSeconds(5)))
                            {
                                Volatile.Write(ref menuBarrierTimedOut, true);
                            }
                        };
                        capturedMenu.OperationFinished += _ => menuFinished.Set();
                        app.Dispatcher.Invoke(() => capturedMenu.SaveItem.PerformClick());

                        if (!menuSnapshotTaken.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Menu worker did not reach snapshot barrier.");
                        }

                        monitors.Monitors = [dockedMon];
                        _ = SystemEventsTests.PostMessageW(savedHandle, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);

                        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                        while (DateTime.UtcNow < deadline)
                        {
                            string currentTip = app.Dispatcher.Invoke(() => capturedIcon.TooltipText);
                            if (currentTip.Contains("Docked", StringComparison.Ordinal))
                            {
                                tooltipAfterDisplayChange = currentTip;
                                break;
                            }

                            Thread.Sleep(25);
                        }

                        releaseMenuCompletion.Set();
                        if (!menuFinished.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Menu completion did not settle on UI thread.");
                        }

                        tooltipAfterStaleMenuCompletion = app.Dispatcher.Invoke(() => capturedIcon.TooltipText);

                        // 3. Flip fake app + taskbar theme to Light and send WM_SETTINGCHANGE("ImmersiveColorSet")
                        //    synchronously from this worker thread via SendMessageTimeoutW (2-second bound).
                        Volatile.Write(ref fakeAppsLight, true);
                        Volatile.Write(ref fakeTaskbarLight, 1);
                        sendSettingResult = SystemEventsTests.SendSettingChange(savedHandle, "ImmersiveColorSet", 2000);

                        app.Dispatcher.Invoke(() =>
                        {
                            appThemeAfterSettingChange = app.CurrentColorTheme;
                            hasLightDictAfterSettingChange = ThemeResources.HasColorDictionary(app.Resources, ColorTheme.Light);
                            iconHandleChanged = capturedIcon.IconHandle != initialIconHandle;
                            taskbarIsLightAfterSettingChange = theme.IsLight;
                            app.Shutdown(0);
                        });
                    }
                    catch (Exception ex)
                    {
                        releaseMenuCompletion.Set();
                        workerError = ex.ToString();
                        app.Dispatcher.BeginInvoke(() => app.Shutdown(3));
                    }
                });
            });

        bool isWindowAfterShutdown = savedHandle != 0 && SystemEventsTests.IsWindow(savedHandle);
        File.WriteAllLines(
            reportFile,
            [
                $"savedHandleNonZero={savedHandle != 0}",
                $"initialTooltip={initialTooltip}",
                $"tooltipAfterStartupReconcile={tooltipAfterStartupReconcile}",
                $"tooltipAfterDisplayChange={tooltipAfterDisplayChange}",
                $"tooltipAfterStaleMenuCompletion={tooltipAfterStaleMenuCompletion}",
                $"initialAppTheme={initialAppTheme}",
                $"sendSettingResult={sendSettingResult}",
                $"appThemeAfterSettingChange={appThemeAfterSettingChange}",
                $"hasLightDictAfterSettingChange={hasLightDictAfterSettingChange}",
                $"iconHandleChanged={iconHandleChanged}",
                $"taskbarIsLightAfterSettingChange={taskbarIsLightAfterSettingChange}",
                $"isWindowAfterShutdown={isWindowAfterShutdown}",
                $"menuBarrierTimedOut={Volatile.Read(ref menuBarrierTimedOut)}",
                $"workerError={workerError}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunAutoRestoreTrayScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        MonitorInfo laptopMon = new(0, 0, 1920, 1080, 100, true);
        MonitorInfo dockedMon = new(0, 0, 2560, 1440, 100, true);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(laptopMon);
        using var desktop = new GatedDesktopIcons(new IconPosition("id-a", "Alpha", 900, 900));
        var store = new LayoutStore(paths);
        string laptopKey = ArrangementKey.From([laptopMon]);
        string dockedKey = ArrangementKey.From([dockedMon]);
        store.Save(new Layout(Layout.CurrentSchemaVersion, laptopKey, "Laptop", fixedNow, [laptopMon], [new IconPosition("id-a", "Alpha", 111, 222)], null));
        store.Save(new Layout(Layout.CurrentSchemaVersion, dockedKey, "Docked", fixedNow, [dockedMon], [new IconPosition("id-a", "Alpha", 333, 444)], null));

        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(Settings.Default with { RestoreAutomatically = true });

        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        SystemEvents? capturedEvents = null;
        AutoRestore? capturedAuto = null;
        TrayMenu? capturedMenu = null;
        List<string> finishedReasons = [];
        using var firstDone = new ManualResetEventSlim(false);
        using var secondDone = new ManualResetEventSlim(false);
        using var thirdDone = new ManualResetEventSlim(false);
        using var displayArmed = new ManualResetEventSlim(false);
        using var explorerArmed1 = new ManualResetEventSlim(false);
        using var explorerArmed2 = new ManualResetEventSlim(false);
        int explorerArmedCount = 0;
        int displayArmedMs = -1;
        int explorerArmedMs = -1;
        string posAfterDisplay = string.Empty;
        string posAfterExplorer = string.Empty;
        int finishedCountAfterOff = -1;
        int finishedCountAtExit = -1;
        bool exitedBeforeGateRelease = false;
        string workerError = string.Empty;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: false,
            Settings.Default with { RestoreAutomatically = true },
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayMenuCreated: menu => capturedMenu = menu,
            onSystemEventsCreated: ev => capturedEvents = ev,
            displayChangeSettle: TimeSpan.FromMilliseconds(60),
            explorerRestartSettle: TimeSpan.FromMilliseconds(120),
            onAutoRestoreCreated: auto =>
            {
                capturedAuto = auto;
                auto.TimerArmed += (reason, due) =>
                {
                    if (reason == "display change" && displayArmedMs < 0)
                    {
                        displayArmedMs = (int)Math.Round(due.TotalMilliseconds);
                        displayArmed.Set();
                    }
                    else if (reason == "Explorer restart")
                    {
                        int n = Interlocked.Increment(ref explorerArmedCount);
                        if (n == 1)
                        {
                            explorerArmedMs = (int)Math.Round(due.TotalMilliseconds);
                            explorerArmed1.Set();
                        }
                        else if (n == 2)
                        {
                            explorerArmed2.Set();
                        }
                    }
                };
                auto.Finished += (_, reason, _) =>
                {
                    int count;
                    lock (finishedReasons)
                    {
                        finishedReasons.Add(reason);
                        count = finishedReasons.Count;
                    }

                    if (count == 1)
                    {
                        firstDone.Set();
                    }
                    else if (count == 2)
                    {
                        secondDone.Set();
                    }
                    else if (count == 3)
                    {
                        thirdDone.Set();
                    }
                };
            },
            onAppStartup: app =>
            {
                if (capturedEvents is null || capturedAuto is null || capturedMenu is null)
                {
                    app.Shutdown(2);
                    return;
                }

                nint hwnd = capturedEvents.Handle;
                uint taskbarCreated = capturedEvents.TaskbarCreatedMessage;
                _ = Task.Run(() =>
                {
                    try
                    {
                        // 1. Post WM_DISPLAYCHANGE -> verify 60ms interval armed -> restores Laptop (111,222).
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        if (!displayArmed.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Display-change timer was not armed.");
                        }

                        if (!firstDone.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Display-change auto-restore did not finish.");
                        }

                        IconPosition p1 = ((IDesktopIcons)desktop).ReadIcons().Single();
                        posAfterDisplay = $"{p1.X},{p1.Y}";

                        // 2. Post TaskbarCreated, wait until 120ms timer IS ARMED, THEN switch fake monitors to Docked BEFORE expiry -> restores Docked (333,444)!
                        _ = SystemEventsTests.PostMessageW(hwnd, taskbarCreated, IntPtr.Zero, IntPtr.Zero);
                        if (!explorerArmed1.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Explorer-restart timer 1 was not armed.");
                        }

                        monitors.Monitors = [dockedMon];
                        if (!secondDone.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Explorer-restart auto-restore did not finish.");
                        }

                        IconPosition p2 = ((IDesktopIcons)desktop).ReadIcons().Single();
                        posAfterExplorer = $"{p2.X},{p2.Y}";

                        // 3. Post TaskbarCreated, wait until timer IS ARMED, THEN flip RestoreAutomatically to FALSE before expiry -> zero work!
                        _ = SystemEventsTests.PostMessageW(hwnd, taskbarCreated, IntPtr.Zero, IntPtr.Zero);
                        if (!explorerArmed2.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Explorer-restart timer 2 was not armed.");
                        }

                        settingsStore.Save(Settings.Default with { RestoreAutomatically = false });
                        Thread.Sleep(250);
                        lock (finishedReasons)
                        {
                            finishedCountAfterOff = finishedReasons.Count;
                        }

                        // 4. Re-enable RestoreAutomatically, gate desktop read, trigger restore + mid-flight retrigger,
                        //    click Exit -> verify shutdown waits for gated restore and suppresses the pending retrigger!
                        settingsStore.Save(Settings.Default with { RestoreAutomatically = true });
                        desktop.ArmGate();
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        desktop.WaitUntilEntered(TimeSpan.FromSeconds(5));

                        // Queue a retrigger while gated restore is running, then click Exit.
                        _ = SystemEventsTests.PostMessageW(hwnd, taskbarCreated, IntPtr.Zero, IntPtr.Zero);
                        Thread.Sleep(40);
                        app.Dispatcher.Invoke(() =>
                        {
                            capturedMenu.ExitItem.PerformClick();
                            exitedBeforeGateRelease = app.Dispatcher.HasShutdownStarted;
                        });
                        Thread.Sleep(50);
                        desktop.ReleaseGate();
                    }
                    catch (Exception ex)
                    {
                        desktop.ReleaseGate();
                        workerError = ex.ToString();
                        app.Dispatcher.BeginInvoke(() => app.Shutdown(3));
                    }
                });
            });

        lock (finishedReasons)
        {
            finishedCountAtExit = finishedReasons.Count;
        }

        string logContents = File.Exists(paths.LogFile) ? File.ReadAllText(paths.LogFile) : string.Empty;
        File.WriteAllLines(
            reportFile,
            [
                $"displayArmedMs={displayArmedMs}",
                $"explorerArmedMs={explorerArmedMs}",
                $"posAfterDisplay={posAfterDisplay}",
                $"posAfterExplorer={posAfterExplorer}",
                $"finishedCountAfterOff={finishedCountAfterOff}",
                $"finishedCountAtExit={finishedCountAtExit}",
                $"exitedBeforeGateRelease={exitedBeforeGateRelease}",
                $"hasDisplayLog={logContents.Contains("(automatic, display change)", StringComparison.Ordinal)}",
                $"hasExplorerLog={logContents.Contains("(automatic, Explorer restart)", StringComparison.Ordinal)}",
                $"workerError={workerError}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunAutoRestoreDirectShutdownScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        MonitorInfo laptopMon = new(0, 0, 1920, 1080, 100, true);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(laptopMon);
        using var desktop = new GatedDesktopIcons(new IconPosition("id-a", "Alpha", 900, 900));
        var store = new LayoutStore(paths);
        string laptopKey = ArrangementKey.From([laptopMon]);
        store.Save(new Layout(Layout.CurrentSchemaVersion, laptopKey, "Laptop", fixedNow, [laptopMon], [new IconPosition("id-a", "Alpha", 111, 222)], null));

        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(Settings.Default with { RestoreAutomatically = true });

        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        SystemEvents? capturedEvents = null;
        bool restoreCompletedBeforeDispose = false;
        bool compositionDisposed = false;
        bool desktopReadAfterDispose = false;
        string workerError = string.Empty;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: false,
            Settings.Default with { RestoreAutomatically = true },
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            createOwnedComposition: () =>
            {
                var trackingDesktop = new DisposeTrackingDesktopIcons(
                    desktop,
                    onDispose: () =>
                    {
                        compositionDisposed = true;
                        IconPosition current = ((IDesktopIcons)desktop).ReadIcons().Single();
                        restoreCompletedBeforeDispose = (current.X, current.Y) == (111, 222);
                    },
                    onUsedAfterDispose: () => desktopReadAfterDispose = true);
                var ownedService = new LayoutService(trackingDesktop, monitors, store, paths, gate, log, clock);
                return Composition.ForTest(paths, log, monitors, trackingDesktop, store, gate, ownedService, ownedDesktop: trackingDesktop);
            },
            onSystemEventsCreated: ev => capturedEvents = ev,
            displayChangeSettle: TimeSpan.FromMilliseconds(40),
            explorerRestartSettle: TimeSpan.FromMilliseconds(40),
            onAppStartup: app =>
            {
                if (capturedEvents is null)
                {
                    app.Shutdown(2);
                    return;
                }

                nint hwnd = capturedEvents.Handle;
                var appExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                app.Exit += (_, _) => appExited.TrySetResult();
                _ = Task.Run(() =>
                {
                    try
                    {
                        desktop.ArmGate();
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        desktop.WaitUntilEntered(TimeSpan.FromSeconds(5));

                        // Call app.Shutdown(0) directly while gated, synchronize with app.Exit, then hold the gate for
                        // 10.5s (strictly past both historical 5s waits = 10s) so only an unconditional drain keeps the
                        // owned Composition alive until the restore completes.
                        _ = app.Dispatcher.BeginInvoke(() => app.Shutdown(0));
                        if (!appExited.Task.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Application.Exit did not fire in time.");
                        }

                        Thread.Sleep(TimeSpan.FromMilliseconds(10500));
                        desktop.ReleaseGate();
                    }
                    catch (Exception ex)
                    {
                        desktop.ReleaseGate();
                        workerError = ex.ToString();
                        app.Dispatcher.BeginInvoke(() => app.Shutdown(3));
                    }
                });
            });

        IconPosition posAfterReturn = ((IDesktopIcons)desktop).ReadIcons().Single();
        File.WriteAllLines(
            reportFile,
            [
                $"compositionDisposed={compositionDisposed}",
                $"restoreCompletedBeforeDispose={restoreCompletedBeforeDispose}",
                $"desktopReadAfterDispose={desktopReadAfterDispose}",
                $"posAfterReturn={posAfterReturn.X},{posAfterReturn.Y}",
                $"workerError={workerError}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunAutoRestoreBalloonScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        MonitorInfo laptopMon = new(0, 0, 1920, 1080, 100, true);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(laptopMon);
        var desktop = new Berth.Tests.Fakes.FakeDesktopIcons(
            new IconPosition("id-a", "Alpha", 900, 900),
            new IconPosition("id-b", "Beta", 950, 950));
        desktop.SetAutoArrange(true);

        var store = new LayoutStore(paths);
        string laptopKey = ArrangementKey.From([laptopMon]);
        store.Save(new Layout(
            Layout.CurrentSchemaVersion,
            laptopKey,
            "Laptop",
            fixedNow,
            [laptopMon],
            [
                new IconPosition("id-a", "Alpha", 111, 222),
                new IconPosition("id-b", "Beta", 333, 444),
            ],
            null));

        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(Settings.Default with { RestoreAutomatically = true });

        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        int uiThreadId = Environment.CurrentManagedThreadId;
        var balloons = new List<(string Title, string Text, System.Windows.Forms.ToolTipIcon Icon, bool OnUiThread)>();
        TrayIcon? capturedIcon = null;
        SystemEvents? capturedEvents = null;
        int bringToFrontCount = 0;
        int finishedCount = 0;
        using var step1Done = new ManualResetEventSlim(false);
        using var step2Done = new ManualResetEventSlim(false);
        using var step3Done = new ManualResetEventSlim(false);
        int balloonsAfterRefusal = -1;
        int balloonsAfterPartial = -1;
        int balloonsAfterSuccess = -1;
        string refusalTitle = string.Empty;
        string refusalText = string.Empty;
        string partialText = string.Empty;
        bool allOnUiThread = true;
        bool allWarningIcon = true;
        string workerError = string.Empty;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: false,
            Settings.Default with { RestoreAutomatically = true },
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayIconCreated: icon => capturedIcon = icon,
            onSystemEventsCreated: ev => capturedEvents = ev,
            displayChangeSettle: TimeSpan.FromMilliseconds(40),
            explorerRestartSettle: TimeSpan.FromMilliseconds(40),
            createMainWindow: () => new RecordingMainWindow(() => Interlocked.Increment(ref bringToFrontCount)),
            showBalloon: (title, text, icon) =>
            {
                bool onUi = Environment.CurrentManagedThreadId == uiThreadId;
                if (!onUi)
                {
                    allOnUiThread = false;
                }

                if (icon != System.Windows.Forms.ToolTipIcon.Warning)
                {
                    allWarningIcon = false;
                }

                lock (balloons)
                {
                    balloons.Add((title, text, icon, onUi));
                }
            },
            onAutoRestoreCreated: auto =>
            {
                auto.Finished += (_, _, _) =>
                {
                    int n = Interlocked.Increment(ref finishedCount);
                    if (n == 1)
                    {
                        step1Done.Set();
                    }
                    else if (n == 2)
                    {
                        step2Done.Set();
                    }
                    else if (n == 3)
                    {
                        step3Done.Set();
                    }
                };
            },
            onAppStartup: app =>
            {
                if (capturedIcon is null || capturedEvents is null)
                {
                    app.Shutdown(2);
                    return;
                }

                nint hwnd = capturedEvents.Handle;
                _ = Task.Run(() =>
                {
                    try
                    {
                        // 1. AutoArrange ON -> Refused -> 1 warning balloon + 1 WARN log line.
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        if (!step1Done.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("AutoArrange refusal restore did not finish.");
                        }

                        // Drain queued BeginInvoke(Normal) balloon work on UI dispatcher before reading recorder.
                        app.Dispatcher.Invoke(
                            () =>
                            {
                                lock (balloons)
                                {
                                    balloonsAfterRefusal = balloons.Count;
                                    if (balloons.Count >= 1)
                                    {
                                        refusalTitle = balloons[0].Title;
                                        refusalText = balloons[0].Text;
                                    }
                                }

                                // Raise the real NotifyIcon.BalloonTipClicked event -> must open/bring window to front!
                                typeof(System.Windows.Forms.NotifyIcon)
                                    .GetMethod("OnBalloonTipClicked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                                    .Invoke(capturedIcon.NotifyIcon, null);
                            },
                            DispatcherPriority.Background);

                        // 2. Partial restore (NotPlaced: 1 icon shifts off its saved spot) -> 1 warning balloon + 1 FAIL log line!
                        desktop.SetAutoArrange(false);
                        desktop.SnapBy = 10;
                        desktop.SnapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id-b" };
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        if (!step2Done.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Partial restore did not finish.");
                        }

                        app.Dispatcher.Invoke(
                            () =>
                            {
                                lock (balloons)
                                {
                                    balloonsAfterPartial = balloons.Count;
                                    if (balloons.Count >= 2)
                                    {
                                        partialText = balloons[1].Text;
                                    }
                                }
                            },
                            DispatcherPriority.Background);

                        // 3. Successful restore -> 0 new balloons + 1 INFO log line!
                        desktop.SnapBy = 0;
                        desktop.SnapIds = null;
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        if (!step3Done.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Successful restore did not finish.");
                        }

                        app.Dispatcher.Invoke(
                            () =>
                            {
                                lock (balloons)
                                {
                                    balloonsAfterSuccess = balloons.Count;
                                }

                                app.Shutdown(0);
                            },
                            DispatcherPriority.Background);
                    }
                    catch (Exception ex)
                    {
                        workerError = ex.ToString();
                        app.Dispatcher.BeginInvoke(() => app.Shutdown(3));
                    }
                });
            });

        string[] logLines = File.Exists(paths.LogFile) ? File.ReadAllLines(paths.LogFile) : [];
        File.WriteAllLines(
            reportFile,
            [
                $"balloonsAfterRefusal={balloonsAfterRefusal}",
                $"balloonsAfterPartial={balloonsAfterPartial}",
                $"balloonsAfterSuccess={balloonsAfterSuccess}",
                $"refusalTitle={refusalTitle}",
                $"refusalText={refusalText}",
                $"partialText={partialText}",
                $"allOnUiThread={allOnUiThread}",
                $"allWarningIcon={allWarningIcon}",
                $"bringToFrontCount={bringToFrontCount}",
                $"logLineCount={logLines.Length}",
                $"logLine0={(logLines.Length > 0 ? logLines[0] : string.Empty)}",
                $"logLine1={(logLines.Length > 1 ? logLines[1] : string.Empty)}",
                $"logLine2={(logLines.Length > 2 ? logLines[2] : string.Empty)}",
                $"workerError={workerError}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunTrayReaddOnExplorerRestartScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        MonitorInfo laptopMon = new(0, 0, 1920, 1080, 100, true);
        var monitors = new Berth.Tests.Fakes.FakeMonitors(laptopMon);
        var desktop = new Berth.Tests.Fakes.FakeDesktopIcons(new IconPosition("id-a", "Alpha", 100, 200));
        var store = new LayoutStore(paths);
        string laptopKey = ArrangementKey.From([laptopMon]);
        store.Save(new Layout(Layout.CurrentSchemaVersion, laptopKey, "Laptop", fixedNow, [laptopMon], [new IconPosition("id-a", "Alpha", 100, 200)], null));

        // Run with RestoreAutomatically OFF to verify SPEC §12 unconditional tray icon recovery on Explorer restart!
        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(Settings.Default with { RestoreAutomatically = false });

        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme", () => 0);

        TrayIcon? capturedIcon = null;
        SystemEvents? capturedEvents = null;
        int readdCount = 0;
        int readdCountAtTimerArmed = -1;
        string initialTooltip = string.Empty;
        string tooltipAfterReadd = string.Empty;
        bool visibleAfterReadd = false;
        using var explorerTimerArmed = new ManualResetEventSlim(false);
        string workerError = string.Empty;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: false,
            Settings.Default with { RestoreAutomatically = false },
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayIconCreated: icon =>
            {
                capturedIcon = icon;
                initialTooltip = icon.TooltipText;
            },
            onSystemEventsCreated: ev => capturedEvents = ev,
            displayChangeSettle: TimeSpan.FromMilliseconds(80),
            explorerRestartSettle: TimeSpan.FromMilliseconds(80),
            onTrayReadd: () => Interlocked.Increment(ref readdCount),
            onAutoRestoreCreated: auto =>
            {
                auto.TimerArmed += (reason, _) =>
                {
                    if (reason == "Explorer restart")
                    {
                        readdCountAtTimerArmed = Volatile.Read(ref readdCount);
                        explorerTimerArmed.Set();
                    }
                };
            },
            onAppStartup: app =>
            {
                if (capturedIcon is null || capturedEvents is null)
                {
                    app.Shutdown(2);
                    return;
                }

                nint hwnd = capturedEvents.Handle;
                uint taskbarCreated = capturedEvents.TaskbarCreatedMessage;
                _ = Task.Run(() =>
                {
                    try
                    {
                        _ = SystemEventsTests.PostMessageW(hwnd, taskbarCreated, IntPtr.Zero, IntPtr.Zero);
                        if (!explorerTimerArmed.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Explorer restart did not arm AutoRestore timer.");
                        }

                        app.Dispatcher.Invoke(() =>
                        {
                            visibleAfterReadd = capturedIcon.IsVisible;
                            tooltipAfterReadd = capturedIcon.TooltipText;
                            app.Shutdown(0);
                        });
                    }
                    catch (Exception ex)
                    {
                        workerError = ex.ToString();
                        app.Dispatcher.BeginInvoke(() => app.Shutdown(3));
                    }
                });
            });

        File.WriteAllLines(
            reportFile,
            [
                $"readdCount={Volatile.Read(ref readdCount)}",
                $"readdCountAtTimerArmed={readdCountAtTimerArmed}",
                $"initialTooltip={initialTooltip}",
                $"tooltipAfterReadd={tooltipAfterReadd}",
                $"visibleAfterReadd={visibleAfterReadd}",
                $"workerError={workerError}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunAutoRestoreWindowScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        var monA = new MonitorInfo(0, 0, 1920, 1080, 100, true);
        var monB1 = new MonitorInfo(0, 0, 2560, 1440, 100, true);
        var monB2 = new MonitorInfo(2560, 0, 1920, 1080, 100, false);
        var monC = new MonitorInfo(0, 0, 1600, 900, 100, true);

        IconPosition[] savedA =
        [
            new("a-1", "Alpha", 100, 100),
            new("a-2", "Beta", 200, 100),
        ];
        IconPosition[] savedB =
        [
            new("b-1", "One", 120, 120),
            new("b-2", "Two", 240, 120),
            new("b-3", "Three", 360, 120),
        ];
        IconPosition[] movedB =
        [
            new("b-1", "One", 900, 120),
            new("b-2", "Two", 900, 240),
            new("b-3", "Three", 900, 360),
        ];

        string keyA = ArrangementKey.From([monA]);
        string keyB = ArrangementKey.From([monB1, monB2]);
        string keyC = ArrangementKey.From([monC]);

        var store = new LayoutStore(paths);
        store.Save(new Layout(Layout.CurrentSchemaVersion, keyA, "Laptop", fixedNow, [monA], [.. savedA], null));
        store.Save(new Layout(Layout.CurrentSchemaVersion, keyB, "Docked", fixedNow, [monB1, monB2], [.. savedB], null));
        var settingsStore = new SettingsStore(paths);
        _ = settingsStore.Update(s => s with { RestoreAutomatically = true });

        var monitors = new Berth.Tests.Fakes.FakeMonitors(monA);
        using var desktop = new SwappableGatedDesktopIcons(savedA);
        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        SystemEvents? capturedEvents = null;
        AutoRestore? capturedAutoRestore = null;
        TrayIcon? capturedIcon = null;
        int balloonCount = 0;

        bool idleDuringSettle = false;
        string workingSentence = string.Empty;
        bool restoreDisabledWhileWorking = false;
        bool saveDisabledWhileWorking = false;
        string restoredSentence = string.Empty;
        bool undoVisible = false;
        string selectedAfterRestore = string.Empty;
        int inPlaceAfterRestore = -1;
        int movedAfterRestore = -1;
        string sentenceAfterUnchangedRefresh = string.Empty;
        bool undoStillVisibleAfterUnchangedRefresh = false;
        string sentenceAfterUndo = string.Empty;
        string undoPositions = string.Empty;
        string tooltipAfterB = string.Empty;
        string tooltipAfterRenameWithoutDisplayChange = string.Empty;
        string tooltipAfterUnsavedC = string.Empty;
        bool cLayoutCreated = false;
        bool silentForC = false;
        string hiddenReopenSentence = string.Empty;
        bool hiddenReopenUndoVisible = false;
        bool hiddenDesktopRestored = false;
        string workerError = string.Empty;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: true,
            Settings.Default with { RestoreAutomatically = true },
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayIconCreated: icon => capturedIcon = icon,
            onSystemEventsCreated: events => capturedEvents = events,
            displayChangeSettle: TimeSpan.FromMilliseconds(120),
            explorerRestartSettle: TimeSpan.FromMilliseconds(120),
            onAutoRestoreCreated: ar => capturedAutoRestore = ar,
            showBalloon: (_, _, _) => Interlocked.Increment(ref balloonCount),
            onAppStartup: app =>
            {
                Berth.Window.MainWindow? win = app.Windows.OfType<Berth.Window.MainWindow>().FirstOrDefault();
                if (win is null || capturedEvents is null || capturedAutoRestore is null || capturedIcon is null)
                {
                    app.Shutdown(2);
                    return;
                }

                nint hwnd = capturedEvents.Handle;
                var timerArmed = new ManualResetEventSlim(false);
                var finishedCount = 0;
                bool lastSilent = false;
                var finishedGate = new ManualResetEventSlim(false);

                capturedAutoRestore.TimerArmed += (_, _) => timerArmed.Set();
                capturedAutoRestore.Finished += (_, _, silent) =>
                {
                    lastSilent = silent;
                    Interlocked.Increment(ref finishedCount);
                    finishedGate.Set();
                };

                _ = Task.Run(() =>
                {
                    try
                    {
                        var vm = app.Dispatcher.Invoke(() => (Berth.Window.MainViewModel)win.DataContext);
                        vm.RefreshAsync().GetAwaiter().GetResult();

                        // 1. Switch monitors from A to B with 3 moved icons, arm gate to observe Working + disabled controls.
                        monitors.Monitors = [monB1, monB2];
                        desktop.ReplaceIcons(movedB);
                        desktop.ArmGate();
                        timerArmed.Reset();
                        finishedGate.Reset();

                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        if (!timerArmed.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("AutoRestore timer did not arm.");
                        }

                        // During settle interval, window must remain idle.
                        app.Dispatcher.Invoke(() =>
                        {
                            idleDuringSettle = !vm.Detail.IsBusy && win.StatusLineControl.SentenceText.Text != "Working";
                        });

                        if (!desktop.WaitUntilEntered(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("AutoRestore did not enter SetPositions gate.");
                        }

                        // While SetPositions is in flight, wait briefly for UI busy marshal.
                        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < deadline)
                        {
                            bool isBusy = app.Dispatcher.Invoke(() => vm.Detail.IsBusy);
                            if (isBusy)
                            {
                                break;
                            }

                            Thread.Sleep(10);
                        }

                        app.Dispatcher.Invoke(() =>
                        {
                            win.UpdateLayout();
                            workingSentence = win.StatusLineControl.SentenceText.Text;
                            restoreDisabledWhileWorking = !win.DetailHeaderControl.RestoreButton.IsEnabled;
                            saveDisabledWhileWorking = !win.DetailHeaderControl.SaveButton.IsEnabled;
                        });

                        desktop.ReleaseGate();
                        if (!finishedGate.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("AutoRestore did not finish.");
                        }

                        // Wait for OnExternalOperationFinishedAsync to publish result on UI thread.
                        deadline = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < deadline)
                        {
                            string sentence = app.Dispatcher.Invoke(() => win.StatusLineControl.SentenceText.Text);
                            if (sentence == "Restored 3 icons.")
                            {
                                break;
                            }

                            Thread.Sleep(15);
                        }

                        app.Dispatcher.Invoke(() =>
                        {
                            win.UpdateLayout();
                            restoredSentence = win.StatusLineControl.SentenceText.Text;
                            undoVisible = win.StatusLineControl.ActionButton.Visibility == System.Windows.Visibility.Visible
                                && Equals(win.StatusLineControl.ActionButton.Content, "Undo");
                            selectedAfterRestore = vm.Selected?.Name ?? string.Empty;
                            inPlaceAfterRestore = vm.Comparison?.InPlace ?? -1;
                            movedAfterRestore = vm.Comparison?.Moved ?? -1;
                            tooltipAfterB = capturedIcon.TooltipText;
                        });

                        // Verify status + Undo survive an unchanged RefreshAsync.
                        vm.RefreshAsync().GetAwaiter().GetResult();
                        app.Dispatcher.Invoke(() =>
                        {
                            win.UpdateLayout();
                            sentenceAfterUnchangedRefresh = win.StatusLineControl.SentenceText.Text;
                            undoStillVisibleAfterUnchangedRefresh =
                                win.StatusLineControl.ActionButton.Visibility == System.Windows.Visibility.Visible
                                && Equals(win.StatusLineControl.ActionButton.Content, "Undo");
                        });

                        // 2. Invoke Undo via UI Automation InvokePattern (flush Input priority so ButtonAutomationPeer click executes).
                        app.Dispatcher.Invoke(() =>
                        {
                            var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(win.StatusLineControl.ActionButton);
                            var provider = (System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(
                                System.Windows.Automation.Peers.PatternInterface.Invoke);
                            provider.Invoke();
                            win.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
                        });
                        vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

                        IReadOnlyList<IconPosition> afterUndo = ((IDesktopIcons)desktop).ReadIcons();
                        undoPositions = string.Join(",", afterUndo.Select(i => $"{i.X}:{i.Y}"));
                        app.Dispatcher.Invoke(() =>
                        {
                            win.UpdateLayout();
                            sentenceAfterUndo = win.StatusLineControl.SentenceText.Text;
                        });

                        // 3. Rename B on disk WITHOUT another WM_DISPLAYCHANGE, trigger AutoRestore directly -> Finished must refresh tooltip!
                        store.Save(new Layout(Layout.CurrentSchemaVersion, keyB, "Docked Renamed", fixedNow, [monB1, monB2], [.. savedB], null));
                        finishedGate.Reset();
                        capturedAutoRestore.Trigger("display change", TimeSpan.FromMilliseconds(40));
                        if (!finishedGate.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("AutoRestore after rename did not finish.");
                        }

                        deadline = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < deadline)
                        {
                            string tip = app.Dispatcher.Invoke(() => capturedIcon.TooltipText);
                            if (tip.Contains("Docked Renamed", StringComparison.Ordinal))
                            {
                                tooltipAfterRenameWithoutDisplayChange = tip;
                                break;
                            }

                            Thread.Sleep(15);
                        }

                        // 4. Switch monitors to unsaved arrangement C and post WM_DISPLAYCHANGE -> silent NoLayout + no-layout tooltip.
                        monitors.Monitors = [monC];
                        finishedGate.Reset();
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        if (!finishedGate.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("AutoRestore for unsaved C did not finish.");
                        }

                        silentForC = lastSilent;
                        cLayoutCreated = store.Find(keyC) is not null;
                        deadline = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < deadline)
                        {
                            string tip = app.Dispatcher.Invoke(() => capturedIcon.TooltipText);
                            if (tip == "Berth: no layout for this monitor arrangement")
                            {
                                tooltipAfterUnsavedC = tip;
                                break;
                            }

                            Thread.Sleep(15);
                        }

                        // 5. Hide the window, switch back to B with moved icons, trigger automatic restore while hidden, then reopen.
                        app.Dispatcher.Invoke(() => win.Close());
                        monitors.Monitors = [monB1, monB2];
                        desktop.ReplaceIcons(movedB);
                        finishedGate.Reset();
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        if (!finishedGate.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Hidden-window AutoRestore did not finish.");
                        }

                        IReadOnlyList<IconPosition> afterHiddenRestore = ((IDesktopIcons)desktop).ReadIcons();
                        hiddenDesktopRestored = afterHiddenRestore.Select(i => i.X).SequenceEqual([120, 240, 360]);

                        // Drain UI queue before reopening so any wrongly-queued hidden completion would have landed.
                        app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                        app.Dispatcher.Invoke(() => win.BringToFront());
                        vm.RefreshAsync().GetAwaiter().GetResult();
                        app.Dispatcher.Invoke(() =>
                        {
                            win.UpdateLayout();
                            hiddenReopenSentence = win.StatusLineControl.SentenceText.Text;
                            hiddenReopenUndoVisible = win.StatusLineControl.ActionButton.Visibility == System.Windows.Visibility.Visible;
                            win.CloseForShutdown();
                            app.Shutdown(0);
                        });
                    }
                    catch (Exception ex)
                    {
                        workerError = ex.ToString();
                        app.Dispatcher.BeginInvoke(() => app.Shutdown(3));
                    }
                    finally
                    {
                        desktop.ReleaseGate();
                    }
                });
            });

        File.WriteAllLines(
            reportFile,
            [
                $"idleDuringSettle={idleDuringSettle}",
                $"workingSentence={workingSentence}",
                $"restoreDisabledWhileWorking={restoreDisabledWhileWorking}",
                $"saveDisabledWhileWorking={saveDisabledWhileWorking}",
                $"restoredSentence={restoredSentence}",
                $"undoVisible={undoVisible}",
                $"selectedAfterRestore={selectedAfterRestore}",
                $"inPlaceAfterRestore={inPlaceAfterRestore}",
                $"movedAfterRestore={movedAfterRestore}",
                $"sentenceAfterUnchangedRefresh={sentenceAfterUnchangedRefresh}",
                $"undoStillVisibleAfterUnchangedRefresh={undoStillVisibleAfterUnchangedRefresh}",
                $"sentenceAfterUndo={sentenceAfterUndo}",
                $"undoPositions={undoPositions}",
                $"tooltipAfterB={tooltipAfterB}",
                $"tooltipAfterRenameWithoutDisplayChange={tooltipAfterRenameWithoutDisplayChange}",
                $"tooltipAfterUnsavedC={tooltipAfterUnsavedC}",
                $"cLayoutCreated={cLayoutCreated}",
                $"silentForC={silentForC}",
                $"balloonCount={Volatile.Read(ref balloonCount)}",
                $"hiddenDesktopRestored={hiddenDesktopRestored}",
                $"hiddenReopenSentence={hiddenReopenSentence}",
                $"hiddenReopenUndoVisible={hiddenReopenUndoVisible}",
                $"workerError={workerError}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private static int RunAutoRestoreMidOpenAndTrayOnlyScenario(BerthPaths paths, Log log, string instanceName)
    {
        string reportFile = Environment.GetEnvironmentVariable(ReportFileEnvVar)
            ?? throw new InvalidOperationException("BERTH_TEST_CHILD_REPORT_FILE must be set.");

        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedChildClock(fixedNow);
        var mon = new MonitorInfo(0, 0, 1920, 1080, 100, true);
        IconPosition[] saved =
        [
            new("m-1", "One", 100, 100),
            new("m-2", "Two", 200, 100),
            new("m-3", "Three", 300, 100),
        ];
        IconPosition[] moved =
        [
            new("m-1", "One", 800, 100),
            new("m-2", "Two", 800, 200),
            new("m-3", "Three", 800, 300),
        ];
        string key = ArrangementKey.From([mon]);

        var store = new LayoutStore(paths);
        store.Save(new Layout(Layout.CurrentSchemaVersion, key, "Desk", fixedNow, [mon], [.. saved], null));
        var settingsStore = new SettingsStore(paths);
        _ = settingsStore.Update(s => s with { RestoreAutomatically = true });

        var monitors = new Berth.Tests.Fakes.FakeMonitors(mon);
        using var desktop = new SwappableGatedDesktopIcons(moved);
        var gate = OperationLock.ForApp(paths);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);
        using var composition = Composition.ForTest(paths, log, monitors, desktop, store, gate, service);
        var theme = new Berth.Desktop.TaskbarTheme(@"Software\Berth.Tests\MissingTrayTheme");

        SystemEvents? capturedEvents = null;
        AutoRestore? capturedAutoRestore = null;
        TrayMenu? capturedMenu = null;

        int windowsAfterTrayOnlyRestore = -1;
        bool trayMenuSaveDisabledDuringTrayOnlyRestore = false;
        bool midOpenSawBusy = false;
        bool midOpenRestoreDisabled = false;
        string midOpenRecoveredSentence = string.Empty;
        bool midOpenRecoveredUndo = false;
        string workerError = string.Empty;

        int exitCode = TrayApp.RunWithInstance(
            openWindow: false,
            Settings.Default with { RestoreAutomatically = true },
            onWindowOpened: null,
            () => new SingleInstance(instanceName),
            log,
            showCrashMessage: _ => true,
            runPrimary: null,
            paths: paths,
            monitors: monitors,
            clock: clock,
            zone: TimeZoneInfo.Utc,
            taskbarTheme: theme,
            composition: composition,
            onTrayMenuCreated: menu => capturedMenu = menu,
            onSystemEventsCreated: events => capturedEvents = events,
            displayChangeSettle: TimeSpan.FromMilliseconds(40),
            explorerRestartSettle: TimeSpan.FromMilliseconds(40),
            onAutoRestoreCreated: ar => capturedAutoRestore = ar,
            onAppStartup: app =>
            {
                if (capturedEvents is null || capturedAutoRestore is null || capturedMenu is null)
                {
                    app.Shutdown(2);
                    return;
                }

                nint hwnd = capturedEvents.Handle;
                var finishedGate = new ManualResetEventSlim(false);
                capturedAutoRestore.Finished += (_, _, _) => finishedGate.Set();

                _ = Task.Run(() =>
                {
                    try
                    {
                        // 1. Tray-only startup: automatic restore blocks tray menu Save while running and finishes without creating a window.
                        desktop.ArmGate();
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        if (!desktop.WaitUntilEntered(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Tray-only AutoRestore did not enter gate.");
                        }

                        app.Dispatcher.Invoke(() => capturedMenu.RefreshState());
                        capturedMenu.LastRefreshTask.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                        app.Dispatcher.Invoke(() =>
                        {
                            trayMenuSaveDisabledDuringTrayOnlyRestore = !capturedMenu.SaveItem.Enabled;
                        });

                        desktop.ReleaseGate();
                        if (!finishedGate.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Tray-only AutoRestore did not finish.");
                        }

                        app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                        windowsAfterTrayOnlyRestore = app.Dispatcher.Invoke(() => app.Windows.Count);

                        // 2. Move icons again, hold automatic restore mid-flight, open window mid-restore!
                        desktop.ReplaceIcons(moved);
                        desktop.ArmGate();
                        finishedGate.Reset();
                        _ = SystemEventsTests.PostMessageW(hwnd, SystemEvents.WmDisplayChange, IntPtr.Zero, IntPtr.Zero);
                        if (!desktop.WaitUntilEntered(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Mid-open AutoRestore did not enter gate.");
                        }

                        Berth.Window.MainWindow? win = null;
                        app.Dispatcher.Invoke(() =>
                        {
                            capturedMenu.OpenItem.PerformClick();
                            win = app.Windows.OfType<Berth.Window.MainWindow>().FirstOrDefault();
                            if (win is not null)
                            {
                                var vm = (Berth.Window.MainViewModel)win.DataContext;
                                win.UpdateLayout();
                                midOpenSawBusy = vm.Detail.IsBusy && win.StatusLineControl.SentenceText.Text == "Working";
                                midOpenRestoreDisabled = !win.DetailHeaderControl.RestoreButton.IsEnabled;
                            }
                        });

                        desktop.ReleaseGate();
                        if (!finishedGate.Wait(TimeSpan.FromSeconds(5)))
                        {
                            throw new TimeoutException("Mid-open AutoRestore did not finish.");
                        }

                        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                        while (DateTime.UtcNow < deadline && win is not null)
                        {
                            string s = app.Dispatcher.Invoke(() => win.StatusLineControl.SentenceText.Text);
                            if (s == "Restored 3 icons.")
                            {
                                break;
                            }

                            Thread.Sleep(15);
                        }

                        app.Dispatcher.Invoke(() =>
                        {
                            if (win is not null)
                            {
                                win.UpdateLayout();
                                midOpenRecoveredSentence = win.StatusLineControl.SentenceText.Text;
                                midOpenRecoveredUndo = win.StatusLineControl.ActionButton.Visibility == System.Windows.Visibility.Visible;
                                win.CloseForShutdown();
                            }

                            app.Shutdown(0);
                        });
                    }
                    catch (Exception ex)
                    {
                        workerError = ex.ToString();
                        app.Dispatcher.BeginInvoke(() => app.Shutdown(3));
                    }
                    finally
                    {
                        desktop.ReleaseGate();
                    }
                });
            });

        File.WriteAllLines(
            reportFile,
            [
                $"windowsAfterTrayOnlyRestore={windowsAfterTrayOnlyRestore}",
                $"trayMenuSaveDisabledDuringTrayOnlyRestore={trayMenuSaveDisabledDuringTrayOnlyRestore}",
                $"midOpenSawBusy={midOpenSawBusy}",
                $"midOpenRestoreDisabled={midOpenRestoreDisabled}",
                $"midOpenRecoveredSentence={midOpenRecoveredSentence}",
                $"midOpenRecoveredUndo={midOpenRecoveredUndo}",
                $"workerError={workerError}",
                $"exitCode={exitCode}",
            ]);

        return exitCode;
    }

    private sealed class SwappableGatedDesktopIcons(params IconPosition[] initial) : IDesktopIcons, IDisposable
    {
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _release = new(false);
        private Berth.Tests.Fakes.FakeDesktopIcons _inner = new(initial);
        private int _gateArmed;

        public void ReplaceIcons(params IconPosition[] icons) =>
            Volatile.Write(ref _inner, new Berth.Tests.Fakes.FakeDesktopIcons(icons));

        public void ArmGate()
        {
            _entered.Reset();
            _release.Reset();
            Volatile.Write(ref _gateArmed, 1);
        }

        public bool WaitUntilEntered(TimeSpan timeout) => _entered.Wait(timeout);

        public void ReleaseGate() => _release.Set();

        public bool AutoArrange => ((IDesktopIcons)Volatile.Read(ref _inner)).AutoArrange;

        public IReadOnlyList<IconPosition> ReadIcons() => ((IDesktopIcons)Volatile.Read(ref _inner)).ReadIcons();

        public void SetPositions(IReadOnlyList<IconPosition> icons) =>
            SetPositions(icons, () => { });

        public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove)
        {
            if (Interlocked.Exchange(ref _gateArmed, 0) == 1)
            {
                _entered.Set();
                _release.Wait(TimeSpan.FromSeconds(10));
            }

            ((IDesktopIcons)Volatile.Read(ref _inner)).SetPositions(icons, beforeMove);
        }

        public bool TryTurnAutoArrangeOff() => ((IDesktopIcons)Volatile.Read(ref _inner)).TryTurnAutoArrangeOff();

        public void Dispose()
        {
            _release.Set();
            _entered.Dispose();
            _release.Dispose();
        }
    }

    private sealed class DisposeTrackingDesktopIcons(
        IDesktopIcons inner,
        Action onDispose,
        Action onUsedAfterDispose) : IDesktopIcons, IDisposable
    {
        private int _disposed;

        public bool AutoArrange => inner.AutoArrange;

        public IReadOnlyList<IconPosition> ReadIcons()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                onUsedAfterDispose();
            }

            return inner.ReadIcons();
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                onUsedAfterDispose();
            }

            inner.SetPositions(icons);
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                onUsedAfterDispose();
            }

            inner.SetPositions(icons, beforeMove);
        }

        public bool TryTurnAutoArrangeOff() => inner.TryTurnAutoArrangeOff();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                onDispose();
            }
        }
    }

    private sealed class RecordingMainWindow(Action onBringToFront) : Berth.Window.IMainWindow
    {
        public void BringToFront() => onBringToFront();

        public void CloseForShutdown()
        {
        }
    }

    private sealed class FixedChildClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
