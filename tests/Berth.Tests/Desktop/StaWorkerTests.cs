using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Desktop.Interop;

namespace Berth.Tests.Desktop;

[Collection(DesktopCollectionDefinition.Name)]
[Trait("Category", "RealDesktop")]
public sealed partial class StaWorkerTests
{
    private const int WmApp = 0x8000;
    private const int WmPumpProbe = WmApp + 1;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint msg, nuint wParam, nint lParam);

    [Fact]
    public async Task ShellDesktopIconsRunWithViewFromThreadPoolOpensLiveDesktopView()
    {
        Assert.SkipUnless(Sta.HasInteractiveDesktopSession, "Requires an interactive Windows desktop session.");

        using var icons = new ShellDesktopIcons(DesktopView.Open);
        (ApartmentState CallerApartment, ApartmentState WorkerApartment, int Count) result = await Task.Run(() =>
        {
            ApartmentState callerApartment = Thread.CurrentThread.GetApartmentState();
            return icons.RunWithView(desktop =>
            {
                desktop.View.ItemCount(ShellConstants.SVGIO_ALLVIEW, out int count);
                return (callerApartment, Thread.CurrentThread.GetApartmentState(), count);
            });
        });

        Assert.Equal(ApartmentState.MTA, result.CallerApartment);
        Assert.Equal(ApartmentState.STA, result.WorkerApartment);
        Assert.True(result.Count >= 0, $"Expected nonnegative desktop icon count, got {result.Count}.");
    }

    [Fact]
    public void IdleWorkerPumpsWin32MessagesWithoutSubmittingAnotherRun()
    {
        using var worker = new StaWorker();
        using var messageHandled = new ManualResetEventSlim(false);
        int messageThreadId = 0;
        ApartmentState messageApartment = ApartmentState.Unknown;

        (int WorkerThreadId, HwndSource Source, nint Handle) setup = worker.Run(() =>
        {
            var parameters = new HwndSourceParameters("BerthStaWorkerPumpProbe")
            {
                ParentWindow = new nint(-3), // HWND_MESSAGE
                Width = 0,
                Height = 0,
            };
            var source = new HwndSource(parameters);
            source.AddHook((nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
            {
                if (msg == WmPumpProbe)
                {
                    messageThreadId = Environment.CurrentManagedThreadId;
                    messageApartment = Thread.CurrentThread.GetApartmentState();
                    handled = true;
                    messageHandled.Set();
                }

                return nint.Zero;
            });
            return (Environment.CurrentManagedThreadId, source, source.Handle);
        });

        try
        {
            Assert.NotEqual(nint.Zero, setup.Handle);
            bool posted = PostMessageW(setup.Handle, WmPumpProbe, 0, 0);
            Assert.True(posted, $"PostMessageW failed with Win32 error {Marshal.GetLastPInvokeError()}.");

            Assert.True(
                messageHandled.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken),
                "Idle STA worker did not pump a posted Win32 message without another Run call.");
            Assert.Equal(setup.WorkerThreadId, messageThreadId);
            Assert.Equal(ApartmentState.STA, messageApartment);
        }
        finally
        {
            worker.Run(() => setup.Source.Dispose());
        }
    }
}

public sealed class UnitStaWorkerTests
{

    [Fact]
    public void RunExecutesOnSingleStaThreadAcrossThreeExternalMtaThreads()
    {
        using var worker = new StaWorker();
        var results = new (int WorkerThreadId, ApartmentState Apartment, bool IsBackground, int CallerThreadId)[3];

        for (int i = 0; i < 3; i++)
        {
            int index = i;
            Exception? threadError = null;
            var caller = new Thread(() =>
            {
                try
                {
                    int callerId = Environment.CurrentManagedThreadId;
                    results[index] = worker.Run(() => (
                        Environment.CurrentManagedThreadId,
                        Thread.CurrentThread.GetApartmentState(),
                        Thread.CurrentThread.IsBackground,
                        callerId));
                }
                catch (Exception ex)
                {
                    threadError = ex;
                }
            });
            caller.SetApartmentState(ApartmentState.MTA);
            caller.Start();
            Assert.True(caller.Join(TimeSpan.FromSeconds(5)), "External MTA caller thread timed out.");
            Assert.Null(threadError);
        }

        int expectedWorkerId = results[0].WorkerThreadId;
        foreach ((int workerId, ApartmentState apartment, bool isBackground, int callerId) in results)
        {
            Assert.Equal(ApartmentState.STA, apartment);
            Assert.True(isBackground);
            Assert.Equal(expectedWorkerId, workerId);
            Assert.NotEqual(callerId, workerId);
        }
    }

