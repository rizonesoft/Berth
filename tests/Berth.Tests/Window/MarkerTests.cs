using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class MarkerTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonDualA = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonDualB = new(2560, 0, 1920, 1080, 100, false);
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        IconPosition savedInPlace = new("id-1", "InPlace.txt", 200, 200);
        IconPosition savedMoved = new("id-2", "Moved.txt", 600, 400);
        IconPosition savedMissing = new("id-3", "Missing.txt", 1000, 600);
        SaveLayout(store, [MonDualA, MonDualB], "Docked", savedInPlace, savedMoved, savedMissing);
        SaveLayout(store, [MonSingle], "Laptop", new IconPosition("id-4", "Neutral.txt", 300, 300));

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        // Live desktop: id-1 in place, id-2 moved to an off-screen point (5000, 400) so unclamped moved line is non-zero, id-3 missing!
        var desktop = new FakeDesktopIcons(savedInPlace, new IconPosition("id-2", "Moved.txt", 5000, 400));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                RenderMeasureAndArrange(window);
                PositionMap map = window.PositionMapControl;
                WpfListBoxItem[] items = RealizeItems(map.MarkersList);
                Assert.Equal(3, items.Length);

                // InPlace: filled Berth.MarkerInPlace, 3 px radii.
                WpfRectangle rInPlace = Assert.IsType<WpfRectangle>(FindDescendant<WpfRectangle>(items[0], "MarkerVisual"));
                Assert.Same(System.Windows.Application.Current.FindResource("Berth.MarkerInPlace"), rInPlace.Fill);
                Assert.Null(rInPlace.Stroke);
                Assert.Equal(3d, rInPlace.RadiusX, precision: 2);
                Assert.Equal(3d, rInPlace.RadiusY, precision: 2);
                Assert.Equal("InPlace.txt", items[0].ToolTip);
                Assert.Equal("InPlace.txt, x 200, y 200, monitor 1, in place", AutomationProperties.GetName(items[0]));
                Assert.Equal("InPlace.txt, x 200, y 200, monitor 1, in place", UIElementAutomationPeer.CreatePeerForElement(items[0]).GetName());

                // Moved: transparent fill, 2 px Berth.MarkerMoved stroke, 3 px radii, plus 1 px connector line to unclamped current!
                WpfRectangle rMoved = Assert.IsType<WpfRectangle>(FindDescendant<WpfRectangle>(items[1], "MarkerVisual"));
                Assert.Equal(System.Windows.Media.Brushes.Transparent, rMoved.Fill);
                Assert.Same(System.Windows.Application.Current.FindResource("Berth.MarkerMoved"), rMoved.Stroke);
                Assert.Equal(2d, rMoved.StrokeThickness, precision: 2);
                Assert.Equal(3d, rMoved.RadiusX, precision: 2);
                Assert.Equal("Moved.txt", items[1].ToolTip);

                (WpfPoint from, WpfPoint to) = Assert.Single(map.Surface.RenderedMovedLines);
                Assert.Equal(map.MarkerItems[1].CenterX, from.X, precision: 2);
                Assert.Equal(map.MarkerItems[1].CenterY, from.Y, precision: 2);
                MapPoint expectedCurrent = map.CurrentGeometry.ToPanel(5000, 400);
                Assert.Equal(expectedCurrent.X, to.X, precision: 2);
                Assert.Equal(expectedCurrent.Y, to.Y, precision: 2);

                // Viewport must clip downward moved connectors so they never paint across the footer!
                Assert.True(map.MapViewport.ClipToBounds);

                // Missing: transparent fill, 1 px dashed Berth.MarkerMissing stroke, 3 px radii.
                WpfRectangle rMissing = Assert.IsType<WpfRectangle>(FindDescendant<WpfRectangle>(items[2], "MarkerVisual"));
                Assert.Equal(System.Windows.Media.Brushes.Transparent, rMissing.Fill);
                Assert.Same(System.Windows.Application.Current.FindResource("Berth.MarkerMissing"), rMissing.Stroke);
                Assert.Equal(1d, rMissing.StrokeThickness, precision: 2);
                Assert.Equal([2d, 2d], [.. rMissing.StrokeDashArray]);
                Assert.Equal(3d, rMissing.RadiusX, precision: 2);
                Assert.Equal("Missing.txt", items[2].ToolTip);

                // Switch to "Laptop" (Other) -> Neutral template filled Berth.MarkerNeutral, count "1 icon".
                vm.Selected = vm.Rows.Single(r => r.Name == "Laptop");
                RenderMeasureAndArrange(window);
                WpfListBoxItem[] neutralItems = RealizeItems(map.MarkersList);
                WpfRectangle rNeutral = Assert.IsType<WpfRectangle>(FindDescendant<WpfRectangle>(neutralItems[0], "MarkerVisual"));
                Assert.Same(System.Windows.Application.Current.FindResource("Berth.MarkerNeutral"), rNeutral.Fill);
                Assert.Null(rNeutral.Stroke);
                Assert.Equal("1 icon", map.CountTextBlock.Text);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    [Fact]
    public async Task SelectionShowsRingDropLineAndReadoutSurvivesRefreshAndClearsOnLayoutSwitch()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        IconPosition savedA = new("id-shared", "Notes.txt", 200, 200);
        IconPosition savedB = new("id-b", "Report.docx", 800, 500);
        IconPosition savedC = new("id-c", "Missing.txt", 1200, 700);
        SaveLayout(store, [MonDualA, MonDualB], "Docked", savedA, savedB, savedC);
        SaveLayout(store, [MonSingle], "Laptop", new IconPosition("id-shared", "Notes.txt", 300, 300), new IconPosition("id-other", "Other.txt", 500, 500));

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var desktop = new FakeDesktopIcons(
            new IconPosition("id-shared", "Notes.txt", 1400, 900), // Moved initially
            savedB);
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                RenderMeasureAndArrange(w);
                PositionMap map = w.PositionMapControl;
                WpfListBoxItem[] items = RealizeItems(map.MarkersList);

                // Before selection: record viewport height so we prove selection causes 0 px vertical shift.
                double viewportHeightBefore = map.MapViewport.ActualHeight;
                Assert.All(items, item =>
                    Assert.Equal(Visibility.Collapsed, FindDescendant<WpfRectangle>(item, "SelectionRing")!.Visibility));
                Assert.Null(map.Surface.RenderedSelectionDropLine);
                Assert.Equal("Select an icon to see its position.", map.ReadoutPrimaryText.Text);
                Assert.Equal(Visibility.Hidden, map.ReadoutSecondaryText.Visibility);
                Assert.Equal("1 of 3 in place", map.CountTextBlock.Text);

                // Select row 0 ("Notes.txt", Moved) via UIA SelectionItemPattern.
                var listPeer = new ListBoxAutomationPeer(map.MarkersList);
                var itemPeer = new ListBoxItemAutomationPeer(map.MarkerItems[0], listPeer);
                var peer = (ISelectionItemProvider)itemPeer.GetPattern(PatternInterface.SelectionItem);
                peer.Select();
                RenderMeasureAndArrange(w);

                Assert.Equal(viewportHeightBefore, map.MapViewport.ActualHeight, precision: 2);

                WpfRectangle ring = Assert.IsType<WpfRectangle>(FindDescendant<WpfRectangle>(items[0], "SelectionRing"));
                Assert.Equal(Visibility.Visible, ring.Visibility);
                Assert.Equal(23d, ring.ActualWidth, precision: 2);
                Assert.Equal(23d, ring.ActualHeight, precision: 2);
                Assert.Equal(6d, ring.RadiusX, precision: 2);
                Assert.Equal(19d, items[0].ActualWidth, precision: 2);
                Assert.Equal(19d, items[0].ActualHeight, precision: 2);

                WpfRectangle selectedVisual = Assert.IsType<WpfRectangle>(FindDescendant<WpfRectangle>(items[0], "MarkerVisual"));
                Assert.Same(System.Windows.Application.Current.FindResource("Berth.MarkerSelected"), selectedVisual.Fill);

                Assert.NotNull(map.Surface.RenderedSelectionDropLine);
                (WpfPoint dropFrom, WpfPoint dropTo) = map.Surface.RenderedSelectionDropLine!.Value;
                Assert.Equal(map.MarkerItems[0].CenterX, dropFrom.X, precision: 2);
                Assert.Equal(map.MapViewport.ActualHeight, dropTo.Y, precision: 2);

                Assert.Equal("Notes.txt", map.ReadoutPrimaryText.Text);
                Assert.Equal(Visibility.Visible, map.ReadoutSecondaryText.Visibility);
                Assert.Equal("x 200 \u00B7 y 200 \u00B7 monitor 1 \u00B7 moved", map.ReadoutSecondaryText.Text);
                return w;
            });

            // Move "Notes.txt" back to its saved position and refresh: selection must stay on "Notes.txt",
            // state word updates to "in place", count updates to "2 of 3 in place", and moved line disappears!
            ((IDesktopIcons)desktop).SetPositions([savedA]);
            await vm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                RenderMeasureAndArrange(window!);
                PositionMap map = window!.PositionMapControl;
                Assert.Equal("Notes.txt", map.ReadoutPrimaryText.Text);
                Assert.Equal("x 200 \u00B7 y 200 \u00B7 monitor 1 \u00B7 in place", map.ReadoutSecondaryText.Text);
                Assert.Equal("2 of 3 in place", map.CountTextBlock.Text);
                Assert.Empty(map.Surface.RenderedMovedLines);
                Assert.NotNull(map.Surface.RenderedSelectionDropLine);
            });

            // Remove the selected parsing ID ("id-shared") from the SAME layout and refresh: selection must clear,
            // prompt returns, ring & drop-line clear, and count reflects the remaining entries!
            SaveLayout(store, [MonDualA, MonDualB], "Docked", savedB, savedC);
            await vm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                RenderMeasureAndArrange(window!);
                PositionMap map = window!.PositionMapControl;
                Assert.Null(map.MarkersList.SelectedItem);
                Assert.Null(map.Surface.RenderedSelectionDropLine);
                Assert.All(RealizeItems(map.MarkersList), item =>
                    Assert.Equal(Visibility.Collapsed, FindDescendant<WpfRectangle>(item, "SelectionRing")!.Visibility));
                Assert.Equal("Select an icon to see its position.", map.ReadoutPrimaryText.Text);
                Assert.Equal("1 of 2 in place", map.CountTextBlock.Text);

                // Restore "id-shared" in "Docked", select it, then switch to "Laptop" (which also has "id-shared"):
                // selection MUST clear on layout switch!
                SaveLayout(store, [MonDualA, MonDualB], "Docked", savedA, savedB, savedC);
            });

            await vm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                RenderMeasureAndArrange(window!);
                PositionMap map = window!.PositionMapControl;
                map.MarkersList.SelectedIndex = 0;
                RenderMeasureAndArrange(window);
                Assert.Equal("Notes.txt", map.ReadoutPrimaryText.Text);

                vm.Selected = vm.Rows.Single(r => r.Name == "Laptop");
                RenderMeasureAndArrange(window);
                Assert.Null(map.MarkersList.SelectedItem);
                Assert.Null(map.Surface.RenderedSelectionDropLine);
                Assert.Equal("Select an icon to see its position.", map.ReadoutPrimaryText.Text);
                Assert.Equal("2 icons", map.CountTextBlock.Text);
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

    [Fact]
    public async Task OverlappingMarkersRaiseSelectedAboveOverlapAndLongNameWrapsAt760DipWithCountVisible()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        string longName = "VeryLongDesktopDocumentNameThatMustWrapCleanlyAtMinWidth.docx";
        IconPosition first = new("id-ov-1", longName, 9000, 5000);
        IconPosition second = new("id-ov-2", "SecondOverlap.txt", 9500, 5200);
        SaveLayout(store, [MonDualA, MonDualB], "Docked", first, second);

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        // Move second far downward (Y = 12000) so moved line heads straight down toward footer (must be clipped by MapViewport).
        var desktop = new FakeDesktopIcons(first, new IconPosition("id-ov-2", "SecondOverlap.txt", 4000, 12000));
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var window = new MainWindow
            {
                DataContext = vm,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                Width = 760,
                Height = 520,
            };
            try
            {
                window.Show();
                RenderMeasureAndArrange(window, 760, 520);
                PositionMap map = window.PositionMapControl;
                WpfListBoxItem[] items = RealizeItems(map.MarkersList);
                Assert.Equal(2, items.Length);

                // Measure single-line readout height before selecting the 60-char wrapping name.
                double singleLineHeight = map.ReadoutPrimaryText.ActualHeight;

                // Exercise real arrow-key navigation (Key.Down -> item 0, Key.Down -> item 1, Key.Up -> item 0)
                // on the realized list with two coincident icons!
                PresentationSource source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(window));
                SendKey(map.MarkersList, source, System.Windows.Input.Key.Down);
                RenderMeasureAndArrange(window, 760, 520);
                Assert.Equal(0, map.MarkersList.SelectedIndex);
                Assert.True(System.Windows.Controls.Panel.GetZIndex(items[0]) > System.Windows.Controls.Panel.GetZIndex(items[1]));
                Assert.Same(
                    System.Windows.Application.Current.FindResource("Berth.MarkerSelected"),
                    FindDescendant<WpfRectangle>(items[0], "MarkerVisual")!.Fill);
                Assert.Equal(longName, map.ReadoutPrimaryText.Text);
                Assert.True(
                    map.ReadoutPrimaryText.ActualHeight > singleLineHeight + 4d,
                    $"Expected long name to wrap onto multiple lines ({map.ReadoutPrimaryText.ActualHeight} > {singleLineHeight}).");
                Assert.Equal("1 of 2 in place", map.CountTextBlock.Text);
                WpfPoint countRight = map.CountTextBlock.TranslatePoint(new WpfPoint(map.CountTextBlock.ActualWidth, 0), map.MapFooter);
                Assert.True(countRight.X <= map.MapFooter.ActualWidth + 0.5d);
                double expectedCountWidth = map.CountTextBlock.DesiredSize.Width - map.CountTextBlock.Margin.Left - map.CountTextBlock.Margin.Right;
                Assert.True(map.CountTextBlock.ActualWidth >= expectedCountWidth - 0.5d);
                Assert.Equal(map.MapViewport.ActualHeight, map.Surface.RenderedSelectionDropLine!.Value.To.Y, precision: 2);

                // Key.Down reaches the second coincident item in reading order!
                SendKey(map.MarkersList, source, System.Windows.Input.Key.Down);
                RenderMeasureAndArrange(window, 760, 520);
                Assert.Equal(1, map.MarkersList.SelectedIndex);
                Assert.True(System.Windows.Controls.Panel.GetZIndex(items[1]) > System.Windows.Controls.Panel.GetZIndex(items[0]));
                Assert.Same(
                    System.Windows.Application.Current.FindResource("Berth.MarkerSelected"),
                    FindDescendant<WpfRectangle>(items[1], "MarkerVisual")!.Fill);
                Assert.Equal("SecondOverlap.txt", map.ReadoutPrimaryText.Text);
                Assert.Contains("moved", map.ReadoutSecondaryText.Text, StringComparison.Ordinal);

                // Key.Up returns to item 0 cleanly!
                SendKey(map.MarkersList, source, System.Windows.Input.Key.Up);
                RenderMeasureAndArrange(window, 760, 520);
                Assert.Equal(0, map.MarkersList.SelectedIndex);
                Assert.Equal(longName, map.ReadoutPrimaryText.Text);

                // Downward moved connector extends past viewport bottom, AND rendered footer pixels have ZERO MarkerMoved colour!
                Assert.True(map.MapViewport.ClipToBounds);
                (WpfPoint from, WpfPoint to) = Assert.Single(map.Surface.RenderedMovedLines);
                Assert.True(to.Y > map.MapViewport.ActualHeight);

                var rtb = new RenderTargetBitmap(760, 520, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(window.RootGrid);
                byte[] pixels = new byte[760 * 520 * 4];
                rtb.CopyPixels(pixels, 760 * 4, 0);
                var movedBrush = Assert.IsType<SolidColorBrush>(System.Windows.Application.Current.FindResource("Berth.MarkerMoved"));
                System.Windows.Media.Color movedColor = movedBrush.Color;

                WpfPoint footerOrigin = map.MapFooter.TranslatePoint(new WpfPoint(0, 0), window.RootGrid);
                int yStart = Math.Clamp((int)Math.Ceiling(footerOrigin.Y) + 1, 0, 519);
                int yEnd = Math.Clamp((int)Math.Floor(footerOrigin.Y + map.MapFooter.ActualHeight) - 1, yStart, 519);
                int xStart = Math.Clamp((int)Math.Floor(footerOrigin.X), 0, 759);
                int xEnd = Math.Clamp((int)Math.Ceiling(footerOrigin.X + map.MapFooter.ActualWidth) - 1, xStart, 759);
                for (int y = yStart; y <= yEnd; y++)
                {
                    for (int x = xStart; x <= xEnd; x++)
                    {
                        int idx = ((y * 760) + x) * 4;
                        bool matchesMoved = Math.Abs(pixels[idx] - movedColor.B) <= 4
                            && Math.Abs(pixels[idx + 1] - movedColor.G) <= 4
                            && Math.Abs(pixels[idx + 2] - movedColor.R) <= 4;
                        Assert.False(matchesMoved, $"Moved connector pixel leaked into MapFooter at ({x}, {y}).");
                    }
                }
            }
            finally
            {
                window.CloseForShutdown();
            }
        });
    }

    private static void SendKey(UIElement target, PresentationSource source, System.Windows.Input.Key key)
    {
        var args = new System.Windows.Input.KeyEventArgs(
            System.Windows.Input.Keyboard.PrimaryDevice,
            source,
            timestamp: 0,
            key)
        {
            RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
        };
        target.RaiseEvent(args);
    }

    [Fact]
    public async Task UnreachableDesktopShowsNeutralMarkersAndIconCountAndRecoversOnRefresh()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);

        IconPosition[] icons = [.. Enumerable.Range(0, 42).Select(i =>
            new IconPosition($"id-{i}", $"File{i}.txt", 100 + ((i % 7) * 300), 100 + ((i / 7) * 180)))];
        SaveLayout(store, [MonDualA, MonDualB], "Docked", icons);

        var monitors = new FakeMonitors(MonDualA, MonDualB);
        var desktop = new FakeDesktopIcons(icons) { Unavailable = true };
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                RenderMeasureAndArrange(w);
                PositionMap map = w.PositionMapControl;
                WpfListBoxItem[] items = RealizeItems(map.MarkersList);
                Assert.Equal(42, items.Length);
                Assert.All(items, item =>
                {
                    WpfRectangle rect = Assert.IsType<WpfRectangle>(FindDescendant<WpfRectangle>(item, "MarkerVisual"));
                    Assert.Same(System.Windows.Application.Current.FindResource("Berth.MarkerNeutral"), rect.Fill);
                });
                Assert.Equal("42 icons", map.CountTextBlock.Text);
                return w;
            });

            // Recover desktop -> markers become InPlace and count becomes "42 of 42 in place".
            desktop.Unavailable = false;
            await vm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                RenderMeasureAndArrange(window!);
                PositionMap map = window!.PositionMapControl;
                WpfListBoxItem[] items = RealizeItems(map.MarkersList);
                WpfRectangle first = Assert.IsType<WpfRectangle>(FindDescendant<WpfRectangle>(items[0], "MarkerVisual"));
                Assert.Same(System.Windows.Application.Current.FindResource("Berth.MarkerInPlace"), first.Fill);
                Assert.Equal("42 of 42 in place", map.CountTextBlock.Text);
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

    [Fact]
    [Trait("Category", "Render")]
    public async Task RendersAllEightSpec19MapStatesInLightAndDark()
    {
        string outputDir = ResolveRenderOutputDirectory();

        // 1. map-in-place
        await RenderMapStateAsync(
            "map-in-place",
            outputDir,
            currentMonitors: [MonDualA, MonDualB],
            savedMonitors: [MonDualA, MonDualB],
            savedIcons: [new("id-1", "Recycle Bin", 76, 40), new("id-2", "Notes.txt", 1436, 702), new("id-3", "Terminal.lnk", 2720, 140)],
            liveIcons: [new("id-1", "Recycle Bin", 76, 40), new("id-2", "Notes.txt", 1436, 702), new("id-3", "Terminal.lnk", 2720, 140)],
            desktopUnavailable: false,
            selectIndex: null,
            assertBeforeCapture: map => Assert.Equal("3 of 3 in place", map.CountTextBlock.Text));

        // 2. map-moved
        await RenderMapStateAsync(
            "map-moved",
            outputDir,
            currentMonitors: [MonDualA, MonDualB],
            savedMonitors: [MonDualA, MonDualB],
            savedIcons: [new("id-1", "Notes.txt", 400, 300), new("id-2", "Report.docx", 1400, 700)],
            liveIcons: [new("id-1", "Notes.txt", 400, 300), new("id-2", "Report.docx", 3100, 420)],
            desktopUnavailable: false,
            selectIndex: null,
            assertBeforeCapture: map =>
            {
                Assert.Equal("1 of 2 in place", map.CountTextBlock.Text);
                Assert.Single(map.Surface.RenderedMovedLines);
            });

        // 3. map-missing
        await RenderMapStateAsync(
            "map-missing",
            outputDir,
            currentMonitors: [MonDualA, MonDualB],
            savedMonitors: [MonDualA, MonDualB],
            savedIcons: [new("id-1", "Notes.txt", 400, 300), new("id-2", "Old.lnk", 1400, 700)],
            liveIcons: [new("id-1", "Notes.txt", 400, 300)],
            desktopUnavailable: false,
            selectIndex: null,
            assertBeforeCapture: map => Assert.Equal("1 of 2 in place", map.CountTextBlock.Text));

        // 4. map-selected
        await RenderMapStateAsync(
            "map-selected",
            outputDir,
            currentMonitors: [MonDualA, MonDualB],
            savedMonitors: [MonDualA, MonDualB],
            savedIcons: [new("id-1", "Recycle Bin", 76, 40), new("id-2", "Notes.txt", 1436, 702)],
            liveIcons: [new("id-1", "Recycle Bin", 76, 40), new("id-2", "Notes.txt", 1436, 702)],
            desktopUnavailable: false,
            selectIndex: 1,
            assertBeforeCapture: map =>
            {
                Assert.Equal("Notes.txt", map.ReadoutPrimaryText.Text);
                Assert.Equal("x 1436 \u00B7 y 702 \u00B7 monitor 1 \u00B7 in place", map.ReadoutSecondaryText.Text);
                Assert.NotNull(map.Surface.RenderedSelectionDropLine);
            });

        // 5. map-none-selected
        await RenderMapStateAsync(
            "map-none-selected",
            outputDir,
            currentMonitors: [MonDualA, MonDualB],
            savedMonitors: [MonDualA, MonDualB],
            savedIcons: [new("id-1", "Notes.txt", 1436, 702)],
            liveIcons: [new("id-1", "Notes.txt", 1436, 702)],
            desktopUnavailable: false,
            selectIndex: null,
            assertBeforeCapture: map =>
            {
                Assert.Equal("Select an icon to see its position.", map.ReadoutPrimaryText.Text);
                Assert.Null(map.Surface.RenderedSelectionDropLine);
            });

        // 6. map-neutral (layout saved for another arrangement)
        await RenderMapStateAsync(
            "map-neutral",
            outputDir,
            currentMonitors: [MonSingle],
            savedMonitors: [MonDualA, MonDualB],
            savedIcons: [new("id-1", "Notes.txt", 400, 300), new("id-2", "Terminal.lnk", 3000, 400)],
            liveIcons: [],
            desktopUnavailable: false,
            selectIndex: 0,
            selectLayoutRowName: "Docked",
            assertBeforeCapture: map =>
            {
                Assert.Equal("2 icons", map.CountTextBlock.Text);
                Assert.Equal("x 400 \u00B7 y 300 \u00B7 monitor 1", map.ReadoutSecondaryText.Text);
            });

        // 7. map-off-screen (select the off-screen icon so " · off screen" is visible!)
        await RenderMapStateAsync(
            "map-off-screen",
            outputDir,
            currentMonitors: [MonDualA, MonDualB],
            savedMonitors: [MonDualA, MonDualB],
            savedIcons: [new("id-1", "Notes.txt", 400, 300), new("id-off", "Stray.txt", 9000, 2000)],
            liveIcons: [new("id-1", "Notes.txt", 400, 300), new("id-off", "Stray.txt", 9000, 2000)],
            desktopUnavailable: false,
            selectIndex: 1,
            assertBeforeCapture: map =>
            {
                Assert.Equal("Stray.txt", map.ReadoutPrimaryText.Text);
                Assert.Contains("off screen", map.ReadoutSecondaryText.Text, StringComparison.Ordinal);
            });

        // 8. map-unreachable (drive through fake desktop.Unavailable = true!)
        await RenderMapStateAsync(
            "map-unreachable",
            outputDir,
            currentMonitors: [MonDualA, MonDualB],
            savedMonitors: [MonDualA, MonDualB],
            savedIcons: [new("id-1", "Notes.txt", 400, 300), new("id-2", "Terminal.lnk", 3000, 400)],
            liveIcons: [],
            desktopUnavailable: true,
            selectIndex: null,
            assertBeforeCapture: map => Assert.Equal("2 icons", map.CountTextBlock.Text));
    }

    private async Task RenderMapStateAsync(
        string stateName,
        string outputDir,
        MonitorInfo[] currentMonitors,
        MonitorInfo[] savedMonitors,
        IconPosition[] savedIcons,
        IconPosition[] liveIcons,
        bool desktopUnavailable,
        int? selectIndex,
        Action<PositionMap> assertBeforeCapture,
        string? selectLayoutRowName = null)
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, savedMonitors, "Docked", savedIcons);

        var monitors = new FakeMonitors(currentMonitors);
        var desktop = new FakeDesktopIcons(liveIcons) { Unavailable = desktopUnavailable };
        var vm = CreateViewModel(store, desktop, monitors);
        await vm.RefreshAsync();

        string lightPath = Path.Combine(outputDir, $"{stateName}-light.png");
        string darkPath = Path.Combine(outputDir, $"{stateName}-dark.png");

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                if (selectLayoutRowName is not null)
                {
                    vm.Selected = vm.Rows.Single(r => r.Name == selectLayoutRowName);
                }

                RenderMeasureAndArrange(window);
                if (selectIndex is { } idx)
                {
                    window.PositionMapControl.MarkersList.SelectedIndex = idx;
                    RenderMeasureAndArrange(window);
                }

                assertBeforeCapture(window.PositionMapControl);
                WindowRenderer.Render(window.RootGrid, 980, 640, lightPath, RenderTheme.Light);
                WindowRenderer.Render(window.RootGrid, 980, 640, darkPath, RenderTheme.Dark);
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
