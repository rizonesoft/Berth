using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using WpfBrush = System.Windows.Media.Brush;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class LayoutsPaneTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo MonDualA = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonDualB = new(2560, 0, 1920, 1080, 100, false);
    private static readonly MonitorInfo MonThird = new(0, 0, 3840, 2160, 150, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonDualA, MonDualB], "Docked", 42);
        SaveLayout(store, [MonSingle], "Laptop", 18);
        SaveLayout(store, [MonThird], "Projector", 12);

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                MeasureAndArrange(window);
                LayoutsPane pane = Assert.IsType<LayoutsPane>(window.LayoutsHost.Content);
                Assert.Equal("Layouts", pane.HeadingText.Text);
                Assert.Equal(3, pane.LayoutsList.Items.Count);

                WpfListBoxItem[] items = RealizeItems(pane.LayoutsList);
                Assert.Equal(3, items.Length);
                Assert.Equal(["Docked", "Laptop", "Projector"], items.Select(AutomationProperties.GetName));
                AssertRealizedRowTexts(
                    items,
                    ("Docked", "2 monitors \u00B7 42 icons"),
                    ("Laptop", "1 monitor \u00B7 18 icons"),
                    ("Projector", "1 monitor \u00B7 12 icons"));

                // Active badge element MUST exist ONLY in the active row (index 0) and read "Active".
                Border?[] badges = [.. items.Select(i => FindDescendant<Border>(i, "ActiveBadge"))];
                Assert.NotNull(badges[0]);
                Assert.Null(badges[1]);
                Assert.Null(badges[2]);
                TextBlock? badgeText = FindDescendant<TextBlock>(badges[0]!, "ActiveBadgeText");
                Assert.NotNull(badgeText);
                Assert.Equal("Active", badgeText.Text);

                // Initially row 0 ("Docked") is selected -> marker visible only on row 0, 3 DIP wide, flush at left edge.
                AssertSelectionMarker(items, selectedIndex: 0);

                // Select row 1 ("Laptop") via ListBox -> two-way binding updates vm.Selected and moves marker to row 1.
                pane.LayoutsList.SelectedIndex = 1;
                MeasureAndArrange(window);
                Assert.Equal("Laptop", vm.Selected?.Name);
                AssertSelectionMarker(items, selectedIndex: 1);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    [Fact]
    public async Task TwoWayBoundSelectionSurvivesRefreshAndTransientFolderOutageRecovery()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonSingle], "Docked", 10);
        SaveLayout(store, [MonThird], "Laptop", 5);

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w);
                var pane = (LayoutsPane)w.LayoutsHost.Content;
                pane.LayoutsList.SelectedIndex = 1; // Select non-active "Laptop" through live two-way binding.
                MeasureAndArrange(w);
                Assert.Equal("Laptop", vm.Selected?.Name);
                return w;
            });

            // 1. Refresh while bound -> "Laptop" stays selected with one selected container.
            await vm.RefreshAsync();
            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window);
                var pane = (LayoutsPane)window.LayoutsHost.Content;
                Assert.Equal("Laptop", vm.Selected?.Name);
                WpfListBoxItem[] items = RealizeItems(pane.LayoutsList);
                Assert.Single(items, i => i.IsSelected);
                AssertSelectionMarker(items, selectedIndex: 1);
            });

            // 2. Transient folder-read failure -> Rows clears while bound, then folder recovers -> "Laptop" restored!
            string backup = paths.LayoutsFolder + ".bak";
            Directory.Move(paths.LayoutsFolder, backup);
            File.WriteAllText(paths.LayoutsFolder, "transient failure");
            try
            {
                await vm.RefreshAsync();
                appFixture.Invoke(() =>
                {
                    MeasureAndArrange(window);
                    Assert.NotNull(vm.StoreReadError);
                    Assert.Empty(vm.Rows);
                });
            }
            finally
            {
                File.Delete(paths.LayoutsFolder);
                Directory.Move(backup, paths.LayoutsFolder);
            }

            await vm.RefreshAsync();
            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window);
                var pane = (LayoutsPane)window.LayoutsHost.Content;
                Assert.Equal("Laptop", vm.Selected?.Name);
                WpfListBoxItem[] items = RealizeItems(pane.LayoutsList);
                Assert.Single(items, i => i.IsSelected);
                AssertSelectionMarker(items, selectedIndex: 1);
            });
        }
        finally
        {
            if (window is not null)
            {
                appFixture.Invoke(window.CloseForShutdown);
            }
        }
    }

    [Fact]
    public async Task CorruptCurrentLayoutFileIsOmittedFromListBoxAndExposedOnUnreadable()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonThird], "Office", 8);

        string currentKey = ArrangementKey.From([MonSingle]);
        string corruptPath = paths.LayoutFile(currentKey);
        BerthPaths.EnsureFolderFor(corruptPath);
        File.WriteAllText(corruptPath, "{ corrupt");

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                MeasureAndArrange(window);
                var pane = (LayoutsPane)window.LayoutsHost.Content;
                WpfListBoxItem[] items = RealizeItems(pane.LayoutsList);
                Assert.Single(items);
                Assert.Equal("Office", AutomationProperties.GetName(items[0]));
                Assert.Null(FindDescendant<Border>(items[0], "ActiveBadge"));
                Assert.Contains(vm.Unreadable, u => string.Equals(u.FileName, currentKey + ".json", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    [Fact]
    [Trait("Category", "Render")]
    public async Task RendersAllFiveSpec19LayoutsPaneStatesInLightAndDarkDifferentFromEmptyFrame()
    {
        string outputDir = ResolveRenderOutputDirectory();
        using var baselineData = new TestData();
        string emptyLightPath = Path.Combine(baselineData.Root, "empty-light.png");
        string emptyDarkPath = Path.Combine(baselineData.Root, "empty-dark.png");

        appFixture.Invoke(() =>
        {
            var emptyWin = new MainWindow();
            try
            {
                emptyWin.LayoutsHost.Content = null;
                emptyWin.DetailHost.Content = null;
                WindowRenderer.Render(emptyWin.RootGrid, 980, 640, emptyLightPath, RenderTheme.Light);
                WindowRenderer.Render(emptyWin.RootGrid, 980, 640, emptyDarkPath, RenderTheme.Dark);
            }
            finally
            {
                emptyWin.CloseForShutdown();
            }
        });

        byte[] emptyLightBytes = File.ReadAllBytes(emptyLightPath);
        byte[] emptyDarkBytes = File.ReadAllBytes(emptyDarkPath);

        // State 1: layouts-none (no saved layouts -> sole selected "This arrangement" / "Not saved yet", no badge).
        await RenderAndAssertStateAsync(
            "layouts-none",
            outputDir,
            emptyLightBytes,
            emptyDarkBytes,
            currentMonitors: [MonSingle],
            seedLayouts: _ => { },
            assertBeforeCapture: (vm, items) =>
            {
                Assert.Single(items);
                AssertRealizedRowTexts(items, ("This arrangement", "Not saved yet"));
                Assert.Null(FindDescendant<Border>(items[0], "ActiveBadge"));
                AssertSelectionMarker(items, selectedIndex: 0);
            });

        // State 2: layouts-one (one saved active layout).
        await RenderAndAssertStateAsync(
            "layouts-one",
            outputDir,
            emptyLightBytes,
            emptyDarkBytes,
            currentMonitors: [MonDualA, MonDualB],
            seedLayouts: store => SaveLayout(store, [MonDualA, MonDualB], "Docked", 42),
            assertBeforeCapture: (vm, items) =>
            {
                Assert.Single(items);
                AssertRealizedRowTexts(items, ("Docked", "2 monitors \u00B7 42 icons"));
                Assert.NotNull(FindDescendant<Border>(items[0], "ActiveBadge"));
                AssertSelectionMarker(items, selectedIndex: 0);
            });

        // State 3: layouts-several (active saved layout + two other saved layouts, active selected).
        await RenderAndAssertStateAsync(
            "layouts-several",
            outputDir,
            emptyLightBytes,
            emptyDarkBytes,
            currentMonitors: [MonDualA, MonDualB],
            seedLayouts: store =>
            {
                SaveLayout(store, [MonDualA, MonDualB], "Docked", 42);
                SaveLayout(store, [MonSingle], "Laptop", 18);
                SaveLayout(store, [MonThird], "Projector", 12);
            },
            assertBeforeCapture: (vm, items) =>
            {
                Assert.Equal(3, items.Length);
                AssertRealizedRowTexts(
                    items,
                    ("Docked", "2 monitors \u00B7 42 icons"),
                    ("Laptop", "1 monitor \u00B7 18 icons"),
                    ("Projector", "1 monitor \u00B7 12 icons"));
                Assert.NotNull(FindDescendant<Border>(items[0], "ActiveBadge"));
                Assert.Null(FindDescendant<Border>(items[1], "ActiveBadge"));
                Assert.Null(FindDescendant<Border>(items[2], "ActiveBadge"));
                AssertSelectionMarker(items, selectedIndex: 0);
            });

        // State 4: layouts-active-saved (select a non-active saved row so Active badge stays on row 0 while marker follows row 1).
        await RenderAndAssertStateAsync(
            "layouts-active-saved",
            outputDir,
            emptyLightBytes,
            emptyDarkBytes,
            currentMonitors: [MonDualA, MonDualB],
            seedLayouts: store =>
            {
                SaveLayout(store, [MonDualA, MonDualB], "Docked", 42);
                SaveLayout(store, [MonSingle], "Laptop", 18);
            },
            beforeMeasureSelectIndex: 1,
            assertBeforeCapture: (vm, items) =>
            {
                Assert.Equal(2, items.Length);
                Assert.Equal("Laptop", vm.Selected?.Name);
                AssertRealizedRowTexts(
                    items,
                    ("Docked", "2 monitors \u00B7 42 icons"),
                    ("Laptop", "1 monitor \u00B7 18 icons"));
                Assert.NotNull(FindDescendant<Border>(items[0], "ActiveBadge"));
                Assert.Null(FindDescendant<Border>(items[1], "ActiveBadge"));
                AssertSelectionMarker(items, selectedIndex: 1);
            });

        // State 5: layouts-active-unsaved ("This arrangement" above saved rows, selected, zero Active badges).
        await RenderAndAssertStateAsync(
            "layouts-active-unsaved",
            outputDir,
            emptyLightBytes,
            emptyDarkBytes,
            currentMonitors: [MonSingle],
            seedLayouts: store =>
            {
                SaveLayout(store, [MonDualA, MonDualB], "Docked", 42);
                SaveLayout(store, [MonThird], "Projector", 12);
            },
            assertBeforeCapture: (vm, items) =>
            {
                Assert.Equal(3, items.Length);
                AssertRealizedRowTexts(
                    items,
                    ("This arrangement", "Not saved yet"),
                    ("Docked", "2 monitors \u00B7 42 icons"),
                    ("Projector", "1 monitor \u00B7 12 icons"));
                Assert.All(items, item => Assert.Null(FindDescendant<Border>(item, "ActiveBadge")));
                AssertSelectionMarker(items, selectedIndex: 0);
            });
    }

    [Fact]
    public async Task CtrlClickDeselectionPreservesSelectedRowAndMarkerAcrossRefresh()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonDualA, MonDualB], "Docked", 42);
        SaveLayout(store, [MonSingle], "Laptop", 18);

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w);
                var pane = (LayoutsPane)w.LayoutsHost.Content;
                pane.LayoutsList.SelectedIndex = 1;
                MeasureAndArrange(w);
                Assert.Equal("Laptop", vm.Selected?.Name);

                // Simulate WPF SelectionMode.Single Ctrl-click deselection writing null into SelectedItem.
                pane.LayoutsList.SelectedItem = null;
                MeasureAndArrange(w);

                Assert.Equal("Laptop", vm.Selected?.Name);
                Assert.Same(vm.Rows[1], pane.LayoutsList.SelectedItem);
                WpfListBoxItem[] items = RealizeItems(pane.LayoutsList);
                AssertSelectionMarker(items, selectedIndex: 1);
                return w;
            });

            // A subsequent refresh must still preserve "Laptop" rather than falling back to the active row.
            await vm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                MeasureAndArrange(window!);
                var pane = (LayoutsPane)window!.LayoutsHost.Content;
                Assert.Equal("Laptop", vm.Selected?.Name);
                Assert.Same(vm.Rows[1], pane.LayoutsList.SelectedItem);
                WpfListBoxItem[] items = RealizeItems(pane.LayoutsList);
                AssertSelectionMarker(items, selectedIndex: 1);
            });
        }
        finally
        {
            if (window is not null)
            {
                appFixture.Invoke(() => window.CloseForShutdown());
            }
        }
    }

    private static void AssertRealizedRowTexts(WpfListBoxItem[] items, params (string Name, string Subtitle)[] expected)
    {
        Assert.Equal(expected.Length, items.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            TextBlock? nameText = FindDescendant<TextBlock>(items[i], "RowNameText");
            TextBlock? subtitleText = FindDescendant<TextBlock>(items[i], "RowSubtitleText");
            Assert.NotNull(nameText);
            Assert.NotNull(subtitleText);
            Assert.Equal(expected[i].Name, nameText.Text);
            Assert.Equal(expected[i].Subtitle, subtitleText.Text);
        }
    }

    private async Task RenderAndAssertStateAsync(
        string stateName,
        string outputDir,
        byte[] emptyLightBytes,
        byte[] emptyDarkBytes,
        MonitorInfo[] currentMonitors,
        Action<LayoutStore> seedLayouts,
        Action<MainViewModel, WpfListBoxItem[]> assertBeforeCapture,
        int? beforeMeasureSelectIndex = null)
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        seedLayouts(store);

        var monitors = new FakeMonitors(currentMonitors);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        string lightPath = Path.Combine(outputDir, $"{stateName}-light.png");
        string darkPath = Path.Combine(outputDir, $"{stateName}-dark.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                MeasureAndArrange(window);
                var pane = (LayoutsPane)window.LayoutsHost.Content;
                if (beforeMeasureSelectIndex is { } idx)
                {
                    pane.LayoutsList.SelectedIndex = idx;
                    MeasureAndArrange(window);
                }

                WpfListBoxItem[] items = RealizeItems(pane.LayoutsList);
                assertBeforeCapture(vm, items);

                WindowRenderer.Render(window.RootGrid, 980, 640, lightPath, RenderTheme.Light);
                WindowRenderer.Render(window.RootGrid, 980, 640, darkPath, RenderTheme.Dark);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });

        AssertValidDistinctPng(lightPath, emptyLightBytes);
        AssertValidDistinctPng(darkPath, emptyDarkBytes);
    }

    private MainViewModel CreateViewModel(LayoutStore store, IDesktopIcons desktop, IMonitors monitors) =>
        new(
            store,
            desktop,
            monitors,
            new FixedClock(FixedNow),
            action =>
            {
                appFixture.Invoke(action);
                return Task.CompletedTask;
            });

    private static void SaveLayout(LayoutStore store, MonitorInfo[] monitors, string name, int iconCount)
    {
        IconPosition[] icons = [.. Enumerable.Range(0, iconCount).Select(i => new IconPosition($"id-{i}", $"Icon {i}", i * 10, 20))];
        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From(monitors),
            name,
            FixedNow,
            [.. monitors],
            [.. icons],
            Previous: null);
        store.Save(layout);
    }

    private static void MeasureAndArrange(MainWindow window)
    {
        var size = new WpfSize(980, 640);
        window.RootGrid.Measure(size);
        window.RootGrid.Arrange(new Rect(new WpfPoint(0, 0), size));
        window.RootGrid.UpdateLayout();
    }

    private static WpfListBoxItem[] RealizeItems(WpfListBox listBox)
    {
        listBox.ApplyTemplate();
        listBox.UpdateLayout();
        var result = new List<WpfListBoxItem>(listBox.Items.Count);
        for (int i = 0; i < listBox.Items.Count; i++)
        {
            var container = (WpfListBoxItem?)listBox.ItemContainerGenerator.ContainerFromIndex(i);
            Assert.NotNull(container);
            container.ApplyTemplate();
            container.UpdateLayout();
            result.Add(container);
        }

        return [.. result];
    }

    private static void AssertSelectionMarker(WpfListBoxItem[] items, int selectedIndex)
    {
        for (int i = 0; i < items.Length; i++)
        {
            Border? marker = FindDescendant<Border>(items[i], "SelectionMarker");
            Assert.NotNull(marker);
            if (i == selectedIndex)
            {
                Assert.Equal(Visibility.Visible, marker.Visibility);
                Assert.Equal(3d, marker.Width);
                Assert.IsAssignableFrom<WpfBrush>(marker.Background);
                WpfPoint markerOrigin = marker.TranslatePoint(new WpfPoint(0, 0), items[i]);
                Assert.InRange(markerOrigin.X, 0d, 1d);
            }
            else
            {
                Assert.Equal(Visibility.Hidden, marker.Visibility);
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && string.Equals(typed.Name, name, StringComparison.Ordinal))
            {
                return typed;
            }

            T? found = FindDescendant<T>(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void AssertValidDistinctPng(string path, byte[] emptyFrameBytes)
    {
        Assert.True(File.Exists(path), $"Expected rendered PNG at '{path}'.");
        byte[] actualBytes = File.ReadAllBytes(path);
        Assert.NotEqual(emptyFrameBytes, actualBytes);

        using var ms = new MemoryStream(actualBytes);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Equal(980, decoder.Frames[0].PixelWidth);
        Assert.Equal(640, decoder.Frames[0].PixelHeight);
    }

    private static string ResolveRenderOutputDirectory()
    {
        string? envDir = Environment.GetEnvironmentVariable("BERTH_RENDER_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            return envDir;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Berth.slnx")))
        {
            dir = dir.Parent;
        }

        string repoRoot = dir?.FullName
            ?? throw new InvalidOperationException("Could not locate Berth.slnx above test output.");
        return Path.Combine(repoRoot, "build", "renders");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
