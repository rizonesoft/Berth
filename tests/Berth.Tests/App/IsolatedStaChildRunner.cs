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
    public const string TimerShutdownScenario = "tray-timer-shutdown";
    public const string DispatcherCrashScenario = "tray-dispatcher-crash";

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
        TimeSpan? timeout = null)
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

            _ => throw new InvalidOperationException($"Unknown scenario '{scenario}'."),
        };
    }
}
