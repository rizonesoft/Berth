using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Berth.App;
using Berth.Cli;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;
using Microsoft.Win32;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSystemColors = System.Windows.SystemColors;

namespace Berth.Tests.App;

public sealed class ThemeResourcesTests
{
    [Theory]
    [InlineData(true, true, ColorTheme.HighContrast)]
    [InlineData(true, false, ColorTheme.HighContrast)]
    [InlineData(false, true, ColorTheme.Light)]
    [InlineData(false, false, ColorTheme.Dark)]
    public void SelectPicksHighContrastFirstThenLightOrDark(bool highContrast, bool appsUseLightTheme, ColorTheme expected)
    {
        Assert.Equal(expected, ThemeResources.Select(highContrast, appsUseLightTheme));
    }

    [Fact]
    public void ReadAppsUseLightThemeMatchesWpfFallbackUnderIsolatedRegistryKey()
    {
        using var testKey = new IsolatedPersonalizeKey();

        // Both values missing -> dark (false).
        Assert.False(ThemeResources.ReadAppsUseLightTheme(testKey.Path));

        // AppsUseLightTheme missing, SystemUsesLightTheme = 1 -> light (true).
        testKey.SetDword("SystemUsesLightTheme", 1);
        Assert.True(ThemeResources.ReadAppsUseLightTheme(testKey.Path));

        // AppsUseLightTheme missing, SystemUsesLightTheme = 0 -> dark (false).
        testKey.SetDword("SystemUsesLightTheme", 0);
        Assert.False(ThemeResources.ReadAppsUseLightTheme(testKey.Path));

        // Explicit AppsUseLightTheme = 1 overrides SystemUsesLightTheme = 0 -> light (true).
        testKey.SetDword("AppsUseLightTheme", 1);
        Assert.True(ThemeResources.ReadAppsUseLightTheme(testKey.Path));

        // Explicit AppsUseLightTheme = 0 overrides SystemUsesLightTheme = 1 -> dark (false).
        testKey.SetDword("SystemUsesLightTheme", 1);
        testKey.SetDword("AppsUseLightTheme", 0);
        Assert.False(ThemeResources.ReadAppsUseLightTheme(testKey.Path));

        // Mistyped string AppsUseLightTheme falls through to SystemUsesLightTheme = 1 -> light (true), matching WPF.
        testKey.SetString("AppsUseLightTheme", "0");
        Assert.True(ThemeResources.ReadAppsUseLightTheme(testKey.Path));

        // Access error falls back cleanly to dark (false) without throwing.
        Assert.False(ThemeResources.ReadAppsUseLightTheme(_ => throw new SecurityException("Access denied")));
    }

    private sealed class IsolatedPersonalizeKey : IDisposable
    {
        public string Path { get; } = $@"Software\Berth.Tests\ThemeResources-{Guid.NewGuid():N}";

        public void SetDword(string valueName, int value)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
            key.SetValue(valueName, value, RegistryValueKind.DWord);
        }

        public void SetString(string valueName, string value)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
            key.SetValue(valueName, value, RegistryValueKind.String);
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(Path, throwOnMissingSubKey: false);
        }
    }
}

public sealed class TrayAppHandoffTests
{
    [Fact]
    public void RunWithInstanceHandlesActivatedExistingRefusedAndClaimedPrimaryBeforeCreatingApplication()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var log = new Log(paths, TimeProvider.System);

        // 1. ActivatedExisting -> exits 0 without entering primary startup.
        string activeName = "handoff-active-" + Guid.NewGuid().ToString("N");
        using (var primary = new SingleInstance(activeName))
        {
            Assert.True(primary.TryClaim());
            int primaryCalls = 0;
            int code = TrayApp.RunWithInstance(
                openWindow: false,
                Settings.Default,
                onWindowOpened: null,
                () => new SingleInstance(activeName),
                log,
                _ => true,
                runPrimary: (_, _) =>
                {
                    primaryCalls++;
                    return 99;
                });
            Assert.Equal(CliExit.Done, code);
            Assert.Equal(0, primaryCalls);
        }

