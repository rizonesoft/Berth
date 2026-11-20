using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.Window;

/// <summary>
/// Berth's two-pane main window frame (SPEC §4): hides to the tray when closed unless shutting down, ignores Escape,
/// remembers its physical-pixel placement across hide/reopen and display changes, and brings itself cleanly to the
/// front when activated from the tray or a second launch.
/// </summary>
internal sealed partial class MainWindow : System.Windows.Window, IMainWindow
{
    private readonly SettingsStore? _settingsStore;
    private readonly IMonitors? _monitors;
    private readonly Log? _log;
    private readonly IWindowBoundsInterop _boundsInterop;
    private readonly double _defaultWidthDips;
    private readonly double _defaultHeightDips;
    private bool _hasInitializedPlacement;

    public MainWindow()
        : this(settingsStore: null, monitors: null, log: null, boundsInterop: null)
    {
    }

    internal MainWindow(
        SettingsStore? settingsStore,
        IMonitors? monitors,
        Log? log,
        IWindowBoundsInterop? boundsInterop = null)
    {
        _settingsStore = settingsStore;
        _monitors = monitors;
        _log = log;
        _boundsInterop = boundsInterop ?? NativeWindowBoundsInterop.Instance;

        InitializeComponent();

        _defaultWidthDips = double.IsFinite(Width) && Width > 0d ? Width : 980d;
        _defaultHeightDips = double.IsFinite(Height) && Height > 0d ? Height : 640d;

        if (_settingsStore is not null || _monitors is not null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
    }

    /// <summary>When <see langword="true"/>, closing the window actually closes it instead of hiding to the tray.</summary>
    internal bool AllowClose { get; set; }

    /// <summary>Optional test seam overriding <see cref="System.Windows.Window.Activate"/> to test activation refusal deterministically.</summary>
    internal Func<bool>? ActivateOverride { get; set; }

    /// <summary>Shows the window (revalidating bounds on hidden-to-visible transitions), restores it if minimized, activates it, and falls back to a brief Topmost toggle if activation is refused.</summary>
    public void BringToFront()
    {
        bool wasHidden = !IsVisible;
        if (wasHidden && _hasInitializedPlacement && (_settingsStore is not null || _monitors is not null))
        {
            RevalidateCurrentPlacementIfHidden();
        }

        if (wasHidden)
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
                Topmost = false;
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!_hasInitializedPlacement && (_settingsStore is not null || _monitors is not null))
        {
            _hasInitializedPlacement = true;
            ApplyInitialPlacement();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnClosing(e);
        if (!AllowClose)
        {
            SavePlacementOnHide();
            e.Cancel = true;
            Hide();
        }
    }

    private void ApplyInitialPlacement()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        WindowPlacement? saved = null;
        if (_settingsStore is not null)
        {
            SettingsLoad loaded = _settingsStore.Load();
            if (loaded.Unreadable is null)
            {
                saved = loaded.Value.Window;
            }
        }

        IReadOnlyList<MonitorInfo> currentMonitors = ReadMonitorsSafe();
        WindowPlacement? resolved = WindowPlacementRule.ResolveForDipsMinimum(
            saved,
            currentMonitors,
            MinWidth,
            MinHeight);

        WindowPlacement? target = resolved ?? ComputePrimaryFallback(currentMonitors);
        if (target is not null)
        {
            _ = _boundsInterop.TrySetWindowRect(hwnd, target);
        }
    }

    private void RevalidateCurrentPlacementIfHidden()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        IReadOnlyList<MonitorInfo> currentMonitors = ReadMonitorsSafe();
        WindowPlacement? currentNormal = _boundsInterop.TryGetNormalPlacement(hwnd, out WindowPlacement captured)
            ? captured
            : null;

        WindowPlacement? resolved = WindowPlacementRule.ResolveForDipsMinimum(
            currentNormal,
            currentMonitors,
            MinWidth,
            MinHeight);

        if (resolved is null)
        {
            WindowPlacement? fallback = ComputePrimaryFallback(currentMonitors);
            if (fallback is not null)
            {
                _ = _boundsInterop.TrySetWindowRect(hwnd, fallback);
            }
        }
    }

    private void SavePlacementOnHide()
    {
        if (_settingsStore is null)
        {
            return;
        }

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !_boundsInterop.TryGetNormalPlacement(hwnd, out WindowPlacement normalRect))
        {
            return;
        }

        try
        {
            _ = _settingsStore.Update(s => s with { Window = normalRect });
        }
        catch (UnreadableSettingsException)
        {
            _log?.Warn(Messages.UnreadableSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TargetChangedException or TargetAppearedException)
        {
            _log?.Warn(Messages.WriteFailed(_settingsStore.FilePath, ex.Message));
        }
    }

    private IReadOnlyList<MonitorInfo> ReadMonitorsSafe()
    {
        if (_monitors is null)
        {
            return [];
        }

        try
        {
            return _monitors.Read();
        }
        catch (DesktopUnavailableException)
        {
            return [];
        }
    }

    private WindowPlacement? ComputePrimaryFallback(IReadOnlyList<MonitorInfo> currentMonitors)
    {
        WindowPlacement? primaryWorkArea = _boundsInterop.TryGetPrimaryWorkArea(out WindowPlacement work)
            ? work
            : null;
        return WindowPlacementRule.CenterOnPrimary(
            currentMonitors,
            primaryWorkArea,
            _defaultWidthDips,
            _defaultHeightDips);
    }

    private bool TryActivate() => ActivateOverride is not null ? ActivateOverride() : Activate();
}
