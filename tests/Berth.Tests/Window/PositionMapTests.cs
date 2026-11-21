using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using Berth.Window.Map;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class PositionMapTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonDualA = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonDualB = new(2560, 0, 1920, 1080, 100, false);
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo MonPortraitPrimary = new(2560, 0, 1080, 1920, 100, true);
    private static readonly MonitorInfo MonTop = new(0, -1080, 1920, 1080, 100, false);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task DrawsSelectedLayoutsMonitorsLabelsAndSavedPositionsAndSwitchesToUnsavedLiveMonitors()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        // Save a dual-monitor layout with a moved icon (saved at 100, 100; live desktop has it at 1800, 900).
        SaveLayout(store, [MonDualA, MonDualB], "Docked", new IconPosition("id-1", "Notes.txt", 100, 100));

        // Connected arrangement is a DIFFERENT single monitor so "Docked" is Other and row 0 is Unsaved.
        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "Notes.txt", 1800, 900));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                // 1. Select "Docked" (Other): map must draw the TWO stored monitors, NOT the 1 connected monitor,
                //    and plot the icon at its SAVED position (100, 100).
                vm.Selected = vm.Rows.Single(r => r.Name == "Docked");
                RenderMeasureAndArrange(window);
                PositionMap map = window.PositionMapControl;

                Assert.Equal(2, map.Surface.RenderedMonitorRects.Count);
                Assert.Equal(
                    ["1 \u00B7 2560 \u00D7 1440 \u00B7 primary", "2 \u00B7 1920 \u00D7 1080"],
                    map.Surface.RenderedLabels.Select(l => l.Text));
                Assert.Single(map.MarkerItems);
                MapPoint expectedSaved = map.CurrentGeometry.ToPanel(100, 100);
                Assert.InRange(Math.Abs(map.MarkerItems[0].CenterX - expectedSaved.X), 0d, 0.5d);
                Assert.InRange(Math.Abs(map.MarkerItems[0].CenterY - expectedSaved.Y), 0d, 0.5d);

                // 2. Switch to "This arrangement" (Unsaved): map must draw the ONE live monitor and live position (1800, 900).
                vm.Selected = vm.Rows.Single(r => r.IsUnsaved);
                RenderMeasureAndArrange(window);

                Assert.Single(map.Surface.RenderedMonitorRects);
                Assert.Equal(["1 \u00B7 1920 \u00D7 1080 \u00B7 primary"], map.Surface.RenderedLabels.Select(l => l.Text));
                Assert.Single(map.MarkerItems);
                MapPoint expectedLive = map.CurrentGeometry.ToPanel(1800, 900);
                Assert.InRange(Math.Abs(map.MarkerItems[0].CenterX - expectedLive.X), 0d, 0.5d);
                Assert.InRange(Math.Abs(map.MarkerItems[0].CenterY - expectedLive.Y), 0d, 0.5d);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    [Fact]
    public async Task Plots42IconsAtExactGeometryPositionsWith19PxTargetsAndReadingOrderZIndexOnOverlap()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        var icons = new List<IconPosition>(42);
        for (int i = 0; i < 40; i++)
        {
            int col = i % 8;
            int row = i / 8;
            icons.Add(new IconPosition($"id-{i}", $"File{i}.txt", 80 + (col * 280), 80 + (row * 220)));
        }

        // Two off-screen icons that clamp to the exact same bottom-right corner so they overlap completely!
        icons.Add(new IconPosition("id-off-1", "OffFirst.txt", 9000, 5000));
        icons.Add(new IconPosition("id-off-2", "OffSecond.txt", 9500, 5200));

        SaveLayout(store, [MonDualA, MonDualB], "Docked", [.. icons]);
        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var desktop = new FakeDesktopIcons([.. icons]);
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                RenderMeasureAndArrange(window);
                PositionMap map = window.PositionMapControl;
                Assert.Equal(42, map.MarkersList.Items.Count);

                WpfListBoxItem[] containers = RealizeItems(map.MarkersList);
                Assert.Equal(42, containers.Length);

                // First item center must match MapGeometry.ToPanel(saved) within 0.5 px, with 19x19 target & 11x11 visual.
                MapMarkerItem first = map.MarkerItems[0];
                MapPoint expected = map.CurrentGeometry.ToPanel(first.Icon.Saved.X, first.Icon.Saved.Y);
                Assert.InRange(Math.Abs(first.CenterX - expected.X), 0d, 0.5d);
                Assert.InRange(Math.Abs(first.CenterY - expected.Y), 0d, 0.5d);
                Assert.Equal(19d, containers[0].ActualWidth, precision: 2);
                Assert.Equal(19d, containers[0].ActualHeight, precision: 2);

                Border? markerVisual = FindDescendant<Border>(containers[0], "MarkerVisual");
                Assert.NotNull(markerVisual);
                Assert.Equal(11d, markerVisual.ActualWidth, precision: 2);
                Assert.Equal(11d, markerVisual.ActualHeight, precision: 2);

                // Hit-test the shared clamped corner of the two overlapping off-screen icons: later reading-order item wins!
                MapMarkerItem earlierOverlap = map.MarkerItems[40];
                MapMarkerItem laterOverlap = map.MarkerItems[41];
                Assert.True(laterOverlap.ZIndex > earlierOverlap.ZIndex);
                Assert.Equal(earlierOverlap.CenterX, laterOverlap.CenterX, precision: 2);
                Assert.Equal(earlierOverlap.CenterY, laterOverlap.CenterY, precision: 2);

                HitTestResult? hit = VisualTreeHelper.HitTest(
                    map.MarkersList,
                    new WpfPoint(laterOverlap.CenterX, laterOverlap.CenterY));
                Assert.NotNull(hit);
                WpfListBoxItem? hitContainer = FindAncestor<WpfListBoxItem>(hit.VisualHit);
                Assert.NotNull(hitContainer);
                Assert.Same(laterOverlap, hitContainer.DataContext);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    [Fact]
    public async Task ResizesProportionallyKeeps220PxMinCardHeightAndKeepsPortraitAndStackedLabelsInsideCard()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        // Rightmost narrow portrait primary + vertically stacked top monitor.
        MonitorInfo[] arrangement = [MonTop, MonSingle, MonPortraitPrimary];
        SaveLayout(store, arrangement, "Studio", new IconPosition("id-1", "One", 500, 400));

        var monitors = new FakeMonitors(arrangement);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 500, 400));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                RenderMeasureAndArrange(window, 980, 640);
                PositionMap map = window.PositionMapControl;
                double wideScale = map.CurrentGeometry.Scale;
                double wideCenterX = map.MarkerItems[0].CenterX;

                // Resize down to minimum window size 760 x 520.
                RenderMeasureAndArrange(window, 760, 520);
                double narrowScale = map.CurrentGeometry.Scale;
                double narrowCenterX = map.MarkerItems[0].CenterX;

                Assert.True(map.MapCard.ActualHeight >= 220d, $"Expected MapCard >= 220 DIP, got {map.MapCard.ActualHeight}.");
                Assert.True(narrowScale < wideScale, $"Expected scale to shrink on narrower window ({narrowScale} < {wideScale}).");
                Assert.NotEqual(wideCenterX, narrowCenterX);

                // Every rendered label (including rightmost portrait primary) must fit inside the padded viewport.
                Assert.Equal(3, map.Surface.RenderedLabels.Count);
                foreach (RenderedMonitorLabel label in map.Surface.RenderedLabels)
                {
                    Assert.True(label.Bounds.Left >= 0d, $"Label '{label.Text}' clipped left at {label.Bounds.Left}.");
                    Assert.True(
                        label.Bounds.Right <= map.MapViewport.ActualWidth - 24d + 0.5d,
                        $"Label '{label.Text}' clipped right at {label.Bounds.Right} > {map.MapViewport.ActualWidth - 24d}.");
                }
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    [Fact]
    public async Task LiveThemeSwitchRepaintsSameLoadedPositionMapInLightDarkAndHighContrast()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonDualA, MonDualB], "Docked", new IconPosition("id-1", "One", 200, 200));

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 200, 200));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        string lightPath = Path.Combine(data.Root, "live-light.png");
        string darkPath = Path.Combine(data.Root, "live-dark.png");
        string hcPath = Path.Combine(data.Root, "live-hc.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            var berthApp = (Berth.App.BerthApplication)System.Windows.Application.Current;
            Berth.App.ColorTheme originalTheme = berthApp.CurrentColorTheme;
            try
            {
                WindowRenderer.Render(window.RootGrid, 980, 640, lightPath, RenderTheme.Light);
                window.PositionMapControl.RefreshThemeVisuals();
                WindowRenderer.Render(window.RootGrid, 980, 640, darkPath, RenderTheme.Dark);
                berthApp.ApplyColorTheme(Berth.App.ColorTheme.HighContrast);
                window.PositionMapControl.RefreshThemeVisuals();
                WindowRenderer.Render(window.RootGrid, 980, 640, hcPath, theme: null);
            }
            finally
            {
                berthApp.ApplyColorTheme(originalTheme);
                window.CloseForShutdown();
            }
        });

        byte[] lightBytes = File.ReadAllBytes(lightPath);
        byte[] darkBytes = File.ReadAllBytes(darkPath);
        byte[] hcBytes = File.ReadAllBytes(hcPath);
        Assert.False(lightBytes.AsSpan().SequenceEqual(darkBytes));
        Assert.False(darkBytes.AsSpan().SequenceEqual(hcBytes));
    }

    [Fact]
    [Trait("Category", "Render")]
    public async Task RendersPositionMapWithTwoMonitorsAndIconsInLightAndDark()
    {
        string outputDir = ResolveRenderOutputDirectory();
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        IconPosition[] icons =
        [
            new("id-1", "Recycle Bin", 76, 40),
            new("id-2", "Notes.txt", 76, 160),
            new("id-3", "Projects", 76, 280),
            new("id-4", "Screenshot.png", 1436, 702),
            new("id-5", "Reference.pdf", 2720, 120),
            new("id-6", "Terminal.lnk", 2720, 260),
        ];
        SaveLayout(store, [MonDualA, MonDualB], "Docked", icons);

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var desktop = new FakeDesktopIcons(icons);
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        string lightPath = Path.Combine(outputDir, "map-monitors-light.png");
        string darkPath = Path.Combine(outputDir, "map-monitors-dark.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                WindowRenderer.Render(window.RootGrid, 980, 640, lightPath, RenderTheme.Light);
                WindowRenderer.Render(window.RootGrid, 980, 640, darkPath, RenderTheme.Dark);

                PositionMap map = window.PositionMapControl;
                Assert.Equal(2, map.Surface.RenderedMonitorRects.Count);
                Assert.Equal(
                    ["1 \u00B7 2560 \u00D7 1440 \u00B7 primary", "2 \u00B7 1920 \u00D7 1080"],
                    map.Surface.RenderedLabels.Select(l => l.Text));
                Assert.Equal(6, map.MarkerItems.Count);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });

        AssertValidPng(lightPath);
        AssertValidPng(darkPath);
    }

    private MainViewModel CreateViewModel(LayoutStore store, IDesktopIcons desktop, IMonitors monitors) =>
        new(
            store,
            desktop,
            monitors,
            new FixedClock(FixedNow),
            marshalToUiAsync: action =>
            {
                appFixture.Invoke(action);
                return Task.CompletedTask;
            },
            timeZone: TimeZoneInfo.Utc);

    private static void SaveLayout(LayoutStore store, MonitorInfo[] monitors, string name, params IconPosition[] icons)
    {
        string key = ArrangementKey.From(monitors);
        store.Save(new Layout(Layout.CurrentSchemaVersion, key, name, FixedNow, [.. monitors], [.. icons], null));
    }

    private static void RenderMeasureAndArrange(MainWindow window, double width = 980, double height = 640)
    {
        window.RootGrid.Measure(new WpfSize(width, height));
        window.RootGrid.Arrange(new Rect(0, 0, width, height));
        window.RootGrid.UpdateLayout();

        // Trigger OnRender on MapSurface off-screen so RenderedMonitorRects and RenderedLabels are populated.
        var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window.RootGrid);
    }

    private static WpfListBoxItem[] RealizeItems(System.Windows.Controls.ListBox listBox)
    {
        var result = new WpfListBoxItem[listBox.Items.Count];
        for (int i = 0; i < listBox.Items.Count; i++)
        {
            result[i] = Assert.IsType<WpfListBoxItem>(listBox.ItemContainerGenerator.ContainerFromIndex(i));
        }

        return result;
    }

    private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            if (FindDescendant<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static string ResolveRenderOutputDirectory()
    {
        string? envDir = Environment.GetEnvironmentVariable("BERTH_RENDER_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            Directory.CreateDirectory(envDir);
            return envDir;
        }

        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Berth.slnx")))
            {
                string outDir = Path.Combine(dir, "build", "renders");
                Directory.CreateDirectory(outDir);
                return outDir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Berth.slnx above " + AppContext.BaseDirectory);
    }

    private static void AssertValidPng(string path)
    {
        Assert.True(File.Exists(path), $"Expected render output at {path}");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0);
        using var ms = new MemoryStream(bytes);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Single(decoder.Frames);
        Assert.Equal(980, decoder.Frames[0].PixelWidth);
        Assert.Equal(640, decoder.Frames[0].PixelHeight);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
