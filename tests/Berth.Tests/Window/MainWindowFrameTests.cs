using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Berth.Tests.App;
using Berth.Window;

namespace Berth.Tests.Window;

[Collection(DesktopCollectionDefinition.Name)]
public sealed class MainWindowFrameTests(AppFixture appFixture)
{
    [Fact]
    public void FrameUsesSpecTitleIconTokensDividerAndPreservesLeftPaneOnResize()
    {
        appFixture.Invoke(() =>
        {
            var window = new MainWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal("Berth", window.Title);
                Assert.NotNull(window.Icon);

                // Exact DIP token checks independent of host DPI scale.
                Assert.Equal(980d, window.Width);
                Assert.Equal(640d, window.Height);
                Assert.Equal(760d, window.MinWidth);
                Assert.Equal(520d, window.MinHeight);
                Assert.Equal(250d, window.LayoutsHost.Width);
                Assert.Equal(1d, window.PaneDivider.Width);

                // Verify layout column placement and named hosts.
                Assert.Equal(0, Grid.GetColumn(window.LayoutsHost));
                Assert.Equal(1, Grid.GetColumn(window.PaneDivider));
                Assert.Equal(2, Grid.GetColumn(window.DetailHost));

                // Verify Fluent divider brush resolves non-null and matches theme resource.
                object? expectedBrush = System.Windows.Application.Current.TryFindResource("DividerStrokeColorDefaultBrush");
                Assert.IsAssignableFrom<System.Windows.Media.Brush>(expectedBrush);
                Assert.Same(expectedBrush, window.PaneDivider.Background);

                // At 100% DPI scale, rendered ActualWidth/ActualHeight match token values exactly;
                // at fractional DPI, allow <= 1 DIP device-pixel rounding tolerance.
                DpiScale dpi = VisualTreeHelper.GetDpi(window);
                double tolerance = Math.Abs(dpi.DpiScaleX - 1.0) < 0.001 && Math.Abs(dpi.DpiScaleY - 1.0) < 0.001 ? 0.0 : 1.0;

                Assert.InRange(window.ActualWidth, 980d - tolerance, 980d + tolerance);
                Assert.InRange(window.ActualHeight, 640d - tolerance, 640d + tolerance);
                Assert.InRange(window.LayoutsColumn.ActualWidth, 250d - tolerance, 250d + tolerance);
                Assert.InRange(window.LayoutsHost.ActualWidth, 250d - tolerance, 250d + tolerance);
                Assert.True(window.PaneDivider.ActualWidth > 0d, "PaneDivider must have a positive rendered width.");
                Assert.InRange(window.PaneDivider.ActualWidth, 1d - tolerance, 1d + tolerance);

                double initialDetailWidth = window.DetailHost.ActualWidth;
                Assert.True(initialDetailWidth > 0d, "DetailHost should fill the remaining width.");

                // Resizing wider must grow DetailHost while preserving the 250 DIP LayoutsHost.
                window.Width = 1100d;
                window.UpdateLayout();

                Assert.InRange(window.LayoutsHost.ActualWidth, 250d - tolerance, 250d + tolerance);
                Assert.True(
                    window.DetailHost.ActualWidth > initialDetailWidth + 100d,
                    $"Expected DetailHost width to grow on resize (before={initialDetailWidth}, after={window.DetailHost.ActualWidth}).");
            }
            finally
            {
                window.AllowClose = true;
                window.Close();
            }
        });
    }

    [Fact]
    public void ClosingRaisesClosingEventHidesWindowAndKeepsItReusableByShow()
    {
        appFixture.Invoke(() =>
        {
            var window = new MainWindow();
            int closingEvents = 0;
            window.Closing += (_, _) => closingEvents++;
            try
            {
                window.Show();
                Assert.True(window.IsVisible);

                window.Close();
                Assert.Equal(1, closingEvents);
                Assert.False(window.IsVisible);

                // Must still be usable after Close() hid it.
                window.Show();
                Assert.True(window.IsVisible);
            }
            finally
            {
                window.AllowClose = true;
                window.Close();
            }
        });
    }

    [Fact]
    public void EscapeKeyDoesNotHideOrCloseWindow()
    {
        appFixture.Invoke(() =>
        {
            var window = new MainWindow();
            try
            {
                window.Show();
                Assert.True(window.IsVisible);

                PresentationSource? source = PresentationSource.FromVisual(window);
                Assert.NotNull(source);

                var args = new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
                {
                    RoutedEvent = UIElement.KeyDownEvent,
                };
                window.RaiseEvent(args);

                Assert.True(window.IsVisible);
            }
            finally
            {
                window.AllowClose = true;
                window.Close();
            }
        });
    }

    [Fact]
    public void BringToFrontRestoresMinimizedWindowAndTogglesTopmostOnlyOnActivationRefusal()
    {
        appFixture.Invoke(() =>
        {
            var window = new MainWindow();
            List<bool> topmostEvents = [];
            DependencyPropertyDescriptor topmostDescriptor = DependencyPropertyDescriptor.FromProperty(
                System.Windows.Window.TopmostProperty,
                typeof(MainWindow))!;
            EventHandler onTopmostChanged = (_, _) => topmostEvents.Add(window.Topmost);
            topmostDescriptor.AddValueChanged(window, onTopmostChanged);
            try
            {
                window.Show();
                window.WindowState = WindowState.Minimized;

                // Case 1: Activation accepted -> restores Normal, visible, NO Topmost toggle.
                window.ActivateOverride = () => true;
                window.BringToFront();

                Assert.True(window.IsVisible);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.False(window.Topmost);
                Assert.Empty(topmostEvents);

                // Case 2: Activation refused -> briefly sets Topmost=true then clears Topmost=false.
                window.WindowState = WindowState.Minimized;
                window.ActivateOverride = () => false;
                window.BringToFront();

                Assert.True(window.IsVisible);
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.False(window.Topmost);
                Assert.Equal([true, false], topmostEvents);
            }
            finally
            {
                topmostDescriptor.RemoveValueChanged(window, onTopmostChanged);
                window.AllowClose = true;
                window.Close();
            }
        });
    }

    [Fact]
    public void TrayAppFirstRunCreatesOnceCallsBringToFrontFourTimesOnUiThreadAndNotifiesFirstOpenOnce()
    {
        using var data = new TestData();
        string reportFile = Path.Combine(data.Root, "wiring-first-run.txt");
        string instanceName = $"Berth.Tests.WiringFirstRun.{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.TrayAppWiringFirstRunScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = ParseReport(reportFile);

        Assert.Equal("1", report["createsAtStartup"]);
        Assert.Equal("1", report["callbacksAtStartup"]);
        Assert.Equal("1", report["createCount"]);
        Assert.Equal("4", report["bringToFrontCount"]);
        Assert.Equal("1", report["onWindowOpenedCount"]);
        Assert.Equal("True", report["allOnUiThread"]);
        Assert.Equal("create,bringToFront,onWindowOpened,bringToFront,bringToFront,bringToFront", report["trace"]);
        Assert.Equal("0", report["exitCode"]);
    }

    [Fact]
    public void TrayAppTrayOnlyDefersWindowCreationUntilFirstOpenEventAndMarshalsWorkerActivationToUiThread()
    {
        using var data = new TestData();
        string reportFile = Path.Combine(data.Root, "wiring-tray-only.txt");
        string instanceName = $"Berth.Tests.WiringTrayOnly.{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.TrayAppWiringTrayOnlyScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = ParseReport(reportFile);

        Assert.Equal("0", report["createsAtStartup"]);
        Assert.Equal("0", report["callbacksAtStartup"]);
        Assert.Equal("1", report["createCount"]);
        Assert.Equal("3", report["bringToFrontCount"]);
        Assert.Equal("1", report["onWindowOpenedCount"]);
        Assert.Equal("True", report["allOnUiThread"]);
        Assert.Equal("create,bringToFront,onWindowOpened,bringToFront,bringToFront", report["trace"]);
        Assert.Equal("0", report["exitCode"]);
    }

    [Fact]
    public void HiddenAndReopenedMainWindowExitsCleanlyFromTrayMenuExit()
    {
        using var data = new TestData();
        string reportFile = Path.Combine(data.Root, "window-menu-exit.txt");
        string instanceName = $"Berth.Tests.WindowMenuExit.{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.WindowHideReopenAndMenuExitScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = ParseReport(reportFile);

        Assert.Equal("True", report["visibleAfterFirstOpen"]);
        Assert.Equal("False", report["visibleAfterClose"]);
        Assert.Equal("True", report["visibleAfterMenuReopen"]);
        Assert.Equal("0", report["exitCode"]);
    }

    [Fact]
    public void ExitDuringInFlightOperationBlocksTrayClickAndSecondInstanceActivationFromReopeningWindow()
    {
        using var data = new TestData();
        string reportFile = Path.Combine(data.Root, "exit-drain-activation.txt");
        string instanceName = $"Berth.Tests.ExitDrainAct.{Guid.NewGuid():N}";

        (int exitCode, string stdout, string stderr) = IsolatedStaChildRunner.RunChild(
            IsolatedStaChildRunner.ExitBlocksActivationDuringDrainScenario,
            data.Root,
            instanceName,
            reportFile: reportFile);

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. stdout={stdout} stderr={stderr}");
        Dictionary<string, string> report = ParseReport(reportFile);

        Assert.Equal("False", report["visibleAfterDrainActivation"]);
        Assert.Equal("0", report["exitCode"]);
    }

    private static Dictionary<string, string> ParseReport(string path)
    {
        Assert.True(File.Exists(path), $"Expected report file at '{path}'.");
        return File.ReadAllLines(path)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }
}
