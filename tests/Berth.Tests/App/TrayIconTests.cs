using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Berth.App;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.Fakes;
using Microsoft.Win32;

namespace Berth.Tests.App;

public sealed class TrayTextTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SavedToday = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public void EmbeddedTrayIconsLoadFromAssemblyAndContain16PxFrame()
    {
        Assembly assembly = typeof(TrayIcon).Assembly;
        foreach (string name in new[] { TrayIcon.DarkIconResourceName, TrayIcon.LightIconResourceName })
        {
            using Stream? stream = assembly.GetManifestResourceStream(name);
            Assert.NotNull(stream);
            using var icon16 = new Icon(stream, new Size(16, 16));
            Assert.Equal(new Size(16, 16), icon16.Size);
        }
    }

    [Fact]
    public void TooltipFormatsActiveAndMissingLayoutsAndTruncatesCleanlyOnTextElementBoundaries()
    {
        Assert.Equal("Berth: no layout for this monitor arrangement", TrayText.Tooltip(null, Now, Utc));

        Layout docked = MakeLayout("Docked", SavedToday);
        Assert.Equal("Berth: Docked, saved today, 09:14", TrayText.Tooltip(docked, Now, Utc));

        // 40-character name stays well within 127 characters verbatim.
        string name40 = new('A', 40);
        string tooltip40 = TrayText.Tooltip(MakeLayout(name40, SavedToday), Now, Utc);
        Assert.Equal($"Berth: {name40}, saved today, 09:14", tooltip40);
        Assert.True(tooltip40.Length <= TrayText.MaxTooltipLength);

        // Exact 127-char tooltip survives verbatim with no ellipsis.
        const string suffix = ", saved today, 09:14";
        int exactNameLen = TrayText.MaxTooltipLength - "Berth: ".Length - suffix.Length;
        string exactName = new('B', exactNameLen);
        string exactTooltip = TrayText.Tooltip(MakeLayout(exactName, SavedToday), Now, Utc);
        Assert.Equal(TrayText.MaxTooltipLength, exactTooltip.Length);
        Assert.DoesNotContain('\u2026', exactTooltip);
        Assert.Equal($"Berth: {exactName}{suffix}", exactTooltip);

        // Over-limit name with an emoji (UTF-16 surrogate pair) right at the cut boundary must NOT split the surrogate pair.
        int prefixBudget = exactNameLen - 2; // leaves 1 UTF-16 code unit before ellipsis if emoji doesn't fit
        string emojiName = new string('C', prefixBudget) + "\U0001F6A2" + "ExtraTail";
        string truncated = TrayText.Tooltip(MakeLayout(emojiName, SavedToday), Now, Utc);
        Assert.True(truncated.Length <= TrayText.MaxTooltipLength);
        Assert.EndsWith($"\u2026{suffix}", truncated, StringComparison.Ordinal);
        Assert.DoesNotContain(truncated, ch => char.IsHighSurrogate(ch) || char.IsLowSurrogate(ch));

        // Embedded newlines and control characters are normalized via Messages.SingleLine before measuring.
        string dirty = TrayText.Tooltip(MakeLayout("Dock\r\nOne\0Two", SavedToday), Now, Utc);
        Assert.DoesNotContain('\r', dirty);
        Assert.DoesNotContain('\n', dirty);
        Assert.DoesNotContain('\0', dirty);
    }

    private static Layout MakeLayout(string name, DateTimeOffset savedAt) =>
        new(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([new(0, 0, 1920, 1080, 100, true)]),
            name,
            savedAt,
            [new(0, 0, 1920, 1080, 100, true)],
            [new IconPosition("id-1", "Icon", 10, 20)],
            Previous: null);
}

[Collection(DesktopCollectionDefinition.Name)]
public sealed class TrayIconTests(AppFixture appFixture)
{
    private static readonly MonitorInfo[] SingleMonitor = [new(0, 0, 1920, 1080, 100, true)];
    private static readonly MonitorInfo[] DualMonitors =
    [
        new(0, 0, 1920, 1080, 100, true),
        new(1920, 0, 2560, 1440, 100, false),
    ];

