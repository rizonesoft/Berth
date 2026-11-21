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

        // Three equal 1920 x 1080 monitors side by side with the primary on the right (tightest horizontal label case).
        MonitorInfo[] threeWide =
        [
            new(0, 0, 1920, 1080, 100, false),
            new(1920, 0, 1920, 1080, 100, false),
            new(3840, 0, 1920, 1080, 100, true),
        ];
        SaveLayout(store, threeWide, "Triple", new IconPosition("id-1", "One", 500, 400));

        var monitors = new FakeMonitors(threeWide);
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
                WpfPoint wideVisualCenter = MeasureFirstMarkerCenterInViewport(map);

                // Resize down to minimum window size 760 x 520 and verify at both 96 DPI (100%) and 144 DPI (150%).
                RenderMeasureAndArrange(window, 760, 520, dpi: 96);
                double narrowScale = map.CurrentGeometry.Scale;
                WpfPoint narrowVisualCenter96 = MeasureFirstMarkerCenterInViewport(map);
                MapPoint expectedNarrow = map.CurrentGeometry.ToPanel(500, 400);

                Assert.True(map.MapCard.ActualHeight >= 220d, $"Expected MapCard >= 220 DIP, got {map.MapCard.ActualHeight}.");
                Assert.True(narrowScale < wideScale, $"Expected scale to shrink on narrower window ({narrowScale} < {wideScale}).");
                Assert.NotEqual(wideVisualCenter.X, narrowVisualCenter96.X);
                Assert.InRange(Math.Abs(narrowVisualCenter96.X - expectedNarrow.X), 0d, 0.5d);
                Assert.InRange(Math.Abs(narrowVisualCenter96.Y - expectedNarrow.Y), 0d, 0.5d);

                // Verify rendered alignment at genuine 150% DPI (144 DPI via SetRootDpi) as well.
                try
                {
                    VisualTreeHelper.SetRootDpi(window.RootGrid, new DpiScale(1.5d, 1.5d));
                    RenderMeasureAndArrange(window, 760, 520, dpi: 144);
                    Assert.Equal(1.5d, VisualTreeHelper.GetDpi(map.Surface).DpiScaleX, precision: 3);
                    WpfPoint narrowVisualCenter144 = MeasureFirstMarkerCenterInViewport(map);
                    Assert.InRange(Math.Abs(narrowVisualCenter144.X - expectedNarrow.X), 0d, 0.5d);
                    Assert.InRange(Math.Abs(narrowVisualCenter144.Y - expectedNarrow.Y), 0d, 0.5d);
                }
                finally
                {
                    VisualTreeHelper.SetRootDpi(window.RootGrid, new DpiScale(1.0d, 1.0d));
                }

                // All three labels must be inside the padded viewport and MUST NOT intersect one another.
                Assert.Equal(3, map.Surface.RenderedLabels.Count);
                for (int i = 0; i < map.Surface.RenderedLabels.Count; i++)
                {
                    RenderedMonitorLabel a = map.Surface.RenderedLabels[i];
                    Assert.True(a.Bounds.Left >= 0d, $"Label '{a.Text}' clipped left at {a.Bounds.Left}.");
                    Assert.True(a.Bounds.Top >= 0d && a.Bounds.Bottom <= map.MapViewport.ActualHeight,
                        $"Label '{a.Text}' outside vertical viewport: {a.Bounds}.");
                    Assert.True(
                        a.Bounds.Right <= map.MapViewport.ActualWidth - 24d + 0.5d,
                        $"Label '{a.Text}' clipped right at {a.Bounds.Right} > {map.MapViewport.ActualWidth - 24d}.");
                    for (int j = i + 1; j < map.Surface.RenderedLabels.Count; j++)
                    {
                        RenderedMonitorLabel b = map.Surface.RenderedLabels[j];
                        Assert.False(
                            a.Bounds.IntersectsWith(b.Bounds),
                            $"Labels '{a.Text}' ({a.Bounds}) and '{b.Text}' ({b.Bounds}) overlapped at 760 DIP.");
                    }
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

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            var berthApp = (Berth.App.BerthApplication)System.Windows.Application.Current;
            Berth.App.ColorTheme originalTheme = berthApp.CurrentColorTheme;
            try
            {
                // Switch theme on the SAME control WITHOUT calling any manual refresh helper:
                // DependencyProperty DynamicResource bindings must match all 4 expected resources in every theme!
                berthApp.ApplyColorTheme(Berth.App.ColorTheme.Light);
                byte[] lightSurface = RenderSurfaceBytes(window);
                AssertSurfaceBrushesMatchTheme(window.PositionMapControl.Surface);

                berthApp.ApplyColorTheme(Berth.App.ColorTheme.Dark);
                byte[] darkSurface = RenderSurfaceBytes(window);
                AssertSurfaceBrushesMatchTheme(window.PositionMapControl.Surface);
                Assert.False(lightSurface.AsSpan().SequenceEqual(darkSurface));

                berthApp.ApplyColorTheme(Berth.App.ColorTheme.HighContrast);
                byte[] hcSurface = RenderSurfaceBytes(window);
                AssertSurfaceBrushesMatchTheme(window.PositionMapControl.Surface);
                Assert.False(darkSurface.AsSpan().SequenceEqual(hcSurface));
            }
            finally
            {
                berthApp.ApplyColorTheme(originalTheme);
                window.CloseForShutdown();
            }
        });
    }

    private static void AssertSurfaceBrushesMatchTheme(MapSurface surface)
    {
        Assert.Same(System.Windows.Application.Current.FindResource("Berth.MapGrid"), surface.GridBrush);
        Assert.Same(System.Windows.Application.Current.FindResource("Berth.MonitorFill"), surface.MonitorFill);
        Assert.Same(System.Windows.Application.Current.FindResource("Berth.MonitorStroke"), surface.MonitorStroke);
        Assert.Same(System.Windows.Application.Current.FindResource("Berth.TextSecondary"), surface.LabelBrush);
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

    private static void RenderMeasureAndArrange(MainWindow window, double width = 980, double height = 640, double dpi = 96)
    {
        window.RootGrid.Measure(new WpfSize(width, height));
        window.RootGrid.Arrange(new Rect(0, 0, width, height));
        window.RootGrid.UpdateLayout();

        // Trigger OnRender on MapSurface off-screen so RenderedMonitorRects and RenderedLabels are populated.
        int pxW = (int)Math.Round(width * dpi / 96d);
        int pxH = (int)Math.Round(height * dpi / 96d);
        var rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(window.RootGrid);
    }

    private static WpfPoint MeasureFirstMarkerCenterInViewport(PositionMap map)
    {
        WpfListBoxItem container = RealizeItems(map.MarkersList)[0];
        Border visual = Assert.IsType<Border>(FindDescendant<Border>(container, "MarkerVisual"));
        return visual.TranslatePoint(new WpfPoint(visual.ActualWidth / 2d, visual.ActualHeight / 2d), map.MapViewport);
    }

    private static byte[] RenderSurfaceBytes(MainWindow window)
    {
        window.RootGrid.Measure(new WpfSize(980, 640));
        window.RootGrid.Arrange(new Rect(0, 0, 980, 640));
        window.RootGrid.UpdateLayout();
        MapSurface surface = window.PositionMapControl.Surface;
        int w = Math.Max(1, (int)Math.Round(surface.ActualWidth));
        int h = Math.Max(1, (int)Math.Round(surface.ActualHeight));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(surface);
        byte[] pixels = new byte[w * h * 4];
        rtb.CopyPixels(pixels, w * 4, 0);
        return pixels;
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
