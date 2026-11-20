using System.Collections.ObjectModel;
using System.IO;
using Berth.Core;
using Berth.Core.Model;
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
    private readonly object _refreshGate = new();

    private Task? _activeRefreshTask;
    private bool _isApplyingSnapshot;
    private LayoutRow? _selected;
    private string? _lastKnownSelectedKey;
    private string? _pendingSelectedKey;
    private DesktopState _desktopState;
    private LayoutComparison? _comparison;
    private ComparisonKind _comparisonKind;
    private IReadOnlyList<UnreadableFile> _unreadable = [];
    private string? _storeReadError;

    public MainViewModel(
        LayoutStore store,
        IDesktopIcons desktop,
        IMonitors monitors,
        TimeProvider clock,
        Func<Action, Task>? marshalToUiAsync = null,
        LayoutService? service = null,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _desktop = desktop;
        _monitors = monitors;
        _clock = clock;
        _marshalToUiAsync = marshalToUiAsync ?? (action =>
        {
            action();
            return Task.CompletedTask;
        });
        _desktopState = DesktopState.Empty(clock.GetUtcNow());
        Detail = new DetailViewModel(this, service, clock, timeZone);
    }

    public DetailViewModel Detail { get; }

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
                    }
                }
                else if (!_isApplyingSnapshot)
                {
                    _lastKnownSelectedKey = null;
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

    internal Task MarshalToUiAsync(Action action) => _marshalToUiAsync(action);

    /// <summary>
    /// Reads monitors, desktop icons, auto-arrange state, and saved layouts off the UI thread, then applies rows,
    /// selection, and comparison on the UI thread. Overlapping calls return immediately without starting a second read.
    /// </summary>
    public Task RefreshAsync()
    {
        TaskCompletionSource gate;
        lock (_refreshGate)
        {
            if (_activeRefreshTask is not null)
            {
                return Task.CompletedTask;
            }

            gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRefreshTask = gate.Task;
        }

        _ = RunRefreshCoreAsync(gate);
        return gate.Task;
    }

    /// <summary>
    /// Waits for any in-flight poll to finish, then runs a fresh capture guaranteed to start after an operation's write.
    /// When <paramref name="selectKey"/> is given, publishes that saved arrangement key only after any older poll finishes,
    /// and retains it until a post-write snapshot selects that saved row.
    /// </summary>
    public async Task RefreshAfterOperationAsync(string? selectKey = null)
    {
        while (true)
        {
            Task? existing;
            TaskCompletionSource? gate = null;
            lock (_refreshGate)
            {
                existing = _activeRefreshTask;
                if (existing is null)
                {
                    gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _activeRefreshTask = gate.Task;
                }
            }

            if (existing is not null)
            {
                await existing.ConfigureAwait(false);
                continue;
            }

            if (selectKey is not null)
            {
                await _marshalToUiAsync(() =>
                {
                    _pendingSelectedKey = selectKey;
                    _lastKnownSelectedKey = selectKey;
                }).ConfigureAwait(false);
            }

            _ = RunRefreshCoreAsync(gate!);
            await gate!.Task.ConfigureAwait(false);
            return;
        }
    }

    private async Task RunRefreshCoreAsync(TaskCompletionSource gate)
    {
        try
        {
            RefreshSnapshot? snapshot = await Task.Run(CaptureSnapshot).ConfigureAwait(false);
            if (snapshot is not null)
            {
                await _marshalToUiAsync(() => ApplySnapshot(snapshot)).ConfigureAwait(false);
            }
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

            gate.TrySetResult();
        }
    }

    /// <summary>Fire-and-forget convenience entry point delegating to <see cref="RefreshAsync"/>.</summary>
    public Task Refresh() => RefreshAsync();

    private RefreshSnapshot? CaptureSnapshot()
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

        return new RefreshSnapshot(currentKey, desktopState, layouts, unreadable, storeReadError);
    }

    private void ApplySnapshot(RefreshSnapshot snapshot)
    {
        // Capture the live selection at UI-application time so user selection changes during an async read win,
        // while a post-operation target key takes priority until a usable snapshot contains that row.
        string? preferredKey = _pendingSelectedKey ?? _selected?.Key ?? _lastKnownSelectedKey;

        Desktop = snapshot.Desktop;
        Unreadable = snapshot.Unreadable;
        StoreReadError = snapshot.StoreReadError;

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
                RebuildComparison();
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
            RebuildComparison();
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

    private void RebuildComparison()
    {
        if (_selected is null)
        {
            ComparisonKind = ComparisonKind.None;
            Comparison = null;
            return;
        }

        if (_selected.IsUnsaved)
        {
            if (_desktopState.Unavailable || _desktopState.Monitors.Count == 0)
            {
                ComparisonKind = ComparisonKind.None;
                Comparison = null;
            }
            else
            {
                ComparisonKind = ComparisonKind.Unsaved;
                Comparison = LayoutComparison.Unsaved(_desktopState.Monitors, _desktopState.Icons);
            }

            return;
        }

        Layout saved = _selected.Layout!;
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
    }

    private sealed record RefreshSnapshot(
        string? CurrentKey,
        DesktopState Desktop,
        IReadOnlyList<Layout> Layouts,
        IReadOnlyList<UnreadableFile> Unreadable,
        string? StoreReadError);
}
