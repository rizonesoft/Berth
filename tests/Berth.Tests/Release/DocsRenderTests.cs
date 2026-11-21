using System.Windows;
using System.Windows.Media.Imaging;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Release;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "Docs")]
public sealed class DocsRenderTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonPrimary = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonSecondary = new(2560, 0, 1920, 1080, 100, false);
    private static readonly MonitorInfo MonLaptop = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo MonUltrawide = new(0, 0, 3440, 1440, 100, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 21, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task RenderMainWindowDocsScreenshotsInLightAndDark()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        IconPosition[] dockedSaved = BuildSampleIcons(42);
        IconPosition[] laptopSaved = BuildSampleIcons(28, monitor2Count: 0);
        IconPosition[] ultrawideSaved = BuildSampleIcons(42, monitor2Count: 0);

        string dockedKey = ArrangementKey.From([MonPrimary, MonSecondary]);
        string laptopKey = ArrangementKey.From([MonLaptop]);
        string ultrawideKey = ArrangementKey.From([MonUltrawide]);

        store.Save(new Layout(Layout.CurrentSchemaVersion, dockedKey, "Docked", FixedNow, [MonPrimary, MonSecondary], [.. dockedSaved], null));
        store.Save(new Layout(Layout.CurrentSchemaVersion, laptopKey, "Laptop", FixedNow.AddDays(-2), [MonLaptop], [.. laptopSaved], null));
        store.Save(new Layout(Layout.CurrentSchemaVersion, ultrawideKey, "Studio ultrawide", FixedNow.AddDays(-5), [MonUltrawide], [.. ultrawideSaved], null));

        // 39 in place, 2 moved, 1 missing so the screenshot shows a realistic live comparison.
        var liveIcons = new List<IconPosition>(dockedSaved.Length);
        for (int i = 0; i < dockedSaved.Length; i++)
        {
            if (i == 12)
            {
                continue; // 1 missing
            }

            if (i is 5 or 30)
            {
                liveIcons.Add(dockedSaved[i] with { X = dockedSaved[i].X + 180, Y = dockedSaved[i].Y + 90 });
            }
            else
            {
                liveIcons.Add(dockedSaved[i]);
            }
        }

        var monitors = new FakeMonitors(MonPrimary, MonSecondary);
        var desktop = new FakeDesktopIcons([.. liveIcons]);
        var clock = new FixedClock(FixedNow);
        var log = new Log(paths, clock);
        var settingsStore = new SettingsStore(paths);
        _ = settingsStore.Update(s => s with { RestoreAutomatically = true });
        var runValue = new RunValue(@"Software\Berth.Tests\DocsRender");
        var settingChanges = new SettingChanges(settingsStore, log);
        var service = new LayoutService(desktop, monitors, store, paths, new OperationLock(paths, TimeSpan.FromSeconds(5)), log, clock);
        var vm = new MainViewModel(
            store,
            desktop,
            monitors,
            clock,
            marshalToUiAsync: action =>
            {
                appFixture.Invoke(action);
                return Task.CompletedTask;
            },
            service: service,
            timeZone: TimeZoneInfo.Utc,
            log: log,
            settingsStore: settingsStore,
            runValue: runValue,
            settingChanges: settingChanges);

        await vm.RefreshAsync();

        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("Docked", vm.Selected?.Name);
        Assert.Equal(39, vm.Comparison?.InPlace);
        Assert.Equal(2, vm.Comparison?.Moved);
        Assert.Equal(1, vm.Comparison?.Missing);
        Assert.NotNull(vm.Settings);
        Assert.True(vm.Settings!.RestoreAutomatically);

        string outputDir = ResolveOutputDirectory(data.Root);
        string lightPath = Path.Combine(outputDir, "main-window-light.png");
        string darkPath = Path.Combine(outputDir, "main-window-dark.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                window.RootGrid.Measure(new WpfSize(980, 640));
                window.RootGrid.Arrange(new Rect(0, 0, 980, 640));
                window.RootGrid.UpdateLayout();

                WindowRenderer.Render(window.RootGrid, 980, 640, lightPath, RenderTheme.Light);
                WindowRenderer.Render(window.RootGrid, 980, 640, darkPath, RenderTheme.Dark);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });

        byte[] lightBytes = AssertValid980x640Png(lightPath);
        byte[] darkBytes = AssertValid980x640Png(darkPath);
        Assert.False(lightBytes.AsSpan().SequenceEqual(darkBytes), "Light and dark screenshots must differ.");
    }

    private static IconPosition[] BuildSampleIcons(int total, int monitor2Count = 12)
    {
        int mon1Count = total - monitor2Count;
        var icons = new List<IconPosition>(total);
        for (int i = 0; i < mon1Count; i++)
        {
            int col = i / 6;
            int row = i % 6;
            icons.Add(new IconPosition($"m1-{i}", $"App {i + 1}", 120 + (col * 220), 120 + (row * 190)));
        }

        for (int i = 0; i < monitor2Count; i++)
        {
            int col = i / 4;
            int row = i % 4;
            icons.Add(new IconPosition($"m2-{i}", $"Doc {i + 1}", 2680 + (col * 240), 120 + (row * 210)));
        }

        return [.. icons];
    }

    private static string ResolveOutputDirectory(string fallbackTempDir)
    {
        string? envDir = Environment.GetEnvironmentVariable("BERTH_DOCS_IMAGE_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            Directory.CreateDirectory(envDir);
            return envDir;
        }

        // Default to the test's own temporary folder so running dotnet test never overwrites tracked docs/images/.
        string outDir = Path.Combine(fallbackTempDir, "docs-images");
        Directory.CreateDirectory(outDir);
        return outDir;
    }

    private static byte[] AssertValid980x640Png(string path)
    {
        Assert.True(File.Exists(path), $"Expected screenshot at {path}");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 1024, $"Screenshot at {path} is unexpectedly small ({bytes.Length} bytes).");
        using var ms = new MemoryStream(bytes);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Single(decoder.Frames);
        Assert.Equal(980, decoder.Frames[0].PixelWidth);
        Assert.Equal(640, decoder.Frames[0].PixelHeight);
        return bytes;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
