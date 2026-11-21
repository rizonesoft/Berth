---
schema_version: 1
id: automatic-restore
domain: 06-auto
status: active
title: "TODO-01 -- Automatic restore"
depends_on: ["layout-core", "desktop-access", "application-shell", "main-window"]
---

# TODO-01 -- Automatic restore

> **Goal:** With "Restore automatically" on, the icons return to their saved places after the monitor arrangement changes or Explorer restarts, with no click, and a failure nobody would otherwise see shows one balloon.

> **Current state:** Measured 2026-11-15. The setting exists in `Settings.RestoreAutomatically` (`D01 T01 §6`) with a switch (`D05 T01 §11`) and a tray menu item (`D04 T01 §8`), and nothing consumes it. The tray app has no window that receives system messages.

## Inputs

- `design/SPEC.md` §12 (the balloon and the icon after an Explorer restart), §16 (automatic restore), §18 (messages).
- `design/tokens.json` `timing.displayChangeSettleSeconds` and `timing.explorerRestartSettleSeconds`.
- -> XREF: D01 T01 §10 -- `LayoutService.Restore` does the work
- -> XREF: D02 T01 §6 -- the monitors are read again when the timer fires
- -> XREF: D04 T01 §7 -- the tray icon that is added again and shows the balloon
- -> XREF: D05 T01 §6 -- the status line shows the result when the window is open
- -> XREF: D07 T01 §5 -- the acceptance sweep covers SPEC §16
- -> XREF: D99 T01 §2 -- the owner's walkthrough on real monitors

## Outcome

- Posting a display change to the running tray app restores the active layout once, 3 seconds after the last message, and only while the setting is on.
- A refused automatic restore shows the SPEC §12 balloon once, and a success shows nothing.

**Adjacency:** list=not-applicable (no records of its own); document=not-applicable (nothing carried); settings=applicable @ D06 T01 §2; reporting=not-applicable (no summaries); notifications=applicable @ D06 T01 §3; permissions=applicable @ D06 T01 §3; audit=applicable @ D06 T01 §2; exchange=not-applicable (nothing imported or exported); reverse=applicable @ D06 T01 §5

**Adjacency rationale:** This file is the named consumer of the "Restore automatically" setting. The balloon is Berth's only notification, shown only for a refusal or failure. Every automatic restore writes a log line, and its "Undo" is offered in the open window.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Message window for display change and Explorer restart | D04 T01 §5 | [ ] |
| 2 | §2 | Settle timer and the automatic restore | §1, D01 T01 §10, D02 T01 §6 | [ ] |
| 3 | §3 | Failure balloon | §2, D04 T01 §7 | [ ] |
| 4 | §4 | Tray icon added again after Explorer restarts | §1, D04 T01 §7 | [ ] |
| 5 | §5 | Result shown in the open window and the tooltip | §2, D05 T01 §6 | [ ] |

## 1. Message window for display change and Explorer restart

A hidden top-level window that turns three system messages into events.

**Phase:** 6
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `SystemEvents` in `src/Berth/App/SystemEvents.cs`. Done when: a posted message raises its event.
2. Register `TaskbarCreated`. Done when: the registered message id raises `ExplorerRestarted`.

- [x] Add `SystemEvents` creating a hidden top-level window (not message-only, since `WM_DISPLAYCHANGE` and `TaskbarCreated` are broadcast only to top-level windows) through `HwndSource` with zero size and no taskbar button, exposing `Handle`, `event Action? DisplayChanged`, `event Action? ExplorerRestarted` and `event Action<string?>? SettingChanged`. Done when: a test on the app fixture creates it and finds a non-zero handle and no visible window. Cheaper substitute: a message-only window, which never receives these broadcasts.
- [x] Raise `DisplayChanged` for `WM_DISPLAYCHANGE`, and refresh `TrayIcon`'s tooltip in `TrayApp` when the monitor arrangement changes. Done when: a test posts `WM_DISPLAYCHANGE` to `Handle` with `PostMessage` and sees the event and the refreshed tray tooltip. Cheaper substitute: `Microsoft.Win32.SystemEvents.DisplaySettingsChanged`, whose hidden thread and window are harder to test and to shut down. **Corrected 2026-11-20:** receives the display-change tooltip refresh acceptance check from `D04 T01 §7`.
- [x] Register `TaskbarCreated` with `RegisterWindowMessage` and raise `ExplorerRestarted` for it. Done when: a test posts the registered id and sees the event. Cheaper substitute: polling for an Explorer process.
- [x] Raise `SettingChanged` with the message's string for `WM_SETTINGCHANGE`, and connect it to `TaskbarTheme.OnSettingChange` (`D04 T01 §6`) and `ThemeResources` (`D04 T01 §5`) in `TrayApp`. Done when: a test sends `WM_SETTINGCHANGE` to its own `Handle` synchronously with `SendMessageTimeout` (a 2 second bound, the string `ImmersiveColorSet` pinned for the call; a pointer-carrying message must never be posted asynchronously) and sees the event with that string. Cheaper substitute: `PostMessage` with a string pointer, which Windows does not support for this message; or leaving the tray icon's theme switch unconnected.
- [x] Dispose destroys the window. Done when: after dispose `IsWindow(Handle)` is `false`. Cheaper substitute: none needed.
- [x] Add `tests/Berth.Tests/App/SystemEventsTests.cs`. Done when: all pass, and the display test fails when the hook ignores `WM_DISPLAYCHANGE`. Cheaper substitute: changing the real display resolution in a test.
- [x] Commit: `app: message window for display change and Explorer restart`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SystemEventsTests"` passes, and fails when `WM_DISPLAYCHANGE` no longer raises `DisplayChanged`.

