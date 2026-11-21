---
schema_version: 1
id: main-window
domain: 05-window
status: active
title: "TODO-01 -- Main window"
depends_on: ["layout-core", "desktop-access", "application-shell"]
---

# TODO-01 -- Main window

> **Goal:** Every control and state of the main window in `design/SPEC.md` §5 to §11 works on real data: the layouts pane, the header and its actions, the position map with its marker states and readout, the status line, the notices, the rename and delete dialogs and the two switches, in light, dark and high contrast.

> **Current state:** Measured 2026-11-15. `D04 T01 §9` gives an empty `MainWindow` with two hosts (`LayoutsHost`, `DetailHost`), and `D04 T01 §11` a renderer. `LayoutService`, `LayoutComparison`, `Messages` and `Formats` come from `D01 T01`; `ShellDesktopIcons` and `SystemMonitors` from `D02 T01`. No view or view model exists.

## Inputs

- `design/SPEC.md` §1 (policy), §3 (shared rules), §5 to §11, §18 (messages), §19 (states to draw).
- `design/tokens.json`, `design/mockups/main-window-light.png`, `design/mockups/main-window-dark.png` and their HTML sources.
- `DesktopCoordinates` (`D02 T01 §3`): every position this file sees is already in physical pixels of the virtual desktop.
- -> XREF: D01 T01 §13 -- `LayoutComparison` supplies every marker's state
- -> XREF: D02 T01 §5 -- the auto arrange notice calls `TryTurnAutoArrangeOff`
- -> XREF: D04 T01 §9 -- the window frame and hosts this file fills
- -> XREF: D06 T01 §5 -- the automatic restore shows its result in the status line built here
- -> XREF: D07 T01 §3 -- the README's screenshots are renders of this window

## Outcome

- With the tray app running on the build machine, the window lists the saved layouts, plots the active one against the live desktop, and saves, restores, undoes, renames, reverts and deletes with every result worded as SPEC says.
- Every state in SPEC §19 has a render in `build/renders/` and a test.

**Adjacency:** list=applicable @ D05 T01 §2; document=not-applicable (nothing carried or printed); settings=applicable @ D05 T01 §11; reporting=not-applicable (the status line is the only summary); notifications=not-applicable (the balloon is owned by D06 T01 §3); permissions=applicable @ D05 T01 §7; audit=not-applicable (the log lines are written by D01 T01 §15); exchange=not-applicable (SPEC §20 rules out import and export); reverse=applicable @ D05 T01 §6

**Adjacency rationale:** The layouts pane is how a layout is found. The two switches are the window's settings, each with a consumer (`D06 T01 §2` and the `Run` value). Refusals are drawn as notices and disabled controls in §7. The status line's "Undo" and the More menu's "Revert to previous save" and "Delete" are the reverse of each create.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Window view model and desktop polling | D04 T01 §9, D01 T01 §16, D02 T01 §8 | [x] |
| 2 | §2 | Layouts pane | §1, D04 T01 §11 | [x] |
| 3 | §3 | Detail header and action buttons | §2 | [x] |
| 4 | §4 | Position map: monitors and markers | §3, D01 T01 §13 | [x] |
| 5 | §5 | Marker states, selection and readout | §4 | [x] |
| 6 | §6 | Status line with Undo and Retry | §3 | [x] |
| 7 | §7 | Notices and the controls they disable | §6 | [x] |
| 8 | §8 | Rename dialog | §3 | [x] |
| 9 | §9 | Revert to previous save and delete | §8 | [x] |
| 10 | §10 | Start with Windows registry value | D00 T01 §3 | [x] |
| 11 | §11 | Settings switches and version caption | §2, §6, §10, D01 T01 §18 | [x] |
| 12 | §12 | Keyboard order and automation names | §5, §7, §9, §11 | [ ] |
| 13 | §13 | High contrast and 150% scale | §12 | [ ] |
| 14 | §14 | UI Automation smoke of the running window | §13 | [ ] |

## 1. Window view model and desktop polling

The state behind the window, testable with fakes and no window: the rows, the selection, the comparison, and the 5 second read that runs only while visible (SPEC §4, last bullet).

**Phase:** 5
**Needs:** none
**Surface:** no

**Build order:**

1. Add `MainViewModel` in `src/Berth/Window/MainViewModel.cs`. Done when: it lists layouts and selects the right row.
2. Add the poller. Done when: it reads only while visible.

- [x] Add `ObservableObject` (an `INotifyPropertyChanged` base with `Set`) and `RelayCommand` (an `ICommand` with `CanExecute`) in `src/Berth/Window/Mvvm/`. Done when: unit tests see `PropertyChanged` raised once per change and `CanExecuteChanged` raised by `RaiseCanExecuteChanged`. Cheaper substitute: a third-party MVVM package for two small classes (foundation plan, "Tech stack").
- [x] Add `LayoutRow` (`Key`, `Name`, `Subtitle` from `Formats.MonitorCount` and `Formats.IconCount` joined by ` · `, `IsActive`, `IsUnsaved`) and `MainViewModel.Rows`, built from `LayoutStore.LoadAll()` and the current arrangement key: the active layout first, the rest by name ignoring case, and a first row `This arrangement` with subtitle `Not saved yet` when the current arrangement has no layout (SPEC §5). Done when: unit tests cover no layouts, an active layout, and an unsaved arrangement beside two saved ones. Cheaper substitute: sorting by file date.
- [x] Add `Selected` with the opening rule: the active row, or the `This arrangement` row when unsaved; after a reload, keep the same key selected when it still exists, else fall back to the opening rule. Done when: unit tests cover opening, a reload that keeps the selection, and a deletion of the selected layout. Cheaper substitute: selecting index 0 after every reload, which jumps the user's selection every 5 seconds.
- [x] Add `DesktopState` (`Icons`, `Monitors`, `AutoArrange`, `Unavailable`, `CheckedAt`) and `MainViewModel.Refresh()`, which reads `IDesktopIcons` and `IMonitors` off the UI thread, reloads the rows, and rebuilds the selected layout's `LayoutComparison` (`Compare` for the active layout, `Neutral` for another arrangement or an unreachable desktop, `Unsaved` for the unsaved row). Done when: unit tests with fakes cover each of the four comparison kinds. Cheaper substitute: reading the shell on the UI thread.
- [x] Add `DesktopPoller` in `src/Berth/Window/DesktopPoller.cs`: calls `Refresh` when started, every `Berth.VisiblePollSeconds` while started, and never overlaps two reads; `MainWindow` starts it on `IsVisibleChanged` to `true` and on `Activated`, and stops it when hidden. Done when: a unit test with a fake timer sees one read on start, one per tick, none after stop, and a second tick during a slow read skipped. Cheaper substitute: a timer that keeps reading the shell while the window is hidden (SPEC §4).
- [x] Add `tests/Berth.Tests/Window/MainViewModelTests.cs` and `DesktopPollerTests.cs`. Done when: all pass, and the hidden-window test fails when the poller ignores `Stop`. Cheaper substitute: testing the view model only through the rendered window.
- [x] Commit: `window: view model and desktop polling`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*MainViewModelTests"` and `--filter-class "*DesktopPollerTests"` pass, and the poller test fails when a stopped poller still reads.

