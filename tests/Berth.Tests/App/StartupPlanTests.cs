using Berth.App;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Cli;
using Berth.Tests.Fakes;

namespace Berth.Tests.App;

public sealed class StartupPlanTests
{
    private static readonly MonitorInfo[] OneMonitor = [new(0, 0, 1920, 1080, 100, true)];

    [Fact]
    public void StartupPlanForMapsEveryArgumentShapeAndSettingsExistence()
    {
        Assert.Equal(new StartupPlan(StartupMode.TrayApp, OpenWindow: true), StartupPlan.For(CliArguments.Parse([]), settingsExist: false));
        Assert.Equal(new StartupPlan(StartupMode.TrayApp, OpenWindow: false), StartupPlan.For(CliArguments.Parse([]), settingsExist: true));

        foreach (string[] cmdArgs in new[]
        {
            new[] { "save" },
            new[] { "restore" },
            new[] { "undo" },
            new[] { "list" },
            new[] { "--help" },
            new[] { "--version" },
        })
        {
            Assert.Equal(new StartupPlan(StartupMode.Command, OpenWindow: false), StartupPlan.For(CliArguments.Parse(cmdArgs), settingsExist: false));
            Assert.Equal(new StartupPlan(StartupMode.Command, OpenWindow: false), StartupPlan.For(CliArguments.Parse(cmdArgs), settingsExist: true));
        }

        Assert.Equal(new StartupPlan(StartupMode.UsageError, OpenWindow: false), StartupPlan.For(CliArguments.Parse(["frobnicate"]), settingsExist: false));
        Assert.Equal(new StartupPlan(StartupMode.UsageError, OpenWindow: false), StartupPlan.For(CliArguments.Parse(["frobnicate"]), settingsExist: true));
    }