> **Started:** 2026-11-21

## 2. Settle timer and the automatic restore

Waits for 3 quiet seconds, then restores the layout of the arrangement connected at that moment (SPEC §16).

**Phase:** 6
**Needs:** none
**Surface:** no

**Build order:**

1. Add `AutoRestore` in `src/Berth/App/AutoRestore.cs` with an injected timer. Done when: a burst of triggers gives one restore.
2. Honour the setting and the no-layout rule. Done when: neither restores nor saves.

- [ ] Add `AutoRestore(LayoutService service, SettingsStore settings, Log log, ITimerFactory timers, TimeSpan settle)` with `Trigger(string reason)`: (re)start a one-shot timer of `settle` (from `timing.displayChangeSettleSeconds`), so the restore runs once, `settle` after the last trigger. Done when: a unit test with a fake timer fires five triggers and sees one restore after the last one's interval, none before. Cheaper substitute: restoring on every message, while Explorer is still rearranging.
- [ ] When the timer fires, read `Settings.RestoreAutomatically` then: off does nothing and logs nothing; on calls `LayoutService.Restore("automatic, " + reason)` off the UI thread and raises `Finished(OperationResult result, string reason, bool silent)`. `LayoutService` writes the one log line, ending ` (automatic, <reason>)` (`D01 T01 §16`); this class writes none. Done when: unit tests cover off (no call) and on (one call, one event, exactly one log line with the origin). Cheaper substitute: reading the setting once at startup, so the switch needs a restart.
- [ ] Treat `Refused` with `NoLayout` as silent: `Finished` with `silent` `true`, no balloon, and nothing saved (SPEC §16 "does nothing and saves nothing"). `Restore` checks for a layout before auto arrange (`D01 T01 §10`), so an arrangement with no layout is silent even while auto arrange is on. Done when: unit tests on an arrangement with no layout, with auto arrange off and on, see no layout file created and `silent` `true`. Cheaper substitute: saving the scrambled desktop as the new arrangement's layout, or a balloon about auto arrange for monitors that have nothing to restore.
- [ ] Never run two automatic restores at once: a trigger that arrives while one is running restarts the timer for after it. Done when: a unit test with a slow fake restore and a trigger mid-way sees two sequential restores, never overlapping. Cheaper substitute: relying on `OperationLock` alone, which would make the second a `Busy` balloon.
- [ ] Connect `SystemEvents.DisplayChanged` (reason `display change`) and `SystemEvents.ExplorerRestarted` (reason `Explorer restart`, settle from `timing.explorerRestartSettleSeconds`) to `Trigger` in `TrayApp`. Done when: a test on the app fixture posts `WM_DISPLAYCHANGE` to the message window with the setting on and a saved layout for the fake arrangement, and the fake desktop is restored after the (shortened) settle time. Cheaper substitute: wiring only one of the two triggers.
- [ ] Add `tests/Berth.Tests/App/AutoRestoreTests.cs`. Done when: all pass, and the burst test fails when the timer is not restarted by a later trigger. Cheaper substitute: tests that sleep for real seconds.
- [ ] Commit: `app: settle timer and the automatic restore`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*AutoRestoreTests"` passes, and fails when five triggers in a burst cause more than one restore.

## 3. Failure balloon

The one notification Berth shows: an automatic restore that was refused or failed (SPEC §12, last bullet; §16).

**Phase:** 6
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §12, last bullet, and §16, fourth bullet.
**Job:** the user learns that their icons were not put back, and why, when nothing else would tell them.
**Treatment:** one balloon per trigger with the SPEC title and the reason; clicking it opens the window. A success is silent. Cheaper substitute that fails: a balloon on every successful restore.
**Chrome:** `NotifyIcon.ShowBalloonTip` on the existing tray icon, with the warning icon.

**Build order:**

1. Add `BalloonPolicy` (pure). Done when: it says when to show and what.
2. Show it through the tray icon. Done when: a refused automatic restore shows one balloon and a click opens the window.

