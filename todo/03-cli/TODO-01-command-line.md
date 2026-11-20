---
schema_version: 1
id: command-line
domain: 03-cli
status: active
title: "TODO-01 -- Command line"
depends_on: ["layout-core", "desktop-access"]
---

# TODO-01 -- Command line

> **Goal:** `berth save`, `berth restore`, `berth undo`, `berth list`, `berth --help` and `berth --version` work from a terminal exactly as `design/SPEC.md` §13 says: the right line on the right stream, the right exit code, no window, and a terminal that waits.

> **Current state:** Measured 2026-11-20. `Program.cs` from `D00 T01 §2` prints the version for `--version` and returns 2 for anything else. `D01 T01` (`LayoutService`, `Messages`, `Formats`, `LayoutStore`, `BerthPaths`, `OperationLock`, `Log`) and `D02 T01` (`ShellDesktopIcons`, `ScopedDesktopIcons`, `SystemMonitors`, `ProbeIcon`) are both `done`.

## Inputs

- `design/SPEC.md` §13 (commands, output, exit codes, console policy), §17 (an unknown first argument is wrong usage), §18 (messages).
- `docs/plans/2026-11-15-berth-foundation.md`: "Console and window from one executable".
- -> XREF: D00 T01 §2 -- the entry point and the manifest's console allocation policy
- -> XREF: D01 T01 §16 -- `Messages.Cli` and `Messages.Refusal` word every line printed here
- -> XREF: D02 T01 §8 -- the commands run against the real desktop implementations
- -> XREF: D04 T01 §1 -- the tray app is started from the same entry point when no command is given
- -> XREF: D07 T01 §3 -- the README documents the commands built here

## Outcome

- Each command prints the SPEC §13 line and exits with the SPEC §13 code, proven by starting the built `berth.exe` as a real process.
- Starting `berth.exe` with no console attached opens no console window, and a terminal waits for a command to finish.

**Adjacency:** list=applicable @ D03 T01 §6; document=not-applicable (nothing carried or printed on paper); settings=not-applicable (the command line reads no setting of its own); reporting=not-applicable (no summaries beyond `berth list`); notifications=not-applicable (a command prints, it never notifies); permissions=applicable @ D03 T01 §4; audit=not-applicable (the log lines are written by D01 T01 §15); exchange=not-applicable (SPEC §20 rules out import and export); reverse=applicable @ D03 T01 §5

**Adjacency rationale:** `berth list` is the command line's way to find a layout. The refusals of SPEC §13 exit code 3 are exercised in §4 and §5. `berth undo` is the reverse of `berth restore`.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Argument parser | D00 T01 §3 | [x] |
| 2 | §2 | Console behaviour proven with real processes | D00 T01 §2 | [x] |
| 3 | §3 | The save command | §1, D01 T01 §16, D02 T01 §8 | [x] |
| 4 | §4 | The restore command | §3 | [x] |
| 5 | §5 | The undo command | §4 | [x] |
| 6 | §6 | The list command | §3, D01 T01 §17 | [x] |
| 7 | §7 | Help, version and usage errors | §3 | [x] |
| 8 | §8 | Process tests of every command and exit code | §2, §5, §6, §7 | [ ] |

## 1. Argument parser

> **Started:** 2026-11-20T14:42Z

A small hand-written parser: the grammar is six commands and one option, which no library is needed for.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Add `CliCommand` and `CliArguments` in `src/Berth/Cli/CliArguments.cs` and record exact usage diagnostics and `--name=<name>` in `design/SPEC.md` §13. Done when: every SPEC §13 command and `--name` form parses strictly (lowercase exact match, empty string preserved as `""` distinct from `null`) and every invalid invocation returns `(None, null, <SPEC §13 diagnostic>)`.
2. Add `tests/Berth.Tests/Cli/CliArgumentsTests.cs` covering all valid forms, empty vs missing `--name`, case/prefix/empty-first-arg rejections, and all six usage-error diagnostics. Done when: all pass with `0` skipped in Release.

