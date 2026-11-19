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

> **Current state:** Measured 2026-11-15. No interop code exists. `D01 T01 §8` declares the seams `IDesktopIcons` and `IMonitors` and their fakes. The interface chain is written in `docs/plans/2026-11-15-berth-foundation.md` under "How icon positions are read and written".

## Inputs

- `docs/plans/2026-11-15-berth-foundation.md`: "How icon positions are read and written", "Seams that make it testable".
- `design/SPEC.md` §3 (physical pixels of the virtual desktop), §9 (auto arrange), §15 (reading the desktop), §18 (the unreachable desktop).
- Microsoft's documentation of `IFolderView`, `IFolderView2`, `IShellWindows::FindWindowSW` and `IShellFolder::GetDisplayNameOf`: read it for the installed SDK before declaring an interface, and cite the page in the file's header comment.
- -> XREF: D00 T01 §2 -- the application project and its manifest (per-monitor DPI awareness)
- -> XREF: D01 T01 §8 -- the `IDesktopIcons` and `IMonitors` seams implemented here
- -> XREF: D03 T01 §3 -- the commands run against `ShellDesktopIcons`
- -> XREF: D05 T01 §7 -- the auto arrange notice calls `TryTurnAutoArrangeOff`
- -> XREF: D06 T01 §2 -- the automatic restore reads the monitors after a display change

## Outcome

- `ShellDesktopIcons` and `SystemMonitors` implement the seams against Windows, and a test moves its own probe icon on the real desktop and reads the new position back.
- A desktop that cannot be reached is one exception type, never a raw COM error.

**Adjacency:** all=not-applicable (interop layer: it holds no records, settings or surface of its own)

**Adjacency rationale:** This file is the Windows side of two seams. The records, their undo and their wording belong to `D01 T01`.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Shell COM interface declarations | D00 T01 §2 | [x] |
| 2 | §2 | The desktop's folder view | §1 | [ ] |
| 3 | §9 | STA worker that owns every shell call | §2 | [ ] |
| 4 | §3 | Reading every icon's identity and position | §9, §6, D01 T01 §8 | [ ] |
| 5 | §4 | Setting icon positions | §3 | [ ] |
| 6 | §5 | Auto arrange read and clear | §9 | [ ] |
| 7 | §6 | Monitor bounds, scale and primary | D01 T01 §8 | [ ] |
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

