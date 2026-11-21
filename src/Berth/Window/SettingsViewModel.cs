using Berth.Core;
using Berth.Desktop;
using Berth.Window.Mvvm;

namespace Berth.Window;

/// <summary>
/// View model behind the two settings switches and version caption pinned to the bottom of the layouts pane
/// (SPEC §5, §11, §18): persists changes off the UI thread through <see cref="SettingChanges"/> and <see cref="RunValue"/>,
/// snaps back on failure with the §18 message in the status line (queued if status is currently busy), and discards stale
/// poll observations captured before a user write.
/// </summary>
internal sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingChanges _changes;
    private readonly SettingsStore _store;
    private readonly RunValue _run;
    private readonly StatusViewModel _status;
    private readonly Func<Action, Task> _marshalToUiAsync;
    private readonly Func<bool> _isBusyProvider;
    private readonly string _versionCaption = $"Berth {AppInfo.Version}";

    private bool _restoreAutomatically;
    private bool _startWithWindows;
    private bool _suppressWrites;
    private int _restoreWriting;
    private int _startWriting;
    private long _restoreEpoch;
    private long _startEpoch;
    private string? _pendingFailureMessage;
    private Task _lastRestoreTask = Task.CompletedTask;
    private Task _lastStartTask = Task.CompletedTask;

    public SettingsViewModel(
        SettingChanges changes,
        SettingsStore store,
        RunValue run,
        StatusViewModel status,
        Func<Action, Task>? marshalToUiAsync = null,
        Func<bool>? isBusyProvider = null)
    {
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _marshalToUiAsync = marshalToUiAsync ?? (action =>
        {
            action();
            return Task.CompletedTask;
        });
        _isBusyProvider = isBusyProvider ?? (() => false);
    }

    public string VersionCaption => _versionCaption;

    public bool RestoreAutomatically
    {
        get => _restoreAutomatically;
        set
        {
            if (_suppressWrites)
            {
                Set(ref _restoreAutomatically, value);
                return;
            }

            if (_restoreAutomatically == value)
            {
                return;
            }

            bool previous = _restoreAutomatically;
            Set(ref _restoreAutomatically, value);
            _lastRestoreTask = ApplyRestoreAutomaticallyAsync(value, previous, _lastRestoreTask);
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_suppressWrites)
            {
                Set(ref _startWithWindows, value);
                return;
            }

            if (_startWithWindows == value)
            {
                return;
            }

            bool previous = _startWithWindows;
            Set(ref _startWithWindows, value);
            _lastStartTask = ApplyStartWithWindowsAsync(value, previous, _lastStartTask);
        }
    }

    internal Task LastRestoreTask => _lastRestoreTask;

    internal Task LastStartTask => _lastStartTask;

    internal (long RestoreEpoch, long StartEpoch) CaptureEpochs() =>
        (Volatile.Read(ref _restoreEpoch), Volatile.Read(ref _startEpoch));

    /// <summary>Reads both sources off the UI thread for polling/refresh.</summary>
    internal (bool? RestoreAutomatically, bool StartWithWindows) ReadSources()
    {
        bool? restoreAuto = null;
        try
        {
            SettingsLoad loaded = _store.Load();
            if (loaded.Unreadable is null)
            {
                restoreAuto = loaded.Value.RestoreAutomatically;
            }
        }
        catch (Exception)
        {
            restoreAuto = null;
        }

        bool startWithWindows = _run.IsOn;
        return (restoreAuto, startWithWindows);
    }

    /// <summary>
    /// Applies a refreshed snapshot on the UI thread without writing or logging, discarding any switch value whose
    /// user-write epoch advanced while the poll was in flight.
    /// </summary>
    internal void ApplyRefreshed(
        bool? restoreAutomatically,
        long restoreEpoch,
        bool startWithWindows,
        long startEpoch)
    {
        _suppressWrites = true;
        try
        {
            if (restoreAutomatically.HasValue
                && Volatile.Read(ref _restoreWriting) == 0
                && Volatile.Read(ref _restoreEpoch) == restoreEpoch)
            {
                Set(ref _restoreAutomatically, restoreAutomatically.Value, nameof(RestoreAutomatically));
            }

            if (Volatile.Read(ref _startWriting) == 0
                && Volatile.Read(ref _startEpoch) == startEpoch)
            {
                Set(ref _startWithWindows, startWithWindows, nameof(StartWithWindows));
            }
        }
        finally
        {
            _suppressWrites = false;
        }
    }

    /// <summary>
    /// Flushes any §18 settings-write failure that completed while the status line was showing "Working".
    /// </summary>
    internal void OnBusyStateChanged(bool isBusy)
    {
        if (isBusy || _pendingFailureMessage is null)
        {
            return;
        }

        string message = _pendingFailureMessage;
        _pendingFailureMessage = null;
        _ = _marshalToUiAsync(() =>
        {
            if (!_isBusyProvider())
            {
                _status.SetCustomMessage(message);
            }
            else
            {
                _pendingFailureMessage = message;
            }
        });
    }

    private async Task ApplyRestoreAutomaticallyAsync(bool target, bool previous, Task prior)
    {
        Interlocked.Increment(ref _restoreEpoch);
        Interlocked.Increment(ref _restoreWriting);
        try
        {
            await prior.ConfigureAwait(false);
            SettingChange result = await Task.Run(() => _changes.SetRestoreAutomatically(target, previous)).ConfigureAwait(false);
            await _marshalToUiAsync(() =>
            {
                _suppressWrites = true;
                try
                {
                    Set(ref _restoreAutomatically, result.Value, nameof(RestoreAutomatically));
                }
                finally
                {
                    _suppressWrites = false;
                }

                if (!result.Applied && !string.IsNullOrWhiteSpace(result.Message))
                {
                    PublishFailureOnUi(result.Message);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _restoreWriting);
        }
    }

    private async Task ApplyStartWithWindowsAsync(bool target, bool previous, Task prior)
    {
        Interlocked.Increment(ref _startEpoch);
        Interlocked.Increment(ref _startWriting);
        try
        {
            await prior.ConfigureAwait(false);
            SettingChange result = await Task.Run(() =>
            {
                Exception? failure = null;
                try
                {
                    _run.Set(target);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                return _changes.RecordStartWithWindows(target, failure);
            }).ConfigureAwait(false);

            await _marshalToUiAsync(() =>
            {
                bool finalValue = result.Applied ? result.Value : previous;
                _suppressWrites = true;
                try
                {
                    Set(ref _startWithWindows, finalValue, nameof(StartWithWindows));
                }
                finally
                {
                    _suppressWrites = false;
                }

                if (!result.Applied && !string.IsNullOrWhiteSpace(result.Message))
                {
                    PublishFailureOnUi(result.Message);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _startWriting);
        }
    }

    private void PublishFailureOnUi(string message)
    {
        if (_isBusyProvider())
        {
            _pendingFailureMessage = message;
            return;
        }

        _pendingFailureMessage = null;
        _status.SetCustomMessage(message);
    }
}
