using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;

namespace Berth.App;

/// <summary>
/// Hosts Berth's notification-area icon, switching between the dark and light taskbar variants on
/// <see cref="TaskbarTheme.Changed"/>, updating the one-line tooltip, and raising <see cref="OpenRequested"/> on
/// left click / left double-click (SPEC §12).
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public const string DarkIconResourceName = "Berth.Assets.berth-tray-dark.ico";
    public const string LightIconResourceName = "Berth.Assets.berth-tray-light.ico";

    private readonly TaskbarTheme _theme;
    private readonly Action<Action>? _marshal;
    private readonly NotifyIcon _notifyIcon;
    private readonly Lock _sync = new();
    private Icon? _currentIcon;
    private bool _disposed;

    public TrayIcon(
        TaskbarTheme theme,
        Layout? initialActive = null,
        DateTimeOffset? now = null,
        TimeZoneInfo? zone = null,
        Action<Action>? marshal = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;
        _marshal = marshal;
        _notifyIcon = new NotifyIcon();

        // Initialize icon and tooltip BEFORE setting Visible = true so Windows never flashes an empty icon or tooltip.
        ApplyIconForTheme();
        _notifyIcon.Text = TrayText.Tooltip(
            initialActive,
            now ?? DateTimeOffset.UtcNow,
            zone ?? TimeZoneInfo.Local);

        // WinForms NotifyIcon fires MouseClick(Left) on the first button-up of both a single left click and a left
        // double-click (suppressing MouseClick on the second button-up), so filtering MouseClick to MouseButtons.Left
        // handles both gestures once and ignores right/middle clicks and double-clicks.
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
        _theme.Changed += OnThemeChanged;
        _notifyIcon.Visible = true;
    }

    /// <summary>Raised when the user left-clicks or left-double-clicks the tray icon.</summary>
    public event Action? OpenRequested;

    internal NotifyIcon NotifyIcon => _notifyIcon;

    internal string TooltipText => _notifyIcon.Text;

    internal bool IsVisible => _notifyIcon.Visible;

    internal IntPtr IconHandle => _notifyIcon.Icon?.Handle ?? IntPtr.Zero;

    internal Size IconSize => _notifyIcon.Icon?.Size ?? Size.Empty;

    /// <summary>Attaches or clears the right-click context menu on the notification-area icon.</summary>
    internal void SetContextMenu(ContextMenuStrip? menu)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _notifyIcon.ContextMenuStrip = menu;
        }
    }

    /// <summary>Updates the tooltip text directly from <paramref name="active"/>.</summary>
    public void SetTooltip(Layout? active, DateTimeOffset now, TimeZoneInfo zone)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _notifyIcon.Text = TrayText.Tooltip(active, now, zone);
        }
    }

    /// <summary>Resolves the saved layout for the current monitors from <paramref name="store"/> and updates the tooltip.</summary>
    public void RefreshTooltip(LayoutStore store, IMonitors monitors, TimeProvider clock, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(zone);

        Layout? active = FindActiveLayout(store, monitors);
        SetTooltip(active, clock.GetUtcNow(), zone);
    }

    /// <summary>Updates the tooltip text directly if the icon has not been disposed; returns <see langword="false"/> if already disposed.</summary>
    internal bool TrySetTooltip(Layout? active, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            _notifyIcon.Text = TrayText.Tooltip(active, now, zone);
            return true;
        }
    }

    /// <summary>Refreshes the tooltip if the icon has not been disposed; returns <see langword="false"/> if already disposed.</summary>
    internal bool TryRefreshTooltip(LayoutStore store, IMonitors monitors, TimeProvider clock, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(zone);

        Layout? active = FindActiveLayout(store, monitors);
        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            _notifyIcon.Text = TrayText.Tooltip(active, clock.GetUtcNow(), zone);
            return true;
        }
    }

    /// <summary>Loads the embedded tray icon for <paramref name="isLightTaskbar"/> at <paramref name="size"/>.</summary>
    internal static Icon LoadEmbeddedIcon(bool isLightTaskbar, Size size)
    {
        string resourceName = isLightTaskbar ? LightIconResourceName : DarkIconResourceName;
        Assembly assembly = typeof(TrayIcon).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded tray icon resource '{resourceName}' was not found.");
        return new Icon(stream, size);
    }

    /// <summary>Finds the saved layout matching the current monitor arrangement via targeted <see cref="LayoutStore.Find"/>, or <see langword="null"/> if none exists or it is unreadable.</summary>
    internal static Layout? FindActiveLayout(LayoutStore store, IMonitors monitors)
    {
        try
        {
            IReadOnlyList<MonitorInfo> current = monitors.Read();
            if (current.Count == 0)
            {
                return null;
            }

            string key = ArrangementKey.From(current);
            return store.Find(key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DesktopUnavailableException or UnreadableLayoutException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _theme.Changed -= OnThemeChanged;
            _notifyIcon.MouseClick -= OnNotifyIconMouseClick;
            _notifyIcon.ContextMenuStrip = null;
            _notifyIcon.Visible = false;
            _notifyIcon.Icon = null;
            _notifyIcon.Dispose();
            _currentIcon?.Dispose();
            _currentIcon = null;
        }
    }

    private void OnThemeChanged()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        if (_marshal is not null)
        {
            _marshal(ApplyIconForTheme);
        }
        else
        {
            ApplyIconForTheme();
        }
    }

    private void ApplyIconForTheme()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            Icon next = LoadEmbeddedIcon(_theme.IsLight, SystemInformation.SmallIconSize);
            Icon? previous = _currentIcon;
            _currentIcon = next;
            _notifyIcon.Icon = next;
            previous?.Dispose();
        }
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        bool disposed;
        lock (_sync)
        {
            disposed = _disposed;
        }

        if (!disposed && e.Button == MouseButtons.Left)
        {
            OpenRequested?.Invoke();
        }
    }
}
