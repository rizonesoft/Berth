using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Berth.Core;
using Berth.Desktop;
using Berth.Desktop.Interop;

namespace Berth.Tests.Desktop;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed class DesktopViewTests
{
    [StaFact]
    public void OpenReturnsLiveDesktopFolderViewAndSupportsItemCountAndFolderView2()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());

        using DesktopView desktop = DesktopView.Open();
        desktop.View.ItemCount(ShellConstants.SVGIO_ALLVIEW, out int count);
        Assert.True(count >= 0, $"Expected nonnegative desktop icon count, got {count}.");

        Assert.NotNull(desktop.View2);
        desktop.View2.GetCurrentFolderFlags(out _);
    }

    [StaFact]
    public void DisposingOneLiveDesktopViewHolderLeavesConcurrentHolderUsable()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        DesktopView first = DesktopView.Open();
        using DesktopView second = DesktopView.Open();
        first.Dispose();

        second.View.ItemCount(ShellConstants.SVGIO_ALLVIEW, out int count);
        Assert.True(count >= 0, $"Expected concurrent second holder to remain usable, got {count}.");
    }

    [StaFact]
    public void FiveHundredAcquisitionsAndDisposalsDoNotGrowProcessHandleCount()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using (DesktopView.Open())
        {
        }

        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        int before = process.HandleCount;

        for (int i = 0; i < 500; i++)
        {
            using DesktopView desktop = DesktopView.Open();
            desktop.View.ItemCount(ShellConstants.SVGIO_ALLVIEW, out _);
        }

        process.Refresh();
        int after = process.HandleCount;
        int delta = after - before;
        Assert.True(delta <= 50, $"Process handle count grew from {before} to {after} (delta {delta} > 50).");
    }
}

public sealed class UnitDesktopViewTests
{
    [Fact]
    public void OpenFromMtaThreadThrowsInvalidOperationExceptionBeforeInvokingFactory()
    {
        int factoryCalls = 0;
        InvalidOperationException? error = null;

        var mta = new Thread(() =>
        {
            error = Assert.Throws<InvalidOperationException>(() =>
                DesktopView.Open(() =>
                {
                    factoryCalls++;
                    return null;
                }));
        });
        mta.SetApartmentState(ApartmentState.MTA);
        mta.Start();
        mta.Join();

        Assert.NotNull(error);
        Assert.Contains("STA", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, factoryCalls);
    }

    [StaFact]
    public async Task StaFactPumpsAsyncContinuationsOnSameStaThread()
    {
        int startThreadId = Environment.CurrentManagedThreadId;
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());

