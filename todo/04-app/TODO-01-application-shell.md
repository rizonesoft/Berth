---
schema_version: 1
id: application-shell
domain: 04-app
status: active
title: "TODO-01 -- Application shell"
depends_on: ["layout-core", "desktop-access", "command-line"]
---

# TODO-01 -- Application shell

> **Goal:** `berth.exe` with no arguments runs once per user as a tray app with its icon, tooltip and menu, an empty two-pane main window in the Windows theme, crash handling, and a way to render the window to a PNG so every later surface can be reviewed without a person looking.

> **Current state:** Measured 2026-11-20. `D01 T01`, `D02 T01`, and `D03 T01` are `done`; `Program.Main` (`Program.Run`) routes all CLI commands through `CliRunner` and `Composition.Create` (`D03 T01 §1`-`§8`) and has no tray-app branch for no arguments yet. No WPF or Windows Forms code exists. `design/tokens.json` holds the values, and `assets/icon/` the three `.ico` files.

## Inputs

- `design/SPEC.md` §1 (policy), §2 (tokens), §3 (shared rules), §4 (main window), §12 (tray), §14 (window placement in `settings.json`), §17 (single instance and startup), §18 (unhandled exception).
- `design/tokens.json` and `design/mockups/main-window-light.png`, `main-window-dark.png`.
- `assets/icon/berth.ico`, `berth-tray-dark.ico`, `berth-tray-light.ico`.
- -> XREF: D00 T01 §2 -- the application project, icon and manifest
- -> XREF: D01 T01 §6 -- `SettingsStore` holds the window placement and the first-run signal
- -> XREF: D03 T01 §3 -- the entry point and `Composition.CreateService` this file extends
- -> XREF: D05 T01 §1 -- the main window's content fills the frame built in §9 here
- -> XREF: D06 T01 §1 -- the automatic restore hooks into the tray app's message window

## Outcome

- Starting `berth.exe` twice leaves one tray icon, and the second start opens the first one's window.
- The tray menu saves and restores, the window opens and hides, and `pwsh scripts/render-window.ps1` writes the window as PNGs in light and dark.

**Adjacency:** list=not-applicable (the layouts list is owned by D05 T01 §2); document=not-applicable (nothing carried); settings=applicable @ D04 T01 §10; reporting=not-applicable (no summaries); notifications=not-applicable (the balloon is owned by D06 T01 §3); permissions=not-applicable (one user, no roles); audit=applicable @ D04 T01 §3; exchange=not-applicable (nothing imported or exported); reverse=not-applicable (the shell creates no record)

**Adjacency rationale:** The window placement is the shell's one setting. The crash report in the log is its audit trail. Records and their undo belong to `D01 T01`.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Entry routing and the first-run rule | D03 T01 §3, D01 T01 §6 | [x] |
| 2 | §2 | Single instance and activation | §1 | [ ] |
| 3 | §3 | Crash handling | §1 | [ ] |
| 4 | §4 | Tokens generated into resource dictionaries | D00 T01 §4 | [ ] |
| 5 | §5 | WPF application object and theme | §1, §2, §3, §4 | [ ] |
| 6 | §6 | Taskbar theme detection | D00 T01 §3 | [ ] |
| 7 | §7 | Tray icon and tooltip | §5, §6, D01 T01 §17 | [ ] |
| 8 | §8 | Tray menu | §7, D01 T01 §11, D01 T01 §18 | [ ] |
| 9 | §9 | Main window frame with two panes | §5 | [ ] |
| 10 | §10 | Window placement that persists | §9, D02 T01 §6 | [ ] |
| 11 | §11 | Off-screen rendering of the window for review | §9 | [ ] |

## 1. Entry routing and the first-run rule

> **Started:** 2026-11-20T17:48:29Z

`Program.Main` decides between the command line and the tray app, and the tray app decides whether to open its window (SPEC §17).

**Phase:** 4
**Needs:** none
**Surface:** no

**Build order:**

1. Add `StartupPlan` and `FirstRun` in `src/Berth/App/StartupPlan.cs` and `TrayApp.Run(bool openWindow, Settings settings, Action? onWindowOpened = null)` in `src/Berth/App/TrayApp.cs`. Done when: each argument shape maps to one plan and first-run saves only after the window-opened callback.
2. Call it from `Program.Run` loading settings only for `TrayApp` (`None` without error), forwarding `loaded.Value` and `OpenWindow` to `TrayApp.Run`, and logging `Messages.UnreadableSettings` on unreadable `settings.json`. Done when: no arguments reaches the tray app's entry and CLI commands never touch settings or tray startup.

