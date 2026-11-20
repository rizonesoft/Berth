using System.Runtime.InteropServices;
using Berth.Core;
using Berth.Desktop;
using Berth.Desktop.Interop;

namespace Berth.Tests.Desktop;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed class AutoArrangeTests
{
    [Fact]
    public void LiveAutoArrangeReadsWithoutThrowingAndAgreesWithFallback()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var icons = new ShellDesktopIcons();
        bool publicValue = icons.AutoArrange;
        (bool Primary, bool Fallback) liveValues = icons.RunWithView(desktop => (
            ShellDesktopIcons.ReadAutoArrange(desktop.View, desktop.View2),
            ShellDesktopIcons.ReadAutoArrange(desktop.View, null)));

        Assert.Equal(liveValues.Primary, liveValues.Fallback);
        Assert.Equal(liveValues.Primary, publicValue);
    }

    [Fact]
    public void OptInRoundTripTurnsAutoArrangeOffReadsBackAndRestores()
    {
        bool allowed = string.Equals(
            Environment.GetEnvironmentVariable("BERTH_TEST_ALLOW_AUTOARRANGE"),
            "1",
            StringComparison.Ordinal);
        Assert.SkipUnless(allowed, "Opt-in test: set BERTH_TEST_ALLOW_AUTOARRANGE=1 on a disposable interactive desktop.");
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var icons = new ShellDesktopIcons();
        bool startedOn = icons.AutoArrange;
        Assert.True(
            startedOn,
            "Opt-in auto arrange round trip requires auto arrange to already be enabled on a disposable desktop.");

        try
        {
            bool turnedOff = icons.TryTurnAutoArrangeOff();
            Assert.True(turnedOff);

            uint flagsAfterOff = icons.RunWithView(desktop =>
            {
                Assert.NotNull(desktop.View2);
                desktop.View2!.GetCurrentFolderFlags(out uint flags);
                return flags;
            });
            Assert.Equal(0u, flagsAfterOff & ShellConstants.FWF_AUTOARRANGE);
            Assert.False(icons.AutoArrange);
        }
        finally
        {
            uint restoredFlags = icons.RunWithView(desktop =>
            {
                Assert.NotNull(desktop.View2);
                desktop.View2!.SetCurrentFolderFlags(ShellConstants.FWF_AUTOARRANGE, ShellConstants.FWF_AUTOARRANGE);
                desktop.View2!.GetCurrentFolderFlags(out uint flags);
                return flags;
            });
            Assert.NotEqual(0u, restoredFlags & ShellConstants.FWF_AUTOARRANGE);
        }
    }
}

public sealed class UnitAutoArrangeTests
{
    private const uint UnrelatedFolderFlags = 0x00000240u;

    [Theory]
    [InlineData(0u, false)]
    [InlineData(UnrelatedFolderFlags, false)]
    [InlineData(ShellConstants.FWF_AUTOARRANGE, true)]
    [InlineData(UnrelatedFolderFlags | ShellConstants.FWF_AUTOARRANGE, true)]
    public void AutoArrangeReadsFolderView2FlagIgnoringUnrelatedBits(uint initialFlags, bool expected)
    {
        var releases = new List<object>();
        var view = new FakeFolderView2(initialFlags);
        using var icons = CreateIcons(view, releases);

        bool actual = icons.AutoArrange;

        Assert.Equal(expected, actual);
        Assert.Equal(1, view.GetFlagsCalls);
        Assert.Equal(0, view.GetAutoArrangeCalls);
        Assert.Single(releases);
    }

    [Theory]
    [InlineData(ShellConstants.S_OK, true)]
    [InlineData(ShellConstants.S_FALSE, false)]
    public void AutoArrangeFallsBackToIFolderViewGetAutoArrangeWhenView2IsNull(int hr, bool expected)
    {
        var releases = new List<object>();
        var view = new FakeFolderViewOnly(hr);
        using var icons = CreateIcons(view, releases);

        bool actual = icons.AutoArrange;

        Assert.Equal(expected, actual);
        Assert.Equal(1, view.GetAutoArrangeCalls);
        Assert.Single(releases);
    }

    [Theory]
    [InlineData(unchecked((int)0x80004005), typeof(COMException))]
    [InlineData(unchecked((int)0x80070005), typeof(UnauthorizedAccessException))]
    [InlineData(0x00000002, null)]
    public void AutoArrangeFallbackWrapsErrorAndUnexpectedHResultAsDesktopUnavailableException(int hr, Type? expectedInnerType)
    {
        var releases = new List<object>();
        var view = new FakeFolderViewOnly(hr);
        using var icons = CreateIcons(view, releases);

        DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() => _ = icons.AutoArrange);

        if (expectedInnerType is not null)
        {
            Assert.IsType(expectedInnerType, thrown.InnerException);
        }
        else
        {
            Assert.Null(thrown.InnerException);
        }

