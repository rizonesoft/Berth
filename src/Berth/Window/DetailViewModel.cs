using System.ComponentModel;
using Berth.Core;
using Berth.Core.Model;
using Berth.Window.Mvvm;

namespace Berth.Window;

/// <summary>Which variant of the detail header is shown for the selected row (SPEC §6).</summary>
internal enum DetailKind
{
    None,
    Active,
    Unsaved,
    Other,
}

/// <summary>Which user-initiated window operation is running or just finished (SPEC §6, §8, §10).</summary>
internal enum WindowOperationKind
{
    Save,
    Restore,
    Undo,
    Revert,
    Rename,
    Delete,
}

/// <summary>Repeatable operation request carrying its target, arguments, and post-success selection key for Retry (SPEC §8).</summary>
internal sealed record WindowOperationRequest(
    WindowOperationKind Kind,
    Func<OperationResult> Action,
    Func<OperationResult, string?>? SelectKeyAfter = null);

/// <summary>
/// View model for the right-pane detail header and action buttons (SPEC §6): computes title, subtitle,
/// action visibility, shared busy state, and off-UI-thread Save and Restore execution.
/// </summary>
internal sealed class DetailViewModel : ObservableObject
{
    public const string OtherArrangementSentence =
        "Saved for another monitor arrangement. Connect those monitors to restore it.";

    private readonly MainViewModel _main;
    private readonly LayoutService? _service;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _timeZone;

    private int _operationInFlight;
    private bool _localBusy;
    private bool _externalBusy;
    private bool _isBusy;
    private DetailKind _kind;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _otherArrangementLine = string.Empty;
    private bool _showRestore;
    private bool _showSave;
    private bool _showMore;
    private bool _showOtherArrangementLine;
    private bool _hasPreviousSave;
    private Func<bool>? _extraCanRestore;
    private Func<bool>? _extraCanSave;
    private string? _restoreToolTip;
    private string? _saveToolTip;
    private Task _lastOperationTask = Task.CompletedTask;

    public DetailViewModel(
        MainViewModel main,
        LayoutService? service,
        TimeProvider clock,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(clock);

        _main = main;
        _service = service;
        _clock = clock;
        _timeZone = timeZone ?? TimeZoneInfo.Local;

        SaveCommand = new RelayCommand(ExecuteSave, () => CanSave);
        RestoreCommand = new RelayCommand(ExecuteRestore, () => CanRestore);

        _main.PropertyChanged += OnMainPropertyChanged;
        UpdateFromMain();
    }

    public event Action<WindowOperationKind>? OperationStarted;

    public event Action<WindowOperationKind, OperationResult>? OperationFinished;

    internal event Action<WindowOperationRequest, OperationResult>? OperationCompleted;

    public DetailKind Kind
    {
        get => _kind;
        private set => Set(ref _kind, value);
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => Set(ref _subtitle, value);
    }

    public string OtherArrangementLine
    {
        get => _otherArrangementLine;
        private set => Set(ref _otherArrangementLine, value);
    }

    public bool ShowRestore
    {
        get => _showRestore;
        private set => Set(ref _showRestore, value);
    }

    public bool ShowSave
    {
        get => _showSave;
        private set => Set(ref _showSave, value);
    }

    public bool ShowMore
    {
        get => _showMore;
        private set => Set(ref _showMore, value);
    }

    public bool ShowOtherArrangementLine
    {
        get => _showOtherArrangementLine;
        private set => Set(ref _showOtherArrangementLine, value);
    }

