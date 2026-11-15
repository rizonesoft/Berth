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
| 1 | §1 | Shell COM interface declarations | D00 T01 §2 | [ ] |
| 2 | §2 | The desktop's folder view | §1 | [ ] |
| 3 | §3 | Reading every icon's identity and position | §2, D01 T01 §8 | [ ] |
| 4 | §4 | Setting icon positions | §3 | [ ] |
| 5 | §5 | Auto arrange read and clear | §2 | [ ] |
| 6 | §6 | Monitor bounds, scale and primary | D01 T01 §8 | [ ] |
| 7 | §7 | Real-desktop round trip on a probe icon | §4, §5, §6, D01 T01 §11 | [ ] |
| 8 | §8 | One exception for an unreachable desktop | §3, §4, §5 | [ ] |

## 1. Shell COM interface declarations

The interop declarations, written from Microsoft's documentation and checked by a test that reads their identifiers.

**Phase:** 2
**Needs:** none
**Surface:** no

**Build order:**

1. Declare the interfaces in `src/Berth/Desktop/Interop/`. Done when: they compile with source-generated COM (`[GeneratedComInterface]`) or classic `[ComImport]`, whichever the installed SDK supports for these shapes.
2. Check the identifiers. Done when: a test compares each GUID with the documented value.

- [ ] Declare `IShellWindows` (only `FindWindowSW` needs a real signature; earlier vtable slots are placeholders in declaration order), `IServiceProvider`, `IShellBrowser` (through `QueryActiveShellView`), `IShellView`, `IFolderView`, `IFolderView2` (through `SetCurrentFolderFlags` and `GetCurrentFolderFlags`), `IShellFolder` (through `GetDisplayNameOf`) and `IEnumIDList` in `src/Berth/Desktop/Interop/`, one file each, with the interface identifier from Microsoft's documentation in a comment that names the documentation page. Done when: `dotnet build Berth.slnx` exits 0 with warnings as errors. Cheaper substitute: `dynamic` late binding through `Shell.Application`, which cannot reach `IFolderView`.
- [ ] Add `ShellConstants` in `src/Berth/Desktop/Interop/ShellConstants.cs` with `CLSID_ShellWindows`, `SID_STopLevelBrowser`, `CSIDL_DESKTOP`, `SWC_DESKTOP`, `SWFO_NEEDDISPATCH`, `SVGIO_ALLVIEW`, `SVSI_POSITIONITEM`, `SHGDN_FORPARSING`, `SHGDN_NORMAL` and `FWF_AUTOARRANGE`, each with its documented value. Done when: the file compiles and every constant has a comment naming where the value is documented. Cheaper substitute: numbers inline at each call.
- [ ] Add `tests/Berth.Tests/Desktop/InteropDeclarationTests.cs` asserting each declared interface's GUID equals the documented one (typed into the test independently) and that the vtable slot order of `IFolderView` matches the documented method order (by reflecting the declared methods). Done when: the test passes, and fails when two `IFolderView` methods are swapped. Cheaper substitute: trusting the declaration because it compiles.
- [ ] Commit: `desktop: shell COM interface declarations`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*InteropDeclarationTests"` passes, and fails when two methods of `IFolderView` are declared in the wrong order.

## 2. The desktop's folder view

The chain from `ShellWindows` to the desktop's `IFolderView`, in one method with every COM object released.

**Phase:** 2
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `DesktopView` in `src/Berth/Desktop/DesktopView.cs`. Done when: it returns a live `IFolderView` on the build machine.
2. Release what it acquires. Done when: a loop of 500 acquisitions does not grow the process handle count.