- [ ] Add `DesktopView.Open()` returning a disposable holder of `IFolderView` (and `IFolderView2` when the view supports it): create `ShellWindows`, call `FindWindowSW` with `CSIDL_DESKTOP`, `SWC_DESKTOP` and `SWFO_NEEDDISPATCH`, query `IServiceProvider` for `SID_STopLevelBrowser` as `IShellBrowser`, call `QueryActiveShellView`, and cast to `IFolderView` (foundation plan). **Corrected 2026-11-19:** expose `IFolderView View` and `IFolderView2? View2`; record and assert `CSIDL_DESKTOP`, null `pvarLocRoot`, `SWC_DESKTOP`, `SWFO_NEEDDISPATCH`, `SID_STopLevelBrowser`, and `typeof(IShellBrowser).GUID` on the injected shell seam (`plan review P1`); translate `FindWindowSW` non-`S_OK` (including `S_FALSE` and negative HRESULTs such as `E_NOINTERFACE`), null intermediate/view results, and `COMException` / `InvalidCastException` / `InvalidComObjectException` / `UnauthorizedAccessException` (`E_ACCESSDENIED`) into `DesktopUnavailableException` preserving `InnerException` and `HResult` (`plan review P2`). Done when: a test on the real desktop calls `Open()` and `ItemCount(SVGIO_ALLVIEW, out int count)` returns a number at or above 0 with `View2` callable, while unit tests verify both optional `IFolderView2` outcomes (`View2` callable vs `View` usable with `View2 == null`), exact shell arguments, `S_FALSE`, `E_NOINTERFACE`, `UnauthorizedAccessException`, null results, and COM errors. Cheaper substitute: `FindWindow("Progman")` and list-view messages, which the README forbids.
- [ ] Release every intermediate COM object (`Marshal.ReleaseComObject` or the generated wrappers' disposal) on success and on failure. **Corrected 2026-11-19:** use `Marshal.ReleaseComObject` (releasing one reference per acquired slot without zeroing a shared RCW's count or skipping aliased interfaces, `plan review P1`) rather than `FinalReleaseComObject`, releasing `shellWindows`, `dispatch`, and `shellBrowser` before returning on success, releasing `shellView` once idempotently in `Dispose()`, and releasing all acquired objects in reverse order on failure at any step; verify a 500x real-desktop handle-count loop (`process.Refresh()`, delta `<= 50`), a live concurrent two-holder test (disposing holder 1 leaves holder 2 callable), plus unit tests over `DesktopView.Open(Func<object?>, Action<object>?)` covering a success-handoff trace, aliased interfaces, concurrent holders, every failure step, and double `Dispose()`. Done when: all handle-count, concurrency, success-handoff, failure-step, and `Dispose()` assertions pass. Cheaper substitute: leaving release to the finaliser, which a long-running tray app pays for.
- [ ] Require an STA thread (production code reaches `DesktopView` only through the `StaWorker` of §9): `DesktopView.Open` asserts `Thread.CurrentThread.GetApartmentState() == ApartmentState.STA` before COM activation and throws `InvalidOperationException` naming `STA` on MTA threads with `0` factory invocations, and the test host runs `[StaFact]` tests on a dedicated `ApartmentState.STA` thread via `ISelfExecutingXunitTestCase` installing `DispatcherSynchronizationContext`, pumping a `DispatcherFrame` until completion, propagating exceptions, and calling `dispatcher.InvokeShutdown()` in `finally` on both success and failure (`tests/Berth.Tests/Sta.cs`, alongside `Sta.HasInteractiveDesktopSession` checking `Environment.UserInteractive && Process.GetCurrentProcess().SessionId != 0`). **Corrected 2026-11-19:** added `DispatcherFrame` pumping, `finally` shutdown, same-`ManagedThreadId` continuation check across `await Task.Yield()`, and zero-call MTA factory check (`plan review P2`). Done when: calling `Open` from an explicit MTA thread throws `InvalidOperationException` naming `STA` with `0` activation calls, `[StaFact]` runs synchronous and post-await continuations on the same `STA` thread ID, and `Sta.RunAsync` propagates failures and shuts down its `Dispatcher` on both outcomes. Cheaper substitute: silently working on some threads and failing on others.
- [ ] Add `tests/Berth.Tests/Desktop/DesktopViewTests.cs` with real-desktop tests in `[Collection(DesktopCollectionDefinition.Name)]` marked `[Trait("Category", "RealDesktop")]` (skipping only when `!Sta.HasInteractiveDesktopSession` so non-interactive Session 0 CI runners pass while interactive hosts report `0` skipped) plus unit tests for `IFolderView2` present/absent, exact `FindWindowSW`/`QueryService` arguments, MTA zero-factory rejection, `DesktopUnavailableException` mapping (including `UnauthorizedAccessException` and `E_NOINTERFACE`), success/aliased/concurrent/failure release tracking, and `StaFact`/`Dispatcher` pumping. **Corrected 2026-11-19:** added `DesktopCollectionDefinition.Name` serialization (`AGENTS.md`, 2026-11-18) and `astra` plan-review cases. Done when: all cases pass with `0` skipped on the build machine. Cheaper substitute: a test that is skipped everywhere.
- [ ] Commit: `desktop: the desktop's folder view`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*DesktopViewTests" --filter-class "*LayerRuleTests"` passes with `0` skipped on a machine with a desktop session, and fails when `SWC_DESKTOP` is replaced by another window class constant, when the STA check is removed, or when `Marshal.ReleaseComObject` is replaced by `Marshal.FinalReleaseComObject`.