> **Started:** 2026-11-20

> **Verified:** 2026-11-21 | §1 | afd8b118a65ab06fc924bbdc996e79e36d1f9dc4
> **Review:** `docs/reviews/d05-t01-s1.json`

## 2. Layouts pane

The left pane: heading, one row per layout, the Active badge and the selection (SPEC §5).

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §5, bullets one to six.
**Job:** the user sees which layouts exist and which one belongs to the monitors connected now.
**Treatment:** a list with name, a second line and the Active badge; the unsaved arrangement is its own first row. Cheaper substitute that fails: hiding the current arrangement when it has no layout, leaving nothing to save from.
**Chrome:** a Fluent `ListBox` with the theme's selection, a 3 px accent marker, and badge colours from the token dictionaries.

**Build order:**

1. Add `LayoutsPane.xaml` in `src/Berth/Window/`. Done when: rows render from `MainViewModel.Rows`.
2. Render the SPEC §19 states. Done when: five PNGs exist in light and dark.

- [x] Add `src/Berth/Window/LayoutsPane.xaml` (a `UserControl`) with the heading `Layouts` in the `bodyStrong` style and a `ListBox` bound to `Rows` and `Selected`, hosted in `MainWindow.LayoutsHost`. Done when: a test on the app fixture with three rows finds three `ListBoxItem`s and the heading text. Cheaper substitute: a `StackPanel` of buttons, which loses arrow-key selection (SPEC §5).
- [x] Give each row the name (`bodyStrong`), the subtitle (`caption`, `Berth.TextSecondary`), and for `IsActive` the badge `Active` (`Berth.ActiveBadgeFill`, `Berth.ActiveBadgeText`, radius `Berth.RadiusBadge`, `caption` at weight 600). Done when: a test finds the badge element only in the active row and its text is `Active`. Cheaper substitute: colouring the active row's name, which is colour alone (SPEC §1).
- [x] Draw the selected row with the theme's list selection plus a 3 px marker on its left edge in the theme's accent brush. Done when: a test selects the second row and finds the marker visible only there. Cheaper substitute: a custom selection colour that ignores the Windows accent (SPEC §1 "Fluent controls stay Fluent").
- [x] Do not list a layout whose file is unreadable; expose `MainViewModel.Unreadable` for the notice of §7. Done when: a test with one corrupt file sees it absent from the rows and present in `Unreadable`. Cheaper substitute: a row that crashes when selected.
- [x] Add render tests (trait `Render`) writing `layouts-none`, `layouts-one`, `layouts-several`, `layouts-active-saved` and `layouts-active-unsaved` in light and dark to `build/renders/` (SPEC §19 "Layouts pane"). Done when: `pwsh scripts/render-window.ps1` writes the ten files and each differs from the empty frame. Cheaper substitute: rendering only the mockup's state.
- [x] Add `tests/Berth.Tests/Window/LayoutsPaneTests.cs`. Done when: all pass, and the badge test fails when the badge is shown on every row. Cheaper substitute: none needed.
- [x] Commit: `window: layouts pane`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutsPaneTests"` passes, and fails when the Active badge appears on a row that is not active.

> **Started:** 2026-11-20

> **Verified:** 2026-11-21 | §2 | 4fd528d7bb2d8dff5a94243f48adb0ce3030567a
> **Review:** `docs/reviews/d05-t01-s2.json`

## 3. Detail header and action buttons

The right pane's header: title, the monitors line, and Restore, Save current layout and More, which differ by the kind of row selected (SPEC §6).

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §6.
**Job:** the user saves or restores the layout in front of them with one click.
**Treatment:** the title, one secondary line, and only the buttons that can work for the selected row. Cheaper substitute that fails: showing Restore on a layout saved for other monitors and failing after the click.
**Chrome:** Fluent `Button` (standard and accent styles) and a Fluent menu for More; no restyling.

**Build order:**

1. Add `DetailViewModel` and `DetailHeader.xaml`. Done when: the three header variants appear for the three row kinds.
2. Wire Save and Restore. Done when: both run off the UI thread and disable the buttons meanwhile.