- [x] Add `StartupMode` (`Command`, `TrayApp`, `UsageError`) and `StartupPlan.For(CliArguments args, bool settingsExist)` returning the mode and `OpenWindow` (`true` only for `TrayApp` when `settingsExist` is `false`, SPEC §17 "very first run"). Done when: unit tests cover no arguments with and without settings, all six commands, and an unknown argument. Cheaper substitute: opening the window on every start, which SPEC §17 says not to.
- [x] In `Program.Run`, branch on `StartupPlan`: `Command` and `UsageError` stay on the CLI paths without loading `settings.json` or calling `TrayApp.Run`; `TrayApp` loads `SettingsStore.Load()`, forwards `loaded.Value`, `trayPlan.OpenWindow`, and an `onWindowOpened` callback to `TrayApp.Run(openWindow, settings, onWindowOpened)` in `src/Berth/App/TrayApp.cs` (which for now creates nothing, does not invoke `onWindowOpened`, and returns 0). **Corrected 2026-11-20:** passed `SettingsLoad.Value` and `onWindowOpened` callback to `TrayApp.Run`, proved all 6 CLI commands + unknown arg bypass tray startup and never load malformed settings, and kept `BerthProcess.Run(data.Root, [])` exiting 0. Done when: `berth.exe` with no arguments exits 0 and all unit/process tests pass. Cheaper substitute: starting the tray app before parsing, so `berth restore` would flash an icon.
- [x] On the first run, write `Settings.Default` through `FirstRun.CompleteAfterWindowOpened(SettingsStore, Log)` ONLY inside the `onWindowOpened` callback after the window has opened, so the next start finds settings; if the write fails (`IOException`/`UnauthorizedAccessException`), log `Messages.WriteFailed` as `FAIL` without crashing and leave the next start requesting `OpenWindow == true`. **Corrected 2026-11-20:** proved via `Program.Run([], ...)` that `settings.json` stays absent before callback and when callback never fires, persists `Settings.Default` after callback, and handles write failures with path-specific `FAIL` logging. Done when: unit tests verify before/after callback, no-callback, second start, and failed first-run save. Cheaper substitute: a separate marker file.
- [x] When the tray app starts and `SettingsStore.Load` reports `Unreadable`, write one `WARN` line with `Messages.UnreadableSettings` through `Log`, pass `Settings.Default` and `OpenWindow == false` to `TrayApp.Run`, and never write the file (preserving its bytes). Done when: a test with `{` in `settings.json` gets 1 `WARN` line, `Settings.Default`, `OpenWindow == false`, and identical file bytes. Cheaper substitute: running silently on defaults, so the user never learns why their switches reset. **Corrected 2026-11-18:** added from the `D01 T01 §6` plan review (SPEC §14 asks for an unreadable settings file to be reported).
- [x] Add `tests/Berth.Tests/App/StartupPlanTests.cs`. Done when: all pass, and the first-run test fails when `OpenWindow` ignores `settingsExist`. Cheaper substitute: testing by launching the app.
- [x] Commit: `app: entry routing and the first-run rule`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*StartupPlanTests" --filter-class "*StartupPlanProcessTests" --filter-class "*LayerRuleTests"` passes with `0` skipped, and fails when `OpenWindow` is `true` on a later run.

> **Verified:** 2026-11-20 | §1 | 0ec65644822a24aa9d13b2b18947977aab91b80e
> **Review:** `docs/reviews/d04-t01-s1.json`

## 2. Single instance and activation

One tray app per user and logon session, and a second start opens the first one's window (SPEC §17).

**Phase:** 4
**Needs:** none
**Surface:** no

**Build order:**

1. Add `SingleInstance` in `src/Berth/App/SingleInstance.cs`. Done when: a second claim is refused and signals the first.
2. Harden the pipe reader. Done when: junk input never stops the listener.