- [ ] Add `DesktopView.Open()` returning a disposable holder of `IFolderView` (and `IFolderView2` when the view supports it): create `ShellWindows`, call `FindWindowSW` with `CSIDL_DESKTOP`, `SWC_DESKTOP` and `SWFO_NEEDDISPATCH`, query `IServiceProvider` for `SID_STopLevelBrowser` as `IShellBrowser`, call `QueryActiveShellView`, and cast to `IFolderView` (foundation plan). Done when: a test on the real desktop calls `Open()` and `ItemCount(SVGIO_ALLVIEW)` returns a number at or above 0 without throwing. Cheaper substitute: `FindWindow("Progman")` and list-view messages, which the README forbids.
- [ ] Release every intermediate COM object (`Marshal.ReleaseComObject` or the generated wrappers' disposal) on success and on failure. Done when: a test opens and disposes the view 500 times and the process handle count afterwards is within 50 of the count before. Cheaper substitute: leaving release to the finaliser, which a long-running tray app pays for.
- [ ] Run the acquisition on an STA thread: `DesktopView.Open` asserts `Thread.CurrentThread.GetApartmentState() == ApartmentState.STA` and the test host runs these tests on an STA thread through a small `StaFact` helper in `tests/Berth.Tests/Sta.cs`. Done when: calling `Open` from an MTA thread throws `InvalidOperationException` with a message naming STA. Cheaper substitute: silently working on some threads and failing on others.
- [ ] Add `tests/Berth.Tests/Desktop/DesktopViewTests.cs` with those cases, marked with a trait `RealDesktop`. Done when: they pass on the build machine. Cheaper substitute: a test that is skipped everywhere.
- [ ] Commit: `desktop: the desktop's folder view`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*DesktopViewTests"` passes on a machine with a desktop session, and fails when `SWC_DESKTOP` is replaced by another window class constant.

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
- [ ] Return positions exactly as the view reports them, with no conversion (SPEC §3: physical pixels of the virtual desktop as the shell reports it). Done when: the header comment of `ShellDesktopIcons.cs` records, from a measurement on the build machine, what the origin of these coordinates is relative to the virtual screen, and `D05 T01 §4` consumes that note. Cheaper substitute: guessing a DPI conversion.
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

- [ ] Implement `SetPositions(IReadOnlyList<IconPosition> icons)`: enumerate the desktop's items once, build a map from parsing name (ignoring case) to item identifier list, and call `IFolderView.SelectAndPositionItems` with the matched items, their points and `SVSI_POSITIONITEM`. Done when: the §7 probe test moves its icon by this call. Cheaper substitute: one call per icon, which is slow and repaints the desktop each time.
- [ ] Pass only `SVSI_POSITIONITEM`, so the user's selection on the desktop is not changed. Done when: the §7 probe test asserts the probe icon is not selected afterwards (through `IFolderView.ItemCount(SVGIO_SELECTION)` being unchanged). Cheaper substitute: adding `SVSI_SELECT`, which deselects what the user had selected.
- [ ] Ignore, without error, an `IconPosition` whose `Id` is not on the desktop. Done when: the §7 test passes a made-up `Id` alongside the probe and the call succeeds. Cheaper substitute: throwing, which would fail every restore with a deleted file.
- [ ] Free every item identifier list in a `finally`, as in §3. Done when: the §3 leak test, extended to call `SetPositions` with the current positions (an identity write that moves nothing), stays within its bound. Cheaper substitute: leaking per restore.
- [ ] Commit: `desktop: setting icon positions`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ProbeIconTests"` (written in §7) is the proof; until §7 lands, `dotnet test --solution Berth.slnx --filter-class "*ReadIconsTests"` passes with the identity write added and the desktop's positions are equal before and after.

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

- [ ] Add `ProbeIcon` in `tests/Berth.Tests/Desktop/ProbeIcon.cs`: creates `berth-test-probe-<guid>.txt` in the user's desktop folder (`Environment.SpecialFolder.DesktopDirectory`), waits up to 10 seconds for an icon with that parsing name to appear in `ReadIcons`, and deletes the file in `Dispose`, also deleting any leftover `berth-test-probe-*.txt` older than one hour at start. Done when: after the test, and after a test that throws mid-way, no `berth-test-probe-*` file is on the desktop. Cheaper substitute: moving one of the owner's real icons.
- [ ] Add `tests/Berth.Tests/Desktop/ProbeIconTests.cs` (trait `RealDesktop`): record every icon's position, skip with a clear message when `AutoArrange` is `true`, set the probe to its current position plus 200 px on `X`, read back, and assert the probe moved to the requested position (or, when the read-back differs because the desktop snaps to its grid, to within one grid cell of it, recording which happened) and every other icon's position is unchanged. Done when: the test passes on the build machine. Cheaper substitute: asserting only that `SetPositions` did not throw.
- [ ] In the same test, pass a made-up `Id` alongside the probe and assert success (`§4`). Done when: the assertion is present and passes. Cheaper substitute: testing the unknown `Id` only against the fake.
- [ ] Run the full `LayoutService` cycle against the real implementations with `TestData`: save, move the probe, restore, assert the probe is back and `Missing` is 0, undo, assert the probe returned to the moved position. Done when: the test passes and the temp root holds the layout and no `undo.json` after the undo. Cheaper substitute: an end-to-end test that runs only against fakes.
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

## Verification

- On the build machine, `ProbeIconTests` saves, moves, restores and undoes a probe icon on the real desktop and leaves the desktop as it found it.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