- [x] Add `CliCommand` (`None`, `Save`, `Restore`, `Undo`, `List`, `Help`, `Version`) and `CliArguments(CliCommand Command, string? Name, string? Error)` with `CliArguments.Parse(string[] args)` (`ArgumentNullException.ThrowIfNull(args)`): no arguments gives `(None, null, null)`; `save`, `restore`, `undo`, `list` (lowercase exact match only), `--help`, `-h`, `/?` and `--version` give their commands with `Name: null, Error: null`. **Corrected 2026-11-20:** added negative tests for wrong case (`Save`), prefix (`rest`), and empty first argument (`""`) so none can match or launch the tray app. Done when: a test per positive and negative form passes asserting the full `(Command, Name, Error)` record. Cheaper substitute: accepting any prefix or case, which makes a typo run a command.
- [x] Accept `--name <name>` and `--name=<name>` only after `save`, storing `Name` verbatim (preserving an explicitly supplied empty value `""` distinct from `null` so §10 produces `Enter a name.`, while consuming the next token verbatim in `save --name --name Docked` so `Docked` triggers `unexpected argument "Docked"`), and document both forms and exact diagnostics in `design/SPEC.md` §13. **Corrected 2026-11-20:** clarified empty-value preservation (`save --name ""`, `save --name=`) vs missing argument (`save --name`), `save --name --name Docked` behavior, and spec update in `design/SPEC.md` §13. Done when: tests cover space/equals forms, spaces in one argument, empty-string preservation, missing value, and `save --name --name Docked`. Cheaper substitute: joining the remaining arguments with spaces or discarding empty values.
- [x] Return `(None, null, <diagnostic>)` (sanitizing `<arg>` via `Messages.SingleLine`) for: unknown command (`unknown command "<arg>"`), unknown option (first arg starting with `-` or after a known command: `unknown option "<arg>"`), `--name` with no value (`--name needs a value`), duplicate `--name` (`--name may be given only once`), `--name` on any command other than `save` (`--name is only valid with save`), and any extra positional argument (`unexpected argument "<arg>"`). **Corrected 2026-11-20:** pinned exact SPEC §13 diagnostic wording for all six usage errors and first-arg `--x` vs post-command `--x` classification. Done when: a test per case asserts the full `(None, null, <exact message>)` record. Cheaper substitute: ignoring what is not understood.
- [x] Add `tests/Berth.Tests/Cli/CliArgumentsTests.cs`. Done when: all pass with `0` skipped in Release, asserting both `[]` -> `(None, null, null)` and `["frobnicate"]` -> `(None, null, "unknown command \"frobnicate\"")` so dropping `Error` fails. Cheaper substitute: testing only valid input.
- [x] Commit: `cli: argument parser`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*CliArgumentsTests" --filter-class "*LayerRuleTests"` passes with `0` skipped, and fails when an unknown first argument drops `Error` or matches case-insensitively/by prefix.

> **Verified:** 2026-11-20 | §1 | 70947bdbe38e2102deb605ecbcc8a95d9010ac6a
> **Review:** `docs/reviews/d03-t01-s1.json`

## 2. Console behaviour proven with real processes

> **Started:** 2026-11-20T15:03Z

The foundation plan's one technical bet: a console program with a detached console policy. This section proves it on the build machine or records the fallback.

**Phase:** 3
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `TestModeSwitches` (`src/Berth/Cli/TestModeSwitches.cs`) wired into `Program.Main`, add `BerthProcess` (`tests/Berth.Tests/Cli/BerthProcess.cs`) with inherited `BERTH_*` stripping, reserved `BERTH_DATA`, bounded stream draining, and `Kill(entireProcessTree: true)` on timeout and `Dispose`, and add `HelperMain` commands (`launch-detached`, `spawn-descendant`, `sleep-forever`). Done when: `BerthProcess` reads `Berth 0.1.0` from `berth.exe --version`, tree-kills both direct process and reported descendant PID on timeout and early `Dispose`, refuses overriding `BERTH_DATA` via extra env, and `TestModeSwitchesTests` + real `--version` unset/whitespace `BERTH_DATA` checks prove the test-mode gate.
2. Add `tests/Berth.Tests/Cli/ConsoleBehaviourTests.cs` (`[Collection(DesktopCollectionDefinition.Name)]` `[Trait("Category", "RealDesktop")]`) proving both halves on this Windows 11 build (`10.0.26300.0`): (a) real `cmd.exe` wait proof using `BERTH_TEST_REPORT` (written before `BERTH_TEST_HOLD_MS=1500` sleep) + post-command marker file (verifying marker is absent while Berth is held and appears after exit with `ERRORLEVEL == 0`, plus a deliberately non-waiting `start "" /b` negative control where the marker appears before the hold ends); and (b) console-less parent launch (`DETACHED_PROCESS` helper verifying `GetConsoleWindow() == 0`, launching Berth with `CreateProcessW` + `CREATE_UNICODE_ENVIRONMENT` + `STARTF_USESHOWWINDOW` + `SW_HIDE`, no `DETACHED_PROCESS` on Berth, owned handle cleanup) over a full runnable temp copy of `berth.exe` (`console=0` unchanged; `console=1` after stripping only `consoleAllocationPolicy` via hidden Windows SDK `mt.exe` and re-extracting to verify the edit), then record the proven Windows build in `docs/plans/2026-11-15-berth-foundation.md` and `design/SPEC.md` §13. Done when: all pass with `0` skipped in Release.