    [Fact]
    public void TrayIconSelectsSmallIconSizeMatchesDarkAndLightAssetsAndSwitchesLive()
    {
        using var testKey = new IsolatedTaskbarKey();
        testKey.SetDword(0); // dark taskbar

        appFixture.Invoke(() =>
        {
            var theme = new TaskbarTheme(testKey.Path);
            Assert.False(theme.IsLight);

            var trayIcon = new TrayIcon(theme);
            try
            {
                Assert.True(trayIcon.IsVisible);
                Size expectedSize = SystemInformation.SmallIconSize;
                Assert.Equal(expectedSize, trayIcon.IconSize);

                // Load expected icons independently from the source assets on disk so swapping TrayIcon's mapping fails.
                string assetsDir = Path.Combine(FindRepoRoot(), "assets", "icon");
                using var expectedDark = new Icon(Path.Combine(assetsDir, "berth-tray-dark.ico"), expectedSize);
                using var expectedLight = new Icon(Path.Combine(assetsDir, "berth-tray-light.ico"), expectedSize);

                // Verify dark and light assets actually differ visually and the initial icon matches dark.
                Assert.False(IconsHaveIdenticalPixels(expectedDark, expectedLight), "Dark and light tray icons must differ.");
                Assert.True(IconsHaveIdenticalPixels(trayIcon.NotifyIcon.Icon!, expectedDark), "Dark taskbar must load berth-tray-dark.ico.");

                IntPtr darkHandle = trayIcon.IconHandle;

                // Flip registry key to light taskbar and notify TaskbarTheme directly.
                testKey.SetDword(1);
                theme.OnSettingChange("ImmersiveColorSet");

                Assert.NotEqual(darkHandle, trayIcon.IconHandle);
                Assert.Equal(expectedSize, trayIcon.IconSize);
                Assert.True(IconsHaveIdenticalPixels(trayIcon.NotifyIcon.Icon!, expectedLight), "Light taskbar must load berth-tray-light.ico.");
            }
            finally
            {
                trayIcon.Dispose();
                Assert.False(trayIcon.IsVisible);
                trayIcon.Dispose(); // idempotent
            }
        });
    }