- [x] Add `DetailViewModel` in `src/Berth/Window/DetailViewModel.cs` with `Title` (the name, or `Not saved yet`), `Subtitle` (`Formats.MonitorSizes`, then ` · saved ` and `Formats.When` for a saved layout), `Kind` (`Active`, `Unsaved`, `Other`), `ShowRestore`, `ShowSave`, `ShowMore`, `OtherArrangementLine` (the SPEC §6 sentence, only for `Other`) and `IsBusy`. Done when: unit tests cover the three kinds and compare the subtitle `2560 × 1440 and 1920 × 1080 · saved today, 09:14`. Cheaper substitute: one header for every kind with buttons disabled without explanation.
- [x] Add `src/Berth/Window/DetailHeader.xaml`: the title in the `title` style, the subtitle in `caption` with `Berth.TextSecondary`, and on the right `Restore` (standard), `Save current layout` (accent) and `More` (three dots, automation name `More options`), each visible per the view model, hosted at the top of `MainWindow.DetailHost` with padding from the tokens. Done when: a test per kind reads which buttons are visible. Cheaper substitute: hiding buttons with opacity, leaving them clickable.
- [x] Add `SaveCommand` and `RestoreCommand`: set `IsBusy`, run `LayoutService.Save()` or `Restore()` off the UI thread, publish the `OperationResult` to the status line (§6), refresh, and clear `IsBusy`. After a save, select the saved arrangement's row. Done when: unit tests with fakes see the layout file written, the fake desktop moved, the selection on the active row, and `IsBusy` `true` during and `false` after. Cheaper substitute: awaiting on the UI thread with `.Result`.
- [x] Disable the three buttons while `IsBusy` (SPEC §6, last bullet). Done when: a test holding a slow fake operation reads `IsEnabled` `false` on all three, and `true` afterwards. Cheaper substitute: allowing a second click to queue a second restore.
- [x] Add the More menu with `Rename`, `Revert to previous save` and `Delete` in that order, the commands left for §8 and §9, and `Revert to previous save` enabled only when the layout has a previous save. Done when: a test opens the menu and reads three items and the enabled state for both cases. Cheaper substitute: a menu with items that do nothing.
- [x] Add render tests writing `header-active`, `header-unsaved`, `header-other` and `header-busy` in light and dark (SPEC §19 "Detail header"). Done when: `pwsh scripts/render-window.ps1` writes the eight files. Cheaper substitute: none needed.
- [x] Add `tests/Berth.Tests/Window/DetailHeaderTests.cs`. Done when: all pass, and the kind test fails when `Restore` is visible for `Other`. Cheaper substitute: none needed.
- [x] Commit: `window: detail header and action buttons`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*DetailHeaderTests"` passes, and fails when "Restore" is shown for a layout saved for another arrangement.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §3 | 63f4470333619c402ffdd55e9a7017af187d46ed
> **Review:** `docs/reviews/d05-t01-s3.json`

## 4. Position map: monitors and markers

The map panel: the grid, each monitor to scale with its label, and one marker per saved icon (SPEC §7, bullets one to three).

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §7, bullets one to three and the last.
**Job:** the user sees where every icon is saved, on which monitor.
**Treatment:** monitors drawn to scale in their real arrangement with a marker per icon. Cheaper substitute that fails: a plain table of names and coordinates.
**Chrome:** a custom `MapPanel` drawing with brushes and sizes from the token dictionaries only.

**Build order:**

1. Add `MapGeometry` (pure) in `src/Berth/Window/Map/MapGeometry.cs`. Done when: desktop pixels map to panel points with known inputs.
2. Add the `PositionMap` control. Done when: monitors, labels and markers render.

- [x] Add `MapGeometry.Fit(IReadOnlyList<MonitorInfo> monitors, double panelWidth, double panelHeight, double padding, double labelHeight)` returning the scale and offset that fit the monitors' bounding box into the panel, centred, keeping proportions, with `padding` (`Berth.MapPadding`) on every side and `labelHeight` reserved above each monitor's top edge; and `ToPanel(int x, int y)`. Done when: unit tests cover one monitor, two side by side, a monitor at negative coordinates, a portrait monitor, and a panel taller than wide. Cheaper substitute: stretching to fill, which distorts the monitors.
- [x] Plot saved positions as they are stored: physical pixels of the virtual desktop, the same space as `MonitorInfo` bounds (`D02 T01 §3` converts at the shell boundary). The map does no coordinate conversion of its own beyond `MapGeometry.ToPanel`. Done when: a unit test with an icon at a monitor's top-left corner plots it at that rectangle's top-left corner, for a monitor at the origin and for one at negative `X`. Cheaper substitute: a second conversion in the window, which would disagree with Core's monitor numbering.
- [x] Add `MapGeometry.Clamp` placing an off-screen icon at the nearest point on the nearest monitor's edge (SPEC §7, last bullet). Done when: a unit test with an icon far outside gets a point on that monitor's border. Cheaper substitute: drawing off-screen markers outside the panel.
- [x] Add `src/Berth/Window/Map/PositionMap.xaml` and `.xaml.cs`: a panel with `Berth.MapBackground`, a 1 px `Berth.MapBorder` border with radius `Berth.RadiusCard`, a grid of `Berth.MapGrid` lines every `Berth.MapGridStep`, each monitor as a rectangle (`Berth.MonitorFill`, `Berth.MonitorStrokeThickness` (1.5 px) `Berth.MonitorStroke`, radius `Berth.RadiusMonitor`) and its label `1 · 2560 × 1440` in the `mono` style, with ` · primary` on the primary. Done when: a render test with two monitors shows two rectangles and both labels. Cheaper substitute: an image of a map.
- [x] Draw each `ComparedIcon` as a `ListBoxItem` in a `ListBox` whose items panel is a `Canvas`, positioned by `MapGeometry`, whose hit target is `Berth.MarkerTarget` (19 px) square and transparent, with the visible marker centred in it at `Berth.Marker` (11 px) with radius `Berth.RadiusMarker`, filled `Berth.MarkerNeutral` for now (SPEC §3: the marker is the one control below 32 px). Where two targets overlap, the one later in reading order is on top. Done when: a test with 42 icons finds 42 items and the first item's position equals `MapGeometry.ToPanel` of its saved position within 0.5 px. Cheaper substitute: drawing markers in `OnRender` with no elements, which assistive technology and tests cannot reach (SPEC §7 "The map is a list").
- [x] Re-layout on resize, keeping the minimum height `Berth.MapMinHeight`. Done when: a test resizes the window and the markers' positions change in proportion. Cheaper substitute: a fixed-size map.
- [x] Add `tests/Berth.Tests/Window/MapGeometryTests.cs` and `PositionMapTests.cs`. Done when: all pass, and the negative-coordinate test fails when the offset ignores the bounding box's origin. Cheaper substitute: none needed.
- [x] Commit: `window: position map with monitors and markers`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*MapGeometryTests"` and `--filter-class "*PositionMapTests"` pass, and the geometry test fails when a monitor at negative coordinates is drawn outside the panel.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §4 | a1427458fc38ab059b6d01e6d584310f03092029
> **Review:** `docs/reviews/d05-t01-s4.json`