- [x] Add `BerthProcess` in `tests/Berth.Tests/Cli/BerthProcess.cs`: locates the built `berth.exe` beside the test assembly (or matching configuration output), strips inherited `BERTH_*` variables, reserves `BERTH_DATA` strictly for the caller's `TestData` folder (rejecting extra-env overrides with `ArgumentException`, with an internal seam only for testing unset/whitespace `BERTH_DATA`), runs with `CreateNoWindow = true` and bounded async stdout/stderr draining, enforces a configurable timeout (default 30s), and calls `Kill(entireProcessTree: true)` + bounded `WaitForExit` on timeout and in `Dispose`. **Corrected 2026-11-20:** added descendant-PID tree-kill verification for both timeout and early `Dispose`, bounded stream draining, and reserved `BERTH_DATA` guard. Done when: `ConsoleBehaviourTests` proves `berth.exe --version` reads `Berth 0.1.0`, inherited `BERTH_*` vars are stripped, `BERTH_DATA` cannot be overwritten via extra env, and both timeout and early `Dispose` kill both the direct child and its reported descendant PID within a deadline. Cheaper substitute: `process.Kill()` without the tree, which `AGENTS.md` forbids.
- [x] Add `TestModeSwitches` (`src/Berth/Cli/TestModeSwitches.cs`) wired into `Program.Main`, honoured only when `BerthPaths.FromEnvironment().TestMode` is `true`: writes `BERTH_TEST_REPORT` (`console=1` when `GetConsoleWindow() != 0`, else `console=0`) BEFORE sleeping `BERTH_TEST_HOLD_MS` (non-negative integer ms, capped at 5000). **Corrected 2026-11-20:** wrote report before hold so the shell-wait test can causally witness Berth inside its hold, and added real `berth.exe --version` tests with unset/whitespace `BERTH_DATA` verifying no report file is created and hold is ignored. Done when: `TestModeSwitchesTests` and `ConsoleBehaviourTests` prove both switches are ignored when `BERTH_DATA` is unset/whitespace, negative/non-numeric hold is ignored, values above 5000 cap at 5000, and `BERTH_TEST_REPORT` writes `console=1` or `console=0`. Cheaper substitute: test hooks that work in a user's normal run.
- [x] Prove "a shell waits" in `tests/Berth.Tests/Cli/ConsoleBehaviourTests.cs`: drive a real interactive `cmd.exe /q /v:on` inside a hidden Windows ConPTY pseudoconsole (`CreatePseudoConsole` + `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`) with `BERTH_DATA`, verifying delayed `!ERRORLEVEL!` captures `2` for `--unknown`, then running `--version` with `BERTH_TEST_REPORT` and `BERTH_TEST_HOLD_MS=1500` so Berth reports `console=1`, prints `Berth 0.1.0` directly to the terminal, holds the prompt so the post-command marker is absent while held and appears `>= 1400 ms` after the report with `EXIT=0`, and `cmd.exe` itself exits `0`; also run a deliberately non-waiting `start "" /b` control inside a Job Object asserting the marker appears before the hold finishes while `berth.exe` is still alive and then exits `0`. **Corrected 2026-11-20:** upgraded from redirected `cmd.exe /c` to a real interactive ConPTY `cmd.exe` proof with delayed `!ERRORLEVEL!`, shell exit code `0` check, and Job-Object-tracked non-waiting control. Done when: both assertions pass in Release. Cheaper substitute: asserting only that output arrived.
- [x] Prove "no console without a console" in `tests/Berth.Tests/Cli/ConsoleBehaviourTests.cs`: copy the full runnable `berth.exe` output directory (`berth.exe`, `berth.dll`, `berth.runtimeconfig.json`, `berth.deps.json`, side-by-side DLLs) to a temp folder under `TestData`; launch via a `DETACHED_PROCESS` `HelperMain` command (`launch-detached`) that verifies `GetConsoleWindow() == 0` and invokes `CreateProcessW` with `CREATE_UNICODE_ENVIRONMENT` + `STARTF_USESHOWWINDOW` + `SW_HIDE` (no `DETACHED_PROCESS` on Berth) with bounded wait and tree-kill cleanup; assert the unchanged temp copy exits `0` and writes `console=0`; run hidden Windows SDK `mt.exe` (exit code `0`) to extract the manifest, strip ONLY `consoleAllocationPolicy`, re-embed, and re-extract to verify removal; launch the stripped copy identically and assert it exits `0` and writes `console=1`. **Corrected 2026-11-20:** copied full runnable apphost directory, enforced `STARTF_USESHOWWINDOW` + `SW_HIDE`, verified helper has no console, verified `mt.exe` edit by re-extraction, and bounded cleanup. Done when: both `console=0` (unchanged) and `console=1` (stripped) pass in Release. Cheaper substitute: looking at the screen once and writing "verified".
- [x] Record the Windows build (`[Environment]::OSVersion.Version`) in `docs/plans/2026-11-15-berth-foundation.md` ("Console and window from one executable") and `design/SPEC.md` §13 AFTER the live proofs pass, replacing "to be confirmed" with "shown on build <number>, 2026-11-20" (if a host fails the detached policy proof, switch to `WinExe` + `AttachConsole`, update §13 and the foundation plan with the fallback behavior and non-waiting cost, and adjust the checkpoint to the fallback criteria). Done when: both documents name the proven build and date after a passing run. Cheaper substitute: leaving the minimum as an unconfirmed claim.
- [x] Commit: `cli: console behaviour proven with real processes`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*ConsoleBehaviourTests" --filter-class "*TestModeSwitchesTests" --filter-class "*LayerRuleTests"` passes with `0` skipped: detached `berth.exe` reports `console=0`, stripped-manifest copy reports `console=1`, `cmd.exe` blocks marker creation while Berth holds (`1500 ms`) while non-waiting control does not, descendant tree-kill passes on timeout and early `Dispose`, and `TestModeSwitches` unit + unset-`BERTH_DATA` process tests pass.