## 3. Reading every icon's identity and position

`ShellDesktopIcons.ReadIcons` returns each icon's parsing name, display name and position.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `ShellDesktopIcons` in `src/Berth/Desktop/ShellDesktopIcons.cs` implementing `IDesktopIcons.ReadIcons`. Done when: it returns the real desktop's icons.
2. Free every item identifier list. Done when: the leak test passes.

- [ ] Implement `ReadIcons()`: get the view's `IShellFolder` with `IFolderView.GetFolder`, enumerate with `IFolderView.Items(SVGIO_ALLVIEW)` as `IEnumIDList`, and for each item read `GetItemPosition`, the parsing name with `GetDisplayNameOf(SHGDN_FORPARSING)` and the display name with `GetDisplayNameOf(SHGDN_NORMAL)`, converting the returned `STRRET` with `StrRetToBuf` or `StrRetToStr`. Done when: a test on the real desktop gets a list whose count equals `ItemCount(SVGIO_ALLVIEW)` and whose every `Id` is non-empty. Cheaper substitute: using the display name as `Id` (SPEC §15 "Matching").
- [ ] Free each item identifier list with `CoTaskMemFree` (or `Marshal.FreeCoTaskMem`) in a `finally`. Done when: a test reads the icons 200 times and private memory afterwards is within 5 MB of the value before. Cheaper substitute: leaking one allocation per icon per read, which a 5 second poll turns into a real leak.
- [ ] Add `DesktopCoordinates` in `src/Berth/Desktop/DesktopCoordinates.cs` with `ToDesktop(viewX, viewY, IReadOnlyList<MonitorInfo> monitors)` and its exact inverse `ToView(...)`, converting between the folder view's coordinates (`IFolderView.GetItemPosition` reports positions in the view, per Microsoft's documentation) and physical pixels of the virtual desktop (SPEC §15 "Positions"). First measure on the build machine where the view's origin is: read a known icon's view position and its on-screen rectangle (through UI Automation or `IFolderView.GetItemPosition` against `GetSystemMetrics(SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN)`), and record the finding and the date in the file's header comment. `ReadIcons` returns desktop coordinates. Done when: unit tests cover a primary-only arrangement, a monitor at negative `X`, and a monitor at negative `Y`, and `ToView(ToDesktop(p))` equals `p` for every tested point; and a `RealDesktop` test asserts every icon read from the live desktop lies inside the bounding box of the live monitors. Cheaper substitute: storing view coordinates and converting only when drawing, so Core's monitor numbering and off-screen checks disagree with the map.
- [ ] When two items report the same parsing name ignoring case, keep the first and write one `WARN` log line naming it. Done when: a unit test over an internal `Deduplicate` helper asserts one icon and one warning. Cheaper substitute: returning duplicates, which `LayoutStore` refuses to load back (`D01 T01 §5`).
- [ ] Add `tests/Berth.Tests/Desktop/ReadIconsTests.cs` (trait `RealDesktop`) asserting: the count matches the view; the Recycle Bin's `Id`, when it is on the desktop, starts with `::{`; two reads in a row return equal lists. Done when: they pass, and the count test fails when the enumeration stops one item early. Cheaper substitute: asserting only that the list is not null.
- [ ] Commit: `desktop: reading every icon's identity and position`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ReadIconsTests"` passes on the real desktop, and fails when the enumeration loop drops the last item.

## 4. Setting icon positions

`ShellDesktopIcons.SetPositions` moves the icons it is given and nothing else.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Implement `SetPositions` with one `SelectAndPositionItems` call. Done when: the probe test of §7 can move its icon.
2. Skip icons that are not on the desktop. Done when: an unknown `Id` is ignored without error.

