---
schema_version: 1
id: desktop-access
domain: 02-desktop
status: active
title: "TODO-01 -- Desktop access"
depends_on: ["layout-core"]
---

# TODO-01 -- Desktop access

> **Goal:** Berth reads every desktop icon's identity and position, sets positions, reads and clears auto arrange, and reads the monitors, through the documented shell interfaces, proven on the real desktop with an icon the test itself creates.

> **Current state:** Measured 2026-11-20. `D01 T01 §8` declares `IDesktopIcons` and `IMonitors` and their fakes. `D02 T01 §1` (shell COM declarations in `src/Berth/Desktop/Interop/`), `D02 T01 §2` (`DesktopView` in `src/Berth/Desktop/DesktopView.cs`, `[StaFact]` in `tests/Berth.Tests/Sta.cs`), `D02 T01 §9` (`StaWorker` in `src/Berth/Desktop/StaWorker.cs` and `ShellDesktopIcons` in `src/Berth/Desktop/ShellDesktopIcons.cs`), `D02 T01 §5` (`AutoArrange` and `TryTurnAutoArrangeOff`), `D02 T01 §6` (`SystemMonitors` in `src/Berth/Desktop/SystemMonitors.cs`), and `D02 T01 §3` (`DesktopCoordinates` and `ShellDesktopIcons.ReadIcons`) are stamped. `D02 T01 §4` (`ShellDesktopIcons.SetPositions` and `SVSI_NOSTATECHANGE`) is implemented and pending verification and stamping. `ScopedDesktopIcons` (`§10`) is not implemented yet. The interface chain is written in `docs/plans/2026-11-15-berth-foundation.md` under "How icon positions are read and written".

## Inputs

- `docs/plans/2026-11-15-berth-foundation.md`: "How icon positions are read and written", "Seams that make it testable".
- `design/SPEC.md` §3 (physical pixels of the virtual desktop), §9 (auto arrange), §15 (reading the desktop), §18 (the unreachable desktop).
- Microsoft's documentation of `IFolderView`, `IFolderView2`, `IShellWindows::FindWindowSW` and `IShellFolder::GetDisplayNameOf`: read it for the installed SDK before declaring an interface, and cite the page in the file's header comment.
- -> XREF: D00 T01 §2 -- the application project and its manifest (per-monitor DPI awareness)
- -> XREF: D01 T01 §8 -- the `IDesktopIcons` and `IMonitors` seams implemented here
- -> XREF: D03 T01 §3 -- the commands run against `ShellDesktopIcons`
- -> XREF: D05 T01 §7 -- the auto arrange notice calls `TryTurnAutoArrangeOff`
- -> XREF: D06 T01 §2 -- the automatic restore reads the monitors after a display change
- -> XREF: D99 T01 §4 -- operator opt-in auto arrange round trip on a disposable desktop

## Outcome

- `ShellDesktopIcons` and `SystemMonitors` implement the seams against Windows, and a test moves its own probe icon on the real desktop and reads the new position back.
- A desktop that cannot be reached is one exception type, never a raw COM error.

**Adjacency:** all=not-applicable (interop layer: it holds no records, settings or surface of its own)

**Adjacency rationale:** This file is the Windows side of two seams. The records, their undo and their wording belong to `D01 T01`.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Shell COM interface declarations | D00 T01 §2 | [x] |
| 2 | §2 | The desktop's folder view | §1 | [x] |
| 3 | §9 | STA worker that owns every shell call | §2 | [x] |
| 4 | §3 | Reading every icon's identity and position | §9, §6, D01 T01 §8 | [x] |
| 5 | §4 | Setting icon positions | §3 | [ ] |
| 6 | §5 | Auto arrange read and clear | §9 | [x] |
| 7 | §6 | Monitor bounds, scale and primary | D01 T01 §8 | [x] |
| 8 | §10 | Desktop scoped to icons with a name prefix | D01 T01 §8 | [ ] |
| 9 | §7 | Real-desktop round trip on a probe icon | §4, §5, §6, §10, D01 T01 §11 | [ ] |
| 10 | §8 | One exception for an unreachable desktop | §3, §4, §5 | [ ] |

## 1. Shell COM interface declarations

> **Started:** 2026-11-19T07:17:43Z

The interop declarations, written from Microsoft's documentation and checked by a test that reads their identifiers.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Declare the interfaces and supporting native structs in `src/Berth/Desktop/Interop/`. Done when: they compile under classic `[ComImport]` (`InterfaceIsDual` for `IShellWindows`, `InterfaceIsIUnknown` for the other seven with flattened base slots) with zero warnings.
2. Check the identifiers, vtable slots, SDK-derived method signatures, `PreserveSig` attributes, struct offsets/layouts, and constants. Done when: `InteropDeclarationTests` and `LayerRuleTests` verify every GUID, vtable order, parameter/marshalling signature, struct field/offset/marshalled layout, and constant.