- [ ] Add `SingleInstance(string name)` with `bool TryClaim()` (a named mutex `Local\Berth.App.<name>`, where the first instance is the one that created it) and `event Action? Activated`. The name is the user's SID, or the value of `BERTH_INSTANCE` when set, so a test never meets the owner's running Berth. Done when: a test with two instances under one random name sees the first claim succeed and the second fail. Cheaper substitute: searching for a process named `berth`, which matches other users and other copies.
- [ ] Listen on a named pipe `Berth.App.<name>.<session id>` with `PipeOptions.CurrentUserOnly`; a second instance connects, writes the word `activate`, and exits 0; the first raises `Activated`. Done when: the test's second instance calls `SignalFirst()` and the first's event fires within 2 seconds. Cheaper substitute: a window message broadcast, which other programs can send.
- [ ] Read at most 64 bytes for at most 2 seconds per client, act only on `activate`, and keep listening after a client that sends nothing, too much or anything else. Done when: a test sends 10,000 bytes, then nothing, then `activate`, and only the last raises the event. Cheaper substitute: `ReadToEnd`, which a client can hold open forever.
- [ ] If the first instance does not answer within 5 seconds, the second tries once to claim the mutex and otherwise exits 3 having started nothing. Done when: a test with a first instance that never reads sees the second return 3. Cheaper substitute: starting a second tray app, which would restore twice.
- [ ] Dispose releases the mutex and stops the listener. Done when: after dispose a new instance under the same name claims successfully. Cheaper substitute: relying on process exit in tests that share a process.
- [ ] Add `tests/Berth.Tests/App/SingleInstanceTests.cs`. Done when: all pass, and the junk-input test fails when the listener stops after one bad client. Cheaper substitute: a manual check with two terminals.
- [ ] Commit: `app: single instance and activation`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SingleInstanceTests"` passes, and fails when the pipe listener stops after a client that sends junk.

## 3. Crash handling

An unhandled exception in the tray app is logged, shown and ends the process (SPEC §18, last bullet).

**Phase:** 4
**Needs:** none
**Surface:** no

**Build order:**

1. Add `CrashHandler` in `src/Berth/App/CrashHandler.cs`. Done when: an exception is written to the log with its stack.
2. Hook the three sources. Done when: each source reaches the handler.

- [ ] Add `CrashHandler(Log log, Func<string, bool> showMessage)` with `Handle(Exception error, string source)`: write `Log.Fail` with the exception, then call `showMessage` with "Something went wrong: `<message>`. The details are in the log." and the log file's path on a second line. Done when: a unit test passes a fake `showMessage`, asserts the text, and finds the stack in the log. Cheaper substitute: a message box with a raw stack trace.
- [ ] Hook `Application.DispatcherUnhandledException` (handle it, then shut down with exit code 1), `AppDomain.CurrentDomain.UnhandledException` (log before the runtime ends the process) and `TaskScheduler.UnobservedTaskException` (log, observe, carry on) in `CrashHandler.Attach`. Done when: a unit test raises each through an internal `Raise` seam and sees the handler called with the right `source`. Cheaper substitute: swallowing UI-thread exceptions and carrying on with unknown state.
- [ ] Replace the user's profile path and user name with `<profile>` and `<user>` in everything the handler writes or shows. Done when: a test with an exception message containing `Environment.UserName` finds neither in the log nor in the message. Cheaper substitute: logs a user cannot safely paste into an issue (`.github/ISSUE_TEMPLATE/bug_report.yml` asks for them).
- [ ] Add `tests/Berth.Tests/App/CrashHandlerTests.cs`. Done when: all pass, and the redaction test fails when redaction is removed. Cheaper substitute: none needed.
- [ ] Commit: `app: crash handling`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*CrashHandlerTests"` passes, and fails when the user name is no longer redacted.

## 4. Tokens generated into resource dictionaries

`design/tokens.json` becomes a resource dictionary, and a test keeps hex colours out of the source (SPEC §1 "Tokens only", §2).

**Phase:** 4
**Needs:** none
**Surface:** no

**Build order:**

1. Add `scripts/build-tokens.py`. Done when: it writes the four `src/Berth/Theme/Tokens.*.xaml` dictionaries.
2. Add the staleness and no-hex gates. Done when: both fail when they should.

