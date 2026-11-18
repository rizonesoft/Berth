---
schema_version: 1
id: layout-core
domain: 01-core
status: active
title: "TODO-01 -- Layout core"
depends_on: ["dotnet-foundation"]
---

# TODO-01 -- Layout core

> **Goal:** Everything Berth decides without touching Windows: the layout and settings files, the arrangement key, and the save, restore, undo, revert, rename and delete rules, all proven against fakes, with one wording for every result.

> **Current state:** Measured 2026-11-15. No C# code exists beyond what `D00 T01` creates (`AppInfo`, the test project and `TestData`). The rules are written in `design/SPEC.md` §3, §8, §10, §13, §14, §15 and §18.

## Inputs

- `design/SPEC.md` §3 (formats), §8 (status sentences), §10 (names, revert, delete), §13 (command output), §14 (storage), §15 (save and restore rules), §18 (messages and the log).
- `docs/plans/2026-11-15-berth-foundation.md`: "Seams that make it testable".
- -> XREF: D00 T01 §3 -- the test project and `TestData` this file's tests use
- -> XREF: D02 T01 §3 -- implements the `IDesktopIcons` seam declared in §8 here
- -> XREF: D03 T01 §3 -- the commands call `LayoutService` and print `Messages`
- -> XREF: D04 T01 §8 -- the tray menu calls `LayoutService`
- -> XREF: D05 T01 §1 -- the window shows `LayoutComparison` and `Messages`
- -> XREF: D06 T01 §2 -- the automatic restore calls `LayoutService.Restore`

## Outcome

- A layout saved through `LayoutService` is on disk in the SPEC §14 shape, read back and compared, and survives a failed write untouched.
- Restore, undo, revert, rename and delete behave as SPEC §10 and §15 say against `FakeDesktopIcons`, including every refusal.

**Adjacency:** list=applicable @ D01 T01 §5; document=not-applicable (a layout is not carried or printed); settings=applicable @ D01 T01 §6; reporting=not-applicable (no summaries beyond the status line); notifications=not-applicable (the balloon is owned by D06 T01 §3); permissions=not-applicable (one user, no roles); audit=applicable @ D01 T01 §15; exchange=not-applicable (SPEC §20 rules out import and sync); reverse=applicable @ D01 T01 §11

**Adjacency rationale:** Layouts are the records and `LayoutStore` lists them. The log is the audit trail. Every create has an undo: a restore has `Undo` (§11), a save over an earlier one has `Revert` (§12), and a layout has `Delete` (§12).

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Layout, monitor and icon records | D00 T01 §3 | [ ] |
| 2 | §2 | Arrangement key | §1 | [ ] |
| 3 | §3 | Data paths that honour BERTH_DATA | D00 T01 §3 | [ ] |
| 4 | §4 | Atomic JSON file with read-back | §3 | [ ] |
| 5 | §5 | Layout store | §1, §2, §4 | [ ] |
| 6 | §6 | Settings store | §4 | [ ] |
| 7 | §7 | Layout names: defaults and validation | §1 | [ ] |
| 8 | §8 | Desktop and monitor seams with fakes | §1 | [ ] |
| 9 | §9 | Save operation | §5, §7, §8, §14, §15 | [ ] |
| 10 | §10 | Restore operation with its undo snapshot | §9 | [ ] |
| 11 | §11 | Undo operation | §10 | [ ] |
| 12 | §12 | Rename, revert and delete operations | §9 | [ ] |
| 13 | §13 | Comparison of a layout with the desktop | §8 | [ ] |
| 14 | §14 | Operation lock across processes | §3 | [ ] |
| 15 | §15 | Log file with a size cap | §3 | [ ] |
| 16 | §16 | One wording for every result | §9, §10, §11, §12, §17 | [ ] |
| 17 | §17 | Date, count and monitor size formats | §13 | [ ] |
| 18 | §18 | Setting changes applied, read back and logged in one place | §6, §15, §16 | [ ] |

## 1. Layout, monitor and icon records

The data shapes of SPEC §14, as immutable records with their JSON names, so every later section reads and writes the same thing.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add the records in `src/Berth/Core/Model/`. Done when: they compile and the layer rule test still passes.
2. Add the source-generated JSON context. Done when: the SPEC §14 example round-trips byte for byte in meaning.

- [ ] Add `MonitorInfo(int X, int Y, int Width, int Height, int Scale, bool Primary)` in `src/Berth/Core/Model/MonitorInfo.cs` (SPEC §14 `monitors`). Done when: it compiles as a `sealed record`. Cheaper substitute: a mutable class whose values can change after a key was computed.
- [ ] Add `IconPosition(string Id, string Name, int X, int Y)` in `src/Berth/Core/Model/IconPosition.cs` (SPEC §14 `icons`). Done when: it compiles as a `sealed record`. Cheaper substitute: using the display name as the identifier (SPEC §15 "Matching").
- [ ] Add `PreviousSave(DateTimeOffset SavedAt, IReadOnlyList<IconPosition> Icons)` and `Layout(int SchemaVersion, string Key, string Name, DateTimeOffset SavedAt, IReadOnlyList<MonitorInfo> Monitors, IReadOnlyList<IconPosition> Icons, PreviousSave? Previous)` in `src/Berth/Core/Model/Layout.cs`. Done when: both compile. Cheaper substitute: a `previous` list of unlimited depth, which SPEC §14 limits to one.
- [ ] Add `BerthJson` in `src/Berth/Core/Model/BerthJson.cs`: a `JsonSerializerContext` for `Layout` with camelCase names, indented output and `savedAt` written as UTC ending in `Z`. Done when: serialising a `Layout` gives the property names of the SPEC §14 example. Cheaper substitute: reflection-based serialisation, which single-file trimming can break.
- [ ] Add `tests/Berth.Tests/Core/LayoutJsonTests.cs`: parse the SPEC §14 example text, serialise it again, parse that, and assert the two `Layout` values are equal, including `previous: null` and a `previous` with icons. Done when: the test passes, and fails when a property is renamed in the record. Cheaper substitute: asserting only that serialisation does not throw.
- [ ] Commit: `core: layout, monitor and icon records`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutJsonTests"` passes, and fails when `IconPosition.Id` is renamed.

