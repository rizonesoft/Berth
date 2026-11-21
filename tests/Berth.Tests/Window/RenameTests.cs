using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Berth.App;
using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.App;
using Berth.Tests.Fakes;
using Berth.Tests.Render;
using Berth.Window;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfSize = System.Windows.Size;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class RenameTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo MonDualA = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonDualB = new(2560, 0, 1920, 1080, 100, false);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public void ViewModelValidatesEveryRuleLiveIncludingCaseInsensitiveDuplicateAndLiveOtherUpdates()
    {
        var vm = new RenameViewModel("Laptop", ["Docked", "Presentation"]);
        Assert.Equal("Laptop", vm.Name);
        Assert.Equal(string.Empty, vm.Error);
        Assert.False(vm.HasError);
        Assert.True(vm.CanRename);

        // Empty and whitespace
        vm.Name = "   ";
        Assert.Equal("Enter a name.", vm.Error);
        Assert.True(vm.HasError);
        Assert.False(vm.CanRename);

        // Too long (41 chars)
        vm.Name = new string('a', 41);
        Assert.Equal("A name can be at most 40 characters.", vm.Error);
        Assert.False(vm.CanRename);

        // Exactly 40 chars with surrounding whitespace is valid and trimmed
        string forty = new string('b', 40);
        vm.Name = $"  {forty}  ";
        Assert.Equal(string.Empty, vm.Error);
        Assert.True(vm.CanRename);
        Assert.Equal(forty, vm.TrimmedName);

        // Duplicate ignoring case
        vm.Name = "dOcKeD";
        Assert.Equal("Another layout already has this name.", vm.Error);
        Assert.False(vm.CanRename);

        // Live UpdateOthers: relinquished name re-enables without touching draft
        vm.UpdateOthers(["Presentation"]);
        Assert.Equal("dOcKeD", vm.Name);
        Assert.Equal(string.Empty, vm.Error);
        Assert.True(vm.CanRename);

        // Live UpdateOthers: another layout takes the draft name -> disables immediately
        vm.UpdateOthers(["Presentation", "DOCKED"]);
        Assert.Equal("Another layout already has this name.", vm.Error);
        Assert.False(vm.CanRename);

        // Busy state blocks submission
        vm.Name = "Fresh";
        Assert.True(vm.CanRename);
        vm.IsBusy = true;
        Assert.False(vm.CanRename);
        Assert.Equal(Messages.Refusal(OperationResult.Refused(Refusal.Busy)), vm.Error);
        vm.IsBusy = false;
        Assert.True(vm.CanRename);
    }

    [Fact]
    public async Task MoreMenuRenameModalHandlesOwnerSelectionInvalidEnterEscAndValidTrimmedRename()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string key = ArrangementKey.From([MonSingle]);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));
        SaveLayout(store, [MonDualA, MonDualB], "Docked", FixedNow, new IconPosition("id-1", "One", 100, 100));

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow
                {
                    DataContext = vm,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000,
                    Top = -20000,
                    ShowActivated = false,
                };
                w.Show();
                return w;
            });

            // 1. Open via More > Rename, verify Owner, selected text, invalid Enter keeps dialog open, Esc cancels!
            bool verifiedModal = false;
            Exception? modalError = null;
            appFixture.Invoke(() =>
            {
                var peer = new MenuItemAutomationPeer(window!.DetailHeaderControl.RenameMenuItem);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();

                WpfApplication.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    RenameDialog? dialog = WpfApplication.Current.Windows.OfType<RenameDialog>().SingleOrDefault(d => d.IsVisible);
                    if (dialog is null)
                    {
                        return;
                    }

                    try
                    {
                        Assert.Same(window, dialog.Owner);
                        Assert.Equal("Laptop", dialog.NameBox.Text);
                        Assert.Equal("Laptop", dialog.NameBox.SelectedText);
                        Assert.True(dialog.NameBox.IsFocused);
                        Assert.True(dialog.RenameButton.IsDefault);
                        Assert.True(dialog.CancelButton.IsCancel);
                        Assert.True(dialog.RenameButton.IsEnabled);

                        // Live busy state while modal is open disables Rename and keeps draft open!
                        vm.Detail.SetExternalBusy(true);
                        Assert.False(dialog.RenameButton.IsEnabled);
                        Assert.Equal(Messages.Refusal(OperationResult.Refused(Refusal.Busy)), dialog.ErrorText.Text);
                        vm.Detail.SetExternalBusy(false);
                        Assert.True(dialog.RenameButton.IsEnabled);

                        // Make name duplicate ("docked"), press Enter -> dialog MUST stay open!
                        dialog.NameBox.Text = "docked";
                        Assert.False(dialog.RenameButton.IsEnabled);
                        Assert.Equal("Another layout already has this name.", dialog.ErrorText.Text);
                        RaiseKey(dialog, dialog.NameBox, Key.Enter);
                        Assert.True(dialog.IsLoaded);

                        // Route Esc through WPF AccessKeyManager (exercises IsCancel directly!) -> closes without change!
                        AccessKeyManager.ProcessKey(null, "\x1B", false);
                        verifiedModal = true;
                    }
                    catch (Exception ex)
                    {
                        modalError = ex;
                        dialog.Close();
                    }
                });

                window!.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            });

            if (modalError is not null)
            {
                throw modalError;
            }

            Assert.True(verifiedModal);
            Assert.Equal("Laptop", store.LoadAll().Layouts.Single(l => l.Key == key).Name);

            // 2. Open via More > Rename again, type 40-char name with spaces, accept -> updates row, header, and disk!
            string forty = new string('W', 40);
            appFixture.Invoke(() =>
            {
                var peer = new MenuItemAutomationPeer(window!.DetailHeaderControl.RenameMenuItem);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();

                WpfApplication.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    RenameDialog? dialog = WpfApplication.Current.Windows.OfType<RenameDialog>().SingleOrDefault(d => d.IsVisible);
                    if (dialog is null)
                    {
                        return;
                    }

                    try
                    {
                        dialog.NameBox.Text = $"  {forty}  ";
                        Assert.True(dialog.RenameButton.IsEnabled);
                        // Route Enter through WPF AccessKeyManager (exercises IsDefault directly!)
                        AccessKeyManager.ProcessKey(null, "\r", false);
                    }
                    catch (Exception ex)
                    {
                        modalError = ex;
                        dialog.Close();
                    }
                });

                window!.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            });

            if (modalError is not null)
            {
                throw modalError;
            }

            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal(forty, store.LoadAll().Layouts.Single(l => l.Key == key).Name);
            Assert.Equal(forty, vm.Selected!.Name);
            Assert.Equal(forty, vm.Detail.Title);
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
    public async Task RenameTargetsCapturedKeyAfterDeletionAndSurfacesFailureWithRetryAndLateDuplicateWithoutRetry()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string keyA = ArrangementKey.From([MonSingle]);
        string keyB = ArrangementKey.From([MonDualA, MonDualB]);
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));
        SaveLayout(store, [MonDualA, MonDualB], "Docked", FixedNow, new IconPosition("id-1", "One", 100, 100));

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        bool corruptNextWrite = false;
        LayoutService service = CreateService(
            paths,
            store,
            desktop,
            monitors,
            afterWrite: () =>
            {
                if (corruptNextWrite)
                {
                    corruptNextWrite = false;
                    File.WriteAllText(paths.LayoutFile(keyA), "{ corrupted");
                }
            });
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        // 1. Late duplicate introduced on disk AFTER initial validation -> status shows duplicate with NO Retry!
        SaveLayout(store, [MonDualA, MonDualB], "LateConflict", FixedNow, new IconPosition("id-1", "One", 100, 100));
        await vm.Detail.RenameAsync(keyA, "lateconflict");
        Assert.Equal("Another layout already has this name.", vm.Status.Sentence);
        Assert.Equal(StatusAction.None, vm.Status.Action);
        Assert.Equal("Laptop", store.LoadAll().Layouts.Single(l => l.Key == keyA).Name);

        // 2. Read-back mismatch via AfterWrite seam -> exact §18 failure sentence + Retry; Retry renames captured key!
        corruptNextWrite = true;
        await vm.Detail.RenameAsync(keyA, "Workstation");
        Assert.StartsWith("Berth could not write ", vm.Status.Sentence);
        Assert.Equal(StatusAction.Retry, vm.Status.Action);
        Assert.NotEqual("Workstation", vm.Detail.Title);

        // Restore valid file on disk and click Retry -> succeeds with captured keyA and "Workstation"!
        File.Delete(paths.LayoutFile(keyA));
        SaveLayout(store, [MonSingle], "Laptop", FixedNow, new IconPosition("id-1", "One", 100, 100));
        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Workstation", store.LoadAll().Layouts.Single(l => l.Key == keyA).Name);
        Assert.Equal("Workstation", vm.Detail.Title);

        // 3. Target deleted while modal dialog is open + poll selects fallback -> captured keyA fails cleanly, keyB untouched!
        MainWindow? window = null;
        Exception? modalError = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow
                {
                    DataContext = vm,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000,
                    Top = -20000,
                    ShowActivated = false,
                };
                w.Show();
                return w;
            });

            appFixture.Invoke(() =>
            {
                var peer = new MenuItemAutomationPeer(window!.DetailHeaderControl.RenameMenuItem);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();

                WpfApplication.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    RenameDialog? dialog = WpfApplication.Current.Windows.OfType<RenameDialog>().SingleOrDefault(d => d.IsVisible);
                    if (dialog is null)
                    {
                        return;
                    }

                    try
                    {
                        // Simulate deletion + fallback selection change while modal is open!
                        File.Delete(paths.LayoutFile(keyA));
                        vm.Selected = vm.Rows.Single(r => r.Key == keyB);
                        dialog.NameBox.Text = "Ghost";
                        AccessKeyManager.ProcessKey(null, "\r", false);
                    }
                    catch (Exception ex)
                    {
                        modalError = ex;
                        dialog.Close();
                    }
                });

                window!.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            });

            if (modalError is not null)
            {
                throw modalError;
            }

            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal("This monitor arrangement has no saved layout.", vm.Status.Sentence);
            Assert.Equal("LateConflict", store.LoadAll().Layouts.Single(l => l.Key == keyB).Name);
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
    public void RendersAllFourSpec19RenameDialogStatesInLightAndDarkAndHighContrast150()
    {
        string outputDir = ResolveRenderOutputDirectory();

        RenderDialogState("rename-valid", outputDir, "Docked", ["Laptop"]);
        RenderDialogState("rename-empty", outputDir, "", ["Laptop"]);
        RenderDialogState("rename-too-long", outputDir, new string('A', 45), ["Laptop"]);
        RenderDialogState("rename-taken", outputDir, "Laptop", ["Laptop"]);

        // High Contrast & 150% scale wrapping verification on error state.
        using var data = new TestData();
        appFixture.Invoke(() =>
        {
            var dialog = new RenameDialog(new RenameViewModel(new string('A', 45), ["Laptop"]));
            var berthApp = (BerthApplication)WpfApplication.Current;
            ColorTheme saved = berthApp.CurrentColorTheme;
            try
            {
                berthApp.ApplyColorTheme(ColorTheme.HighContrast);
                VisualTreeHelper.SetRootDpi(dialog.DialogRoot, new DpiScale(1.5, 1.5));
                dialog.DialogRoot.Measure(new WpfSize(400, 260));
                dialog.DialogRoot.Arrange(new Rect(0, 0, 400, 260));
                dialog.DialogRoot.UpdateLayout();
                Assert.True(dialog.NameBox.ActualHeight >= 32d);
                Assert.True(dialog.RenameButton.ActualHeight >= 32d);
                Assert.True(dialog.CancelButton.ActualHeight >= 32d);
                Assert.True(dialog.ErrorText.ActualWidth > 0d && dialog.ErrorText.ActualHeight > 0d);
                Rect rootBounds = new(0, 0, dialog.DialogRoot.ActualWidth, dialog.DialogRoot.ActualHeight);
                Rect errorBounds = dialog.ErrorText.TransformToAncestor(dialog.DialogRoot)
                    .TransformBounds(new Rect(0, 0, dialog.ErrorText.ActualWidth, dialog.ErrorText.ActualHeight));
                Assert.True(errorBounds.Right <= rootBounds.Width + 0.5d);
                Assert.True(errorBounds.Bottom <= rootBounds.Height + 0.5d);
                var errorBrush = Assert.IsType<SolidColorBrush>(dialog.ErrorText.Foreground);
                Assert.Equal(System.Windows.SystemColors.WindowTextColor, errorBrush.Color);
                Assert.True(KeyboardNavigation.GetIsTabStop(dialog.NameBox));
                Assert.True(KeyboardNavigation.GetIsTabStop(dialog.RenameButton));
                Assert.True(KeyboardNavigation.GetIsTabStop(dialog.CancelButton));
            }
            finally
            {
                dialog.Close();
                berthApp.ApplyColorTheme(saved);
            }
        });
    }

    private void RenderDialogState(string name, string outputDir, string draft, string[] others)
    {
        string lightPath = Path.Combine(outputDir, $"{name}-light.png");
        string darkPath = Path.Combine(outputDir, $"{name}-dark.png");

        appFixture.Invoke(() =>
        {
            var dialog = new RenameDialog(new RenameViewModel(draft, others));
            try
            {
                // Apply binding evaluation before measuring so Collapsed ErrorText contributes 0 height in valid state.
                dialog.DialogRoot.Measure(new WpfSize(400, double.PositiveInfinity));
                dialog.DialogRoot.Arrange(new Rect(0, 0, 400, dialog.DialogRoot.DesiredSize.Height));
                dialog.DialogRoot.UpdateLayout();
                dialog.DialogRoot.Measure(new WpfSize(400, double.PositiveInfinity));
                int height = (int)Math.Ceiling(dialog.DialogRoot.DesiredSize.Height);
                WindowRenderer.Render(dialog.DialogRoot, 400, height, lightPath, RenderTheme.Light);
                WindowRenderer.Render(dialog.DialogRoot, 400, height, darkPath, RenderTheme.Dark);
            }
            finally
            {
                dialog.Close();
            }
        });

        AssertValidPng(lightPath);
        AssertValidPng(darkPath);
    }

    private static void RaiseKey(System.Windows.Window window, UIElement target, Key key)
    {
        var args = new System.Windows.Input.KeyEventArgs(
            Keyboard.PrimaryDevice,
            new FakePresentationSource(window),
            timestamp: 0,
            key)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent,
        };
        target.RaiseEvent(args);
    }

    private static LayoutService CreateService(
        BerthPaths paths,
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors,
        Action? afterWrite = null) =>
        new(
            desktop,
            monitors,
            store,
            paths,
            new OperationLock(paths, TimeSpan.FromSeconds(5)),
            new Log(paths, new FixedClock(FixedNow)),
            new FixedClock(FixedNow))
        {
            AfterWrite = afterWrite,
        };

    private MainViewModel CreateViewModel(LayoutStore store, IDesktopIcons desktop, IMonitors monitors, LayoutService service) =>
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
            timeZone: TimeZoneInfo.Utc);

    private static void SaveLayout(LayoutStore store, MonitorInfo[] monitors, string name, DateTimeOffset savedAt, params IconPosition[] icons)
    {
        string key = ArrangementKey.From(monitors);
        store.Save(new Layout(Layout.CurrentSchemaVersion, key, name, savedAt, [.. monitors], [.. icons], null));
    }

    private static void InvokeButton(WpfButton button)
    {
        var peer = new ButtonAutomationPeer(button);
        IInvokeProvider provider = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
        provider.Invoke();
        button.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
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
        Assert.Equal(400, decoder.Frames[0].PixelWidth);
    }

    private sealed class FakePresentationSource(Visual root) : PresentationSource
    {
        public override Visual RootVisual { get; set; } = root;

        public override bool IsDisposed => false;

        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