    public bool HasPreviousSave
    {
        get => _hasPreviousSave;
        private set => Set(ref _hasPreviousSave, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                RaiseAvailabilityChanged();
            }
        }
    }

    /// <summary>Whether a window-initiated operation (Save, Restore, Undo, Turn it off) is running.</summary>
    internal bool IsLocalBusy => _localBusy;

    /// <summary>
    /// Shares in-process operation busy state from the tray menu so a slow tray Save or Restore disables the open
    /// window's header actions until it completes.
    /// </summary>
    internal void SetExternalBusy(bool busy)
    {
        if (_externalBusy == busy)
        {
            return;
        }

        _externalBusy = busy;
        IsBusy = _localBusy || _externalBusy;
    }

    public bool CanRestore =>
        !_isBusy
        && _service is not null
        && _kind == DetailKind.Active
        && (_extraCanRestore?.Invoke() ?? true);

    public bool CanSave =>
        !_isBusy
        && _service is not null
        && _kind is DetailKind.Active or DetailKind.Unsaved
        && (_extraCanSave?.Invoke() ?? true);

    public bool CanMore => !_isBusy && _kind is DetailKind.Active or DetailKind.Other;

    public string? RestoreToolTip
    {
        get => _restoreToolTip;
        private set => Set(ref _restoreToolTip, value);
    }

    public string? SaveToolTip
    {
        get => _saveToolTip;
        private set => Set(ref _saveToolTip, value);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand RestoreCommand { get; }

    /// <summary>Last command task started by <see cref="SaveCommand"/> or <see cref="RestoreCommand"/>, for tests.</summary>
    internal Task LastOperationTask => _lastOperationTask;

    /// <summary>
    /// Lets §7 notices compose additional disable rules (auto arrange, unreachable desktop) with busy state so
    /// completing an operation never re-enables a still-refused button.
    /// </summary>
    internal void SetNoticeOverrides(
        Func<bool>? extraCanRestore,
        string? restoreToolTip,
        Func<bool>? extraCanSave,
        string? saveToolTip)
    {
        _extraCanRestore = extraCanRestore;
        _extraCanSave = extraCanSave;
        RestoreToolTip = restoreToolTip;
        SaveToolTip = saveToolTip;
        RaiseAvailabilityChanged();
    }

    /// <summary>
    /// Runs a layout operation off the UI thread under the shared busy state (used by Save, Restore, Undo, Revert,
    /// Rename, and Delete), refreshes after the write, clears busy, and publishes <see cref="OperationFinished"/> and
    /// <see cref="OperationCompleted"/>.
    /// </summary>
    public Task<OperationResult?> RunOperationAsync(
        WindowOperationKind kind,
        Func<OperationResult> action,
        Func<OperationResult, string?>? selectKeyAfter = null) =>
        RunOperationAsync(new WindowOperationRequest(kind, action, selectKeyAfter));

    public async Task<OperationResult?> RunOperationAsync(WindowOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Action);

        if (Interlocked.CompareExchange(ref _operationInFlight, 1, 0) != 0)
        {
            return null;
        }

        if (_externalBusy)
        {
            Volatile.Write(ref _operationInFlight, 0);
            return null;
        }

        return await RunAdmittedOperationAsync(request).ConfigureAwait(false);
    }

    private async Task<OperationResult?> RunAdmittedOperationAsync(WindowOperationRequest request)
    {
        OperationResult? result = null;
        try
        {
            await _main.MarshalToUiAsync(() =>
            {
                _localBusy = true;
                IsBusy = true;
                OperationStarted?.Invoke(request.Kind);
            }).ConfigureAwait(false);

            result = await Task.Run(request.Action).ConfigureAwait(false);

            string? selectKey = request.SelectKeyAfter?.Invoke(result);
            try
            {
                await _main.RefreshAfterOperationAsync(selectKey).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Preserve the operation's result and still release busy state cleanly if refresh fails unexpectedly.
            }

            return result;
        }
        finally
        {
            OperationResult? finalResult = result;
            await _main.MarshalToUiAsync(() =>
            {
                Volatile.Write(ref _operationInFlight, 0);
                _localBusy = false;
                IsBusy = _externalBusy;
                if (finalResult is not null)
                {
                    OperationCompleted?.Invoke(request, finalResult);
                    OperationFinished?.Invoke(request.Kind, finalResult);
                }
            }).ConfigureAwait(false);
        }
    }

    public Task<OperationResult?> SaveAsync()
    {
        if (!CanSave || _service is null)
        {
            return Task.FromResult<OperationResult?>(null);
        }

        return RunOperationAsync(new WindowOperationRequest(
            WindowOperationKind.Save,
            () => _service.Save(),
            result => result.Done ? result.ArrangementKey : null));
    }

    public Task<OperationResult?> RestoreAsync()
    {
        if (!CanRestore || _service is null)
        {
            return Task.FromResult<OperationResult?>(null);
        }

        string? expectedKey = _main.Selected?.Key;
        return RunOperationAsync(new WindowOperationRequest(
            WindowOperationKind.Restore,
            () => _service.Restore(expectedKey: expectedKey)));
    }

    public Task<OperationResult?> UndoAsync()
    {
        if (_isBusy || _service is null)
        {
            return Task.FromResult<OperationResult?>(null);
        }

        return RunOperationAsync(new WindowOperationRequest(
            WindowOperationKind.Undo,
            () => _service.Undo()));
    }

    public Task<OperationResult?> RenameAsync(string key, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(newName);
        if (_service is null)
        {
            return Task.FromResult<OperationResult?>(null);
        }

        _lastOperationTask = RunOperationAsync(new WindowOperationRequest(
            WindowOperationKind.Rename,
            () => _service.Rename(key, newName),
            _ => key));
        return (Task<OperationResult?>)_lastOperationTask;
    }

    /// <summary>
    /// Atomically checks admission and starts a Rename operation for <paramref name="key"/>. Returns <c>false</c>
    /// without starting anything if another window or tray operation currently holds the slot so the Rename dialog can
    /// stay open with the user's draft intact.
    /// </summary>
    internal bool TryStartRename(string key, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(newName);
        if (_service is null || _externalBusy || Interlocked.CompareExchange(ref _operationInFlight, 1, 0) != 0)
        {
            return false;
        }

        if (_externalBusy)
        {
            Volatile.Write(ref _operationInFlight, 0);
            return false;
        }

        var request = new WindowOperationRequest(
            WindowOperationKind.Rename,
            () => _service.Rename(key, newName),
            _ => key);
        _lastOperationTask = RunAdmittedOperationAsync(request);
        return true;
    }

    public Task<OperationResult?> RevertAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_service is null)
        {
            return Task.FromResult<OperationResult?>(null);
        }

        DateTimeOffset? targetPreviousSavedAt = _main.FindSavedLayoutSafe(key)?.Previous?.SavedAt;
        bool isRetry = false;
        _lastOperationTask = RunOperationAsync(new WindowOperationRequest(
            WindowOperationKind.Revert,
            () =>
            {
                if (isRetry && targetPreviousSavedAt is { } intended)
                {
                    // Guard against retrying a swap that already reached disk before a read-back failure.
                    Layout? current = _main.FindSavedLayoutSafe(key);
                    if (current is not null && current.SavedAt == intended)
                    {
                        return OperationResult.Reverted(current.Name, current.SavedAt);
                    }
                }

                isRetry = true;
                return _service.Revert(key);
            },
            _ => key));
        return (Task<OperationResult?>)_lastOperationTask;
    }

    /// <summary>
    /// Atomically checks admission and starts a Delete operation for <paramref name="key"/>. Returns <c>false</c>
    /// without starting anything if another operation currently holds the slot so the Delete dialog stays open.
    /// Retries verify the layout identity so a retry never deletes a replacement layout saved under the same key.
    /// </summary>
    internal bool TryStartDelete(string key, string layoutName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(layoutName);
        if (_service is null || _externalBusy || Interlocked.CompareExchange(ref _operationInFlight, 1, 0) != 0)
        {
            return false;
        }

        if (_externalBusy)
        {
            Volatile.Write(ref _operationInFlight, 0);
            return false;
        }

        DateTimeOffset? originalSavedAt = _main.FindSavedLayoutSafe(key)?.SavedAt;
        bool isRetry = false;
        var request = new WindowOperationRequest(
            WindowOperationKind.Delete,
            () =>
            {
                if (isRetry)
                {
                    Layout? current = _main.FindSavedLayoutSafe(key);
                    if (current is null)
                    {
                        return OperationResult.Deleted(layoutName);
                    }

                    if (originalSavedAt is { } expected && current.SavedAt != expected)
                    {
                        return OperationResult.Refused(Refusal.NoLayout);
                    }
                }

                isRetry = true;
                return _service.Delete(key);
            });
        _lastOperationTask = RunAdmittedOperationAsync(request);
        return true;
    }

    internal string? SelectedSavedKey => _main.Selected is { IsUnsaved: false } row ? row.Key : null;

    internal System.Collections.Specialized.INotifyCollectionChanged RowsCollection => _main.Rows;

    internal IReadOnlyList<string> GetOtherSavedNames(string excludeKey) =>
        [.. _main.Rows
            .Where(r => !r.IsUnsaved && !string.Equals(r.Key, excludeKey, StringComparison.Ordinal))
            .Select(r => r.Name)];

    /// <summary>
    /// Runs a non-OperationResult action (such as §9 "Turn it off") under the same busy lock as Save/Restore/Undo so it
    /// is mutually exclusive with window and tray operations, then invokes <paramref name="onFinishedOnUi"/> once idle.
    /// </summary>
    internal async Task RunBusyActionAsync(Func<Task> action, Action? onFinishedOnUi = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Interlocked.CompareExchange(ref _operationInFlight, 1, 0) != 0)
        {
            return;
        }

        if (_externalBusy)
        {
            Volatile.Write(ref _operationInFlight, 0);
            return;
        }

        try
        {
            await _main.MarshalToUiAsync(() =>
            {
                _localBusy = true;
                IsBusy = true;
            }).ConfigureAwait(false);

            await action().ConfigureAwait(false);
        }
        finally
        {
            await _main.MarshalToUiAsync(() =>
            {
                Volatile.Write(ref _operationInFlight, 0);
                _localBusy = false;
                IsBusy = _externalBusy;
                onFinishedOnUi?.Invoke();
            }).ConfigureAwait(false);
        }
    }

    public Task<OperationResult?> RetryAsync(WindowOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_isBusy)
        {
            return Task.FromResult<OperationResult?>(null);
        }

        return RunOperationAsync(request);
    }

    private void ExecuteSave() => _lastOperationTask = SaveAsync();

    private void ExecuteRestore() => _lastOperationTask = RestoreAsync();

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Selected) or nameof(MainViewModel.Desktop) or "")
        {
            UpdateFromMain();
        }
    }

    private void UpdateFromMain()
    {
        LayoutRow? selected = _main.Selected;
        if (selected is null)
        {
            Kind = DetailKind.None;
            Title = string.Empty;
            Subtitle = string.Empty;
            OtherArrangementLine = string.Empty;
            ShowRestore = false;
            ShowSave = false;
            ShowMore = false;
            ShowOtherArrangementLine = false;
            HasPreviousSave = false;
            RaiseAvailabilityChanged();
            return;
        }

        if (selected.IsUnsaved)
        {
            Kind = DetailKind.Unsaved;
            Title = LayoutRow.UnsavedSubtitle;
            IReadOnlyList<MonitorInfo> liveMonitors = _main.Desktop.Monitors;
            Subtitle = liveMonitors.Count > 0 ? Formats.MonitorSizes(liveMonitors) : string.Empty;
            OtherArrangementLine = string.Empty;
            ShowRestore = false;
            ShowSave = true;
            ShowMore = false;
            ShowOtherArrangementLine = false;
            HasPreviousSave = false;
            RaiseAvailabilityChanged();
            return;
        }

        Layout layout = selected.Layout!;
        bool isActive = selected.IsActive;
        Kind = isActive ? DetailKind.Active : DetailKind.Other;
        Title = layout.Name;
        string monitorsText = layout.Monitors.Length > 0 ? Formats.MonitorSizes(layout.Monitors) : string.Empty;
        string savedText = $"saved {Formats.When(layout.SavedAt, _clock.GetUtcNow(), _timeZone)}";
        Subtitle = string.IsNullOrEmpty(monitorsText) ? savedText : $"{monitorsText} \u00B7 {savedText}";
        OtherArrangementLine = isActive ? string.Empty : OtherArrangementSentence;
        ShowRestore = isActive;
        ShowSave = isActive;
        ShowMore = true;
        ShowOtherArrangementLine = !isActive;
        HasPreviousSave = layout.Previous is not null;
        RaiseAvailabilityChanged();
    }

    private void RaiseAvailabilityChanged()
    {
        RaisePropertyChanged(nameof(CanRestore));
        RaisePropertyChanged(nameof(CanSave));
        RaisePropertyChanged(nameof(CanMore));
        SaveCommand.RaiseCanExecuteChanged();
        RestoreCommand.RaiseCanExecuteChanged();
    }
}
