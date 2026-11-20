using System.Diagnostics;
using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Desktop.Interop;
using Berth.Tests.Fakes;

namespace Berth.Tests.Desktop;

public sealed class ProbeIconUnitTests
{
    [Fact]
    public void ProbeIconDeletesOnlyOwnedFileOnDisposeMidTestExceptionReadFailureTimeoutAndPreservesCollisionAndDecoy()
    {
        using var tempDesktop = new TestData();
        string decoyPath = Path.Combine(tempDesktop.Root, "berth-test-probe-decoy.txt");
        File.WriteAllText(decoyPath, "decoy-bytes");

        // 1) Normal dispose removes only the created file; double Dispose is idempotent; decoy untouched.
        string prefix1 = $"berth-test-probe-{Guid.NewGuid():N}";
        string path1 = Path.Combine(tempDesktop.Root, $"{prefix1}.txt");
        var fake1 = new FakeDesktopIcons(new IconPosition(path1, prefix1, 100, 120));
        ProbeIcon probe1 = ProbeIcon.CreateInFolder(fake1, tempDesktop.Root, prefix1, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
        Assert.True(File.Exists(path1));
        probe1.Dispose();
        probe1.Dispose();
        Assert.False(File.Exists(path1));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));

        // 2) Mid-test exception still removes the probe file; decoy untouched.
        string prefix2 = $"berth-test-probe-{Guid.NewGuid():N}";
        string path2 = Path.Combine(tempDesktop.Root, $"{prefix2}.txt");
        var fake2 = new FakeDesktopIcons(new IconPosition(path2, prefix2, 100, 120));
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using ProbeIcon _ = ProbeIcon.CreateInFolder(fake2, tempDesktop.Root, prefix2, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
            Assert.True(File.Exists(path2));
            throw new InvalidOperationException("Simulated mid-test failure.");
        }));
        Assert.False(File.Exists(path2));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));

        // 3) Forced startup timeout deletes created file; decoy untouched.
        string prefix3 = $"berth-test-probe-{Guid.NewGuid():N}";
        string path3 = Path.Combine(tempDesktop.Root, $"{prefix3}.txt");
        var emptyFake = new FakeDesktopIcons();
        Assert.Throws<TimeoutException>(() =>
            ProbeIcon.CreateInFolder(emptyFake, tempDesktop.Root, prefix3, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(15)));
        Assert.False(File.Exists(path3));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));

        // 4) Post-creation ReadIcons() failure deletes created file; decoy untouched.
        string prefix4 = $"berth-test-probe-{Guid.NewGuid():N}";
        string path4 = Path.Combine(tempDesktop.Root, $"{prefix4}.txt");
        var failingFake = new FakeDesktopIcons { Unavailable = true };
        Assert.Throws<DesktopUnavailableException>(() =>
            ProbeIcon.CreateInFolder(failingFake, tempDesktop.Root, prefix4, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)));
        Assert.False(File.Exists(path4));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));

        // 5) Exact-name collision rejects creation and NEVER deletes the pre-existing file.
        string collisionPrefix = $"berth-test-probe-{Guid.NewGuid():N}";
        string collisionPath = Path.Combine(tempDesktop.Root, $"{collisionPrefix}.txt");
        File.WriteAllText(collisionPath, "pre-existing-owner-bytes");
        Assert.Throws<IOException>(() =>
            ProbeIcon.CreateInFolder(emptyFake, tempDesktop.Root, collisionPrefix, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10)));
        Assert.True(File.Exists(collisionPath));
        Assert.Equal("pre-existing-owner-bytes", File.ReadAllText(collisionPath));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));

        // 6) Transient sharing violation on Dispose retains ownership so a subsequent Dispose deletes the file.
        string lockedPrefix = $"berth-test-probe-{Guid.NewGuid():N}";
        string lockedPath = Path.Combine(tempDesktop.Root, $"{lockedPrefix}.txt");
        var lockedFake = new FakeDesktopIcons(new IconPosition(lockedPath, lockedPrefix, 100, 120));
        ProbeIcon lockedProbe = ProbeIcon.CreateInFolder(lockedFake, tempDesktop.Root, lockedPrefix, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10));
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => lockedProbe.Dispose());
            Assert.True(File.Exists(lockedPath));
        }

        lockedProbe.Dispose();
        Assert.False(File.Exists(lockedPath));
        Assert.Equal("decoy-bytes", File.ReadAllText(decoyPath));
    }

    [Fact]
    public void ChooseInBoundsTargetPreservesRequiredSeparationEvenOnNarrowAxis()
    {
        IReadOnlyList<MonitorInfo> narrow = [new(0, 0, 600, 600, 100, Primary: true)];
        (int targetX, int targetY) = ProbeIconTests.ChooseInBoundsTarget(300, 300, 201, 0, narrow);
        Assert.Equal(300, targetY);
        Assert.InRange(targetX, 0, 599);
        Assert.True(Math.Abs(targetX - 300) >= 201, $"Expected separation >= 201, got targetX={targetX}.");
    }

    [Fact]
    public void ReadGridInfoDistinguishesSnapToGridOnAndOffWithUnrelatedFlagsAndSpacingFallback()
    {
        // Controlled desktop fixture with FWF_SNAPTOGRID (0x4) ON plus unrelated bits (including FWF_NOENUMREFRESH = 0x80000).
        var snapOnView = new GridFixtureView(flags: 0x00080244u, spacing: new NativePoint { X = 100, Y = 110 }, defaultSpacing: new NativePoint { X = 75, Y = 75 });
        using (var shellOn = new ShellDesktopIcons(
            openView: () => DesktopView.Open(() => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(snapOnView))), _ => { }),
            monitors: new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)])))
        {
            (bool snap, int sx, int sy) = ProbeIconTests.ReadGridInfo(shellOn);
            Assert.True(snap);
            Assert.Equal((100, 110), (sx, sy));
        }

        // Controlled desktop fixture with FWF_SNAPTOGRID (0x4) OFF (even when FWF_NOENUMREFRESH = 0x80000 is set) and zero GetSpacing -> falls back to GetDefaultSpacing.
        var snapOffView = new GridFixtureView(flags: 0x00080240u, spacing: default, defaultSpacing: new NativePoint { X = 80, Y = 90 });
        using (var shellOff = new ShellDesktopIcons(
            openView: () => DesktopView.Open(() => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(snapOffView))), _ => { }),
            monitors: new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)])))
        {
            (bool snap, int sx, int sy) = ProbeIconTests.ReadGridInfo(shellOff);
            Assert.False(snap);
            Assert.Equal((80, 90), (sx, sy));
        }
    }

    private sealed class FixedMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitors
    {
        public IReadOnlyList<MonitorInfo> Read() => monitors;
    }

    private sealed class GridFixtureView(uint flags, NativePoint spacing, NativePoint defaultSpacing) : IShellView, IFolderView, IFolderView2
    {
        public void GetWindow(out nint phwnd) => phwnd = nint.Zero;
        public void ContextSensitiveHelp(bool fEnterMode) { }
        public int TranslateAccelerator(nint pmsg) => 0;
        public void EnableModeless(bool fEnable) { }
        public void UIActivate(uint uState) { }
        public void Refresh() { }
        public void CreateViewWindow(IShellView? psvPrevious, nint pfs, IShellBrowser? psb, nint prcView, out nint phWnd) => phWnd = nint.Zero;
        public void DestroyViewWindow() { }
        public void GetCurrentInfo(nint pfs) { }
        public void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam) { }
        public void SaveViewState() { }
        public void SelectItem(nint pidlItem, uint uFlags) { }
        public void GetItemObject(uint uItem, in Guid riid, out object ppv) => ppv = new object();
        public void GetCurrentViewMode(out uint viewMode) => viewMode = 0;
        public void SetCurrentViewMode(uint viewMode) { }
        public void GetFolder(in Guid riid, out object ppv) => ppv = new object();
        public void Item(int iItemIndex, out nint ppidl) => ppidl = nint.Zero;
        public void ItemCount(uint uFlags, out int pcItems) => pcItems = 0;
        public void Items(uint uFlags, in Guid riid, out object ppv) => ppv = new object();
        public void GetSelectionMarkedItem(out int piItem) => piItem = -1;
        public void GetFocusedItem(out int piItem) => piItem = -1;
        public void GetItemPosition(nint pidl, out NativePoint ppt) => ppt = default;
        public void GetSpacing(ref NativePoint ppt) => ppt = spacing;
        public void GetDefaultSpacing(out NativePoint ppt) => ppt = defaultSpacing;
        public int GetAutoArrange() => ShellConstants.S_FALSE;
        public void SelectItem(int iItem, uint dwFlags) { }
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) { }
        public void SetGroupBy(nint key, bool fAscending) { }
        public void GetGroupBy(nint pkey, out bool pfAscending) => pfAscending = false;
        public void SetViewProperty(nint pidl, nint propkey, nint propvar) { }
        public void GetViewProperty(nint pidl, nint propkey, nint propvar) { }
        public void SetTileViewProperties(nint pidl, string pszPropList) { }
        public void SetExtendedTileViewProperties(nint pidl, string pszPropList) { }
        public void SetText(int iType, string pwszText) { }
        public void SetCurrentFolderFlags(uint dwMask, uint dwFlags) { }
        public void GetCurrentFolderFlags(out uint pdwFlags) => pdwFlags = flags;
    }

    private sealed class StubShellWindows(object dispatch) : IShellWindows
    {
        public int get_Count() => 1;
        public object? Item(object? index = null) => null;
        public object _NewEnum() => throw new NotImplementedException();
        public void Register(object? pid, int hwnd, int swClass, out int plCookie) => plCookie = 0;
        public void RegisterPending(int lThreadId, ref object? pvarloc, ref object? pvarlocRoot, int swClass, out int plCookie) => plCookie = 0;
        public void Revoke(int lCookie) { }
        public void OnNavigate(int lCookie, ref object? pvarloc) { }
        public void OnActivated(int lCookie, bool fActive) { }
        public int FindWindowSW(ref object? pvarLoc, ref object? pvarLocRoot, int swClass, out int phwnd, int swfwOptions, out object? ppdispOut)
        {
            phwnd = 1;
            ppdispOut = dispatch;
            return ShellConstants.S_OK;
        }
        public void OnCreated(int lCookie, object? punk) { }
        public void ProcessAttachDetach(bool fAttach) { }
    }

    private sealed class StubServiceProvider(object browser) : Berth.Desktop.Interop.IServiceProvider
    {
        public void QueryService(in Guid guidService, in Guid riid, out object? ppvObject) => ppvObject = browser;
    }

    private sealed class StubShellBrowser(IShellView view) : IShellBrowser
    {
        public void GetWindow(out nint phwnd) => phwnd = nint.Zero;
        public void ContextSensitiveHelp(bool fEnterMode) { }
        public void InsertMenusSB(nint hmenuShared, nint lpMenuWidths) { }
        public void SetMenuSB(nint hmenuShared, nint holemenuRes, nint hwndActiveObject) { }
        public void RemoveMenusSB(nint hmenuShared) { }
        public void SetStatusTextSB(string? pszStatusText) { }
        public void EnableModelessSB(bool fEnable) { }
        public int TranslateAcceleratorSB(nint pmsg, ushort wID) => 0;
        public void BrowseObject(nint pidl, uint wFlags) { }
        public void GetViewStateStream(uint grfMode, out object? ppStrm) => ppStrm = null;
        public void GetControlWindow(uint id, out nint phwnd) => phwnd = nint.Zero;
        public void SendControlMsg(uint id, uint uMsg, nuint wParam, nint lParam, out nint pret) => pret = nint.Zero;
        public void QueryActiveShellView(out IShellView ppshv) => ppshv = view;
        public void OnViewWindowActive(IShellView? pshv) { }
        public void SetToolbarItems(nint lpButtons, uint nButtons, uint uFlags) { }
    }
}

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed class ProbeIconTests
{
    // Windows SDK 10.0.26100.0 (ShObjIdl_core.h): FOLDERFLAGS.FWF_SNAPTOGRID = 0x00000004.
    private const uint FwfSnapToGrid = 0x00000004u;

    [Fact]
    public void RealDesktopRoundTripMovesProbePreservesSelectionAndRunsFullLayoutServiceCycle()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var data = new TestData();
        BerthPaths paths = BerthPaths.ForRoot(data.Root);
        var log = new Log(paths, TimeProvider.System);
        var monitors = new SystemMonitors();
        using var shell = new ShellDesktopIcons(monitors, log);

        if (shell.AutoArrange)
        {
            Assert.Skip("Desktop Auto Arrange is enabled; real-desktop probe positioning requires Auto Arrange off.");
        }

        (bool snapToGrid, int spacingX, int spacingY) = ReadGridInfo(shell);
        IReadOnlyList<MonitorInfo> currentMonitors = monitors.Read();
        string arrangementKey = ArrangementKey.From(currentMonitors);

        string probeFilePath;
        using (ProbeIcon probe = ProbeIcon.Create(shell))
        {
            probeFilePath = probe.FilePath;
            IconPosition initial = probe.Icon;
            HashSet<string> selectionBaseline = ReadSelectionIds(shell);

            // Part A: move probe in-bounds by at least max(200, spacingX * 2 + 1) alongside an unknown Id.
            int stepX = Math.Max(200, spacingX * 2 + 1);
            (int target1X, int target1Y) = ChooseInBoundsTarget(initial.X, initial.Y, stepX, 0, currentMonitors);
            Assert.True(
                Math.Abs(target1X - initial.X) >= stepX,
                $"Target 1 X ({target1X}) did not achieve required separation {stepX} from initial X ({initial.X}).");
            probe.ScopedDesktop.SetPositions(
            [
                initial with { X = target1X, Y = target1Y },
                new IconPosition(@"C:\NonExistent\berth-unknown-probe-id.txt", "Unknown", 40, 40),
            ]);

            IconPosition posA = WaitForPosition(
                probe.ScopedDesktop,
                initial.Id,
                $"Move 1 target ({target1X},{target1Y})",
                candidate => IsWithinAcceptance(candidate.X, candidate.Y, target1X, target1Y, snapToGrid, spacingX, spacingY)
                             && !IsWithinAcceptance(candidate.X, candidate.Y, initial.X, initial.Y, snapToGrid, spacingX, spacingY));

            bool exact1 = posA.X == target1X && posA.Y == target1Y;
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Move 1: initial=({initial.X},{initial.Y}), requested=({target1X},{target1Y}), landed=({posA.X},{posA.Y}), snapToGrid={snapToGrid}, spacing=({spacingX},{spacingY}), mode={(exact1 ? "exact" : "grid-snapped")}");

            Assert.True(
                IsWithinAcceptance(posA.X, posA.Y, target1X, target1Y, snapToGrid, spacingX, spacingY),
                $"Probe landed at ({posA.X},{posA.Y}), outside expected ({target1X},{target1Y}) tolerance ({spacingX},{spacingY}).");
            Assert.True(
                selectionBaseline.SetEquals(ReadSelectionIds(shell)),
                "Desktop selection identities changed after Move 1.");

            // Part B: full LayoutService cycle (Save -> Move -> Restore -> Undo) using Explorer's reported positions.
            var store = new LayoutStore(paths);
            var opLock = new OperationLock(paths, TimeSpan.FromSeconds(5));
            var service = new LayoutService(probe.ScopedDesktop, monitors, store, paths, opLock, log, TimeProvider.System);

            OperationResult saveResult = service.Save();
            Assert.Equal(OperationKind.Saved, saveResult.Kind);
            Assert.Equal(1, saveResult.Total);

            Layout? savedLayout = store.Snapshot(arrangementKey).Layout;
            Assert.NotNull(savedLayout);
            Assert.Equal(arrangementKey, savedLayout.Key);
            IconPosition savedIcon = Assert.Single(savedLayout.Icons);
            Assert.Equal(initial.Id, savedIcon.Id, StringComparer.OrdinalIgnoreCase);
            Assert.Equal((posA.X, posA.Y), (savedIcon.X, savedIcon.Y));

            // Move probe to a distinct second in-bounds position outside posA's acceptance region.
            int stepY = Math.Max(200, spacingY * 2 + 1);
            (int target2X, int target2Y) = ChooseInBoundsTarget(posA.X, posA.Y, 0, stepY, currentMonitors);
            Assert.True(
                Math.Abs(target2Y - posA.Y) >= stepY,
                $"Target 2 Y ({target2Y}) did not achieve required separation {stepY} from posA Y ({posA.Y}).");
            probe.ScopedDesktop.SetPositions([posA with { X = target2X, Y = target2Y }]);

            IconPosition posB = WaitForPosition(
                probe.ScopedDesktop,
                initial.Id,
                $"Move 2 target ({target2X},{target2Y})",
                candidate => IsWithinAcceptance(candidate.X, candidate.Y, target2X, target2Y, snapToGrid, spacingX, spacingY)
                             && !IsWithinAcceptance(candidate.X, candidate.Y, posA.X, posA.Y, snapToGrid, spacingX, spacingY));

            Assert.True(
                IsWithinAcceptance(posB.X, posB.Y, target2X, target2Y, snapToGrid, spacingX, spacingY),
                $"Move 2 landed at ({posB.X},{posB.Y}), outside expected ({target2X},{target2Y}) tolerance ({spacingX},{spacingY}).");
            Assert.False(
                IsWithinAcceptance(posB.X, posB.Y, posA.X, posA.Y, snapToGrid, spacingX, spacingY),
                $"Move 2 landed at ({posB.X},{posB.Y}), still within posA ({posA.X},{posA.Y}) acceptance region.");
            Assert.True(
                selectionBaseline.SetEquals(ReadSelectionIds(shell)),
                "Desktop selection identities changed after Move 2.");

            // Restore: returns probe to exact Explorer-reported posA and writes undo.json with posB.
            OperationResult restoreResult = service.Restore();
            Assert.Equal(OperationKind.Restored, restoreResult.Kind);
            Assert.Equal(1, restoreResult.InPlace);
            Assert.Equal(0, restoreResult.NotPlaced);
            Assert.Equal(0, restoreResult.Missing);
            Assert.True(service.CanUndo);
            Assert.True(File.Exists(paths.UndoFile));

            IconPosition afterRestore = Assert.Single(probe.ScopedDesktop.ReadIcons());
            Assert.Equal((posA.X, posA.Y), (afterRestore.X, afterRestore.Y));
            Assert.True(
                selectionBaseline.SetEquals(ReadSelectionIds(shell)),
                "Desktop selection identities changed after Restore.");

            UndoSnapshot? undoSnapshot = System.Text.Json.JsonSerializer.Deserialize(
                File.ReadAllText(paths.UndoFile),
                BerthJson.Default.UndoSnapshot);
            Assert.NotNull(undoSnapshot);
            Assert.Equal(arrangementKey, undoSnapshot.Key);
            IconPosition undoIcon = Assert.Single(undoSnapshot.Icons);
            Assert.Equal(initial.Id, undoIcon.Id, StringComparer.OrdinalIgnoreCase);
            Assert.Equal((posB.X, posB.Y), (undoIcon.X, undoIcon.Y));

            // Undo: returns probe to exact Explorer-reported posB and deletes undo.json.
            OperationResult undoResult = service.Undo();
            Assert.Equal(OperationKind.Undone, undoResult.Kind);
            Assert.Equal(1, undoResult.InPlace);
            Assert.Equal(0, undoResult.NotPlaced);
            Assert.Equal(0, undoResult.Missing);
            Assert.False(File.Exists(paths.UndoFile));
            Assert.False(service.CanUndo);

            IconPosition afterUndo = Assert.Single(probe.ScopedDesktop.ReadIcons());
            Assert.Equal((posB.X, posB.Y), (afterUndo.X, afterUndo.Y));
            Assert.True(
                selectionBaseline.SetEquals(ReadSelectionIds(shell)),
                "Desktop selection identities changed after Undo.");
        }

        Assert.False(File.Exists(probeFilePath), "ProbeIcon.Dispose left the probe file on the desktop.");
    }

    internal static (bool SnapToGrid, int SpacingX, int SpacingY) ReadGridInfo(ShellDesktopIcons shell) =>
        shell.RunWithView(desktop =>
        {
            bool snapToGrid = false;
            if (desktop.View2 is not null)
            {
                desktop.View2.GetCurrentFolderFlags(out uint flags);
                snapToGrid = (flags & FwfSnapToGrid) != 0;
            }

            NativePoint spacing = default;
            desktop.View.GetSpacing(ref spacing);
            if (spacing.X <= 0 || spacing.Y <= 0)
            {
                desktop.View.GetDefaultSpacing(out spacing);
            }

            Assert.True(spacing.X > 0 && spacing.Y > 0, $"Expected positive desktop grid spacing, got ({spacing.X},{spacing.Y}).");
            return (snapToGrid, spacing.X, spacing.Y);
        });

    private static HashSet<string> ReadSelectionIds(ShellDesktopIcons shell)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return ReadSelectionIdsOnce(shell);
            }
            catch (COMException ex) when (attempt == 0 && ex.HResult == unchecked((int)0x80070490))
            {
                // Transient selection change between ItemCount and Items: retry the full snapshot once.
            }
        }
    }

    private static HashSet<string> ReadSelectionIdsOnce(ShellDesktopIcons shell) =>
        shell.RunWithView(desktop =>
        {
            object? folderObj = null;
            object? enumObj = null;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                desktop.View.ItemCount(ShellConstants.SVGIO_SELECTION, out int selectedCount);
                if (selectedCount == 0)
                {
                    return ids;
                }

                Guid folderIid = typeof(IShellFolder).GUID;
                desktop.View.GetFolder(in folderIid, out folderObj);
                var folder = (IShellFolder)folderObj!;

                Guid enumIid = typeof(IEnumIDList).GUID;
                desktop.View.Items(ShellConstants.SVGIO_SELECTION, in enumIid, out enumObj);
                var enumerator = (IEnumIDList)enumObj!;

                var batch = new nint[1];
                while (true)
                {
                    batch[0] = nint.Zero;
                    int hr = enumerator.Next(1, batch, out uint fetched);
                    if (hr < 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }

                    if (fetched == 0)
                    {
                        break;
                    }

                    nint pidl = batch[0];
                    try
                    {
                        folder.GetDisplayNameOf(pidl, ShellConstants.SHGDN_FORPARSING, out StrRet parsingRet);
                        string id = ShellDesktopIcons.DecodeStrRet(ref parsingRet, pidl, Marshal.FreeCoTaskMem);
                        if (!string.IsNullOrEmpty(id))
                        {
                            ids.Add(id);
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pidl);
                    }
                }

                return ids;
            }
            finally
            {
                if (enumObj is not null && Marshal.IsComObject(enumObj))
                {
                    _ = Marshal.ReleaseComObject(enumObj);
                }

                if (folderObj is not null && Marshal.IsComObject(folderObj))
                {
                    _ = Marshal.ReleaseComObject(folderObj);
                }
            }
        });

    internal static (int X, int Y) ChooseInBoundsTarget(
        int startX,
        int startY,
        int deltaX,
        int deltaY,
        IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo host = monitors.FirstOrDefault(m =>
            startX >= m.X && startX < m.X + m.Width && startY >= m.Y && startY < m.Y + m.Height)
            ?? monitors.First(m => m.Primary);

        int targetX = ChooseAxisTarget(startX, deltaX, host.X, host.X + host.Width - 1);
        int targetY = ChooseAxisTarget(startY, deltaY, host.Y, host.Y + host.Height - 1);
        return (targetX, targetY);
    }

    private static int ChooseAxisTarget(int start, int delta, int axisMin, int axisMax)
    {
        if (delta == 0)
        {
            return start;
        }

        const int preferredMargin = 120;
        int softMin = Math.Min(axisMax, axisMin + preferredMargin);
        int softMax = Math.Max(axisMin, axisMax - preferredMargin);

        int plus = start + delta;
        int minus = start - delta;

        if (plus >= softMin && plus <= softMax)
        {
            return plus;
        }

        if (minus >= softMin && minus <= softMax)
        {
            return minus;
        }

        if (plus >= axisMin && plus <= axisMax)
        {
            return plus;
        }

        if (minus >= axisMin && minus <= axisMax)
        {
            return minus;
        }

        return Math.Clamp(plus, axisMin, axisMax);
    }

    private static bool IsWithinAcceptance(
        int actualX,
        int actualY,
        int targetX,
        int targetY,
        bool snapToGrid,
        int spacingX,
        int spacingY)
    {
        if (!snapToGrid)
        {
            return actualX == targetX && actualY == targetY;
        }

        return Math.Abs(actualX - targetX) <= spacingX && Math.Abs(actualY - targetY) <= spacingY;
    }

    private static IconPosition WaitForPosition(
        ScopedDesktopIcons scoped,
        string expectedId,
        string description,
        Func<IconPosition, bool> predicate)
    {
        var elapsed = Stopwatch.StartNew();
        IconPosition last = Assert.Single(scoped.ReadIcons());
        while (true)
        {
            if (string.Equals(last.Id, expectedId, StringComparison.OrdinalIgnoreCase) && predicate(last))
            {
                return last;
            }

            if (elapsed.Elapsed >= TimeSpan.FromSeconds(2))
            {
                throw new TimeoutException(
                    $"Probe icon '{expectedId}' did not settle at {description} within 2s (last observed: ({last.X},{last.Y})).");
            }

            Thread.Sleep(50);
            last = Assert.Single(scoped.ReadIcons());
        }
    }
}