- [ ] Add `scripts/build-tokens.py` (standard library only): read `design/tokens.json` and write four plain WPF `ResourceDictionary` files under `src/Berth/Theme/` (WPF has no `ThemeDictionaries`; that is a WinUI feature): `Tokens.Light.xaml` and `Tokens.Dark.xaml` with a `SolidColorBrush` per colour key named `Berth.<Key>` (for example `Berth.MarkerInPlace`), `Tokens.HighContrast.xaml` with the same keys as brushes on the system colours SPEC §2 names (through `DynamicResource` on `SystemColors` keys), and `Tokens.Shared.xaml` with every `size` (as `x:Double`, key `Berth.<Key>`), every `radius` (`CornerRadius`, key `Berth.Radius<Key>`), every `timing` value (`x:Double`, for example `Berth.VisiblePollSeconds`), the three font families, and for each `type` style a `Style` for `TextBlock` keyed `Berth.Type.<Name>` setting family, size and weight. Each file starts with a comment saying it is generated and from what. Done when: running it writes four files that `XamlReader.Load` parses in a test, and the three colour files hold exactly the same set of keys. Cheaper substitute: a hand-written dictionary that drifts from the JSON.
- [ ] Add `--check`: write nothing and exit 1 when a file on disk differs from what would be generated. Done when: editing one colour in `Tokens.Dark.xaml` by hand makes `python scripts/build-tokens.py --check` exit 1. Cheaper substitute: trusting people to rerun the generator.
- [ ] Add a test in `scripts/tests/test_build_tokens.py` that every key in `size`, `radius`, `timing` and `type` of `design/tokens.json` has a resource in `Tokens.Shared.xaml`, and a C# test `tests/Berth.Tests/Theme/TokenKeysTests.cs` that every `Berth.*` key referenced by a `StaticResource` or `DynamicResource` in any XAML under `src/Berth/` exists in the generated files. Done when: both pass, and the C# test fails when a XAML file references `Berth.NoSuchKey`. Cheaper substitute: a missing key that surfaces only as a run-time resource error.
- [ ] Add a `tokens` step to `scripts/check-all.ps1` before `build` running `python scripts/build-tokens.py --check`. Done when: `pwsh scripts/check-all.ps1 -List` shows it. Cheaper substitute: a check nobody runs.
- [ ] Add `scripts/tests/test_build_tokens.py`: the generator's output for a small fixture JSON matches an expected string; `--check` exits 0 on a fresh file and 1 on an edited one; every colour key in `design/tokens.json` appears in both theme dictionaries. Done when: `python -m unittest discover -s scripts/tests` passes with the new test. Cheaper substitute: testing the generator only by building the app.
- [ ] Add `tests/Berth.Tests/Theme/NoHexColourTests.cs`: read every `.xaml` and `.cs` file under `src/Berth/` except the generated `Theme/Tokens.*.xaml` and fail when one contains a hex colour literal (`#` followed by 3, 4, 6 or 8 hexadecimal digits inside a string or attribute) or a `Color.FromRgb`/`FromArgb` call. Done when: it passes now and fails when `Background="#FF0000"` is added to a XAML file. Cheaper substitute: a review convention (SPEC §1).
- [ ] Merge the dictionaries into the application's resources in §5. Done when: §5's test finds `Berth.MarkerInPlace` through `Application.Current.FindResource`. Cheaper substitute: merging them in each window.
- [ ] Commit: `theme: tokens generated into resource dictionaries`

**Test checkpoint:** `python scripts/build-tokens.py --check` exits 0, and exits 1 after one colour in `src/Berth/Theme/Tokens.Dark.xaml` is changed by hand; `dotnet test --solution Berth.slnx --filter-class "*NoHexColourTests"` fails when a hex colour is added to a XAML file.

## 5. WPF application object and theme

The `Application` that hosts the tray app and the window, following the Windows theme (SPEC §3).

**Phase:** 4
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `BerthApplication` in `src/Berth/App/BerthApplication.cs`. Done when: it runs a message loop with no window and exits on request.
2. Apply the Fluent theme and the tokens. Done when: a test window resolves both.

- [ ] Add `BerthApplication : Application` created in code (no `App.xaml` startup object, since `Program.Main` is the entry point) with `ShutdownMode.OnExplicitShutdown` and `ThemeMode = ThemeMode.System`. Done when: `TrayApp.Run` creates it, runs it, and a call to `Shutdown(0)` from a timer in a test ends `Run` with 0. Cheaper substitute: `ShutdownMode.OnLastWindowClose`, which would end the tray app when the window hides.
- [ ] Add `ThemeResources` in `src/Berth/App/ThemeResources.cs`: merges `Tokens.Shared.xaml` and one colour dictionary into `Resources.MergedDictionaries`, chosen by `Select(bool highContrast, bool appsUseLightTheme)` (high contrast wins, then light or dark), and replaces the colour dictionary when `SystemParameters.HighContrast` changes or the Windows app theme changes (`SettingChanged` with `ImmersiveColorSet`, wired in `D06 T01 §1`; until then through `SystemParameters.StaticPropertyChanged` and `Application.ThemeMode` changes). Surfaces refer to colours with `DynamicResource`, so open windows restyle live (SPEC §3). Done when: a test on an STA thread creates the application, `FindResource("Berth.MarkerInPlace")` returns the light brush, and after `ThemeResources.Apply(dark)` returns the dark one; unit tests cover the three outcomes of `Select`. Cheaper substitute: `StaticResource` colours, which keep the old theme until restart.
- [ ] Add an STA application fixture in `tests/Berth.Tests/App/AppFixture.cs` that creates one `BerthApplication` for the test run on a dedicated STA thread with a running dispatcher, exposes `Invoke(Action)`, and shuts it down at the end. Done when: two test classes use it in one run without "cannot create more than one Application" errors. Cheaper substitute: a new `Application` per test, which WPF forbids.
- [ ] Wire `SingleInstance` (§2) and `CrashHandler` (§3) in `TrayApp.Run`: a failed claim signals the first instance and returns 0 before any application object is created. Done when: a process test starts two `berth.exe` with a random `BERTH_INSTANCE` and `BERTH_DATA`, the second exits 0 within 10 seconds, and the first is then stopped by the test's tree kill. Cheaper substitute: creating the tray icon before the claim, which flashes a second icon.
- [ ] Add `tests/Berth.Tests/App/BerthApplicationTests.cs`. Done when: all pass, and the resource test fails when the merge is removed. Cheaper substitute: checking the theme by eye.
- [ ] Commit: `app: WPF application object and theme`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*BerthApplicationTests"` passes, and fails when the token dictionaries are not merged into the application resources.