    [Fact]
    public void NestedRunOnWorkerThreadExecutesInlineWithoutDeadlock()
    {
        using var worker = new StaWorker();

        (int OuterId, int InnerFuncId, int InnerActionId, ApartmentState Apartment) result = worker.Run(() =>
        {
            int outerId = Environment.CurrentManagedThreadId;
            int innerFuncId = worker.Run(() => Environment.CurrentManagedThreadId);
            int innerActionId = 0;
            worker.Run(() =>
            {
                innerActionId = Environment.CurrentManagedThreadId;
            });
            return (outerId, innerFuncId, innerActionId, Thread.CurrentThread.GetApartmentState());
        });

        Assert.Equal(ApartmentState.STA, result.Apartment);
        Assert.Equal(result.OuterId, result.InnerFuncId);
        Assert.Equal(result.OuterId, result.InnerActionId);
    }

    [Fact]
    public void RunPropagatesExceptionTypeAndOriginalStackFrame()
    {
        using var worker = new StaWorker();

        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            worker.Run<int>(ThrowFromNamedHelper));

        Assert.Equal("sta-boom", error.Message);
        Assert.Contains(nameof(ThrowFromNamedHelper), error.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void RunRejectsNullDelegate()
    {
        using var worker = new StaWorker();

        Assert.Throws<ArgumentNullException>(() => worker.Run((Func<int>)null!));
        Assert.Throws<ArgumentNullException>(() => worker.Run((Action)null!));
    }

    [Fact]
    public async Task DisposeDrainsHeldAndQueuedWorkRejectsLateCallersAndExitsAfterTimeout()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int enqueuedCount = 0;
        using var secondEnqueued = new ManualResetEventSlim(false);
        using var thirdEnqueued = new ManualResetEventSlim(false);
        var worker = new StaWorker(
            disposeTimeout: TimeSpan.FromMilliseconds(150),
            onOperationCreated: _ =>
            {
                int count = Interlocked.Increment(ref enqueuedCount);
                if (count == 3)
                {
                    secondEnqueued.Set();
                }
                else if (count == 4)
                {
                    thirdEnqueued.Set();
                }
            });

        Thread workerThread = worker.Run(() => Thread.CurrentThread);
        Assert.True(workerThread.IsAlive);

        using var firstEntered = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        int firstExecutions = 0;
        int secondExecutions = 0;
        int thirdExecutions = 0;
        var executionOrder = new List<int>(3);

        Task<int> firstTask = Task.Run(() => worker.Run(() =>
        {
            executionOrder.Add(1);
            Interlocked.Increment(ref firstExecutions);
            firstEntered.Set();
            Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(5), ct), "Timed out waiting to release first callback.");
            return 101;
        }), ct);
        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5), ct), "First callback did not start.");

        Task<int> secondTask = Task.Run(() => worker.Run(() =>
        {
            executionOrder.Add(2);
            Interlocked.Increment(ref secondExecutions);
            return 202;
        }), ct);
        Assert.True(secondEnqueued.Wait(TimeSpan.FromSeconds(5), ct), "Second callback was not enqueued.");

        Task thirdTask = Task.Run(() => worker.Run(() =>
        {
            executionOrder.Add(3);
            Interlocked.Increment(ref thirdExecutions);
            throw new InvalidOperationException("queued-failure");
        }), ct);
        Assert.True(thirdEnqueued.Wait(TimeSpan.FromSeconds(5), ct), "Third callback was not enqueued.");

        var stopwatch = Stopwatch.StartNew();
        worker.Dispose();
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1.5), $"Dispose waited too long: {stopwatch.Elapsed}.");
        Assert.True(workerThread.IsAlive, "Worker thread should still be alive while first callback is held open.");

        Assert.Throws<ObjectDisposedException>(() => worker.Run(() => 999));
        Assert.Throws<ObjectDisposedException>(() => worker.Run(() => { }));

        releaseFirst.Set();

        Assert.Equal(101, await firstTask);
        Assert.Equal(202, await secondTask);
        InvalidOperationException thirdError = await Assert.ThrowsAsync<InvalidOperationException>(() => thirdTask);
        Assert.Equal("queued-failure", thirdError.Message);

        Assert.Equal(1, firstExecutions);
        Assert.Equal(1, secondExecutions);
        Assert.Equal(1, thirdExecutions);
        Assert.Equal([1, 2, 3], executionOrder);
        Assert.True(workerThread.Join(TimeSpan.FromSeconds(2)), "Worker thread did not exit after draining.");
        Assert.False(workerThread.IsAlive);
    }

    [Fact]
    public async Task SelfDisposeFromWorkerThreadDoesNotDeadlockAndExitsAfterDrainingQueuedWork()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int enqueuedCount = 0;
        using var secondEnqueued = new ManualResetEventSlim(false);
        var worker = new StaWorker(
            disposeTimeout: TimeSpan.FromSeconds(2),
            onOperationCreated: _ =>
            {
                if (Interlocked.Increment(ref enqueuedCount) == 3)
                {
                    secondEnqueued.Set();
                }
            });

        Thread workerThread = worker.Run(() => Thread.CurrentThread);
        using var firstEntered = new ManualResetEventSlim(false);
        using var selfDisposed = new ManualResetEventSlim(false);
        using var allowFirstReturn = new ManualResetEventSlim(false);

        Task<int> firstTask = Task.Run(() => worker.Run(() =>
        {
            firstEntered.Set();
            Assert.True(secondEnqueued.Wait(TimeSpan.FromSeconds(5), ct), "Second item was not queued before self-dispose.");
            worker.Dispose();
            selfDisposed.Set();
            Assert.True(allowFirstReturn.Wait(TimeSpan.FromSeconds(5), ct), "Timed out waiting to return from self-dispose.");
            return 11;
        }), ct);
        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5), ct));

        Task<int> secondTask = Task.Run(() => worker.Run(() => 22), ct);
        Assert.True(selfDisposed.Wait(TimeSpan.FromSeconds(5), ct), "Self-dispose deadlocked on worker thread.");

        Assert.Throws<ObjectDisposedException>(() => worker.Run(() => 33));
        allowFirstReturn.Set();

        Assert.Equal(11, await firstTask);
        Assert.Equal(22, await secondTask);
        Assert.True(workerThread.Join(TimeSpan.FromSeconds(2)), "Worker thread did not exit after self-disposal.");
        Assert.False(workerThread.IsAlive);
    }

    [Fact]
    public async Task RunThrowsPromptlyWhenQueuedDispatcherOperationIsAborted()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int createdCount = 0;
        using var worker = new StaWorker(
            disposeTimeout: TimeSpan.FromSeconds(2),
            onOperationCreated: op =>
            {
                if (Interlocked.Increment(ref createdCount) == 2)
                {
                    _ = op.Abort();
                }
            });

        using var firstEntered = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        int abortedBodyExecutions = 0;

        Task<int> firstTask = Task.Run(() => worker.Run(() =>
        {
            firstEntered.Set();
            Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(5), ct), "Timed out waiting to release first callback.");
            return 7;
        }), ct);
        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5), ct), "First callback did not start.");

        Task<int> abortedTask = Task.Run(() => worker.Run(() =>
        {
            Interlocked.Increment(ref abortedBodyExecutions);
            return 99;
        }), ct);

        Task completed = await Task.WhenAny(abortedTask, Task.Delay(TimeSpan.FromSeconds(2), ct));
        Assert.Same(abortedTask, completed);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => abortedTask);
        Assert.Contains("Aborted", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, abortedBodyExecutions);

        releaseFirst.Set();
        Assert.Equal(7, await firstTask);
    }

    [Fact]
    public void DisposeStopsThreadAndIsIdempotent()
    {
        var worker = new StaWorker();
        Thread workerThread = worker.Run(() => Thread.CurrentThread);
        Assert.True(workerThread.IsAlive);

        worker.Dispose();
        worker.Dispose();

        Assert.False(workerThread.IsAlive);
        Assert.Throws<ObjectDisposedException>(() => worker.Run(() => 1));
        Assert.Throws<ObjectDisposedException>(() => worker.Run(() => { }));
    }

    [Fact]
    public void ShellDesktopIconsRunWithViewReleasesViewOnceOnStaWorkerOnSuccessAndFailure()
    {
        var releases = new List<(object Released, int ThreadId, ApartmentState Apartment)>();

        DesktopView CreateFakeView()
        {
            var viewObj = new StubShellView();
            var browserObj = new StubShellBrowser(viewObj);
            var dispatchObj = new StubServiceProvider(browserObj);
            var shellWindowsObj = new StubShellWindows(dispatchObj);
            return DesktopView.Open(
                () => shellWindowsObj,
                obj =>
                {
                    if (ReferenceEquals(obj, viewObj))
                    {
                        releases.Add((obj, Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
                    }
                });
        }

        using var icons = new ShellDesktopIcons(CreateFakeView);
        int expectedWorkerId = icons.RunOnWorker(() => Environment.CurrentManagedThreadId);

        int count = icons.RunWithView(desktop =>
        {
            Assert.Equal(expectedWorkerId, Environment.CurrentManagedThreadId);
            Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            desktop.View.ItemCount(ShellConstants.SVGIO_ALLVIEW, out int c);
            return c;
        });
        Assert.Equal(7, count);
        Assert.Single(releases);
        Assert.Equal(expectedWorkerId, releases[0].ThreadId);
        Assert.Equal(ApartmentState.STA, releases[0].Apartment);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            icons.RunWithView<int>(_ => throw new InvalidOperationException("view-callback-failed")));
        Assert.Equal("view-callback-failed", failure.Message);
        Assert.Equal(2, releases.Count);
        Assert.Equal(expectedWorkerId, releases[1].ThreadId);
        Assert.Equal(ApartmentState.STA, releases[1].Apartment);
    }

    [Fact]
    public void ShellDesktopIconsRoutesAllPublicMembersThroughSharedWorkerBeforeDisposal()
    {
        var calls = new List<(string Member, int ThreadId, ApartmentState Apartment)>();
        using var icons = new ShellDesktopIcons(
            () => DesktopView.Open(
                () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(new StubShellView()))),
                _ => { }),
            member => calls.Add((member, Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState())),
            monitors: new StubMonitors());

        int expectedWorkerId = icons.RunOnWorker(() => Environment.CurrentManagedThreadId);

        Exception? mtaError = null;
        var mta = new Thread(() =>
        {
            try
            {
                Assert.Empty(icons.ReadIcons());
                icons.SetPositions(Array.Empty<IconPosition>());
                Assert.False(icons.AutoArrange);
                Assert.False(icons.TryTurnAutoArrangeOff());
            }
            catch (Exception ex)
            {
                mtaError = ex;
            }
        });
        mta.SetApartmentState(ApartmentState.MTA);
        mta.Start();
        Assert.True(mta.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(mtaError);

        Assert.Equal(4, calls.Count);
        Assert.Equal(
            [nameof(IDesktopIcons.ReadIcons), nameof(IDesktopIcons.SetPositions), nameof(IDesktopIcons.AutoArrange), nameof(IDesktopIcons.TryTurnAutoArrangeOff)],
            calls.Select(c => c.Member).ToArray());
        foreach ((_, int threadId, ApartmentState apartment) in calls)
        {
            Assert.Equal(expectedWorkerId, threadId);
            Assert.Equal(ApartmentState.STA, apartment);
        }
    }

    [Fact]
    public void ShellDesktopIconsRejectsNullViewFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new ShellDesktopIcons(null!));
    }

    [Fact]
    public void DisposingShellDesktopIconsStopsWorkerThreadAndRejectsSubsequentCalls()
    {
        var icons = new ShellDesktopIcons(DesktopView.Open);
        Thread workerThread = icons.RunOnWorker(() => Thread.CurrentThread);
        Assert.True(workerThread.IsAlive);

        icons.Dispose();
        icons.Dispose();

        Assert.False(workerThread.IsAlive);
        Assert.Throws<ObjectDisposedException>(() => icons.ReadIcons());
        Assert.Throws<ObjectDisposedException>(() => icons.SetPositions(Array.Empty<IconPosition>()));
        Assert.Throws<ObjectDisposedException>(() => _ = icons.AutoArrange);
        Assert.Throws<ObjectDisposedException>(() => icons.TryTurnAutoArrangeOff());
        Assert.Throws<ObjectDisposedException>(() => icons.RunOnWorker(() => 1));
        Assert.Throws<ObjectDisposedException>(() => icons.RunWithView(_ => 1));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ThrowFromNamedHelper() => throw new InvalidDataException("sta-boom");

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

    private sealed class StubShellView : IShellView, IFolderView
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
        public void GetFolder(in Guid riid, out object ppv) => ppv = new StubShellFolder();
        public void Item(int iItemIndex, out nint ppidl) => ppidl = nint.Zero;
        public void ItemCount(uint uFlags, out int pcItems) => pcItems = 7;
        public void Items(uint uFlags, in Guid riid, out object ppv) => ppv = new EmptyEnumIdList();
        public void GetSelectionMarkedItem(out int piItem) => piItem = 0;
        public void GetFocusedItem(out int piItem) => piItem = 0;
        public void GetItemPosition(nint pidl, out NativePoint ppt) => ppt = default;
        public void GetSpacing(ref NativePoint ppt) { }
        public void GetDefaultSpacing(out NativePoint ppt) => ppt = default;
        public int GetAutoArrange() => ShellConstants.S_FALSE;
        public void SelectItem(int iItem, uint dwFlags) { }
        public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) { }
    }

    private sealed class StubMonitors : IMonitors
    {
        public IReadOnlyList<MonitorInfo> Read() => [new(0, 0, 1920, 1080, 100, Primary: true)];
    }

    private sealed class StubShellFolder : IShellFolder
    {
        public void ParseDisplayName(nint hwnd, nint pbc, string pszDisplayName, nint pchEaten, out nint ppidl, nint pdwAttributes) => ppidl = nint.Zero;
        public int EnumObjects(nint hwnd, uint grfFlags, out IEnumIDList? ppenumIDList) { ppenumIDList = null; return ShellConstants.S_FALSE; }
        public void BindToObject(nint pidl, nint pbc, in Guid riid, out object ppv) => ppv = new object();
        public void BindToStorage(nint pidl, nint pbc, in Guid riid, out object ppv) => ppv = new object();
        public int CompareIDs(nint lParam, nint pidl1, nint pidl2) => 0;
        public void CreateViewObject(nint hwndOwner, in Guid riid, out object ppv) => ppv = new object();
        public void GetAttributesOf(uint cidl, nint[]? apidl, ref uint rgfInOut) { }
        public void GetUIObjectOf(nint hwndOwner, uint cidl, nint[] apidl, in Guid riid, nint rgfReserved, out object ppv) => ppv = new object();
        public void GetDisplayNameOf(nint pidl, uint uFlags, out StrRet pName) => pName = default;
        public void SetNameOf(nint hwnd, nint pidl, string pszName, uint uFlags, out nint ppidlOut) => ppidlOut = nint.Zero;
    }

    private sealed class EmptyEnumIdList : IEnumIDList
    {
        public int Next(uint celt, nint[] rgelt, out uint pceltFetched) { pceltFetched = 0; return ShellConstants.S_FALSE; }
        public int Skip(uint celt) => ShellConstants.S_FALSE;
        public void Reset() { }
        public void Clone(out IEnumIDList ppenum) => ppenum = this;
    }
}