- [ ] Implement `SetPositions(IReadOnlyList<IconPosition> icons)`: enumerate the desktop's items once, build a map from parsing name (ignoring case) to item identifier list, and call `IFolderView.SelectAndPositionItems` with the matched items, their points converted with `DesktopCoordinates.ToView`, and `SVSI_POSITIONITEM`. Done when: the §7 probe test moves its icon by this call. Cheaper substitute: one call per icon, which is slow and repaints the desktop each time.
- [ ] Pass only `SVSI_POSITIONITEM`, so the user's selection on the desktop is not changed. Done when: the §7 probe test asserts the probe icon is not selected afterwards (through `IFolderView.ItemCount(SVGIO_SELECTION)` being unchanged). Cheaper substitute: adding `SVSI_SELECT`, which deselects what the user had selected.
- [ ] Ignore, without error, an `IconPosition` whose `Id` is not on the desktop. Done when: the §7 test passes a made-up `Id` alongside the probe and the call succeeds. Cheaper substitute: throwing, which would fail every restore with a deleted file.
- [ ] Free every item identifier list in a `finally`, as in §3. Done when: a unit test over the internal matching helper with a fake enumerator counts one free per allocation, on success and when the positioning call throws. Cheaper substitute: leaking per restore.
- [ ] Do not call `SetPositions` against the real desktop in this section: the only real write is §7's, on its own probe icon. Done when: `git grep -n "SetPositions" tests/Berth.Tests/Desktop` shows real-desktop calls only in `ProbeIconTests.cs`. Cheaper substitute: an "identity write" of every icon's current position, which reverses any move the owner makes between the read and the write (`AGENTS.md`).
- [ ] Commit: `desktop: setting icon positions`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SetPositionsTests"` passes (the matching, conversion and free-count unit tests), and fails when an `IconPosition` whose `Id` is not on the desktop makes the call throw; the real-desktop proof is §7's `ProbeIconTests`.

## 5. Auto arrange read and clear

Berth detects auto arrange and can turn it off on the user's click (SPEC §9).

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Implement `AutoArrange` through `IFolderView2.GetCurrentFolderFlags`. Done when: it matches the desktop's View menu.
2. Implement `TryTurnAutoArrangeOff` with read-back. Done when: it returns what the read-back says.

- [ ] Implement `AutoArrange`: `true` when `GetCurrentFolderFlags` has `FWF_AUTOARRANGE` set; when the view does not offer `IFolderView2`, fall back to `IFolderView.GetAutoArrange` (`S_OK` means on). Done when: a test on the real desktop reads the value without throwing and it equals the value read through the fallback path. Cheaper substitute: reading the `FFlags` registry value under `Bags\1\Desktop`, which Explorer writes only at sign-out.
- [ ] Implement `TryTurnAutoArrangeOff()`: call `SetCurrentFolderFlags(FWF_AUTOARRANGE, 0)`, read the flag back, and return `true` only when it reads off; return `false` when `IFolderView2` is not available. Done when: a unit test over a fake `IFolderView2` wrapper asserts `false` when the flag stays set. Cheaper substitute: returning `true` because the call did not throw.
- [ ] Never change the flag in an automated test on the real desktop: the real-desktop test only reads it. Done when: `git grep -n TryTurnAutoArrangeOff tests/` shows no call against `ShellDesktopIcons` outside a test that is skipped unless the environment variable `BERTH_TEST_ALLOW_AUTOARRANGE` is `1`. Cheaper substitute: a test that turns the owner's auto arrange off and leaves it (`AGENTS.md`, rules specific to Berth).
- [ ] Add `tests/Berth.Tests/Desktop/AutoArrangeTests.cs` with the read test (trait `RealDesktop`), the fake-wrapper tests and the opt-in round trip (turn off, read, restore the original value in `finally`). Done when: the default run passes with the round trip reported as skipped. Cheaper substitute: no test of the read-back.
- [ ] Commit: `desktop: auto arrange read and clear`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*AutoArrangeTests"` passes, and the fake-wrapper test fails when `TryTurnAutoArrangeOff` returns `true` without reading back.

## 6. Monitor bounds, scale and primary