## 6. Taskbar theme detection

Whether the taskbar is light or dark, and an event when that changes (SPEC §12, first bullet).

**Phase:** 4
**Needs:** none
**Surface:** no

**Build order:**

1. Add `TaskbarTheme` in `src/Berth/Desktop/TaskbarTheme.cs`. Done when: it reads the registry value.
2. Raise a change event. Done when: a changed value raises it once.

- [ ] Add `TaskbarTheme(string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")` with `bool IsLight` reading the DWORD `SystemUsesLightTheme` under `HKCU` (missing means dark). Done when: a test passing a key under `HKCU\Software\Berth.Tests\<guid>` reads `false` when missing, `true` for 1 and `false` for 0, and deletes its key afterwards. Cheaper substitute: `AppsUseLightTheme`, which is the app theme, not the taskbar's.
- [ ] Add `event Action? Changed` and `void OnSettingChange(string? area)` that re-reads the value when `area` is `ImmersiveColorSet` and raises `Changed` only when `IsLight` actually changed. Done when: a test flips the test key, calls `OnSettingChange("ImmersiveColorSet")` twice, and sees one event. Cheaper substitute: polling the registry on a timer.
- [ ] Add `tests/Berth.Tests/Desktop/TaskbarThemeTests.cs`. Done when: all pass, and the change test fails when the event fires on every call. Cheaper substitute: writing the real `Personalize` key in a test (`AGENTS.md`).
- [ ] Commit: `desktop: taskbar theme detection`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*TaskbarThemeTests"` passes, and fails when `Changed` is raised without the value changing.

## 7. Tray icon and tooltip

The icon in the notification area: the right variant for the taskbar, a tooltip, and a click that opens the window.

**Phase:** 4
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §12, first three bullets.
**Job:** the user sees at a glance that Berth is running and what it has saved for these monitors.
**Treatment:** the theme-matched tray icon with a one-line tooltip; left click or double click opens the window. Cheaper substitute that fails: one icon for both taskbar themes, which disappears on one of them.
**Chrome:** Windows Forms `NotifyIcon` with `assets/icon/berth-tray-dark.ico` and `berth-tray-light.ico` embedded as resources.

**Build order:**

1. Add `TrayIcon` in `src/Berth/App/TrayIcon.cs`. Done when: `berth.exe` shows an icon in the notification area.
2. Add the tooltip text and the click. Done when: the tooltip matches SPEC §12 for both states.