## 2. Arrangement key

The same monitors in the same places at the same scale must always map to the same layout file (SPEC §14, last bullet).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `ArrangementKey.From` in `src/Berth/Core/ArrangementKey.cs`. Done when: a known input gives a known 16 character key.
2. Test order independence and sensitivity. Done when: reordering monitors keeps the key and changing any field changes it.

- [ ] Add `ArrangementKey.From(IReadOnlyList<MonitorInfo> monitors)` that sorts by `X` then `Y`, writes each as `x,y,width,height,scale`, joins with `;`, hashes the UTF-8 bytes with SHA-256 and returns the first 16 lowercase hexadecimal characters (SPEC §14). Done when: one monitor `0,0,2560,1440,100` gives the value the test computes independently with `SHA256.HashData`. Cheaper substitute: `GetHashCode`, which differs between runs.
- [ ] Refuse an empty list with `ArgumentException`. Done when: a test asserts the exception. Cheaper substitute: returning the hash of an empty string, which would make "no monitors" a real layout.
- [ ] Add `tests/Berth.Tests/Core/ArrangementKeyTests.cs` covering: same key for two orders of the same two monitors; a different key when `X`, `Y`, `Width`, `Height` or `Scale` changes by one; `Primary` not affecting the key; 16 characters of `[0-9a-f]`. Done when: all pass, and the order test fails when the sort is removed. Cheaper substitute: one happy-path assertion.
- [ ] Commit: `core: arrangement key`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ArrangementKeyTests"` passes, and fails when the sort in `ArrangementKey.From` is removed.

## 3. Data paths that honour BERTH_DATA

One place decides where Berth's files live, and tests redirect it (SPEC §14, first bullet).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `BerthPaths` in `src/Berth/Core/BerthPaths.cs`. Done when: it returns the SPEC §14 paths.
2. Test the override. Done when: with `BERTH_DATA` set, every path is inside it.

- [ ] Add `BerthPaths` with `DataRoot`, `LocalRoot`, `LayoutsFolder`, `LayoutFile(string key)`, `SettingsFile`, `UndoFile`, `LogFile`, built by `BerthPaths.ForRoot(string root)` (`<root>\roaming` and `<root>\local`) and by `BerthPaths.FromEnvironment(Func<string, string?>? getenv = null)`: `%APPDATA%\Berth` and `%LOCALAPPDATA%\Berth`, or `ForRoot(<BERTH_DATA>)` when the variable is set and not empty (SPEC §14). `getenv` defaults to `Environment.GetEnvironmentVariable`, and tests pass their own function instead of changing the process environment. Also expose `bool TestMode` (the variable was set) for the SPEC §14 test-mode switches. Done when: a test with `TestData` and a fake `getenv` sees all seven paths start with the temp folder and `TestMode` `true`. Cheaper substitute: paths built in each class that needs one.
- [ ] Make `LayoutFile` refuse a key that is not exactly 16 characters of `[0-9a-f]` with `ArgumentException`. Done when: a test passes `..\x` and asserts the exception. Cheaper substitute: accepting any string, which lets a crafted key escape the layouts folder (`SECURITY.md`).
- [ ] Add `EnsureFolders()` that creates `LayoutsFolder` and the log folder. Done when: a test calls it twice on an empty temp root and both folders exist. Cheaper substitute: creating folders at each write site.
- [ ] Add `tests/Berth.Tests/Core/BerthPathsTests.cs` for the override, the default shape (asserting only that the default `DataRoot` ends with `\Berth` and is under the `ApplicationData` special folder, without writing there) and the key refusal. Done when: all pass, and the override test fails when `FromEnvironment` ignores the variable. Cheaper substitute: a test that writes into the real `%APPDATA%`.
- [ ] Commit: `core: data paths that honour BERTH_DATA`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*BerthPathsTests"` passes, and fails when `FromEnvironment` ignores `BERTH_DATA`.

## 4. Atomic JSON file with read-back

Every write Berth makes goes through one routine: temporary file, flush, replace, read back, compare (SPEC §14 "Every write").

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `JsonFile` in `src/Berth/Core/JsonFile.cs`. Done when: a write is readable and equal.
2. Prove a failed write leaves the old file. Done when: the fault-injection test passes.

- [ ] Add `JsonFile.Write<T>(string path, T value, JsonTypeInfo<T> info)`: serialise to `<path>.tmp-<guid>` in the same folder, `Flush(true)`, read the temporary file back and compare its bytes with what was serialised (throw `IOException` naming the path when they differ), and only then `File.Move(temp, path, overwrite: true)` (SPEC §14 "Every write": verify, then replace, so a failure at any point leaves the old file). Done when: a test writes a value, reads the file and finds equal content, and no `.tmp-` file remains; and a test that injects a verification failure through an internal seam finds the earlier target byte for byte unchanged. Cheaper substitute: replacing first and verifying after, which cannot give the old file back.
- [ ] Delete the temporary file in a `finally` when the write did not complete. Done when: a test that makes serialisation throw finds no `.tmp-` file and the earlier target unchanged. Cheaper substitute: leaving temporary files behind for the next run to trip on.
- [ ] Add `JsonFile.TryRead<T>(string path, JsonTypeInfo<T> info, out T? value, out string? reason)`: `false` with `reason` `missing` when the file is absent, and `false` with the parser's message for invalid JSON; it never deletes or rewrites the file (SPEC §14 "never overwritten or deleted"). Done when: tests cover a good file, a missing file and a file holding `{`, and the bad file's bytes are unchanged afterwards. Cheaper substitute: catching the exception and returning a default value, which hides a corrupt layout.
- [ ] Add `tests/Berth.Tests/Core/JsonFileTests.cs` with those cases plus: writing over an existing file replaces it; a write into a folder that does not exist throws and creates nothing. Done when: all pass, and the leftover-temp test fails when the `finally` is removed. Cheaper substitute: testing only the happy path.
- [ ] Commit: `core: atomic JSON file with read-back`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*JsonFileTests"` passes, and fails when `JsonFile.Write` writes directly to the target path.