- [x] Declare `IShellWindows` (slots 7..17 under `InterfaceIsDual`, with `[PreserveSig] int FindWindowSW([In, MarshalAs(UnmanagedType.Struct)] ref object? pvarLoc, [In, MarshalAs(UnmanagedType.Struct)] ref object? pvarLocRoot, int swClass, out int phwnd, int swfwOptions, [MarshalAs(UnmanagedType.IDispatch)] out object? ppdispOut)` at index 8 / vtable slot 15), `IServiceProvider` (`InterfaceIsIUnknown`, `QueryService(in Guid, in Guid, [MarshalAs(UnmanagedType.Interface)] out object?)`), `IShellBrowser` (flattening `IOleWindow` slots through `QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView)` at index 12 / vtable slot 15 and remaining methods through `SetToolbarItems`), `IShellView` (flattening `IOleWindow` slots through `GetItemObject`), `IFolderView` (all 14 slots including `[PreserveSig] int GetAutoArrange()` and `SelectAndPositionItems` with `LPArray` `SizeParamIndex = 0`), `IFolderView2` (flattening all 14 `IFolderView` slots verbatim without C# interface inheritance through `SetCurrentFolderFlags` at index 21 / vtable slot 24 and `GetCurrentFolderFlags` at index 22 / vtable slot 25), `IShellFolder` (`[PreserveSig] int CompareIDs(...)` and `GetDisplayNameOf` at index 8 / vtable slot 11), and `IEnumIDList` (`[PreserveSig] int Next(...)`, `[PreserveSig] int Skip(...)`, `Reset`, `Clone`) in `src/Berth/Desktop/Interop/`, one interface file each under classic `[ComImport]`, plus `NativePoint` (`int X` at offset `0`, `int Y` at offset `4`, size `8`), `[InlineArray(260)]` `StrRetBuffer` (`260` bytes), and x64 `StrRet` (`uint uType` at offset `0`; overlapping union `nint pOleStr`, `uint uOffset`, `StrRetBuffer cStr` at offset `8`; size `272`) in `src/Berth/Desktop/Interop/ShellTypes.cs`, each naming its Windows SDK `10.0.26100.0` header and Microsoft Learn page in a header comment. **Corrected 2026-11-19:** committed to classic `[ComImport]` (`IDispatch`/`VARIANT`), explicit base-slot flattening, `[PreserveSig]` on `IShellWindows.FindWindowSW` (`S_FALSE` when desktop missing, `plan review P2`) plus `GetAutoArrange`/`Next`/`Skip`/`CompareIDs`, and explicit `StrRetBuffer` inline union layout (`plan review P1`). Done when: `dotnet build Berth.slnx -c Release` exits 0 with warnings as errors. Cheaper substitute: `dynamic` late binding through `Shell.Application`, which cannot reach `IFolderView`.
- [x] Add `ShellConstants` in `src/Berth/Desktop/Interop/ShellConstants.cs` with `S_OK`, `S_FALSE`, `CLSID_ShellWindows`, `SID_STopLevelBrowser`, `CSIDL_DESKTOP`, `SWC_DESKTOP`, `SWFO_NEEDDISPATCH`, `SVGIO_SELECTION`, `SVGIO_ALLVIEW`, `SVSI_POSITIONITEM`, `SHGDN_FORPARSING`, `SHGDN_NORMAL`, and `FWF_AUTOARRANGE`, each with a comment naming its Windows SDK header and Microsoft Learn source. **Corrected 2026-11-19:** added `SVGIO_SELECTION` (`§4`) and `S_OK`/`S_FALSE` (`plan review P2`). Done when: the file compiles and every constant has a doc comment naming where the value is documented. Cheaper substitute: numbers inline at each call.
- [x] Add `tests/Berth.Tests/Desktop/InteropDeclarationTests.cs` asserting `[ComImport]`, exact `[InterfaceType]`, independent GUID equality, and empty `GetInterfaces()` across all 8 interfaces; `MetadataToken`-ordered method names and target vtable slot indices across all 8 interfaces; full SDK-derived signature verification (return type, `PreserveSig`, return `MarshalAs`, parameter types/by-ref depth, `IsIn`/`IsOut`, parameter `MarshalAs` + `SizeParamIndex`) for all called methods across `IShellWindows`, `IServiceProvider`, `IShellBrowser`, `IFolderView`, flattened `IFolderView2`, `IShellFolder`, and `IEnumIDList`; field types, offsets, sizes, and `Marshal.StructureToPtr` layouts for `NativePoint` (`X`@0, `Y`@4, size `8`), `StrRetBuffer` (`260` bytes), and `StrRet` (`uType`@0, union `pOleStr`/`uOffset`/`cStr`@8, size `272`); and all `ShellConstants` values typed independently. **Corrected 2026-11-19:** added SDK-derived method signature assertions across all called methods including flattened `IFolderView2` and complete field-offset/marshalling checks on `NativePoint`/`StrRet` (`plan review P1`). Done when: all tests pass, and fail when two `IFolderView` methods are swapped, a by-ref parameter becomes by-value, `SelectAndPositionItems` loses its `LPArray`/`SizeParamIndex`, `NativePoint.X`/`.Y` are swapped or a `StrRet` union member is misplaced, or any GUID/constant differs. Cheaper substitute: trusting the declaration because it compiles.
- [x] Commit: `desktop: shell COM interface declarations`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*InteropDeclarationTests" --filter-class "*LayerRuleTests"` passes, and fails when two `IFolderView` methods are swapped, a `FindWindowSW` by-ref `VARIANT*` parameter becomes by-value, `IFolderView2.SelectAndPositionItems` loses its `LPArray` `SizeParamIndex`, `NativePoint` coordinates are swapped or `StrRet` union offsets shift, or a `ShellConstants`/GUID value is altered.

> **Verified:** 2026-11-19 | §1 | 2884581f21e891313ab77100574e6875a46304e7
> **Review:** `docs/reviews/d02-t01-s1.json`

## 2. The desktop's folder view

> **Started:** 2026-11-19T09:09:50Z

The chain from `ShellWindows` to the desktop's `IFolderView`, in one method with every COM object released.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `DesktopView` in `src/Berth/Desktop/DesktopView.cs`. Done when: it returns a live `IFolderView` on the build machine.
2. Release what it acquires. Done when: a loop of 500 acquisitions does not grow the process handle count.

- [x] Add `DesktopView.Open()` returning a disposable holder of `IFolderView` (and `IFolderView2` when the view supports it): create `ShellWindows`, call `FindWindowSW` with `CSIDL_DESKTOP`, `SWC_DESKTOP` and `SWFO_NEEDDISPATCH`, query `IServiceProvider` for `SID_STopLevelBrowser` as `IShellBrowser`, call `QueryActiveShellView`, and cast to `IFolderView` (foundation plan). **Corrected 2026-11-19:** expose `IFolderView View` and `IFolderView2? View2`; record and assert `CSIDL_DESKTOP`, null `pvarLocRoot`, `SWC_DESKTOP`, `SWFO_NEEDDISPATCH`, `SID_STopLevelBrowser`, and `typeof(IShellBrowser).GUID` on the injected shell seam (`plan review P1`); translate `FindWindowSW` non-`S_OK` (including `S_FALSE` and negative HRESULTs such as `E_NOINTERFACE`), null intermediate/view results, and `COMException` / `InvalidCastException` / `InvalidComObjectException` / `UnauthorizedAccessException` (`E_ACCESSDENIED`) into `DesktopUnavailableException` preserving `InnerException` and `HResult` (`plan review P2`). Done when: a test on the real desktop calls `Open()` and `ItemCount(SVGIO_ALLVIEW, out int count)` returns a number at or above 0 with `View2` callable, while unit tests verify both optional `IFolderView2` outcomes (`View2` callable vs `View` usable with `View2 == null`), exact shell arguments, `S_FALSE`, `E_NOINTERFACE`, `UnauthorizedAccessException`, null results, and COM errors. Cheaper substitute: `FindWindow("Progman")` and list-view messages, which the README forbids.
- [x] Release every intermediate COM object (`Marshal.ReleaseComObject` or the generated wrappers' disposal) on success and on failure. **Corrected 2026-11-19:** use `Marshal.ReleaseComObject` (releasing one reference per acquired slot without zeroing a shared RCW's count or skipping aliased interfaces, `plan review P1`) rather than `FinalReleaseComObject`, releasing `shellWindows`, `dispatch`, and `shellBrowser` before returning on success, releasing `shellView` once idempotently in `Dispose()`, and releasing each intermediate before the next step and any remaining acquired object in finally on failure; verify a 500x real-desktop handle-count loop (`process.Refresh()`, delta `<= 50`), a live concurrent two-holder test (disposing holder 1 leaves holder 2 callable), plus unit tests over `DesktopView.Open(Func<object?>, Action<object>?)` covering a success-handoff trace, aliased interfaces, concurrent holders, every failure step, and double `Dispose()`. Done when: all handle-count, concurrency, success-handoff, failure-step, and `Dispose()` assertions pass. Cheaper substitute: leaving release to the finaliser, which a long-running tray app pays for.
- [x] Require an STA thread (production code reaches `DesktopView` only through the `StaWorker` of §9): `DesktopView.Open` asserts `Thread.CurrentThread.GetApartmentState() == ApartmentState.STA` before COM activation and throws `InvalidOperationException` naming `STA` on MTA threads with `0` factory invocations, and the test host runs `[StaFact]` tests on a dedicated `ApartmentState.STA` thread via `ISelfExecutingXunitTestCase` installing `DispatcherSynchronizationContext`, pumping a `DispatcherFrame` until completion, propagating exceptions, and calling `dispatcher.InvokeShutdown()` in `finally` on both success and failure (`tests/Berth.Tests/Sta.cs`, alongside `Sta.HasInteractiveDesktopSession` checking `Environment.UserInteractive && Process.GetCurrentProcess().SessionId != 0`). **Corrected 2026-11-19:** added `DispatcherFrame` pumping, `finally` shutdown, same-`ManagedThreadId` continuation check across `await Task.Yield()`, and zero-call MTA factory check (`plan review P2`). Done when: calling `Open` from an explicit MTA thread throws `InvalidOperationException` naming `STA` with `0` activation calls, `[StaFact]` runs synchronous and post-await continuations on the same `STA` thread ID, and `Sta.RunAsync` propagates failures and shuts down its `Dispatcher` on both outcomes. Cheaper substitute: silently working on some threads and failing on others.
- [x] Add `tests/Berth.Tests/Desktop/DesktopViewTests.cs` with real-desktop tests in `[Collection(DesktopCollectionDefinition.Name)]` marked `[Trait("Category", "RealDesktop")]` (skipping only when `!Sta.HasInteractiveDesktopSession` so non-interactive Session 0 CI runners pass while interactive hosts report `0` skipped) plus unit tests for `IFolderView2` present/absent, exact `FindWindowSW`/`QueryService` arguments, MTA zero-factory rejection, `DesktopUnavailableException` mapping (including `UnauthorizedAccessException` and `E_NOINTERFACE`), success/aliased/concurrent/failure release tracking, and `StaFact`/`Dispatcher` pumping. **Corrected 2026-11-19:** added `DesktopCollectionDefinition.Name` serialization (`AGENTS.md`, 2026-11-18) and `astra` plan-review cases. Done when: all cases pass with `0` skipped on the build machine. Cheaper substitute: a test that is skipped everywhere.
- [x] Commit: `desktop: the desktop's folder view`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*DesktopViewTests" --filter-class "*LayerRuleTests"` passes with `0` skipped on a machine with a desktop session, and fails when `SWC_DESKTOP` is replaced by another window class constant, when the STA check is removed, or when `Marshal.ReleaseComObject` is replaced by `Marshal.FinalReleaseComObject`.

> **Verified:** 2026-11-19 | §2 | a7f3567cbc7d93453b58dbb62b5e579cdfb0ec1c
> **Review:** `docs/reviews/d02-t01-s2.json`

## 3. Reading every icon's identity and position

> **Started:** 2026-11-20T11:24Z
> **Deferred:** live-desktop read-back on a test-created probe icon (tests never read the owner's icons per `AGENTS.md` and foundation plan) -> XREF: D02 T01 §7

`ShellDesktopIcons.ReadIcons` returns each icon's parsing name, display name and position.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `DesktopCoordinates` in `src/Berth/Desktop/DesktopCoordinates.cs` (grounded in the live multi-monitor measurement) and implement `IDesktopIcons.ReadIcons` in `src/Berth/Desktop/ShellDesktopIcons.cs` via its `StaWorker`, `DesktopView`, `IMonitors`, and `Log`. Done when: controlled shell-fixture tests verify exact signed desktop coordinates, `StrRetToStrW` decoding across all `STRRET` forms, deduplication, and freshness across moves.
2. Free every PIDL, `StrRetToStrW` output buffer, `IShellFolder`, and `IEnumIDList` deterministically in `finally`, and verify with memory and exact-release tests. Done when: the 200-read controlled-fixture memory test and exact free-count unit tests pass on both success and failure.

- [x] Implement `ShellDesktopIcons.ReadIcons()` (`src/Berth/Desktop/ShellDesktopIcons.cs`, exposing `public ShellDesktopIcons(IMonitors monitors, Log log)` so production composition always wires its logger and monitor reader) routed through `StaWorker` and `DesktopView`: read `monitors = _monitors.Read()`, get the view's `IShellFolder` with `IFolderView.GetFolder(typeof(IShellFolder).GUID, ...)`, enumerate with `IFolderView.Items(SVGIO_ALLVIEW, typeof(IEnumIDList).GUID, ...)` as `IEnumIDList`, loop `Next(1, ...)` until `fetched == 0` (throwing `DesktopUnavailableException` on negative HRESULT and honoring `fetched == 1` even if `S_FALSE`), and for each item read `GetItemPosition`, parsing name (`SHGDN_FORPARSING`), and display name (`SHGDN_NORMAL`), converting `STRRET` via `shlwapi.dll` `StrRetToStrW(ref StrRet, pidl, out nint ppsz)` (respecting `StrRetToStrW`'s ownership transfer of `STRRET` without double-freeing `pOleStr`, freeing `ppsz` via `Marshal.FreeCoTaskMem` in `finally`, and wrapping failing HRESULTs as `DesktopUnavailableException`), and converting view coordinates to virtual-desktop coordinates via `DesktopCoordinates.ToDesktop`. Wrap `COMException`, `InvalidCastException`, `InvalidComObjectException`, `UnauthorizedAccessException`, `FileNotFoundException`, and `DirectoryNotFoundException` from mid-enumeration calls into `DesktopUnavailableException` preserving `InnerException`. **Corrected 2026-11-20:** resolved all 7 `astra` plan-review findings: (1) moved whole-desktop enumeration and 200-read checks onto a controlled shell fixture without reading the owner's icons, with live probe proof deferred to `D02 T01 §7`; (2) added 3-item duplicate-display-name + case-insensitive duplicate-parsing-name + empty-enumeration tests; (3) drove public `ReadIcons` with distinct per-item points (including off-screen) and nonzero monitor origin asserting exact signed coordinates plus independent `ToView` expectations; (4) followed `StrRetToStrW` ownership transfer (no `pOleStr` double-free) and tested real `StrRetToStrW` over >260-char Unicode `WSTR`, `OFFSET`, `CSTR`, and mid-loop conversion failure; (5) tested `GetFolder`-succeeds/`Items`-fails cleanup and `FileNotFoundException` during `GetDisplayNameOf` -> `DesktopUnavailableException`; (6) made `public ShellDesktopIcons(IMonitors, Log)` require `Log`; and (7) replaced live two-read equality with a controlled two-read freshness test across an icon move. Done when: controlled-fixture unit tests pass for all cases above and `StaWorkerTests` passes with stub `IShellFolder`/`IEnumIDList`/`IMonitors`. Cheaper substitute: using the display name as `Id` (SPEC §15 "Matching").
- [x] Free every item identifier list and `StrRetToStrW` output string with `Marshal.FreeCoTaskMem` in `finally`, and release `IShellFolder` and `IEnumIDList` with `Marshal.ReleaseComObject` in `finally` on both success and partial/mid-loop failure (including `GetFolder` succeeding and `Items` failing). **Corrected 2026-11-20:** added `GetFolder`-succeeds/`Items`-fails release test and 200-read controlled-fixture memory test with real `AllocCoTaskMem` PIDLs and native `StrRetToStrW` (`plan review P1/P2`). Done when: a 200-read controlled-fixture test using real `AllocCoTaskMem` PIDLs and `StrRetToStrW` stays within 5 MB private memory (after GC), and unit tests assert `freeCount == allocCount` and exact COM release counts on success, `Items` failure, `GetItemPosition` failure, `GetDisplayNameOf` `FileNotFoundException`, and `StrRetToStrW` failure. Cheaper substitute: leaking one allocation per icon per read, which a 5 second poll turns into a real leak.
- [x] Add `DesktopCoordinates` in `src/Berth/Desktop/DesktopCoordinates.cs` with `ToDesktop(int viewX, int viewY, IReadOnlyList<MonitorInfo> monitors)` and its exact inverse `ToView(int desktopX, int desktopY, IReadOnlyList<MonitorInfo> monitors)`, converting between the folder view's coordinates (origin at `(min(m.X), min(m.Y))`, measured 2026-11-20 on this Windows 11 multi-monitor host: `VirtualScreen=(-5760,0) 9600x2160`, `ItemCount=128`, `ViewPos X=[5780..9476] Y=[2..1926]` -> `Desktop X=[20..3716]` inside primary `[0..3840)`) and physical pixels of the virtual desktop (SPEC §15 "Positions"), recording that measurement and date in the file's header comment. `ReadIcons` returns desktop coordinates. Done when: unit tests cover primary-only, negative `X`, negative `Y`, both negative `X` and `Y`, null/empty monitor rejection, independent expected `ToDesktop`/`ToView` literal values, round-trip `ToView(ToDesktop(p)) == p`, and public `ReadIcons` exact signed coordinates (including an off-screen point) on a nonzero monitor origin. Cheaper substitute: storing view coordinates and converting only when drawing, so Core's monitor numbering and off-screen checks disagree with the map.
- [x] When two items report the same parsing name ignoring case, keep the first and write one `WARN` log line naming it (`Log.Warn` via the required `Log` parameter / internal `Deduplicate(IReadOnlyList<IconPosition>, Action<string>?)`). Done when: unit tests over both `Deduplicate` and public `ShellDesktopIcons(monitors, log).ReadIcons()` with a real `Log` on a temp `BerthPaths.ForRoot` and a 3-item fixture (two distinct parsing names sharing one display name plus one case-variant duplicate of the first parsing name) assert all 3 items visited, 2 icons returned with the first duplicate's fields preserved, and 1 `WARN` line written to `logs\berth.log` naming the duplicate parsing name. Cheaper substitute: returning duplicates, which `LayoutStore` refuses to load back (`D01 T01 §5`).
- [x] Add `tests/Berth.Tests/Desktop/ReadIconsTests.cs` covering: exact signed per-item coordinates (including off-screen) through public `ReadIcons`; 3-item duplicate-display-name + duplicate-parsing-name + `Log.Warn` integration; empty enumeration; Recycle Bin `::{...}` parsing-name preservation; two-read freshness across an icon move; real native `StrRetToStrW` across >260-char Unicode `WSTR`, `OFFSET`, `CSTR`, and mid-loop failure; `GetFolder`-succeeds/`Items`-fails cleanup; `FileNotFoundException` during `GetDisplayNameOf` -> `DesktopUnavailableException`; negative `Next` HRESULT -> `DesktopUnavailableException`; `S_FALSE` with `fetched == 1`; 200-read controlled-fixture memory leak check; and `DesktopCoordinates` unit tests. **Corrected 2026-11-20:** aligned all tests with `AGENTS.md` (zero reads of owner icons; live probe read-back owned by `D02 T01 §7`). Done when: all pass with `0` skipped on the build machine, and fail when enumeration stops one item early, when `DesktopCoordinates` omits the origin offset, when display names are used for deduplication, or when `GetFolder` leaks on `Items` failure. Cheaper substitute: asserting only that the list is not null.
- [x] Commit: `desktop: reading every icon's identity and position`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*ReadIconsTests" --filter-class "*StaWorkerTests" --filter-class "*LayerRuleTests"` passes with `0` skipped, and fails when the enumeration loop drops the last item, when `DesktopCoordinates` omits the virtual-screen origin offset, when display names are used as `Id`, or when duplicate parsing names are not deduplicated and logged.

> **Verified:** 2026-11-20 | §3 | 6e12dda58a674ca2c172d6086f0b4196d2185ab3
> **Review:** `docs/reviews/d02-t01-s3.json`

## 4. Setting icon positions

> **Started:** 2026-11-20T11:54Z
> **Deferred:** live-desktop move and selection-preservation proof on a test-created probe icon (`AGENTS.md` forbids touching owner icons) -> XREF: D02 T01 §7

`ShellDesktopIcons.SetPositions` moves the icons it is given and nothing else.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Implement `ShellDesktopIcons.SetPositions` (`src/Berth/Desktop/ShellDesktopIcons.cs`) via `StaWorker`, `DesktopView`, `IMonitors`, and `DesktopCoordinates.ToView` with one `SelectAndPositionItems` call. Done when: controlled-fixture unit tests verify batched matching, view-coordinate conversion, `SVSI_POSITIONITEM`-only flags, and skipping unknown `Id`s.
2. Free every PIDL, `StrRetToStrW` buffer, `IShellFolder`, and `IEnumIDList` deterministically in `finally` on both success and failure, and update `StaWorkerTests`. Done when: `SetPositionsTests` and `StaWorkerTests` pass with `0` skipped and no real-desktop `SetPositions` call exists outside `ProbeIconTests.cs`.

- [x] Implement `ShellDesktopIcons.SetPositions(IReadOnlyList<IconPosition> icons)` (`src/Berth/Desktop/ShellDesktopIcons.cs`) routed through `StaWorker` and `DesktopView`: validate `ArgumentNullException.ThrowIfNull(icons)`, open `DesktopView` even for an empty request (so an unreachable desktop throws `DesktopUnavailableException`, returning without calling `SelectAndPositionItems`), read `monitors = _monitors.Read()`, enumerate desktop items once via `GetFolder` + `Items(SVGIO_ALLVIEW)` + `IEnumIDList.Next(1, ...)` (checking `hr < 0` and `fetched == 0` before owning `batch[0]`), build a case-insensitive map from parsing name (`SHGDN_FORPARSING` via `StrRetToStrW`, first occurrence wins) to PIDL, keep all PIDLs alive until `finally` after `SelectAndPositionItems`, match requested icons (deduplicating repeated requested `Id`s ignoring case so no PIDL is passed twice), convert each matched position via `DesktopCoordinates.ToView`, and when at least one icon matched call `IFolderView.SelectAndPositionItems` once with the matched PIDLs, converted `NativePoint`s, and `ShellConstants.SVSI_POSITIONITEM | ShellConstants.SVSI_NOSTATECHANGE`. **Corrected 2026-11-20:** resolved all 6 `astra` plan-review findings: (1) added `SVSI_NOSTATECHANGE` (`0x80000000u`) to `ShellConstants` and passed `SVSI_POSITIONITEM | SVSI_NOSTATECHANGE` to preserve selection and focus; (2) verified every submitted PIDL is still live/caller-owned during `SelectAndPositionItems` and released exactly once afterward; (3) tested out-of-order case-variant requests with duplicate desktop PIDLs (first wins) and repeated case-variant requests; (4) tested empty request on an unreachable desktop (factory invoked, `DesktopUnavailableException` thrown) and zero `SelectAndPositionItems` calls on reachable empty/all-unknown inputs; (5) assigned mixed known/unknown `Id` proof for `ShellDesktopIcons` to `SetPositionsTests` (with `§7` proving `ScopedDesktopIcons` + probe move + `SVGIO_SELECTION` preservation); and (6) broadened safety grep across all of `tests/` (`new ShellDesktopIcons|SetPositions`). Done when: controlled-fixture unit tests verify all cases above and `StaWorkerTests` passes. Cheaper substitute: one call per icon, which is slow and repaints the desktop each time.
- [x] Pass `ShellConstants.SVSI_POSITIONITEM | ShellConstants.SVSI_NOSTATECHANGE` (`0x80000080u`) to `SelectAndPositionItems`, so the user's selection and focus on the desktop are not changed. Done when: `InteropDeclarationTests` asserts `SVSI_NOSTATECHANGE == 0x80000000u` and controlled-fixture unit tests assert `dwFlags == (ShellConstants.SVSI_POSITIONITEM | ShellConstants.SVSI_NOSTATECHANGE)` (real-desktop `SVGIO_SELECTION` preservation is proven on the probe icon in `D02 T01 §7`). Cheaper substitute: adding `SVSI_SELECT`, which deselects what the user had selected, or omitting `SVSI_NOSTATECHANGE`.
- [x] Ignore, without error, an `IconPosition` whose `Id` is not on the desktop (and make zero `SelectAndPositionItems` calls when `icons` is empty or has zero matches). Done when: controlled-fixture unit tests pass an unknown `Id` alongside matched icons and also pass only unknown `Id`s / an empty list, asserting success and exact `SelectAndPositionItems` call counts (`0` when unmatched/empty, `1` when matched; `D02 T01 §7` proves `ScopedDesktopIcons` + the live probe move). Cheaper substitute: throwing, which would fail every restore with a deleted file.
- [x] Free every item identifier list and `StrRetToStrW` output string with `Marshal.FreeCoTaskMem` in `finally` (keeping every PIDL live while `SelectAndPositionItems` runs), and release `IShellFolder` and `IEnumIDList` with `Marshal.ReleaseComObject` in `finally`, on both success and failure (including `Items` failure, `Next` negative HRESULT with sentinel slot, `GetDisplayNameOf` `FileNotFoundException`/`COMException`, and `SelectAndPositionItems` throwing `COMException` wrapped in `DesktopUnavailableException` preserving `InnerException`). **Corrected 2026-11-20:** added live-during-call PIDL ownership check, exact COM release (`== 1` per object), `StrRetToStrW` output string free count, sentinel non-free on `Next` failure, and `DesktopUnavailableException` wrapping assertions. Done when: unit tests in `tests/Berth.Tests/Desktop/SetPositionsTests.cs` assert every submitted PIDL is live inside `SelectAndPositionItems`, `FreeEvents == AllocationEvents`, exact `OutputStringsFreed`, and per-object COM release count `== 1` across every success and failure path. Cheaper substitute: leaking per restore or freeing PIDLs before `SelectAndPositionItems`.
- [x] Do not call `SetPositions` (directly or through `LayoutService.Restore`/`Undo`) against a real-desktop `ShellDesktopIcons` in this section: the only real write is §7's, on its own probe icon. Done when: `git grep -n -E "new ShellDesktopIcons|SetPositions" tests/` shows no real-desktop write anywhere in `tests/` until `ProbeIconTests.cs` lands in `D02 T01 §7`. Cheaper substitute: an "identity write" of every icon's current position, which reverses any move the owner makes between the read and the write (`AGENTS.md`).
- [ ] Commit: `desktop: setting icon positions`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*SetPositionsTests" --filter-class "*StaWorkerTests" --filter-class "*InteropDeclarationTests" --filter-class "*LayerRuleTests"` passes with `0` skipped, and fails when an unknown `Id` throws, when `dwFlags` is not `SVSI_POSITIONITEM | SVSI_NOSTATECHANGE`, when `DesktopCoordinates.ToView` is omitted, when `SelectAndPositionItems` is called per icon instead of once, when a PIDL is freed before `SelectAndPositionItems` or leaked on failure, or when an empty request skips opening the view; the real-desktop proof is §7's `ProbeIconTests`.

## 5. Auto arrange read and clear

> **Started:** 2026-11-20T10:41Z

**Corrected 2026-11-20:** re-planned with `todo-graph.py replan`; the build first began at 2026-11-20T10:24:00Z; its review chain is kept as `docs/reviews/02-desktop/D02-T01-s5-chain1.md`.
> **Deferred:** real Explorer on/off/on auto arrange round trip on a disposable Windows 11 desktop (`BERTH_TEST_ALLOW_AUTOARRANGE=1`) -> XREF: D99 T01 §4

Berth detects auto arrange and can turn it off on the user's click (SPEC §9).

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Implement `AutoArrange` through `IFolderView2.GetCurrentFolderFlags` with `IFolderView.GetAutoArrange` fallback on `ShellDesktopIcons` (`src/Berth/Desktop/ShellDesktopIcons.cs`) via its `StaWorker` and `DesktopView`. Done when: both shell APIs agree on the live desktop and unrelated folder-flag bits do not affect the result.
2. Implement `TryTurnAutoArrangeOff` with `IFolderView2.SetCurrentFolderFlags(FWF_AUTOARRANGE, 0)` and read-back. Done when: it returns what the read-back says (ignoring unrelated bits), returns `false` when `IFolderView2` is absent or the flag stays set, and maps mid-call COM failures to `DesktopUnavailableException` while releasing the view.

- [x] Implement `ShellDesktopIcons.AutoArrange` (`src/Berth/Desktop/ShellDesktopIcons.cs`) routed through its `StaWorker` and `DesktopView`: return `true` when `View2.GetCurrentFolderFlags` has `FWF_AUTOARRANGE` set (masked with `& FWF_AUTOARRANGE`, unaffected by unrelated flag bits); when `View2` is `null`, fall back to `View.GetAutoArrange()` (`S_OK` means `true`, `S_FALSE` means `false`, negative HRESULT throws via `Marshal.GetExceptionForHR` wrapped as `DesktopUnavailableException`); wrap `COMException`, `InvalidCastException`, `InvalidComObjectException`, and `UnauthorizedAccessException` thrown after `DesktopView.Open()` succeeds into `DesktopUnavailableException` preserving `InnerException` and releasing the view on the STA worker. **Corrected 2026-11-20:** specified `StaWorker` + `DesktopView` routing (`D02 T01 §9`), unrelated-bit masking (`plan review P2`), mid-call COM error wrapping with view release (`plan review P1`), shell-API agreement criterion (`plan review P2`), comparing the captured public `icons.AutoArrange` result against an independent raw `IFolderView.GetAutoArrange()` call (asserted `S_OK` or `S_FALSE` with `View2 != null`) on the same live `DesktopView` acquisition (`plan review 2 P1`), and updating `StaWorkerTests.ShellDesktopIconsRoutesAllPublicMembersThroughSharedWorkerBeforeDisposal` to use an injected stub `DesktopView` factory for the now-implemented `AutoArrange`/`TryTurnAutoArrangeOff` members. Done when: a `RealDesktop` test captures `icons.AutoArrange` and asserts it equals an independent raw `IFolderView.GetAutoArrange()` (`S_OK`/`S_FALSE`) read on the same live `DesktopView` with `View2 != null`, while unit tests cover `View2` on/off with unrelated bits set, `View2 == null` fallback `S_OK`/`S_FALSE`/negative HRESULT, mid-call COM failure wrapping + view release, and `StaWorkerTests` continues to prove all 4 `IDesktopIcons` members run on the STA worker. Cheaper substitute: reading the `FFlags` registry value under `Bags\1\Desktop`, which Explorer writes only at sign-out, testing `flags != 0`, or discarding `icons.AutoArrange`.
- [x] Implement `ShellDesktopIcons.TryTurnAutoArrangeOff()` (`src/Berth/Desktop/ShellDesktopIcons.cs`) routed through its `StaWorker` and `DesktopView`: when `View2` is `null`, return `false`; otherwise call `View2.SetCurrentFolderFlags(ShellConstants.FWF_AUTOARRANGE, 0)`, read the flags back via `View2.GetCurrentFolderFlags(out uint flags)`, and return `(flags & ShellConstants.FWF_AUTOARRANGE) == 0`; wrap `COMException`, `InvalidCastException`, `InvalidComObjectException`, and `UnauthorizedAccessException` from `SetCurrentFolderFlags` or post-write `GetCurrentFolderFlags` into `DesktopUnavailableException` preserving `InnerException` and releasing the view on the STA worker (never reporting `true` when read-back fails). **Corrected 2026-11-20:** added unrelated-bit masking (`plan review P2`) and mid-call COM error wrapping on `SetCurrentFolderFlags` and post-write `GetCurrentFolderFlags` (`plan review P1`). Done when: unit tests over a fake `IFolderView2` wrapper assert exact mask `FWF_AUTOARRANGE` and flags `0`, return `true` when read-back clears `FWF_AUTOARRANGE` while unrelated bits remain set, return `false` when read-back leaves `FWF_AUTOARRANGE` set, return `false` when `View2` is `null`, and throw `DesktopUnavailableException` (preserving inner exception and releasing `DesktopView`) when either `SetCurrentFolderFlags` or post-write `GetCurrentFolderFlags` throws a COM error. Cheaper substitute: returning `true` because `SetCurrentFolderFlags` did not throw, or checking `flags == 0`.
- [x] Never change the flag in an automated test on the real desktop: the default real-desktop test only reads it (running by default in an interactive desktop session and skipping only when `!Sta.HasInteractiveDesktopSession`), while any `TryTurnAutoArrangeOff` or raw `SetCurrentFolderFlags` write (including restoration) against a real-desktop `ShellDesktopIcons` (`new ShellDesktopIcons()` / `DesktopView.Open`) is reachable only after the `BERTH_TEST_ALLOW_AUTOARRANGE == "1"` opt-in guard. **Corrected 2026-11-20:** limited the `BERTH_TEST_ALLOW_AUTOARRANGE` opt-in guard strictly to real-desktop writes (`plan review P3`), extended the safety grep check to cover raw `SetCurrentFolderFlags` writes as well as `TryTurnAutoArrangeOff` (`plan review 2 P2`), and explicitly unset `BERTH_TEST_ALLOW_AUTOARRANGE` in the default checkpoint command (`plan review 2 P2`). Done when: `git grep -n -E "TryTurnAutoArrangeOff|SetCurrentFolderFlags" tests/` shows no real-desktop write (clear or restore) reachable outside a test skipped unless `BERTH_TEST_ALLOW_AUTOARRANGE` is `1`. Cheaper substitute: a test that turns the owner's auto arrange off and leaves it (`AGENTS.md`, rules specific to Berth).
- [x] Add `tests/Berth.Tests/Desktop/AutoArrangeTests.cs` with: (a) the read-only real-desktop read and fallback-agreement test (`Sta.HasInteractiveDesktopSession`, asserting `desktop.View2` is non-null, reading raw `IFolderView.GetAutoArrange()` as `S_OK` or `S_FALSE`, and asserting `icons.AutoArrange` matches it within one `DesktopView` acquisition) and the opt-in real-desktop round trip harness (skipped unless `BERTH_TEST_ALLOW_AUTOARRANGE == "1"` and `Sta.HasInteractiveDesktopSession`; requires auto arrange to already be confirmed on, calls `TryTurnAutoArrangeOff()`, asserts `true`, independently reads `GetCurrentFolderFlags` to verify `FWF_AUTOARRANGE` is cleared, and restores `FWF_AUTOARRANGE` in `finally` with read-back verification) in `[Collection(DesktopCollectionDefinition.Name)]` with `[Trait("Category", "RealDesktop")]`; and (b) unit tests for `IFolderView2` on/off with unrelated bits set, `IFolderView` fallback `S_OK`/`S_FALSE`/negative HRESULT, `TryTurnAutoArrangeOff` exact mask/flags/read-back-with-unrelated-bits/sticky-flag/`View2 == null`, and mid-call COM failure wrapping + `DesktopView` release on `GetCurrentFolderFlags`, `GetAutoArrange`, `SetCurrentFolderFlags`, and post-write `GetCurrentFolderFlags`. **Corrected 2026-11-20:** split automated delivery (`D02 T01 §5`) from disposable-desktop execution (`D99 T01 §4`, `Needs: attended, operator`) and captured public `icons.AutoArrange` against independent raw `IFolderView.GetAutoArrange()` (`plan review 2 P1`). Done when: in an interactive desktop session with `BERTH_TEST_ALLOW_AUTOARRANGE` explicitly unset, only the opt-in round-trip test is skipped (`1` skipped) and all other tests pass (`0` failed); in non-interactive Session 0 both real-desktop tests skip and all unit tests pass. Cheaper substitute: no test of the read-back or an opt-in round trip that passes when auto arrange starts off.
- [x] Commit: `desktop: auto arrange read and clear`

**Test checkpoint:** `Remove-Item Env:BERTH_TEST_ALLOW_AUTOARRANGE -ErrorAction SilentlyContinue; dotnet test --solution Berth.slnx -c Release --filter-class "*AutoArrangeTests" --filter-class "*StaWorkerTests" --filter-class "*LayerRuleTests"` passes with only the opt-in `BERTH_TEST_ALLOW_AUTOARRANGE` test skipped on an interactive desktop (`1` skipped, `0` failed), and the unit tests fail when `TryTurnAutoArrangeOff` returns `true` without reading back, when `flags != 0` or `flags == 0` is used instead of masking `FWF_AUTOARRANGE`, when the wrong mask/flags are passed, or when a mid-call `COMException` escapes instead of throwing `DesktopUnavailableException`.

> **Verified:** 2026-11-20 | §5 | 40d9c217c9e94bf94a8b19ae3643e9684ca9934c
> **Review:** `docs/reviews/d02-t01-s5.json`

## 6. Monitor bounds, scale and primary

> **Started:** 2026-11-20T10:57Z

`SystemMonitors.Read` returns each monitor as SPEC §14 stores it.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `SystemMonitors` in `src/Berth/Desktop/SystemMonitors.cs`. Done when: it returns at least one monitor with one primary, in `MonitorNumbering` order.
2. Read each monitor's scale and validate native failures. Done when: the scale is a whole percentage such as 100, 125 or 150, and native enumeration, info, or DPI failures throw `DesktopUnavailableException`.

- [x] Implement `SystemMonitors` (`src/Berth/Desktop/SystemMonitors.cs`, implementing `IMonitors`) with `[LibraryImport("user32.dll", SetLastError = true)]` `EnumDisplayMonitors` (using an `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]` callback and `GCHandle` freed in `finally`, capturing any callback failure in state and stopping enumeration before throwing after the native call returns so no managed exception unwinds across native code), `GetMonitorInfoW` (`MONITORINFOEXW` with `cbSize` initialized, `rcMonitor` physical pixel bounds distinct from `rcWork`, `MONITORINFOF_PRIMARY = 0x00000001u`, and `szDevice`), `EnumDisplayDevicesW(null, iDevNum, ...)` to exclude invisible pseudo-monitors (`DISPLAY_DEVICE_MIRRORING_DRIVER` or missing `DISPLAY_DEVICE_ATTACHED_TO_DESKTOP`), `SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` (restoring the previous context in `finally` so physical bounds and effective DPI are read under production-equivalent awareness even before any window opens or inside the test host), and `[LibraryImport("shcore.dll")]` `GetDpiForMonitor(MDT_EFFECTIVE_DPI = 0)` with `ScaleFromDpi(uint dpi) => (int)((dpi * 100L + 48L) / 96L)` (rounded away from zero), reading fresh on every `Read()` call with no caching. **Corrected 2026-11-20:** resolved all 5 `astra` plan-review findings: (1) multi-monitor native-seam test with 2 distinct handles, different DPIs, negative origin, distinct `rcMonitor` vs `rcWork`, and primary second asserting exact count and every field; (2) explicit `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2` thread context during read and live awareness check (no window exists in CLI/tray startup, so `GetDpiForMonitor` under PerMonitorV2 is the documented windowless monitor API); (3) callback-state exception containment tested with a succeeding 1st monitor and failing 2nd monitor; (4) no-caching test calling `Read()` twice across a 2-to-1 monitor change; and (5) excluding `DISPLAY_DEVICE_MIRRORING_DRIVER` / unattached pseudo-monitors via `EnumDisplayDevicesW`. Done when: the live `RealDesktop` test verifies Per-Monitor-V2 awareness, returns one or more monitors, exactly one `Primary`, positive sizes, and scales from 100 to 500; and unit tests over the native seam verify 2-monitor exact fields (`rcMonitor` vs `rcWork`, negative origin, different DPIs, primary second), no caching across calls, mirroring/unattached exclusion, and `ScaleFromDpi` across standard (96, 120, 144, 168, 192, 480) and midpoint (108) DPIs. Cheaper substitute: `System.Windows.Forms.Screen`, which reports no scale, or reading `rcWork` instead of `rcMonitor`.
- [x] Return monitors in `MonitorNumbering.Order` order (`D01 T01 §13`: sorted by `X` then `Y`). Done when: unit tests with unsorted multi-monitor input (including negative `X` and `Y` and equal `X` tie-breaking by `Y`) and the live `RealDesktop` test assert the returned list equals `MonitorNumbering.Order(result)`. Cheaper substitute: enumeration order, which Windows does not promise.
- [x] Throw `DesktopUnavailableException` when no monitor is returned (locked or disconnected session), when `EnumDisplayMonitors` or `GetMonitorInfoW` fails (preserving `Win32Exception` as `InnerException`), when `GetDpiForMonitor` returns a non-zero HRESULT (preserving `Marshal.GetExceptionForHR` as `InnerException`) or `dpiX == 0`, when any monitor has non-positive width/height or scale `<= 0`, or when the enumerated set does not have exactly one primary monitor; when a second monitor fails inside the enumeration callback after a first monitor succeeded, stop enumeration cleanly, let no partial list escape, and throw `DesktopUnavailableException` preserving the second monitor's error as `InnerException`. **Corrected 2026-11-20:** specified native failure mapping (`EnumDisplayMonitors`, `GetMonitorInfoW`, `GetDpiForMonitor`), callback-state error capture on a failing second monitor (`plan review P1`), and bounds/primary invariants alongside the empty-list case so a partial or corrupt enumeration never produces a wrong arrangement key. Done when: unit tests over the internal raw-list and native-seam overloads assert `DesktopUnavailableException` (with inner exception preserved where applicable) for each failure case, including a 2-monitor callback where monitor 1 succeeds and monitor 2 fails. Cheaper substitute: returning an empty list or silently dropping a monitor whose info/DPI query failed.
- [x] Add `tests/Berth.Tests/Desktop/SystemMonitorsTests.cs` with the live read in `[Collection(DesktopCollectionDefinition.Name)]` marked `[Trait("Category", "RealDesktop")]` (skipping only when `!Sta.HasInteractiveDesktopSession`) plus unit tests for the 2-monitor native-seam full-field verification, no-caching across two `Read()` calls, mirroring/unattached pseudo-monitor exclusion, `MonitorNumbering` ordering, DPI rounding, empty list, missing/duplicate primary, non-positive size, and native `EnumDisplayMonitors`/`GetMonitorInfoW`/`GetDpiForMonitor` failures (including 1st-succeeds/2nd-fails callback containment). **Corrected 2026-11-20:** added `DesktopCollectionDefinition.Name` serialization and `Sta.HasInteractiveDesktopSession` guard (`AGENTS.md`, 2026-11-18) plus all 5 `astra` plan-review test cases. Done when: all pass with `0` skipped on the build machine, and the tests fail when `Primary` is never set, when `rcWork` is read instead of `rcMonitor`, when a mirroring device is included, when `Read()` caches its first result, or when a 2nd-monitor failure escapes as a partial list. Cheaper substitute: asserting only a non-empty list.
- [x] Commit: `desktop: monitor bounds, scale and primary`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*SystemMonitorsTests" --filter-class "*LayerRuleTests"` passes with `0` skipped on the build machine, and fails when `Primary` is never set, when `rcWork` is used instead of `rcMonitor`, when ordering is omitted, when mirroring pseudo-monitors are included, when `Read()` caches its first result, or when an empty list or 2nd-monitor native failure does not throw `DesktopUnavailableException`.

> **Verified:** 2026-11-20 | §6 | d941963ccfb00bba606da90ca14040d2b59a0b3a
> **Review:** `docs/reviews/d02-t01-s6.json`

## 7. Real-desktop round trip on a probe icon

The one test that proves reading and writing against Windows itself, touching only an icon it creates (foundation plan, "Seams", last bullet).

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add a probe fixture that creates and removes its own desktop file. Done when: the file never outlives the test.
2. Move the probe and read it back. Done when: the read-back equals the requested position and every other icon is where it was.

- [ ] Add `ProbeIcon` in `tests/Berth.Tests/Desktop/ProbeIcon.cs`: creates `berth-test-probe-<guid>.txt` in the user's desktop folder (`Environment.SpecialFolder.DesktopDirectory`), waits up to 10 seconds for an icon with that display name to appear in `ReadIcons`, exposes `Prefix` (`berth-test-probe-<guid>`), and deletes exactly the file it created in `Dispose` and when its own start-up fails or times out. It deletes no other file, whatever its name or age. Done when: after the test, after a test that throws mid-way, and after a forced start-up timeout, the file it created is gone and a decoy file `berth-test-probe-decoy.txt` placed by the test in a temp folder passed as the "desktop" in a unit variant is untouched. Cheaper substitute: wildcard cleanup of old probe files, which deletes files this run did not create.
- [ ] Add `tests/Berth.Tests/Desktop/ProbeIconTests.cs` (trait `RealDesktop`): skip with a clear message when `AutoArrange` is `true`; read the desktop through `ScopedDesktopIcons` with the probe's `Prefix` (§10), so only the probe is read; set the probe to its current position plus 200 px on `X` through the scoped desktop, read back, and assert the probe is at the requested position (or, when the desktop snaps to its grid, within one grid cell of it, recording which happened in the test output). Done when: the test passes on the build machine and the only `Id` ever passed to the real `SetPositions` is the probe's. Cheaper substitute: asserting only that `SetPositions` did not throw.
- [ ] In the same test, pass a made-up `Id` alongside the probe and assert success (`§4`). Done when: the assertion is present and passes. Cheaper substitute: testing the unknown `Id` only against the fake.
- [ ] Run the full `LayoutService` cycle over `ScopedDesktopIcons` and `SystemMonitors` with a `TestData` root: save (the layout holds exactly one icon, the probe), move the probe, restore, assert the probe is back and `Missing` is 0, undo, assert the probe returned to the moved position. Done when: the test passes, the layout file lists one icon, and the temp root holds no `undo.json` after the undo. Cheaper substitute: a cycle over the unscoped desktop, which snapshots and repositions every icon the owner has.
- [ ] Commit: `desktop: real-desktop round trip on a probe icon`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ProbeIconTests"` passes on the build machine with auto arrange off, leaves no probe file on the desktop, and fails when `SetPositions` passes the points in the wrong order.

## 8. One exception for an unreachable desktop

Explorer restarting, a locked session or a COM failure all surface as `DesktopUnavailableException` (SPEC §9 second notice, §18).

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Map failures in `DesktopView` and `ShellDesktopIcons`. Done when: no `COMException` or `InvalidCastException` leaves the `Desktop` namespace.
2. Test the mapping with a failing wrapper. Done when: each member throws the one exception.

- [ ] Wrap the body of `DesktopView.Open`, `ReadIcons`, `SetPositions`, `AutoArrange` and `TryTurnAutoArrangeOff` so that `COMException`, `InvalidCastException`, `InvalidComObjectException` and a null view become `DesktopUnavailableException` with the original as inner exception. Done when: a unit test injecting a view factory that throws `COMException` sees `DesktopUnavailableException` from each member. Cheaper substitute: `catch (Exception)`, which would also hide bugs such as a null reference in Berth's own code.
- [ ] Open a fresh `DesktopView` for every operation instead of caching one. Done when: a unit test with a factory that fails once and then succeeds shows the second `ReadIcons` succeeding. Cheaper substitute: caching the view, which is dead after Explorer restarts.
- [ ] Add `tests/Berth.Tests/Desktop/UnavailableTests.cs`. Done when: all pass, and one fails when `ReadIcons` lets `COMException` through. Cheaper substitute: restarting Explorer in a test.
- [ ] Commit: `desktop: one exception for an unreachable desktop`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*UnavailableTests"` passes, and fails when `ReadIcons` lets a `COMException` escape.

## 9. STA worker that owns every shell call

> **Started:** 2026-11-19T15:52:00Z

The shell's folder view must be used from a single-threaded apartment, and the window, the tray menu and the automatic restore all call it from background threads. One worker thread owns every call, so no caller has to care.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `StaWorker` in `src/Berth/Desktop/StaWorker.cs`. Done when: work queued from any thread runs on one message-pumping STA thread and drains cleanly on disposal.
2. Make it the only way `ShellDesktopIcons` reaches the shell. Done when: all public members and `RunWithView` execute on the shared STA worker, dispose `DesktopView` on that STA thread, and stop the worker thread on disposal.

- [x] Add `StaWorker` in `src/Berth/Desktop/StaWorker.cs`: a dedicated background thread set to `ApartmentState.STA` before `Start()`, running a message-pumping WPF `Dispatcher` loop (`Dispatcher.Run()`) so incoming Win32/COM messages are dispatched while the STA thread is idle; `T Run<T>(Func<T> work)` and `void Run(Action work)` validate `ArgumentNullException`, run inline when called from the worker's own thread (unless disposed), enqueue FIFO onto the `Dispatcher` otherwise, and return the result or rethrow the exception with original type and stack trace (`ExceptionDispatchInfo`). `Dispose()` is idempotent, immediately transitions to disposed under a lock so any subsequent `Run` throws `ObjectDisposedException`, posts `BeginInvokeShutdown(DispatcherPriority.Normal)` so every already-accepted queued item drains in FIFO order and delivers its result or exception exactly once, joins up to a configurable timeout (default 2 seconds) when called from an external thread without deadlocking if the wait expires (the background worker still drains and exits once the in-flight callback returns), and returns immediately without self-joining when called from the worker's own thread (exiting after the callback returns and accepted items drain). **Corrected 2026-11-19:** resolved `astra` plan-review P1 findings 1 and 2 by specifying message-pumping `Dispatcher` idle loop, exact disposal/timeout/self-disposal semantics, held-open + queued drain test, and idle `PostMessage` pump test. Done when: unit tests prove STA apartment and single `ManagedThreadId` across three external threads (including explicit MTA threads), idle Win32 `PostMessage` handling on an `HwndSource` without submitting another `Run`, nested inline execution without deadlock, exception type and stack preservation, null guard, held-open callback + queued second item + late `ObjectDisposedException` + bounded external `Dispose` timeout + eventual exit, and self-disposal exit after callback return. Cheaper substitute: `Task.Run` (MTA) or a non-pumping blocking queue that leaves STA messages undispatched.
- [x] Give `ShellDesktopIcons` (`src/Berth/Desktop/ShellDesktopIcons.cs`, implementing `IDesktopIcons` and `IDisposable`) one `StaWorker` and an internal `RunWithView<T>(Func<DesktopView, T> work)` helper (using an injectable `Func<DesktopView>` factory defaulting to `DesktopView.Open`) that acquires `DesktopView`, invokes `work`, and disposes `DesktopView` all inside one `_worker.Run(...)` invocation; route `ReadIcons`, `SetPositions`, `AutoArrange`, and `TryTurnAutoArrangeOff` through `_worker.Run(...)` (in `§9`, throwing `NotImplementedException` naming `D02 T01 §3`/`§4`/`§5` inside the worker invocation, observable via an internal pre-throw worker hook so tests prove no public member bypasses the shared worker). **Corrected 2026-11-19:** resolved `astra` plan-review P1 finding 3 and P2 finding 4 by verifying single-invocation `DesktopView` release on the STA worker on both success and callback failure (with callback exception preserved) using the `DesktopView` release seam, and proving pre-disposal worker execution through all four `IDesktopIcons` members; the `RealDesktop` test calls `ShellDesktopIcons.RunWithView` from `Task.Run` to open a live `DesktopView` and read `ItemCount(SVGIO_ALLVIEW, out int count) >= 0`. Done when: all unit tests and the `RealDesktop` test (in `[Collection(DesktopCollectionDefinition.Name)]`, skipping only when `!Sta.HasInteractiveDesktopSession`) pass with `0` skipped on the build machine. Cheaper substitute: requiring every caller to be on the UI thread, leaking `DesktopView`, or checking a disposed flag before bypassing the worker.
- [x] `ShellDesktopIcons` is disposable and disposes its worker idempotently; `Composition` (`D03 T01 §3`) owns one for the life of the process. **Corrected 2026-11-19:** resolved `astra` plan-review P2 finding 5 by capturing the actual worker `Thread` during a callback, disposing `ShellDesktopIcons` twice from outside that thread, and asserting that the captured thread exits (`!IsAlive`) in addition to subsequent calls throwing `ObjectDisposedException`. Done when: the captured worker thread exits after disposal and later calls to `ReadIcons`, `SetPositions`, `AutoArrange`, `TryTurnAutoArrangeOff`, and `RunWithView` each throw `ObjectDisposedException`. Cheaper substitute: a thread that outlives the tray app's exit or a wrapper that only sets a disposed flag.
- [x] Add `tests/Berth.Tests/Desktop/StaWorkerTests.cs` covering all unit behaviors above plus the `RealDesktop` thread-pool `DesktopView` test. Done when: all pass with `0` skipped on the build machine, and fail when the worker thread's apartment state is left as MTA, when the idle loop stops pumping messages, or when any public member bypasses the worker. Cheaper substitute: none needed.
- [x] Commit: `desktop: STA worker that owns every shell call`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*StaWorkerTests" --filter-class "*LayerRuleTests"` passes with `0` skipped on a machine with a desktop session, and fails when the worker thread's apartment state is left as MTA, when the idle wait stops pumping Win32 messages, or when a public `ShellDesktopIcons` member bypasses the worker.

> **Verified:** 2026-11-19 | §9 | cb30e5b8a7966941718812fce5c08018e8a494e7
> **Review:** `docs/reviews/d02-t01-s9.json`

## 10. Desktop scoped to icons with a name prefix

A wrapper that lets Berth see and move only icons whose display name starts with a prefix. It is how every real-desktop test keeps to its own probe icon, in process and through the built executable (SPEC §14 "Test mode").

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Add `ScopedDesktopIcons` in `src/Berth/Desktop/ScopedDesktopIcons.cs`. Done when: reads and writes are limited to the prefix.
2. Test it over a fake. Done when: an icon outside the scope is never read or passed on.

- [ ] Add `ScopedDesktopIcons(IDesktopIcons inner, string prefix)` implementing `IDesktopIcons`: `ReadIcons` returns only the icons whose `Name` starts with `prefix` (ordinal, ignoring case); `SetPositions` passes on only the positions whose `Id` belongs to an icon in scope at that moment; `AutoArrange` passes through; `TryTurnAutoArrangeOff` returns `false` without calling `inner`. An empty prefix is refused with `ArgumentException`. Done when: unit tests over `FakeDesktopIcons` with three icons, one in scope, show one icon read, an out-of-scope `Id` never reaching the fake's `SetPositions`, and auto arrange never changed. Cheaper substitute: filtering in each test, which the built executable cannot use.
- [ ] Add `tests/Berth.Tests/Desktop/ScopedDesktopIconsTests.cs`. Done when: all pass, and the write test fails when `SetPositions` forwards every position. Cheaper substitute: none needed.
- [ ] Commit: `desktop: desktop scoped to icons with a name prefix`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ScopedDesktopIconsTests"` passes, and fails when an out-of-scope position is forwarded to the inner desktop.

## Verification

- On the build machine, `ProbeIconTests` saves, moves, restores and undoes a probe icon on the real desktop and leaves the desktop as it found it.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