> **Verified:** 2026-11-20 | §2 | 54ad2a1190037950fa8f84622199a920229de645
> **Review:** `docs/reviews/d03-t01-s2.json`

## 3. The save command

> **Started:** 2026-11-20T15:46:05Z

`berth save [--name <name>]` (SPEC §13).

**Phase:** 3
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `CliRunner` in `src/Berth/Cli/CliRunner.cs` (`CliExit` constants + `Save` result/stream mapping) and `Composition` in `src/Berth/App/Composition.cs` (`OperationLock.ForApp(paths)`, `ShellDesktopIcons` disposal even when wrapped in `ScopedDesktopIcons`, `paths.TestMode` + `BERTH_TEST_ICON_PREFIX` gate), and route `Program.Main` (`CliCommand.Save` through `Composition` + `CliRunner`, preserving `args.Error` exit 2, `--version` exit 0 + `TestModeSwitches.Apply`, deferred commands exit 2, and top-level unhandled-exception logging + SPEC §18 stderr + exit 1). Done when: `berth save` writes a layout under `BERTH_DATA`.
2. Add unit tests (including both streams with literal expected sentences, named new/existing saves with `previous` verification, `save --name=` vs `save --name`, both sides of the icon-scope gate via `BerthPaths.FromEnvironment` delegates + fake desktop, and top-level catch after `Log` creation and during path resolution) plus the real `berth.exe save` process test with `ProbeIcon` and `BERTH_TEST_ICON_PREFIX`. Done when: all pass in Release alongside `ConsoleBehaviourTests`.