`SystemMonitors.Read` returns each monitor as SPEC §14 stores it.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `SystemMonitors` in `src/Berth/Desktop/SystemMonitors.cs`. Done when: it returns at least one monitor with one primary.
2. Read each monitor's scale. Done when: the scale is a whole percentage such as 100, 125 or 150.

- [ ] Implement `IMonitors.Read()` with `EnumDisplayMonitors` and `GetMonitorInfo` (physical pixel bounds in virtual-desktop coordinates, since the process is per-monitor DPI aware) and `GetDpiForMonitor(MDT_EFFECTIVE_DPI)`, converting DPI to a percentage (`dpi * 100 / 96`, rounded). Done when: a test on the build machine gets one or more monitors, exactly one `Primary`, positive sizes and a scale from 100 to 500. Cheaper substitute: `System.Windows.Forms.Screen`, which reports no scale.
- [ ] Return monitors in `MonitorNumbering` order (`D01 T01 §13`). Done when: a test asserts the returned list equals its own sort by `X` then `Y`. Cheaper substitute: enumeration order, which Windows does not promise.
- [ ] Throw `DesktopUnavailableException` when no monitor is returned (a locked or disconnected session). Done when: a unit test over an internal overload that takes the raw list asserts the exception for an empty list. Cheaper substitute: returning an empty list, which `ArrangementKey` then refuses with a less useful error.
- [ ] Add `tests/Berth.Tests/Desktop/SystemMonitorsTests.cs` (trait `RealDesktop` for the live read). Done when: they pass, and the primary test fails when the primary flag is never set. Cheaper substitute: asserting only a non-empty list.
- [ ] Commit: `desktop: monitor bounds, scale and primary`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SystemMonitorsTests"` passes on the build machine, and fails when `Primary` is never set.

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

The shell's folder view must be used from a single-threaded apartment, and the window, the tray menu and the automatic restore all call it from background threads. One worker thread owns every call, so no caller has to care.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `StaWorker` in `src/Berth/Desktop/StaWorker.cs`. Done when: work queued from any thread runs on one STA thread.
2. Make it the only way `ShellDesktopIcons` reaches the shell. Done when: a call from a thread-pool thread succeeds.

- [ ] Add `StaWorker`: a dedicated background thread set to `ApartmentState.STA` before it starts, with a blocking queue, `T Run<T>(Func<T> work)` that runs `work` on that thread and returns its result or rethrows its exception on the caller, and `Dispose` that ends the thread within 2 seconds. A call made from the worker's own thread runs inline. Done when: unit tests show `work` sees `ApartmentState.STA` and the same managed thread id for calls from three different threads, an exception reaches the caller with its type intact, and the thread is gone after `Dispose`. Cheaper substitute: `Task.Run`, whose thread-pool threads are MTA and make `DesktopView.Open` throw in production while fake-based tests pass.
- [ ] Give `ShellDesktopIcons` one `StaWorker` and route `ReadIcons`, `SetPositions`, `AutoArrange` and `TryTurnAutoArrangeOff` through `Run` (§3 to §5 implement them inside it). Done when: a `RealDesktop` test calls `ReadIcons` from a thread-pool thread (`Task.Run`) and gets the icons. Cheaper substitute: requiring every caller to be on the UI thread, which freezes the window during a restore.
- [ ] `ShellDesktopIcons` is disposable and disposes its worker; `Composition` (`D03 T01 §3`) owns one for the life of the process. Done when: a test disposes it and a later call throws `ObjectDisposedException`. Cheaper substitute: a thread that outlives the tray app's exit.
- [ ] Add `tests/Berth.Tests/Desktop/StaWorkerTests.cs`. Done when: all pass, and the apartment test fails when the thread is not set to STA. Cheaper substitute: none needed.
- [ ] Commit: `desktop: STA worker that owns every shell call`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*StaWorkerTests"` passes, and fails when the worker thread's apartment state is left as MTA.

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
