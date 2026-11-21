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
                if (!_release.Wait(TimeSpan.FromSeconds(5)))
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
