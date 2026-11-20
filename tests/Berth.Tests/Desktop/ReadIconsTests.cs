using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Berth.Core;
using Berth.Core.Model;
using Berth.Desktop;
using Berth.Desktop.Interop;

namespace Berth.Tests.Desktop;

public sealed class ReadIconsTests
{
    private const uint StrRetWstr = 0u;
    private const uint StrRetOffset = 1u;
    private const uint StrRetCstr = 2u;

    [Fact]
    public void DesktopCoordinatesConvertsPrimaryNegativeXNegativeYAndRoundTrips()
    {
        MonitorInfo[] primaryOnly = [new(0, 0, 2560, 1440, 100, Primary: true)];
        Assert.Equal((1436, 702), DesktopCoordinates.ToDesktop(1436, 702, primaryOnly));
        Assert.Equal((1436, 702), DesktopCoordinates.ToView(1436, 702, primaryOnly));

        MonitorInfo[] negativeX =
        [
            new(-5760, 0, 1920, 1080, 100, Primary: false),
            new(0, 0, 3840, 2160, 150, Primary: true),
        ];
        Assert.Equal((20, 48), DesktopCoordinates.ToDesktop(5780, 48, negativeX));
        Assert.Equal((-5700, 100), DesktopCoordinates.ToDesktop(60, 100, negativeX));
        Assert.Equal((5780, 48), DesktopCoordinates.ToView(20, 48, negativeX));
        Assert.Equal((60, 100), DesktopCoordinates.ToView(-5700, 100, negativeX));

        MonitorInfo[] negativeY =
        [
            new(0, -1080, 1920, 1080, 100, Primary: false),
            new(0, 0, 2560, 1440, 100, Primary: true),
        ];
        Assert.Equal((100, -980), DesktopCoordinates.ToDesktop(100, 100, negativeY));
        Assert.Equal((100, 20), DesktopCoordinates.ToDesktop(100, 1100, negativeY));
        Assert.Equal((100, 100), DesktopCoordinates.ToView(100, -980, negativeY));
        Assert.Equal((100, 1100), DesktopCoordinates.ToView(100, 20, negativeY));

        MonitorInfo[] bothNegative =
        [
            new(-1920, -240, 1920, 1080, 125, Primary: false),
            new(0, 0, 2560, 1440, 100, Primary: true),
        ];
        foreach ((int vx, int vy) in new[] { (0, 0), (50, 300), (2000, 500), (-100, -50) })
        {
            (int dx, int dy) = DesktopCoordinates.ToDesktop(vx, vy, bothNegative);
            Assert.Equal((vx, vy), DesktopCoordinates.ToView(dx, dy, bothNegative));
        }

        Assert.Throws<ArgumentNullException>(() => DesktopCoordinates.ToDesktop(0, 0, null!));
        Assert.Throws<ArgumentException>(() => DesktopCoordinates.ToDesktop(0, 0, []));
        Assert.Throws<ArgumentException>(() => DesktopCoordinates.ToView(0, 0, []));
    }

