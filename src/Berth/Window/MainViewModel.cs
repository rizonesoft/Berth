using System.Collections.ObjectModel;
using System.IO;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Window.Mvvm;

namespace Berth.Window;

/// <summary>
/// View model behind Berth's main window (SPEC §4, §5, §7, §9): loads saved layouts and live desktop state off the UI
/// thread, maintains the SPEC §5 row order and selection rule, and builds the active <see cref="LayoutComparison"/>.
/// </summary>
internal sealed class MainViewModel : ObservableObject
{
    private const int MaxCaptureAttempts = 3;

    private readonly LayoutStore _store;
    private readonly IDesktopIcons _desktop;
    private readonly IMonitors _monitors;
    private readonly TimeProvider _clock;
    private readonly Func<Action, Task> _marshalToUiAsync;
    private readonly LayoutService? _service;
    private readonly IFolderOpener _folderOpener;
    private readonly Log? _log;
    private readonly object _refreshGate = new();

    private Task? _activeRefreshTask;
    private bool _isApplyingSnapshot;
    private bool _lastCanUndo;
    private volatile bool _checkCanUndoOnPoll;
    private long _operationEpoch;
    private long _selectionEpoch;
    private LayoutRow? _selected;
    private string? _lastKnownSelectedKey;
    private string? _pendingSelectedKey;
    private DesktopState _desktopState;
    private LayoutComparison? _comparison;
    private ComparisonKind _comparisonKind;
    private IReadOnlyList<MonitorInfo> _mapMonitors = [];
    private IReadOnlyList<UnreadableFile> _unreadable = [];
    private string? _storeReadError;

