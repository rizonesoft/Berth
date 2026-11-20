using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Desktop.Interop;
using Berth.Tests.Fakes;

namespace Berth.Tests.Desktop;

public sealed class ScopedDesktopIconsTests
{
    private const string Prefix = "berth-test-probe-";

    [Fact]
    public void ShellSeamReadAndWriteNeverReadPositionParsingNameOrMoveNonMatchingOwnerIcons()
    {
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);
        var fixture = new ScopedShellFixture(
        [
            new ShellItem(@"C:\Desktop\Owner Notes.txt", "Owner Notes", 10, 20),
            new ShellItem(@"C:\Desktop\Middle.txt", "my berth-test-probe-middle", 30, 40),
            new ShellItem(@"C:\Desktop\Probe.txt", "BERTH-TEST-PROBE-01", 50, 60),
        ]);

        using ShellDesktopIcons shell = fixture.CreateShell(monitors);
        var scoped = new ScopedDesktopIcons(shell, Prefix);

        // ReadIcons: only the matching probe receives GetItemPosition and SHGDN_FORPARSING.
        IReadOnlyList<IconPosition> read = scoped.ReadIcons();
        Assert.Equal(1, fixture.ViewOpens);
        IconPosition only = Assert.Single(read);
        Assert.Equal(@"C:\Desktop\Probe.txt", only.Id);
        Assert.Equal("BERTH-TEST-PROBE-01", only.Name);
        Assert.Equal(50, only.X);
        Assert.Equal(60, only.Y);
        Assert.Equal([@"C:\Desktop\Probe.txt"], fixture.PositionReadIds);
        Assert.Equal([@"C:\Desktop\Probe.txt"], fixture.ParsingNameReadIds);
        fixture.AssertAllResourcesReleased();

        fixture.ClearOperationLogs();

        // 2-arg SetPositions: single view open, nonmatching owner icons never get SHGDN_FORPARSING or moves,
        // beforeMove runs strictly before SelectAndPositionItems.
        var order = new List<string>();
        fixture.OnSelectAndPosition = () => order.Add("move");
        scoped.SetPositions(
        [
            new(@"C:\DESKTOP\PROBE.TXT", "StaleNameOutsidePrefix", 300, 400),
            new(@"C:\Desktop\Owner Notes.txt", "berth-test-probe-spoof", 999, 999),
            new(@"C:\Desktop\Middle.txt", "berth-test-probe-middle", 888, 888),
            new(@"C:\Desktop\Missing.txt", "berth-test-probe-missing", 777, 777),
        ],
        () => order.Add("beforeMove"));

        Assert.Equal(1, fixture.ViewOpens);
        Assert.Equal(["beforeMove", "move"], order);
        Assert.Empty(fixture.PositionReadIds);
        Assert.Equal([@"C:\Desktop\Probe.txt"], fixture.ParsingNameReadIds);
        Assert.Equal([(@"C:\Desktop\Probe.txt", 300, 400)], fixture.MovedItems);
        fixture.AssertAllResourcesReleased();

        // Read back through ScopedDesktopIcons confirms probe moved and owner icons were untouched.
        IconPosition moved = Assert.Single(scoped.ReadIcons());
        Assert.Equal((300, 400), (moved.X, moved.Y));
        Assert.Equal((10, 20), fixture.GetPosition(@"C:\Desktop\Owner Notes.txt"));
        Assert.Equal((30, 40), fixture.GetPosition(@"C:\Desktop\Middle.txt"));
        fixture.AssertAllResourcesReleased();

        fixture.ClearOperationLogs();
        fixture.OnSelectAndPosition = null;

        // 1-arg SetPositions: single view open, only matching probe moved.
        scoped.SetPositions(
        [
            new(@"c:\desktop\probe.txt", "AnotherStaleName", 510, 620),
            new(@"C:\Desktop\Owner Notes.txt", "berth-test-probe-spoof", 111, 222),
        ]);

        Assert.Equal(1, fixture.ViewOpens);
        Assert.Equal([@"C:\Desktop\Probe.txt"], fixture.ParsingNameReadIds);
        Assert.Equal([(@"C:\Desktop\Probe.txt", 510, 620)], fixture.MovedItems);
        Assert.Equal((10, 20), fixture.GetPosition(@"C:\Desktop\Owner Notes.txt"));
        fixture.AssertAllResourcesReleased();