## 5. Marker states, selection and readout

What a marker says about its icon, and the readout and count under the map (SPEC §7, the table and the bullets after it).

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §7, the state table, the selection, readout, count and automation bullets.
**Job:** the user sees which icons have moved or gone, and can ask about any one of them.
**Treatment:** three marker shapes with a word each, a selection ring and a readout line. Cheaper substitute that fails: three colours with no shape or word.
**Chrome:** marker templates per state from the token dictionaries; the readout in the `mono` styles.

**Build order:**

1. Add the three marker templates. Done when: each state draws its SPEC shape.
2. Add selection, the readout and the count. Done when: selecting a marker fills the readout with the SPEC text.

- [x] Template the marker by `IconState`: `InPlace` filled `Berth.MarkerInPlace`; `Moved` outlined 2 px `Berth.MarkerMoved` with a 1 px line in the same brush from the saved position to the icon's current position; `Missing` outlined 1 px dashed `Berth.MarkerMissing`; `Neutral` filled `Berth.MarkerNeutral` (SPEC §7 table). Done when: a test with one icon per state finds the fill and stroke properties of each template as specified. Cheaper substitute: one template with a colour trigger.
- [x] Show the selected marker in `Berth.MarkerSelected` with a ring of `Berth.MarkerSelectedRing` px (radius `Berth.RadiusMarkerSelectedRing`) and a dashed 1 px line from the marker down to the readout's top. Done when: a test selects an item and finds the ring and the line visible, and neither before. Cheaper substitute: the `ListBox` default highlight rectangle.
- [x] Add a tooltip with the icon's name on each marker. Done when: a test reads the `ToolTip` of an item. Cheaper substitute: none needed.
- [x] Add `IconReadout` in `src/Berth/Window/Map/IconReadout.cs` producing the two readout lines: the name, and `x 1436 · y 702 · monitor 1 · in place`, with the state word from the SPEC §7 table (omitted for `Neutral`) and ` · off screen` when flagged; with nothing selected the single line `Select an icon to see its position.`. Done when: unit tests compare each exact string. Cheaper substitute: wording built in XAML triggers.
- [x] Show the readout at the bottom left of the map (name in `monoStrong`, second line in `mono` with `Berth.TextSecondary`) and the count at the bottom right: `42 of 42 in place` for the active layout and `42 icons` otherwise. Done when: tests cover both count forms and the empty readout. Cheaper substitute: a count that says "all".
- [x] When the desktop is unreachable, draw the active layout's markers `Neutral` and the count as `42 icons` (SPEC §9). Done when: a test with the fake desktop `Unavailable` reads neutral markers. Cheaper substitute: showing every icon as missing.
- [x] Add render tests writing `map-in-place`, `map-moved`, `map-missing`, `map-selected`, `map-none-selected`, `map-neutral`, `map-off-screen` and `map-unreachable` in light and dark (SPEC §19 "Position map"). Done when: `pwsh scripts/render-window.ps1` writes the sixteen files. Cheaper substitute: none needed.
- [x] Add `tests/Berth.Tests/Window/MarkerTests.cs` and `IconReadoutTests.cs`. Done when: all pass, and the readout test fails when the state word is dropped. Cheaper substitute: none needed.
- [x] Commit: `window: marker states, selection and readout`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*MarkerTests"` and `--filter-class "*IconReadoutTests"` pass, and the readout test fails when "moved" is missing from a moved icon's readout.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §5 | 034a2671f86693e6bd600f5c7dc910d3524fc80e
> **Review:** `docs/reviews/d05-t01-s5.json`

## 6. Status line with Undo and Retry

The card under the map: one sentence chosen by the SPEC §8 table, with a time or an action on the right.

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §8.
**Job:** the user always knows what Berth just did and whether the icons are where they should be.
**Treatment:** one sentence with a square, and Undo or Retry only when it can act. Cheaper substitute that fails: a toast that disappears before it is read.
**Chrome:** a card in the theme's card brush with radius `Berth.RadiusCard` (**Corrected 2026-11-21:** Opus 5.5 visual design check aligned the status card radius with the map card above, 6 px `Berth.RadiusCard`, replacing `Berth.RadiusControl`); a Fluent hyperlink-style `Button` for the action.

**Build order:**

1. Add `StatusViewModel` (pure). Done when: every SPEC §8 row is chosen for its inputs.
2. Add `StatusLine.xaml` with the actions. Done when: Undo and Retry work.