        // 2. Refused (mutex held with no pipe) -> exits 3 without entering primary startup.
        string refusedName = "handoff-refused-" + Guid.NewGuid().ToString("N");
        using (var holder = new HeldMutex($@"Local\Berth.App.{refusedName}"))
        {
            int primaryCalls = 0;
            int code = TrayApp.RunWithInstance(
                openWindow: false,
                Settings.Default,
                onWindowOpened: null,
                () => new SingleInstance(
                    refusedName,
                    connectTimeout: TimeSpan.FromMilliseconds(120),
                    clientReadTimeout: TimeSpan.FromMilliseconds(120)),
                log,
                _ => true,
                runPrimary: (_, _) =>
                {
                    primaryCalls++;
                    return 99;
                });
            Assert.Equal(CliExit.Refused, code);
            Assert.Equal(0, primaryCalls);
        }

        // 3. ClaimedPrimary takeover: initial TryClaim() fails against a held mutex with no pipe, holder releases
        //    right before the single retry in RunSecondary(), and primary startup runs exactly once.
        string takeoverName = "handoff-takeover-" + Guid.NewGuid().ToString("N");
        var takeoverHolder = new HeldMutex($@"Local\Berth.App.{takeoverName}");
        int entered = 0;
        try
        {
            int primaryCode = TrayApp.RunWithInstance(
                openWindow: true,
                Settings.Default,
                onWindowOpened: null,
                () => new SingleInstance(
                    takeoverName,
                    connectTimeout: TimeSpan.FromMilliseconds(120),
                    clientReadTimeout: TimeSpan.FromMilliseconds(120)),
                log,
                _ => true,
                runPrimary: (_, _) =>
                {
                    entered++;
                    return 0;
                },
                onBeforeReclaim: () => takeoverHolder.Dispose());
            Assert.Equal(0, primaryCode);
            Assert.Equal(1, entered);
        }
        finally
        {
            takeoverHolder.Dispose();
        }

        // 4. Synchronous startup failure is handled by CrashHandler (1 dialog, 1 FAIL record, exit 1).
        string crashName = "handoff-startup-crash-" + Guid.NewGuid().ToString("N");
        int dialogs = 0;
        string? dialogText = null;
        int crashCode = TrayApp.RunWithInstance(
            openWindow: false,
            Settings.Default,
            onWindowOpened: null,
            () => new SingleInstance(crashName),
            log,
            msg =>
            {
                dialogs++;
                dialogText = msg;
                return true;
            },
            runPrimary: (_, _) => throw new InvalidOperationException("Startup theme load failed"));
        Assert.Equal(CliExit.Error, crashCode);
        Assert.Equal(1, dialogs);
        Assert.NotNull(dialogText);
        Assert.StartsWith("Something went wrong: Startup theme load failed. The details are in the log.", dialogText, StringComparison.Ordinal);
        Assert.Contains(" FAIL TrayApp.Startup: Something went wrong: Startup theme load failed.", File.ReadAllText(paths.LogFile), StringComparison.Ordinal);
    }

    private sealed class HeldMutex : IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly Thread _thread;

        public HeldMutex(string mutexName)
        {
            using var acquired = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                using var m = new Mutex(initiallyOwned: false, mutexName);
                Assert.True(m.WaitOne(0));
                acquired.Set();
                _release.Wait();
                m.ReleaseMutex();
            })
            {
                IsBackground = true,
            };
            _thread.Start();
            acquired.Wait();
        }

        public void Dispose()
        {
            _release.Set();
            _thread.Join();
            _release.Dispose();
        }
    }
}

[Collection(DesktopCollectionDefinition.Name)]
public sealed class BerthApplicationTests(AppFixture appFixture)
{
    private static readonly WpfColor LightMarkerColor = (WpfColor)WpfColorConverter.ConvertFromString("#0F6CBD")!;
    private static readonly WpfColor DarkMarkerColor = (WpfColor)WpfColorConverter.ConvertFromString("#4CA6F0")!;

