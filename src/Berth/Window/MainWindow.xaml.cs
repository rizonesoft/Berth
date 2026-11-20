using System.ComponentModel;
using System.Windows;

namespace Berth.Window;

/// <summary>
/// Berth's two-pane main window frame (SPEC §4): hides to the tray when closed unless shutting down, ignores Escape,
/// and brings itself cleanly to the front when activated from the tray or a second launch.
/// </summary>
internal sealed partial class MainWindow : System.Windows.Window, IMainWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>When <see langword="true"/>, closing the window actually closes it instead of hiding to the tray.</summary>
    internal bool AllowClose { get; set; }

    /// <summary>Optional test seam overriding <see cref="System.Windows.Window.Activate"/> to test activation refusal deterministically.</summary>
    internal Func<bool>? ActivateOverride { get; set; }

    /// <summary>Optional test observer invoked whenever <see cref="BringToFront"/> toggles <see cref="System.Windows.Window.Topmost"/>.</summary>
    internal Action<bool>? TopmostChangedForTest { get; set; }

    /// <summary>Shows the window, restores it if minimized, activates it, and falls back to a brief Topmost toggle if activation is refused.</summary>
    public void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (!TryActivate())
        {
            bool wasTopmost = Topmost;
            if (!wasTopmost)
            {
                Topmost = true;
                TopmostChangedForTest?.Invoke(true);
                Topmost = false;
                TopmostChangedForTest?.Invoke(false);
            }

            _ = TryActivate();
        }
    }

    /// <summary>Allows the window to close and closes it as part of application shutdown.</summary>
    public void CloseForShutdown()
    {
        AllowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private bool TryActivate() => ActivateOverride is not null ? ActivateOverride() : Activate();
}