- [x] Add `StatusViewModel` in `src/Berth/Window/StatusViewModel.cs` with `Sentence`, `RightText`, `Action` (`None`, `Undo`, `Retry`), fed by `SetBusy()`, `SetResult(OperationResult)`, `SetComparison(...)`, choosing by the order of the SPEC §8 table and wording through `Messages`. Done when: a unit test per table row compares the exact sentence and right side. Cheaper substitute: a second copy of the sentences in the view model.
- [x] Keep a just-finished sentence until the next operation, a selection change, or a refresh that finds a different comparison (SPEC §8, last paragraph). Done when: unit tests cover each of the three ends and a refresh with no change that keeps the sentence. Cheaper substitute: a timer that clears it.
- [x] Add `src/Berth/Window/StatusLine.xaml`: a 10 px square in `Berth.MarkerInPlace` with radius `Berth.RadiusMarker`, the sentence (`body`), and on the right the time (`caption`, `Berth.TextSecondary`) or the `Undo` or `Retry` button, hosted under the map in `MainWindow.DetailHost`. Done when: a test reads the sentence and the right-side element for a comparison state and a restored state. Cheaper substitute: none needed.
- [x] `Undo` runs `LayoutService.Undo()` off the UI thread and shows its result; it is offered only after a restore and while `LayoutService.CanUndo`. Done when: a test restores, clicks `Undo`, and sees the fake desktop back and the sentence `Put 3 icons back where they were before the restore.`. Cheaper substitute: an Undo that stays visible after it has been used.
- [x] `Retry` repeats the refused or failed operation, offered for `DesktopUnavailable`, `Busy` and a failed write, and not for `AutoArrange`, `NoLayout`, `NoPreviousSave` or `NothingToUndo` (**Corrected 2026-11-18:** D01 T01 §12 plan review: `NoPreviousSave` included among refusals that do not offer Retry). Done when: unit tests cover which refusals offer it, and a test retries a save after the fake desktop becomes available. Cheaper substitute: Retry on a refusal that retrying cannot fix.
- [x] Show results from the tray menu's `OperationFinished` (`D04 T01 §8`) in the same status line when the window is open. Done when: a test raises the event with a `Restored` result and reads the sentence. Cheaper substitute: tray operations that the open window never mentions.
- [x] Add render tests writing one PNG per SPEC §8 row in light and dark, named `status-<row>`. Done when: `pwsh scripts/render-window.ps1` writes them. Cheaper substitute: none needed.
- [x] Add `tests/Berth.Tests/Window/StatusViewModelTests.cs` and `StatusLineTests.cs`. Done when: all pass, and the persistence test fails when a refresh with no change clears the sentence. Cheaper substitute: none needed.
- [x] Commit: `window: status line with Undo and Retry`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*StatusViewModelTests"` and `--filter-class "*StatusLineTests"` pass, and fail when a sentence differs from the `design/SPEC.md` §8 table.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §6 | 7cdebeed8640a20bcc7096082ad231164d3d6865
> **Review:** `docs/reviews/d05-t01-s6.json`

## 7. Notices and the controls they disable

The three notices of SPEC §9, their buttons, and what each one disables.

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §9.
**Job:** the user learns why a restore cannot work before clicking it, and can fix it in one click where Berth can.
**Treatment:** a bar with an icon, a sentence and one button, stacked most serious first. Cheaper substitute that fails: letting the restore fail and explaining afterwards.
**Chrome:** the Fluent `InfoBar` style when the WPF theme offers one, else a bar with fill and border from `Berth.NoticeWarning*` and `Berth.NoticeError*`.

**Build order:**

1. Add `NoticesViewModel` (pure). Done when: the right notices appear in the right order.
2. Add `Notices.xaml` and the three buttons. Done when: each button does its SPEC job.

- [x] Add `Notice(NoticeKind Kind, bool IsError, string Text, string ButtonText)` and `NoticesViewModel.For(DesktopState desktop, IReadOnlyList<UnreadableFile> unreadable)` returning, errors first: the unreachable desktop, each unreadable file (with its file name), then auto arrange, with the SPEC §9 texts and button texts. Done when: unit tests cover none, each alone, and auto arrange with an unreadable file (two, in order). Cheaper substitute: showing only the first notice.
- [x] Add `src/Berth/Window/Notices.xaml`: an `ItemsControl` between the header and the map, each notice with an icon (a warning or error glyph from the Segoe Fluent Icons font, with the word in its automation name), the sentence and its button. Done when: a test with two notices finds two bars with their texts and buttons. Cheaper substitute: colour alone to tell a warning from an error.
- [x] `Turn it off` calls `IDesktopIcons.TryTurnAutoArrangeOff()` off the UI thread, refreshes, and when the read-back still says on leaves the notice and puts the SPEC §18 "Windows did not turn auto arrange off" message in the status line. Done when: tests with the fake cover both outcomes. Cheaper substitute: removing the notice without reading back.
- [x] `Retry` (unreachable desktop) calls `Refresh()` now. Done when: a test makes the fake available, clicks it, and the notice is gone. Cheaper substitute: waiting for the next poll.
- [x] `Open folder` starts File Explorer with `/select,` and the file's full path, through an `IFolderOpener` seam so tests do not open a window. Done when: a test clicks it and the fake opener received the layout file's path. Cheaper substitute: opening a real Explorer window in a test (`AGENTS.md`).
- [x] While auto arrange is on, disable `Restore` (window) with a tooltip repeating the notice; while the desktop is unreachable, disable `Restore` and `Save current layout` (SPEC §9). Done when: tests read `IsEnabled` and the tooltip in both conditions, and `Save current layout` stays enabled under auto arrange. Cheaper substitute: disabling with no explanation.
- [x] Add render tests writing `notice-none`, `notice-auto-arrange`, `notice-unreachable`, `notice-unreadable` and `notice-two` in light and dark (SPEC §19 "Notices"). Done when: `pwsh scripts/render-window.ps1` writes the ten files. Cheaper substitute: none needed.
- [x] Add `tests/Berth.Tests/Window/NoticesTests.cs`. Done when: all pass, and the order test fails when the warning is listed before the error. Cheaper substitute: none needed.
- [x] Commit: `window: notices and the controls they disable`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*NoticesTests"` passes, and fails when "Restore" stays enabled while auto arrange is on.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §7 | 7d1a036cb1fed662ee6aa34a2399b9cd0a79305a
> **Review:** `docs/reviews/d05-t01-s7.json`

