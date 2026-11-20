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
                    capturedMenu.OperationFinished += _ => completedCount++;
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

    private sealed class FixedChildClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
