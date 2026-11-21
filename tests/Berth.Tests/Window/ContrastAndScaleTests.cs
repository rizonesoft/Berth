using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Berth.App;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using Microsoft.Win32;
using IoPath = System.IO.Path;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class ContrastAndScaleTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonPrimary = new(0, 0, 1920, 1080, 100, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);
    private const string FortyCharLayoutName = "Workstation Dual Monitor Ultrawide Setup";

    // Every opaque Light and Dark Berth colour token from design/tokens.json that must NEVER appear in High Contrast.
    private static readonly HashSet<(byte R, byte G, byte B)> ForbiddenTokenColours =
    [
        (0x0F, 0x6C, 0xBD), // light markerInPlace / activeBadgeFill
        (0x4C, 0xA6, 0xF0), // dark markerInPlace / activeBadgeFill
        (0x8A, 0x5A, 0x00), // light markerMoved
        (0xF2, 0xC6, 0x61), // dark markerMoved
        (0x5C, 0x5C, 0x5C), // light markerMissing / markerNeutral / textSecondary
        (0xC5, 0xC5, 0xC5), // dark markerMissing / markerNeutral / textSecondary
        (0x1A, 0x1A, 0x1A), // light markerSelected
        (0xFF, 0xFF, 0xFF), // light mapBackground / activeBadgeText / dark markerSelected
        (0x06, 0x24, 0x3D), // dark activeBadgeText
        (0xF3, 0xF3, 0xF3), // light monitorFill
        (0x24, 0x24, 0x24), // dark monitorFill
        (0x16, 0x16, 0x16), // dark mapBackground
        (0xFF, 0xF4, 0xCE), // light noticeWarningFill
        (0xFD, 0xE7, 0xE9), // light noticeErrorFill
    ];

    [Fact]
    [Trait("Category", "Render")]
    public async Task RendersDataHighContrastWithOnlyStandInSystemColoursAndData150At1470x960()
    {
        string outputDir = ResolveRenderOutputDirectory();
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(Settings.Default with { RestoreAutomatically = true });
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        var fakeRegistry = new FakeRunRegistry { StoredValue = "\"C:\\Berth\\berth.exe\"" };
        var runValue = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", fakeRegistry);

        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonPrimary]),
            "Laptop",
            FixedNow.AddDays(-1),
            [MonPrimary],
            [
                new IconPosition("id-1", "Notes.txt", 400, 300),
                new IconPosition("id-2", "Projects", 1200, 500),
                new IconPosition("id-3", "Archive.zip", 900, 800),
            ],
            null);
        layoutStore.Save(layout);

        var monitors = new FakeMonitors(MonPrimary);
        var desktop = new FakeDesktopIcons(
            new IconPosition("id-1", "Notes.txt", 400, 300),
            new IconPosition("id-2", "Projects", 1200, 500),
            new IconPosition("id-3", "Archive.zip", 1500, 800));
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        string highContrastPath = IoPath.Combine(outputDir, "data-high-contrast.png");
        string scale150Path = IoPath.Combine(outputDir, "data-150.png");

        Rect badgeBounds = default;
        Rect selectedRowBounds = default;
        Rect inPlaceMarkerBounds = default;
        Rect selectedMarkerBounds = default;
        Rect moreButtonBounds150 = default;

        appFixture.Invoke(() =>
        {
            var window = new MainWindow { DataContext = vm };
            try
            {
                // Select marker 0 ("Notes.txt") so both selected (#1AEBFF) and ordinary in-place (#F5F500) markers render!
                window.RootGrid.Measure(new WpfSize(980, 640));
                window.RootGrid.Arrange(new Rect(0, 0, 980, 640));
                window.RootGrid.UpdateLayout();
                window.PositionMapControl.MarkersList.SelectedIndex = 0;
                window.RootGrid.UpdateLayout();

                // Capture element regions in 980x640 coordinates before rendering High Contrast.
                Border activeBadge = FindVisualChildren<Border>(window.LayoutsPaneControl)
                    .Single(b => b.Name == "ActiveBadge");
                badgeBounds = TransformBounds(activeBadge, window.RootGrid);
                var selectedRowItem = (ListBoxItem)window.LayoutsPaneControl.LayoutsList.ItemContainerGenerator.ContainerFromIndex(0);
                selectedRowBounds = TransformBounds(selectedRowItem, window.RootGrid);

                var selectedItem = (ListBoxItem)window.PositionMapControl.MarkersList.ItemContainerGenerator.ContainerFromIndex(0);
                var inPlaceItem = (ListBoxItem)window.PositionMapControl.MarkersList.ItemContainerGenerator.ContainerFromIndex(1);
                selectedMarkerBounds = TransformBounds(selectedItem, window.RootGrid);
                inPlaceMarkerBounds = TransformBounds(inPlaceItem, window.RootGrid);

                WindowRenderer.Render(window.RootGrid, 980, 640, highContrastPath, RenderTheme.HighContrast, scale: 1.0);

                // Verify WindowRenderer preserved unset local LayoutTransform & Foreground on RootGrid!
                Assert.Equal(DependencyProperty.UnsetValue, window.RootGrid.ReadLocalValue(FrameworkElement.LayoutTransformProperty));
                Assert.Equal(DependencyProperty.UnsetValue, window.RootGrid.ReadLocalValue(System.Windows.Documents.TextElement.ForegroundProperty));

                // Render 150% scale -> 1470x960 PNG while keeping 980x640 logical layout!
                WindowRenderer.Render(window.RootGrid, 980, 640, scale150Path, RenderTheme.Light, scale: 1.5);
                Assert.Equal(DependencyProperty.UnsetValue, window.RootGrid.ReadLocalValue(FrameworkElement.LayoutTransformProperty));
                window.RootGrid.Measure(new WpfSize(980, 640));
                window.RootGrid.Arrange(new Rect(0, 0, 980, 640));
                window.RootGrid.UpdateLayout();
                Assert.Equal(980d, window.RootGrid.ActualWidth, precision: 1);
                Assert.Equal(640d, window.RootGrid.ActualHeight, precision: 1);
                moreButtonBounds150 = TransformBounds(window.DetailHeaderControl.MoreButton, window.RootGrid);
            }
            finally
            {
                window.CloseForShutdown();
            }
        });

        // Verify data-high-contrast.png (980x640): exact stand-in HC system colours and ZERO Light/Dark Berth token colours!
        (int hcW, int hcH, byte[] hcPixels) = LoadPngPixels(highContrastPath);
        Assert.Equal(980, hcW);
        Assert.Equal(640, hcH);

        // Shrink badgeBounds by 3 px so near-black lettering is sampled strictly inside the yellow badge fill!
        Rect badgeInterior = new(
            badgeBounds.Left + 3d,
            badgeBounds.Top + 3d,
            Math.Max(1d, badgeBounds.Width - 6d),
            Math.Max(1d, badgeBounds.Height - 6d));
        AssertRegionHasColourAndNoForbiddenTokens(
            hcPixels, hcW, badgeInterior,
            expectedColours: [WindowRenderer.HighContrastWindowTextColor, WindowRenderer.HighContrastWindowColor],
            label: "ActiveBadgeInterior");
        AssertRegionHasColourAndNoForbiddenTokens(
            hcPixels, hcW, selectedRowBounds,
            expectedColours: [WindowRenderer.HighContrastHighlightColor],
            label: "SelectedLayoutRow");
        AssertRegionHasColourAndNoForbiddenTokens(
            hcPixels, hcW, inPlaceMarkerBounds,
            expectedColours: [WindowRenderer.HighContrastWindowTextColor],
            label: "InPlaceMarker");
        AssertRegionHasColourAndNoForbiddenTokens(
            hcPixels, hcW, selectedMarkerBounds,
            expectedColours: [WindowRenderer.HighContrastHighlightColor],
            label: "SelectedMarker");

        // Verify data-150.png is EXACTLY 1470x960 and scaled content fills to the far edge!
        (int sW, int sH, byte[] sPixels) = LoadPngPixels(scale150Path);
        Assert.Equal(1470, sW);
        Assert.Equal(960, sH);
        Assert.True(moreButtonBounds150.Right > 900d, $"Expected MoreButton near far right of 980 logical width, got {moreButtonBounds150.Right}");

        // Sample the scaled pane divider at x = 375 (250 * 1.5) vs background at x = 360 to prove 1.5x layout scaling!
        (byte rDiv, byte gDiv, byte bDiv) = GetPixel(sPixels, sW, 375, 100);
        (byte rBg, byte gBg, byte bBg) = GetPixel(sPixels, sW, 360, 100);
        Assert.NotEqual((rBg, gBg, bBg), (rDiv, gDiv, bDiv));

        // Sample the scaled far-right MoreButton region (1.5x coordinates) proving it actually rendered at the far right!
        int moreX = Math.Clamp((int)Math.Round((moreButtonBounds150.Left + (moreButtonBounds150.Width / 2d)) * 1.5d), 0, sW - 1);
        int moreY = Math.Clamp((int)Math.Round((moreButtonBounds150.Top + (moreButtonBounds150.Height / 2d)) * 1.5d), 0, sH - 1);
        bool foundMoreNonBg = false;
        (byte pageR, byte pageG, byte pageB) = GetPixel(sPixels, sW, sW - 5, 5);
        for (int dy = -12; dy <= 12 && !foundMoreNonBg; dy++)
        {
            for (int dx = -12; dx <= 12 && !foundMoreNonBg; dx++)
            {
                (byte pr, byte pg, byte pb) = GetPixel(sPixels, sW, Math.Clamp(moreX + dx, 0, sW - 1), Math.Clamp(moreY + dy, 0, sH - 1));
                if (pr != pageR || pg != pageG || pb != pageB)
                {
                    foundMoreNonBg = true;
                }
            }
        }

        Assert.True(foundMoreNonBg, $"Expected scaled MoreButton pixels near ({moreX},{moreY}) in 1470x960 PNG.");
    }

    [Fact]
    public async Task MinimumSize760x520FitsWorstCaseStringsWithoutClippingAcrossAllThemesAndScales()
    {
        Assert.Equal(40, FortyCharLayoutName.Length);

        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        var runValue = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", new FakeRunRegistry());

        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonPrimary]),
            FortyCharLayoutName,
            FixedNow.AddDays(-1),
            [MonPrimary],
            [new IconPosition("id-1", "Notes.txt", 400, 300)],
            null);
        layoutStore.Save(layout);
        File.WriteAllText(IoPath.Combine(paths.LayoutsFolder, "corrupt.json"), "{ bad json");

        var monitors = new FakeMonitors(MonPrimary);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "Notes.txt", 400, 300));
        desktop.SetAutoArrange(true);
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        RenderTheme[] themes = [RenderTheme.Light, RenderTheme.Dark, RenderTheme.HighContrast];
        double[] scales = [1.0, 1.5];

        foreach (RenderTheme theme in themes)
        {
            foreach (double scale in scales)
            {
                appFixture.Invoke(() =>
                {
                    var berthApp = (BerthApplication)WpfApplication.Current;
#pragma warning disable WPF0001
                    ThemeMode savedMode = berthApp.ThemeMode;
#pragma warning restore WPF0001
                    ColorTheme savedTheme = berthApp.CurrentColorTheme;
                    ResourceDictionary? hcOverlay = null;
                    var window = new MainWindow { DataContext = vm };

                    try
                    {
                        if (theme == RenderTheme.HighContrast)
                        {
#pragma warning disable WPF0001
                            berthApp.ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
                            hcOverlay = WindowRenderer.CreateHighContrastStandInDictionary();
                            berthApp.Resources.MergedDictionaries.Add(hcOverlay);
                            berthApp.ApplyColorTheme(ColorTheme.HighContrast);
                        }
                        else
                        {
#pragma warning disable WPF0001
                            berthApp.ThemeMode = theme == RenderTheme.Light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
                            berthApp.ApplyColorTheme(theme == RenderTheme.Light ? ColorTheme.Light : ColorTheme.Dark);
                        }

                        // Set the FULL longest SPEC §8 status sentence (both notPlaced > 0 and missing > 0) + Undo button!
                        vm.Status.SetResult(
                            OperationResult.Restored(FortyCharLayoutName, total: 26, inPlace: 22, notPlaced: 2, missing: 2),
                            canUndo: true);

                        int pxW = (int)Math.Round(760 * scale);
                        int pxH = (int)Math.Round(520 * scale);
                        window.RootGrid.LayoutTransform = Math.Abs(scale - 1.0) > 1e-6
                            ? new ScaleTransform(scale, scale)
                            : Transform.Identity;
                        window.RootGrid.Measure(new WpfSize(pxW, pxH));
                        window.RootGrid.Arrange(new Rect(0, 0, pxW, pxH));
                        window.RootGrid.UpdateLayout();

                        Assert.Equal(760d, window.RootGrid.ActualWidth, precision: 1);
                        Assert.Equal(520d, window.RootGrid.ActualHeight, precision: 1);

                        // 1. Verify BOTH notices, BOTH 40-char name elements, and the COMPLETE SPEC §8 sentence + Undo are visible!
                        WpfButton[] noticeButtons = [.. FindVisualChildren<WpfButton>(window.NoticesControl).Where(b => b.Visibility == Visibility.Visible)];
                        Assert.Equal(2, noticeButtons.Length);
                        Assert.Equal(Visibility.Visible, window.StatusLineControl.ActionButton.Visibility);
                        Assert.Equal("Undo", window.StatusLineControl.ActionButton.Content);
                        Assert.Equal(
                            "Restored 22 of 26 icons. 2 did not stay where they were saved. 2 are no longer on the desktop.",
                            window.StatusLineControl.SentenceText.Text);

                        TextBlock rowName = FindVisualChildren<TextBlock>(window.LayoutsPaneControl.LayoutsList)
                            .Single(tb => tb.Name == "RowNameText");
                        TextBlock headerTitle = window.DetailHeaderControl.TitleText;

                        // 2. Verify BOTH layout-name elements actually overflow horizontally AND trim cleanly with CharacterEllipsis + ToolTip!
                        AssertLayoutNameTrimmedWithTooltip(rowName, FortyCharLayoutName, $"RowNameText ({theme} {scale}x)");
                        AssertLayoutNameTrimmedWithTooltip(headerTitle, FortyCharLayoutName, $"TitleText ({theme} {scale}x)");

                        // 3. Verify EVERY visible control (including LayoutsList, PositionMap, buttons, switches, notices) fits inside its parent and RootGrid!
                        Rect rootRect = new(0, 0, window.RootGrid.ActualWidth + 1d, window.RootGrid.ActualHeight + 1d);
                        FrameworkElement[] controls =
                        [
                            window.LayoutsPaneControl.LayoutsList,
                            window.PositionMapControl,
                            window.StatusLineControl.StatusCard,
                            window.DetailHeaderControl.RestoreButton,
                            window.DetailHeaderControl.SaveButton,
                            window.DetailHeaderControl.MoreButton,
                            window.StatusLineControl.ActionButton,
                            window.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch,
                            window.LayoutsPaneControl.SettingsPanelControl.StartWithWindowsSwitch,
                            .. noticeButtons,
                        ];
                        foreach (FrameworkElement ctrl in controls.Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0))
                        {
                            Rect inRoot = TransformBounds(ctrl, window.RootGrid);
                            Assert.True(
                                rootRect.Contains(inRoot),
                                $"Control {ctrl.Name} ({theme} {scale}x) overflows RootGrid: {inRoot} vs {rootRect}");
                            if (VisualTreeHelper.GetParent(ctrl) is FrameworkElement parent && parent.ActualWidth > 0 && parent.ActualHeight > 0)
                            {
                                Rect inParent = TransformBounds(ctrl, parent);
                                Assert.True(
                                    inParent.Left >= -1d && inParent.Top >= -1d
                                    && inParent.Right <= parent.ActualWidth + 1d
                                    && inParent.Bottom <= parent.ActualHeight + 1d,
                                    $"Control {ctrl.Name} ({theme} {scale}x) overflows parent {parent.GetType().Name}: {inParent} vs {parent.ActualWidth}x{parent.ActualHeight}");
                            }
                        }

                        // 4. Verify EVERY non-name visible TextBlock has TextTrimming.None and fits its REAL FormattedText height!
                        foreach (TextBlock tb in FindVisualChildren<TextBlock>(window.RootGrid).Where(t => t.Visibility == Visibility.Visible && t.ActualWidth > 0 && !string.IsNullOrWhiteSpace(t.Text)))
                        {
                            if (tb.Name is "RowNameText" or "TitleText")
                            {
                                continue;
                            }

                            Assert.Equal(TextTrimming.None, tb.TextTrimming);
                            if (tb.ActualWidth <= 0)
                            {
                                continue;
                            }

                            FormattedText ft = MeasureText(tb, tb.ActualWidth);
                            Assert.True(
                                ft.Height <= tb.ActualHeight + 2.5d,
                                $"TextBlock '{tb.Name}' (\"{tb.Text}\") clipped vertically at {theme} {scale}x: needed {ft.Height:F1}, got {tb.ActualHeight:F1}");

                            // Also verify no ancestor clips the TextBlock's arranged bounds!
                            Rect tbInRoot = TransformBounds(tb, window.RootGrid);
                            Assert.True(
                                rootRect.Contains(tbInRoot),
                                $"TextBlock '{tb.Name}' overflows RootGrid at {theme} {scale}x: {tbInRoot}");
                        }
                    }
                    finally
                    {
                        window.RootGrid.LayoutTransform = Transform.Identity;
                        window.CloseForShutdown();
                        if (hcOverlay is not null)
                        {
                            berthApp.Resources.MergedDictionaries.Remove(hcOverlay);
                        }

#pragma warning disable WPF0001
                        berthApp.ThemeMode = savedMode;
#pragma warning restore WPF0001
                        berthApp.ApplyColorTheme(savedTheme);
                    }
                });
            }
        }
    }

    [Fact]
    public async Task HighContrast150PercentSwitchesAndFocusRingsAreDistinguishableAndUnclipped()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        settingsStore.Save(Settings.Default with { RestoreAutomatically = true });
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        var fakeRegistry = new FakeRunRegistry { StoredValue = null };
        var runValue = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", fakeRegistry);

        var monitors = new FakeMonitors(MonPrimary);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "Notes.txt", 400, 300));
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        appFixture.Invoke(() =>
        {
            var berthApp = (BerthApplication)WpfApplication.Current;
#pragma warning disable WPF0001
            ThemeMode savedMode = berthApp.ThemeMode;
#pragma warning restore WPF0001
            ColorTheme savedTheme = berthApp.CurrentColorTheme;
            ResourceDictionary hcOverlay = WindowRenderer.CreateHighContrastStandInDictionary();
            var window = new MainWindow
            {
                DataContext = vm,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
            };

            try
            {
#pragma warning disable WPF0001
                berthApp.ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
                berthApp.Resources.MergedDictionaries.Add(hcOverlay);
                berthApp.ApplyColorTheme(ColorTheme.HighContrast);

                window.Show();
                window.RootGrid.LayoutTransform = new ScaleTransform(1.5, 1.5);
                window.RootGrid.Measure(new WpfSize(1140, 780));
                window.RootGrid.Arrange(new Rect(0, 0, 1140, 780));
                window.RootGrid.UpdateLayout();

                SettingsPanel panel = window.LayoutsPaneControl.SettingsPanelControl;
                Assert.Equal(Visibility.Visible, panel.Visibility);
                Assert.Equal(1d, panel.Opacity);

                // Caption must be legible WindowTextColor (#F5F500) on WindowColor (#050505).
                var captionBrush = Assert.IsType<SolidColorBrush>(panel.VersionCaptionText.Foreground);
                Assert.Equal(WindowRenderer.HighContrastWindowTextColor, captionBrush.Color);

                // RestoreAutomaticallySwitch is ON -> track HighlightColor (#1AEBFF), knob HighlightTextColor (#050505).
                Border onTrack = (Border)panel.RestoreAutomaticallySwitch.Template.FindName("Track", panel.RestoreAutomaticallySwitch);
                Border onKnob = (Border)panel.RestoreAutomaticallySwitch.Template.FindName("Knob", panel.RestoreAutomaticallySwitch);
                Assert.Equal(WindowRenderer.HighContrastHighlightColor, Assert.IsType<SolidColorBrush>(onTrack.Background).Color);
                Assert.Equal(WindowRenderer.HighContrastHighlightTextColor, Assert.IsType<SolidColorBrush>(onKnob.Background).Color);

                // StartWithWindowsSwitch is OFF -> track WindowColor (#050505), border & knob WindowTextColor (#F5F500).
                Border offTrack = (Border)panel.StartWithWindowsSwitch.Template.FindName("Track", panel.StartWithWindowsSwitch);
                Border offKnob = (Border)panel.StartWithWindowsSwitch.Template.FindName("Knob", panel.StartWithWindowsSwitch);
                Assert.Equal(WindowRenderer.HighContrastWindowColor, Assert.IsType<SolidColorBrush>(offTrack.Background).Color);
                Assert.Equal(WindowRenderer.HighContrastWindowTextColor, Assert.IsType<SolidColorBrush>(offTrack.BorderBrush).Color);
                Assert.Equal(WindowRenderer.HighContrastWindowTextColor, Assert.IsType<SolidColorBrush>(offKnob.Background).Color);

                // Focus BOTH switches in High Contrast 150%, verify non-zero 2px/1px borders, unclipped geometry, AND rendered pixel change!
                foreach (ToggleButton sw in new[] { panel.RestoreAutomaticallySwitch, panel.StartWithWindowsSwitch })
                {
                    window.DetailHeaderControl.MoreButton.Focus();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
                    byte[] unfocusedPixels = RenderElementPixels(panel);

                    sw.Focus();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
                    Border outer = (Border)sw.Template.FindName("FocusRingOuter", sw);
                    Border inner = (Border)sw.Template.FindName("FocusRingInner", sw);
                    Assert.Equal(Visibility.Visible, outer.Visibility);
                    Assert.Equal(Visibility.Visible, inner.Visibility);
                    Assert.Equal(2d, outer.BorderThickness.Left);
                    Assert.Equal(1d, inner.BorderThickness.Left);
                    WpfColor ringColour = Assert.IsType<SolidColorBrush>(outer.BorderBrush).Color;
                    Assert.Equal(WindowRenderer.HighContrastHighlightColor, ringColour);
                    Assert.NotEqual(WindowRenderer.HighContrastWindowColor, ringColour);

                    byte[] focusedPixels = RenderElementPixels(panel);
                    int changedHighlightPixels = CountChangedPixelsMatching(unfocusedPixels, focusedPixels, WindowRenderer.HighContrastHighlightColor);
                    Assert.True(changedHighlightPixels >= 40, $"Expected >= 40 highlight focus pixels around {sw.Name}, got {changedHighlightPixels}");

                    // Walk EVERY ancestor including SettingsPanel itself up to RootGrid checking effective clip geometry!
                    var ringRect = new Rect(0d, 0d, outer.ActualWidth, outer.ActualHeight);
                    DependencyObject? cursor = VisualTreeHelper.GetParent(outer);
                    while (cursor is not null)
                    {
                        if (cursor is UIElement ui)
                        {
                            Assert.Equal(Visibility.Visible, ui.Visibility);
                            Assert.Equal(1d, ui.Opacity);
                            if (VisualTreeHelper.GetClip(ui) is Geometry clip)
                            {
                                Rect ringInUi = outer.TransformToAncestor(ui).TransformBounds(ringRect);
                                Assert.Equal(
                                    IntersectionDetail.FullyContains,
                                    clip.FillContainsWithDetail(new RectangleGeometry(ringInUi)));
                            }
                        }

                        if (ReferenceEquals(cursor, window.RootGrid))
                        {
                            break;
                        }

                        cursor = VisualTreeHelper.GetParent(cursor);
                    }
                }

                // Flip switches to the opposite states (Off/On) and verify track/knob colours swap cleanly!
                panel.RestoreAutomaticallySwitch.IsChecked = false;
                panel.StartWithWindowsSwitch.IsChecked = true;
                window.RootGrid.UpdateLayout();
                Assert.Equal(WindowRenderer.HighContrastWindowColor, Assert.IsType<SolidColorBrush>(onTrack.Background).Color);
                Assert.Equal(WindowRenderer.HighContrastWindowTextColor, Assert.IsType<SolidColorBrush>(onKnob.Background).Color);
                Assert.Equal(WindowRenderer.HighContrastHighlightColor, Assert.IsType<SolidColorBrush>(offTrack.Background).Color);
                Assert.Equal(WindowRenderer.HighContrastHighlightTextColor, Assert.IsType<SolidColorBrush>(offKnob.Background).Color);
            }
            finally
            {
                window.RootGrid.LayoutTransform = Transform.Identity;
                window.CloseForShutdown();
                berthApp.Resources.MergedDictionaries.Remove(hcOverlay);
#pragma warning disable WPF0001
                berthApp.ThemeMode = savedMode;
#pragma warning restore WPF0001
                berthApp.ApplyColorTheme(savedTheme);
            }
        });
    }

    private static void AssertLayoutNameTrimmedWithTooltip(TextBlock tb, string fullName, string label)
    {
        Assert.Equal(fullName, tb.Text);
        Assert.Equal(TextTrimming.CharacterEllipsis, tb.TextTrimming);
        Assert.Equal(fullName, tb.ToolTip as string);

        FormattedText unconstrained = MeasureText(tb, double.PositiveInfinity);
        Assert.True(
            unconstrained.Width > tb.ActualWidth,
            $"{label} did not actually overflow horizontally (needed {unconstrained.Width:F1} <= ActualWidth {tb.ActualWidth:F1}).");
        Assert.True(
            unconstrained.Height <= tb.ActualHeight + 2.5d,
            $"{label} clipped vertically: line height {unconstrained.Height:F1} > ActualHeight {tb.ActualHeight:F1}");
    }

    private static byte[] RenderElementPixels(FrameworkElement element)
    {
        int w = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * 1.5d));
        int h = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * 1.5d));
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, w, h));
        }

        var rtb = new RenderTargetBitmap(w, h, 96d, 96d, PixelFormats.Pbgra32);
        rtb.Render(dv);
        byte[] pixels = new byte[w * h * 4];
        rtb.CopyPixels(pixels, w * 4, 0);
        return pixels;
    }

    private static int CountChangedPixelsMatching(byte[] before, byte[] after, WpfColor expected)
    {
        int count = 0;
        int len = Math.Min(before.Length, after.Length);
        for (int i = 0; i + 3 < len; i += 4)
        {
            if (before[i] != after[i] || before[i + 1] != after[i + 1] || before[i + 2] != after[i + 2])
            {
                if (after[i + 2] == expected.R && after[i + 1] == expected.G && after[i] == expected.B)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static FormattedText MeasureText(TextBlock tb, double maxWidth)
    {
        var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
        var ft = new FormattedText(
            tb.Text,
            CultureInfo.CurrentUICulture,
            tb.FlowDirection,
            typeface,
            tb.FontSize,
            System.Windows.Media.Brushes.Black,
            pixelsPerDip: 1.0);
        if (!double.IsInfinity(maxWidth) && maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
        }

        return ft;
    }

    private static Rect TransformBounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static void AssertRegionHasColourAndNoForbiddenTokens(
        byte[] pixels,
        int strideWidth,
        Rect bounds,
        WpfColor[] expectedColours,
        string label)
    {
        int x0 = Math.Max(0, (int)Math.Floor(bounds.Left));
        int y0 = Math.Max(0, (int)Math.Floor(bounds.Top));
        int x1 = Math.Min(strideWidth - 1, (int)Math.Ceiling(bounds.Right));
        int y1 = Math.Max(y0, (int)Math.Ceiling(bounds.Bottom));

        var foundExpected = new bool[expectedColours.Length];
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                (byte r, byte g, byte b) = GetPixel(pixels, strideWidth, x, y);
                Assert.False(
                    ForbiddenTokenColours.Contains((r, g, b)),
                    $"Forbidden Berth Light/Dark token colour #{r:X2}{g:X2}{b:X2} found in {label} at ({x},{y}).");

                for (int i = 0; i < expectedColours.Length; i++)
                {
                    if (r == expectedColours[i].R && g == expectedColours[i].G && b == expectedColours[i].B)
                    {
                        foundExpected[i] = true;
                    }
                }
            }
        }

        for (int i = 0; i < expectedColours.Length; i++)
        {
            Assert.True(
                foundExpected[i],
                $"Expected High Contrast stand-in colour #{expectedColours[i].R:X2}{expectedColours[i].G:X2}{expectedColours[i].B:X2} not found in {label} ({bounds}).");
        }
    }

    private static (int Width, int Height, byte[] Pixels) LoadPngPixels(string path)
    {
        Assert.True(File.Exists(path), $"Expected render output at {path}");
        using FileStream stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth;
        int h = bgra.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);
        return (w, h, pixels);
    }

    private static (byte R, byte G, byte B) GetPixel(byte[] bgra, int width, int x, int y)
    {
        int offset = ((y * width) + x) * 4;
        return (bgra[offset + 2], bgra[offset + 1], bgra[offset]);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
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
            if (File.Exists(IoPath.Combine(dir, "Berth.slnx")))
            {
                string outDir = IoPath.Combine(dir, "build", "renders");
                Directory.CreateDirectory(outDir);
                return outDir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Berth.slnx above " + AppContext.BaseDirectory);
    }

    private static LayoutService CreateService(BerthPaths paths, LayoutStore store, IDesktopIcons desktop, IMonitors monitors, Log log) =>
        new(
            desktop,
            monitors,
            store,
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(5)),
            log,
            new FixedClock(FixedNow));

    private MainViewModel CreateViewModel(
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors,
        LayoutService service,
        SettingsStore settingsStore,
        RunValue runValue,
        SettingChanges settingChanges,
        Log log) =>
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
            service: service,
            timeZone: TimeZoneInfo.Utc,
            log: log,
            settingsStore: settingsStore,
            runValue: runValue,
            settingChanges: settingChanges);

    private sealed class FakeRunRegistry : IRunRegistry
    {
        public string? StoredValue { get; set; }

        public bool TryReadValue(string keyPath, string valueName, out string? value, out RegistryValueKind kind)
        {
            if (StoredValue is null)
            {
                value = null;
                kind = RegistryValueKind.Unknown;
                return false;
            }

            value = StoredValue;
            kind = RegistryValueKind.String;
            return true;
        }

        public void WriteStringValue(string keyPath, string valueName, string value) => StoredValue = value;

        public void DeleteValue(string keyPath, string valueName) => StoredValue = null;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
