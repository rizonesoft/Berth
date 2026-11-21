using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Window;
using Berth.Window.Map;
using Microsoft.Win32;
using IoPath = System.IO.Path;
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class AccessibilityTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonLeft = new(-1920, 0, 1920, 1080, 100, false);
    private static readonly MonitorInfo MonPrimary = new(0, 0, 1920, 1080, 100, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task TabOrderTraversesForwardAndBackwardAcrossAllWindowStates()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        var fakeRegistry = new FakeRunRegistry();
        var runValue = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", fakeRegistry);

        var savedLayout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonPrimary]),
            "Laptop",
            FixedNow.AddDays(-1),
            [MonPrimary],
            [new IconPosition("id-1", "Notes.txt", 120, 200)],
            null);
        var otherLayout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonLeft, MonPrimary]),
            "Docked",
            FixedNow.AddDays(-2),
            [MonLeft, MonPrimary],
            [new IconPosition("id-1", "Notes.txt", 120, 200)],
            null);
        layoutStore.Save(savedLayout);
        layoutStore.Save(otherLayout);

        // Corrupt an extra layout file so an Unreadable notice appears alongside Auto-arrange!
        File.WriteAllText(IoPath.Combine(paths.LayoutsFolder, "corrupt.json"), "{ bad json");

        var monitors = new FakeMonitors(MonPrimary);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "Notes.txt", 300, 200));
        desktop.SetAutoArrange(true);
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() => ShowOffScreen(vm));

            // 1. Active layout + 2 notices (Auto-arrange + Unreadable) + Retry status action.
            // Auto-arrange disables Restore, so Tab order is:
            // LayoutsList item -> Restore automatically -> Start with Windows -> Turn off -> Open folder -> Save -> More -> Marker -> Retry.
            appFixture.Invoke(() =>
            {
                vm.Status.SetResult(
                    OperationResult.Refused(Refusal.DesktopUnavailable),
                    canUndo: false,
                    new WindowOperationRequest(WindowOperationKind.Restore, () => OperationResult.Restored("Laptop", 1, 1, 0, 0)));
                PumpUi(window!);

                AssertFullTabCycle(
                    window!,
                    [
                        "Laptop",
                        "Restore automatically",
                        "Start with Windows",
                        "Open folder",
                        "Turn it off",
                        "Save current layout",
                        "More options",
                        "Notes.txt, x 120, y 200, monitor 1, moved",
                        "Retry",
                    ]);
            });

            // 2. Clear notices (Auto-arrange off + corrupt file deleted) + Undo status action -> Restore is enabled!
            desktop.SetAutoArrange(false);
            File.Delete(IoPath.Combine(paths.LayoutsFolder, "corrupt.json"));
            await vm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                vm.Status.SetResult(OperationResult.Restored("Laptop", 1, 1, 0, 0), canUndo: true);
                PumpUi(window!);

                AssertFullTabCycle(
                    window!,
                    [
                        "Laptop",
                        "Restore automatically",
                        "Start with Windows",
                        "Restore",
                        "Save current layout",
                        "More options",
                        "Notes.txt, x 120, y 200, monitor 1, moved",
                        "Undo",
                    ]);
            });

            // 2b. Working/busy state disables header buttons and status action -> Tab skips them cleanly!
            appFixture.Invoke(() =>
            {
                vm.Detail.SetExternalBusy(true);
                PumpUi(window!);

                AssertFullTabCycle(
                    window!,
                    [
                        "Laptop",
                        "Restore automatically",
                        "Start with Windows",
                        "Notes.txt, x 120, y 200, monitor 1, moved",
                    ]);
                vm.Detail.SetExternalBusy(false);
                PumpUi(window!);
            });

            // 3. Other arrangement selected -> only More options in header, no Restore or Save!
            appFixture.Invoke(() =>
            {
                vm.Selected = vm.Rows.Single(r => r.Name == "Docked");
                PumpUi(window!);

                AssertFullTabCycle(
                    window!,
                    [
                        "Docked",
                        "Restore automatically",
                        "Start with Windows",
                        "More options",
                        "Notes.txt, x 120, y 200, monitor 2",
                    ]);
            });

            // 4. Unsaved current arrangement -> Save current layout + live desktop neutral marker!
            layoutStore.Delete(savedLayout.Key);
            await vm.RefreshAsync();

            appFixture.Invoke(() =>
            {
                vm.Selected = vm.Rows.Single(r => r.IsUnsaved);
                PumpUi(window!);

                AssertFullTabCycle(
                    window!,
                    [
                        "This arrangement",
                        "Restore automatically",
                        "Start with Windows",
                        "Save current layout",
                        "Notes.txt, x 300, y 200, monitor 1",
                    ]);
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
    public async Task AutomationPeerTreeNamesEveryControlMoreMenuAndModalDialogs()
    {
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
            "Laptop",
            FixedNow.AddDays(-1),
            [MonPrimary],
            [new IconPosition("id-1", "Notes.txt", 100, 100)],
            null);
        layoutStore.Save(layout);

        var monitors = new FakeMonitors(MonPrimary);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "Notes.txt", 100, 100));
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                MainWindow w = ShowOffScreen(vm);

                // Explicit More button automation name check (negative checkpoint removes this property!).
                WpfButton moreButton = w.DetailHeaderControl.MoreButton;
                Assert.Equal("More options", AutomationProperties.GetName(moreButton));
                var morePeer = UIElementAutomationPeer.CreatePeerForElement(moreButton);
                Assert.NotNull(morePeer);
                Assert.Equal("More options", morePeer!.GetName());

                // Map list must be named "Saved icon positions" (SPEC §7, §12), and NoticesItems named "Notices".
                Assert.Equal("Saved icon positions", AutomationProperties.GetName(w.PositionMapControl.MarkersList));
                Assert.Equal("Notices", AutomationProperties.GetName(w.NoticesControl.NoticesItems));
                var mapPeer = UIElementAutomationPeer.CreatePeerForElement(w.PositionMapControl.MarkersList);
                Assert.NotNull(mapPeer);
                Assert.Equal("Saved icon positions", mapPeer!.GetName());

                // Walk AutomationPeer tree of the window and audit every interactive control!
                var rootPeer = new WindowAutomationPeer(w);
                List<AutomationPeer> peers = CollectPeers(rootPeer);

                int buttons = 0;
                int listBoxes = 0;
                int listItems = 0;
                int toggles = 0;
                foreach (AutomationPeer peer in peers)
                {
                    if (peer is UIElementAutomationPeer uiPeer && !uiPeer.Owner.IsVisible)
                    {
                        continue;
                    }

                    if (peer is RepeatButtonAutomationPeer)
                    {
                        continue;
                    }

                    AutomationControlType type = peer.GetAutomationControlType();
                    if (type is AutomationControlType.Button
                        or AutomationControlType.List
                        or AutomationControlType.ListItem
                        or AutomationControlType.Edit
                        or AutomationControlType.CheckBox)
                    {
                        string name = peer.GetName();
                        string ownerDesc = peer is UIElementAutomationPeer up && up.Owner is FrameworkElement fe
                            ? $"{fe.GetType().Name} '{fe.Name}'"
                            : peer.GetType().Name;
                        Assert.False(string.IsNullOrWhiteSpace(name), $"Empty automation name on {ownerDesc} ({type})");
                        if (type == AutomationControlType.Button)
                        {
                            buttons++;
                        }
                        else if (type == AutomationControlType.List)
                        {
                            listBoxes++;
                        }
                        else if (type == AutomationControlType.ListItem)
                        {
                            listItems++;
                        }
                    }

                    if (peer is ToggleButtonAutomationPeer)
                    {
                        toggles++;
                    }
                }

                Assert.True(buttons >= 3, $"Expected >= 3 buttons, got {buttons}");
                Assert.True(listBoxes >= 2, $"Expected >= 2 lists, got {listBoxes}");
                Assert.True(listItems >= 2, $"Expected >= 2 list items, got {listItems}");
                Assert.Equal(2, toggles);

                // Audit More menu items (including disabled Revert) and Escape focus return.
                moreButton.Focus();
                var invoke = (System.Windows.Automation.Provider.IInvokeProvider)morePeer.GetPattern(PatternInterface.Invoke);
                invoke.Invoke();
                PumpUi(w);

                WpfContextMenu menu = moreButton.ContextMenu!;
                Assert.True(menu.IsOpen);
                Assert.Equal("Rename", AutomationProperties.GetName(w.DetailHeaderControl.RenameMenuItem));
                Assert.Equal("Revert to previous save", AutomationProperties.GetName(w.DetailHeaderControl.RevertMenuItem));
                Assert.False(w.DetailHeaderControl.RevertMenuItem.IsEnabled);
                Assert.Equal("Delete", AutomationProperties.GetName(w.DetailHeaderControl.DeleteMenuItem));
                menu.IsOpen = false;
                PumpUi(w);
                Assert.True(moreButton.IsKeyboardFocused);

                // Audit RenameDialog controls, invalid-empty disabling Rename, and Escape dismissal.
                var renameVm = new RenameViewModel("Laptop", ["Docked"]);
                var renameDialog = new RenameDialog(renameVm, _ => true)
                {
                    Owner = w,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                    Top = -10000,
                };
                renameDialog.Show();
                PumpUi(renameDialog);
                Assert.Equal("Layout name", AutomationProperties.GetName(renameDialog.NameBox));
                Assert.Equal("Rename", AutomationProperties.GetName(renameDialog.RenameButton));
                Assert.Equal("Cancel", AutomationProperties.GetName(renameDialog.CancelButton));
                renameVm.Name = "   ";
                Assert.False(renameDialog.RenameButton.IsEnabled);
                renameDialog.Close();

                // Audit DeleteDialog controls and Cancel as safe default.
                var deleteVm = new DeleteDialogViewModel("Laptop");
                var deleteDialog = new DeleteDialog(deleteVm, () => true)
                {
                    Owner = w,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                    Top = -10000,
                };
                deleteDialog.Show();
                PumpUi(deleteDialog);
                Assert.Equal("Delete", AutomationProperties.GetName(deleteDialog.DeleteButton));
                Assert.Equal("Cancel", AutomationProperties.GetName(deleteDialog.CancelButton));
                Assert.True(deleteDialog.CancelButton.IsDefault);
                Assert.True(deleteDialog.CancelButton.IsKeyboardFocused);
                deleteDialog.Close();

                return w;
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
    public async Task MapMarkersOrderInReadingOrderAcrossTwoMonitorsAndSpeakExactSpec7Names()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        var runValue = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", new FakeRunRegistry());

        // 5 markers spanning 2 monitors (MonLeft: -1920..0 is monitor 1, MonPrimary: 0..1920 is monitor 2):
        // Deliberately unsorted in the saved layout file; reading order must be Y ascending, then X ascending across the whole map!
        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonLeft, MonPrimary]),
            "Docked",
            FixedNow.AddDays(-1),
            [MonLeft, MonPrimary],
            [
                new IconPosition("id-low-left", "LowLeft.txt", -1500, 700),
                new IconPosition("id-top-right", "TopRight.txt", 400, 100),
                new IconPosition("id-top-left", "TopLeft.txt", -800, 100),
                new IconPosition("id-coincident-b", "SameB.txt", 200, 400),
                new IconPosition("id-offscreen", "OffScreen.txt", 3500, 900),
            ],
            null);
        layoutStore.Save(layout);

        var monitors = new FakeMonitors(MonLeft, MonPrimary);
        // TopLeft in place, TopRight moved, LowLeft missing ("not on the desktop"), SameB in place, OffScreen in place (off screen).
        var desktop = new FakeDesktopIcons(
            new IconPosition("id-top-left", "TopLeft.txt", -800, 100),
            new IconPosition("id-top-right", "TopRight.txt", 500, 100),
            new IconPosition("id-coincident-b", "SameB.txt", 200, 400),
            new IconPosition("id-offscreen", "OffScreen.txt", 3500, 900));
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                MainWindow w = ShowOffScreen(vm);
                WpfListBox markers = w.PositionMapControl.MarkersList;
                Assert.Equal(5, markers.Items.Count);

                string[] expectedNames =
                [
                    "TopLeft.txt, x -800, y 100, monitor 1, in place",
                    "TopRight.txt, x 400, y 100, monitor 2, moved",
                    "SameB.txt, x 200, y 400, monitor 2, in place",
                    "LowLeft.txt, x -1500, y 700, monitor 1, not on the desktop",
                    "OffScreen.txt, x 3500, y 900, monitor 2, in place, off screen",
                ];

                for (int i = 0; i < expectedNames.Length; i++)
                {
                    var item = (ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(i);
                    Assert.NotNull(item);
                    var peer = UIElementAutomationPeer.CreatePeerForElement(item);
                    Assert.Equal(expectedNames[i], peer!.GetName());
                }

                // Neutral marker (other arrangement) must omit state word with no trailing comma.
                var neutralIcon = new ComparedIcon(
                    new IconPosition("id-n", "Neutral.txt", 1436, 702),
                    Current: null,
                    State: IconState.Neutral,
                    MonitorNumber: 1,
                    OffScreen: false);
                Assert.Equal("Neutral.txt, x 1436, y 702, monitor 1", IconReadout.AutomationName(neutralIcon));

                return w;
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
    public async Task ArrowHomeEndKeysNavigateBothListsAndMapPreservesFocusAcrossRefresh()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        var runValue = new RunValue(@"Software\Berth.Tests\Fake", "Berth", @"C:\Berth\berth.exe", new FakeRunRegistry());

        var active = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonPrimary]),
            "Active Layout",
            FixedNow,
            [MonPrimary],
            [
                new IconPosition("id-1", "A.txt", 100, 100),
                new IconPosition("id-2", "B.txt", 200, 100),
                new IconPosition("id-3", "C.txt", 300, 100),
            ],
            null);
        layoutStore.Save(active);

        // Add several more layouts so End scrolls to an off-viewport row!
        for (int i = 1; i <= 12; i++)
        {
            var mon = new MonitorInfo(0, 0, 1280 + (i * 10), 720, 100, true);
            layoutStore.Save(new Layout(
                Layout.CurrentSchemaVersion,
                ArrangementKey.From([mon]),
                $"Layout {i:D2}",
                FixedNow.AddMinutes(-i),
                [mon],
                [new IconPosition("id-1", "A.txt", 100, 100)],
                null));
        }

        var monitors = new FakeMonitors(MonPrimary);
        var desktop = new FakeDesktopIcons(
            new IconPosition("id-1", "A.txt", 100, 100),
            new IconPosition("id-2", "B.txt", 200, 100),
            new IconPosition("id-3", "C.txt", 300, 100));
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                MainWindow w = ShowOffScreen(vm);

                // 1. Layouts list: Down, Right, Up, Left, End (off-viewport), Home!
                WpfListBox layouts = w.LayoutsPaneControl.LayoutsList;
                var firstLayoutItem = (ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(0);
                firstLayoutItem.Focus();
                PumpUi(w);
                Assert.Equal(0, layouts.SelectedIndex);

                SendKey(firstLayoutItem, Key.Down);
                Assert.Equal(1, layouts.SelectedIndex);
                Assert.True(((ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(1)).IsKeyboardFocused);

                SendKey((ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(1), Key.Right);
                Assert.Equal(2, layouts.SelectedIndex);
                Assert.True(((ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(2)).IsKeyboardFocused);

                SendKey((ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(2), Key.Up);
                Assert.Equal(1, layouts.SelectedIndex);
                Assert.True(((ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(1)).IsKeyboardFocused);

                SendKey((ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(1), Key.Left);
                Assert.Equal(0, layouts.SelectedIndex);
                Assert.True(((ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(0)).IsKeyboardFocused);

                SendKey((ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(0), Key.End);
                Assert.Equal(layouts.Items.Count - 1, layouts.SelectedIndex);
                var lastContainer = (ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(layouts.Items.Count - 1);
                Assert.NotNull(lastContainer);
                Assert.True(lastContainer.IsKeyboardFocused);

                SendKey(lastContainer, Key.Home);
                Assert.Equal(0, layouts.SelectedIndex);
                Assert.True(((ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(0)).IsKeyboardFocused);

                // 2. Map markers list: Down, Right, Up, Left, End, Home!
                WpfListBox markers = w.PositionMapControl.MarkersList;
                var firstMarker = (ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(0);
                firstMarker.Focus();
                markers.SelectedIndex = 0;
                PumpUi(w);

                SendKey(firstMarker, Key.Right);
                Assert.Equal(1, markers.SelectedIndex);
                Assert.True(((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(1)).IsKeyboardFocused);

                SendKey((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(1), Key.Down);
                Assert.Equal(2, markers.SelectedIndex);
                Assert.True(((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(2)).IsKeyboardFocused);

                SendKey((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(2), Key.Up);
                Assert.Equal(1, markers.SelectedIndex);
                Assert.True(((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(1)).IsKeyboardFocused);

                SendKey((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(1), Key.Left);
                Assert.Equal(0, markers.SelectedIndex);
                Assert.True(((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(0)).IsKeyboardFocused);

                SendKey((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(0), Key.End);
                Assert.Equal(2, markers.SelectedIndex);

                SendKey((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(2), Key.Home);
                Assert.Equal(0, markers.SelectedIndex);

                // Leave keyboard focus on marker index 1 before refresh!
                SendKey((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(0), Key.Down);
                Assert.Equal(1, markers.SelectedIndex);
                Assert.True(((ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(1)).IsKeyboardFocused);
                return w;
            });

            // 3. Refresh while marker 1 owns focus -> selection AND keyboard focus survive!
            await vm.RefreshAsync();
            appFixture.Invoke(() =>
            {
                PumpUi(window!);
                WpfListBox markers = window!.PositionMapControl.MarkersList;
                Assert.Equal(1, markers.SelectedIndex);
                var restoredContainer = (ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(1);
                Assert.NotNull(restoredContainer);
                Assert.True(restoredContainer.IsKeyboardFocused);

                // Next arrow continues from marker 1 -> marker 2!
                SendKey(restoredContainer, Key.Down);
                Assert.Equal(2, markers.SelectedIndex);

                // Refresh while switch owns focus -> switch keeps focus!
                ToggleButton restoreSwitch = window.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch;
                restoreSwitch.Focus();
                PumpUi(window);
                Assert.True(restoreSwitch.IsKeyboardFocused);
            });

            await vm.RefreshAsync();
            appFixture.Invoke(() =>
            {
                PumpUi(window!);
                Assert.True(window!.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch.IsKeyboardFocused);
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
    public async Task KeyboardSpaceAndEnterActivateSwitchesAndStatusActionsWithVisibleFocusCues()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var layoutStore = new LayoutStore(paths);
        var settingsStore = new SettingsStore(paths);
        var log = new Log(paths, new FixedClock(FixedNow));
        var changes = new SettingChanges(settingsStore, log);
        string testKeyPath = $@"Software\Berth.Tests\{Guid.NewGuid():N}";
        var runValue = new RunValue(testKeyPath, "Berth", @"C:\Program Files\Berth\berth.exe");

        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From([MonPrimary]),
            "Laptop",
            FixedNow.AddDays(-1),
            [MonPrimary],
            [new IconPosition("id-1", "Notes.txt", 100, 100)],
            null);
        layoutStore.Save(layout);

        var monitors = new FakeMonitors(MonPrimary);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "Notes.txt", 250, 250));
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                MainWindow w = ShowOffScreen(vm);
                SettingsPanel panel = w.LayoutsPaneControl.SettingsPanelControl;

                // Focus Restore automatically switch -> uses native Fluent FocusVisualStyle, Tab moves without toggling!
                panel.RestoreAutomaticallySwitch.Focus();
                PumpUi(w);
                Assert.NotNull(panel.RestoreAutomaticallySwitch.FocusVisualStyle);
                Assert.True(panel.RestoreAutomaticallySwitch.IsKeyboardFocused);

                // Tab moves to Start with Windows without toggling Restore automatically!
                panel.RestoreAutomaticallySwitch.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                PumpUi(w);
                Assert.False(vm.Settings!.RestoreAutomatically);
                Assert.False(panel.RestoreAutomaticallySwitch.IsKeyboardFocused);
                Assert.True(panel.StartWithWindowsSwitch.IsKeyboardFocused);

                // Space (key-down + key-up) toggles Start with Windows ON!
                SendKey(panel.StartWithWindowsSwitch, Key.Space, sendKeyUp: true);
                return w;
            });

            await vm.Settings!.LastStartTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(runValue.IsOn);

            // Space again toggles Start with Windows OFF!
            appFixture.Invoke(() =>
            {
                ToggleButton startSwitch = window!.LayoutsPaneControl.SettingsPanelControl.StartWithWindowsSwitch;
                startSwitch.Focus();
                SendKey(startSwitch, Key.Space, sendKeyUp: true);
            });
            await vm.Settings.LastStartTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(runValue.IsOn);

            // Space toggles Restore automatically ON and back OFF!
            appFixture.Invoke(() =>
            {
                ToggleButton restoreSwitch = window!.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch;
                restoreSwitch.Focus();
                SendKey(restoreSwitch, Key.Space, sendKeyUp: true);
            });
            await vm.Settings.LastRestoreTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(settingsStore.Load().Value.RestoreAutomatically);

            appFixture.Invoke(() =>
            {
                ToggleButton restoreSwitch = window!.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch;
                SendKey(restoreSwitch, Key.Space, sendKeyUp: true);
            });
            await vm.Settings.LastRestoreTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(settingsStore.Load().Value.RestoreAutomatically);

            // Marker FocusRing is visible when focused and collapsed when focus leaves while SelectionRing remains Visible!
            appFixture.Invoke(() =>
            {
                WpfListBox markers = window!.PositionMapControl.MarkersList;
                markers.SelectedIndex = 0;
                var markerItem = (ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(0);
                markerItem.Focus();
                PumpUi(window);

                var selectionRing = (WpfRectangle)markerItem.Template.FindName("SelectionRing", markerItem);
                var markerFocusOuter = (WpfRectangle)markerItem.Template.FindName("FocusRingOuter", markerItem);
                var markerFocusInner = (WpfRectangle)markerItem.Template.FindName("FocusRingInner", markerItem);
                Assert.Equal(Visibility.Visible, selectionRing.Visibility);
                Assert.Equal(Visibility.Visible, markerFocusOuter.Visibility);
                Assert.Equal(Visibility.Visible, markerFocusInner.Visibility);
                Assert.True(markerFocusOuter.Width > selectionRing.Width);

                // Move focus away -> SelectionRing stays Visible, FocusRingOuter/Inner hide!
                window.DetailHeaderControl.MoreButton.Focus();
                PumpUi(window);
                Assert.Equal(Visibility.Visible, selectionRing.Visibility);
                Assert.Equal(Visibility.Collapsed, markerFocusOuter.Visibility);
                Assert.Equal(Visibility.Collapsed, markerFocusInner.Visibility);
            });

            // Status Retry button activated via keyboard Enter invokes Retry!
            int retryCount = 0;
            appFixture.Invoke(() =>
            {
                vm.Status.SetResult(
                    OperationResult.Refused(Refusal.DesktopUnavailable),
                    canUndo: false,
                    new WindowOperationRequest(WindowOperationKind.Restore, () =>
                    {
                        Interlocked.Increment(ref retryCount);
                        return OperationResult.Restored("Laptop", 1, 1, 0, 0);
                    }));
                PumpUi(window!);

                WpfButton actionButton = window!.StatusLineControl.ActionButton;
                actionButton.Focus();
                PumpUi(window);
                Assert.True(actionButton.IsKeyboardFocused);
                SendKey(actionButton, Key.Enter, sendKeyUp: true);
            });

            // Wait for retry operation to run and assert exactly one invocation!
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref retryCount) == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }

            Assert.Equal(1, Volatile.Read(ref retryCount));
            await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Status Undo button activated via keyboard Enter invokes Undo and puts icon back!
            OperationResult? restoreResult = await vm.Detail.RestoreAsync();
            Assert.NotNull(restoreResult);
            Assert.Equal(100, ((IDesktopIcons)desktop).ReadIcons()[0].X);
            appFixture.Invoke(() =>
            {
                PumpUi(window!);
                WpfButton undoButton = window!.StatusLineControl.ActionButton;
                Assert.Equal("Undo", undoButton.Content);
                undoButton.Focus();
                PumpUi(window);
                Assert.True(undoButton.IsKeyboardFocused);
                SendKey(undoButton, Key.Enter, sendKeyUp: true);
            });
            await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(250, ((IDesktopIcons)desktop).ReadIcons()[0].X);
        }
        finally
        {
            if (window is not null)
            {
                appFixture.Invoke(() => window.CloseForShutdown());
            }

            Registry.CurrentUser.DeleteSubKeyTree(testKeyPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public async Task InteractiveControlsMeet32PxMinimumAndMarkerTargetsAreExactly19PxSquare()
    {
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
            "Laptop",
            FixedNow.AddDays(-1),
            [MonPrimary],
            [
                new IconPosition("id-1", "One.txt", 100, 100),
                new IconPosition("id-2", "Two.txt", 300, 200),
            ],
            null);
        layoutStore.Save(layout);

        var monitors = new FakeMonitors(MonPrimary);
        var desktop = new FakeDesktopIcons(
            new IconPosition("id-1", "One.txt", 100, 100),
            new IconPosition("id-2", "Two.txt", 300, 200));
        desktop.SetAutoArrange(true);
        LayoutService service = CreateService(paths, layoutStore, desktop, monitors, log);
        var vm = CreateViewModel(layoutStore, desktop, monitors, service, settingsStore, runValue, changes, log);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                MainWindow w = ShowOffScreen(vm);
                vm.Status.SetResult(OperationResult.Restored("Laptop", 2, 2, 0, 0), canUndo: true);
                PumpUi(w);

                // Every non-marker interactive control must be >= 32 px high at 100% scale!
                var checkedControls = new List<FrameworkElement>
                {
                    (ListBoxItem)w.LayoutsPaneControl.LayoutsList.ItemContainerGenerator.ContainerFromIndex(0),
                    w.LayoutsPaneControl.SettingsPanelControl.RestoreAutomaticallySwitch,
                    w.LayoutsPaneControl.SettingsPanelControl.StartWithWindowsSwitch,
                    w.DetailHeaderControl.RestoreButton,
                    w.DetailHeaderControl.SaveButton,
                    w.DetailHeaderControl.MoreButton,
                    w.StatusLineControl.ActionButton,
                };

                WpfButton[] noticeButtons = [.. FindVisualChildren<WpfButton>(w.NoticesControl)];
                Assert.NotEmpty(noticeButtons);
                checkedControls.AddRange(noticeButtons);

                foreach (FrameworkElement control in checkedControls)
                {
                    Assert.True(
                        control.ActualHeight >= 32d,
                        $"Control {control.Name} ({control.GetType().Name}) height {control.ActualHeight} < 32 px");
                }

                // Every map marker ListBoxItem must be EXACTLY 19x19 px and hit-testable from MarkersList on its transparent edge
                // without enlarging past 19x19!
                WpfListBox markers = w.PositionMapControl.MarkersList;
                Assert.Equal(2, markers.Items.Count);
                for (int i = 0; i < markers.Items.Count; i++)
                {
                    var item = (ListBoxItem)markers.ItemContainerGenerator.ContainerFromIndex(i);
                    Assert.Equal(19d, item.ActualWidth);
                    Assert.Equal(19d, item.ActualHeight);
                    WpfPoint insideInList = item.TransformToAncestor(markers).Transform(new WpfPoint(1d, 1d));
                    HitTestResult? edgeHit = VisualTreeHelper.HitTest(markers, insideInList);
                    Assert.NotNull(edgeHit);
                    Assert.True(IsDescendantOf(edgeHit!.VisualHit, item));

                    WpfPoint outsideInList = item.TransformToAncestor(markers).Transform(new WpfPoint(-2d, -2d));
                    HitTestResult? outsideHit = VisualTreeHelper.HitTest(markers, outsideInList);
                    Assert.False(outsideHit is not null && IsDescendantOf(outsideHit.VisualHit, item));
                }

                // Open More menu and measure realized heights >= 32 px on all 3 menu items!
                w.DetailHeaderControl.MoreButton.ContextMenu!.PlacementTarget = w.DetailHeaderControl.MoreButton;
                w.DetailHeaderControl.MoreButton.ContextMenu.IsOpen = true;
                PumpUi(w);
                Assert.True(w.DetailHeaderControl.RenameMenuItem.ActualHeight >= 32d);
                Assert.True(w.DetailHeaderControl.RevertMenuItem.ActualHeight >= 32d);
                Assert.True(w.DetailHeaderControl.DeleteMenuItem.ActualHeight >= 32d);
                w.DetailHeaderControl.MoreButton.ContextMenu.IsOpen = false;

                // Measure RenameDialog and DeleteDialog controls >= 32 px!
                var renameDialog = new RenameDialog(new RenameViewModel("Laptop", []), _ => true)
                {
                    Owner = w,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                    Top = -10000,
                };
                renameDialog.Show();
                PumpUi(renameDialog);
                Assert.True(renameDialog.NameBox.ActualHeight >= 32d);
                Assert.True(renameDialog.RenameButton.ActualHeight >= 32d);
                Assert.True(renameDialog.CancelButton.ActualHeight >= 32d);
                renameDialog.Close();

                var deleteDialog = new DeleteDialog(new DeleteDialogViewModel("Laptop"), () => true)
                {
                    Owner = w,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -10000,
                    Top = -10000,
                };
                deleteDialog.Show();
                PumpUi(deleteDialog);
                Assert.True(deleteDialog.DeleteButton.ActualHeight >= 32d);
                Assert.True(deleteDialog.CancelButton.ActualHeight >= 32d);
                deleteDialog.Close();

                return w;
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

    private static MainWindow ShowOffScreen(MainViewModel vm)
    {
        var window = new MainWindow
        {
            DataContext = vm,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = true,
        };
        window.Show();
        PumpUi(window);
        return window;
    }

    private static void AssertFullTabCycle(MainWindow window, string[] expected)
    {
        string[] forward = WalkTabOrder(window, FocusNavigationDirection.Next, expected.Length + 1);
        Assert.Equal([.. expected, expected[0]], forward);

        // From wrapped expected[0], walk backward expected.Length steps back to expected[0] in exact reverse!
        string[] backward = WalkTabOrderFromCurrent(window, FocusNavigationDirection.Previous, expected.Length);
        string[] expectedReverse = [.. expected.Skip(1).Reverse(), expected[0]];
        Assert.Equal(expectedReverse, backward);
    }

    private static bool IsDescendantOf(DependencyObject? visual, DependencyObject ancestor)
    {
        DependencyObject? current = visual;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static string[] WalkTabOrder(MainWindow window, FocusNavigationDirection direction, int steps)
    {
        WpfListBox layouts = window.LayoutsPaneControl.LayoutsList;
        int startIndex = layouts.SelectedIndex >= 0 ? layouts.SelectedIndex : 0;
        var first = (ListBoxItem)layouts.ItemContainerGenerator.ContainerFromIndex(startIndex);
        first.Focus();
        PumpUi(window);

        var names = new List<string> { FocusedName() };
        for (int i = 1; i < steps; i++)
        {
            var current = Keyboard.FocusedElement as UIElement;
            Assert.NotNull(current);
            Assert.True(current!.MoveFocus(new TraversalRequest(direction)), $"MoveFocus({direction}) failed at step {i}");
            PumpUi(window);
            names.Add(FocusedName());
        }

        return [.. names];
    }

    private static string[] WalkTabOrderFromCurrent(MainWindow window, FocusNavigationDirection direction, int steps)
    {
        var names = new List<string>();
        for (int i = 0; i < steps; i++)
        {
            var current = Keyboard.FocusedElement as UIElement;
            Assert.NotNull(current);
            Assert.True(current!.MoveFocus(new TraversalRequest(direction)), $"MoveFocus({direction}) failed at step {i}");
            PumpUi(window);
            names.Add(FocusedName());
        }

        return [.. names];
    }

    private static string FocusedName()
    {
        var el = Keyboard.FocusedElement as DependencyObject;
        Assert.NotNull(el);
        string explicitName = AutomationProperties.GetName(el!);
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        if (el is UIElement ui && UIElementAutomationPeer.CreatePeerForElement(ui) is { } peer)
        {
            return peer.GetName();
        }

        return string.Empty;
    }

    private static List<AutomationPeer> CollectPeers(AutomationPeer root)
    {
        var list = new List<AutomationPeer>();
        var stack = new Stack<AutomationPeer>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            AutomationPeer current = stack.Pop();
            list.Add(current);
            List<AutomationPeer>? children = current.GetChildren();
            if (children is null)
            {
                continue;
            }

            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        return list;
    }

    private static void SendKey(UIElement target, Key key, bool sendKeyUp = false)
    {
        PresentationSource? source = PresentationSource.FromVisual(target);
        Assert.NotNull(source);
        var down = new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        target.RaiseEvent(down);
        if (!down.Handled)
        {
            var keyDown = new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            };
            target.RaiseEvent(keyDown);
        }

        if (sendKeyUp)
        {
            var up = new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, key)
            {
                RoutedEvent = Keyboard.KeyUpEvent,
            };
            target.RaiseEvent(up);
        }

        target.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
    }

    private static void PumpUi(System.Windows.Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
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