    [Fact]
    public void ReadIconsAssignsExactSignedCoordinatesIncludingOffScreenAndPreservesRecycleBinId()
    {
        var monitors = new FixedMonitors([
            new(-1920, -240, 1920, 1080, 125, Primary: false),
            new(0, 0, 2560, 1440, 100, Primary: true),
        ]);
        var fixture = new ShellFixture(
        [
            new FixtureItem("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin", new NativePoint { X = 1996, Y = 242 }),
            new FixtureItem(@"C:\Users\Owner\Desktop\LeftMonitor.txt", "LeftMonitor", new NativePoint { X = 120, Y = 40 }),
            new FixtureItem(@"C:\Users\Owner\Desktop\OffScreen.txt", "OffScreen", new NativePoint { X = 9000, Y = 4000 }),
        ]);

        using var icons = fixture.CreateDesktopIcons(monitors);
        IReadOnlyList<IconPosition> result = icons.ReadIcons();

        Assert.Equal(3, result.Count);
        Assert.Equal(new IconPosition("::{645FF040-5081-101B-9F08-00AA002F954E}", "Recycle Bin", 76, 2), result[0]);
        Assert.StartsWith("::{", result[0].Id, StringComparison.Ordinal);
        Assert.Equal(new IconPosition(@"C:\Users\Owner\Desktop\LeftMonitor.txt", "LeftMonitor", -1800, -200), result[1]);
        Assert.Equal(new IconPosition(@"C:\Users\Owner\Desktop\OffScreen.txt", "OffScreen", 7080, 3760), result[2]);
        Assert.Equal((1996, 242), DesktopCoordinates.ToView(76, 2, monitors.Read()));
        Assert.Equal((120, 40), DesktopCoordinates.ToView(-1800, -200, monitors.Read()));
        Assert.Equal((9000, 4000), DesktopCoordinates.ToView(7080, 3760, monitors.Read()));
        fixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 6);
    }

    [Fact]
    public void ReadIconsDistinguishesSameDisplayNameDeduplicatesParsingNameIgnoringCaseAndSanitizesLogWarn()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "Berth.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            BerthPaths paths = BerthPaths.ForRoot(tempRoot);
            var log = new Log(paths, TimeProvider.System);
            var monitors = new FixedMonitors([new(0, 0, 2560, 1440, 100, Primary: true)]);
            const string firstPath = "C:\\Users\\Public\\Desktop\\Notes\u2028\b.txt";
            const string dupPath = "c:\\users\\public\\desktop\\notes\u2028\b.txt";
            var fixture = new ShellFixture(
            [
                new FixtureItem(firstPath, "Notes", new NativePoint { X = 100, Y = 200 }),
                new FixtureItem(@"C:\Users\Owner\Desktop\Notes.txt", "Notes", new NativePoint { X = 300, Y = 400 }),
                new FixtureItem(dupPath, "Notes (dup)", new NativePoint { X = 900, Y = 900 }),
            ]);

            // Construct via the public (IMonitors, Log) constructor so disconnecting Log.Warn forwarding fails this test.
            using var icons = new ShellDesktopIcons(monitors, log);
            IReadOnlyList<IconPosition> result = icons.RunOnWorker(() =>
            {
                using DesktopView desktop = fixture.OpenDesktopView();
                return icons.ReadIconsFrom(desktop, fixture.FreeCoTaskMem, fixture.RecordComRelease);
            });

            Assert.Equal(3, fixture.AllocationEvents);
            Assert.Equal(2, result.Count);
            Assert.Equal(new IconPosition(firstPath, "Notes", 100, 200), result[0]);
            Assert.Equal(new IconPosition(@"C:\Users\Owner\Desktop\Notes.txt", "Notes", 300, 400), result[1]);
            fixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 6);

            string logText = File.ReadAllText(paths.LogFile);
            Assert.DoesNotContain("\u2028", logText, StringComparison.Ordinal);
            Assert.DoesNotContain("\b", logText, StringComparison.Ordinal);
            string[] warnLines = logText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(warnLines);
            Assert.Contains(" WARN ", warnLines[0], StringComparison.Ordinal);
            Assert.Contains(@"c:\users\public\desktop\notes \u0008.txt", warnLines[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DeduplicateKeepsFirstAndWritesOneSanitizedWarning()
    {
        var warnings = new List<string>();
        IconPosition[] items =
        [
            new("C:\\Desktop\\Alpha\u2028.lnk", "Alpha", 10, 20),
            new("c:\\desktop\\alpha\u2028.lnk", "Alpha Second", 99, 99),
        ];

        IReadOnlyList<IconPosition> deduped = ShellDesktopIcons.Deduplicate(items, warnings.Add);

        Assert.Single(deduped);
        Assert.Equal(items[0], deduped[0]);
        Assert.Single(warnings);
        Assert.Contains(@"c:\desktop\alpha .lnk", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadIconsHandlesEmptyEnumerationAndContinuesPastSFalseWithFetchedOne()
    {
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);
        var emptyFixture = new ShellFixture([]);
        using (ShellDesktopIcons emptyIcons = emptyFixture.CreateDesktopIcons(monitors))
        {
            Assert.Empty(emptyIcons.ReadIcons());
        }

        emptyFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 0);

        var twoItemSFalseOnFirstFixture = new ShellFixture(
            [
                new FixtureItem(@"C:\Desktop\First.txt", "First", new NativePoint { X = 40, Y = 80 }),
                new FixtureItem(@"C:\Desktop\Second.txt", "Second", new NativePoint { X = 120, Y = 160 }),
            ],
            returnSFalseOnFirstItem: true);
        using (ShellDesktopIcons icons = twoItemSFalseOnFirstFixture.CreateDesktopIcons(monitors))
        {
            IReadOnlyList<IconPosition> result = icons.ReadIcons();
            Assert.Equal(2, result.Count);
            Assert.Equal(new IconPosition(@"C:\Desktop\First.txt", "First", 40, 80), result[0]);
            Assert.Equal(new IconPosition(@"C:\Desktop\Second.txt", "Second", 120, 160), result[1]);
        }

        twoItemSFalseOnFirstFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 4);
    }

    [Fact]
    public void ReadIconsDoesNotFreeSentinelSlotWhenNextFailsOrReturnsZeroFetched()
    {
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);
        nint sentinel = new(0x123456);
        var freedPointers = new List<nint>();

        var zeroFetchedFixture = new ShellFixture(
            [],
            sentinelOnZeroOrError: sentinel,
            onFreeSpy: freedPointers.Add);
        using (ShellDesktopIcons icons = zeroFetchedFixture.CreateDesktopIcons(monitors))
        {
            Assert.Empty(icons.ReadIcons());
        }

        Assert.DoesNotContain(sentinel, freedPointers);
        zeroFetchedFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 0);

        freedPointers.Clear();
        var errorFixture = new ShellFixture(
            [],
            nextErrorHrAfterItems: unchecked((int)0x80004005),
            sentinelOnZeroOrError: sentinel,
            onFreeSpy: freedPointers.Add);
        using (ShellDesktopIcons icons = errorFixture.CreateDesktopIcons(monitors))
        {
            Assert.Throws<DesktopUnavailableException>(() => icons.ReadIcons());
        }

        Assert.DoesNotContain(sentinel, freedPointers);
        errorFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 0);
    }

    [Fact]
    public void ReadIconsDecodesLongUnicodeWstrOffsetAndCstrThroughNativeStrRetToStrW()
    {
        string longUnicodeParsingName = @"C:\Desktop\" + new string('あ', 290) + ".txt";
        Assert.True(longUnicodeParsingName.Length > 260);
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);
        var fixture = new ShellFixture(
        [
            new FixtureItem(longUnicodeParsingName, "Long Unicode", new NativePoint { X = 10, Y = 20 }, ParsingKind: StrRetWstr, DisplayKind: StrRetWstr),
            new FixtureItem(@"C:\Desktop\OffsetItem.txt", "Offset Display", new NativePoint { X = 30, Y = 40 }, ParsingKind: StrRetOffset, DisplayKind: StrRetCstr),
        ]);

        using var icons = fixture.CreateDesktopIcons(monitors);
        IReadOnlyList<IconPosition> result = icons.ReadIcons();

        Assert.Equal(2, result.Count);
        Assert.Equal(longUnicodeParsingName, result[0].Id);
        Assert.Equal("Long Unicode", result[0].Name);
        Assert.Equal(@"C:\Desktop\OffsetItem.txt", result[1].Id);
        Assert.Equal("Offset Display", result[1].Name);
        fixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 4);
    }

    [Fact]
    public void ReadIconsDoesNotCachePositionsAcrossCalls()
    {
        var monitors = new FixedMonitors([new(-1920, 0, 1920, 1080, 100, Primary: false), new(0, 0, 1920, 1080, 100, Primary: true)]);
        var item = new FixtureItem(@"C:\Desktop\Movable.txt", "Movable", new NativePoint { X = 1940, Y = 50 });
        var fixture = new ShellFixture([item]);
        using var icons = fixture.CreateDesktopIcons(monitors);

        IReadOnlyList<IconPosition> first = icons.ReadIcons();
        Assert.Equal(new IconPosition(@"C:\Desktop\Movable.txt", "Movable", 20, 50), first[0]);

        item.ViewPosition = new NativePoint { X = 240, Y = 300 };
        IReadOnlyList<IconPosition> second = icons.ReadIcons();
        Assert.Equal(new IconPosition(@"C:\Desktop\Movable.txt", "Movable", -1680, 300), second[0]);
        fixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 4);
    }

    [Fact]
    public void ReadIconsReleasesResourcesAndWrapsMidCallFailuresAsDesktopUnavailableException()
    {
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);

        // GetFolder succeeds, Items throws COMException -> folder and view still released exactly once.
        Exception itemsError = Marshal.GetExceptionForHR(unchecked((int)0x800706BA))!;
        var itemsFailFixture = new ShellFixture([], itemsError: itemsError);
        using (ShellDesktopIcons icons = itemsFailFixture.CreateDesktopIcons(monitors))
        {
            DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() => icons.ReadIcons());
            Assert.Same(itemsError, thrown.InnerException);
        }

        itemsFailFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 0);

        // GetItemPosition throws COMException on second item -> PIDLs, strings, and COM objects all released once.
        Exception positionError = Marshal.GetExceptionForHR(unchecked((int)0x800706BA))!;
        var positionFailFixture = new ShellFixture(
        [
            new FixtureItem(@"C:\Desktop\First.txt", "First", new NativePoint { X = 10, Y = 10 }),
            new FixtureItem(@"C:\Desktop\PosFail.txt", "PosFail", new NativePoint { X = 20, Y = 20 }, PositionError: positionError),
        ]);
        using (ShellDesktopIcons icons = positionFailFixture.CreateDesktopIcons(monitors))
        {
            DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() => icons.ReadIcons());
            Assert.Same(positionError, thrown.InnerException);
        }

        positionFailFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 2);

        // Item disappears during GetDisplayNameOf on second item -> FileNotFoundException wrapped, all PIDLs/COM freed.
        var disappearedError = new FileNotFoundException("Icon deleted mid-read");
        var disappearFixture = new ShellFixture(
        [
            new FixtureItem(@"C:\Desktop\First.txt", "First", new NativePoint { X = 10, Y = 10 }),
            new FixtureItem(@"C:\Desktop\Gone.txt", "Gone", new NativePoint { X = 20, Y = 20 }, DisplayNameError: disappearedError),
        ]);
        using (ShellDesktopIcons icons = disappearFixture.CreateDesktopIcons(monitors))
        {
            DesktopUnavailableException thrown = Assert.Throws<DesktopUnavailableException>(() => icons.ReadIcons());
            Assert.Same(disappearedError, thrown.InnerException);
        }

        disappearFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 2);

        // Native StrRetToStrW failure on second item after first succeeded -> DesktopUnavailableException, no leak.
        var badStrRetFixture = new ShellFixture(
        [
            new FixtureItem(@"C:\Desktop\First.txt", "First", new NativePoint { X = 10, Y = 10 }),
            new FixtureItem(@"C:\Desktop\BadStrRet.txt", "Bad", new NativePoint { X = 20, Y = 20 }, ParsingKind: 99u),
        ]);
        using (ShellDesktopIcons icons = badStrRetFixture.CreateDesktopIcons(monitors))
        {
            Assert.Throws<DesktopUnavailableException>(() => icons.ReadIcons());
        }

        badStrRetFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 2);

        // Negative Next HRESULT -> DesktopUnavailableException, no partial list.
        var nextFailFixture = new ShellFixture(
            [new FixtureItem(@"C:\Desktop\First.txt", "First", new NativePoint { X = 10, Y = 10 })],
            nextErrorHrAfterItems: unchecked((int)0x80004005));
        using (ShellDesktopIcons icons = nextFailFixture.CreateDesktopIcons(monitors))
        {
            Assert.Throws<DesktopUnavailableException>(() => icons.ReadIcons());
        }

        nextFailFixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 2);
    }

    [Fact]
    public void ReadIconsTwoHundredReadsOverControlledFixtureDoNotLeakMemory()
    {
        var monitors = new FixedMonitors([new(0, 0, 2560, 1440, 100, Primary: true)]);
        FixtureItem[] items = Enumerable.Range(0, 40)
            .Select(i => new FixtureItem($@"C:\Desktop\Icon_{i:D3}.txt", $"Icon {i:D3}", new NativePoint { X = i * 40, Y = i * 20 }))
            .ToArray();
        var fixture = new ShellFixture(items);
        using var icons = fixture.CreateDesktopIcons(monitors);

        Assert.Equal(40, icons.ReadIcons().Count);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long beforeBytes = Process.GetCurrentProcess().PrivateMemorySize64;

        for (int i = 0; i < 200; i++)
        {
            IReadOnlyList<IconPosition> read = icons.ReadIcons();
            Assert.Equal(40, read.Count);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long afterBytes = Process.GetCurrentProcess().PrivateMemorySize64;
        long deltaBytes = afterBytes - beforeBytes;
        Assert.True(
            deltaBytes <= 5L * 1024 * 1024,
            $"Expected private memory growth <= 5 MB across 200 reads, observed {deltaBytes} bytes.");
        fixture.AssertAllResourcesReleased(expectedOutputStringsFreed: 201 * 40 * 2);
    }

    [Fact]
    public void PublicConstructorRequiresNonNullMonitorsAndLog()
    {
        var monitors = new FixedMonitors([new(0, 0, 1920, 1080, 100, Primary: true)]);
        string tempRoot = Path.Combine(Path.GetTempPath(), "Berth.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var log = new Log(BerthPaths.ForRoot(tempRoot), TimeProvider.System);
            Assert.Throws<ArgumentNullException>(() => new ShellDesktopIcons(null!, log));
            Assert.Throws<ArgumentNullException>(() => new ShellDesktopIcons(monitors, null!));
            using var valid = new ShellDesktopIcons(monitors, log);
            Assert.NotNull(valid);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class FixedMonitors(IReadOnlyList<MonitorInfo> monitors) : IMonitors
    {
        public IReadOnlyList<MonitorInfo> Read() => monitors;
    }

    private sealed class FixtureItem(
        string parsingName,
        string displayName,
        NativePoint viewPosition,
        uint ParsingKind = StrRetWstr,
        uint DisplayKind = StrRetWstr,
        Exception? DisplayNameError = null,
        Exception? PositionError = null)
    {
        public string ParsingName { get; } = parsingName;
        public string DisplayName { get; } = displayName;
        public NativePoint ViewPosition { get; set; } = viewPosition;
        public uint ParsingStrRetKind { get; } = ParsingKind;
        public uint DisplayStrRetKind { get; } = DisplayKind;
        public Exception? GetDisplayNameError { get; } = DisplayNameError;
        public Exception? GetPositionError { get; } = PositionError;
    }

    private sealed class ShellFixture(
        FixtureItem[] items,
        bool returnSFalseOnFirstItem = false,
        Exception? itemsError = null,
        int? nextErrorHrAfterItems = null,
        nint sentinelOnZeroOrError = 0,
        Action<nint>? onFreeSpy = null)
    {
        private readonly FixtureItem[] _items = items;
        private readonly bool _returnSFalseOnFirstItem = returnSFalseOnFirstItem;
        private readonly Exception? _itemsError = itemsError;
        private readonly int? _nextErrorHrAfterItems = nextErrorHrAfterItems;
        private readonly nint _sentinelOnZeroOrError = sentinelOnZeroOrError;
        private readonly Action<nint>? _onFreeSpy = onFreeSpy;
        private readonly Dictionary<nint, FixtureItem> _activePidls = [];
        private readonly Dictionary<object, int> _comReleaseCounts = new(ReferenceEqualityComparer.Instance);
        public int AllocationEvents { get; private set; }
        public int FreeEvents { get; private set; }
        public int OutputStringsFreed { get; private set; }

        public ShellDesktopIcons CreateDesktopIcons(IMonitors monitors, Action<string>? warn = null) =>
            new(
                openView: OpenDesktopView,
                monitors: monitors,
                warn: warn,
                freeCoTaskMem: FreeCoTaskMem,
                releaseComObject: RecordComRelease);

        public DesktopView OpenDesktopView()
        {
            var view = new FakeFolderView(this);
            TrackComObject(view);
            return DesktopView.Open(
                () => new StubShellWindows(new StubServiceProvider(new StubShellBrowser(view))),
                RecordComRelease);
        }

        public void FreeCoTaskMem(nint ptr)
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
        }

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

        public void RecordComRelease(object comObj)
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
            AllocationEvents++;
            return ptr;
        }

        private static StrRet BuildStrRet(string value, uint kind)
        {
            var ret = new StrRet { uType = kind };
            if (kind == StrRetWstr)
            {
                ret.pOleStr = Marshal.StringToCoTaskMemUni(value);
            }
            else if (kind == StrRetOffset)
            {
                ret.uOffset = 4u;
            }
            else if (kind == StrRetCstr)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(value);
                for (int i = 0; i < Math.Min(bytes.Length, 259); i++)
                {
                    ret.cStr[i] = bytes[i];
                }

                ret.cStr[Math.Min(bytes.Length, 259)] = 0;
            }

            return ret;
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

            public void GetItemPosition(nint pidl, out NativePoint ppt)
            {
                FixtureItem item = owner._activePidls[pidl];
                if (item.GetPositionError is not null)
                {
                    throw item.GetPositionError;
                }

                ppt = item.ViewPosition;
            }

            public void GetSpacing(ref NativePoint ppt) { }
            public void GetDefaultSpacing(out NativePoint ppt) => ppt = default;
            public int GetAutoArrange() => ShellConstants.S_FALSE;
            public void SelectItem(int iItem, uint dwFlags) { }
            public void SelectAndPositionItems(uint cidl, nint[] apidl, NativePoint[]? apt, uint dwFlags) { }
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
                FixtureItem item = owner._activePidls[pidl];
                if (item.GetDisplayNameError is not null)
                {
                    throw item.GetDisplayNameError;
                }

                if (uFlags == ShellConstants.SHGDN_FORPARSING)
                {
                    pName = BuildStrRet(item.ParsingName, item.ParsingStrRetKind);
                }
                else if (uFlags == ShellConstants.SHGDN_NORMAL)
                {
                    pName = BuildStrRet(item.DisplayName, item.DisplayStrRetKind);
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(uFlags), uFlags, null);
                }
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
                if (owner._returnSFalseOnFirstItem && _index == 1)
                {
                    return ShellConstants.S_FALSE;
                }

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