## 8. Rename dialog

The More menu's Rename (SPEC §10, first bullet).

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §10, "Rename".
**Job:** the user gives a layout a name that means something, such as "Docked".
**Treatment:** a small dialog with the name selected, live validation under the box, Enter and Esc. Cheaper substitute that fails: accepting any text and failing after the click.
**Chrome:** a Fluent dialog window owned by the main window, with the theme's `TextBox` and buttons.

**Build order:**

1. Add `RenameViewModel` (pure). Done when: each rule gives its SPEC reason.
2. Add `RenameDialog.xaml` and wire the menu item. Done when: a rename changes the row and the file.

- [x] Add `RenameViewModel(string current, IEnumerable<string> others)` with `Name`, `Error` (the SPEC §10 reason, or empty) and `CanRename`, validating on every change with `LayoutNames.Validate`. Done when: unit tests compare `Enter a name.`, `A name can be at most 40 characters.` and `Another layout already has this name.`, and `CanRename` for each. Cheaper substitute: validating only on the button click.
- [x] Add `src/Berth/Window/RenameDialog.xaml`: title `Rename layout`, one `TextBox` holding the current name with all text selected and the focus, the reason under it, and `Rename` (default) and `Cancel` (cancel) buttons; `Rename` disabled while `CanRename` is `false`. Done when: a test opens it on the app fixture and reads the selected text, the focused element and the button states. Cheaper substitute: an inline text box in the list, which SPEC §10 does not describe.
- [x] Wire `Rename` in the More menu: open the dialog owned by the main window, and on `Rename` run `LayoutService.Rename` off the UI thread, show a failure in the status line, and refresh. Done when: a test renames a layout and reads the new name in the row, the header and the layout file. Cheaper substitute: changing the row's text without writing the file.
- [x] `Enter` renames when allowed and `Esc` cancels without change. Done when: tests send both keys. Cheaper substitute: none needed.
- [x] Add render tests writing `rename-valid`, `rename-empty`, `rename-too-long` and `rename-taken` in light and dark (SPEC §19 "Dialogs"). Done when: `pwsh scripts/render-window.ps1` writes the eight files. Cheaper substitute: none needed.
- [x] Add `tests/Berth.Tests/Window/RenameTests.cs`. Done when: all pass, and the taken-name test fails when the comparison is case-sensitive. Cheaper substitute: none needed.
- [x] Commit: `window: rename dialog`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*RenameTests"` passes, and fails when "Rename" is enabled for a name another layout already has.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §8 | d1c0819b6b2b4dbf183fdf82594e63a86b5acb26
> **Review:** `docs/reviews/d05-t01-s8.json`

## 9. Revert to previous save and delete

The other two More menu items (SPEC §10).

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §10, "Revert to previous save" and "Delete".
**Job:** the user takes back a save made by mistake, or removes a layout for monitors they no longer have.
**Treatment:** revert acts at once and says what it did; delete asks once, naming the layout, with Cancel as the default. Cheaper substitute that fails: a delete with no confirmation.
**Chrome:** the Fluent dialog window of §8 for the confirmation; the status line for the revert's result.

**Build order:**

1. Wire Revert. Done when: the map shows the previous save's positions and nothing moved on the desktop.
2. Add the delete confirmation. Done when: Delete removes the layout and Cancel does nothing.

- [x] Wire `Revert to previous save`: run `LayoutService.Revert` off the UI thread, show `Reverted to the save from 12 November.` in the status line, and refresh. Done when: a test saves twice, reverts, and reads the first save's positions in the map, with no `SetPositions` call on the fake. Cheaper substitute: a revert that also moves the icons, which SPEC §10 says it does not.
- [x] Add `src/Berth/Window/DeleteDialog.xaml`: title `Delete layout`, the text `Delete the layout "Docked"? Its saved positions are removed. This cannot be undone.` with the layout's name, and `Delete` and `Cancel`, where `Cancel` is both the default and the cancel button. Done when: a test opens it and reads the text with the name and `Cancel` as `IsDefault`. Cheaper substitute: `MessageBox.Show`, which does not follow the Fluent theme.
- [x] Wire `Delete`: on `Delete` run `LayoutService.Delete` off the UI thread and refresh; after deleting the active layout the selection is the `This arrangement` row and the icons are where they were. Done when: tests cover deleting the active layout and another arrangement's layout, and the fake desktop is unchanged. Cheaper substitute: leaving the deleted row selected.
- [x] `Cancel`, `Esc` and closing the dialog change nothing. Done when: a test cancels each way and the file still exists. Cheaper substitute: none needed.
- [x] Add a render test writing `delete-dialog` in light and dark. Done when: `pwsh scripts/render-window.ps1` writes the two files. Cheaper substitute: none needed.
- [x] Add `tests/Berth.Tests/Window/RevertDeleteTests.cs`. Done when: all pass, and the default-button test fails when `Delete` is the default. Cheaper substitute: none needed.
- [x] Commit: `window: revert to previous save and delete`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*RevertDeleteTests"` passes, and fails when "Delete" is the dialog's default button.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §9 | d45679ac4205ccadfc0e44e1c600d00f94d3b06d
> **Review:** `docs/reviews/d05-t01-s9.json`

## 10. Start with Windows registry value

The `Run` value behind the second switch, with the registry path as a parameter so no test writes the real one (SPEC §11).

**Phase:** 5
**Needs:** none
**Surface:** no

**Build order:**

1. Add `RunValue` in `src/Berth/Desktop/RunValue.cs`. Done when: it reads, sets and clears the value under a given key.
2. Test under a test key. Done when: the real `Run` key is never written by a test.

