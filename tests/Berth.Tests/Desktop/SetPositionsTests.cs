using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Desktop.Interop;

namespace Berth.Tests.Desktop;

public sealed class SetPositionsTests
{
    private const uint StrRetWstr = 0u;
    private const uint ExpectedPositionFlags = ShellConstants.SVSI_POSITIONITEM | ShellConstants.SVSI_NOSTATECHANGE;

    [Fact]
    public void SetPositionsBatchesMatchedIconsIgnoringCaseFirstDesktopWinsConvertsToViewAndPreservesSelectionFlags()
    {
        var monitors = new FixedMonitors([
            new(-1920, -240, 1920, 1080, 125, Primary: false),
            new(0, 0, 2560, 1440, 100, Primary: true),
        ]);
        var fixture = new ShellFixture(
        [
            new FixtureItem(@"C:\Desktop\Alpha.txt"),
            new FixtureItem(@"C:\Desktop\Beta.txt"),
            new FixtureItem(@"c:\desktop\alpha.txt"), // Duplicate desktop parsing name; first PIDL must win.
        ]);

        using var icons = fixture.CreateDesktopIcons(monitors);
        icons.SetPositions(
        [
            new(@"C:\DESKTOP\BETA.TXT", "Beta", 500, 600),
            new(@"C:\DESKTOP\ALPHA.TXT", "Alpha", -1800, -200),
            new(@"c:\desktop\alpha.txt", "Alpha Repeated", 999, 999), // Repeated request; must not submit twice.
            new(@"C:\Desktop\Missing.txt", "Missing", 10, 20), // Unknown Id; ignored without error.
        ]);

        Assert.Equal(1, fixture.SelectAndPositionCalls);
        Assert.Equal(ExpectedPositionFlags, fixture.LastPositionFlags);
        Assert.Equal(2, fixture.LastPositionedItems.Count);

        // Index 0: Beta -> view = (500 - (-1920), 600 - (-240)) = (2420, 840)
        Assert.Equal(fixture.AllocatedPidlOrder[1], fixture.LastPositionedItems[0].Pidl);
        Assert.Equal(2420, fixture.LastPositionedItems[0].Point.X);
        Assert.Equal(840, fixture.LastPositionedItems[0].Point.Y);

        // Index 1: Alpha (first desktop PIDL, index 0, NOT duplicate index 2) -> view = (-1800 - (-1920), -200 - (-240)) = (120, 40)
        Assert.Equal(fixture.AllocatedPidlOrder[0], fixture.LastPositionedItems[1].Pidl);
        Assert.Equal(120, fixture.LastPositionedItems[1].Point.X);
        Assert.Equal(40, fixture.LastPositionedItems[1].Point.Y);

        Assert.True(fixture.AllSubmittedPidlsWereLiveDuringCall);
        fixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 3);
    }

    [Fact]
    public void SetPositionsSkipsSelectAndPositionItemsForReachableEmptyOrAllUnknownInputs()
    {
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);

        var emptyFixture = new ShellFixture([new FixtureItem(@"C:\Desktop\Only.txt")]);
        using (ShellDesktopIcons icons = emptyFixture.CreateDesktopIcons(monitors))
        {
            icons.SetPositions([]);
        }

        Assert.Equal(0, emptyFixture.SelectAndPositionCalls);
        emptyFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 0);

        var unmatchedFixture = new ShellFixture([new FixtureItem(@"C:\Desktop\Only.txt")]);
        using (ShellDesktopIcons icons = unmatchedFixture.CreateDesktopIcons(monitors))
        {
            icons.SetPositions([new(@"C:\Desktop\DoesNotExist.txt", "Missing", 50, 60)]);
        }

        Assert.Equal(0, unmatchedFixture.SelectAndPositionCalls);
        unmatchedFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 1);
    }

    [Fact]
    public void SetPositionsEmptyRequestStillOpensViewAndThrowsDesktopUnavailableExceptionWhenUnreachable()
    {
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);
        int factoryCalls = 0;
        using var icons = new ShellDesktopIcons(
            openView: () =>
            {
                factoryCalls++;
                throw new DesktopUnavailableException("Explorer is restarting.");
            },
            monitors: monitors);

        Assert.Throws<DesktopUnavailableException>(() => icons.SetPositions([]));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void SetPositionsRejectsNullInputWithoutOpeningView()
    {
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);
        int factoryCalls = 0;
        using var icons = new ShellDesktopIcons(
            openView: () =>
            {
                factoryCalls++;
                throw new InvalidOperationException("Should not open view for null input.");
            },
            monitors: monitors);

        Assert.Throws<ArgumentNullException>(() => icons.SetPositions(null!));
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public void SetPositionsFreesAllPidlsAndComObjectsOnFailuresAndWrapsErrorsAsDesktopUnavailableException()
    {
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);

        // GetFolder succeeds, Items throws -> folder and view released once.
        Exception itemsError = Marshal.GetExceptionForHR(unchecked((int)0x800706BA))!;
        var itemsFailFixture = new ShellFixture([new FixtureItem(@"C:\Desktop\A.txt")], itemsError: itemsError);
        using (ShellDesktopIcons icons = itemsFailFixture.CreateDesktopIcons(monitors))
        {
            DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() =>
                icons.SetPositions([new(@"C:\Desktop\A.txt", "A", 10, 20)]));
            Assert.Same(itemsError, thrown.InnerException);
        }

        itemsFailFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 0);

        // Next returns E_FAIL with nonzero sentinel slot -> sentinel never freed, earlier PIDL freed.
        nint sentinel = new(0x55AA00);
        var freedPointers = new List<nint>();
        var nextFailFixture = new ShellFixture(
            [new FixtureItem(@"C:\Desktop\A.txt")],
            nextErrorHrAfterItems: unchecked((int)0x80004005),
            sentinelOnZeroOrError: sentinel,
            onFreeSpy: freedPointers.Add);
        using (ShellDesktopIcons icons = nextFailFixture.CreateDesktopIcons(monitors))
        {
            Assert.Throws<DesktopUnavailableException>(() =>
                icons.SetPositions([new(@"C:\Desktop\A.txt", "A", 10, 20)]));
        }

        Assert.DoesNotContain(sentinel, freedPointers);
        nextFailFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 1);

        // GetDisplayNameOf throws FileNotFoundException on second item -> wrapped, all PIDLs/COM freed.
        var disappearedError = new FileNotFoundException("Item deleted during enumeration");
        var disappearFixture = new ShellFixture(
        [
            new FixtureItem(@"C:\Desktop\A.txt"),
            new FixtureItem(@"C:\Desktop\B.txt", DisplayNameError: disappearedError),
        ]);
        using (ShellDesktopIcons icons = disappearFixture.CreateDesktopIcons(monitors))
        {
            DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() =>
                icons.SetPositions([new(@"C:\Desktop\A.txt", "A", 10, 20)]));
            Assert.Same(disappearedError, thrown.InnerException);
        }

        disappearFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 1);

        // GetDisplayNameOf throws COMException on second item -> wrapped, zero positioning calls, all PIDLs/COM freed.
        Exception nameComError = Marshal.GetExceptionForHR(unchecked((int)0x800706BA))!;
        var nameComFailFixture = new ShellFixture(
        [
            new FixtureItem(@"C:\Desktop\A.txt"),
            new FixtureItem(@"C:\Desktop\B.txt", DisplayNameError: nameComError),
        ]);
        using (ShellDesktopIcons icons = nameComFailFixture.CreateDesktopIcons(monitors))
        {
            DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() =>
                icons.SetPositions([new(@"C:\Desktop\A.txt", "A", 10, 20)]));
            Assert.Same(nameComError, thrown.InnerException);
        }

        Assert.Equal(0, nameComFailFixture.SelectAndPositionCalls);
        nameComFailFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 1);

        // SelectAndPositionItems throws COMException -> submitted PIDLs were live during call and freed afterward.
        Exception positionError = Marshal.GetExceptionForHR(unchecked((int)0x800706BA))!;
        var positionFailFixture = new ShellFixture(
        [
            new FixtureItem(@"C:\Desktop\A.txt"),
            new FixtureItem(@"C:\Desktop\B.txt"),
        ],
        selectAndPositionError: positionError);
        using (ShellDesktopIcons icons = positionFailFixture.CreateDesktopIcons(monitors))
        {
            DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() =>
                icons.SetPositions([
                    new(@"C:\Desktop\A.txt", "A", 10, 20),
                    new(@"C:\Desktop\B.txt", "B", 30, 40),
                ]));
            Assert.Same(positionError, thrown.InnerException);
        }

        Assert.Equal(1, positionFailFixture.SelectAndPositionCalls);
        Assert.True(positionFailFixture.AllSubmittedPidlsWereLiveDuringCall);
        positionFailFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 2);

        // Empty parsing name during enumeration -> throws DesktopUnavailableException before moving any icon.
        var emptyNameFixture = new ShellFixture(
        [
            new FixtureItem(@"C:\Desktop\A.txt"),
            new FixtureItem(string.Empty),
        ]);
        using (ShellDesktopIcons icons = emptyNameFixture.CreateDesktopIcons(monitors))
        {
            Assert.Throws<DesktopUnavailableException>(() =>
                icons.SetPositions([new(@"C:\Desktop\A.txt", "A", 10, 20)]));
        }

        Assert.Equal(0, emptyNameFixture.SelectAndPositionCalls);
        emptyNameFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 2);
    }

    [Fact]
    public void RestoreWithCoordinateOverflowingNativePointPreservesExistingUndoSnapshotAndMovesNothing()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Berth.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            BerthPaths paths = BerthPaths.ForRoot(tempRoot);
            MonitorInfo[] arrangement =
            [
                new(-1920, 0, 1920, 1080, 100, Primary: false),
                new(0, 0, 2560, 1440, 100, Primary: true),
            ];
            string key = ArrangementKey.From(arrangement);
            var monitors = new FixedMonitors(arrangement);
            var fixture = new ShellFixture([new FixtureItem(@"C:\Desktop\Alpha.txt")]);

            // Seed a saved layout whose matched icon overflows signed 32-bit view X (int.MaxValue - (-1920)).
            var store = new LayoutStore(paths);
            var layout = new Layout(
                Layout.CurrentSchemaVersion,
                key,
                "Docked",
                DateTimeOffset.UtcNow,
                [.. arrangement],
                [new IconPosition(@"C:\Desktop\Alpha.txt", "Alpha", int.MaxValue, 100)],
                Previous: null);
            store.Save(layout, new LayoutSnapshot(key, null, null));

            // Seed a pre-existing undo.json that must remain untouched when Restore refuses the overflow.
            var existingUndo = new UndoSnapshot(
                UndoSnapshot.CurrentSchemaVersion,
                key,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                [new IconPosition(@"C:\Desktop\Alpha.txt", "Alpha", 42, 84)]);
            BerthPaths.EnsureFolderFor(paths.UndoFile);
            JsonFile.Write(paths.UndoFile, existingUndo, BerthJson.Default.UndoSnapshot);
            string undoBefore = File.ReadAllText(paths.UndoFile);

            using var icons = fixture.CreateDesktopIcons(monitors);
            var opLock = new OperationLock(paths, TimeSpan.FromSeconds(5));
            var log = new Log(paths, TimeProvider.System);
            var service = new LayoutService(icons, monitors, store, paths, opLock, log, TimeProvider.System);

            OperationResult result = service.Restore();

            Assert.Equal(OperationKind.Failed, result.Kind);
            Assert.Equal(FailureKind.Unexpected, result.Failure);
            Assert.Equal(0, fixture.SelectAndPositionCalls);
            Assert.Equal(undoBefore, File.ReadAllText(paths.UndoFile));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void RestoreWithMidOperationMonitorOriginChangePreservesExistingUndoSnapshotAndMovesNothing()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Berth.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            BerthPaths paths = BerthPaths.ForRoot(tempRoot);
            MonitorInfo[] initialArrangement = [new(0, 0, 2560, 1440, 100, Primary: true)];
            MonitorInfo[] extendedArrangement =
            [
                new(-1920, 0, 1920, 1080, 100, Primary: false),
                new(0, 0, 2560, 1440, 100, Primary: true),
            ];
            string key = ArrangementKey.From(initialArrangement);
            // LayoutService.RestoreOnce reads monitors 5 times before SetPositions (Capture before + ReadIcons + Capture after + StillThe x2);
            // the 6th read happens inside SetPositionsOnView, simulating a left monitor connecting right after undo.json moved.
            int monitorReads = 0;
            var dynamicMonitors = new DynamicMonitors(() => ++monitorReads <= 5 ? initialArrangement : extendedArrangement);
            var fixture = new ShellFixture([new FixtureItem(@"C:\Desktop\Alpha.txt")]);

            var store = new LayoutStore(paths);
            var layout = new Layout(
                Layout.CurrentSchemaVersion,
                key,
                "Single",
                DateTimeOffset.UtcNow,
                [.. initialArrangement],
                [new IconPosition(@"C:\Desktop\Alpha.txt", "Alpha", int.MaxValue, 100)],
                Previous: null);
            store.Save(layout, new LayoutSnapshot(key, null, null));

            var existingUndo = new UndoSnapshot(
                UndoSnapshot.CurrentSchemaVersion,
                key,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                [new IconPosition(@"C:\Desktop\Alpha.txt", "Alpha", 42, 84)]);
            BerthPaths.EnsureFolderFor(paths.UndoFile);
            JsonFile.Write(paths.UndoFile, existingUndo, BerthJson.Default.UndoSnapshot);
            byte[] undoBefore = File.ReadAllBytes(paths.UndoFile);

            using var icons = fixture.CreateDesktopIcons(dynamicMonitors);
            var opLock = new OperationLock(paths, TimeSpan.FromSeconds(5));
            var log = new Log(paths, TimeProvider.System);
            var service = new LayoutService(icons, dynamicMonitors, store, paths, opLock, log, TimeProvider.System);

            OperationResult result = service.Restore();

            Assert.Equal(6, monitorReads);
            Assert.Equal(OperationKind.Failed, result.Kind);
            Assert.Equal(FailureKind.Unexpected, result.Failure);
            Assert.Equal(0, fixture.SelectAndPositionCalls);
            Assert.Equal(undoBefore, File.ReadAllBytes(paths.UndoFile));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class DynamicMonitors(Func<IReadOnlyList<MonitorInfo>> read) : IMonitors
    {
        public IReadOnlyList<MonitorInfo> Read() => read();
    }

    private sealed class FixedMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitors
    {
        public IReadOnlyList<MonitorInfo> Read() => monitors;
    }

    private sealed class FixtureItem(string parsingName, Exception? DisplayNameError = null)
    {
        public string ParsingName { get; } = parsingName;
        public Exception? GetDisplayNameError { get; } = DisplayNameError;
    }

    private sealed class ShellFixture(
        FixtureItem[] items,
        Exception? itemsError = null,
        int? nextErrorHrAfterItems = null,
        nint sentinelOnZeroOrError = 0,
        Action<nint>? onFreeSpy = null,
        Exception? selectAndPositionError = null)
    {
        private readonly FixtureItem[] _items = items;
        private readonly Exception? _itemsError = itemsError;
        private readonly int? _nextErrorHrAfterItems = nextErrorHrAfterItems;
        private readonly nint _sentinelOnZeroOrError = sentinelOnZeroOrError;
        private readonly Action<nint>? _onFreeSpy = onFreeSpy;
        private readonly Exception? _selectAndPositionError = selectAndPositionError;
        private readonly Dictionary<nint, FixtureItem> _activePidls = [];
        private readonly Dictionary<object, int> _comReleaseCounts = new(ReferenceEqualityComparer.Instance);

        public List<nint> AllocatedPidlOrder { get; } = [];
        public int AllocationEvents { get; private set; }
        public int FreeEvents { get; private set; }
        public int OutputStringsFreed { get; private set; }
        public int SelectAndPositionCalls { get; private set; }
        public uint LastPositionFlags { get; private set; }
        public List<(nint Pidl, NativePoint Point)> LastPositionedItems { get; } = [];
        public bool AllSubmittedPidlsWereLiveDuringCall { get; private set; }

        public ShellDesktopIcons CreateDesktopIcons(IMonitors monitors) =>
            new(
                openView: () =>
                {
                    var view = new FakeFolderView(this);
                    TrackComObject(view);
                    return DesktopView.Open(
                        () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(view))),
                        RecordComRelease);
                },
                monitors: monitors,
                freeCoTaskMem: ptr =>
                {
                    _onFreeSpy?.Invoke(ptr);
                    if (ptr == _sentinelOnZeroOrError && _sentinelOnZeroOrError != nint.Zero)
                    {
                        return;
                    }

                    if (_activePidls.Remove(ptr))
                    {
                        FreeEvents++;
                    }
                    else
                    {
                        OutputStringsFreed++;
                    }

                    Marshal.FreeCoTaskMem(ptr);
                },
                releaseComObject: RecordComRelease);

        public void AssertAllResourcesReleased(int expectedOutputStringsFreed)
        {
            Assert.Empty(_activePidls);
            Assert.Equal(AllocationEvents, FreeEvents);
            Assert.Equal(expectedOutputStringsFreed, OutputStringsFreed);
            Assert.NotEmpty(_comReleaseCounts);
            foreach (KeyValuePair<object, int> entry in _comReleaseCounts)
            {
                Assert.Equal(1, entry.Value);
            }
        }

        private void TrackComObject(object comObj) => _comReleaseCounts[comObj] = 0;

        private void RecordComRelease(object comObj)
        {
            if (_comReleaseCounts.TryGetValue(comObj, out int count))
            {
                _comReleaseCounts[comObj] = count + 1;
            }
        }

        private nint AllocatePidl(FixtureItem item)
        {
            byte[] asciiBytes = Encoding.ASCII.GetBytes(item.ParsingName);
            nint ptr = Marshal.AllocCoTaskMem(4 + asciiBytes.Length + 1);
            Marshal.WriteInt32(ptr, 0, asciiBytes.Length);
            Marshal.Copy(asciiBytes, 0, ptr + 4, asciiBytes.Length);
            Marshal.WriteByte(ptr, 4 + asciiBytes.Length, 0);
            _activePidls[ptr] = item;
            AllocatedPidlOrder.Add(ptr);
            AllocationEvents++;
            return ptr;
        }

        private sealed class FakeFolderView(ShellFixture owner) : IShellView, IFolderView
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
            public void GetCurrentViewMode(out uint pViewMode) => pViewMode = 0;
            public void SetCurrentViewMode(uint viewMode) { }

            public void GetFolder(in Guid riid, out object ppv)
            {
                Assert.Equal(typeof(IShellFolder).GUID, riid);
                var folder = new FakeShellFolder(owner);
                owner.TrackComObject(folder);
                ppv = folder;
            }

            public void Item(int iItemIndex, out nint ppidl) => ppidl = nint.Zero;
            public void ItemCount(uint uFlags, out int pcItems) => pcItems = owner._items.Length;

            public void Items(uint uFlags, in Guid riid, out object ppv)
            {
                Assert.Equal(ShellConstants.SVGIO_ALLVIEW, uFlags);
                Assert.Equal(typeof(IEnumIDList).GUID, riid);
                if (owner._itemsError is not null)
                {
                    throw owner._itemsError;
                }

                var enumerator = new FakeEnumIdList(owner);
                owner.TrackComObject(enumerator);
                ppv = enumerator;
            }

            public void GetSelectionMarkedItem(out int piItem) => piItem = 0;
            public void GetFocusedItem(out int piItem) => piItem = 0;
            public void GetItemPosition(nint pidl, out NativePoint ppt) => ppt = default;
            public void GetSpacing(ref NativePoint ppt) { }
            public void GetDefaultSpacing(out NativePoint ppt) => ppt = default;
            public int GetAutoArrange() => ShellConstants.S_FALSE;
            public void SelectItem(int iItem, uint dwFlags) { }

            public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags)
            {
                owner.SelectAndPositionCalls++;
                owner.LastPositionFlags = dwFlags;
                owner.LastPositionedItems.Clear();
                Assert.NotNull(apt);
                Assert.Equal((int)cidl, apidl.Length);
                Assert.Equal((int)cidl, apt!.Length);

                bool allLive = true;
                for (int i = 0; i < (int)cidl; i++)
                {
                    if (!owner._activePidls.ContainsKey(apidl[i]))
                    {
                        allLive = false;
                    }

                    owner.LastPositionedItems.Add((apidl[i], apt[i]));
                }

                owner.AllSubmittedPidlsWereLiveDuringCall = allLive;
                if (owner._selectAndPositionError is not null)
                {
                    throw owner._selectAndPositionError;
                }
            }
        }

        private sealed class FakeShellFolder(ShellFixture owner) : IShellFolder
        {
            public void ParseDisplayName(nint hwnd, nint pbc, string pszDisplayName, nint pchEaten, out nint ppidl, nint pdwAttributes) => ppidl = nint.Zero;
            public int EnumObjects(nint hwnd, uint grfFlags, out IEnumIDList? ppenumIDList) { ppenumIDList = null; return ShellConstants.S_FALSE; }
            public void BindToObject(nint pidl, nint pbc, in Guid riid, out object ppv) => ppv = new object();
            public void BindToStorage(nint pidl, nint pbc, in Guid riid, out object ppv) => ppv = new object();
            public int CompareIDs(nint lParam, nint pidl1, nint pidl2) => 0;
            public void CreateViewObject(nint hwndOwner, in Guid riid, out object ppv) => ppv = new object();
            public void GetAttributesOf(uint cidl, nint[]? apidl, ref uint rgfInOut) { }
            public void GetUIObjectOf(nint hwndOwner, uint cidl, nint[] apidl, in Guid riid, nint rgfReserved, out object ppv) => ppv = new object();

            public void GetDisplayNameOf(nint pidl, uint uFlags, out StrRet pName)
            {
                Assert.True(
                    uFlags is ShellConstants.SHGDN_FORPARSING or ShellConstants.SHGDN_NORMAL,
                    $"Unexpected SHGDN flags: 0x{uFlags:X}.");
                FixtureItem item = owner._activePidls[pidl];
                if (item.GetDisplayNameError is not null)
                {
                    throw item.GetDisplayNameError;
                }

                pName = new StrRet
                {
                    uType = StrRetWstr,
                    pOleStr = Marshal.StringToCoTaskMemUni(item.ParsingName),
                };
            }

            public void SetNameOf(nint hwnd, nint pidl, string pszName, uint uFlags, out nint ppidlOut) => ppidlOut = nint.Zero;
        }

        private sealed class FakeEnumIdList(ShellFixture owner) : IEnumIDList
        {
            private int _index;

            public int Next(uint celt, nint[] rgelt, out uint pceltFetched)
            {
                if (_index >= owner._items.Length)
                {
                    rgelt[0] = owner._sentinelOnZeroOrError;
                    pceltFetched = 0;
                    return owner._nextErrorHrAfterItems ?? ShellConstants.S_FALSE;
                }

                FixtureItem item = owner._items[_index++];
                rgelt[0] = owner.AllocatePidl(item);
                pceltFetched = 1;
                return ShellConstants.S_OK;
            }

            public int Skip(uint celt) => ShellConstants.S_FALSE;
            public void Reset() => _index = 0;
            public void Clone(out IEnumIDList ppenum) => ppenum = this;
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
}
