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
    private readonly LayoutStore _store;
    private readonly IDesktopIcons _desktop;
    private readonly IMonitors _monitors;
    private readonly TimeProvider _clock;
    private readonly Func<Action, Task> _marshalToUiAsync;

    private int _refreshInFlight;
    private LayoutRow? _selected;
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
        Func<Action, Task>? marshalToUiAsync = null)
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
    }

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
            if (Set(ref _selected, value))
            {
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

    /// <summary>
    /// Reads monitors, desktop icons, auto-arrange state, and saved layouts off the UI thread, then applies rows,
    /// selection, and comparison on the UI thread. Overlapping calls return immediately without starting a second read.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
        {
            return;
        }

        try
        {
            RefreshSnapshot snapshot = await Task.Run(CaptureSnapshot).ConfigureAwait(false);
            await _marshalToUiAsync(() => ApplySnapshot(snapshot)).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _refreshInFlight, 0);
        }
    }

    /// <summary>Fire-and-forget convenience entry point delegating to <see cref="RefreshAsync"/>.</summary>
    public Task Refresh() => RefreshAsync();

    private RefreshSnapshot CaptureSnapshot()
    {
        DateTimeOffset checkedAt = _clock.GetUtcNow();

        // 1. Read monitors independently so a shell/Explorer outage still preserves the known monitor arrangement.
        IReadOnlyList<MonitorInfo> monitors;
        bool monitorsAvailable;
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

        string? currentKey = monitorsAvailable ? ArrangementKey.From(monitors) : null;

        // 2. Read desktop icons and auto-arrange state independently.
        IReadOnlyList<IconPosition> icons;
        bool autoArrange;
        bool desktopUnavailable;
        try
        {
            icons = _desktop.ReadIcons();
            autoArrange = _desktop.AutoArrange;
            desktopUnavailable = false;
        }
        catch (DesktopUnavailableException)
        {
            icons = [];
            autoArrange = false;
            desktopUnavailable = true;
        }

        var desktopState = new DesktopState(
            icons,
            monitors,
            autoArrange,
            Unavailable: desktopUnavailable || !monitorsAvailable,
            CheckedAt: checkedAt);

        // 3. Load saved layouts independently of shell availability.
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
        // Capture the live selection at UI-application time so user selection changes during an async read win.
        string? previouslySelectedKey = _selected?.Key;

        Desktop = snapshot.Desktop;
        Unreadable = snapshot.Unreadable;
        StoreReadError = snapshot.StoreReadError;

        List<LayoutRow> nextRows = BuildRows(snapshot);

        Rows.Clear();
        foreach (LayoutRow row in nextRows)
        {
            Rows.Add(row);
        }

        LayoutRow? nextSelected = ChooseSelection(nextRows, previouslySelectedKey);
        bool selectionChanged = !EqualityComparer<LayoutRow?>.Default.Equals(_selected, nextSelected);
        if (selectionChanged)
        {
            Selected = nextSelected;
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