- [x] Add `CliRunner(LayoutService service, TextWriter output, TextWriter error)` with `int Run(CliArguments args)`, and `CliExit` constants `Done` 0, `Error` 1, `Usage` 2, `Refused` 3. **Corrected 2026-11-20:** narrowed constructor to `LayoutService` and the two writers for `save` (`LayoutStore`, `IMonitors`, `TimeProvider`, `TimeZoneInfo` join in §6 when `list` consumes them). Done when: a unit test with fakes and `StringWriter`s runs `Save` and gets 0. Cheaper substitute: writing to `Console` directly, which unit tests cannot capture.
- [x] Implement `Save`: call `LayoutService.Save(args.Name)`; on `Saved` write `Messages.Cli(result)` to `output` and return 0. **Corrected 2026-11-20:** also cover valid `--name` on a new layout and on an existing layout (verifying printed and persisted names and `previous` save), asserting literal expected strings on both streams including newline. Done when: the unit test's output is exactly `Saved 3 icons to "2 monitors".` followed by a newline and `error` is empty. Cheaper substitute: printing the result object.
- [x] Map results using `Messages.Cli(result)` for text and `OperationKind`/`Refusal` for stream and code: `Refused` with `InvalidName` writes to `error` and returns 2; any other `Refused` writes to `error` and returns 3; `Failed` writes to `error` and returns 1. **Corrected 2026-11-20:** assert exact literal contents of both `output` and `error` (with newline) for `save --name=` (`Enter a name.`, 2), 41-char `--name` (2), `save --name` (parser error, 2), unreachable desktop (3), and failed write (1). Done when: unit tests cover every case and `output` is empty in each. Cheaper substitute: exit code 1 for everything.
- [x] In `Program.Main` (via testable `Program.Run`), check `args.Error` first (exit 2), preserve `--version` (prints `Berth <AppInfo.Version>`, exit 0, runs `TestModeSwitches.Apply`), keep deferred commands (`Restore`, `Undo`, `List`, `Help`) and error-free `None` at exit 2 until their sections land, and for `CliCommand.Save` create `BerthPaths.FromEnvironment()`, create `Log(paths, TimeProvider.System)` before `Composition.Create(paths, log, ...)` (which builds `SystemMonitors`, `ShellDesktopIcons` wrapped in `ScopedDesktopIcons` ONLY when `paths.TestMode` is `true` and `BERTH_TEST_ICON_PREFIX` is non-whitespace, `LayoutStore`, `OperationLock.ForApp(paths)`, and `LayoutService`, and disposes the underlying `ShellDesktopIcons` on `Dispose()`), run `CliRunner`, and return its code; catch any unhandled exception, log it with `Log.Fail` when `Log` is available, write `Messages.Unexpected(ex.Message)` + newline to `error` (empty `output`), and return 1. **Corrected 2026-11-20:** routed strictly on `CliCommand.Save` so `--version` and deferred commands never touch `OperationLock`/desktop; created `Log` before `Composition.Create` so composition failures are logged with stack trace; added unit tests for both sides of the scope gate (using `BerthPaths.FromEnvironment` delegates + 2-icon fake desktop) and both entry-point catch paths; required `[Collection(DesktopCollectionDefinition.Name)]`, `ProbeIcon`, and `BerthProcess` for the real process test. Done when: `berth save` run by `BerthProcess` with a `ProbeIcon` and `BERTH_TEST_ICON_PREFIX=probe.Prefix` exits 0 and the sole layout under `paths.LayoutsFolder` lists exactly the probe icon, and all unit tests pass. Cheaper substitute: a stack trace on standard error, or a process test that saves the owner's whole desktop.
- [x] Add `tests/Berth.Tests/Cli/SaveCommandTests.cs` with the unit and real-process cases. Done when: all pass, and the invalid-name test fails when its exit code is changed to 3. Cheaper substitute: only the process test.
- [x] Commit: `cli: the save command`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*SaveCommandTests" --filter-class "*SaveCommandProcessTests" --filter-class "*ConsoleBehaviourTests" --filter-class "*TestModeSwitchesTests" --filter-class "*LayerRuleTests"` passes with `0` skipped, and fails when an invalid `--name` returns exit code 3 instead of 2.

> **Verified:** 2026-11-20 | §3 | fa79a12c84321591a256674b21535b1c6bb1f1d3
> **Review:** `docs/reviews/d03-t01-s3.json`

## 4. The restore command

> **Started:** 2026-11-20T16:10:59Z

`berth restore` with its refusals (SPEC §13, §15).

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Implement `Restore` in `CliRunner` and route `CliCommand.Restore` in `Program.Run`. **Corrected 2026-11-20:** included `Program.Run` routing and an entry-point test so `berth restore` does not exit 2. Done when: the full and partial lines match SPEC §13 and `Program.Run(["restore"], ...)` exits 0.
2. Cover all five refusals (`AutoArrange`, `NoLayout`, `DesktopUnavailable`, `Busy`, `UnreadableLayout`) and placement failure (`SnapBy = 1` + `SnapIds`). Done when: each asserts exact literal contents of both streams (including newline) and the SPEC exit code.

- [x] Implement `Restore` in `CliRunner` and route `CliCommand.Restore` in `Program.Run`: call `LayoutService.Restore()`, write `Messages.Cli(result)` to `output` and return 0 for `Restored`, full or partial. **Corrected 2026-11-20:** added `Program.Run(["restore"], ...)` routing test and exact literal both-stream assertions (including newline). Done when: unit tests get `Restored 3 icons from "2 monitors".` and, with one icon missing, `Restored 2 of 3 icons from "2 monitors". 1 is no longer on the desktop.`, both with exit 0 and empty stderr. Cheaper substitute: a non-zero code for a partial restore, which SPEC §13 says is still done.
- [x] Return 1 with the SPEC §13 "did not stay" line on standard error, and nothing on standard output, when the result is `Failed` with `NotPlaced` above 0. **Corrected 2026-11-20:** added both all-snapped and combined missing + `SnapIds` cases proving §8 clause order and placement-failure precedence. Done when: unit tests with `SnapBy` 1 on the fake compare both streams against exact literals and exit 1. Cheaper substitute: exit 0 for a restore Windows did not honour.
- [x] Return 3 with the SPEC §18 message on standard error (and empty standard output) for `AutoArrange`, `NoLayout`, `DesktopUnavailable`, `Busy`, and `UnreadableLayout` (leaving malformed JSON bytes intact). **Corrected 2026-11-20:** added `UnreadableLayout` case with malformed JSON byte-preservation check. Done when: a unit test per refusal compares exact literal contents of both streams and exit 3. Cheaper substitute: printing the refusal on standard output, where a script would read it as a result.
- [x] Add `tests/Berth.Tests/Cli/RestoreCommandTests.cs`. Done when: all pass, and the auto arrange test fails when its message is written to `output`. Cheaper substitute: one happy-path test.
- [x] Commit: `cli: the restore command`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*RestoreCommandTests" --filter-class "*SaveCommandTests" --filter-class "*LayerRuleTests"` passes with `0` skipped, and fails when a refusal is written to standard output.

