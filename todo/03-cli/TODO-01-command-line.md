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
| 1 | §1 | Argument parser | D00 T01 §3 | [ ] |
| 2 | §2 | Console behaviour proven with real processes | D00 T01 §2 | [ ] |
| 3 | §3 | The save command | §1, D01 T01 §16, D02 T01 §8 | [ ] |
| 4 | §4 | The restore command | §3 | [ ] |
| 5 | §5 | The undo command | §4 | [ ] |
| 6 | §6 | The list command | §3, D01 T01 §17 | [ ] |
| 7 | §7 | Help, version and usage errors | §3 | [ ] |
| 8 | §8 | Process tests of every command and exit code | §2, §5, §6, §7 | [ ] |

## 1. Argument parser

A small hand-written parser: the grammar is six commands and one option, which no library is needed for.

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Add `CliArguments` in `src/Berth/Cli/CliArguments.cs`. Done when: every SPEC §13 command parses.
2. Add the usage errors. Done when: each bad input returns an error that names it.

- [ ] Add `CliCommand` (`None`, `Save`, `Restore`, `Undo`, `List`, `Help`, `Version`) and `CliArguments(CliCommand Command, string? Name, string? Error)` with `CliArguments.Parse(string[] args)`: no arguments gives `None`; `save`, `restore`, `undo`, `list` (lowercase only), `--help`, `-h`, `/?` and `--version` give their commands. Done when: a test per form passes. Cheaper substitute: accepting any prefix or case, which makes a typo run a command.
- [ ] Accept `--name <name>` and `--name=<name>` only after `save`, storing `Name`. Done when: tests cover both forms and a name with spaces passed as one argument. Cheaper substitute: joining the remaining arguments with spaces, which mangles quoting.
- [ ] Return `Error` (and `Command` `None`) for: an unknown command (`unknown command "x"`), an unknown option (`unknown option "--x"`), `--name` with no value (`--name needs a value`), `--name` given twice, `--name` on a command other than `save`, and any extra argument after a complete command. Done when: a test per case asserts the message. Cheaper substitute: ignoring what is not understood.
- [ ] Add `tests/Berth.Tests/Cli/CliArgumentsTests.cs`. Done when: all pass, and the unknown-command test fails when unknown words are treated as `None` (which would start the tray app, against SPEC §17). Cheaper substitute: testing only valid input.
- [ ] Commit: `cli: argument parser`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*CliArgumentsTests"` passes, and fails when an unknown first argument parses as `None`.

## 2. Console behaviour proven with real processes

The foundation plan's one technical bet: a console program with a detached console policy. This section proves it on the build machine or records the fallback.

**Phase:** 3
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add a process test helper that starts the built `berth.exe` and always stops its tree. Done when: a timed-out process leaves nothing running.
2. Prove both halves of the policy. Done when: both tests pass, or the fallback is implemented and recorded.