    [Fact]
    public void StartupMergesTokensWithFluentThemeRestylesLiveControlAndPreservesUnrelatedDictionaries()
    {
        appFixture.Invoke(() =>
        {
            var app = Assert.IsType<BerthApplication>(WpfApplication.Current);
            Assert.Equal(ShutdownMode.OnExplicitShutdown, app.ShutdownMode);
#pragma warning disable WPF0001
            Assert.Equal(ThemeMode.System, app.ThemeMode);
#pragma warning restore WPF0001

            // Verify resources captured during AppFixture startup BEFORE any test could call Apply*.
            Assert.Equal(980.0, appFixture.InitialWindowDefaultWidth);
            Assert.True(appFixture.InitialMarkerBrushResolved);
            Assert.True(appFixture.InitialFluentButtonStyleResolved);

            var window = new System.Windows.Window { Width = 300, Height = 200 };
            var marker = new Border();
            marker.SetResourceReference(Border.BackgroundProperty, "Berth.MarkerInPlace");
            window.Content = marker;

            // Switch Light -> Dark -> HighContrast -> Light and verify the SAME live control updates every time.
            app.ApplyColorTheme(ColorTheme.Light);
            Assert.Equal(LightMarkerColor, Assert.IsType<SolidColorBrush>(marker.Background).Color);

            app.ApplyColorTheme(ColorTheme.Dark);
            Assert.Equal(DarkMarkerColor, Assert.IsType<SolidColorBrush>(marker.Background).Color);

            app.ApplyColorTheme(ColorTheme.HighContrast);
            Assert.Equal(WpfSystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(marker.Background).Color);

            app.ApplyColorTheme(ColorTheme.Light);
            Assert.Equal(LightMarkerColor, Assert.IsType<SolidColorBrush>(marker.Background).Color);

            // Add an unrelated merged dictionary with a COLLIDING key AFTER the Berth colour dictionary;
            // switching themes must replace Berth's colour dictionary in place so the later dictionary still wins.
            var overrideBrush = new SolidColorBrush(Colors.MediumVioletRed);
            var customDict = new ResourceDictionary { ["Berth.MarkerInPlace"] = overrideBrush };
            app.Resources.MergedDictionaries.Add(customDict);
            try
            {
                Assert.Same(overrideBrush, app.FindResource("Berth.MarkerInPlace"));
                app.ApplyColorTheme(ColorTheme.Dark);
                Assert.Same(overrideBrush, app.FindResource("Berth.MarkerInPlace"));
                Assert.Equal(1, app.Resources.MergedDictionaries.Count(ThemeResources.IsSharedDictionary));
                Assert.Equal(1, app.Resources.MergedDictionaries.Count(ThemeResources.IsColorDictionary));
            }
            finally
            {
                app.Resources.MergedDictionaries.Remove(customDict);
            }
        });
    }

    [Fact]
    public void UserPreferenceChangedSwitchesBetweenLightDarkAndHighContrastLive()
    {
        try
        {
            appFixture.Invoke(() =>
            {
                var app = appFixture.App;

                appFixture.SetThemeOverrides(highContrast: false, appsUseLightTheme: true);
                app.OnUserPreferenceChanged(UserPreferenceCategory.General);
                Assert.Equal(ColorTheme.Light, app.CurrentColorTheme);
                Assert.Equal(LightMarkerColor, Assert.IsType<SolidColorBrush>(app.FindResource("Berth.MarkerInPlace")).Color);

                appFixture.SetThemeOverrides(highContrast: false, appsUseLightTheme: false);
                app.OnUserPreferenceChanged(UserPreferenceCategory.General);
                Assert.Equal(ColorTheme.Dark, app.CurrentColorTheme);
                Assert.Equal(DarkMarkerColor, Assert.IsType<SolidColorBrush>(app.FindResource("Berth.MarkerInPlace")).Color);

                appFixture.SetThemeOverrides(highContrast: true, appsUseLightTheme: false);
                app.OnUserPreferenceChanged(UserPreferenceCategory.Accessibility);
                Assert.Equal(ColorTheme.HighContrast, app.CurrentColorTheme);

                // Leaving High Contrast returns to the current app preference (Light).
                appFixture.SetThemeOverrides(highContrast: false, appsUseLightTheme: true);
                app.OnUserPreferenceChanged(UserPreferenceCategory.General);
                Assert.Equal(ColorTheme.Light, app.CurrentColorTheme);
            });
        }
        finally
        {
            appFixture.ResetThemeOverrides();
        }
    }