    [Fact]
    public void TrayIconRefreshesTooltipAfterSaveAndMonitorChangeAndFiltersClickButtons()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(SingleMonitor);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "Icon", 10, 20));
        var gate = new OperationLock(paths, TimeSpan.FromSeconds(1));
        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var clock = new FixedClock(fixedNow);
        var log = new Log(paths, clock);
        var service = new LayoutService(desktop, monitors, store, paths, gate, log, clock);

        using var testKey = new IsolatedTaskbarKey();
        testKey.SetDword(0);

        appFixture.Invoke(() =>
        {
            var theme = new TaskbarTheme(testKey.Path);
            var trayIcon = new TrayIcon(theme, initialActive: null, fixedNow, TimeZoneInfo.Utc);
            try
            {
                Assert.Equal("Berth: no layout for this monitor arrangement", trayIcon.TooltipText);

                // Save a layout for SingleMonitor and refresh tooltip -> transitions to "saved" form.
                OperationResult saved = service.Save();
                Assert.Equal(OperationKind.Saved, saved.Kind);
                trayIcon.RefreshTooltip(store, monitors, clock, TimeZoneInfo.Utc);
                Assert.Equal($"Berth: {saved.LayoutName}, saved today, 09:14", trayIcon.TooltipText);

                // Switch to an arrangement with no saved layout -> transitions back to "no layout" form.
                trayIcon.RefreshTooltip(store, new FakeMonitors(DualMonitors), clock, TimeZoneInfo.Utc);
                Assert.Equal("Berth: no layout for this monitor arrangement", trayIcon.TooltipText);

                // Drive the real wired NotifyIcon MouseClick / MouseDoubleClick events:
                // Left click => 1; Left double-click (MouseClick + MouseDoubleClick) => 1; Right/Middle => 0.
                int openCalls = 0;
                trayIcon.OpenRequested += () => openCalls++;

                InvokeNotifyIconMouseEvent(trayIcon.NotifyIcon, "OnMouseClick", MouseButtons.Right);
                InvokeNotifyIconMouseEvent(trayIcon.NotifyIcon, "OnMouseDoubleClick", MouseButtons.Right);
                InvokeNotifyIconMouseEvent(trayIcon.NotifyIcon, "OnMouseClick", MouseButtons.Middle);
                InvokeNotifyIconMouseEvent(trayIcon.NotifyIcon, "OnMouseDoubleClick", MouseButtons.Middle);
                Assert.Equal(0, openCalls);

                // Single left click:
                InvokeNotifyIconMouseEvent(trayIcon.NotifyIcon, "OnMouseClick", MouseButtons.Left);
                Assert.Equal(1, openCalls);

                // Left double-click sequence in WinForms (MouseClick on first up + MouseDoubleClick on second down):
                InvokeNotifyIconMouseEvent(trayIcon.NotifyIcon, "OnMouseClick", MouseButtons.Left);
                InvokeNotifyIconMouseEvent(trayIcon.NotifyIcon, "OnMouseDoubleClick", MouseButtons.Left);
                Assert.Equal(2, openCalls);
            }
            finally
            {
                trayIcon.Dispose();
                Assert.False(trayIcon.IsVisible);
            }
        });
    }

    [Fact]
    public void BackgroundAppDomainCrashDisposesTrayIconBeforeProcessExit()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var log = new Log(paths, TimeProvider.System);
        using var testKey = new IsolatedTaskbarKey();
        testKey.SetDword(0);

        bool? visibleAtExit = null;
        int? exitCode = null;
        TrayIcon? icon = null;

        appFixture.Invoke(() =>
        {
            var theme = new TaskbarTheme(testKey.Path);
            icon = new TrayIcon(theme);
            Assert.True(icon.IsVisible);
        });

        try
        {
            var sources = new FakeCrashSources();
            // Exercise the REAL TrayApp.CreateCrashExitAction production callback from a non-UI thread.
            Action<int> crashExit = TrayApp.CreateCrashExitAction(
                () => appFixture.App.Dispatcher,
                () => icon,
                code =>
                {
                    visibleAtExit = icon?.IsVisible;
                    exitCode = code;
                });

            var handler = new CrashHandler(log, _ => true, exitProcess: crashExit);
            using IDisposable attachment = handler.Attach(sources);
            sources.RaiseAppDomain(new InvalidOperationException("Background crash"));

            Assert.Equal(1, exitCode);
            Assert.False(visibleAtExit);
            Assert.False(icon!.IsVisible);
        }
        finally
        {
            appFixture.Invoke(() => icon?.Dispose());
        }
    }

    [Fact]
    public void PrimaryStartupCreatesVisibleTrayIconWithSavedTooltipAndReconcilesThemeAfterSubscribe()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var fixedNow = new DateTimeOffset(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
        var savedLayout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From(SingleMonitor),
            "Docked",
            fixedNow,
            [.. SingleMonitor],
            [new IconPosition("id-1", "Icon", 10, 20)],
            Previous: null);
        store.Save(savedLayout);

        using var testKey = new IsolatedTaskbarKey();
        testKey.SetDword(0); // Start dark; child flips to light right before subscribing to UserPreferenceChanged.

        string reportFile = Path.Combine(data.Root, "tray-startup-report.txt");
        var result = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.PrimaryStartupTrayScenario,
            data.Root,
            "iso-tray-" + Guid.NewGuid().ToString("N"),
            regPath: testKey.Path,
            reportFile: reportFile);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(reportFile), "Primary startup scenario did not write its report.");
        string[] lines = File.ReadAllLines(reportFile);
        Assert.Contains("initialVisible=True", lines);
        Assert.Contains("initialTooltip=Berth: Docked, saved today, 09:14", lines);
        Assert.Contains("reconciledIsLight=True", lines);
        Assert.Contains("handleChanged=True", lines);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Berth.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate Berth.slnx above test output.");
    }

    private sealed class FakeCrashSources : ICrashSources
    {
        public event Action<Exception, Action>? DispatcherUnhandled
        {
            add { }
            remove { }
        }

        public event Action<object?>? AppDomainUnhandled;

        public event Action<AggregateException, Action>? UnobservedTask
        {
            add { }
            remove { }
        }

        public void RaiseAppDomain(object? payload) => AppDomainUnhandled?.Invoke(payload);

        public void Dispose()
        {
        }
    }

    private static void InvokeNotifyIconMouseEvent(NotifyIcon icon, string methodName, MouseButtons button)
    {
        MethodInfo method = typeof(NotifyIcon).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"NotifyIcon.{methodName} not found.");
        method.Invoke(icon, [new MouseEventArgs(button, clicks: 1, x: 0, y: 0, delta: 0)]);
    }

    private static bool IconsHaveIdenticalPixels(Icon a, Icon b)
    {
        using Bitmap ba = a.ToBitmap();
        using Bitmap bb = b.ToBitmap();
        if (ba.Width != bb.Width || ba.Height != bb.Height)
        {
            return false;
        }

        for (int y = 0; y < ba.Height; y++)
        {
            for (int x = 0; x < ba.Width; x++)
            {
                if (ba.GetPixel(x, y) != bb.GetPixel(x, y))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class IsolatedTaskbarKey : IDisposable
    {
        public string Path { get; } = $@"Software\Berth.Tests\TrayIcon-{Guid.NewGuid():N}";

        public void SetDword(int value)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Path, writable: true);
            key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(Path, throwOnMissingSubKey: false);
        }
    }
}