> **Verified:** 2026-11-20 | §4 | ef12e9afd67c59d262710551079c324a7290b91a
> **Review:** `docs/reviews/d03-t01-s4.json`

## 5. The undo command

> **Started:** 2026-11-20T16:27:37Z

`berth undo`, the reverse of `berth restore` (SPEC §13, §15 "Undo").

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Implement `Undo` in `CliRunner` and route `CliCommand.Undo` in `Program.Run`. **Corrected 2026-11-20:** included `Program.Run` routing and an entry-point test via `Composition.ForTest` so `berth undo` does not exit 2. Done when: a restore then an undo returns the fake desktop to its pre-restore positions and `Program.Run(["undo"], ...)` exits 0.
2. Cover partial missing-only undo, placement failures (with `SnapIds` and preserved snapshot bytes), and all four refusals (`NothingToUndo`, `AutoArrange`, `DesktopUnavailable`, `Busy`). Done when: each compares exact literal contents of both streams (including newline) and the SPEC exit code.

- [x] Implement `Undo` in `CliRunner` and route `CliCommand.Undo` in `Program.Run`: call `LayoutService.Undo()`, write `Messages.Cli(result)` to `output` and return 0 for `Undone` (full `Put 3 icons back.` or missing-only partial `Put 2 of 3 icons back. 1 is no longer on the desktop.`), deleting `undo.json`. **Corrected 2026-11-20:** added `Program.Run(["undo"], ...)` test, missing-only partial undo test, and exact literal both-stream assertions (with `Environment.NewLine`). Done when: unit tests run `restore` then `undo` through `CliRunner` and `Program.Run` and the fake desktop equals its exact pre-restore positions with `undo.json` deleted. Cheaper substitute: an undo that only deletes `undo.json`.
- [x] Return 1 with `Put 2 of 3 icons back. 1 did not stay where it was.` (and combined `Put 1 of 3 icons back. 1 did not stay where it was. 1 is no longer on the desktop.`) on standard error and empty stdout when the undo is `Failed` with `NotPlaced` above 0, preserving the exact `undo.json` bytes for another try (SPEC §13; remaining core failure edges are covered by `tests/Berth.Tests/Core/UndoTests.cs`). **Corrected 2026-11-20:** targeted one icon with `SnapIds`, added combined missing + not-placed case, and verified exact preserved snapshot bytes. Done when: unit tests compare exact literal contents of both streams, exit 1, and identical `undo.json` bytes. Cheaper substitute: none needed.
- [x] Return 3 with exact literal SPEC §18 stderr (plus `Environment.NewLine`) and empty stdout for `NothingToUndo` (fresh data folder and second undo in a row), `AutoArrange` (with valid snapshot seeded), `DesktopUnavailable` (before placement), and `Busy`. **Corrected 2026-11-20:** required explicit CLI unit tests for all four refusals with exact both-stream assertions. Done when: all four refusal tests pass. Cheaper substitute: exit 0 with no output.
- [x] Add `tests/Berth.Tests/Cli/UndoCommandTests.cs`. Done when: all pass, and the second-undo test fails when `undo.json` is not deleted after an undo. Cheaper substitute: testing undo only through the window later.
- [x] Commit: `cli: the undo command`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*UndoCommandTests" --filter-class "*RestoreCommandTests" --filter-class "*LayerRuleTests"` passes with `0` skipped, and fails when a second `undo` in a row succeeds.

> **Verified:** 2026-11-20 | §5 | ba34c8ba6697aa8a6b11746b33c5af4e0804f851
> **Review:** `docs/reviews/d03-t01-s5.json`

## 6. The list command

> **Started:** 2026-11-20T16:37:56Z

`berth list` prints one aligned line per layout (SPEC §13).

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Implement `CliRunner.RunList` and route `CliCommand.List` in `Program.Run` through read-only dependencies (`LayoutStore`, `BerthPaths`, `IMonitors`, `Log`, `TimeZoneInfo`) without constructing `ShellDesktopIcons` or `OperationLock`. **Corrected 2026-11-20:** added read-only `Program.Run(["list"], ...)` route and test with a throwing mutation-composition factory. Done when: the SPEC §13 example line is produced for its data and `Program.Run(["list"], ...)` exits 0.
2. Cover alignment with sanitized single-line names, total ordering, monitor-read fallback, empty folder, corrupt-only and mixed corrupt/readable layouts, and folder-read failure (`Messages.ReadFailed`, exit 1). Done when: all unit tests pass in Release.