        await Task.Yield();
        await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.Equal(startThreadId, Environment.CurrentManagedThreadId);
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
    }

    [Fact]
    public void StaRunAsyncPropagatesFailuresAndShutsDownDispatcherOnSuccessAndFailure()
    {
        int ok = Sta.RunAsync(async () =>
        {
            await Task.Yield();
            return 42;
        }, out Dispatcher? okDispatcher);
        Assert.Equal(42, ok);
        Assert.NotNull(okDispatcher);
        Assert.True(okDispatcher.HasShutdownFinished);

        Dispatcher? failDispatcher = null;
        InvalidOperationException boom = Assert.Throws<InvalidOperationException>(() =>
            Sta.RunAsync<int>(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom on STA");
            }, out failDispatcher));
        Assert.Equal("boom on STA", boom.Message);
        Assert.NotNull(failDispatcher);
        Assert.True(failDispatcher.HasShutdownFinished);
    }

    [StaFact]
    public void OpenPassesDocumentedDesktopArgumentsReleasesIntermediatesAndSupportsView2()
    {
        var released = new List<object>();
        var shellView = new FakeFolderView2();
        var browser = new FakeShellBrowser(shellView);
        var provider = new FakeServiceProvider(browser);
        var shellWindows = new FakeShellWindows(dispatchResult: provider);

        DesktopView view = DesktopView.Open(() => shellWindows, released.Add);

        Assert.Equal(ShellConstants.CSIDL_DESKTOP, Assert.IsType<int>(shellWindows.RecordedLocation));
        Assert.Null(shellWindows.RecordedLocationRoot);
        Assert.Equal(ShellConstants.SWC_DESKTOP, shellWindows.RecordedSwClass);
        Assert.Equal(ShellConstants.SWFO_NEEDDISPATCH, shellWindows.RecordedOptions);
        Assert.Equal(ShellConstants.SID_STopLevelBrowser, provider.RecordedServiceId);
        Assert.Equal(typeof(IShellBrowser).GUID, provider.RecordedInterfaceId);

        Assert.Equal(new object[] { shellWindows, provider, browser }, released);

        view.View.ItemCount(ShellConstants.SVGIO_ALLVIEW, out int count);
        Assert.Equal(7, count);
        Assert.NotNull(view.View2);
        view.View2.GetCurrentFolderFlags(out uint flags);
        Assert.Equal(0x12u, flags);

        view.Dispose();
        view.Dispose();
        Assert.Equal(new object[] { shellWindows, provider, browser, shellView }, released);
    }

    [StaFact]
    public void OpenSucceedsWithNullView2WhenViewImplementsOnlyIFolderView()
    {
        var released = new List<object>();
        var shellView = new FakeFolderViewOnly();
        var browser = new FakeShellBrowser(shellView);
        var provider = new FakeServiceProvider(browser);
        var shellWindows = new FakeShellWindows(dispatchResult: provider);

        using DesktopView view = DesktopView.Open(() => shellWindows, released.Add);
        Assert.NotNull(view.View);
        Assert.Null(view.View2);

        view.View.ItemCount(ShellConstants.SVGIO_ALLVIEW, out int count);
        Assert.Equal(3, count);
    }

    [StaFact]
    public void OpenReleasesEachAcquiredReferenceWhenInterfacesAliasSingleObject()
    {
        var released = new List<object>();
        var combined = new FakeCombinedShellChain();

        DesktopView view = DesktopView.Open(() => combined, released.Add);
        Assert.Equal(3, released.Count);
        Assert.All(released, item => Assert.Same(combined, item));

        view.View.ItemCount(ShellConstants.SVGIO_ALLVIEW, out int count);
        Assert.Equal(5, count);
        Assert.NotNull(view.View2);

        view.Dispose();
        view.Dispose();
        Assert.Equal(4, released.Count);
    }

    [StaFact]
    public void DisposingOneHolderOverSharedFakeViewLeavesSecondHolderUsable()
    {
        var sharedView = new RefCountedFakeFolderView();
        DesktopView CreateHolder()
        {
            var browser = new FakeShellBrowser(sharedView, onAcquired: () => sharedView.RefCount++);
            var provider = new FakeServiceProvider(browser);
            var shellWindows = new FakeShellWindows(dispatchResult: provider);
            return DesktopView.Open(
                () => shellWindows,
                instance =>
                {
                    if (ReferenceEquals(instance, sharedView))
                    {
                        sharedView.RefCount--;
                    }
                });
        }

        DesktopView first = CreateHolder();
        using DesktopView second = CreateHolder();
        Assert.Equal(2, sharedView.RefCount);

        first.Dispose();
        Assert.Equal(1, sharedView.RefCount);
        second.View.ItemCount(ShellConstants.SVGIO_ALLVIEW, out int count);
        Assert.Equal(9, count);

        second.Dispose();
        Assert.Equal(0, sharedView.RefCount);
    }

    [StaFact]
    public void FindWindowReturningSFalseThrowsDesktopUnavailableAndReleasesShellWindows()
    {
        var released = new List<object>();
        var shellWindows = new FakeShellWindows(hr: ShellConstants.S_FALSE, dispatchResult: null);

        var error = Assert.Throws<DesktopUnavailableException>(() =>
            DesktopView.Open(() => shellWindows, released.Add));

        Assert.Null(error.InnerException);
        Assert.Equal(new object[] { shellWindows }, released);
    }

    [StaFact]
    public void FindWindowReturningNegativeHResultPreservesErrorCodeAndReleasesShellWindows()
    {
        const int ENoInterface = unchecked((int)0x80004002);
        var released = new List<object>();
        var shellWindows = new FakeShellWindows(hr: ENoInterface, dispatchResult: null);

        var error = Assert.Throws<DesktopUnavailableException>(() =>
            DesktopView.Open(() => shellWindows, released.Add));

        Assert.NotNull(error.InnerException);
        Assert.Equal(ENoInterface, error.InnerException.HResult);
        Assert.Equal(new object[] { shellWindows }, released);
    }

    [StaFact]
    public void QueryServiceThrowingUnauthorizedAccessWrapsExceptionAndReleasesAcquiredObjects()
    {
        var released = new List<object>();
        var denied = new UnauthorizedAccessException("E_ACCESSDENIED from shell");
        var provider = new FakeServiceProvider(browser: null, toThrow: denied);
        var shellWindows = new FakeShellWindows(dispatchResult: provider);

        var error = Assert.Throws<DesktopUnavailableException>(() =>
            DesktopView.Open(() => shellWindows, released.Add));

        Assert.Same(denied, error.InnerException);
        Assert.Equal(new object[] { shellWindows, provider }, released);
    }

    [StaFact]
    public void QueryActiveShellViewThrowingComExceptionWrapsExceptionAndReleasesAcquiredObjects()
    {
        var released = new List<object>();
        var comError = Assert.IsType<COMException>(Marshal.GetExceptionForHR(unchecked((int)0x800706BA)));
        var browser = new FakeShellBrowser(view: null, toThrow: comError);
        var provider = new FakeServiceProvider(browser);
        var shellWindows = new FakeShellWindows(dispatchResult: provider);

        var error = Assert.Throws<DesktopUnavailableException>(() =>
            DesktopView.Open(() => shellWindows, released.Add));

        Assert.Same(comError, error.InnerException);
        Assert.Equal(new object[] { shellWindows, provider, browser }, released);
    }

    [StaFact]
    public void ViewWithoutIFolderViewThrowsDesktopUnavailableWithInvalidCastAndReleasesAllObjects()
    {
        var released = new List<object>();
        var bareShellView = new FakeShellViewWithoutFolderView();
        var browser = new FakeShellBrowser(bareShellView);
        var provider = new FakeServiceProvider(browser);
        var shellWindows = new FakeShellWindows(dispatchResult: provider);

        var error = Assert.Throws<DesktopUnavailableException>(() =>
            DesktopView.Open(() => shellWindows, released.Add));

        Assert.IsType<InvalidCastException>(error.InnerException);
        Assert.Equal(new object[] { shellWindows, provider, browser, bareShellView }, released);
    }

    [StaFact]
    public void NullActivationOrIntermediateResultsThrowDesktopUnavailableAndReleaseAcquiredObjects()
    {
        var nullFactoryReleased = new List<object>();
        var nullFactoryError = Assert.Throws<DesktopUnavailableException>(() =>
            DesktopView.Open(() => null, nullFactoryReleased.Add));
        Assert.Null(nullFactoryError.InnerException);
        Assert.Empty(nullFactoryReleased);

        var nullBrowserReleased = new List<object>();
        var nullBrowserProvider = new FakeServiceProvider(browser: null);
        var nullBrowserShellWindows = new FakeShellWindows(dispatchResult: nullBrowserProvider);
        var nullBrowserError = Assert.Throws<DesktopUnavailableException>(() =>
            DesktopView.Open(() => nullBrowserShellWindows, nullBrowserReleased.Add));
        Assert.Null(nullBrowserError.InnerException);
        Assert.Equal(new object[] { nullBrowserShellWindows, nullBrowserProvider }, nullBrowserReleased);

        var nullViewReleased = new List<object>();
        var nullViewBrowser = new FakeShellBrowser(view: null);
        var nullViewProvider = new FakeServiceProvider(nullViewBrowser);
        var nullViewShellWindows = new FakeShellWindows(dispatchResult: nullViewProvider);
        var nullViewError = Assert.Throws<DesktopUnavailableException>(() =>
            DesktopView.Open(() => nullViewShellWindows, nullViewReleased.Add));
        Assert.Null(nullViewError.InnerException);
        Assert.Equal(
            new object[] { nullViewShellWindows, nullViewProvider, nullViewBrowser },
            nullViewReleased);

        const int classNotRegistered = unchecked((int)0x80040154);
        var factoryComError = Assert.Throws<DesktopUnavailableException>(() =>
            DesktopView.Open(
                () => throw Marshal.GetExceptionForHR(classNotRegistered)!,
                static _ => { }));
        var inner = Assert.IsType<COMException>(factoryComError.InnerException);
        Assert.Equal(classNotRegistered, inner.HResult);
    }

    private sealed class FakeShellWindows(int hr = ShellConstants.S_OK, object? dispatchResult = null) : IShellWindows
    {
        public object? RecordedLocation { get; private set; }
        public object? RecordedLocationRoot { get; private set; }
        public int RecordedSwClass { get; private set; }
        public int RecordedOptions { get; private set; }

        public int FindWindowSW(
            ref object? pvarLoc,
            ref object? pvarLocRoot,
            int swClass,
            out int phwnd,
            int swfwOptions,
            out object? ppdispOut)
        {
            RecordedLocation = pvarLoc;
            RecordedLocationRoot = pvarLocRoot;
            RecordedSwClass = swClass;
            RecordedOptions = swfwOptions;
            phwnd = 1234;
            ppdispOut = dispatchResult;
            return hr;
        }

        public int get_Count() => throw new NotImplementedException();
        public object? Item(object index) => throw new NotImplementedException();
        public object _NewEnum() => throw new NotImplementedException();
        public void Register(object pid, int hwnd, int swClass, out int plCookie) => throw new NotImplementedException();
        public void RegisterPending(int lThreadId, ref object? pvarloc, ref object? pvarlocRoot, int swClass, out int plCookie) => throw new NotImplementedException();
        public void Revoke(int lCookie) => throw new NotImplementedException();
        public void OnNavigate(int lCookie, ref object? pvarLoc) => throw new NotImplementedException();
        public void OnActivated(int lCookie, bool fActive) => throw new NotImplementedException();
        public void OnCreated(int lCookie, object punk) => throw new NotImplementedException();
        public void ProcessAttachDetach(bool fAttach) => throw new NotImplementedException();
    }

    private sealed class FakeServiceProvider(object? browser, Exception? toThrow = null) : Berth.Desktop.Interop.IServiceProvider
    {
        public Guid RecordedServiceId { get; private set; }
        public Guid RecordedInterfaceId { get; private set; }

        public void QueryService(in Guid guidService, in Guid riid, out object? ppvObject)
        {
            RecordedServiceId = guidService;
            RecordedInterfaceId = riid;
            if (toThrow is not null)
            {
                throw toThrow;
            }

            ppvObject = browser;
        }
    }

    private sealed class FakeShellBrowser(IShellView? view, Exception? toThrow = null, Action? onAcquired = null) : IShellBrowser
    {
        public void QueryActiveShellView(out IShellView ppshv)
        {
            if (toThrow is not null)
            {
                throw toThrow;
            }

            onAcquired?.Invoke();
            ppshv = view!;
        }

        public void GetWindow(out nint phwnd) => throw new NotImplementedException();
        public void ContextSensitiveHelp(bool fEnterMode) => throw new NotImplementedException();
        public void InsertMenusSB(nint hmenuShared, nint lpMenuWidths) => throw new NotImplementedException();
        public void SetMenuSB(nint hmenuShared, nint holemenuRes, nint hwndActiveObject) => throw new NotImplementedException();
        public void RemoveMenusSB(nint hmenuShared) => throw new NotImplementedException();
        public void SetStatusTextSB(string? pszStatusText) => throw new NotImplementedException();
        public void EnableModelessSB(bool fEnable) => throw new NotImplementedException();
        public int TranslateAcceleratorSB(nint pmsg, ushort wID) => throw new NotImplementedException();
        public void BrowseObject(nint pidl, uint wFlags) => throw new NotImplementedException();
        public void GetViewStateStream(uint grfMode, out object? ppStrm) => throw new NotImplementedException();
        public void GetControlWindow(uint id, out nint phwnd) => throw new NotImplementedException();
        public void SendControlMsg(uint id, uint uMsg, nuint wParam, nint lParam, out nint pret) => throw new NotImplementedException();
        public void OnViewWindowActive(IShellView? pshv) => throw new NotImplementedException();
        public void SetToolbarItems(nint lpButtons, uint nButtons, uint uFlags) => throw new NotImplementedException();
    }

    private sealed class FakeShellViewWithoutFolderView : IShellView
    {
        public void GetWindow(out nint phwnd) => throw new NotImplementedException();
        public void ContextSensitiveHelp(bool fEnterMode) => throw new NotImplementedException();
        public int TranslateAccelerator(nint pmsg) => throw new NotImplementedException();
        public void EnableModeless(bool fEnable) => throw new NotImplementedException();
        public void UIActivate(uint uState) => throw new NotImplementedException();
        public void Refresh() => throw new NotImplementedException();
        public void CreateViewWindow(IShellView? psvPrevious, nint pfs, IShellBrowser? psb, nint prcView, out nint phWnd) => throw new NotImplementedException();
        public void DestroyViewWindow() => throw new NotImplementedException();
        public void GetCurrentInfo(nint pfs) => throw new NotImplementedException();
        public void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam) => throw new NotImplementedException();
        public void SaveViewState() => throw new NotImplementedException();
        public void SelectItem(nint pidlItem, uint uFlags) => throw new NotImplementedException();
        public void GetItemObject(uint uItem, in Guid riid, out object ppv) => throw new NotImplementedException();
    }

    private sealed class FakeFolderViewOnly : IShellView, IFolderView
    {
        public void ItemCount(uint uFlags, out int pcItems) => pcItems = 3;

        public void GetWindow(out nint phwnd) => throw new NotImplementedException();
        public void ContextSensitiveHelp(bool fEnterMode) => throw new NotImplementedException();
        public int TranslateAccelerator(nint pmsg) => throw new NotImplementedException();
        public void EnableModeless(bool fEnable) => throw new NotImplementedException();
        public void UIActivate(uint uState) => throw new NotImplementedException();
        public void Refresh() => throw new NotImplementedException();
        public void CreateViewWindow(IShellView? psvPrevious, nint pfs, IShellBrowser? psb, nint prcView, out nint phWnd) => throw new NotImplementedException();
        public void DestroyViewWindow() => throw new NotImplementedException();
        public void GetCurrentInfo(nint pfs) => throw new NotImplementedException();
        public void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam) => throw new NotImplementedException();
        public void SaveViewState() => throw new NotImplementedException();
        public void SelectItem(nint pidlItem, uint uFlags) => throw new NotImplementedException();
        public void GetItemObject(uint uItem, in Guid riid, out object ppv) => throw new NotImplementedException();
        public void GetCurrentViewMode(out uint pViewMode) => throw new NotImplementedException();
        public void SetCurrentViewMode(uint viewMode) => throw new NotImplementedException();
        public void GetFolder(in Guid riid, out object ppv) => throw new NotImplementedException();
        public void Item(int iItemIndex, out nint ppidl) => throw new NotImplementedException();
        public void Items(uint uFlags, in Guid riid, out object ppv) => throw new NotImplementedException();
        public void GetSelectionMarkedItem(out int piItem) => throw new NotImplementedException();
        public void GetFocusedItem(out int piItem) => throw new NotImplementedException();
        public void GetItemPosition(nint pidl, out NativePoint ppt) => throw new NotImplementedException();
        public void GetSpacing(ref NativePoint ppt) => throw new NotImplementedException();
        public void GetDefaultSpacing(out NativePoint ppt) => throw new NotImplementedException();
        public int GetAutoArrange() => throw new NotImplementedException();
        public void SelectItem(int iItem, uint dwFlags) => throw new NotImplementedException();
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) => throw new NotImplementedException();
    }

    private sealed class FakeFolderView2 : IShellView, IFolderView, IFolderView2
    {
        public void ItemCount(uint uFlags, out int pcItems) => pcItems = 7;
        public void GetCurrentFolderFlags(out uint pdwFlags) => pdwFlags = 0x12u;

        public void GetWindow(out nint phwnd) => throw new NotImplementedException();
        public void ContextSensitiveHelp(bool fEnterMode) => throw new NotImplementedException();
        public int TranslateAccelerator(nint pmsg) => throw new NotImplementedException();
        public void EnableModeless(bool fEnable) => throw new NotImplementedException();
        public void UIActivate(uint uState) => throw new NotImplementedException();
        public void Refresh() => throw new NotImplementedException();
        public void CreateViewWindow(IShellView? psvPrevious, nint pfs, IShellBrowser? psb, nint prcView, out nint phWnd) => throw new NotImplementedException();
        public void DestroyViewWindow() => throw new NotImplementedException();
        public void GetCurrentInfo(nint pfs) => throw new NotImplementedException();
        public void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam) => throw new NotImplementedException();
        public void SaveViewState() => throw new NotImplementedException();
        public void SelectItem(nint pidlItem, uint uFlags) => throw new NotImplementedException();
        public void GetItemObject(uint uItem, in Guid riid, out object ppv) => throw new NotImplementedException();
        public void GetCurrentViewMode(out uint pViewMode) => throw new NotImplementedException();
        public void SetCurrentViewMode(uint viewMode) => throw new NotImplementedException();
        public void GetFolder(in Guid riid, out object ppv) => throw new NotImplementedException();
        public void Item(int iItemIndex, out nint ppidl) => throw new NotImplementedException();
        public void Items(uint uFlags, in Guid riid, out object ppv) => throw new NotImplementedException();
        public void GetSelectionMarkedItem(out int piItem) => throw new NotImplementedException();
        public void GetFocusedItem(out int piItem) => throw new NotImplementedException();
        public void GetItemPosition(nint pidl, out NativePoint ppt) => throw new NotImplementedException();
        public void GetSpacing(ref NativePoint ppt) => throw new NotImplementedException();
        public void GetDefaultSpacing(out NativePoint ppt) => throw new NotImplementedException();
        public int GetAutoArrange() => throw new NotImplementedException();
        public void SelectItem(int iItem, uint dwFlags) => throw new NotImplementedException();
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) => throw new NotImplementedException();
        public void SetGroupBy(nint key, bool fAscending) => throw new NotImplementedException();
        public void GetGroupBy(nint pkey, out bool pfAscending) => throw new NotImplementedException();
        public void SetViewProperty(nint pidl, nint propkey, nint propvar) => throw new NotImplementedException();
        public void GetViewProperty(nint pidl, nint propkey, nint ppropvar) => throw new NotImplementedException();
        public void SetTileViewProperties(nint pidl, string pszPropList) => throw new NotImplementedException();
        public void SetExtendedTileViewProperties(nint pidl, string pszPropList) => throw new NotImplementedException();
        public void SetText(int iType, string pwszText) => throw new NotImplementedException();
        public void SetCurrentFolderFlags(uint dwMask, uint dwFlags) => throw new NotImplementedException();
    }

    private sealed class RefCountedFakeFolderView : IShellView, IFolderView
    {
        public int RefCount { get; set; }

        public void ItemCount(uint uFlags, out int pcItems)
        {
            if (RefCount <= 0)
            {
                throw new InvalidComObjectException("Shared RCW refcount already reached zero.");
            }

            pcItems = 9;
        }

        public void GetWindow(out nint phwnd) => throw new NotImplementedException();
        public void ContextSensitiveHelp(bool fEnterMode) => throw new NotImplementedException();
        public int TranslateAccelerator(nint pmsg) => throw new NotImplementedException();
        public void EnableModeless(bool fEnable) => throw new NotImplementedException();
        public void UIActivate(uint uState) => throw new NotImplementedException();
        public void Refresh() => throw new NotImplementedException();
        public void CreateViewWindow(IShellView? psvPrevious, nint pfs, IShellBrowser? psb, nint prcView, out nint phWnd) => throw new NotImplementedException();
        public void DestroyViewWindow() => throw new NotImplementedException();
        public void GetCurrentInfo(nint pfs) => throw new NotImplementedException();
        public void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam) => throw new NotImplementedException();
        public void SaveViewState() => throw new NotImplementedException();
        public void SelectItem(nint pidlItem, uint uFlags) => throw new NotImplementedException();
        public void GetItemObject(uint uItem, in Guid riid, out object ppv) => throw new NotImplementedException();
        public void GetCurrentViewMode(out uint pViewMode) => throw new NotImplementedException();
        public void SetCurrentViewMode(uint viewMode) => throw new NotImplementedException();
        public void GetFolder(in Guid riid, out object ppv) => throw new NotImplementedException();
        public void Item(int iItemIndex, out nint ppidl) => throw new NotImplementedException();
        public void Items(uint uFlags, in Guid riid, out object ppv) => throw new NotImplementedException();
        public void GetSelectionMarkedItem(out int piItem) => throw new NotImplementedException();
        public void GetFocusedItem(out int piItem) => throw new NotImplementedException();
        public void GetItemPosition(nint pidl, out NativePoint ppt) => throw new NotImplementedException();
        public void GetSpacing(ref NativePoint ppt) => throw new NotImplementedException();
        public void GetDefaultSpacing(out NativePoint ppt) => throw new NotImplementedException();
        public int GetAutoArrange() => throw new NotImplementedException();
        public void SelectItem(int iItem, uint dwFlags) => throw new NotImplementedException();
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) => throw new NotImplementedException();
    }

    private sealed class FakeCombinedShellChain :
        IShellWindows,
        Berth.Desktop.Interop.IServiceProvider,
        IShellBrowser,
        IShellView,
        IFolderView,
        IFolderView2
    {
        public int FindWindowSW(ref object? pvarLoc, ref object? pvarLocRoot, int swClass, out int phwnd, int swfwOptions, out object? ppdispOut)
        {
            phwnd = 1;
            ppdispOut = this;
            return ShellConstants.S_OK;
        }

        public void QueryService(in Guid guidService, in Guid riid, out object? ppvObject) => ppvObject = this;
        public void QueryActiveShellView(out IShellView ppshv) => ppshv = this;
        public void ItemCount(uint uFlags, out int pcItems) => pcItems = 5;
        public void GetCurrentFolderFlags(out uint pdwFlags) => pdwFlags = 0u;

        public int get_Count() => throw new NotImplementedException();
        public object? Item(object index) => throw new NotImplementedException();
        public object _NewEnum() => throw new NotImplementedException();
        public void Register(object pid, int hwnd, int swClass, out int plCookie) => throw new NotImplementedException();
        public void RegisterPending(int lThreadId, ref object? pvarloc, ref object? pvarlocRoot, int swClass, out int plCookie) => throw new NotImplementedException();
        public void Revoke(int lCookie) => throw new NotImplementedException();
        public void OnNavigate(int lCookie, ref object? pvarLoc) => throw new NotImplementedException();
        public void OnActivated(int lCookie, bool fActive) => throw new NotImplementedException();
        public void OnCreated(int lCookie, object punk) => throw new NotImplementedException();
        public void ProcessAttachDetach(bool fAttach) => throw new NotImplementedException();
        public void GetWindow(out nint phwnd) => throw new NotImplementedException();
        public void ContextSensitiveHelp(bool fEnterMode) => throw new NotImplementedException();
        public void InsertMenusSB(nint hmenuShared, nint lpMenuWidths) => throw new NotImplementedException();
        public void SetMenuSB(nint hmenuShared, nint holemenuRes, nint hwndActiveObject) => throw new NotImplementedException();
        public void RemoveMenusSB(nint hmenuShared) => throw new NotImplementedException();
        public void SetStatusTextSB(string? pszStatusText) => throw new NotImplementedException();
        public void EnableModelessSB(bool fEnable) => throw new NotImplementedException();
        public int TranslateAcceleratorSB(nint pmsg, ushort wID) => throw new NotImplementedException();
        public void BrowseObject(nint pidl, uint wFlags) => throw new NotImplementedException();
        public void GetViewStateStream(uint grfMode, out object? ppStrm) => throw new NotImplementedException();
        public void GetControlWindow(uint id, out nint phwnd) => throw new NotImplementedException();
        public void SendControlMsg(uint id, uint uMsg, nuint wParam, nint lParam, out nint pret) => throw new NotImplementedException();
        public void OnViewWindowActive(IShellView? pshv) => throw new NotImplementedException();
        public void SetToolbarItems(nint lpButtons, uint nButtons, uint uFlags) => throw new NotImplementedException();
        public int TranslateAccelerator(nint pmsg) => throw new NotImplementedException();
        public void EnableModeless(bool fEnable) => throw new NotImplementedException();
        public void UIActivate(uint uState) => throw new NotImplementedException();
        public void Refresh() => throw new NotImplementedException();
        public void CreateViewWindow(IShellView? psvPrevious, nint pfs, IShellBrowser? psb, nint prcView, out nint phWnd) => throw new NotImplementedException();
        public void DestroyViewWindow() => throw new NotImplementedException();
        public void GetCurrentInfo(nint pfs) => throw new NotImplementedException();
        public void AddPropertySheetPages(uint dwReserved, nint pfn, nint lparam) => throw new NotImplementedException();
        public void SaveViewState() => throw new NotImplementedException();
        public void SelectItem(nint pidlItem, uint uFlags) => throw new NotImplementedException();
        public void GetItemObject(uint uItem, in Guid riid, out object ppv) => throw new NotImplementedException();
        public void GetCurrentViewMode(out uint pViewMode) => throw new NotImplementedException();
        public void SetCurrentViewMode(uint viewMode) => throw new NotImplementedException();
        public void GetFolder(in Guid riid, out object ppv) => throw new NotImplementedException();
        public void Item(int iItemIndex, out nint ppidl) => throw new NotImplementedException();
        public void Items(uint uFlags, in Guid riid, out object ppv) => throw new NotImplementedException();
        public void GetSelectionMarkedItem(out int piItem) => throw new NotImplementedException();
        public void GetFocusedItem(out int piItem) => throw new NotImplementedException();
        public void GetItemPosition(nint pidl, out NativePoint ppt) => throw new NotImplementedException();
        public void GetSpacing(ref NativePoint ppt) => throw new NotImplementedException();
        public void GetDefaultSpacing(out NativePoint ppt) => throw new NotImplementedException();
        public int GetAutoArrange() => throw new NotImplementedException();
        public void SelectItem(int iItem, uint dwFlags) => throw new NotImplementedException();
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) => throw new NotImplementedException();
        public void SetGroupBy(nint key, bool fAscending) => throw new NotImplementedException();
        public void GetGroupBy(nint pkey, out bool pfAscending) => throw new NotImplementedException();
        public void SetViewProperty(nint pidl, nint propkey, nint propvar) => throw new NotImplementedException();
        public void GetViewProperty(nint pidl, nint propkey, nint ppropvar) => throw new NotImplementedException();
        public void SetTileViewProperties(nint pidl, string pszPropList) => throw new NotImplementedException();
        public void SetExtendedTileViewProperties(nint pidl, string pszPropList) => throw new NotImplementedException();
        public void SetText(int iType, string pwszText) => throw new NotImplementedException();
        public void SetCurrentFolderFlags(uint dwMask, uint dwFlags) => throw new NotImplementedException();
    }
}