## 5. Layout store

Loads every layout, saves one, deletes one, and reports files it cannot read without touching them (SPEC §14, §9 third notice).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `LayoutStore` in `src/Berth/Core/LayoutStore.cs`. Done when: save then load returns the layout.
2. Add schema checks and the unreadable list. Done when: each bad file is reported and left byte for byte.

- [ ] Add `LayoutStore(BerthPaths paths)` with `Save(Layout layout)` writing `LayoutFile(layout.Key)` through `JsonFile.Write`. Done when: a test saves a layout and finds `layouts\<key>.json` in the temp root. Cheaper substitute: naming the file after the layout's display name, which a rename would orphan.
- [ ] Make `Save` refuse, with `UnreadableLayoutException` carrying the file name, when the destination exists and is unreadable by the rules below (SPEC §14: an unreadable layout file is never overwritten). Done when: a test puts `{` in `layouts\<key>.json`, calls `Save` for that key, gets the exception, and the file's bytes are unchanged. Cheaper substitute: skipping the file on load but overwriting it on the next save.
- [ ] Add `LoadAll()` returning `LayoutSet(IReadOnlyList<Layout> Layouts, IReadOnlyList<UnreadableFile> Unreadable)`, where `UnreadableFile(string FileName, string Reason)`. Done when: a test with two good files and one holding `{` gets two layouts and one unreadable entry naming that file. Cheaper substitute: throwing on the first bad file, which would hide every good layout.
- [ ] Treat as unreadable, with a reason, a file whose `schemaVersion` is not 1, whose `key` does not match its file name, whose `name` is empty, whose `monitors` is empty, or that holds two icons with the same `id` ignoring case. Done when: a test per rule reports the file and its bytes are unchanged. Cheaper substitute: loading a file under a key it does not carry, so a save would overwrite another arrangement.
- [ ] Add `Find(string key)` and `Delete(string key)`, where `Delete` removes the file and returns `false` when it did not exist. Done when: tests cover both results and a deleted layout is gone from `LoadAll`. Cheaper substitute: marking a layout deleted in memory only.
- [ ] Ignore files in the layouts folder that do not end in `.json` (leftover temporary files included). Done when: a test with `x.json.tmp-1` present gets neither a layout nor an unreadable entry for it. Cheaper substitute: reporting temporary files as corrupt layouts.
- [ ] Add `tests/Berth.Tests/Core/LayoutStoreTests.cs` with the cases above. Done when: all pass, and the key-mismatch test fails when that check is removed. Cheaper substitute: tests that share one folder and depend on order.
- [ ] Commit: `core: layout store`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutStoreTests"` passes, and fails when the key and file name check is removed.

## 6. Settings store

`settings.json` holds the automatic restore switch and the window placement (SPEC §14).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Settings` and `SettingsStore` in `src/Berth/Core/SettingsStore.cs`. Done when: defaults load from a missing file.
2. Add save with read-back. Done when: a saved value loads again.

- [ ] Add `WindowPlacement(int X, int Y, int Width, int Height)` and `Settings(int SchemaVersion, bool RestoreAutomatically, WindowPlacement? Window)` with `Settings.Default` (`1`, `false`, `null`), and register `Settings` in `BerthJson`. Done when: serialising `Settings.Default` gives the SPEC §14 shape with `"window": null`. Cheaper substitute: storing "Start with Windows" here, which SPEC §11 says the registry owns.
- [ ] Add `SettingsStore.Load()` returning `SettingsLoad(Settings Value, bool Exists, string? Unreadable)`: defaults with `Exists` `false` for a missing file, and defaults with `Unreadable` set for a bad or wrong-version file, which is left untouched. Done when: tests cover missing, good, invalid JSON and `schemaVersion` 2. Cheaper substitute: overwriting an unreadable settings file with defaults on load.
- [ ] Add `SettingsStore.Save(Settings value)` through `JsonFile.Write`, refusing with `InvalidOperationException` while the file on disk is unreadable. Done when: a test saves `RestoreAutomatically` `true` and loads it back, and a save over a corrupt file throws and leaves it. Cheaper substitute: a save that silently replaces a file the user may want to repair.
- [ ] Add `tests/Berth.Tests/Core/SettingsStoreTests.cs` with those cases. Done when: all pass, and the missing-file test fails when `Exists` is always `true` (`D04 T01 §1` relies on it for the first run). Cheaper substitute: testing through the window.
- [ ] Commit: `core: settings store`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SettingsStoreTests"` passes, and fails when `Load` reports `Exists` `true` for a missing file.

## 7. Layout names: defaults and validation

The default name of a first save and the three naming rules of SPEC §10, in one place for the dialog and `--name`.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `LayoutNames` in `src/Berth/Core/LayoutNames.cs`. Done when: defaults and validation match SPEC §10 and §15.
2. Test each rule. Done when: each error is returned for its input.

