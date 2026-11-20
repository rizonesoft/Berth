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

        // 3. ClaimedPrimary -> enters primary startup exactly once and returns its exit code.
        string primaryName = "handoff-primary-" + Guid.NewGuid().ToString("N");
        int entered = 0;
        int primaryCode = TrayApp.RunWithInstance(
            openWindow: true,
            Settings.Default,
            onWindowOpened: null,
            () => new SingleInstance(primaryName),
            log,
            _ => true,
            runPrimary: (_, _) =>
            {
                entered++;
                return 0;
            });
        Assert.Equal(0, primaryCode);
        Assert.Equal(1, entered);
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

            // BEFORE any test-driven Apply: verify startup already merged Shared + active colour dictionary + Fluent resources.
            var window = new System.Windows.Window { Width = 300, Height = 200 };
            var marker = new Border();
            marker.SetResourceReference(Border.BackgroundProperty, "Berth.MarkerInPlace");
            window.Content = marker;

            Assert.Equal(980.0, Assert.IsType<double>(window.FindResource("Berth.WindowDefaultWidth")));
            Assert.IsType<SolidColorBrush>(window.FindResource("Berth.MarkerInPlace"));
            // Fluent theme resource resolves through Application resources.
            Assert.NotNull(window.TryFindResource(typeof(System.Windows.Controls.Button)));

            // Add an unrelated merged dictionary to verify its presence and precedence are preserved across theme switches.
            var customDict = new ResourceDictionary { ["Custom.TestKey"] = "keep-me" };
            app.Resources.MergedDictionaries.Add(customDict);
            try
            {
                // Switch Light -> Dark -> HighContrast -> Light and verify the SAME live control updates every time.
                app.ApplyColorTheme(ColorTheme.Light);
                Assert.Equal(LightMarkerColor, Assert.IsType<SolidColorBrush>(marker.Background).Color);

                app.ApplyColorTheme(ColorTheme.Dark);
                Assert.Equal(DarkMarkerColor, Assert.IsType<SolidColorBrush>(marker.Background).Color);

                app.ApplyColorTheme(ColorTheme.HighContrast);
                Assert.Equal(WpfSystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(marker.Background).Color);

                app.ApplyColorTheme(ColorTheme.Light);
                Assert.Equal(LightMarkerColor, Assert.IsType<SolidColorBrush>(marker.Background).Color);

                // After repeated switches: exactly 1 Shared dictionary, 1 Berth colour dictionary, and customDict preserved.
                Assert.Equal(1, app.Resources.MergedDictionaries.Count(ThemeResources.IsSharedDictionary));
                Assert.Equal(1, app.Resources.MergedDictionaries.Count(ThemeResources.IsColorDictionary));
                Assert.Contains(customDict, app.Resources.MergedDictionaries);
                Assert.Equal("keep-me", app.FindResource("Custom.TestKey"));
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