- [x] Implement `List` (`CliRunner.RunList` + read-only `Program.Run` dispatch): load with `LayoutStore.LoadAll`, catch `IOException`/`UnauthorizedAccessException` as SPEC §18 `Messages.ReadFailed` (logged as a single `FAIL` line without stack trace, stderr, exit 1), sanitize each name with `Messages.SingleLine` and measure terminal cell width before padding, order active first then `OrdinalIgnoreCase` name, `Ordinal` name, `Ordinal` key, and write `* ` or two spaces, padded display name, two spaces, `Formats.MonitorCount`, two spaces, `Formats.IconCount`, two spaces, `Formats.ListDate(savedAt, zone)`. **Corrected 2026-11-20:** sanitized names and measured terminal display cell width (handling wide East Asian characters) before padding, removed unused instance `List` constructor/arm, logged expected folder-read failure as one line, and split exact single-layout SPEC example from control-character and wide-Unicode alignment fixtures using a fixed non-UTC zone. Done when: unit tests get `* Docked  2 monitors  42 icons  2026-11-15 09:14` and exact aligned multi-row output. Cheaper substitute: tab characters, which do not align in every terminal.
- [x] Mark the active layout by reading `monitors.Read()` once and comparing with `ArrangementKey.From`; when `DesktopUnavailableException` is thrown, mark none and still list. Done when: three-layout unit tests cover matching active (alphabetically last), no match, and throwing monitor reader. Cheaper substitute: failing the whole list because the desktop is unreachable.
- [x] Print `No layouts saved yet.` and return 0 when there are no readable layouts (both empty folder and corrupt-only folder after reporting every corrupt file to `error`). **Corrected 2026-11-20:** covered corrupt-only folder with two corrupt files (bytes intact) and clarified SPEC §13. Done when: both tests compare exact stdout and stderr lines and exit 0. Cheaper substitute: printing nothing.
- [x] For each unreadable file write `Messages.UnreadableLayout(file.FileName)` to `error`, still list the readable ones, and return 0. Done when: a unit test with one good and one corrupt file gets exact lines on both streams and leaves corrupt bytes untouched. Cheaper substitute: hiding corrupt files from the one command a user would run to look for them.
- [x] Add `tests/Berth.Tests/Cli/ListCommandTests.cs`. Done when: all pass, and the alignment test fails when padding is removed. Cheaper substitute: asserting the name appears somewhere in the output.
- [x] Commit: `cli: the list command`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*ListCommandTests" --filter-class "*UndoCommandTests" --filter-class "*LayerRuleTests"` passes with `0` skipped, and fails when name padding is removed.

> **Verified:** 2026-11-20 | §6 | 76cfe6e426c444bc6667be911c431508911c6a73
> **Review:** `docs/reviews/d03-t01-s6.json`

## 7. Help, version and usage errors

> **Started:** 2026-11-20T16:55:39Z

