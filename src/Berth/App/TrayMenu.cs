using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Berth.Core;
using Berth.Core.Model;

namespace Berth.App;

/// <summary>
/// Right-click notification-area menu (SPEC §12): six entries in SPEC order, system renderer, live state refreshed
/// off the UI thread on <see cref="ContextMenuStrip.Opening"/> (never blocking on shell or file contention),
/// background execution for desktop operations, and graceful drain of all background work on Exit.
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    private readonly LayoutService _service;
    private readonly LayoutStore _store;
    private readonly IMonitors _monitors;
    private readonly IDesktopIcons _desktop;
    private readonly SettingsStore _settingsStore;
    private readonly SettingChanges _settingChanges;
    private readonly Action<Action, Action> _marshal;

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
        Action<Action, Action> marshal)
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

    /// <summary>Raised on the UI thread when an off-UI-thread tray operation starts (<c>true</c>) or all finish (<c>false</c>).</summary>
    public event Action<bool>? OperationBusyChanged;

    /// <summary>Whether any tray-initiated Save or Restore operation is currently running.</summary>
    public bool IsOperationBusy
    {
        get
        {
            lock (_sync)
            {
                return _inFlightCount > 0;
            }
        }
    }

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

    /// <summary>
    /// The active layout snapshot captured off the UI thread immediately after the most recent menu operation,
    /// allowing <see cref="OperationFinished"/> subscribers to refresh the tray tooltip without file I/O on the UI thread.
    /// </summary>
    internal Layout? LastObservedActiveLayout { get; private set; }

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
    /// Reads <c>settings.json</c>, the active layout, and <see cref="IDesktopIcons.AutoArrange"/> off the UI thread
    /// while keeping Restore and Restore automatically disabled until the read finishes, discarding superseded openings
    /// and tracking the work in the Exit drain count so dependencies stay alive until access ends.
    /// </summary>
    internal void RefreshState()
    {
        long generation;
        bool exiting;
        lock (_sync)
        {
            generation = ++_openGeneration;
            exiting = _exiting || _disposed;
            if (!exiting)
            {
                _inFlightCount++;
            }
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

        // Disable dynamic items while their background reads are pending so a late read cannot overwrite a click
        // and Opening never blocks the UI thread on file or shell contention.
        _restoreItem.Enabled = false;
        _restoreAutomaticallyItem.Enabled = false;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _lastRefreshTask = tcs.Task;
        }

        _ = Task.Run(() =>
        {
            bool canRestore = false;
            bool restoreAutomatically = false;
            try
            {
                SettingsLoad loaded = _settingsStore.Load();
                restoreAutomatically = loaded.Value.RestoreAutomatically;
                canRestore = CanRestoreCurrentArrangement();
            }
            catch (Exception)
            {
                canRestore = false;
                restoreAutomatically = false;
            }

            int settled = 0;

            void SettleAborted()
            {
                if (Interlocked.Exchange(ref settled, 1) != 0)
                {
                    return;
                }

                Action? drainCallback = CompleteInFlightWork();
                tcs.TrySetResult();
                DispatchDrainOnUiThread(drainCallback);
            }

            try
            {
                _marshal(
                    () =>
                    {
                        if (Interlocked.Exchange(ref settled, 1) != 0)
                        {
                            return;
                        }

                        Action? drainCallback = null;
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
                                _restoreAutomaticallyItem.Checked = restoreAutomatically;
                                _restoreAutomaticallyItem.Enabled = true;
                            }
                        }
                        finally
                        {
                            drainCallback = CompleteInFlightWork();
                            tcs.TrySetResult();
                        }

                        drainCallback?.Invoke();
                    },
                    SettleAborted);
            }
            catch (Exception)
            {
                SettleAborted();
            }
        });
    }

    /// <summary>
    /// Stops accepting new menu clicks and runs <paramref name="onIdle"/> on the UI thread immediately if no
    /// background menu operation or opening refresh is running, or as soon as all in-flight work settles.
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
        bool becameBusy;
        lock (_sync)
        {
            if (_disposed || _exiting)
            {
                return;
            }

            _inFlightCount++;
            becameBusy = _inFlightCount == 1;
        }

        if (becameBusy)
        {
            OperationBusyChanged?.Invoke(true);
        }

        _ = Task.Run(() =>
        {
            OperationResult result;
            Layout? activeLayout;
            try
            {
                result = operation();
            }
            catch (Exception ex)
            {
                result = OperationResult.Failed(FailureKind.Unexpected, path: null, ex.Message, null, ex);
            }

            activeLayout = TrayIcon.FindActiveLayout(_store, _monitors);

            int settled = 0;

            void SettleAborted()
            {
                if (Interlocked.Exchange(ref settled, 1) != 0)
                {
                    return;
                }

                Action? drainCallback = CompleteInFlightWork();
                DispatchDrainOnUiThread(drainCallback);
            }

            try
            {
                _marshal(
                    () =>
                    {
                        if (Interlocked.Exchange(ref settled, 1) != 0)
                        {
                            return;
                        }

                        CompleteOperationOnUiThread(result, activeLayout);
                    },
                    SettleAborted);
            }
            catch (Exception)
            {
                SettleAborted();
            }
        });
    }

    private void CompleteOperationOnUiThread(OperationResult result, Layout? activeLayout)
    {
        Action? drainCallback = null;
        bool becameIdle = false;
        try
        {
            bool disposed;
            lock (_sync)
            {
                disposed = _disposed;
            }

            if (!disposed)
            {
                LastObservedActiveLayout = activeLayout;
                OperationFinished?.Invoke(result);
            }
        }
        finally
        {
            drainCallback = CompleteInFlightWork();
            lock (_sync)
            {
                becameIdle = _inFlightCount == 0 && !_disposed;
            }
        }

        if (becameIdle)
        {
            OperationBusyChanged?.Invoke(false);
        }

        drainCallback?.Invoke();
    }

    private void DispatchDrainOnUiThread(Action? drainCallback)
    {
        if (drainCallback is null)
        {
            return;
        }

        // Never run the drain callback on a worker thread: if the dispatcher is already shutting down,
        // UI-thread teardown after app.Run() owns tray disposal.
        try
        {
            _marshal(drainCallback, static () => { });
        }
        catch (Exception)
        {
            // Dispatcher unavailable; UI-thread teardown owns disposal.
        }
    }

    private Action? CompleteInFlightWork()
    {
        lock (_sync)
        {
            if (_inFlightCount > 0)
            {
                _inFlightCount--;
            }

            if (_inFlightCount == 0 && _exiting && !_disposed && _whenIdle is not null)
            {
                Action callback = _whenIdle;
                _whenIdle = null;
                return callback;
            }

            return null;
        }
    }
}