    [Fact]
    public void AttachedCrashHandlerCatchesDispatcherExceptionLogsFailShowsDialogAndRequestsExit1()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var log = new Log(paths, TimeProvider.System);

        using var shutdownDone = new ManualResetEventSlim(false);
        int? shutdownCode = null;
        string? shownMessage = null;

        var handler = new CrashHandler(
            log,
            msg =>
            {
                shownMessage = msg;
                return true;
            },
            exitProcess: _ => { });

        IDisposable? attachment = null;
        try
        {
            appFixture.Invoke(() =>
            {
                attachment = handler.Attach(appFixture.App, shutdown: code =>
                {
                    shutdownCode = code;
                    shutdownDone.Set();
                });

                // Queue an unhandled exception onto the real WPF dispatcher.
                _ = appFixture.App.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    new Action(() => throw new InvalidOperationException("Dispatcher test failure")));
            });

            Assert.True(shutdownDone.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "CrashHandler did not handle dispatcher exception within 5 seconds.");
            Assert.Equal(1, shutdownCode);
            Assert.NotNull(shownMessage);
            Assert.StartsWith("Something went wrong: Dispatcher test failure. The details are in the log.", shownMessage, StringComparison.Ordinal);

            string logText = File.ReadAllText(paths.LogFile);
            Assert.Contains(" FAIL DispatcherUnhandledException: Something went wrong: Dispatcher test failure.", logText, StringComparison.Ordinal);
        }
        finally
        {
            appFixture.Invoke(() => attachment?.Dispose());
        }
    }

    [Fact]
    public void IsolatedStaChildProvesRealTrayAppTimerShutdownExits0AndDispatcherCrashExits1()
    {
        // 1. Real TrayApp.RunWithInstance -> BerthApplication -> DispatcherTimer Shutdown(0) returns exit code 0.
        using (var timerData = new TestData())
        {
            var timerResult = IsolatedStaChildRunner.RunChild(
                IsolatedStaChildRunner.TimerShutdownScenario,
                timerData.Root,
                "iso-timer-" + Guid.NewGuid().ToString("N"));
            Assert.Equal(0, timerResult.ExitCode);
        }

        // 2. Real TrayApp.RunWithInstance -> BerthApplication -> CrashHandler.Attach(app) -> throwing dispatcher
        //    callback logs FAIL, shows dialog, and exits with code 1.
        using var crashData = new TestData();
        string dialogFile = Path.Combine(crashData.Root, "crash-dialog.txt");
        var crashResult = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.DispatcherCrashScenario,
            crashData.Root,
            "iso-crash-" + Guid.NewGuid().ToString("N"),
            dialogFile: dialogFile);

        Assert.Equal(1, crashResult.ExitCode);
        Assert.True(File.Exists(dialogFile), "Crash dialog callback was not invoked in isolated child.");
        string dialogText = File.ReadAllText(dialogFile);
        Assert.StartsWith("Something went wrong: Isolated child dispatcher boom. The details are in the log.", dialogText, StringComparison.Ordinal);

        BerthPaths crashPaths = BerthPaths.ForRoot(crashData.Root);
        Assert.True(File.Exists(crashPaths.LogFile), "Temporary log file was not written on dispatcher crash.");
        string logText = File.ReadAllText(crashPaths.LogFile);
        Assert.Contains(" FAIL DispatcherUnhandledException: Something went wrong: Isolated child dispatcher boom.", logText, StringComparison.Ordinal);
    }
}

[Collection(DesktopCollectionDefinition.Name)]
public sealed class BerthApplicationSecondConsumerTests(AppFixture appFixture)
{
    [Fact]
    public void SecondTestClassSharesAppFixtureAndResolvesBerthMarkerInPlace()
    {
        appFixture.Invoke(() =>
        {
            Assert.IsType<BerthApplication>(WpfApplication.Current);
            Assert.IsType<SolidColorBrush>(WpfApplication.Current.FindResource("Berth.MarkerInPlace"));
        });
    }
}