- [ ] Add `BalloonPolicy.For(OperationResult result)` in `src/Berth/App/BalloonPolicy.cs` returning `null` for `Restored` (including one with missing icons) and for `Refused` with `NoLayout`, and otherwise the title `Berth could not restore your icons` with the text `Messages.Refusal(result)`, or for a `Failed` restore whose icons did not stay the SPEC §8 partial sentence from `Messages.Status`. Done when: unit tests cover success, success with missing icons, no layout, auto arrange, unreachable desktop, busy, a failed write and icons that did not stay, comparing title and text. Cheaper substitute: a balloon when saved icons are merely missing, which SPEC §16 counts as success.
- [ ] Add `TrayIcon.ShowBalloon(string title, string text)` and call it from `AutoRestore.Finished` when the policy returns a balloon. Done when: a test on the app fixture with auto arrange on in the fake triggers an automatic restore and sees exactly one `ShowBalloon` call through a seam, with the SPEC text. Cheaper substitute: a message box from a background trigger, which steals focus.
- [ ] Clicking the balloon raises `OpenRequested` (`NotifyIcon.BalloonTipClicked`). Done when: a test invokes the handler and the window factory's `BringToFront` is called. Cheaper substitute: a balloon that leads nowhere.
- [ ] Assert the log holds exactly one line per automatic restore, written by `LayoutService`: `WARN` for a refusal, `FAIL` for a failure, `INFO` for a success, each ending ` (automatic, <reason>)` (SPEC §16). Done when: a test reads exactly one new line, `WARN Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again. (automatic, display change)`. Cheaper substitute: a second line from this layer, so every automatic restore is logged twice.
- [ ] Add `tests/Berth.Tests/App/BalloonTests.cs`. Done when: all pass, and the success test fails when a balloon is returned for `Restored`. Cheaper substitute: none needed.
- [ ] Commit: `app: failure balloon`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*BalloonTests"` passes, and fails when a successful automatic restore produces a balloon.

## 4. Tray icon added again after Explorer restarts

When Explorer restarts, the notification area is rebuilt empty: Berth puts its icon back (SPEC §12).

**Phase:** 6
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `TrayIcon.Readd()`. Done when: it hides and shows the icon.
2. Call it on `ExplorerRestarted`. Done when: the event leads to one call.

- [ ] Add `TrayIcon.Readd()`: set `Visible` `false` then `true`, and reapply the icon and tooltip. Done when: a test calls it and reads `Visible` `true` with the same tooltip. Cheaper substitute: creating a second `NotifyIcon`, which leaves two after a partial restart.
- [ ] Call `Readd()` when `SystemEvents.ExplorerRestarted` fires, before the automatic restore's timer starts. Done when: a test posts the registered `TaskbarCreated` message and counts one `Readd` through a seam. Cheaper substitute: none needed.
- [ ] Add the two cases to `tests/Berth.Tests/App/TrayIconTests.cs`. Done when: they pass, and the event test fails when `ExplorerRestarted` is not connected. Cheaper substitute: restarting Explorer in a test (`AGENTS.md`).
- [ ] Commit: `app: tray icon added again after Explorer restarts`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*TrayIconTests"` passes, and fails when `TaskbarCreated` does not lead to `TrayIcon.Readd`.

## 5. Result shown in the open window and the tooltip

An automatic restore is visible afterwards: in the status line with "Undo" when the window is open, and in the tooltip (SPEC §16, last bullet).

**Phase:** 6
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §16, last bullet, and §8, the "A restore just finished" row.
**Job:** a user who was watching sees what Berth just did and can take it back.
**Treatment:** the same status sentence and Undo as a manual restore. Cheaper substitute that fails: an automatic restore the open window never mentions.
**Chrome:** the status line of `D05 T01 §6`, unchanged.

**Build order:**

1. Route `AutoRestore.Finished` to the window. Done when: the status line shows the result.
2. Refresh the window and tooltip. Done when: the map shows icons in place after the restore.

- [ ] In `TrayApp`, pass every non-silent `AutoRestore.Finished` result to `StatusViewModel.SetResult` when the window exists, on the UI thread, and call `MainViewModel.Refresh()`. Done when: a test on the app fixture with the window open triggers an automatic restore and reads `Restored 3 icons.` and a visible `Undo`. Cheaper substitute: updating the status line from the background thread.
- [ ] `Undo` after an automatic restore works exactly as after a manual one. Done when: the same test clicks `Undo` and the fake desktop returns to its pre-restore positions. Cheaper substitute: none needed.
- [ ] After a monitor change, refresh the tray tooltip so it names the new arrangement's layout or says there is none. Done when: a test changes the fake monitors, triggers, and reads the new tooltip. Cheaper substitute: a tooltip that keeps naming the old arrangement.
- [ ] Add a render test writing `status-auto-restored` in light and dark. Done when: `pwsh scripts/render-window.ps1` writes both. Cheaper substitute: none needed.
- [ ] Add `tests/Berth.Tests/App/AutoRestoreWindowTests.cs`. Done when: all pass, and the status test fails when the result is not routed to the window. Cheaper substitute: none needed.
- [ ] Commit: `app: automatic restore shown in the window and tooltip`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*AutoRestoreWindowTests"` passes, and fails when an automatic restore leaves the open window's status line unchanged.

## Verification

- With the tray app running on the build machine and the setting on, posting `WM_DISPLAYCHANGE` to its message window restores a moved probe icon about 3 seconds later, and with auto arrange forced on in a fake it shows one balloon.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
