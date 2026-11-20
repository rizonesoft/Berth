using Berth.Core;
using Berth.Core.Model;
using Berth.Tests.Fakes;
using Berth.Window;
using Berth.Window.Mvvm;

namespace Berth.Tests.Window;

public sealed class MainViewModelTests
{
    private static readonly MonitorInfo MonA = new(0, 0, 1920, 1080, 100, true);
    private static readonly MonitorInfo MonB = new(0, 0, 2560, 1440, 100, true);
    private static readonly MonitorInfo MonC = new(0, 0, 3840, 2160, 150, true);
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 20, 9, 14, 0, TimeSpan.Zero);

    [Fact]
    public void ObservableObjectAndRelayCommandRaiseEventsOncePerActualChange()
    {
        var probe = new ProbeObservable();
        List<string?> changed = [];
        probe.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        probe.Value = "alpha";
        probe.Value = "alpha"; // Same value must not raise again.
        probe.Value = "beta";
        Assert.Equal(["Value", "Value"], changed);

        int executed = 0;
        bool canExecute = false;
        int canExecuteChanges = 0;
        var cmd = new RelayCommand(() => executed++, () => canExecute);
        cmd.CanExecuteChanged += (_, _) => canExecuteChanges++;

        Assert.False(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(0, executed);

        canExecute = true;
        cmd.RaiseCanExecuteChanged();
        Assert.Equal(1, canExecuteChanges);
        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(1, executed);
    }

    [Fact]
    public async Task RowsFollowSpecOrderForNoLayoutsActiveLayoutAndUnsavedBesideSaved()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        var monitors = new FakeMonitors(MonA);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var vm = new MainViewModel(store, desktop, monitors, new FixedClock(FixedNow));

        // 1. No saved layouts -> single "This arrangement" / "Not saved yet" row selected.
        await vm.RefreshAsync();
        Assert.Single(vm.Rows);
        Assert.Equal("This arrangement", vm.Rows[0].Name);
        Assert.Equal("Not saved yet", vm.Rows[0].Subtitle);
        Assert.True(vm.Rows[0].IsUnsaved);
        Assert.Equal(vm.Rows[0], vm.Selected);

        // 2. Two saved layouts for OTHER arrangements beside an unsaved current arrangement -> unsaved first, then alphabetical.
        SaveLayout(store, [MonB], "Work");
        SaveLayout(store, [MonC], "alpha");
        await vm.RefreshAsync();
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal(["This arrangement", "alpha", "Work"], vm.Rows.Select(r => r.Name));
        Assert.Equal("1 monitor \u00B7 1 icon", vm.Rows[1].Subtitle);
        Assert.Equal(vm.Rows[0], vm.Selected);

        // 3. Save current arrangement with name sorting last ("Zebra") -> active layout moves to index 0, others sorted by name.
        SaveLayout(store, [MonA], "Zebra");
        await vm.RefreshAsync();
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal(["Zebra", "alpha", "Work"], vm.Rows.Select(r => r.Name));
        Assert.True(vm.Rows[0].IsActive);
        Assert.False(vm.Rows[1].IsActive);
        Assert.Equal(vm.Rows[0], vm.Selected);
    }

    [Fact]
    public async Task SelectionPersistsAcrossReloadFallsBackOnDeleteAndRespectsSelectionChangedDuringInFlightRead()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonA], "Docked");
        SaveLayout(store, [MonB], "Laptop");
        SaveLayout(store, [MonC], "Office");

        var monitors = new FakeMonitors(MonA);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var vm = new MainViewModel(store, desktop, monitors, new FixedClock(FixedNow));

        // Opening rule selects active layout ("Docked").
        await vm.RefreshAsync();
        Assert.Equal("Docked", vm.Selected?.Name);

        // User selects "Laptop"; reload keeps "Laptop" selected.
        vm.Selected = vm.Rows.Single(r => r.Name == "Laptop");
        await vm.RefreshAsync();
        Assert.Equal("Laptop", vm.Selected?.Name);

        // Delete "Laptop" on disk; reload falls back to opening rule ("Docked").
        File.Delete(paths.LayoutFile(ArrangementKey.From([MonB])));
        await vm.RefreshAsync();
        Assert.Equal("Docked", vm.Selected?.Name);

        // User changes selection to "Office" WHILE a refresh read is in flight -> UI application preserves "Office".
        using var readEntered = new ManualResetEventSlim(false);
        using var readRelease = new ManualResetEventSlim(false);
        var gatedDesktop = new GatedReadDesktop(desktop, () =>
        {
            readEntered.Set();
            readRelease.Wait(TimeSpan.FromSeconds(5));
        });
        var vmGated = new MainViewModel(store, gatedDesktop, monitors, new FixedClock(FixedNow));
        await vmGated.RefreshAsync(); // Initial load (not gated yet since gate isn't armed until armed).
        Assert.Equal("Docked", vmGated.Selected?.Name);

        gatedDesktop.Arm();
        Task inFlight = vmGated.RefreshAsync();
        Assert.True(readEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        vmGated.Selected = vmGated.Rows.Single(r => r.Name == "Office");
        readRelease.Set();
        await inFlight.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("Office", vmGated.Selected?.Name);
    }

    [Fact]
    public async Task CoversAllFourComparisonScenariosAndPreservesKnownMonitorsDuringExplorerOutage()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        // Active layout sorts LAST alphabetically ("Zebra") to prove Explorer outage still knows it is active!
        SaveLayout(store, [MonA], "Zebra", new IconPosition("id-1", "One", 100, 200));
        SaveLayout(store, [MonB], "Alpha", new IconPosition("id-2", "Two", 300, 400));

        var monitors = new FakeMonitors(MonA);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 999, 888)); // Moved!
        var vm = new MainViewModel(store, desktop, monitors, new FixedClock(FixedNow));

        // 1. Active layout with live desktop -> Compared (shows Moved icon).
        await vm.RefreshAsync();
        Assert.Equal("Zebra", vm.Selected?.Name);
        Assert.Equal(ComparisonKind.Compared, vm.ComparisonKind);
        Assert.NotNull(vm.Comparison);
        Assert.Equal(1, vm.Comparison.Moved);
        Assert.Equal(IconState.Moved, vm.Comparison.Icons[0].State);

        // 2. Other arrangement selected -> Neutral (saved positions, Neutral state).
        vm.Selected = vm.Rows.Single(r => r.Name == "Alpha");
        Assert.Equal(ComparisonKind.Neutral, vm.ComparisonKind);
        Assert.Equal(IconState.Neutral, vm.Comparison!.Icons[0].State);
        Assert.Equal(300, vm.Comparison.Icons[0].Saved.X);

        // 3. Explorer outage (desktop.Unavailable = true) while monitors still succeed -> preserves active "Zebra"
        //    and shows Neutral(savedLayout), then recovers to live Compare on next refresh without losing selection!
        vm.Selected = null; // Reset to test opening rule during outage.
        desktop.Unavailable = true;
        await vm.RefreshAsync();
        Assert.True(vm.Desktop.Unavailable);
        Assert.Equal("Zebra", vm.Selected?.Name);
        Assert.True(vm.Selected!.IsActive);
        Assert.Equal(ComparisonKind.Neutral, vm.ComparisonKind);
        Assert.Equal(IconState.Neutral, vm.Comparison!.Icons[0].State);
        Assert.Equal(100, vm.Comparison.Icons[0].Saved.X);

        desktop.Unavailable = false;
        await vm.RefreshAsync();
        Assert.False(vm.Desktop.Unavailable);
        Assert.Equal("Zebra", vm.Selected?.Name);
        Assert.Equal(ComparisonKind.Compared, vm.ComparisonKind);
        Assert.Equal(1, vm.Comparison!.Moved);

        // 4. Unsaved current arrangement -> opening rule selects "This arrangement" and shows live icons with Neutral state.
        vm.Selected = null;
        monitors.Monitors = [MonC];
        await vm.RefreshAsync();
        Assert.True(vm.Selected?.IsUnsaved);
        Assert.Equal(ComparisonKind.Unsaved, vm.ComparisonKind);
        Assert.Equal(IconState.Neutral, vm.Comparison!.Icons[0].State);
        Assert.Equal(999, vm.Comparison.Icons[0].Saved.X);
    }

    [Fact]
    public async Task HandlesCorruptCurrentLayoutFileAndFolderReadFailureWithRecovery()
    {
        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var store = new LayoutStore(paths);
        SaveLayout(store, [MonB], "Other");

        // Write a corrupt JSON file for the CURRENT arrangement key.
        string currentKey = ArrangementKey.From([MonA]);
        string corruptPath = paths.LayoutFile(currentKey);
        BerthPaths.EnsureFolderFor(corruptPath);
        File.WriteAllText(corruptPath, "{ broken json");

        var monitors = new FakeMonitors(MonA);
        var desktop = new FakeDesktopIcons(new IconPosition("id-1", "One", 10, 20));
        var vm = new MainViewModel(store, desktop, monitors, new FixedClock(FixedNow));

        await vm.RefreshAsync();
        // Corrupt current file is omitted from Rows (NOT replaced with "Not saved yet"), exposed in Unreadable, and "Other" is selected.
        Assert.Single(vm.Rows);
        Assert.Equal("Other", vm.Rows[0].Name);
        Assert.Equal("Other", vm.Selected?.Name);
        Assert.Contains(vm.Unreadable, u => string.Equals(u.FileName, currentKey + ".json", StringComparison.OrdinalIgnoreCase));

        // Folder-wide read failure: replace LayoutsFolder with a file so LoadAll() throws IOException, then recover.
        using var failData = new TestData();
        BerthPaths failPaths = BerthPaths.ForRoot(failData.Root);
        BerthPaths.EnsureFolderFor(failPaths.LayoutsFolder);
        File.WriteAllText(failPaths.LayoutsFolder, "not a directory");
        var failStore = new LayoutStore(failPaths);
        var failVm = new MainViewModel(failStore, desktop, monitors, new FixedClock(FixedNow));

        await failVm.RefreshAsync();
        Assert.NotNull(failVm.StoreReadError);
        Assert.Empty(failVm.Rows);

        // Recover by removing the blocking file.
        File.Delete(failPaths.LayoutsFolder);
        await failVm.RefreshAsync();
        Assert.Null(failVm.StoreReadError);
        Assert.Single(failVm.Rows);
        Assert.True(failVm.Rows[0].IsUnsaved);
    }

    private static void SaveLayout(LayoutStore store, MonitorInfo[] monitors, string name, params IconPosition[] icons)
    {
        IconPosition[] effectiveIcons = icons.Length > 0 ? icons : [new IconPosition("id-default", "Default", 10, 20)];
        var layout = new Layout(
            Layout.CurrentSchemaVersion,
            ArrangementKey.From(monitors),
            name,
            FixedNow,
            [.. monitors],
            [.. effectiveIcons],
            Previous: null);
        store.Save(layout);
    }

    private sealed class ProbeObservable : ObservableObject
    {
        private string _value = string.Empty;

        public string Value
        {
            get => _value;
            set => Set(ref _value, value);
        }
    }

    private sealed class GatedReadDesktop(IDesktopIcons inner, Action onRead) : IDesktopIcons
    {
        private int _armed;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public bool AutoArrange => inner.AutoArrange;

        public IReadOnlyList<IconPosition> ReadIcons()
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                onRead();
            }

            return inner.ReadIcons();
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons) => inner.SetPositions(icons);
        public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove) => inner.SetPositions(icons, beforeMove);
        public bool TryTurnAutoArrangeOff() => inner.TryTurnAutoArrangeOff();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