- [ ] Add `BerthProcess` in `tests/Berth.Tests/Cli/BerthProcess.cs`: finds the built `berth.exe` beside the test assembly's configuration output, starts it with `CreateNoWindow` `true`, redirected standard output and error, `BERTH_DATA` set to a `TestData` folder in the child's own `ProcessStartInfo.Environment` (never the test process's), an optional `BERTH_TEST_ICON_PREFIX`, a 30 second timeout, and `Kill(entireProcessTree: true)` on timeout and in `Dispose`. Done when: a test starts `berth.exe --version`, reads `Berth 0.1.0`, and a test with a deliberately hanging child (the helper pointed at `ping -n 60 127.0.0.1`) shows no `ping` process afterwards. Cheaper substitute: `process.Kill()` without the tree, which `AGENTS.md` forbids.
- [ ] Implement the SPEC §14 test-mode switches in `Program.Main`, honoured only when `BerthPaths.TestMode` is `true`: `BERTH_TEST_HOLD_MS` (wait that long, at most 5000, before exiting) and `BERTH_TEST_REPORT` (write `console=1` when `GetConsoleWindow()` is not zero, else `console=0`, to that file). Done when: unit tests over a `TestMode` helper show both ignored without `BERTH_DATA` and the hold capped at 5000. Cheaper substitute: test hooks that work in a user's normal run.
- [ ] Prove "a shell waits": start `cmd.exe /c "berth.exe --version & echo done"` with `BERTH_TEST_HOLD_MS=1500` in the child environment and output redirected to a file, and assert `cmd.exe` took at least 1500 ms to return, the file holds `Berth 0.1.0` then `done` in that order, and the exit code is 0. Done when: the test passes, and fails when the hold is removed from the measurement (proving the delay is what is observed). Cheaper substitute: asserting only that output arrived, which a program the shell did not wait for can also satisfy when it is fast.
- [ ] Prove "no console without a console": start `berth.exe --version` detached from any console (`CreateProcess` with `DETACHED_PROCESS` through `ProcessStartInfo` with `UseShellExecute` `true` and `WindowStyle` `Hidden`, the way Explorer starts it) with `BERTH_TEST_REPORT` set, and assert the report file reads `console=0`. The console window belongs to the console host, not to `berth.exe`, so the child reports its own state instead of the test searching for windows. Done when: the test passes, and the same test run against a copy of the executable whose manifest lacks `consoleAllocationPolicy` (built once by the test into a temp folder with `mt.exe` or by a second project configuration, started hidden, and killed by tree in `finally`) reads `console=1`. Cheaper substitute: looking at the screen once and writing "verified".
- [ ] Record the Windows build the proofs ran on (`[Environment]::OSVersion.Version`) in the foundation plan's "Console and window from one executable" section and in `design/SPEC.md` §13, replacing "to be confirmed" with "shown on build <number>, 2026-<date>". Done when: both documents name the build. Cheaper substitute: leaving the minimum as an unconfirmed claim in a public README.
- [ ] If either proof cannot pass on this Windows build, switch to the recorded fallback: `OutputType` `WinExe` with `AttachConsole(ATTACH_PARENT_PROCESS)` at the start of a command, record the decision and its cost (a terminal does not wait unless the command is run with `start /wait` or piped) in `docs/plans/2026-11-15-berth-foundation.md` and in `design/SPEC.md` §13, dated, and adjust the waiting test to the fallback's documented behaviour. Done when: the foundation plan's "Console and window from one executable" section says which approach shipped, with the date and the evidence. Cheaper substitute: shipping whichever compiles without recording it.
- [ ] Commit: `cli: console behaviour proven with real processes`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ConsoleBehaviourTests"` passes: the detached start reports `console=0`, the copy without the console allocation policy reports `console=1`, and the shell-wait test fails when the measured delay is below the hold.

## 3. The save command

`berth save [--name <name>]` (SPEC §13).

**Phase:** 3
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add `CliRunner` in `src/Berth/Cli/CliRunner.cs` and route `Program.Main` to it. Done when: `berth save` writes a layout under `BERTH_DATA`.
2. Add the refusal and error paths. Done when: each prints one line on standard error with the right code.

- [ ] Add `CliRunner(LayoutService service, LayoutStore store, IMonitors monitors, TextWriter output, TextWriter error, TimeProvider clock, TimeZoneInfo zone)` with `int Run(CliArguments args)`, and `CliExit` constants `Done` 0, `Error` 1, `Usage` 2, `Refused` 3. Done when: a unit test with fakes and `StringWriter`s runs `Save` and gets 0. Cheaper substitute: writing to `Console` directly, which unit tests cannot capture.
- [ ] Implement `Save`: call `LayoutService.Save(args.Name)`; on `Saved` write `Messages.Cli(result)` to `output` and return 0. Done when: the unit test's output is exactly `Saved 3 icons to "2 monitors".` followed by a newline. Cheaper substitute: printing the result object.
- [ ] Map results: `Refused` with `InvalidName` writes the SPEC §10 name error to `error` and returns 2; any other `Refused` writes `Messages.Refusal` and returns 3; `Failed` writes `Messages.Refusal` (or, for a restore or undo whose icons did not stay, `Messages.Cli`) to `error` and returns 1. Done when: unit tests cover an unreachable desktop (3), a 41 character `--name` (2) and a failed write (1), and `output` is empty in each. Cheaper substitute: exit code 1 for everything.
- [ ] In `Program.Main`, when `CliArguments.Parse` gives a command other than `None`, build the real `ShellDesktopIcons` (wrapped in `ScopedDesktopIcons` when `BerthPaths.TestMode` is `true` and `BERTH_TEST_ICON_PREFIX` is set, SPEC §14), `SystemMonitors`, `BerthPaths.FromEnvironment()`, `LayoutStore`, `OperationLock`, `Log` and `LayoutService` in one disposable `Composition.Create()` in `src/Berth/App/Composition.cs`, run `CliRunner`, and return its code; catch any unhandled exception, log it with `Log.Fail`, write the SPEC §18 "Something went wrong" line to standard error and return 1. Done when: `berth save` run by `BerthProcess` on the build machine with a `ProbeIcon` and its prefix exits 0 and the one file under `layouts\` in the test's data folder lists exactly one icon, the probe; and a unit test shows the scope is not applied when `BERTH_DATA` is unset even if the prefix variable is set. Cheaper substitute: a stack trace on standard error, or a process test that saves the owner's whole desktop.
- [ ] Add `tests/Berth.Tests/Cli/SaveCommandTests.cs` with the unit cases. Done when: all pass, and the invalid-name test fails when its exit code is changed to 3. Cheaper substitute: only the process test.
- [ ] Commit: `cli: the save command`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SaveCommandTests"` passes, and fails when an invalid `--name` returns exit code 3 instead of 2.

## 4. The restore command

`berth restore` with its refusals (SPEC §13, §15).

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Implement `Restore` in `CliRunner`. Done when: the full and partial lines match SPEC §13.
2. Cover the three refusals. Done when: each returns 3 with its SPEC §18 message.

- [ ] Implement `Restore`: call `LayoutService.Restore()`, write `Messages.Cli(result)` to `output` and return 0 for `Restored`, full or partial. Done when: unit tests get `Restored 3 icons from "2 monitors".` and, with one icon missing, `Restored 2 of 3 icons from "2 monitors". 1 is no longer on the desktop.`, both with exit 0. Cheaper substitute: a non-zero code for a partial restore, which SPEC §13 says is still done.
- [ ] Return 1 with the SPEC §13 "did not stay" line on standard error, and nothing on standard output, when the result is `Failed` with `NotPlaced` above 0. Done when: a unit test with `SnapBy` 1 on the fake compares the exact line and code. Cheaper substitute: exit 0 for a restore Windows did not honour.
- [ ] Return 3 with the SPEC §18 message on standard error for auto arrange, no layout and an unreachable desktop, and for `Busy`. Done when: a unit test per refusal compares the exact message and code. Cheaper substitute: printing the refusal on standard output, where a script would read it as a result.
- [ ] Add `tests/Berth.Tests/Cli/RestoreCommandTests.cs`. Done when: all pass, and the auto arrange test fails when its message is written to `output`. Cheaper substitute: one happy-path test.
- [ ] Commit: `cli: the restore command`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*RestoreCommandTests"` passes, and fails when a refusal is written to standard output.

## 5. The undo command

`berth undo`, the reverse of `berth restore` (SPEC §13, §15 "Undo").

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Implement `Undo` in `CliRunner`. Done when: a restore then an undo returns the fake desktop to its start.
2. Cover "nothing to undo". Done when: it returns 3 with the SPEC message.

- [ ] Implement `Undo`: call `LayoutService.Undo()`, write `Put 3 icons back.` (through `Messages.Cli`) and return 0 for `Undone`. Done when: a unit test runs `restore` then `undo` through `CliRunner` and the fake desktop equals its starting positions. Cheaper substitute: an undo that only deletes `undo.json`.
- [ ] Return 1 with `Put 2 of 3 icons back. 1 did not stay where it was.` on standard error when the undo is `Failed` with `NotPlaced` above 0, leaving `undo.json` for another try (SPEC §13). Done when: a unit test with `SnapBy` 1 compares the line and code and finds the file still present. Cheaper substitute: none needed.
- [ ] Return 3 with "There is nothing to undo." when the result is `NothingToUndo`, and 3 with the matching message for auto arrange, an unreachable desktop and `Busy`. Done when: unit tests cover a fresh data folder and a second undo in a row. Cheaper substitute: exit 0 with no output.
- [ ] Add `tests/Berth.Tests/Cli/UndoCommandTests.cs`. Done when: all pass, and the second-undo test fails when `undo.json` is not deleted after an undo. Cheaper substitute: testing undo only through the window later.
- [ ] Commit: `cli: the undo command`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*UndoCommandTests"` passes, and fails when a second `undo` in a row succeeds.

## 6. The list command

`berth list` prints one aligned line per layout (SPEC §13).

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Implement `List` in `CliRunner`. Done when: the SPEC §13 example line is produced for its data.
2. Cover the empty and unreadable cases. Done when: both print what SPEC says.

- [ ] Implement `List`: load with `LayoutStore.LoadAll`, order the active layout first then by name, and write per layout `* ` or two spaces, the name padded to the longest name, two spaces, `Formats.MonitorCount`, two spaces, `Formats.IconCount`, two spaces, `Formats.ListDate`. Done when: a unit test with the SPEC example's data gets `* Docked  2 monitors  42 icons  2026-11-15 09:14` aligned with a second, longer-named layout. Cheaper substitute: tab characters, which do not align in every terminal.
- [ ] Mark the active layout by comparing each key with `ArrangementKey.From(monitors.Read())`; when the monitors cannot be read, mark none and still list. Done when: unit tests cover an active layout, none active, and unreadable monitors. Cheaper substitute: failing the whole list because the desktop is unreachable.
- [ ] Print `No layouts saved yet.` and return 0 when there are none. Done when: a unit test on an empty data folder compares the line. Cheaper substitute: printing nothing.
- [ ] For each unreadable file write the SPEC §18 "could not be read" message to `error`, still list the readable ones, and return 0. Done when: a unit test with one good and one corrupt file gets one line on each stream. Cheaper substitute: hiding corrupt files from the one command a user would run to look for them.
- [ ] Add `tests/Berth.Tests/Cli/ListCommandTests.cs`. Done when: all pass, and the alignment test fails when padding is removed. Cheaper substitute: asserting the name appears somewhere in the output.
- [ ] Commit: `cli: the list command`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ListCommandTests"` passes, and fails when name padding is removed.

## 7. Help, version and usage errors

The usage text and what a mistake prints (SPEC §13, §17).

**Phase:** 3
**Needs:** none
**Surface:** no

**Build order:**

1. Add the usage text in `src/Berth/Cli/Usage.cs`. Done when: `berth --help` prints it.
2. Route usage errors. Done when: an unknown command prints the error and a hint, and exits 2.

- [ ] Add `Usage.Text`: a first line `Berth saves the positions of your desktop icons and puts them back.`, a `Usage:` block listing every SPEC §13 command with its one-line description (the "Does" column), and an `Exit codes:` block with the four codes. Done when: a unit test asserts every command and every exit code of SPEC §13 appears. Cheaper substitute: a help text maintained apart from the command list, which drifts.
- [ ] Implement `Help` (print `Usage.Text` to `output`, return 0) and `Version` (print `Berth ` plus `AppInfo.Version`, return 0), replacing the temporary code in `Program.cs`. Done when: unit tests compare both. Cheaper substitute: two version strings.
- [ ] For `CliArguments.Error`, write `berth: <error>` and then `Run "berth --help" for the commands.` to `error`, and return 2, without starting the tray app. Done when: a unit test for `berth frobnicate` gets both lines and 2. Cheaper substitute: printing the whole usage text on every typo.
- [ ] Add `tests/Berth.Tests/Cli/UsageTests.cs`. Done when: all pass, and the completeness test fails when a command is removed from `Usage.Text`. Cheaper substitute: none needed.
- [ ] Commit: `cli: help, version and usage errors`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*UsageTests"` passes, and fails when `undo` is removed from `Usage.Text`.

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