        Assert.Single(releases);
    }

    [Fact]
    public void TryTurnAutoArrangeOffClearsOnlyAutoArrangeBitAndReadsBackIgnoringUnrelatedBits()
    {
        var releases = new List<object>();
        var view = new FakeFolderView2(UnrelatedFolderFlags | ShellConstants.FWF_AUTOARRANGE);
        using var icons = CreateIcons(view, releases);

        bool turnedOff = icons.TryTurnAutoArrangeOff();

        Assert.True(turnedOff);
        Assert.Equal(1, view.SetFlagsCalls);
        Assert.Equal(ShellConstants.FWF_AUTOARRANGE, view.LastSetMask);
        Assert.Equal(0u, view.LastSetFlags);
        Assert.Equal(1, view.GetFlagsCalls);
        Assert.Equal(UnrelatedFolderFlags, view.CurrentFlags);
        Assert.Single(releases);
    }

    [Fact]
    public void TryTurnAutoArrangeOffReturnsFalseWhenReadBackKeepsFlagSet()
    {
        var releases = new List<object>();
        var view = new FakeFolderView2(
            UnrelatedFolderFlags | ShellConstants.FWF_AUTOARRANGE,
            ignoreSetFlags: true);
        using var icons = CreateIcons(view, releases);

        bool turnedOff = icons.TryTurnAutoArrangeOff();

        Assert.False(turnedOff);
        Assert.Equal(1, view.SetFlagsCalls);
        Assert.Equal(ShellConstants.FWF_AUTOARRANGE, view.LastSetMask);
        Assert.Equal(0u, view.LastSetFlags);
        Assert.Equal(1, view.GetFlagsCalls);
        Assert.Single(releases);
    }

    [Fact]
    public void TryTurnAutoArrangeOffReturnsFalseWhenView2IsNull()
    {
        var releases = new List<object>();
        var view = new FakeFolderViewOnly(ShellConstants.S_OK);
        using var icons = CreateIcons(view, releases);

        bool turnedOff = icons.TryTurnAutoArrangeOff();

        Assert.False(turnedOff);
        Assert.Equal(0, view.GetAutoArrangeCalls);
        Assert.Single(releases);
    }

    public static TheoryData<string, Exception> MidCallFailureCases() => new()
    {
        { "GetCurrentFolderFlags", Marshal.GetExceptionForHR(unchecked((int)0x800706BA))! },
        { "GetCurrentFolderFlags", new InvalidCastException("QI failed") },
        { "GetCurrentFolderFlags", new InvalidComObjectException("RCW disconnected") },
        { "GetCurrentFolderFlags", new UnauthorizedAccessException("Access denied") },
        { "GetAutoArrange", Marshal.GetExceptionForHR(unchecked((int)0x800706BA))! },
        { "SetCurrentFolderFlags", Marshal.GetExceptionForHR(unchecked((int)0x800706BA))! },
        { "SetCurrentFolderFlags", new UnauthorizedAccessException("Access denied") },
        { "PostWriteGetCurrentFolderFlags", Marshal.GetExceptionForHR(unchecked((int)0x800706BA))! },
        { "PostWriteGetCurrentFolderFlags", new InvalidComObjectException("RCW disconnected on read-back") },
    };

    [Theory]
    [MemberData(nameof(MidCallFailureCases))]
    public void MidCallComFailuresWrapInDesktopUnavailableExceptionPreserveCauseAndReleaseView(string failurePoint, Exception injected)
    {
        var releases = new List<(int ThreadId, ApartmentState Apartment)>();
        IShellView fakeView = failurePoint switch
        {
            "GetCurrentFolderFlags" => new FakeFolderView2(ShellConstants.FWF_AUTOARRANGE, getFlagsError: injected),
            "GetAutoArrange" => new FakeFolderViewOnly(ShellConstants.S_OK, getAutoArrangeError: injected),
            "SetCurrentFolderFlags" => new FakeFolderView2(ShellConstants.FWF_AUTOARRANGE, setFlagsError: injected),
            "PostWriteGetCurrentFolderFlags" => new FakeFolderView2(ShellConstants.FWF_AUTOARRANGE, getFlagsError: injected),
            _ => throw new ArgumentOutOfRangeException(nameof(failurePoint), failurePoint, null),
        };

        using var icons = new ShellDesktopIcons(() => DesktopView.Open(
            () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(fakeView))),
            obj =>
            {
                if (ReferenceEquals(obj, fakeView))
                {
                    releases.Add((Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
                }
            }));

        int workerThreadId = icons.RunOnWorker(() => Environment.CurrentManagedThreadId);

        DesktopUnavailableException thrown = failurePoint is "GetCurrentFolderFlags" or "GetAutoArrange"
            ? Assert.Throws<DesktopUnavailableException>(() => _ = icons.AutoArrange)
            : Assert.Throws<DesktopUnavailableException>(() => _ = icons.TryTurnAutoArrangeOff());

        Assert.Same(injected, thrown.InnerException);
        Assert.Single(releases);
        Assert.Equal(workerThreadId, releases[0].ThreadId);
        Assert.Equal(ApartmentState.STA, releases[0].Apartment);
    }

    private static ShellDesktopIcons CreateIcons(IShellView view, List<object> viewReleases) =>
        new(() => DesktopView.Open(
            () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(view))),
            obj =>
            {
                if (ReferenceEquals(obj, view))
                {
                    viewReleases.Add(obj);
                }
            }));

    private sealed class FakeFolderViewOnly(int autoArrangeHr, Exception? getAutoArrangeError = null) : IShellView, IFolderView
    {
        public int GetAutoArrangeCalls { get; private set; }

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
        public void GetFolder(in Guid riid, out object ppv) => ppv = new object();
        public void Item(int iItemIndex, out nint ppidl) => ppidl = nint.Zero;
        public void ItemCount(uint uFlags, out int pcItems) => pcItems = 0;
        public void Items(uint uFlags, in Guid riid, out object ppv) => ppv = new object();
        public void GetSelectionMarkedItem(out int piItem) => piItem = 0;
        public void GetFocusedItem(out int piItem) => piItem = 0;
        public void GetItemPosition(nint pidl, out NativePoint ppt) => ppt = default;
        public void GetSpacing(ref NativePoint ppt) { }
        public void GetDefaultSpacing(out NativePoint ppt) => ppt = default;
        public int GetAutoArrange()
        {
            GetAutoArrangeCalls++;
            if (getAutoArrangeError is not null)
            {
                throw getAutoArrangeError;
            }

            return autoArrangeHr;
        }
        public void SelectItem(int iItem, uint dwFlags) { }
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) { }
    }

    private sealed class FakeFolderView2(
        uint initialFlags,
        bool ignoreSetFlags = false,
        Exception? getFlagsError = null,
        Exception? setFlagsError = null) : IShellView, IFolderView, IFolderView2
    {
        public uint CurrentFlags { get; private set; } = initialFlags;
        public int GetFlagsCalls { get; private set; }
        public int SetFlagsCalls { get; private set; }
        public int GetAutoArrangeCalls { get; private set; }
        public uint LastSetMask { get; private set; }
        public uint LastSetFlags { get; private set; }

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
        public void GetFolder(in Guid riid, out object ppv) => ppv = new object();
        public void Item(int iItemIndex, out nint ppidl) => ppidl = nint.Zero;
        public void ItemCount(uint uFlags, out int pcItems) => pcItems = 0;
        public void Items(uint uFlags, in Guid riid, out object ppv) => ppv = new object();
        public void GetSelectionMarkedItem(out int piItem) => piItem = 0;
        public void GetFocusedItem(out int piItem) => piItem = 0;
        public void GetItemPosition(nint pidl, out NativePoint ppt) => ppt = default;
        public void GetSpacing(ref NativePoint ppt) { }
        public void GetDefaultSpacing(out NativePoint ppt) => ppt = default;
        public int GetAutoArrange()
        {
            GetAutoArrangeCalls++;
            return (CurrentFlags & ShellConstants.FWF_AUTOARRANGE) != 0 ? ShellConstants.S_OK : ShellConstants.S_FALSE;
        }
        public void SelectItem(int iItem, uint dwFlags) { }
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) { }
        public void SetGroupBy(nint key, bool fAscending) { }
        public void GetGroupBy(nint pkey, out bool pfAscending) => pfAscending = false;
        public void SetViewProperty(nint pidl, nint propkey, nint propvar) { }
        public void GetViewProperty(nint pidl, nint propkey, nint ppropvar) { }
        public void SetTileViewProperties(nint pidl, string pszPropList) { }
        public void SetExtendedTileViewProperties(nint pidl, string pszPropList) { }
        public void SetText(int iType, string pwszText) { }

        public void SetCurrentFolderFlags(uint dwMask, uint dwFlags)
        {
            SetFlagsCalls++;
            LastSetMask = dwMask;
            LastSetFlags = dwFlags;
            if (setFlagsError is not null)
            {
                throw setFlagsError;
            }

            if (!ignoreSetFlags)
            {
                CurrentFlags = (CurrentFlags & ~dwMask) | (dwFlags & dwMask);
            }
        }

        public void GetCurrentFolderFlags(out uint pdwFlags)
        {
            GetFlagsCalls++;
            if (getFlagsError is not null)
            {
                throw getFlagsError;
            }

            pdwFlags = CurrentFlags;
        }
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