- [ ] Embed the two tray `.ico` files as resources in `src/Berth/Berth.csproj` (linked from `assets/icon/`, not copied). Done when: a test loads both through `Assembly.GetManifestResourceStream` and gets an `Icon` with a 16 px frame. Cheaper substitute: a second copy of the files under `src/`.
- [ ] Add `TrayIcon(TaskbarTheme theme)` creating a `NotifyIcon`, choosing `berth-tray-dark.ico` when `theme.IsLight` is `false` and `berth-tray-light.ico` when `true`, at the size `SystemInformation.SmallIconSize` reports, and switching on `theme.Changed`. Done when: a test with a fake theme flips it and reads the `NotifyIcon.Icon` handle change. Cheaper substitute: the application icon scaled to 16 px, whose tile is unreadable in the tray (README, "Brand").
- [ ] Add `TrayText.Tooltip(Layout? active, DateTimeOffset now, TimeZoneInfo zone)` in `src/Berth/App/TrayText.cs` returning `Berth: Docked, saved today, 09:14` or `Berth: no layout for this monitor arrangement`, shortened with an ellipsis character inside the name when the whole text would pass 127 characters (the `NotifyIcon.Text` limit). Done when: unit tests compare both strings and a 40 character name stays within 127. Cheaper substitute: letting `NotifyIcon.Text` throw on a long name.
- [ ] Refresh the tooltip after every operation and when the monitors change. Done when: a test invoking a save through the menu handler sees the tooltip text change from the "no layout" form to the "saved" form. Cheaper substitute: a tooltip computed once at startup.
- [ ] Raise `OpenRequested` on a left click and on a double click, and not on a right click. Done when: a unit test invoking the `MouseClick` handler with each button sees the event only for the left button. Cheaper substitute: opening the window on any click, which fights the menu.
- [ ] Dispose removes the icon (`Visible = false`, then `Dispose`). Done when: a test disposes it and `NotifyIcon.Visible` is `false`. Cheaper substitute: a ghost icon left until the pointer passes over it.
- [ ] Add `tests/Berth.Tests/App/TrayIconTests.cs`. Done when: all pass, and the theme test fails when the variants are swapped. Cheaper substitute: checking the tray by eye.
- [ ] Commit: `app: tray icon and tooltip`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*TrayIconTests"` passes, and fails when the dark and light icon variants are swapped.

## 8. Tray menu

The right-click menu of SPEC §12, wired to the same operations as the window.

**Phase:** 4
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §12, bullets four to six.
**Job:** the user saves or restores without opening a window.
**Treatment:** five items and a separator in the SPEC order, the default item bold, restore disabled when it cannot work. Cheaper substitute that fails: a menu whose "Restore layout" is always enabled and fails after the click.
**Chrome:** Windows Forms `ContextMenuStrip` with the system renderer, so it follows the Windows theme.

**Build order:**

1. Add `TrayMenu` in `src/Berth/App/TrayMenu.cs`. Done when: the six entries appear in order.
2. Wire each item and its enabled state. Done when: each item does its job against fakes.