        // Fully filtered batch on shell path still invokes beforeMove once and moves nothing.
        fixture.ClearOperationLogs();
        int beforeMoveCalls = 0;
        scoped.SetPositions(
            [new(@"C:\Desktop\Owner Notes.txt", "berth-test-probe-spoof", 12, 34)],
            () => beforeMoveCalls++);
        Assert.Equal(1, fixture.ViewOpens);
        Assert.Equal(1, beforeMoveCalls);
        Assert.Empty(fixture.MovedItems);
        Assert.Equal([@"C:\Desktop\Probe.txt"], fixture.ParsingNameReadIds);
        fixture.AssertAllResourcesReleased();
    }

    [Fact]
    public void SetPositionsBothOverloadsMoveCaseVariantStaleNameProbeAndDropSpoofedOrOutOfScopeIcons()
    {
        var fake = new FakeDesktopIcons(
            new(@"C:\Desktop\Owner.txt", "Owner Notes", 10, 20),
            new(@"C:\Desktop\Middle.txt", "my berth-test-probe-1", 30, 40),
            new(@"C:\Desktop\Probe.txt", "BERTH-TEST-PROBE-1", 50, 60));
        var scoped = new ScopedDesktopIcons(fake, Prefix);

        // ReadIcons returns only the matching prefix icon (ignoring case, rejecting middle substring).
        IconPosition initial = Assert.Single(scoped.ReadIcons());
        Assert.Equal(@"C:\Desktop\Probe.txt", initial.Id);

        // 1-arg overload: mixed batch with case-variant Id, stale Name, spoofed owner/middle, and unknown Id.
        scoped.SetPositions(
        [
            new(@"C:\DESKTOP\PROBE.TXT", "CompletelyDifferentName", 200, 250),
            new(@"C:\Desktop\Owner.txt", "berth-test-probe-spoof", 900, 910),
            new(@"C:\Desktop\Middle.txt", "berth-test-probe-middle", 800, 810),
            new(@"C:\Desktop\Missing.txt", "berth-test-probe-missing", 700, 710),
        ]);

        IReadOnlyList<IconPosition> firstCall = Assert.Single(fake.Calls);
        IconPosition forwarded1 = Assert.Single(firstCall);
        Assert.Equal(@"C:\DESKTOP\PROBE.TXT", forwarded1.Id);
        Assert.Equal(200, forwarded1.X);
        Assert.Equal(250, forwarded1.Y);

        IconPosition afterFirst = Assert.Single(scoped.ReadIcons());
        Assert.Equal((200, 250), (afterFirst.X, afterFirst.Y));

        // 2-arg overload: mixed batch forwards only the live in-scope Id with exact requested coordinates.
        int callbackCount = 0;
        scoped.SetPositions(
        [
            new(@"c:\desktop\probe.txt", "AnotherStaleName", 310, 420),
            new(@"C:\Desktop\Owner.txt", "berth-test-probe-spoof", 950, 960),
            new(@"C:\Desktop\Middle.txt", "berth-test-probe-middle", 850, 860),
            new(@"C:\Desktop\Missing.txt", "berth-test-probe-missing", 750, 760),
        ],
        () => callbackCount++);

        Assert.Equal(1, callbackCount);
        Assert.Equal(2, fake.Calls.Count);
        IconPosition forwarded2 = Assert.Single(fake.Calls[1]);
        Assert.Equal(@"c:\desktop\probe.txt", forwarded2.Id);
        Assert.Equal(310, forwarded2.X);
        Assert.Equal(420, forwarded2.Y);

        IconPosition afterSecond = Assert.Single(scoped.ReadIcons());
        Assert.Equal((310, 420), (afterSecond.X, afterSecond.Y));

        // Out-of-scope icons on the inner desktop never moved.
        IReadOnlyList<IconPosition> allInner = ((IDesktopIcons)fake).ReadIcons();
        Assert.Equal((10, 20), (allInner[0].X, allInner[0].Y));
        Assert.Equal((30, 40), (allInner[1].X, allInner[1].Y));
    }

    [Fact]
    public void SetPositionsBeforeMoveEnforcesOrderingFailureAndFullyFilteredBatchContract()
    {
        var fake = new FakeDesktopIcons(
            new(@"C:\Desktop\Owner.txt", "Owner Notes", 10, 20),
            new(@"C:\Desktop\Probe.txt", "berth-test-probe-1", 50, 60));
        var scoped = new ScopedDesktopIcons(fake, Prefix);

        // 1) Unavailable desktop -> 0 callbacks.
        fake.Unavailable = true;
        int unavailableCallbacks = 0;
        Assert.Throws<DesktopUnavailableException>(() =>
            scoped.SetPositions([new(@"C:\Desktop\Probe.txt", "berth-test-probe-1", 100, 100)], () => unavailableCallbacks++));
        Assert.Equal(0, unavailableCallbacks);
        Assert.Throws<DesktopUnavailableException>(() =>
            scoped.SetPositions([], () => unavailableCallbacks++));
        Assert.Equal(0, unavailableCallbacks);
        fake.Unavailable = false;

        // 2) Reachable success -> exactly 1 callback strictly before move.
        var order = new List<string>();
        fake.BeforeSet = () => order.Add("move");
        scoped.SetPositions(
            [new(@"C:\Desktop\Probe.txt", "berth-test-probe-1", 120, 140)],
            () => order.Add("beforeMove"));
        Assert.Equal(["beforeMove", "move"], order);
        Assert.Equal((120, 140), (scoped.ReadIcons()[0].X, scoped.ReadIcons()[0].Y));
        fake.BeforeSet = null;

        // 3) Callback throws -> exception propagates and zero movement occurs.
        int callsBeforeThrow = fake.Calls.Count;
        Assert.Throws<InvalidOperationException>(() =>
            scoped.SetPositions(
                [new(@"C:\Desktop\Probe.txt", "berth-test-probe-1", 999, 999)],
                () => throw new InvalidOperationException("Undo commit failed.")));
        Assert.Equal(callsBeforeThrow, fake.Calls.Count);
        Assert.Equal((120, 140), (scoped.ReadIcons()[0].X, scoped.ReadIcons()[0].Y));

        // 4) Fully filtered non-empty batch -> beforeMove still runs once and forwards an empty batch.
        int filteredCallbacks = 0;
        scoped.SetPositions(
            [new(@"C:\Desktop\Owner.txt", "berth-test-probe-spoof", 700, 800)],
            () => filteredCallbacks++);
        Assert.Equal(1, filteredCallbacks);
        Assert.Empty(fake.Calls[^1]);
        Assert.Equal((120, 140), (scoped.ReadIcons()[0].X, scoped.ReadIcons()[0].Y));

        // 5) Empty batch -> beforeMove runs once.
        int emptyCallbacks = 0;
        scoped.SetPositions([], () => emptyCallbacks++);
        Assert.Equal(1, emptyCallbacks);
        Assert.Empty(fake.Calls[^1]);
    }

    [Fact]
    public void SetPositionsRefreshesLiveScopeWhenProbeIsRemovedOrRenamedOutsidePrefix()
    {
        var desktop = new MutableDesktopIcons(
            new(@"C:\Desktop\Owner.txt", "Owner Notes", 10, 20),
            new(@"C:\Desktop\ProbeA.txt", "berth-test-probe-a", 50, 60),
            new(@"C:\Desktop\ProbeB.txt", "berth-test-probe-b", 70, 80));
        var scoped = new ScopedDesktopIcons(desktop, Prefix);

        // Initially both probes are in scope.
        Assert.Equal(2, scoped.ReadIcons().Count);

        // Remove ProbeA and rename ProbeB outside the prefix before calling either SetPositions overload.
        desktop.Remove(@"C:\Desktop\ProbeA.txt");
        desktop.Rename(@"C:\Desktop\ProbeB.txt", "Owner Renamed");

        // 1-arg overload forwards neither stale ID.
        scoped.SetPositions(
        [
            new(@"C:\Desktop\ProbeA.txt", "berth-test-probe-a", 300, 310),
            new(@"C:\Desktop\ProbeB.txt", "berth-test-probe-b", 400, 410),
        ]);
        Assert.Empty(Assert.Single(desktop.ForwardedCalls));

        // 2-arg overload forwards neither stale ID and still runs beforeMove once.
        int beforeMoveCalls = 0;
        scoped.SetPositions(
        [
            new(@"C:\Desktop\ProbeA.txt", "berth-test-probe-a", 300, 310),
            new(@"C:\Desktop\ProbeB.txt", "berth-test-probe-b", 400, 410),
        ],
        () => beforeMoveCalls++);
        Assert.Equal(1, beforeMoveCalls);
        Assert.Equal(2, desktop.ForwardedCalls.Count);
        Assert.Empty(desktop.ForwardedCalls[1]);

        // Renamed ProbeB stayed at its original position.
        Assert.Equal((70, 80), desktop.GetPosition(@"C:\Desktop\ProbeB.txt"));
    }

    [Fact]
    public void ArgumentGuardsUnavailablePropagationAutoArrangeAndTryTurnAutoArrangeOff()
    {
        var spy = new SpyDesktopIcons();

        // Constructor guards before any inner call.
        Assert.Throws<ArgumentNullException>(() => new ScopedDesktopIcons(null!, Prefix));
        Assert.Throws<ArgumentNullException>(() => new ScopedDesktopIcons(spy, null!));
        Assert.Throws<ArgumentException>(() => new ScopedDesktopIcons(spy, string.Empty));
        Assert.Throws<ArgumentException>(() => new ScopedDesktopIcons(spy, "   "));
        Assert.Equal(0, spy.TotalCalls);

        var scoped = new ScopedDesktopIcons(spy, Prefix);

        // Method guards before any inner call.
        Assert.Throws<ArgumentNullException>(() => scoped.SetPositions(null!));
        Assert.Throws<ArgumentNullException>(() => scoped.SetPositions(null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => scoped.SetPositions([], null!));
        Assert.Equal(0, spy.TotalCalls);

        // AutoArrange toggles pass through without caching.
        spy.AutoArrangeState = false;
        Assert.False(scoped.AutoArrange);
        spy.AutoArrangeState = true;
        Assert.True(scoped.AutoArrange);
        spy.AutoArrangeState = false;
        Assert.False(scoped.AutoArrange);

        // TryTurnAutoArrangeOff returns false and never touches inner even when AutoArrange is on.
        spy.AutoArrangeState = true;
        int callsBefore = spy.TotalCalls;
        Assert.False(scoped.TryTurnAutoArrangeOff());
        Assert.Equal(callsBefore, spy.TotalCalls);
        Assert.Equal(0, spy.TryTurnAutoArrangeOffCalls);
        Assert.True(scoped.AutoArrange);

        // Unavailable desktop: ReadIcons, both SetPositions overloads, and AutoArrange throw DesktopUnavailableException,
        // while TryTurnAutoArrangeOff still returns false without touching inner.
        spy.Unavailable = true;
        Assert.Throws<DesktopUnavailableException>(() => scoped.ReadIcons());
        Assert.Throws<DesktopUnavailableException>(() => scoped.SetPositions([new(@"C:\Desktop\P.txt", "berth-test-probe-1", 1, 2)]));
        Assert.Throws<DesktopUnavailableException>(() => scoped.SetPositions([new(@"C:\Desktop\P.txt", "berth-test-probe-1", 1, 2)], () => { }));
        Assert.Throws<DesktopUnavailableException>(() => _ = scoped.AutoArrange);

        int callsBeforeUnavailableOff = spy.TotalCalls;
        Assert.False(scoped.TryTurnAutoArrangeOff());
        Assert.Equal(callsBeforeUnavailableOff, spy.TotalCalls);
        Assert.Equal(0, spy.TryTurnAutoArrangeOffCalls);
    }

    private sealed record ShellItem(string Id, string DisplayName, int X, int Y)
    {
        public int CurrentX { get; set; } = X;
        public int CurrentY { get; set; } = Y;
    }

    private sealed class ScopedShellFixture
    {
        private const uint StrRetWstr = 0u;
        private readonly List<ShellItem> _items;
        private readonly Dictionary<nint, ShellItem> _pidlToItem = [];
        private readonly HashSet<nint> _livePidls = [];
        private readonly List<object> _allocatedComObjects = [];
        private readonly HashSet<object> _releasedComObjects = [];
        private int _nextPidl = 0x1000;

        public ScopedShellFixture(IReadOnlyList<ShellItem> items)
        {
            _items = [.. items];
        }

        public int ViewOpens { get; private set; }
        public List<string> PositionReadIds { get; } = [];
        public List<string> ParsingNameReadIds { get; } = [];
        public List<(string Id, int X, int Y)> MovedItems { get; } = [];
        public Action? OnSelectAndPosition { get; set; }

        public ShellDesktopIcons CreateShell(IMonitors monitors) =>
            new(
                openView: OpenView,
                monitors: monitors,
                freeCoTaskMem: FreeCoTaskMem,
                releaseComObject: ReleaseComObject);

        public void ClearOperationLogs()
        {
            ViewOpens = 0;
            PositionReadIds.Clear();
            ParsingNameReadIds.Clear();
            MovedItems.Clear();
        }

        public (int X, int Y) GetPosition(string id)
        {
            ShellItem item = _items.Single(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            return (item.CurrentX, item.CurrentY);
        }

        public void AssertAllResourcesReleased()
        {
            Assert.Empty(_livePidls);
            foreach (object obj in _allocatedComObjects)
            {
                Assert.Contains(obj, _releasedComObjects);
            }
        }

        private DesktopView OpenView()
        {
            ViewOpens++;
            var view = new FakeFolderView(this);
            _allocatedComObjects.Add(view);
            return DesktopView.Open(
                () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(view))),
                ReleaseComObject);
        }

        private nint AllocatePidl(ShellItem item)
        {
            nint pidl = new(_nextPidl);
            _nextPidl += 0x10;
            _pidlToItem[pidl] = item;
            Assert.True(_livePidls.Add(pidl));
            return pidl;
        }

        private void FreeCoTaskMem(nint ptr)
        {
            if (_pidlToItem.ContainsKey(ptr))
            {
                Assert.True(_livePidls.Remove(ptr), $"PIDL 0x{ptr:X} freed more than once or never allocated.");
                return;
            }

            Marshal.FreeCoTaskMem(ptr);
        }

        private void ReleaseComObject(object obj)
        {
            if (_allocatedComObjects.Contains(obj))
            {
                Assert.True(_releasedComObjects.Add(obj), "COM object released more than once.");
            }
        }

        private sealed class FakeFolderView(ScopedShellFixture fixture) : IShellView, IFolderView
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
            public void GetFolder(in Guid riid, out object ppv)
            {
                var folder = new FakeShellFolder(fixture);
                fixture._allocatedComObjects.Add(folder);
                ppv = folder;
            }

            public void Item(int iItemIndex, out nint ppidl) => ppidl = nint.Zero;
            public void ItemCount(uint uFlags, out int pcItems) => pcItems = fixture._items.Count;

            public void Items(uint uFlags, in Guid riid, out object ppv)
            {
                var enumerator = new FakeEnumIdList(fixture);
                fixture._allocatedComObjects.Add(enumerator);
                ppv = enumerator;
            }

            public void GetSelectionMarkedItem(out int piItem) => piItem = -1;
            public void GetFocusedItem(out int piItem) => piItem = -1;

            public void GetItemPosition(nint pidl, out NativePoint ppt)
            {
                Assert.Contains(pidl, fixture._livePidls);
                ShellItem item = fixture._pidlToItem[pidl];
                fixture.PositionReadIds.Add(item.Id);
                ppt = new NativePoint { X = item.CurrentX, Y = item.CurrentY };
            }

            public void GetSpacing(ref NativePoint ppt) { }
            public void GetDefaultSpacing(out NativePoint ppt) => ppt = default;
            public int GetAutoArrange() => ShellConstants.S_FALSE;

            public void SelectItem(int iItem, uint dwFlags) { }

            public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags)
            {
                fixture.OnSelectAndPosition?.Invoke();
                Assert.NotNull(apt);
                for (int i = 0; i < cidl; i++)
                {
                    Assert.Contains(apidl[i], fixture._livePidls);
                    ShellItem item = fixture._pidlToItem[apidl[i]];
                    item.CurrentX = apt[i].X;
                    item.CurrentY = apt[i].Y;
                    fixture.MovedItems.Add((item.Id, apt[i].X, apt[i].Y));
                }
            }
        }

        private sealed class FakeShellFolder(ScopedShellFixture fixture) : IShellFolder
        {
            public void ParseDisplayName(nint hwnd, nint pbc, string pszDisplayName, nint pchEaten, out nint ppidl, nint pdwAttributes) => ppidl = nint.Zero;
            public int EnumObjects(nint hwnd, uint grfFlags, out IEnumIDList? ppenumIDList)
            {
                ppenumIDList = new FakeEnumIdList(fixture);
                return ShellConstants.S_OK;
            }
            public void BindToObject(nint pidl, nint pbc, in Guid riid, out object ppv) => ppv = this;
            public void BindToStorage(nint pidl, nint pbc, in Guid riid, out object ppv) => ppv = this;
            public int CompareIDs(nint lParam, nint pidl1, nint pidl2) => 0;
            public void CreateViewObject(nint hwndOwner, in Guid riid, out object ppv) => ppv = this;
            public void GetAttributesOf(uint cidl, nint[]? apidl, ref uint rgfInOut) { }
            public void GetUIObjectOf(nint hwndOwner, uint cidl, nint[] apidl, in Guid riid, nint rgfReserved, out object ppv) => ppv = this;

            public void GetDisplayNameOf(nint pidl, uint uFlags, out StrRet pName)
            {
                Assert.Contains(pidl, fixture._livePidls);
                ShellItem item = fixture._pidlToItem[pidl];
                string text;
                if (uFlags == ShellConstants.SHGDN_FORPARSING)
                {
                    fixture.ParsingNameReadIds.Add(item.Id);
                    text = item.Id;
                }
                else
                {
                    text = item.DisplayName;
                }

                pName = new StrRet
                {
                    uType = StrRetWstr,
                    pOleStr = Marshal.StringToCoTaskMemUni(text),
                };
            }

            public void SetNameOf(nint hwnd, nint pidl, string pszName, uint uFlags, out nint ppidlOut) => ppidlOut = nint.Zero;
        }

        private sealed class FakeEnumIdList(ScopedShellFixture fixture) : IEnumIDList
        {
            private int _index;

            public int Next(uint celt, nint[] rgelt, out uint pceltFetched)
            {
                if (_index >= fixture._items.Count)
                {
                    pceltFetched = 0;
                    return ShellConstants.S_FALSE;
                }

                rgelt[0] = fixture.AllocatePidl(fixture._items[_index++]);
                pceltFetched = 1;
                return ShellConstants.S_OK;
            }

            public int Skip(uint celt) => ShellConstants.S_OK;
            public void Reset() => _index = 0;
            public void Clone(out IEnumIDList ppenum) => ppenum = new FakeEnumIdList(fixture);
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

    private sealed class MutableDesktopIcons(params IconPosition[] initial) : IDesktopIcons
    {
        private readonly List<IconPosition> _icons = [.. initial];

        public List<IReadOnlyList<IconPosition>> ForwardedCalls { get; } = [];
        public bool AutoArrange => false;

        public void Remove(string id) =>
            _icons.RemoveAll(icon => string.Equals(icon.Id, id, StringComparison.OrdinalIgnoreCase));

        public void Rename(string id, string newName)
        {
            int idx = _icons.FindIndex(icon => string.Equals(icon.Id, id, StringComparison.OrdinalIgnoreCase));
            Assert.True(idx >= 0);
            _icons[idx] = _icons[idx] with { Name = newName };
        }

        public (int X, int Y) GetPosition(string id)
        {
            IconPosition icon = _icons.Single(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            return (icon.X, icon.Y);
        }

        public IReadOnlyList<IconPosition> ReadIcons() => [.. _icons];

        public void SetPositions(IReadOnlyList<IconPosition> icons)
        {
            ForwardedCalls.Add([.. icons]);
            Apply(icons);
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove)
        {
            beforeMove();
            ForwardedCalls.Add([.. icons]);
            Apply(icons);
        }

        public bool TryTurnAutoArrangeOff() => false;

        private void Apply(IReadOnlyList<IconPosition> icons)
        {
            foreach (IconPosition wanted in icons)
            {
                int idx = _icons.FindIndex(i => string.Equals(i.Id, wanted.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    _icons[idx] = _icons[idx] with { X = wanted.X, Y = wanted.Y };
                }
            }
        }
    }

    private sealed class SpyDesktopIcons : IDesktopIcons
    {
        public int TotalCalls { get; private set; }
        public int TryTurnAutoArrangeOffCalls { get; private set; }
        public bool AutoArrangeState { get; set; }
        public bool Unavailable { get; set; }

        public bool AutoArrange
        {
            get
            {
                TotalCalls++;
                ThrowIfUnavailable();
                return AutoArrangeState;
            }
        }

        public IReadOnlyList<IconPosition> ReadIcons()
        {
            TotalCalls++;
            ThrowIfUnavailable();
            return [];
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons)
        {
            TotalCalls++;
            ThrowIfUnavailable();
        }

        public void SetPositions(IReadOnlyList<IconPosition> icons, Action beforeMove)
        {
            TotalCalls++;
            ThrowIfUnavailable();
            beforeMove();
        }

        public bool TryTurnAutoArrangeOff()
        {
            TotalCalls++;
            TryTurnAutoArrangeOffCalls++;
            ThrowIfUnavailable();
            return true;
        }

        private void ThrowIfUnavailable()
        {
            if (Unavailable)
            {
                throw new DesktopUnavailableException("Desktop unreachable.");
            }
        }
    }

    private sealed class FixedMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitors
    {
        public IReadOnlyList<MonitorInfo> Read() => monitors;
    }
}