- [x] Add `RunValue(string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run", string valueName = "Berth", string? executablePath = null)` with `bool IsOn` (the value exists and, unquoted and compared ignoring case, equals the running executable's path from `Environment.ProcessPath` or the parameter). Done when: tests under `HKCU\Software\Berth.Tests\<guid>` cover missing, matching, quoted matching, and a value naming another path (off). Cheaper substitute: "the value exists", which shows On for a stale copy (SPEC §11).
- [x] Add `void Set(bool on)`: write the quoted path as `REG_SZ` or delete the value, then read back and throw `IOException` with the system's reason when the state is not what was asked. Done when: tests set on, read `IsOn` `true` and the exact quoted string, set off, and find the value gone. Cheaper substitute: writing without read-back.
- [x] Add `tests/Berth.Tests/Desktop/RunValueTests.cs`, each test creating and deleting its own key under `HKCU\Software\Berth.Tests`. Done when: all pass, and `git grep -n "CurrentVersion\\\\Run" tests/` finds no test that passes the real path. Cheaper substitute: a test that writes the owner's real `Run` key (`AGENTS.md`).
- [x] Commit: `desktop: Start with Windows registry value`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*RunValueTests"` passes, and fails when `IsOn` is `true` for a value naming another executable.

> **Started:** 2026-11-20

> **Verified:** 2026-11-21 | §10 | 803284ec2a092bf3736f72e42a69c265d82d1526
> **Review:** `docs/reviews/d05-t01-s10.json`

## 11. Settings switches and version caption

The two switches and the version at the bottom of the layouts pane (SPEC §11, §5 last bullet).

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §11 and §5, last bullet.
**Job:** the user turns automatic restore and start-up on or off and trusts that the switch shows the truth.
**Treatment:** two switches that apply at once and snap back with a reason when the write fails. Cheaper substitute that fails: a switch that flips while the setting does not.
**Chrome:** the Fluent `ToggleSwitch`-style `CheckBox` or `ToggleButton` the WPF Fluent theme provides, with the label on the left and no On or Off text.

**Build order:**

1. Add `SettingsViewModel` (pure). Done when: each switch writes, reads back and reverts on failure.
2. Add `SettingsPanel.xaml` at the bottom of the layouts pane. Done when: both switches and the caption render.

- [x] Add `SettingsViewModel(SettingChanges changes, SettingsStore store, RunValue run, StatusViewModel status)` with `RestoreAutomatically` and `StartWithWindows` (**Corrected 2026-11-18:** "Restore automatically" goes through `SettingChanges.SetRestoreAutomatically` and the `Run` value result through `SettingChanges.RecordStartWithWindows`, `D01 T01 §18`, which own the SPEC §18 log lines): a change writes through `SettingChanges` or `RunValue.Set`, reads back, and on failure returns the property to its old value and puts the SPEC §18 message in the status line. Done when: unit tests cover a successful change of each, a failing store, and a failing `RunValue` (a key the test made read-only or a fake). Cheaper substitute: an optimistic switch.
- [x] Load both from their sources on every `Refresh`, so a change made by the tray menu or outside Berth shows. Done when: a test changes `settings.json` on disk, refreshes, and reads the new switch state. Cheaper substitute: reading once at startup.
- [x] Add `src/Berth/Window/SettingsPanel.xaml` pinned to the bottom of the layouts pane under a 1 px divider: `Restore automatically` and `Start with Windows`, each a label on the left and the switch on the right, then the caption `Berth ` plus `AppInfo.Version` in `caption` with `Berth.TextSecondary`. Done when: a test finds two switches with those automation names and the caption text `Berth 0.1.0`. Cheaper substitute: a settings dialog, which SPEC §11 does not describe.
- [x] Add render tests writing `switches-on-off` and `switches-off-on` in light and dark. Done when: `pwsh scripts/render-window.ps1` writes the four files. Cheaper substitute: none needed.
- [x] Add `tests/Berth.Tests/Window/SettingsTests.cs`. Done when: all pass, and the failure test fails when the switch stays flipped after a failed write. Cheaper substitute: none needed.
- [x] Commit: `window: settings switches and version caption`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SettingsTests"` passes, and fails when a switch stays on after its write failed.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §11 | e26e6d5e7a5a86db61e269af999297648f37a658
> **Review:** `docs/reviews/d05-t01-s11.json`

## 12. Keyboard order and automation names

Every control reachable and named, and the map readable as a list (SPEC §3, §7).

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §3, third bullet, and §7, "The map is a list".
**Job:** a keyboard or screen reader user can do everything a mouse user can.
**Treatment:** Tab order in reading order, a name on every control, arrow keys in both lists. Cheaper substitute that fails: a mouse-only map.
**Chrome:** `AutomationProperties` on existing controls; no new visuals.

**Build order:**

1. Set tab order and names. Done when: a walk of the automation tree finds a name on every control.
2. Name the map's items. Done when: each reads as SPEC §7 says.

- [ ] Set the tab order: layouts list, the two switches, notices' buttons, `Restore`, `Save current layout`, `More`, the map, the status action. Done when: a test moves focus with `MoveFocus(Next)` from the first element and records exactly that order. Cheaper substitute: relying on declaration order, which the two hosts break.
- [ ] Give every interactive control an `AutomationProperties.Name`, and the map list the name `Saved icon positions`. Done when: a test walks the `AutomationPeer` tree of the window and fails on any `Button`, `ListBox`, `ListBoxItem`, `TextBox` or switch with an empty name. Cheaper substitute: names only on the icon-only button.
- [ ] Name each marker `Notes.txt, x 1436, y 702, monitor 1, in place` (the state word omitted for neutral markers, `off screen` added when flagged), and order the items in reading order. Done when: a test reads the names of three markers and the order of five. Cheaper substitute: the readout's dotted format, which a screen reader reads as "dot".
- [ ] Arrow keys move the selection in the layouts list and in the map (up and down through reading order; left and right do the same), and `Home` and `End` go to the first and last. Done when: a test sends the keys to each list and reads the selected item. Cheaper substitute: none needed.
- [ ] Check every interactive control is at least `Berth.ControlHeight` (32 px) high at 100% scale, except map markers, whose target must be exactly `Berth.MarkerTarget` (19 px) square (SPEC §3). Done when: a test measures them, fails on a smaller control, and fails on a marker target that is not 19 px. Cheaper substitute: none needed.
- [ ] Add `tests/Berth.Tests/Window/AccessibilityTests.cs`. Done when: all pass, and the name test fails when the `More` button's name is removed. Cheaper substitute: none needed.
- [ ] Commit: `window: keyboard order and automation names`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*AccessibilityTests"` passes, and fails when the "More" button has no automation name.

## 13. High contrast and 150% scale

The fidelity conditions of SPEC §1 that the earlier renders did not cover.

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §1 "Fidelity" and §2, last bullet.
**Job:** the window is as usable in a high contrast theme and on a scaled display as it is by default.
**Treatment:** system colours for Berth's own elements in high contrast, and a layout that does not clip at 150%. Cheaper substitute that fails: brand blues on a high contrast background.
**Chrome:** `Theme/Tokens.HighContrast.xaml`.

**Build order:**

1. Render in high contrast. Done when: no Berth token colour appears in the render.
2. Render at 150%. Done when: nothing is clipped at the minimum window size.

- [ ] Extend `WindowRenderer` with a `HighContrast` theme that selects the `HighContrast` dictionary and fixed stand-in system colours for the render. Done when: a render of the data state contains none of the light or dark token colours (checked by sampling the marker and badge pixels). Cheaper substitute: a claim that it "should work".
- [ ] Extend `WindowRenderer` with a `scale` parameter (1.0, 1.5) applied as a layout transform at render time. Done when: the 1.5 render is 1470 by 960 pixels. Cheaper substitute: none needed.
- [ ] Add a test that lays the window out at the minimum size (760 by 520) at both scales with the longest strings (a 40 character layout name, the two-notice state, the longest SPEC §8 sentence) and fails when any control lies outside its parent's bounds, when any text other than a layout's name is trimmed or clipped, or when a trimmed layout name (in a row or in the header) has no tooltip with the full name (SPEC §3: text wraps, and only a layout's name may trim). Done when: it passes, after fixing whatever it finds: wrapping for sentences, ellipsis plus tooltip for the two name elements. Cheaper substitute: testing only the default size.
- [ ] Record each layout fix in the commit message, and when a fix changes a measurement that `design/SPEC.md` states, change the SPEC first with a dated line (SPEC §1 "Design first"). Done when: `git log -1` for this section names what was fixed. Cheaper substitute: tuning in code only.
- [ ] Add render tests writing `data-high-contrast` and `data-150` (SPEC §1). Done when: `pwsh scripts/render-window.ps1` writes both. Cheaper substitute: none needed.
- [ ] Commit: `window: high contrast and 150% scale`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ContrastAndScaleTests"` passes, and fails when a 40 character layout name is clipped at the minimum window size.

## 14. UI Automation smoke of the running window

The built `berth.exe`, running for real, driven through UI Automation on a probe icon.

**Phase:** 5
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add a helper that starts the tray app and finds its window. Done when: it finds the window by automation.
2. Drive save, restore and undo. Done when: the probe icon moves as each button is invoked.

- [ ] Add `RunningApp` in `tests/Berth.Tests/Smoke/RunningApp.cs` with a static `Start(TestData data, string iconPrefix)`: starts `berth.exe` with `BERTH_DATA`, a random `BERTH_INSTANCE` and `BERTH_TEST_ICON_PREFIX` (the probe's prefix, so the app sees only the probe) in the child's own environment (first run, so the window opens), then waits up to 20 seconds for a top-level window named `Berth` owned by that process through `System.Windows.Automation`. Everything after the process starts is inside a `try` whose `catch` kills the process tree before rethrowing, so a failed or timed-out start-up never leaves a tray app behind; `Dispose` kills the tree too. Done when: a test finds the window; after dispose no process with that id remains; and a test that forces the window wait to time out (a 1 ms wait) also finds no process afterwards. Cheaper substitute: cleanup only in `Dispose`, which never runs when the constructor throws.
- [ ] Invoke `Save current layout` through its `InvokePattern`, wait for the status sentence to start `Saved`, and assert the layout file in the test's data folder lists exactly one icon, the probe. Done when: the test passes with `ProbeIcon` on the desktop. Cheaper substitute: sending mouse clicks at coordinates.
- [ ] Move the probe through `ScopedDesktopIcons` with the probe's prefix, wait for the status sentence `1 icon has moved since this layout was saved.`, invoke `Restore`, wait for a sentence starting `Restored`, and assert the probe is back; then invoke `Undo` and assert the probe is at the moved position. Skip with a clear message when auto arrange is on. Done when: the test passes on the build machine and leaves no probe file. Cheaper substitute: asserting only that buttons exist.
- [ ] Start `berth.exe` a second time with the same `BERTH_INSTANCE` and assert it exits 0 within 10 seconds and only one process remains. Done when: the assertion passes. Cheaper substitute: none needed.
- [ ] Add `tests/Berth.Tests/Smoke/WindowSmokeTests.cs` (traits `RealDesktop` and `Smoke`, in the `Desktop` collection so it never runs beside another interface test, and driving the window only through UI Automation patterns, never the real mouse or keyboard; **Corrected 2026-11-18:** owner instructions, AGENTS.md). Done when: it passes, and fails when `Restore` is wired to `Save`. Cheaper substitute: none needed.
- [ ] Commit: `test: UI Automation smoke of the running window`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*WindowSmokeTests"` passes on the build machine with auto arrange off, and fails when the "Restore" button is wired to the save command.

## Verification

- On the build machine, with the tray app running: every row of the SPEC §19 table has a render in `build/renders/`, and the smoke test saves, restores and undoes a probe icon through the real window.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
