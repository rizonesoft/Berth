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
public sealed class RevertDeleteTests(AppFixture appFixture)
{
    private static readonly MonitorInfo MonSingle = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo MonDualA = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonDualB = new(2560, 0, 1920, 1080, 100, false);
    private static readonly MonitorInfo MonTriple = new(0, 0, 3840, 2160, 150, true);
    private static readonly DateTimeOffset Nov12 = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Nov18 = new(2026, 11, 18, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public async Task RevertSwapsTwiceWithoutMovingDesktopShowsWorkingAndHandlesStaleRefusals()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string key = ArrangementKey.From([MonSingle]);
        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            key,
            "Laptop",
            Nov18,
            [MonSingle],
            [new IconPosition("id-1", "One", 500, 500)],
            new PreviousSave(Nov12, [new IconPosition("id-1", "One", 100, 100)]));
        store.Save(layout);

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 500, 500));

        TaskCompletionSource? gate = null;
        var enteredGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LayoutService service = CreateService(
            paths,
            store,
            desktop,
            monitors,
            afterWrite: () =>
            {
                if (gate is { } g)
                {
                    enteredGate.TrySetResult();
                    g.Task.GetAwaiter().GetResult();
                }
            });
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        MainWindow? window = null;
        try
        {
            window = appFixture.Invoke(() =>
            {
                var w = new MainWindow { DataContext = vm };
                MeasureAndArrange(w);
                Assert.True(w.DetailHeaderControl.RevertMenuItem.IsEnabled);
                return w;
            });

            // 1. Hold first Revert at AfterWrite -> assert Working, StatusAction.None, and disabled header actions!
            gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            appFixture.Invoke(() =>
            {
                var peer = new MenuItemAutomationPeer(window!.DetailHeaderControl.RevertMenuItem);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
            });
            await enteredGate.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal("Working", vm.Status.Sentence);
            Assert.Equal(StatusAction.None, vm.Status.Action);
            Assert.False(vm.Detail.CanRestore);
            Assert.False(vm.Detail.CanSave);
            Assert.False(vm.Detail.CanMore);

            gate.TrySetResult();
            gate = null;
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Map and disk now hold Nov 12 (100,100); desktop SetPositions was NEVER called!
            Assert.Empty(desktop.Calls);
            Assert.Equal("Reverted to the save from 12 November.", vm.Status.Sentence);
            Assert.Equal(StatusAction.None, vm.Status.Action);
            Assert.Contains("12 November", vm.Detail.Subtitle);
            Assert.Equal(100, vm.Comparison!.Icons.Single().Saved.X);
            Layout afterFirst = store.Find(key)!;
            Assert.Equal(Nov12, afterFirst.SavedAt);
            Assert.Equal(Nov18, afterFirst.Previous!.SavedAt);

            // 2. Revert a second time -> swaps back to Nov 18 (500,500), still zero SetPositions calls!
            await vm.Detail.RevertAsync(key);
            Assert.Empty(desktop.Calls);
            Assert.Equal("Reverted to the save from 18 November.", vm.Status.Sentence);
            Assert.Contains("18 November", vm.Detail.Subtitle);
            Assert.Equal(500, vm.Comparison!.Icons.Single().Saved.X);
            Assert.Equal(Nov18, store.Find(key)!.SavedAt);

            // 3. Stale menu: layout without previous save -> NoPreviousSave with NO Retry; missing layout -> NoLayout with NO Retry!
            store.Save(new Layout(Layout.CurrentSchemaVersion, key, "Laptop", Nov18, [MonSingle], [new IconPosition("id-1", "One", 500, 500)], null));
            await vm.Detail.RevertAsync(key);
            Assert.Equal("This layout has no previous save.", vm.Status.Sentence);
            Assert.Equal(StatusAction.None, vm.Status.Action);

            File.Delete(paths.LayoutFile(key));
            await vm.Detail.RevertAsync(key);
            Assert.Equal("This monitor arrangement has no saved layout.", vm.Status.Sentence);
            Assert.Equal(StatusAction.None, vm.Status.Action);
        }
        finally
        {
            gate?.TrySetResult();
            if (window is not null)
            {
                appFixture.Invoke(() => window.CloseForShutdown());
            }
        }
    }

    [Fact]
    public async Task RevertAndDeleteRetriesAreIdempotentAndNeverReverseSwapOrDeleteReplacementLayout()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string key = ArrangementKey.From([MonSingle]);
        store.Save(new Layout(
            Layout.CurrentSchemaVersion,
            key,
            "Laptop",
            Nov18,
            [MonSingle],
            [new IconPosition("id-1", "One", 500, 500)],
            new PreviousSave(Nov12, [new IconPosition("id-1", "One", 100, 100)])));

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 500, 500));
        bool tamperOnce = false;
        bool recreateReplacementOnce = false;
        LayoutService service = CreateService(
            paths,
            store,
            desktop,
            monitors,
            afterWrite: () =>
            {
                if (tamperOnce)
                {
                    tamperOnce = false;
                    // Corrupt file during read-back check so Revert returns WriteFailed.
                    File.WriteAllText(paths.LayoutFile(key), "{ corrupted");
                }
                else if (recreateReplacementOnce)
                {
                    recreateReplacementOnce = false;
                    // Write a brand-new replacement layout under the same key with a newer SavedAt!
                    store.Save(new Layout(
                        Layout.CurrentSchemaVersion,
                        key,
                        "BrandNew",
                        FixedNow,
                        [MonSingle],
                        [new IconPosition("id-1", "One", 999, 999)],
                        null));
                }
            });
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

        // 1. Revert fails read-back -> Retry offered; suppose the swapped Nov12 layout actually landed on disk -> Retry MUST NOT swap it back!
        tamperOnce = true;
        await vm.Detail.RevertAsync(key);
        Assert.Equal(StatusAction.Retry, vm.Status.Action);

        File.Delete(paths.LayoutFile(key));
        store.Save(new Layout(
            Layout.CurrentSchemaVersion,
            key,
            "Laptop",
            Nov12,
            [MonSingle],
            [new IconPosition("id-1", "One", 100, 100)],
            new PreviousSave(Nov18, [new IconPosition("id-1", "One", 500, 500)])));

        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("Reverted to the save from 12 November.", vm.Status.Sentence);
        Assert.Equal(Nov12, store.Find(key)!.SavedAt);

        // 2. Delete read-back sees a same-second replacement layout (different icons, SAME SavedAt!) -> Retry MUST refuse and keep "BrandNew"!
        recreateReplacementOnce = true;
        Assert.True(vm.Detail.TryStartDelete(key, store.Find(key)));
        await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(StatusAction.Retry, vm.Status.Action);

        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("This monitor arrangement has no saved layout.", vm.Status.Sentence);
        Assert.Equal("BrandNew", store.Find(key)!.Name);

        // 3. Delete Retry on an unreadable file MUST report unreadable (never fake Deleted success)!
        Layout brandNew = store.Find(key)!;
        recreateReplacementOnce = true;
        Assert.True(vm.Detail.TryStartDelete(key, brandNew));
        await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        File.WriteAllText(paths.LayoutFile(key), "{ unreadable");
        vm.Status.ActionCommand.Execute(null);
        await vm.Status.LastActionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains("could not be read. It was left untouched.", vm.Status.Sentence);
        Assert.True(File.Exists(paths.LayoutFile(key)));
    }

    [Fact]
    public async Task DeleteDialogDefaultsToCancelAndCancelEscEnterAndClosePreserveExactFileBytes()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string key = ArrangementKey.From([MonSingle]);
        SaveLayout(store, [MonSingle], "Docked", Nov18, new IconPosition("id-1", "One", 100, 100));
        byte[] originalBytes = File.ReadAllBytes(paths.LayoutFile(key));

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

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

            void OpenDeleteModalAndRun(Action<DeleteDialog> interact)
            {
                modalError = null;
                bool dialogRan = false;
                appFixture.Invoke(() =>
                {
                    var peer = new MenuItemAutomationPeer(window!.DetailHeaderControl.DeleteMenuItem);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();

                    WpfApplication.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        DeleteDialog? dialog = WpfApplication.Current.Windows.OfType<DeleteDialog>().SingleOrDefault(d => d.IsVisible);
                        if (dialog is null)
                        {
                            return;
                        }

                        try
                        {
                            dialogRan = true;
                            interact(dialog);
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

                Assert.True(dialogRan, "Expected DeleteDialog to open and run interaction.");
            }

            // 1. Inspect copy, Cancel=IsDefault+IsCancel, Delete!=IsDefault, live busy state, then click Cancel!
            OpenDeleteModalAndRun(dialog =>
            {
                Assert.Same(window, dialog.Owner);
                Assert.Equal("Delete layout", dialog.Title);
                Assert.Equal(
                    "Delete the layout \"Docked\"? Its saved positions are removed. This cannot be undone.",
                    dialog.MessageText.Text);
                Assert.True(dialog.CancelButton.IsDefault);
                Assert.True(dialog.CancelButton.IsCancel);
                Assert.False(dialog.DeleteButton.IsDefault);
                Assert.True(dialog.CancelButton.IsFocused);

                vm.Detail.SetExternalBusy(true);
                Assert.False(dialog.DeleteButton.IsEnabled);
                Assert.True(dialog.CancelButton.IsEnabled);
                Assert.Equal(Messages.Refusal(OperationResult.Refused(Refusal.Busy)), dialog.BusyErrorText.Text);
                vm.Detail.SetExternalBusy(false);
                Assert.True(dialog.DeleteButton.IsEnabled);

                InvokeButton(dialog.CancelButton);
            });
            Assert.Equal(originalBytes, File.ReadAllBytes(paths.LayoutFile(key)));

            // 2. Esc cancels!
            OpenDeleteModalAndRun(_ => AccessKeyManager.ProcessKey(null, "\x1B", false));
            Assert.Equal(originalBytes, File.ReadAllBytes(paths.LayoutFile(key)));

            // 3. Enter with Cancel focused cancels!
            OpenDeleteModalAndRun(dialog => RaiseKey(dialog, dialog.CancelButton, Key.Enter));
            Assert.Equal(originalBytes, File.ReadAllBytes(paths.LayoutFile(key)));

            // 4. Enter even with Delete focused STILL cancels (SPEC §10 safety)!
            OpenDeleteModalAndRun(dialog =>
            {
                dialog.DeleteButton.Focus();
                RaiseKey(dialog, dialog.DeleteButton, Key.Enter);
            });
            Assert.Equal(originalBytes, File.ReadAllBytes(paths.LayoutFile(key)));

            // 5. Title-bar Close() cancels!
            OpenDeleteModalAndRun(dialog => dialog.Close());
            Assert.Equal(originalBytes, File.ReadAllBytes(paths.LayoutFile(key)));
            Assert.Empty(desktop.Calls);
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
    public async Task DeleteNonActiveSelectsActiveAndPreservesAdjacentAndDeleteActiveSelectsThisArrangement()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        string keyActive = ArrangementKey.From([MonSingle]);
        string keyOtherA = ArrangementKey.From([MonDualA, MonDualB]);
        string keyOtherB = ArrangementKey.From([MonTriple]);
        SaveLayout(store, [MonSingle], "Laptop", Nov18, new IconPosition("id-1", "One", 100, 100));
        SaveLayout(store, [MonDualA, MonDualB], "Docked", Nov18, new IconPosition("id-1", "One", 200, 200));
        SaveLayout(store, [MonTriple], "Studio", Nov18, new IconPosition("id-1", "One", 300, 300));

        var monitors = new FakeMonitors(MonSingle);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 100, 100));
        LayoutService service = CreateService(paths, store, desktop, monitors);
        var vm = CreateViewModel(store, desktop, monitors, service);
        await vm.RefreshAsync();

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

            void ConfirmDeleteThroughModal()
            {
                modalError = null;
                appFixture.Invoke(() =>
                {
                    var peer = new MenuItemAutomationPeer(window!.DetailHeaderControl.DeleteMenuItem);
                    ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();

                    WpfApplication.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        DeleteDialog? dialog = WpfApplication.Current.Windows.OfType<DeleteDialog>().SingleOrDefault(d => d.IsVisible);
                        if (dialog is null)
                        {
                            return;
                        }

                        try
                        {
                            InvokeButton(dialog.DeleteButton);
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
            }

            // 1. Select non-active "Docked" (adjacent to "Studio"), delete -> selects active "Laptop", "Studio" survives!
            appFixture.Invoke(() => vm.Selected = vm.Rows.Single(r => r.Key == keyOtherA));
            ConfirmDeleteThroughModal();
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Null(store.Find(keyOtherA));
            Assert.NotNull(store.Find(keyOtherB));
            Assert.Equal(keyActive, vm.Selected!.Key);
            Assert.Equal("Every icon is at its saved position.", vm.Status.Sentence);
            Assert.Empty(desktop.Calls);

            // 2. Delete active "Laptop" -> file removed, selection becomes "This arrangement", desktop untouched!
            ConfirmDeleteThroughModal();
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Null(store.Find(keyActive));
            Assert.True(vm.Selected!.IsUnsaved);
            Assert.Equal("This arrangement", vm.Selected.Name);
            Assert.Equal("This monitor arrangement has no saved layout yet.", vm.Status.Sentence);
            Assert.Empty(desktop.Calls);

            // 3. Open More menu on "Studio", remove "Studio" on disk and refresh so selection falls back, then invoke Revert -> targets "Studio" (NoLayout), not fallback!
            SaveLayout(store, [MonSingle], "LaptopFresh", Nov18, new IconPosition("id-1", "One", 100, 100));
            store.Save(new Layout(
                Layout.CurrentSchemaVersion,
                keyActive,
                "LaptopFresh",
                Nov18,
                [MonSingle],
                [new IconPosition("id-1", "One", 500, 500)],
                new PreviousSave(Nov12, [new IconPosition("id-1", "One", 100, 100)])));
            store.Save(new Layout(
                Layout.CurrentSchemaVersion,
                keyOtherB,
                "Studio",
                Nov18,
                [MonTriple],
                [new IconPosition("id-1", "One", 300, 300)],
                new PreviousSave(Nov12, [new IconPosition("id-1", "One", 50, 50)])));
            await vm.RefreshAsync();
            appFixture.Invoke(() =>
            {
                vm.Selected = vm.Rows.Single(r => r.Key == keyOtherB);
                InvokeButton(window!.DetailHeaderControl.MoreButton);
            });

            // Delete Studio and refresh while More menu is open (selection falls back to LaptopFresh).
            File.Delete(paths.LayoutFile(keyOtherB));
            await vm.RefreshAsync();
            Assert.Equal(keyActive, vm.Selected!.Key);

            appFixture.Invoke(() =>
            {
                var peer = new MenuItemAutomationPeer(window!.DetailHeaderControl.RevertMenuItem);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.Input);
            });
            await vm.Detail.LastOperationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // Must report NoLayout for captured Studio key and leave LaptopFresh at Nov18 untouched!
            Assert.Equal("This monitor arrangement has no saved layout.", vm.Status.Sentence);
            Assert.Equal(Nov18, store.Find(keyActive)!.SavedAt);
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
    public void RendersDeleteDialogInLightAndDarkAndVerifies40CharNameAt150PercentHighContrast()
    {
        string outputDir = ResolveRenderOutputDirectory();
        string lightPath = Path.Combine(outputDir, "delete-dialog-light.png");
        string darkPath = Path.Combine(outputDir, "delete-dialog-dark.png");

        appFixture.Invoke(() =>
        {
            var dialog = new DeleteDialog(new DeleteDialogViewModel("Docked"));
            try
            {
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

            // 40-character layout name at 150% DPI in High Contrast wraps without clipping.
            string forty = new string('M', 40);
            var hcDialog = new DeleteDialog(new DeleteDialogViewModel(forty));
            var berthApp = (BerthApplication)WpfApplication.Current;
            ColorTheme saved = berthApp.CurrentColorTheme;
            try
            {
                berthApp.ApplyColorTheme(ColorTheme.HighContrast);
                VisualTreeHelper.SetRootDpi(hcDialog.DialogRoot, new DpiScale(1.5, 1.5));
                hcDialog.DialogRoot.Measure(new WpfSize(400, double.PositiveInfinity));
                double h = hcDialog.DialogRoot.DesiredSize.Height;
                hcDialog.DialogRoot.Arrange(new Rect(0, 0, 400, h));
                hcDialog.DialogRoot.UpdateLayout();
                Assert.True(hcDialog.DeleteButton.ActualHeight >= 32d);
                Assert.True(hcDialog.CancelButton.ActualHeight >= 32d);
                Rect msgBounds = hcDialog.MessageText.TransformToAncestor(hcDialog.DialogRoot)
                    .TransformBounds(new Rect(0, 0, hcDialog.MessageText.ActualWidth, hcDialog.MessageText.ActualHeight));
                // 40-char confirmation sentence fits at 150% scale without clipping inside DialogRoot!
                Assert.True(hcDialog.MessageText.ActualHeight >= hcDialog.MessageText.DesiredSize.Height - 0.5d);
                Assert.True(msgBounds.Right <= hcDialog.DialogRoot.ActualWidth + 0.5d);
                Assert.True(msgBounds.Bottom <= hcDialog.DialogRoot.ActualHeight + 0.5d);
            }
            finally
            {
                hcDialog.Close();
                berthApp.ApplyColorTheme(saved);
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

    private static void MeasureAndArrange(MainWindow window, double width = 980, double height = 640)
    {
        window.RootGrid.Measure(new WpfSize(width, height));
        window.RootGrid.Arrange(new Rect(0, 0, width, height));
        window.RootGrid.UpdateLayout();
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