- [ ] Add `LayoutNames.Default(int monitorCount, IEnumerable<string> taken)` returning `1 monitor` or `N monitors`, then ` (2)`, ` (3)` and so on until the name is not in `taken` ignoring case (SPEC §15 "Save"). Done when: tests give `2 monitors`, then `2 monitors (2)` when the first is taken, then `(3)`. Cheaper substitute: a timestamp name, which SPEC does not allow.
- [ ] Add `LayoutNames.Validate(string name, IEnumerable<string> others)` returning `NameError.None`, `Empty`, `TooLong` or `Taken`: the name is trimmed first, 1 to 40 characters, and unique among `others` ignoring case (SPEC §10). Done when: tests cover a blank name, 41 characters, 40 characters, a case-only clash, and a valid name with surrounding spaces. Cheaper substitute: comparing names case-sensitively.
- [ ] Add `LayoutNames.Trimmed(string name)` used by every caller before storing. Done when: a test shows `"  Docked "` is stored as `Docked` through `LayoutService.Rename` (asserted in §12). Cheaper substitute: trimming in the dialog only, so `--name` stores spaces.
- [ ] Add `tests/Berth.Tests/Core/LayoutNamesTests.cs`. Done when: all pass, and the clash test fails when comparison becomes case-sensitive. Cheaper substitute: one test of the default name.
- [ ] Commit: `core: layout names`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutNamesTests"` passes, and fails when name comparison is made case-sensitive.

## 8. Desktop and monitor seams with fakes

The two interfaces that separate the rules from Windows, and the fakes every rule test uses (foundation plan, "Seams").

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add the interfaces in `src/Berth/Core/`. Done when: they compile with no Windows type in their signatures.
2. Add the fakes in the test project. Done when: a fake round-trips positions.

- [ ] Add `IDesktopIcons` in `src/Berth/Core/IDesktopIcons.cs` with `IReadOnlyList<IconPosition> ReadIcons()`, `void SetPositions(IReadOnlyList<IconPosition> icons)`, `bool AutoArrange { get; }` and `bool TryTurnAutoArrangeOff()`, each documented to throw `DesktopUnavailableException` when the desktop cannot be reached. Done when: the layer rule test passes. Cheaper substitute: exposing a COM interface type in the signature.
- [ ] Add `DesktopUnavailableException` in `src/Berth/Core/DesktopUnavailableException.cs`. Done when: it compiles and carries an inner exception. Cheaper substitute: letting `COMException` cross into Core.
- [ ] Add `IMonitors` in `src/Berth/Core/IMonitors.cs` with `IReadOnlyList<MonitorInfo> Read()`. Done when: it compiles. Cheaper substitute: reading `System.Windows.Forms.Screen` inside Core.
- [ ] Add `FakeDesktopIcons` in `tests/Berth.Tests/Fakes/FakeDesktopIcons.cs`: holds a list of icons, `SetPositions` moves only icons whose `Id` exists (ignoring case) and ignores the rest, `AutoArrange` is settable, `Unavailable` makes every member throw, and `SnapBy` (default 0) can alter set positions to imitate Windows adjusting them. Done when: a test sets a position, reads it back, and sees an unknown `Id` ignored. Cheaper substitute: a fake that accepts any position for any `Id`, hiding the missing-icon rule.
- [ ] Add `FakeMonitors` in `tests/Berth.Tests/Fakes/FakeMonitors.cs` with a settable list. Done when: a test reads back what it set. Cheaper substitute: none needed.
- [ ] Add `tests/Berth.Tests/Fakes/FakeDesktopIconsTests.cs` for those behaviours. Done when: all pass, and the unknown-`Id` test fails when the fake starts adding unknown icons. Cheaper substitute: untested fakes whose own bugs pass as product behaviour.
- [ ] Commit: `core: desktop and monitor seams with fakes`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*FakeDesktopIconsTests"` passes, and `LayerRuleTests` still passes with the new interfaces in `Berth.Core`.

## 9. Save operation

`LayoutService.Save` reads the desktop and the monitors and writes the current arrangement's layout (SPEC §15 "Save").

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `LayoutService` and `OperationResult` in `src/Berth/Core/`. Done when: a first save creates a named layout.
2. Add the second-save and refusal paths. Done when: `previous` is filled and an unreachable desktop is a refusal.

- [ ] Add `OperationResult` in `src/Berth/Core/OperationResult.cs` with `Kind` (`Saved`, `Restored`, `Undone`, `Reverted`, `Renamed`, `Deleted`, `Refused`, `Failed`), `LayoutName`, `Total`, `InPlace`, `NotPlaced` (on the desktop but not at the wanted position after read-back), `Missing`, `ReplacedEarlierSave`, `SavedAt` (the save a revert returned to), `Origin` (`null`, or text such as `automatic, display change`), `Refusal` (`None`, `AutoArrange`, `NoLayout`, `DesktopUnavailable`, `NothingToUndo`, `Busy`, `InvalidName`, `UnreadableLayout`), `NameError`, `Path` and `Detail`. A restore or undo with `NotPlaced` above 0 has `Kind` `Failed`, keeping its counts (SPEC §8, §13, §16). Done when: it compiles as a `sealed record` with static factory methods for each kind. Cheaper substitute: returning strings, which the window, tray and command line would each reword.
- [ ] Add `LayoutService(IDesktopIcons desktop, IMonitors monitors, LayoutStore store, BerthPaths paths, OperationLock gate, Log log, TimeProvider clock)` in `src/Berth/Core/LayoutService.cs` with `Save(string? name = null)`. A first save of an arrangement builds a `Layout` with `ArrangementKey.From`, `LayoutNames.Default` (or the validated `name`), the icons and `clock` time, and `Previous` `null`. Done when: a test with two fake monitors and three icons gets `Saved`, `Total` 3, name `2 monitors`, and the file on disk. Cheaper substitute: saving without reading the monitors, so every arrangement shares one file.
- [ ] On a later save of the same arrangement keep the name (or apply a validated `name`), move the earlier `Icons` and `SavedAt` into `Previous`, and set `ReplacedEarlierSave` `true`. Done when: a test saves twice with different positions and finds the first positions in `Previous`. Cheaper substitute: overwriting with no `Previous`, which loses the undo of a save.
- [ ] Return `Refused` with `DesktopUnavailable` when `ReadIcons` or `IMonitors.Read` throws `DesktopUnavailableException`, `Refused` with `InvalidName` and the `NameError` when `name` fails `LayoutNames.Validate`, `Refused` with `UnreadableLayout` and the file name in `Path` when the arrangement's existing file is unreadable (`UnreadableLayoutException`), and `Failed` with `Path` and `Detail` when the write throws `IOException` or `UnauthorizedAccessException`. Done when: a test per case asserts the result and that no layout file was created or changed. Cheaper substitute: letting the exception escape to each caller.
- [ ] Run the whole operation inside `OperationLock.Run`, returning `Refused` with `Busy` when the lock is not acquired, and write one log line per result through `Log` (kind, counts and layout name until §16 supplies the wording). Done when: a test holding the lock from a second `OperationLock` gets `Busy` and no file change, and a successful save leaves exactly one `INFO` line. Cheaper substitute: locking and logging in the callers, so the command line and the tray app can interleave.
- [ ] Save while auto arrange is on (SPEC §9: "Save current layout still works"). Done when: a test with `AutoArrange` `true` gets `Saved`. Cheaper substitute: refusing saves under auto arrange.
- [ ] Add `tests/Berth.Tests/Core/SaveTests.cs` with those cases. Done when: all pass, and the second-save test fails when `Previous` is not written. Cheaper substitute: asserting on the in-memory result only, never the file.
- [ ] Commit: `core: save operation`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SaveTests"` passes, and fails when a second save stops filling `Previous`.

