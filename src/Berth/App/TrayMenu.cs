using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.App;

/// <summary>
/// Right-click notification-area menu (SPEC §12): six entries in SPEC order, system renderer, live state refreshed on
/// <see cref="ContextMenuStrip.Opening"/> (with shell <see cref="IDesktopIcons.AutoArrange"/> read off the UI thread so
/// opening never freezes), background execution for desktop operations, and graceful drain on Exit.
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    private readonly LayoutService _service;
    private readonly LayoutStore _store;
    private readonly IMonitors _monitors;
    private readonly IDesktopIcons _desktop;
    private readonly SettingsStore _settingsStore;
    private readonly SettingChanges _settingChanges;
    private readonly Action<Action> _marshal;

    private readonly ContextMenuStrip _strip;
    private readonly ToolStripMenuItem _saveItem;
    private readonly ToolStripMenuItem _restoreItem;
    private readonly ToolStripMenuItem _restoreAutomaticallyItem;
    private readonly ToolStripSeparator _separator;
    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly Font _boldFont;

    private readonly Lock _sync = new();
    private int _inFlightCount;
    private long _openGeneration;
    private Task _lastRefreshTask = Task.CompletedTask;
    private bool _exiting;
    private bool _disposed;
    private Action? _whenIdle;

    public TrayMenu(
        LayoutService service,
        LayoutStore store,
        IMonitors monitors,
        IDesktopIcons desktop,
        SettingsStore settingsStore,
        Log log,
        Action<Action> marshal)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(marshal);

        _service = service;
        _store = store;
        _monitors = monitors;
        _desktop = desktop;
        _settingsStore = settingsStore;
        _settingChanges = new SettingChanges(settingsStore, log);
        _marshal = marshal;

        _strip = new ContextMenuStrip
        {
            RenderMode = ToolStripRenderMode.System,
        };

        _saveItem = new ToolStripMenuItem("Save layout");
        _restoreItem = new ToolStripMenuItem("Restore layout");
        _restoreAutomaticallyItem = new ToolStripMenuItem("Restore automatically")
        {
            CheckOnClick = false,
        };
        _separator = new ToolStripSeparator();
        _openItem = new ToolStripMenuItem("Open Berth");
        _boldFont = new Font(_openItem.Font, FontStyle.Bold);
        _openItem.Font = _boldFont;
        _exitItem = new ToolStripMenuItem("Exit");

        _saveItem.Click += OnSaveClick;
        _restoreItem.Click += OnRestoreClick;
        _restoreAutomaticallyItem.Click += OnRestoreAutomaticallyClick;
        _openItem.Click += OnOpenClick;
        _exitItem.Click += OnExitClick;

        _strip.Items.AddRange(
        [
            _saveItem,
            _restoreItem,
            _restoreAutomaticallyItem,
            _separator,
            _openItem,
            _exitItem,
        ]);

        _strip.Opening += OnStripOpening;
    }

    /// <summary>Raised on the UI thread after a Save or Restore triggered from the menu finishes.</summary>
    public event Action<OperationResult>? OperationFinished;

    /// <summary>Raised when the user clicks "Open Berth".</summary>
    public event Action? OpenRequested;

    /// <summary>Raised when the user clicks "Exit".</summary>
    public event Action? ExitRequested;

    internal ContextMenuStrip Strip => _strip;

    internal ToolStripMenuItem SaveItem => _saveItem;

    internal ToolStripMenuItem RestoreItem => _restoreItem;

    internal ToolStripMenuItem RestoreAutomaticallyItem => _restoreAutomaticallyItem;

    internal ToolStripSeparator Separator => _separator;

    internal ToolStripMenuItem OpenItem => _openItem;

    internal ToolStripMenuItem ExitItem => _exitItem;

    internal Task LastRefreshTask
    {
        get
        {
            lock (_sync)
            {
                return _lastRefreshTask;
            }
        }
    }

    internal bool HasInFlightOperations
    {
        get
        {
            lock (_sync)
            {
                return _inFlightCount > 0;
            }
        }
    }

    /// <summary>
    /// Refreshes the dynamic enabled and checked states for the current arrangement and settings (SPEC §12).
    /// Queries <see cref="IDesktopIcons.AutoArrange"/> off the UI thread while keeping Restore disabled until the
    /// check finishes, discarding results from superseded openings.
    /// </summary>
    internal void RefreshState()
    {
        long generation;
        bool exiting;
        lock (_sync)
        {
            generation = ++_openGeneration;
            exiting = _exiting || _disposed;
        }

        if (exiting)
        {
            _saveItem.Enabled = false;
            _restoreItem.Enabled = false;
            _restoreAutomaticallyItem.Enabled = false;
            _openItem.Enabled = false;
            _exitItem.Enabled = false;
            lock (_sync)
            {
                _lastRefreshTask = Task.CompletedTask;
            }

            return;
        }

        SettingsLoad loaded = _settingsStore.Load();
        _restoreAutomaticallyItem.Checked = loaded.Value.RestoreAutomatically;

        // Keep Restore disabled while the asynchronous shell check is pending so Opening never freezes the UI thread.
        _restoreItem.Enabled = false;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _lastRefreshTask = tcs.Task;
        }

        _ = Task.Run(() =>
        {
            bool canRestore = CanRestoreCurrentArrangement();
            _marshal(() =>
            {
                try
                {
                    bool apply;
                    lock (_sync)
                    {
                        apply = !_disposed && !_exiting && generation == _openGeneration;
                    }

                    if (apply)
                    {
                        _restoreItem.Enabled = canRestore;
                    }
                }
                finally
                {
                    tcs.TrySetResult();
                }
            });
        });
    }

    /// <summary>
    /// Stops accepting new menu clicks and runs <paramref name="onIdle"/> on the UI thread immediately if no
    /// background menu operation is running, or as soon as all in-flight operations have delivered their completion.
    /// </summary>
    internal void BeginDrain(Action onIdle)
    {
        ArgumentNullException.ThrowIfNull(onIdle);

        bool runNow;
        lock (_sync)
        {
            _exiting = true;
            _openGeneration++;
            if (_inFlightCount == 0)
            {
                runNow = true;
                _whenIdle = null;
            }
            else
            {
                runNow = false;
                _whenIdle += onIdle;
            }
        }

        if (runNow)
        {
            onIdle();
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
            _exiting = true;
            _openGeneration++;
            _whenIdle = null;
        }

        _strip.Opening -= OnStripOpening;
        _saveItem.Click -= OnSaveClick;
        _restoreItem.Click -= OnRestoreClick;
        _restoreAutomaticallyItem.Click -= OnRestoreAutomaticallyClick;
        _openItem.Click -= OnOpenClick;
        _exitItem.Click -= OnExitClick;

        _strip.Dispose();
        _boldFont.Dispose();
    }

    private void OnStripOpening(object? sender, CancelEventArgs e)
    {
        RefreshState();
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        RunOperationOffUiThread(() => _service.Save());
    }

    private void OnRestoreClick(object? sender, EventArgs e)
    {
        RunOperationOffUiThread(() => _service.Restore());
    }

    private void OnRestoreAutomaticallyClick(object? sender, EventArgs e)
    {
        if (!CanAcceptClick())
        {
            return;
        }

        bool current = _restoreAutomaticallyItem.Checked;
        bool desired = !current;
        SettingChange change = _settingChanges.SetRestoreAutomatically(desired, current);
        if (change.Applied)
        {
            _restoreAutomaticallyItem.Checked = change.Value;
        }
        else
        {
            _restoreAutomaticallyItem.Checked = current;
        }
    }

    private void OnOpenClick(object? sender, EventArgs e)
    {
        if (!CanAcceptClick())
        {
            return;
        }

        OpenRequested?.Invoke();
    }

    private void OnExitClick(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            if (_disposed || _exiting)
            {
                return;
            }

            _exiting = true;
            _openGeneration++;
        }

        ExitRequested?.Invoke();
    }

    private bool CanAcceptClick()
    {
        lock (_sync)
        {
            return !_disposed && !_exiting;
        }
    }

    private bool CanRestoreCurrentArrangement()
    {
        Layout? active = TrayIcon.FindActiveLayout(_store, _monitors);
        if (active is null)
        {
            return false;
        }

        try
        {
            return !_desktop.AutoArrange;
        }
        catch (DesktopUnavailableException)
        {
            return false;
        }
    }

    private void RunOperationOffUiThread(Func<OperationResult> operation)
    {
        lock (_sync)
        {
            if (_disposed || _exiting)
            {
                return;
            }

            _inFlightCount++;
        }

        _ = Task.Run(() =>
        {
            OperationResult result;
            try
            {
                result = operation();
            }
            catch (Exception ex)
            {
                result = OperationResult.Failed(FailureKind.Unexpected, path: null, ex.Message, null, ex);
            }

            _marshal(() => CompleteOperationOnUiThread(result));
        });
    }

    private void CompleteOperationOnUiThread(OperationResult result)
    {
        Action? drainCallback = null;
        try
        {
            bool disposed;
            lock (_sync)
            {
                disposed = _disposed;
            }

            if (!disposed)
            {
                OperationFinished?.Invoke(result);
            }
        }
        finally
        {
            lock (_sync)
            {
                if (_inFlightCount > 0)
                {
                    _inFlightCount--;
                }

                if (_inFlightCount == 0 && _exiting && !_disposed && _whenIdle is not null)
                {
                    drainCallback = _whenIdle;
                    _whenIdle = null;
                }
            }
        }

        drainCallback?.Invoke();
    }
}