The usage text and what a mistake prints (SPEC §13, §17).

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Usage.Text` in `src/Berth/Cli/Usage.cs` and a shared `CliRunner.TryRunStandalone` handler used by both `CliRunner.Run` and `Program.Run` (without touching storage, desktop, or tray). **Corrected 2026-11-20:** unified `Help`, `Version`, and `Error` across `CliRunner.Run` and `Program.Run`, stuck strictly to SPEC §13 forms (`--help`, `-h`, `/?`, `--version`), and documented the two-line parser diagnostic in `design/SPEC.md` §13. Done when: `berth --help` prints `Usage.Text`.
2. Add `tests/Berth.Tests/Cli/UsageTests.cs` checking structural completeness of `Usage.Text` (exact first line, both headings, 8 distinct command rows, 4 exit-code rows) and exercising `--help`, `-h`, `/?`, `--version`, `frobnicate`, and `save --name` through both `CliRunner.Run` and `Program.Run`. Done when: all pass in Release.

- [x] Add `Usage.Text` in `src/Berth/Cli/Usage.cs`: exact first line `Berth saves the positions of your desktop icons and puts them back.`, a `Usage:` block listing 8 distinct command rows (`berth`, `berth save`, `berth save --name <name>`, `berth restore`, `berth undo`, `berth list`, `berth --help, -h, /?`, `berth --version`) with their descriptions, and an `Exit codes:` block with 4 separate exit-code rows (`0`, `1`, `2`, `3`). **Corrected 2026-11-20:** verified exact line prefixes so removing bare `berth` or standalone `berth save` fails even while `berth save --name <name>` remains. Done when: `UsageTests` verifies exact structure. Cheaper substitute: a help text maintained apart from the command list, which drifts.
- [x] Implement `Help` (print `Usage.Text` to `output`, return 0) and `Version` (print `Berth ` plus `AppInfo.Version`, return 0) in shared `CliRunner.TryRunStandalone`, replacing the temporary `--version` branch in `Program.cs`. **Corrected 2026-11-20:** unified both entry points and verified `--help`, `-h`, `/?`, and `--version` through both `CliRunner.Run` (with throwing `IDesktopIcons`, `IMonitors`, `TimeProvider`, and a file blocking `LayoutsFolder`) and `Program.Run` (with throwing storage/composition/monitor delegates). Done when: unit tests compare both streams and exit 0. Cheaper substitute: two version strings.
- [x] For `CliArguments.Error`, write `berth: <error>` and then `Run "berth --help" for the commands.` to `error`, empty `output`, and return 2, without starting the tray app or resolving storage (clarified in `design/SPEC.md` §13). **Corrected 2026-11-20:** tested `frobnicate`, `save --name`, and `--nope` through both `CliRunner.Run` and `Program.Run`. Done when: both entry points get exact two-line stderr, empty stdout, and exit 2. Cheaper substitute: printing the whole usage text on every typo.
- [x] Add `tests/Berth.Tests/Cli/UsageTests.cs`. Done when: all pass, and the completeness test fails when a command row is removed from `Usage.Text`. Cheaper substitute: none needed.
- [x] Commit: `cli: help, version and usage errors`

**Test checkpoint:** `dotnet test --solution Berth.slnx -c Release --filter-class "*UsageTests" --filter-class "*SaveCommandTests" --filter-class "*CliArgumentsTests" --filter-class "*LayerRuleTests"` passes with `0` skipped, and fails when `undo` or `berth save` is removed from `Usage.Text`.

> **Verified:** 2026-11-20 | §7 | eaf879f815aff36697f1724bfe934a08f7a96bf2
> **Review:** `docs/reviews/d03-t01-s7.json`

## 8. Process tests of every command and exit code

The built executable, started for real, against the real desktop with a probe icon (SPEC §13 end to end).

**Phase:** 3
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add process tests for the commands that do not move icons. Done when: `list`, `--help`, `--version` and the usage error pass.
2. Add the save, restore and undo cycle on a probe icon. Done when: the probe returns and every exit code is the SPEC one.

- [ ] Add `tests/Berth.Tests/Cli/ProcessTests.cs` (trait `RealDesktop`) using `BerthProcess`: `--version` gives `Berth 0.1.0` and 0; `--help` gives text containing `berth restore` and 0; `frobnicate` gives 2 with `berth: unknown command "frobnicate"` on standard error and nothing on standard output; `list` on a fresh data folder gives `No layouts saved yet.` and 0. Done when: all four pass. Cheaper substitute: calling `CliRunner` in process, which misses the manifest, the entry point and the streams.
- [ ] Add the cycle with `ProbeIcon`, every `berth.exe` started with `BERTH_TEST_ICON_PREFIX` set to the probe's prefix so the executable sees only the probe: `save` (0, `Saved 1 icon to`), move the probe through `ScopedDesktopIcons`, `restore` (0, the probe is back where it was saved), `undo` (0, the probe is at the moved position), `undo` again (3, `There is nothing to undo.`), `list` (one line starting `* `). Skip with a clear message when auto arrange is on. Done when: the test passes on the build machine, the layout file lists exactly one icon, and the probe file is gone afterwards. Cheaper substitute: a cycle against fakes only, or one that lets the executable snapshot and reposition every icon on the owner's desktop.
- [ ] Add a refusal test: `restore` on a fresh data folder exits 3 with `This monitor arrangement has no saved layout.` on standard error. Done when: it passes. Cheaper substitute: testing refusals only in unit tests.
- [ ] Assert in each process test that the test's data folder holds a `local\logs\berth.log` with one line per command that changed something, and that the user's real `%APPDATA%\Berth` was not created or modified by the run (compare its last write time before and after, when it exists). Done when: the assertions pass. Cheaper substitute: trusting that `BERTH_DATA` is honoured.
- [ ] Commit: `cli: process tests of every command and exit code`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ProcessTests"` passes on the build machine, and its refusal test fails when `restore` with no layout exits 0.

## Verification

- From a terminal on the build machine: `berth save`, move an icon, `berth restore` puts it back, `berth undo` moves it again, `berth list` shows the layout, and `echo %ERRORLEVEL%` shows the SPEC §13 code after each.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