## 10. Restore operation with its undo snapshot

`LayoutService.Restore` puts icons back and records where they were first (SPEC §15 "Restore", "In place", "Matching").

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Restore()` with its three refusals. Done when: each refusal moves nothing and writes no undo file.
2. Add the snapshot, the positioning and the read-back count. Done when: counts match a fake desktop with a missing icon.

- [ ] Add `Restore(string? origin = null)`, carrying `origin` into the result and its log line. Refuse, in this order (SPEC §15), with `DesktopUnavailable` (reading the monitors or the desktop throws), `NoLayout` (no layout for `ArrangementKey.From(monitors)`, or its file is unreadable) and `AutoArrange` (`IDesktopIcons.AutoArrange` is `true`). Done when: a test per refusal asserts the result, that `FakeDesktopIcons.SetPositions` was never called, and that `undo.json` does not exist; and a test with no layout and auto arrange on gets `NoLayout` (`D06 T01 §2` relies on this order). Cheaper substitute: attempting the restore under auto arrange and reporting success.
- [ ] Before moving anything, write `UndoSnapshot(int SchemaVersion, string Key, DateTimeOffset TakenAt, IReadOnlyList<IconPosition> Icons)` with the positions read now to `BerthPaths.UndoFile` through `JsonFile.Write` (registered in `BerthJson`). Done when: a test finds the pre-restore positions in `undo.json` after a restore. Cheaper substitute: keeping the snapshot in memory, so `berth undo` in a new process has nothing.
- [ ] Call `SetPositions` with every saved icon whose `Id` is on the desktop now, matched ignoring case, and with no icon that is not in the layout. Done when: a test with a saved icon missing from the desktop and an unsaved icon present sees exactly the common icons passed. Cheaper substitute: matching by display name (SPEC §15 "Matching").
- [ ] Read the desktop back and count `Total` (saved icons), `InPlace` (read-back position equals the saved position exactly), `NotPlaced` (on the desktop, read-back differs) and `Missing` (saved icons not on the desktop). Return `Restored` when `NotPlaced` is 0 and `Failed` with the same counts when it is not. Done when: a test with 3 saved icons, one missing, gets `Restored`, `Total` 3, `InPlace` 2, `Missing` 1; and with `SnapBy` 1 on the fake gets `Failed`, `InPlace` 0, `NotPlaced` 3. Cheaper substitute: reporting the number of positions sent instead of the number read back, so a restore Windows ignored reads as success.
- [ ] An existing `undo.json` that cannot be read is replaced by the new snapshot (SPEC §14: it is Berth's scratch file). Done when: a test with `{` in `undo.json` restores successfully and finds a valid snapshot afterwards. Cheaper substitute: refusing every restore until the user deletes a file they have never heard of.
- [ ] When the snapshot cannot be written, return `Failed` and move nothing. Done when: a test with a read-only local root asserts `Failed`, the `Path` of the undo file, and no `SetPositions` call. Cheaper substitute: restoring anyway, leaving the user with no way back.
- [ ] Add `tests/Berth.Tests/Core/RestoreTests.cs` with those cases. Done when: all pass, and the missing-icon test fails when matching uses `Name`. Cheaper substitute: one test with identical saved and current sets.
- [ ] Commit: `core: restore operation with its undo snapshot`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*RestoreTests"` passes, and fails when icons are matched by display name.

## 11. Undo operation

`LayoutService.Undo` puts icons back where they were before the last restore (SPEC §15 "Undo").

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Undo()` and `CanUndo`. Done when: a restore followed by an undo returns the fake desktop to its earlier positions.
2. Add the refusals. Done when: a missing or foreign snapshot is refused and nothing moves.

- [ ] Add `Undo()`: read `undo.json`, and when its `Key` equals the current arrangement's key call `SetPositions` with its icons, read back, and count `Total`, `InPlace`, `NotPlaced` and `Missing` as `Restore` does. When `NotPlaced` is 0, delete the file and return `Undone`; otherwise keep the file and return `Failed` with the counts, so the undo can be tried again (SPEC §15 "Undo"). Done when: a test restores, undoes, and finds every icon at its pre-restore position and `undo.json` gone; and a test with `SnapBy` 1 gets `Failed` and `undo.json` still present and unchanged. Cheaper substitute: deleting the snapshot after a failed undo, which throws away the user's only way back.
- [ ] Refuse with `NothingToUndo` when the file is missing, unreadable, has another `SchemaVersion`, or was written for another arrangement key; an unreadable file is left in place. Done when: a test per case asserts the refusal and no `SetPositions` call. Cheaper substitute: applying a snapshot taken on other monitors.
- [ ] Refuse with `AutoArrange` and `DesktopUnavailable` as `Restore` does. Done when: two tests assert them. Cheaper substitute: an undo that Windows silently ignores.
- [ ] Add `bool CanUndo` returning whether an undo would not be refused with `NothingToUndo` (without touching the desktop). Done when: it is `false` before a restore, `true` after, and `false` after an undo. Cheaper substitute: tracking it in window state, which the command line cannot see.
- [ ] Add `tests/Berth.Tests/Core/UndoTests.cs` with those cases. Done when: all pass, and the foreign-key test fails when the key check is removed. Cheaper substitute: testing only restore then undo.
- [ ] Commit: `core: undo operation`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*UndoTests"` passes, and fails when the arrangement key check in `Undo` is removed.

## 12. Rename, revert and delete operations

The three "More" menu operations of SPEC §10, each written and read back before it reports.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Rename`, `Revert` and `Delete` to `LayoutService`. Done when: each changes the file as SPEC §10 says.
2. Add their refusals. Done when: an invalid name, a layout with no previous save and an unknown key are refused.

- [ ] Add `Rename(string key, string name)`: validate against the other layouts' names with `LayoutNames.Validate`, store the trimmed name, return `Renamed`; an invalid name returns `Refused` with `InvalidName` and the `NameError`, and an unknown key returns `Refused` with `NoLayout`. Done when: tests cover success with trimming, each `NameError`, renaming to its own name with different case (allowed), and an unknown key. Cheaper substitute: renaming the file, which is named by key and must not move.
- [ ] Add `Revert(string key)`: swap `Icons` and `SavedAt` with `Previous`, so a second revert returns to the start; return `Reverted` with `SavedAt` set to the save now current (for the SPEC §8 sentence); a layout with `Previous` `null` returns `Refused` with `NoLayout`. Done when: a test reverts twice and the file equals its starting content, and `SetPositions` is never called. Cheaper substitute: dropping the current save instead of swapping, which makes revert one-way.
- [ ] Add `Delete(string key)` removing the file and returning `Deleted`, or `Refused` with `NoLayout` for an unknown key; it never moves an icon. Done when: tests cover both and assert no `SetPositions` call. Cheaper substitute: deleting `undo.json` too, which belongs to the restore.
- [ ] Return `Failed` with `Path` and `Detail` when a write or delete throws. Done when: a test with a read-only layout file asserts `Failed` for rename and the file unchanged. Cheaper substitute: reporting success before the read-back.
- [ ] Add `tests/Berth.Tests/Core/LayoutEditTests.cs` with those cases. Done when: all pass, and the double-revert test fails when revert discards instead of swapping. Cheaper substitute: tests that assert only the returned `Kind`.
- [ ] Commit: `core: rename, revert and delete operations`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutEditTests"` passes, and fails when `Revert` discards the current save instead of swapping.

## 13. Comparison of a layout with the desktop

What the position map and the status line show: each saved icon is in place, moved or missing (SPEC §7 table, §8).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `LayoutComparison` in `src/Berth/Core/LayoutComparison.cs`. Done when: three icons in three states are classified.
2. Add monitor lookup and off-screen detection. Done when: an icon outside every monitor is flagged with its nearest monitor.

- [ ] Add `IconState` (`InPlace`, `Moved`, `Missing`, `Neutral`) and `ComparedIcon(IconPosition Saved, IconState State, IconPosition? Current, int MonitorNumber, bool OffScreen)`, and `LayoutComparison.Compare(Layout layout, IReadOnlyList<IconPosition> desktop)` returning the icons with counts `InPlace`, `Moved`, `Missing`. Done when: a test with one equal, one shifted and one absent icon gets one of each. Cheaper substitute: comparing display names.
- [ ] Add `LayoutComparison.Neutral(Layout layout)` (every icon `Neutral`, for another arrangement's layout and for an unreachable desktop) and `LayoutComparison.Unsaved(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<IconPosition> desktop)` (the current icons, `Neutral`). Done when: tests assert every icon is `Neutral`. Cheaper substitute: showing "in place" for a layout that cannot be compared.
- [ ] Number monitors left to right then top to bottom (sorted by `X` then `Y`, starting at 1) in `MonitorNumbering.Order(monitors)`, and give each icon the number of the monitor whose bounds contain its saved position (SPEC §7). Done when: a test with a monitor at `-1920,0` numbers it 1. Cheaper substitute: numbering by the order Windows returns, which changes between sessions.
- [ ] For a saved position outside every monitor, set `OffScreen` `true` and `MonitorNumber` to the nearest monitor by distance to its rectangle (SPEC §7, last bullet). Done when: a test with an icon at `5000,5000` gets `OffScreen` and the nearest monitor. Cheaper substitute: dropping off-screen icons from the result.
- [ ] Return the icons in reading order: by `Y`, then `X` (SPEC §7 automation order). Done when: a test with shuffled input gets sorted output. Cheaper substitute: file order.
- [ ] Add `tests/Berth.Tests/Core/LayoutComparisonTests.cs`. Done when: all pass, and the moved test fails when equality ignores `Y`. Cheaper substitute: one all-in-place test.
- [ ] Commit: `core: comparison of a layout with the desktop`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutComparisonTests"` passes, and fails when position equality ignores `Y`.

## 14. Operation lock across processes

Save, restore, undo and revert run one at a time across the tray app and the command line (SPEC §15, last bullet).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `OperationLock` in `src/Berth/Core/OperationLock.cs`. Done when: a second holder waits and is refused after the timeout.
2. Use it in `LayoutService`. Done when: a busy lock returns `Refused` with `Busy`.

- [ ] Add `OperationLock(BerthPaths paths, TimeSpan wait)` with one entry point, `Run<T>(Func<T> work, Func<T> busy)`: take the named mutex `Local\Berth.Operation.<first 16 hex of SHA-256 of the data root path, lowercased>`, waiting up to `wait` (5 seconds in production), run `work` and release the mutex on the same thread, or return `busy()` when it was not acquired. `LayoutService` wraps every operation in it (§9 to §12). Done when: a test holds the mutex on a second thread (signalled with events, never with sleeps) and `Run` on the test thread with a 200 ms wait returns `busy` without calling `work`, then calls `work` once the second thread releases. Cheaper substitute: an in-process `lock`, which the command line and tray app do not share; or a test where one thread takes the mutex twice, which a mutex allows.
- [ ] Treat an abandoned mutex (the holder crashed) as acquired. Done when: a test abandons the mutex on a thread that exits and the next `Run` calls `work`. Cheaper substitute: failing forever after one crash.
- [ ] Prove it across processes: a small test helper process (the test assembly started with an argument, with `CreateNoWindow`, a 20 second bound and a tree kill in `finally`) holds the mutex for the same data root until told to release through its standard input, and `Run` in the test process returns `busy` while it holds. Done when: the test passes and no helper process remains. Cheaper substitute: assuming two wrapper objects in one process prove what two processes do.
- [ ] Add `tests/Berth.Tests/Core/OperationLockTests.cs`. Done when: all pass, and the busy test fails when `Run` calls `work` without acquiring. Cheaper substitute: a test that sleeps and hopes.
- [ ] Commit: `core: operation lock across processes`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*OperationLockTests"` passes, and fails when `OperationLock.Run` calls `work` without holding the mutex.

## 15. Log file with a size cap

One line per event, capped at 1 MB with one previous file (SPEC §18).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Log` in `src/Berth/Core/Log.cs`. Done when: lines have the SPEC §18 shape.
2. Add the cap and the calls from `LayoutService`. Done when: the file rolls at 1 MB and every operation writes a line.

- [ ] Add `Log(BerthPaths paths, TimeProvider clock)` with `Info`, `Warn`, `Fail(string message)` and `Fail(string message, Exception error)`, appending `<UTC time ending Z> <LEVEL> <message>` as one line (the exception form adds the exception text on following lines), opening the file shared so two processes can append. Done when: a test reads back `2026-11-15T07:14:03Z INFO hello` with a fixed clock. Cheaper substitute: `Console.WriteLine`, which a tray app has nowhere to show.
- [ ] Before a write that would pass 1,048,576 bytes, rename `berth.log` to `berth.log.1` (replacing it) and start a new file. Done when: a test with the cap lowered through an internal constructor parameter sees the roll and exactly two files. Cheaper substitute: an unbounded log (`AGENTS.md`: a writer has a bound on what it writes).
- [ ] Never throw from a log call: a failed write is swallowed after one retry. Done when: a test with the log folder made read-only shows `Info` returning normally. Cheaper substitute: a logging failure that aborts a restore half way.
- [ ] Add `Log.Result(OperationResult? result, string text)` choosing the level from the result kind (`INFO` for done, `WARN` for `Refused`, `FAIL` for `Failed`), for `LayoutService` to call once per operation (§9 to §12; `OperationResult` arrives in §9, so until then the method takes the level as an enum `LogLevel` and §9 adds the overload). Done when: a test logs one line per level and reads the three levels back. Cheaper substitute: logging in each caller, so the command line and window disagree.
- [ ] Add `tests/Berth.Tests/Core/LogTests.cs`. Done when: all pass, and the roll test fails when the cap check is removed. Cheaper substitute: asserting the file exists.
- [ ] Commit: `core: log file with a size cap`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LogTests"` passes, and fails when the size cap check is removed.

## 16. One wording for every result

Every sentence of SPEC §8, §13 and §18 produced from an `OperationResult` in one class, so the window, the tray and the command line cannot drift.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Messages` in `src/Berth/Core/Messages.cs`. Done when: each SPEC sentence is produced for its result.
2. Test against the SPEC text. Done when: every row of the SPEC §8 result rows, §13 output column and §18 table has a test with the exact string.

- [ ] Add `Messages.Status(OperationResult r, TimeZoneInfo zone)` returning the SPEC §8 sentence for `Restored`, `Undone`, `Saved` (with and without "The previous save is kept."), `Reverted` (from `r.SavedAt`), and for a `Failed` restore or undo with counts the partial sentence ("did not stay", "no longer on the desktop", in that order), with singular forms for 1 icon; and the constant `Messages.Working` (`Working`). Done when: a test per row of the SPEC §8 table and per partial combination (not placed only, missing only, both, one icon) compares the exact SPEC string. Cheaper substitute: building plurals with `(s)`.
- [ ] Add `Messages.Refusal(OperationResult r)` returning the SPEC §18 message for each `Refusal` and for `Failed` (a write failure naming `Path` and `Detail`, otherwise "Something went wrong: ..."), and the three name errors of SPEC §10. Done when: a test per row of the SPEC §18 table compares the exact string. Cheaper substitute: showing exception type names.
- [ ] Add `Messages.Cli(OperationResult r)` returning the SPEC §13 line for `Saved`, `Restored` (full, and with missing icons), `Undone`, and for a `Failed` restore or undo with counts the partial sentence with ` from "<name>"` after the count for a restore. Done when: tests compare `Saved 42 icons to "Docked".`, the missing-icons line and a "did not stay" line exactly. Cheaper substitute: reusing the window's sentence, which SPEC §13 words differently.
- [ ] Append ` (<Origin>)` to the log wording when `Origin` is set (SPEC §16). Done when: a test compares `Restored 3 icons from "2 monitors". (automatic, display change)`. Cheaper substitute: a second log line written by the caller.
- [ ] Add `Messages.Comparison(int moved, int missing)` returning the four SPEC §8 comparison sentences with singular forms, and `Messages.Unsaved` and `Messages.OtherArrangement(DateTimeOffset savedAt, ...)`. Done when: tests cover 0/0, 3/0, 0/2, 3/2, 1/0 and 0/1. Cheaper substitute: one sentence with two numbers for every case.
- [ ] Switch the log lines of §15 to `Messages`. Done when: the save test's log line reads `INFO Saved 3 icons to "2 monitors".`. Cheaper substitute: a second wording for the log.
- [ ] Add `tests/Berth.Tests/Core/MessagesTests.cs`. Done when: all pass, and one fails when a full stop is removed from a SPEC sentence in `Messages`. Cheaper substitute: tests that assert `Contains("Restored")`.
- [ ] Commit: `core: one wording for every result`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*MessagesTests"` passes, and fails when any sentence differs from `design/SPEC.md` by one character.

## 17. Date, count and monitor size formats

The three small formats of SPEC §3 that several surfaces share.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Formats` in `src/Berth/Core/Formats.cs`. Done when: each format matches SPEC §3.
2. Test the boundaries. Done when: yesterday, another year and three monitors are covered.

- [ ] Add `Formats.When(DateTimeOffset value, DateTimeOffset now, TimeZoneInfo zone)` returning `today, 09:14`, `yesterday, 17:02` or `12 November` (24-hour time, the user's zone, English month names from the invariant culture), adding the year for another year (`12 November 2025`). Done when: tests cover each form across a midnight boundary in a non-UTC zone. Cheaper substitute: `ToString("g")`, which follows the machine's culture and breaks the SPEC text.
- [ ] Add `Formats.MonitorSizes(IReadOnlyList<MonitorInfo> monitors)` returning `2560 × 1440`, `2560 × 1440 and 1920 × 1080`, or `A, B and C`, in `MonitorNumbering` order, using the multiplication sign U+00D7. Done when: tests cover one, two and three monitors. Cheaper substitute: a lowercase `x`.
- [ ] Add `Formats.MonitorCount(int n)` (`1 monitor`, `2 monitors`) and `Formats.IconCount(int n)` (`1 icon`, `42 icons`). Done when: tests cover 1 and 2. Cheaper substitute: `monitor(s)`.
- [ ] Add `Formats.ListDate(DateTimeOffset value, TimeZoneInfo zone)` returning `2026-11-15 09:14` for `berth list` (SPEC §13). Done when: a test compares the exact string. Cheaper substitute: the round-trip format with seconds and offset.
- [ ] Add `tests/Berth.Tests/Core/FormatsTests.cs`. Done when: all pass, and the yesterday test fails when the comparison uses UTC dates. Cheaper substitute: tests that run only in the build machine's time zone.
- [ ] Commit: `core: date, count and monitor size formats`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*FormatsTests"` passes, and fails when `Formats.When` compares UTC dates instead of local ones.

## 18. Setting changes applied, read back and logged in one place

Filed 2026-11-18 from the phase 1 gap audit: SPEC §18 says every setting change writes a log line, and both the tray menu (`D04 T01 §8`) and the window (`D05 T01 §11`) change "Restore automatically", so the change, its read-back and its line live in Core once, worded as SPEC §18 now says.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `SettingChanges` in `src/Berth/Core/SettingChanges.cs`. Done when: a change is written, read back and logged.
2. Add the failure paths. Done when: a failed write leaves the file, returns the SPEC §18 message and logs `FAIL`.

- [ ] Add `SettingChange(bool Applied, bool Value, string? Message)` and `SettingChanges(SettingsStore store, Log log)` with `SetRestoreAutomatically(bool on)`: load the settings, save them with `RestoreAutomatically` set to `on` and every other value kept (`Window` included), load again, and return `Applied` `true` with `Value` `on` only when the read-back holds `on`; then log `INFO Restore automatically turned on.` or `INFO Restore automatically turned off.` (SPEC §18). Done when: a test with a fixed clock reads that line back and finds `Window` unchanged in the file. Cheaper substitute: writing the switch without reading it back, which reports a change that did not land.
- [ ] Return `Applied` `false`, `Value` the old state and `Message` the SPEC §18 write message (`Berth could not write <path>: <reason>.` through `Messages`) when the save throws `IOException`, `UnauthorizedAccessException` or `InvalidOperationException` (an unreadable `settings.json`, which `SettingsStore.Save` refuses), and log one `FAIL` line with that message. Done when: tests with a read-only settings file and with `{` in `settings.json` get `Applied` `false`, the message, one `FAIL` line, and the file's bytes unchanged. Cheaper substitute: letting the exception reach the tray, which would leave the check flipped.
- [ ] Add `RecordStartWithWindows(bool on, Exception? failure)`, for the caller that wrote the `Run` value (`D05 T01 §10` owns the registry): with no failure log `INFO Start with Windows turned on.` or `INFO Start with Windows turned off.` and return `Applied` `true`; with one, log `FAIL` with the SPEC §18 message `Berth could not change the Start with Windows setting: <reason>.` and return it with `Applied` `false`. Done when: tests read each line back. Cheaper substitute: logging in the view model, so the tray and window word it differently.
- [ ] Add `Messages.SettingChanged(string setting, bool on)` for the four SPEC §18 lines, used by `SettingChanges`. Done when: `MessagesTests` compares the four strings exactly. Cheaper substitute: building the sentence in `SettingChanges`, outside the one class that words results.
- [ ] Add `tests/Berth.Tests/Core/SettingChangesTests.cs` with those cases. Done when: all pass, and the read-back test fails when `SetRestoreAutomatically` skips the second load. Cheaper substitute: asserting on the returned value only.
- [ ] Commit: `core: setting changes applied, read back and logged`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SettingChangesTests"` passes, and fails when a failed save is reported as applied.

## Verification

- With `FakeDesktopIcons`, a save, a move, a restore and an undo leave the fake desktop where it started, the layout file in the SPEC §14 shape, and one log line per operation.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