- [ ] Build the menu: `Save layout`, `Restore layout`, `Restore automatically`, a separator, `Open Berth` (bold) and `Exit`, in that order. Done when: a test reads the items' text, the separator's position and the bold font of `Open Berth`. Cheaper substitute: a different order from the mockup.
- [ ] `Save layout` calls `LayoutService.Save()` and `Restore layout` calls `LayoutService.Restore()`, both off the UI thread through `Task.Run` (the shell calls inside them run on `ShellDesktopIcons`'s own STA worker, `D02 T01 §9`), each raising `OperationFinished(OperationResult)` on the UI thread. Done when: tests with `FakeDesktopIcons` click each item and see the fake desktop and the layout file change. Cheaper substitute: running the shell calls on the UI thread, freezing the menu.
- [ ] Before the menu opens (`Opening`), set `Restore layout` enabled only when the current arrangement has a layout and auto arrange is off, and `Restore automatically` checked from `SettingsStore`. Done when: tests cover no layout (disabled), auto arrange on (disabled), a layout with auto arrange off (enabled), and the check mark for both setting values. Cheaper substitute: computing the state once at startup.
- [ ] `Restore automatically` toggles the setting through `SettingChanges.SetRestoreAutomatically` (`D01 T01 §18`, which writes, reads back and logs), and leaves the check as it was when the result is not applied. **Corrected 2026-11-18:** the write, read-back and log line moved to Core so the tray and the window share them. Done when: tests cover a successful toggle and a failing store. Cheaper substitute: a check mark that changes without the file changing.
- [ ] `Open Berth` raises `OpenRequested`, and `Exit` raises `ExitRequested`, which `TrayApp` answers by disposing the tray icon and calling `Shutdown(0)`. Done when: a test clicks `Exit` and the application's run loop ends with 0. Cheaper substitute: `Environment.Exit`, which skips disposal and leaves a ghost icon.
- [ ] A refused or failed operation from the menu is logged, and when the window is open it shows there (wired in `D05 T01 §6`); the menu itself shows nothing. Done when: a test with auto arrange forced on between `Opening` and the click gets a `WARN` log line and no exception. Cheaper substitute: a message box from a tray menu.
- [ ] Add `tests/Berth.Tests/App/TrayMenuTests.cs`. Done when: all pass, and the enabled-state test fails when `Restore layout` is always enabled. Cheaper substitute: none needed.
- [ ] Commit: `app: tray menu`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*TrayMenuTests"` passes, and fails when "Restore layout" is enabled with no layout for the arrangement.

## 9. Main window frame with two panes

The window's frame: title, icon, sizes, the 250 px layouts pane, the divider and the detail area, with placeholders that `D05 T01` fills (SPEC §4).

**Phase:** 4
**Needs:** windows-host, display-session
**Surface:** yes
**Fidelity:** `design/SPEC.md` §4.
**Job:** the user opens Berth and gets a normal Windows 11 window that hides to the tray when closed.
**Treatment:** a standard Fluent window with two panes at the token sizes; closing hides it. Cheaper substitute that fails: a window that exits the app when closed.
**Chrome:** WPF `Window` with the Fluent theme's own title bar and backdrop; sizes from the generated `Theme/Tokens.Shared.xaml`.

**Build order:**

1. Add `MainWindow` in `src/Berth/Window/MainWindow.xaml`. Done when: it opens at the token sizes with two panes.
2. Wire open, hide and activate. Done when: closing hides it and the tray reopens it.

- [ ] Add `src/Berth/Window/MainWindow.xaml` and `.xaml.cs`: title `Berth`, the application icon, `Width` and `Height` from `Berth.WindowDefaultWidth` and `Berth.WindowDefaultHeight`, `MinWidth` and `MinHeight` from their tokens, a `Grid` with a 250 px first column (`Berth.LayoutsPaneWidth`), a 1 px divider in the theme's divider brush, and a star column, each holding a named empty `ContentControl` (`LayoutsHost`, `DetailHost`). Done when: a test on the app fixture shows the window and reads `ActualWidth` 980, `ActualHeight` 640 and the first column's `ActualWidth` 250 at 100% scale. Cheaper substitute: literal numbers in the XAML (SPEC §1).
- [ ] Closing the window cancels the close and hides it, unless the application is shutting down. Done when: a test calls `Close()` and sees `IsVisible` `false` and the window still usable by `Show()`; and `Exit` from the menu still ends the run. Cheaper substitute: letting the window close, after which reopening throws.
- [ ] `Esc` does nothing (SPEC §4). Done when: a test sends `Key.Escape` through `RaiseEvent` and the window stays visible. Cheaper substitute: a cancel button binding that hides the window.
- [ ] Add `MainWindow.BringToFront()`: `Show()`, restore from minimised, `Activate()`, and briefly set and clear `Topmost` when activation is refused. Done when: a test minimises the window, calls it, and reads `WindowState.Normal` and `IsVisible` `true`. Cheaper substitute: `Show()` alone, which leaves a minimised window minimised.
- [ ] In `TrayApp`, create the window lazily on the first open, and call `BringToFront` for `TrayIcon.OpenRequested`, `TrayMenu.OpenRequested`, `SingleInstance.Activated` and the first-run plan. Done when: a test raises each source against a fake window factory and counts one creation and four `BringToFront` calls. Cheaper substitute: creating the window at startup, which costs memory in a tray-only session.
- [ ] Add `tests/Berth.Tests/Window/MainWindowFrameTests.cs`. Done when: all pass, and the close test fails when `OnClosing` stops cancelling. Cheaper substitute: checking by hand.
- [ ] Commit: `window: main window frame with two panes`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*MainWindowFrameTests"` passes, and fails when closing the window no longer hides it.

## 10. Window placement that persists

The window remembers its size and position and never opens off screen (SPEC §4, §14).

**Phase:** 4
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `WindowPlacementRule` in `src/Berth/Window/WindowPlacementRule.cs`. Done when: a saved placement is accepted or rejected by pure logic.
2. Save on hide and apply on open. Done when: a reopened window is where it was.

- [ ] Add `WindowPlacementRule.Resolve(WindowPlacement? saved, IReadOnlyList<MonitorInfo> monitors, int minWidth, int minHeight)` returning the saved placement when at least 100 by 100 px of it lies on some monitor and it is at least the minimum size, and `null` (use the default, centred on the primary monitor) otherwise. Done when: unit tests cover a visible placement, one entirely off screen, one on a disconnected monitor's coordinates, and one smaller than the minimum. Cheaper substitute: restoring any saved position, which loses the window after undocking.
- [ ] Add `WindowPixels` in `src/Berth/Window/WindowPixels.cs` converting between WPF's device-independent units and physical pixels at the window boundary: `ToPhysical(Rect dips, double scale)` and `ToDips(WindowPlacement physical, double scale)`, where `scale` is the DPI scale of the monitor the placement lies on (`MonitorInfo.Scale / 100`). `settings.json` holds physical pixels (SPEC §14), and the rule above compares physical with physical. Done when: unit tests round-trip a placement at 100%, 150% and 200% without drift. Cheaper substitute: saving WPF units and comparing them with physical monitor bounds, which is wrong on any scaled display.
- [ ] Apply the rule when the window is first shown, placing it with `SetWindowPos` in physical pixels (or setting `Left`, `Top`, `Width`, `Height` from `WindowPixels.ToDips` for the target monitor's scale). Done when: a test with a stored placement shows the window and `GetWindowRect` returns that placement. Cheaper substitute: `WindowStartupLocation` alone.
- [ ] When the window hides, save its restored bounds in physical pixels (`GetWindowPlacement`'s normal position, so a maximised or minimised window saves its restored size) to `Settings.Window` through `SettingsStore`, keeping `RestoreAutomatically` as it is. Done when: a test moves the window, hides it, and reads the new placement from `settings.json`. Cheaper substitute: saving on every move event, which writes the file hundreds of times.
- [ ] A failed settings write when hiding (an `IOException`, an `UnauthorizedAccessException`, or the `UnreadableSettingsException` of an unreadable `settings.json`, `D01 T01 §6`) is logged as `WARN` with its SPEC §18 message and never blocks the hide or ends the tray app. Done when: tests with a failing store and with `{` in `settings.json` hide the window, the tray app keeps running, and the bad file's bytes are unchanged. Cheaper substitute: an error dialog on close, or an exception that reaches the crash handler. **Corrected 2026-11-18:** the unreadable-file refusal was not named (`D01 T01 §6` round 1 P2).
- [ ] Add `tests/Berth.Tests/Window/WindowPlacementTests.cs`. Done when: all pass, and the off-screen test fails when the rule accepts every placement. Cheaper substitute: none needed.
- [ ] Commit: `window: placement that persists`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*WindowPlacementTests"` passes, and fails when an off-screen placement is accepted.

## 11. Off-screen rendering of the window for review

A way to turn the window, in a given state and theme, into a PNG with nobody looking, so the design lens of every `Surface: yes` review has a picture.

**Phase:** 4
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `WindowRenderer` in `tests/Berth.Tests/Render/WindowRenderer.cs`. Done when: it writes a PNG of the frame.
2. Add a render script. Done when: one command writes the frame in light and dark.

- [ ] Add `WindowRenderer.Render(FrameworkElement root, int width, int height, string path)`: measure and arrange `root` at the size, render it with `RenderTargetBitmap` at 96 DPI, and save a PNG. Done when: a test renders the `MainWindow` content at 980 by 640 and the file is a PNG of that size whose pixels are not all one colour. Cheaper substitute: a screenshot of the visible desktop, which needs a person's screen and captures their windows.
- [ ] Add a `theme` parameter (`Light`, `Dark`) that sets the application's `ThemeMode` for the render and restores it afterwards. Done when: the light and dark renders of the frame differ in their average brightness by a wide margin asserted in the test. Cheaper substitute: rendering only in the theme the machine happens to use.
- [ ] Add `scripts/render-window.ps1` that runs the tests with trait `Render` and collects their PNGs into `build/renders/` (ignored by git), printing each path. Done when: `pwsh scripts/render-window.ps1` exits 0 and `build/renders/frame-light.png` and `frame-dark.png` exist. Cheaper substitute: committing renders, which go stale silently.
- [ ] Record in `docs/todo-workflow.md`, under a new heading "Rendering a surface for review", that a `Surface: yes` section runs `pwsh scripts/render-window.ps1` and passes the PNG paths to the review. Done when: the heading exists and names the script and the output folder. Cheaper substitute: leaving each section to invent its own capture.
- [ ] Add `tests/Berth.Tests/Render/WindowRendererTests.cs` (trait `Render`). Done when: all pass, and the size test fails when the render size is ignored. Cheaper substitute: none needed.
- [ ] Commit: `test: off-screen rendering of the window for review`

**Test checkpoint:** `pwsh scripts/render-window.ps1` exits 0 and writes `build/renders/frame-light.png` and `build/renders/frame-dark.png`, two different images of 980 by 640 pixels.

## Verification

- On the build machine: start `berth.exe` (first run opens the window), close the window (it hides), left-click the tray icon (it returns), start `berth.exe` again (the same window comes forward, one tray icon), choose Exit (the icon goes and the process ends).
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