    public MainViewModel(
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors,
        TimeProvider clock,
        Func<Action, Task>? marshalToUiAsync = null,
        LayoutService? service = null,
        TimeZoneInfo? timeZone = null,
        IFolderOpener? folderOpener = null,
        Log? log = null,
        SettingsStore? settingsStore = null,
        RunValue? runValue = null,
        SettingChanges? settingChanges = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _desktop = desktop;
        _monitors = monitors;
        _clock = clock;
        _service = service;
        _folderOpener = folderOpener ?? new ShellFolderOpener();
        _log = log;
        _marshalToUiAsync = marshalToUiAsync ?? (action =>
        {
            action();
            return Task.CompletedTask;
        });
        _desktopState = DesktopState.Empty(clock.GetUtcNow());
        Detail = new DetailViewModel(this, service, clock, timeZone);
        Status = new StatusViewModel(
            clock,
            timeZone,
            undoHandler: service is not null ? () => Detail.UndoAsync() : null,
            retryHandler: request => Detail.RetryAsync(request));
        Notices = new NoticesViewModel(
            store.LayoutsFolder,
            turnAutoArrangeOffAsync: TurnAutoArrangeOffAsync,
            retryDesktopAsync: RetryDesktopAsync,
            openFolder: OpenFolder,
            isBusyProvider: () => Detail.IsBusy);
        if (settingsStore is not null && runValue is not null && settingChanges is not null)
        {
            Settings = new SettingsViewModel(
                settingChanges,
                settingsStore,
                runValue,
                Status,
                _marshalToUiAsync,
                () => Detail.IsBusy);
        }

        Detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DetailViewModel.IsBusy) or "")
            {
                if (Detail.IsBusy)
                {
                    Interlocked.Increment(ref _operationEpoch);
                }

                Status.SetBusyState(Detail.IsBusy);
                Notices.RaiseCanExecuteChanged();
                if (!Detail.IsCompletingOperation)
                {
                    Settings?.OnBusyStateChanged(Detail.IsBusy);
                }
            }
        };
        Status.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StatusViewModel.Action) or "")
            {
                _checkCanUndoOnPoll = Status.Action == StatusAction.Undo;
            }
        };
        Detail.OperationCompleted += (request, result) =>
        {
            Interlocked.Increment(ref _operationEpoch);
            if (!Detail.IsBusy)
            {
                Status.SetResult(result, _lastCanUndo, request);
                Settings?.OnBusyStateChanged(Detail.IsBusy);
            }
        };
    }

    public DetailViewModel Detail { get; }

    public StatusViewModel Status { get; }

    public NoticesViewModel Notices { get; }

    public SettingsViewModel? Settings { get; }

    public ObservableCollection<LayoutRow> Rows { get; } = [];

    public IReadOnlyList<UnreadableFile> Unreadable
    {
        get => _unreadable;
        private set => Set(ref _unreadable, value);
    }

    public string? StoreReadError
    {
        get => _storeReadError;
        private set => Set(ref _storeReadError, value);
    }

    public DesktopState Desktop
    {
        get => _desktopState;
        private set => Set(ref _desktopState, value);
    }

    public LayoutRow? Selected
    {
        get => _selected;
        set
        {
            if (value is null && !_isApplyingSnapshot && Rows.Count > 0)
            {
                RaisePropertyChanged(nameof(Selected));
                return;
            }

            if (Set(ref _selected, value))
            {
                if (value is not null)
                {
                    _lastKnownSelectedKey = value.Key;
                    if (!_isApplyingSnapshot)
                    {
                        _pendingSelectedKey = null;
                        Interlocked.Increment(ref _selectionEpoch);
                    }
                }
                else if (!_isApplyingSnapshot)
                {
                    _lastKnownSelectedKey = null;
                    Interlocked.Increment(ref _selectionEpoch);
                }

                RebuildComparison();
            }
        }
    }

    public LayoutComparison? Comparison
    {
        get => _comparison;
        private set => Set(ref _comparison, value);
    }

    public ComparisonKind ComparisonKind
    {
        get => _comparisonKind;
        private set => Set(ref _comparisonKind, value);
    }

    /// <summary>The monitor arrangement the position map should draw for <see cref="Selected"/> (SPEC §7).</summary>
    public IReadOnlyList<MonitorInfo> MapMonitors
    {
        get => _mapMonitors;
        private set => Set(ref _mapMonitors, value);
    }

    internal Task MarshalToUiAsync(Action action) => _marshalToUiAsync(action);

    /// <summary>
    /// Reads monitors, desktop icons, auto-arrange state, and saved layouts off the UI thread, then applies rows,
    /// selection, and comparison on the UI thread. Overlapping calls return immediately without starting a second read.
    /// </summary>
    public Task RefreshAsync()
    {
        TaskCompletionSource<bool> gate;
        lock (_refreshGate)
        {
            if (_activeRefreshTask is not null)
            {
                return Task.CompletedTask;
            }

            gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRefreshTask = gate.Task;
        }

        _ = RunRefreshCoreAsync(gate, includeCanUndo: _checkCanUndoOnPoll);
        return gate.Task;
    }

    /// <summary>
    /// Waits for any in-flight poll to finish, then runs a fresh capture guaranteed to start after an operation's write.
    /// When <paramref name="selectKey"/> is given, publishes that saved arrangement key only after any older poll finishes,
    /// and retains it until a post-write snapshot selects that saved row.
    /// </summary>
    public async Task RefreshAfterOperationAsync(string? selectKey = null) =>
        _ = await RefreshAfterOperationCoreAsync(selectKey).ConfigureAwait(false);

    /// <summary>
    /// Invalidates any in-flight external completion (for example when the window is hidden or closed) so a delayed
    /// automatic restore never surfaces as a fresh result on reopen or touches a closed window.
    /// </summary>
    internal void CancelPendingExternalOperations()
    {
        Interlocked.Increment(ref _operationEpoch);
        _pendingSelectedKey = null;
    }

    private async Task<bool> RefreshAfterOperationCoreAsync(
        string? selectKey,
        long? expectedEpoch = null,
        long? expectedSelectionEpoch = null,
        Func<bool>? shouldContinue = null)
    {
        while (true)
        {
            if (expectedEpoch.HasValue && Volatile.Read(ref _operationEpoch) != expectedEpoch.Value)
            {
                return false;
            }

            Task? existing;
            TaskCompletionSource<bool>? gate = null;
            lock (_refreshGate)
            {
                existing = _activeRefreshTask;
                if (existing is null)
                {
                    gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _activeRefreshTask = gate.Task;
                }
            }

            if (existing is not null)
            {
                try
                {
                    await existing.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A prior poll failure must not block the post-operation refresh.
                }

                continue;
            }

            if (selectKey is not null || expectedEpoch.HasValue || shouldContinue is not null)
            {
                bool proceed = true;
                await _marshalToUiAsync(() =>
                {
                    if ((expectedEpoch.HasValue && Volatile.Read(ref _operationEpoch) != expectedEpoch.Value)
                        || (shouldContinue is not null && !shouldContinue()))
                    {
                        proceed = false;
                        return;
                    }

                    if (selectKey is not null
                        && (!expectedSelectionEpoch.HasValue
                            || Volatile.Read(ref _selectionEpoch) == expectedSelectionEpoch.Value))
                    {
                        _pendingSelectedKey = selectKey;
                        _lastKnownSelectedKey = selectKey;
                    }
                }).ConfigureAwait(false);

                if (!proceed)
                {
                    lock (_refreshGate)
                    {
                        if (ReferenceEquals(_activeRefreshTask, gate!.Task))
                        {
                            _activeRefreshTask = null;
                        }
                    }

                    gate!.TrySetResult(false);
                    return false;
                }
            }

            _ = RunRefreshCoreAsync(gate!, includeCanUndo: true);
            return await gate!.Task.ConfigureAwait(false);
        }
    }

    private async Task RunRefreshCoreAsync(TaskCompletionSource<bool> gate, bool includeCanUndo)
    {
        Exception? failure = null;
        bool applied = false;
        try
        {
            (long restoreEpoch, long startEpoch) = Settings?.CaptureEpochs() ?? (0L, 0L);
            (RefreshSnapshot? snapshot, (bool? RestoreAuto, bool? StartWithWindows)? settingsSnapshot) =
                await Task.Run(() =>
                {
                    (bool? RestoreAuto, bool? StartWithWindows)? readSettings = Settings?.ReadSources();
                    RefreshSnapshot? snap = CaptureSnapshot(includeCanUndo);
                    return (snap, readSettings);
                }).ConfigureAwait(false);

            if (snapshot is not null || settingsSnapshot is not null)
            {
                await _marshalToUiAsync(() =>
                {
                    if (settingsSnapshot is { } s)
                    {
                        Settings?.ApplyRefreshed(s.RestoreAuto, restoreEpoch, s.StartWithWindows, startEpoch);
                    }

                    if (snapshot is not null)
                    {
                        ApplySnapshot(snapshot);
                        applied = true;
                    }
                }).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            lock (_refreshGate)
            {
                if (ReferenceEquals(_activeRefreshTask, gate.Task))
                {
                    _activeRefreshTask = null;
                }
            }

            if (failure is not null)
            {
                gate.TrySetException(failure);
            }
            else
            {
                gate.TrySetResult(applied);
            }
        }
    }

    /// <summary>Fire-and-forget convenience entry point delegating to <see cref="RefreshAsync"/>.</summary>
    public Task Refresh() => RefreshAsync();

    private RefreshSnapshot? CaptureSnapshot(bool includeCanUndo)
    {
        DateTimeOffset checkedAt = _clock.GetUtcNow();

        // Capture monitors and desktop icons together: if the monitor arrangement changes mid-read, retry up to
        // MaxCaptureAttempts times and discard a perpetually shifting snapshot rather than publishing a mixed comparison.
        IReadOnlyList<MonitorInfo> monitors = [];
        bool monitorsAvailable = false;
        string? currentKey = null;
        IReadOnlyList<IconPosition> icons = [];
        bool autoArrange = false;
        bool desktopUnavailable = false;
        bool stable = false;

        for (int attempt = 0; attempt < MaxCaptureAttempts; attempt++)
        {
            try
            {
                monitors = _monitors.Read();
                monitorsAvailable = monitors.Count > 0;
            }
            catch (DesktopUnavailableException)
            {
                monitors = [];
                monitorsAvailable = false;
            }

            currentKey = monitorsAvailable ? ArrangementKey.From(monitors) : null;

            IReadOnlyList<MonitorInfo> iconMonitors = [];
            try
            {
                (iconMonitors, icons) = _desktop.ReadIconsWithMonitors(_monitors);
                autoArrange = _desktop.AutoArrange;
                desktopUnavailable = false;
            }
            catch (DesktopUnavailableException)
            {
                icons = [];
                autoArrange = false;
                desktopUnavailable = true;
            }

            if (desktopUnavailable || !monitorsAvailable)
            {
                // No live comparison will be built from mixed coordinates; accept this snapshot.
                stable = true;
                break;
            }

            string? iconKey = iconMonitors.Count > 0 ? ArrangementKey.From(iconMonitors) : null;
            string? afterKey;
            try
            {
                IReadOnlyList<MonitorInfo> afterMonitors = _monitors.Read();
                afterKey = afterMonitors.Count > 0 ? ArrangementKey.From(afterMonitors) : null;
            }
            catch (DesktopUnavailableException)
            {
                afterKey = null;
            }

            if (string.Equals(currentKey, iconKey, StringComparison.Ordinal)
                && string.Equals(iconKey, afterKey, StringComparison.Ordinal))
            {
                stable = true;
                break;
            }
        }

        if (!stable)
        {
            return null;
        }

        var desktopState = new DesktopState(
            icons,
            monitors,
            autoArrange,
            Unavailable: desktopUnavailable || !monitorsAvailable,
            CheckedAt: checkedAt);

        // Load saved layouts independently of shell availability.
        IReadOnlyList<Layout> layouts;
        IReadOnlyList<UnreadableFile> unreadable;
        string? storeReadError;
        try
        {
            LayoutSet loaded = _store.LoadAll();
            layouts = loaded.Layouts;
            unreadable = loaded.Unreadable;
            storeReadError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            layouts = [];
            unreadable = [];
            storeReadError = ex.Message;
        }

        bool? canUndo = includeCanUndo ? (_service?.CanUndo ?? false) : null;
        return new RefreshSnapshot(currentKey, desktopState, layouts, unreadable, storeReadError, canUndo);
    }

    /// <summary>
    /// Refreshes the window's comparison baseline after a tray or automatic operation finishes and publishes the result
    /// to <see cref="Status"/> with Undo/Retry support (SPEC §8, §16). Discards superseded completions if a newer
    /// operation started or <paramref name="shouldPublish"/> became false while the refresh was in flight.
    /// </summary>
    internal async Task OnExternalOperationFinishedAsync(
        WindowOperationKind kind,
        OperationResult result,
        Func<bool>? shouldPublish = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        long epoch = Interlocked.Increment(ref _operationEpoch);
        long selectionEpoch = Volatile.Read(ref _selectionEpoch);
        string? selectKey = (kind is WindowOperationKind.Save or WindowOperationKind.Restore)
            && !string.IsNullOrWhiteSpace(result.ArrangementKey)
            ? result.ArrangementKey
            : null;
        bool refreshed = false;
        try
        {
            refreshed = await RefreshAfterOperationCoreAsync(
                selectKey,
                expectedEpoch: epoch,
                expectedSelectionEpoch: selectionEpoch,
                shouldContinue: shouldPublish).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Still surface the external operation result in the status line even if refresh fails,
            // without offering Undo from a stale _lastCanUndo value.
            refreshed = false;
        }

        WindowOperationRequest? retryRequest = null;
        if (_service is not null)
        {
            retryRequest = kind switch
            {
                WindowOperationKind.Save => new WindowOperationRequest(
                    WindowOperationKind.Save,
                    () => _service.Save(),
                    res => res.Done ? res.ArrangementKey : null),
                WindowOperationKind.Restore => new WindowOperationRequest(
                    WindowOperationKind.Restore,
                    () => _service.Restore()),
                _ => null,
            };
        }

        await _marshalToUiAsync(() =>
        {
            if (Detail.IsBusy
                || Volatile.Read(ref _operationEpoch) != epoch
                || (shouldPublish is not null && !shouldPublish()))
            {
                return;
            }

            Status.SetResult(result, refreshed && _lastCanUndo, retryRequest);
        }).ConfigureAwait(false);
    }

    private void ApplySnapshot(RefreshSnapshot snapshot)
    {
        // Capture the live selection at UI-application time so user selection changes during an async read win,
        // while a post-operation target key takes priority until a usable snapshot contains that row.
        string? preferredKey = _pendingSelectedKey ?? _selected?.Key ?? _lastKnownSelectedKey;

        if (snapshot.CanUndo.HasValue)
        {
            _lastCanUndo = snapshot.CanUndo.Value;
        }

        Desktop = snapshot.Desktop;
        Unreadable = snapshot.Unreadable;
        StoreReadError = snapshot.StoreReadError;
        Notices.Update(Desktop, Unreadable);
        ApplyNoticeOverrides();

        List<LayoutRow> nextRows = BuildRows(snapshot);

        bool rowsUnchanged = Rows.Count == nextRows.Count && Rows.SequenceEqual(nextRows);
        if (!rowsUnchanged)
        {
            _isApplyingSnapshot = true;
            try
            {
                Rows.Clear();
                foreach (LayoutRow row in nextRows)
                {
                    Rows.Add(row);
                }
            }
            finally
            {
                _isApplyingSnapshot = false;
            }
        }

        LayoutRow? nextSelected = ChooseSelection(nextRows, preferredKey);
        if (_pendingSelectedKey is not null
            && nextSelected is { IsUnsaved: false }
            && string.Equals(nextSelected.Key, _pendingSelectedKey, StringComparison.Ordinal))
        {
            _pendingSelectedKey = null;
        }

        if (snapshot.StoreReadError is not null && nextSelected is null)
        {
            // Transient folder-read failure: clear Selected while keeping _lastKnownSelectedKey for recovery.
            if (Set(ref _selected, null, nameof(Selected)))
            {
                RebuildComparison(snapshot.CanUndo);
            }

            return;
        }

        bool selectionChanged = !EqualityComparer<LayoutRow?>.Default.Equals(_selected, nextSelected);
        if (selectionChanged)
        {
            _isApplyingSnapshot = true;
            try
            {
                Selected = nextSelected;
            }
            finally
            {
                _isApplyingSnapshot = false;
            }
        }
        else
        {
            RebuildComparison(snapshot.CanUndo);
        }
    }

    private void ApplyNoticeOverrides()
    {
        if (Desktop.Unavailable)
        {
            Detail.SetNoticeOverrides(
                extraCanRestore: static () => false,
                restoreToolTip: NoticesViewModel.DesktopUnavailableText,
                extraCanSave: static () => false,
                saveToolTip: NoticesViewModel.DesktopUnavailableText);
        }
        else if (Desktop.AutoArrange)
        {
            Detail.SetNoticeOverrides(
                extraCanRestore: static () => false,
                restoreToolTip: NoticesViewModel.AutoArrangeText,
                extraCanSave: null,
                saveToolTip: null);
        }
        else
        {
            Detail.SetNoticeOverrides(null, null, null, null);
        }
    }

    private Task TurnAutoArrangeOffAsync()
    {
        long epoch = 0;
        bool writeUnavailable = false;
        Exception? unexpectedError = null;
        bool snapshotApplied = false;

        return Detail.RunBusyActionAsync(
            async () =>
            {
                epoch = Volatile.Read(ref _operationEpoch);
                try
                {
                    await Task.Run(() => _desktop.TryTurnAutoArrangeOff()).ConfigureAwait(false);
                }
                catch (DesktopUnavailableException)
                {
                    writeUnavailable = true;
                }
                catch (Exception ex)
                {
                    unexpectedError = ex;
                }

                try
                {
                    snapshotApplied = await RefreshAfterOperationCoreAsync(selectKey: null).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    unexpectedError ??= ex;
                    snapshotApplied = false;
                }
            },
            onFinishedOnUi: () =>
            {
                if (Detail.IsBusy || Volatile.Read(ref _operationEpoch) != epoch)
                {
                    return;
                }

                if (unexpectedError is not null)
                {
                    OperationResult failed = OperationResult.Failed(
                        FailureKind.Unexpected,
                        path: null,
                        unexpectedError.Message,
                        error: unexpectedError);
                    string message = Messages.Refusal(failed);
                    _log?.FailWithDetails(message, unexpectedError.ToString());
                    Status.SetCustomMessage(message);
                }
                else if (writeUnavailable || Desktop.Unavailable)
                {
                    OperationResult unavailable = OperationResult.Refused(Refusal.DesktopUnavailable);
                    _log?.Fail(Messages.Refusal(unavailable));
                    Status.SetResult(unavailable);
                }
                else if (snapshotApplied && Desktop.AutoArrange)
                {
                    _log?.Fail(Messages.AutoArrangeWouldNotTurnOff);
                    Status.SetCustomMessage(Messages.AutoArrangeWouldNotTurnOff);
                }
            });
    }

    private async Task RetryDesktopAsync()
    {
        try
        {
            await RefreshAfterOperationAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OperationResult failed = OperationResult.Failed(FailureKind.Unexpected, path: null, ex.Message, error: ex);
            string message = Messages.Refusal(failed);
            _log?.FailWithDetails(message, ex.ToString());
            await _marshalToUiAsync(() =>
            {
                if (!Detail.IsBusy)
                {
                    Status.SetCustomMessage(message);
                }
            }).ConfigureAwait(false);
        }
    }

    private void OpenFolder(string fullPath)
    {
        try
        {
            _folderOpener.OpenSelectFile(fullPath);
        }
        catch (Exception ex)
        {
            OperationResult failed = OperationResult.Failed(FailureKind.Unexpected, path: null, ex.Message, error: ex);
            string message = Messages.Refusal(failed);
            _log?.FailWithDetails(message, ex.ToString());
            if (!Detail.IsBusy)
            {
                Status.SetCustomMessage(message);
            }
        }
    }

    private static List<LayoutRow> BuildRows(RefreshSnapshot snapshot)
    {
        var rows = new List<LayoutRow>(snapshot.Layouts.Count + 1);
        Layout? activeLayout = snapshot.CurrentKey is not null
            ? snapshot.Layouts.FirstOrDefault(l => string.Equals(l.Key, snapshot.CurrentKey, StringComparison.Ordinal))
            : null;

        if (activeLayout is not null)
        {
            rows.Add(LayoutRow.FromSaved(activeLayout, isActive: true));
        }
        else if (snapshot.CurrentKey is not null && snapshot.StoreReadError is null)
        {
            string expectedFileName = snapshot.CurrentKey + ".json";
            bool currentFileIsUnreadable = snapshot.Unreadable.Any(
                u => string.Equals(u.FileName, expectedFileName, StringComparison.OrdinalIgnoreCase));
            if (!currentFileIsUnreadable)
            {
                rows.Add(LayoutRow.Unsaved(snapshot.CurrentKey));
            }
        }

        IEnumerable<Layout> others = snapshot.Layouts
            .Where(l => activeLayout is null || !string.Equals(l.Key, activeLayout.Key, StringComparison.Ordinal))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Key, StringComparer.Ordinal);

        foreach (Layout layout in others)
        {
            rows.Add(LayoutRow.FromSaved(layout, isActive: false));
        }

        return rows;
    }

    private static LayoutRow? ChooseSelection(List<LayoutRow> rows, string? previouslySelectedKey)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        if (previouslySelectedKey is not null)
        {
            LayoutRow? preserved = rows.FirstOrDefault(
                r => string.Equals(r.Key, previouslySelectedKey, StringComparison.Ordinal));
            if (preserved is not null)
            {
                return preserved;
            }
        }

        return rows.FirstOrDefault(r => r.IsActive)
            ?? rows.FirstOrDefault(r => r.IsUnsaved)
            ?? rows[0];
    }

    private void RebuildComparison(bool? measuredCanUndo = null)
    {
        if (_selected is null)
        {
            MapMonitors = [];
            ComparisonKind = ComparisonKind.None;
            Comparison = null;
            Status.SetComparison(_selected, Comparison, ComparisonKind, _desktopState, measuredCanUndo);
            return;
        }

        if (_selected.IsUnsaved)
        {
            if (_desktopState.Unavailable || _desktopState.Monitors.Count == 0)
            {
                MapMonitors = [];
                ComparisonKind = ComparisonKind.None;
                Comparison = null;
            }
            else
            {
                MapMonitors = _desktopState.Monitors;
                ComparisonKind = ComparisonKind.Unsaved;
                Comparison = LayoutComparison.Unsaved(_desktopState.Monitors, _desktopState.Icons);
            }

            Status.SetComparison(_selected, Comparison, ComparisonKind, _desktopState, measuredCanUndo);
            return;
        }

        Layout saved = _selected.Layout!;
        MapMonitors = saved.Monitors;
        if (_selected.IsActive && !_desktopState.Unavailable)
        {
            ComparisonKind = ComparisonKind.Compared;
            Comparison = LayoutComparison.Compare(saved, _desktopState.Icons);
        }
        else
        {
            ComparisonKind = ComparisonKind.Neutral;
            Comparison = LayoutComparison.Neutral(saved);
        }

        Status.SetComparison(_selected, Comparison, ComparisonKind, _desktopState, measuredCanUndo);
    }

    private sealed record RefreshSnapshot(
        string? CurrentKey,
        DesktopState Desktop,
        IReadOnlyList<Layout> Layouts,
        IReadOnlyList<UnreadableFile> Unreadable,
        string? StoreReadError,
        bool? CanUndo);
}