    [Fact]
    public void FirstRunPersistsDefaultSettingsOnlyAfterWindowOpenedCallbackAndSecondStartDoesNotOpenWindow()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);

        int trayCalls = 0;
        bool observedBeforeCallback = true;
        bool firstOpenWindow = false;
        Settings? firstSettings = null;

        var out1 = new StringWriter();
        var err1 = new StringWriter();
        int code1 = global::Berth.Program.Run(
            [],
            out1,
            err1,
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            runTrayApp: (openWindow, settings, onWindowOpened) =>
            {
                trayCalls++;
                firstOpenWindow = openWindow;
                firstSettings = settings;
                observedBeforeCallback = File.Exists(paths.SettingsFile);
                onWindowOpened();
                return 0;
            });

        Assert.Equal(0, code1);
        Assert.Equal(1, trayCalls);
        Assert.True(firstOpenWindow);
        Assert.Equal(Settings.Default, firstSettings);
        Assert.False(observedBeforeCallback, "settings.json must not be created before the first-run window opens.");
        Assert.Equal(string.Empty, out1.ToString());
        Assert.Equal(string.Empty, err1.ToString());

        SettingsLoad reloaded = store.Load();
        Assert.True(reloaded.Exists);
        Assert.Null(reloaded.Unreadable);
        Assert.Equal(Settings.Default, reloaded.Value);

        // Second start sees settings.json and passes OpenWindow = false.
        bool secondOpenWindow = true;
        var out2 = new StringWriter();
        var err2 = new StringWriter();
        int code2 = global::Berth.Program.Run(
            [],
            out2,
            err2,
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            runTrayApp: (openWindow, _, _) =>
            {
                trayCalls++;
                secondOpenWindow = openWindow;
                return 0;
            });

        Assert.Equal(0, code2);
        Assert.Equal(2, trayCalls);
        Assert.False(secondOpenWindow);
    }

    [Fact]
    public void FirstRunDoesNotCreateSettingsWhenWindowNeverOpens()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var output = new StringWriter();
        var error = new StringWriter();

        // Tray delegate that never opens a window or invokes onWindowOpened must leave settings.json absent.
        int code = global::Berth.Program.Run(
            [],
            output,
            error,
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            runTrayApp: (_, _, _) => 0);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.False(File.Exists(paths.SettingsFile));
    }

    [Fact]
    public void FirstRunDoesNotOverwriteSettingsCreatedBeforeWindowOpened()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new SettingsStore(paths);
        var custom = new Settings(
            Settings.CurrentSchemaVersion,
            RestoreAutomatically: true,
            new WindowPlacement(140, 90, 960, 640));

        byte[]? bytesBeforeCallback = null;
        int code = global::Berth.Program.Run(
            [],
            new StringWriter(),
            new StringWriter(),
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            runTrayApp: (openWindow, _, onWindowOpened) =>
            {
                Assert.True(openWindow);
                // Another writer creates settings.json between startup and the window-opened callback.
                store.Save(custom);
                bytesBeforeCallback = File.ReadAllBytes(paths.SettingsFile);
                onWindowOpened();
                return 0;
            });

        Assert.Equal(0, code);
        Assert.NotNull(bytesBeforeCallback);
        Assert.Equal(bytesBeforeCallback, File.ReadAllBytes(paths.SettingsFile));
        Assert.Equal(custom, store.Load().Value);

        // Also verify the atomic create-only race when a file appears right before MoveNew.
        using var raceData = new TestData();
        BerthPaths racePaths = BerthPaths.ForRoot(raceData.Root);
        var raceStore = new SettingsStore(racePaths);
        byte[]? racedBytes = null;
        bool created = raceStore.TrySaveIfMissing(
            Settings.Default,
            beforeReplace: () =>
            {
                raceStore.Save(custom);
                racedBytes = File.ReadAllBytes(racePaths.SettingsFile);
            });

        Assert.False(created);
        Assert.NotNull(racedBytes);
        Assert.Equal(racedBytes, File.ReadAllBytes(racePaths.SettingsFile));
    }

    [Fact]
    public void TrayAppReceivesReadableNonDefaultSettingsWithOpenWindowFalse()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var custom = new Settings(
            Settings.CurrentSchemaVersion,
            RestoreAutomatically: true,
            new WindowPlacement(120, 80, 1024, 720));
        new SettingsStore(paths).Save(custom);

        bool? receivedOpenWindow = null;
        Settings? receivedSettings = null;
        int code = global::Berth.Program.Run(
            [],
            new StringWriter(),
            new StringWriter(),
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            runTrayApp: (openWindow, settings, _) =>
            {
                receivedOpenWindow = openWindow;
                receivedSettings = settings;
                return 0;
            });

        Assert.Equal(0, code);
        Assert.False(receivedOpenWindow);
        Assert.Equal(custom, receivedSettings);
    }

    [Fact]
    public void UnreadableSettingsLogsOneWarnRunsOnDefaultsWithOpenWindowFalseAndPreservesFileBytes()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        Directory.CreateDirectory(paths.DataRoot);
        byte[] corruptBytes = "{"u8.ToArray();
        File.WriteAllBytes(paths.SettingsFile, corruptBytes);

        bool? receivedOpenWindow = null;
        Settings? receivedSettings = null;
        int code = global::Berth.Program.Run(
            [],
            new StringWriter(),
            new StringWriter(),
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            runTrayApp: (openWindow, settings, onWindowOpened) =>
            {
                receivedOpenWindow = openWindow;
                receivedSettings = settings;
                onWindowOpened();
                return 0;
            });

        Assert.Equal(0, code);
        Assert.False(receivedOpenWindow);
        Assert.Equal(Settings.Default, receivedSettings);
        Assert.Equal(corruptBytes, File.ReadAllBytes(paths.SettingsFile));

        Assert.True(File.Exists(paths.LogFile));
        string[] logLines = File.ReadAllLines(paths.LogFile);
        string soleLine = Assert.Single(logLines);
        Assert.EndsWith($" WARN {Messages.UnreadableSettings}", soleLine, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedFirstRunSaveLogsPathSpecificFailDoesNotCrashAndNextStartStillOpensWindow()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);

        int code1 = global::Berth.Program.Run(
            [],
            new StringWriter(),
            new StringWriter(),
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            runTrayApp: (openWindow, _, onWindowOpened) =>
            {
                Assert.True(openWindow);
                // Block DataRoot with a regular file so Load() sees Missing and the write fails with IOException.
                File.WriteAllText(paths.DataRoot, "blocker");
                onWindowOpened();
                return 0;
            });

        Assert.Equal(0, code1);
        Assert.True(File.Exists(paths.LogFile));
        string[] logLines = File.ReadAllLines(paths.LogFile);
        string soleLine = Assert.Single(logLines);
        Assert.Contains($" FAIL Berth could not write {paths.SettingsFile}: ", soleLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Something went wrong", soleLine, StringComparison.Ordinal);

        // Remove the file blocker: because settings.json is still missing, a fresh start still requests OpenWindow = true.
        File.Delete(paths.DataRoot);
        bool nextOpenWindow = false;
        int code2 = global::Berth.Program.Run(
            [],
            new StringWriter(),
            new StringWriter(),
            getenv: k => k == "BERTH_DATA" ? data.Root : null,
            runTrayApp: (openWindow, _, _) =>
            {
                nextOpenWindow = openWindow;
                return 0;
            });

        Assert.Equal(0, code2);
        Assert.True(nextOpenWindow);
    }

    [Fact]
    public void CliCommandsAndUsageErrorsBypassTrayStartupAndDoNotLoadMalformedSettings()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        Directory.CreateDirectory(paths.DataRoot);
        File.WriteAllText(paths.SettingsFile, "{");

        int trayCalls = 0;
        int loadSettingsCalls = 0;
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "Icon", 10, 20));
        var monitors = new FakeMonitors(OneMonitor);
        var store = new LayoutStore(paths);
        var gate = new OperationLock(paths, TimeSpan.FromSeconds(1));
        var clock = TimeProvider.System;

        (string[] Args, int ExpectedExit)[] invocations =
        [
            (["--help"], 0),
            (["--version"], 0),
            (["list"], 0),
            (["save"], 0),
            (["restore"], 0),
            (["undo"], 0),
            (["frobnicate"], 2),
        ];

        foreach ((string[] args, int expectedExit) in invocations)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            int exitCode = global::Berth.Program.Run(
                args,
                output,
                error,
                getenv: k => k == "BERTH_DATA" ? data.Root : null,
                createComposition: (p, l) => Composition.ForTest(
                    p,
                    l,
                    monitors,
                    desktop,
                    store,
                    gate,
                    new LayoutService(desktop, monitors, store, p, gate, l, clock)),
                createMonitors: () => monitors,
                runTrayApp: (_, _, _) =>
                {
                    trayCalls++;
                    throw new InvalidOperationException("CLI commands must never start the tray app.");
                },
                loadSettings: s =>
                {
                    loadSettingsCalls++;
                    return s.Load();
                });

            Assert.Equal(expectedExit, exitCode);
        }

        Assert.Equal(0, trayCalls);
        Assert.Equal(0, loadSettingsCalls);
        if (File.Exists(paths.LogFile))
        {
            string logText = File.ReadAllText(paths.LogFile);
            Assert.DoesNotContain(Messages.UnreadableSettings, logText, StringComparison.Ordinal);
        }
    }
}

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed class StartupPlanProcessTests
{
    [Fact]
    public void SecondBerthExeWithSameInstanceSignalsPrimaryAndExits0Within10Seconds()
    {
        using var data = new TestData();
        string instanceName = "paired-" + Guid.NewGuid().ToString("N");
        var env = new Dictionary<string, string> { ["BERTH_INSTANCE"] = instanceName };

        using BerthProcess first = BerthProcess.Start(data.Root, [], extraEnvironment: env);

        // Wait until the first child owns the named instance mutex before launching the second child.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        bool firstClaimed = false;
        while (DateTime.UtcNow < deadline)
        {
            using var probe = new SingleInstance(instanceName);
            if (!probe.TryClaim())
            {
                firstClaimed = true;
                break;
            }

            Thread.Sleep(50);
        }

        Assert.True(firstClaimed, "First berth.exe child did not claim the single-instance mutex within 10 seconds.");

        using BerthProcess second = BerthProcess.Start(data.Root, [], extraEnvironment: env);
        BerthProcessResult secondResult = second.WaitForExit(TimeSpan.FromSeconds(10));

        Assert.Equal(0, secondResult.ExitCode);
        Assert.Equal(string.Empty, secondResult.StandardOutput);
        Assert.Equal(string.Empty, secondResult.StandardError);

        // Confirm the first instance is still running after the second signalled it and exited.
        using (var postProbe = new SingleInstance(instanceName))
        {
            Assert.False(postProbe.TryClaim(), "Primary berth.exe should still be running after secondary exited.");
        }
    }
}
