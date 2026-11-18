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

**Adjacency:** list=applicable @ D01 T01 §5; document=not-applicable (a layout is not carried or printed); settings=applicable @ D01 T01 §6; reporting=not-applicable (no summaries beyond the status line); notifications=not-applicable (the balloon is owned by D06 T01 §3); permissions=not-applicable (one user, no roles); audit=applicable @ D01 T01 §15; exchange=not-applicable (SPEC §20 rules out import and sync); reverse=applicable

**Adjacency rationale:** Layouts are the records and `LayoutStore` lists them. The log is the audit trail. Every create has an undo: a restore has `Undo` (§11), a save over an earlier one has `Revert` (§12), and a layout has `Delete` (§12). Corrected 2026-11-18 (D01 T01 §9 completion review): `reverse` names no single owner, since §11 and §12 share it as this paragraph says.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Layout, monitor and icon records | D00 T01 §3 | [x] |
| 2 | §2 | Arrangement key | §1 | [x] |
| 3 | §3 | Data paths that honour BERTH_DATA | D00 T01 §3 | [x] |
| 4 | §4 | Atomic JSON file with read-back | §1, §3 | [x] |
| 5 | §5 | Layout store | §1, §2, §4 | [x] |
| 6 | §6 | Settings store | §4 | [x] |
| 7 | §7 | Layout names: defaults and validation | §1, §9, §12 | [ ] |
| 8 | §8 | Desktop and monitor seams with fakes | §1 | [x] |
| 9 | §9 | Save operation | §5, §8, §14, §15 | [ ] |
| 10 | §10 | Restore operation with its undo snapshot | §9 | [ ] |
| 11 | §11 | Undo operation | §10 | [ ] |
| 12 | §12 | Rename, revert and delete operations | §9 | [ ] |
| 13 | §13 | Comparison of a layout with the desktop | §8 | [x] |
| 14 | §14 | Operation lock across processes | §3 | [x] |
| 15 | §15 | Log file with a size cap | §3 | [x] |
| 16 | §16 | One wording for every result | §9, §10, §11, §12, §17 | [ ] |
| 17 | §17 | Date, count and monitor size formats | §13 | [ ] |
| 18 | §18 | Setting changes applied, read back and logged in one place | §6, §15, §16 | [ ] |
| 19 | §19 | Fake contract tests all go through the seams | §8 | [x] |
| 20 | §20 | The waiting-lock test must see the contender blocked | §14 | [x] |
| 21 | §21 | The reverse adjacency names its owner | §9 | [ ] |

## 1. Layout, monitor and icon records

> **Started:** 2026-11-18T02:16:14Z

The data shapes of SPEC §14, as immutable records with their JSON names, so every later section reads and writes the same thing.

**Phase:** 1
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** rewritten after the plan review (P1 1 and 2, P2 3 and 4): collections are immutable snapshots compared by content; a missing or null required value is a parse failure, never a silent zero; `savedAt` is written as UTC ending `Z` at whole seconds; and the tests compare against independently written values and the expected JSON, not one parse against another.

**Build order:**

1. Add the records in `src/Berth/Core/Model/`. Done when: they compile and the layer rule test still passes.
2. Add the source-generated JSON context. Done when: the SPEC §14 example parses to the expected values and serialises to the expected document.

- [x] Add `MonitorInfo(int X, int Y, int Width, int Height, int Scale, bool Primary)` in `src/Berth/Core/Model/MonitorInfo.cs` (SPEC §14 `monitors`). Done when: it compiles as a `sealed record`. Cheaper substitute: a mutable class whose values can change after a key was computed.
- [x] Add `IconPosition(string Id, string Name, int X, int Y)` in `src/Berth/Core/Model/IconPosition.cs` (SPEC §14 `icons`). Done when: it compiles as a `sealed record`. Cheaper substitute: using the display name as the identifier (SPEC §15 "Matching").
- [x] Add `PreviousSave(DateTimeOffset SavedAt, ImmutableArray<IconPosition> Icons)` and `Layout(int SchemaVersion, string Key, string Name, DateTimeOffset SavedAt, ImmutableArray<MonitorInfo> Monitors, ImmutableArray<IconPosition> Icons, PreviousSave? Previous)` in `src/Berth/Core/Model/Layout.cs`, each with `Equals` and `GetHashCode` that compare the arrays by content. Done when: a test builds a `Layout` from lists it then changes, and the `Layout` still holds the original items; and two layouts with equal content are equal. Cheaper substitute: `IReadOnlyList<T>` over a list the caller can keep changing, or a `previous` list of unlimited depth, which SPEC §14 limits to one.
- [x] Add `BerthJson` in `src/Berth/Core/Model/BerthJson.cs`: a source-generated `JsonSerializerContext` for `Layout` with camelCase names, indented output, `RespectRequiredConstructorParameters` and `RespectNullableAnnotations` on, and a converter that writes every `DateTimeOffset` as UTC `yyyy-MM-ddTHH:mm:ssZ` (whole seconds, SPEC §14's `2026-11-15T07:14:03Z`) and reads any ISO 8601 offset back as UTC. Done when: the tests below pass. Cheaper substitute: reflection-based serialisation, which single-file trimming can break, or the default writer, which emits `+00:00` and keeps the offset.
- [x] Add `tests/Berth.Tests/Core/LayoutJsonTests.cs`: parse the SPEC §14 example and assert each field against values written in the test (`schemaVersion` 1, key `3f9a61c2d0b47e15`, name `Docked`, `savedAt` 2026-11-15 07:14:03 UTC, one monitor `0,0,2560,1440,100,true`, one icon `::{645FF040-5081-101B-9F08-00AA002F954E}`, `Recycle Bin`, 76, 2, `previous` null); do the same for a fixture with a `previous` save holding different positions and times; serialise each and compare the output with the expected document structurally (`JsonNode.DeepEquals`), and check it is indented. Done when: all pass, and fail when `IconPosition.Id` is renamed or its JSON property is dropped. Cheaper substitute: asserting that one parse equals another parse, which agrees on a lost field.
- [x] Add tests that a `savedAt` given with offset `+02:00` in the current and previous save is written back as the UTC `Z` string, and that a missing or null `id`, `name`, `x`, `y`, `width`, `height`, `scale` or `primary`, in `monitors`, `icons` or `previous.icons`, fails to parse with a `JsonException`, while a valid icon at `0,0` parses. Done when: all pass. Cheaper substitute: letting a missing `x` read as 0, which would restore an icon to the wrong place.
- [x] Commit: `core: layout, monitor and icon records`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutJsonTests"` passes, and fails when `IconPosition.Id` is renamed or a missing `x` parses as 0.

> **Verified:** 2026-11-18 | §1 | 6f95d2903da89be139d7d2416138fb379065f619
> **Review:** `docs/reviews/d01-t01-s1.json`

## 2. Arrangement key

> **Started:** 2026-11-18T02:41:01Z

The same monitors in the same places at the same scale must always map to the same layout file (SPEC §14, last bullet).

**Phase:** 1
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** rewritten after the plan review (P1 1, P2 2 and 3, P3 4): exact keys for one and for three monitors against hand-written strings, invariant formatting under two cultures, and the SPEC §14 example made self-consistent. That example's `key` was `3f9a61c2d0b47e15`, which is not the key of its monitor; SPEC §14 now gives `c7f017905ce518e6`, and `D01 T01 §1`'s stamped checklist, which quotes the old value, is superseded on that one literal by this section.

**Build order:**

1. Add `ArrangementKey.From` in `src/Berth/Core/ArrangementKey.cs`. Done when: known inputs give known 16 character keys.
2. Test order independence, sensitivity and culture. Done when: reordering monitors keeps the key, changing `X`, `Y`, `Width`, `Height` or `Scale` changes it, changing `Primary` does not, and the culture does not.

- [x] Add `ArrangementKey.From(IReadOnlyList<MonitorInfo> monitors)` that sorts by `X` then `Y` (then `Width`, `Height` and `Scale`, so the order is total), writes each as `x,y,width,height,scale` with invariant-culture decimal integers, joins with `;`, hashes the UTF-8 bytes with SHA-256 and returns the first 16 lowercase hexadecimal characters (SPEC §14). Done when: one monitor `0,0,2560,1440,100` gives `c7f017905ce518e6`, and the three monitors `0,0,2560,1440,150`, `0,1440,1920,1080,125` and `-1920,-200,1920,1080,100`, in any order, give the SHA-256 of the hand-written string `-1920,-200,1920,1080,100;0,0,2560,1440,150;0,1440,1920,1080,125`, which is `2681c996402f364e`. Cheaper substitute: `GetHashCode`, which differs between runs; or the current culture, whose negative sign can differ (U+2212 in `sv-SE`) and lose the layout of a monitor to the left.
- [x] Refuse an empty list with `ArgumentException`. Done when: a test asserts the exception. Cheaper substitute: returning the hash of an empty string, which would make "no monitors" a real layout.
- [x] Add `tests/Berth.Tests/Core/ArrangementKeyTests.cs` covering: the two exact keys above with the three-monitor input in all six orders; a different key when `X`, `Y`, `Width`, `Height` or `Scale` of one monitor changes by one; the same key when only `Primary` changes; the same three-monitor key with the current culture set to `en-US` and to `sv-SE` on the test's own thread (restored in `finally`); 16 characters of `[0-9a-f]`. Done when: all pass, and the three-monitor test fails when the sort is removed, reversed, or the separator becomes `,`. Cheaper substitute: one happy-path assertion.
- [x] Assert in `ArrangementKeyTests` that `ArrangementKey.From` of the SPEC §14 example's monitors equals its `key`, and update the SPEC §14 example text in `tests/Berth.Tests/Core/LayoutJsonTests.cs` (and its literal `Key` assertion) to `c7f017905ce518e6`, as the corrected SPEC reads. Done when: both test classes pass on the same example. Cheaper substitute: leaving the two tests to disagree about which file a layout lives in.
- [x] Commit: `core: arrangement key`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*ArrangementKeyTests"` passes, and fails when the sort in `ArrangementKey.From` is removed or reversed.

> **Verified:** 2026-11-18 | §2 | 37b9f3c14246b8c72a92d84c30412cfe21eb9d1c
> **Review:** `docs/reviews/d01-t01-s2.json`

## 3. Data paths that honour BERTH_DATA

> **Started:** 2026-11-18T02:49:46Z

One place decides where Berth's files live, and tests redirect it (SPEC §14, first bullet).

**Phase:** 1
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** rewritten after the plan review (P1 1 to 3, P2 4): exact paths for both roots under all three constructions, one condition for the override and `TestMode`, four more key refusals, and the folder of each file created by its own writer (`LayoutStore.Save` in §5, `SettingsStore.Save` in §6, the undo snapshot in §10 and `Log` in §15) instead of by a caller that could forget, so a first save on a fresh profile finds its folder.

**Build order:**

1. Add `BerthPaths` in `src/Berth/Core/BerthPaths.cs`. Done when: it returns the SPEC §14 paths.
2. Test the override, the defaults and the key rule. Done when: every path is exact in each case.

- [x] Add `BerthPaths` with `DataRoot`, `LocalRoot`, `LayoutsFolder` (`<DataRoot>\layouts`), `LayoutFile(string key)` (`<LayoutsFolder>\<key>.json`), `SettingsFile` (`<DataRoot>\settings.json`), `UndoFile` (`<LocalRoot>\undo.json`), `LogFolder` (`<LocalRoot>\logs`) and `LogFile` (`<LogFolder>\berth.log`), built by `BerthPaths.ForRoot(string root)` (`<root>\roaming` and `<root>\local`, the root made absolute) and by `BerthPaths.FromEnvironment(Func<string, string?>? getenv = null, Func<Environment.SpecialFolder, string>? folder = null)`: `%APPDATA%\Berth` and `%LOCALAPPDATA%\Berth` from `Environment.SpecialFolder.ApplicationData` and `LocalApplicationData`, refusing with `InvalidOperationException` a special folder that is not an absolute path (**Corrected 2026-11-18:** an unavailable redirected profile gives `""`, which would make the roots relative to the working folder, round 1 P1 3), or `ForRoot(<BERTH_DATA>)` when the variable is set to a value that is not empty or white space (SPEC §14). `getenv` defaults to `Environment.GetEnvironmentVariable`, and tests pass their own function instead of changing the process environment. Done when: tests assert all eight paths exactly for `ForRoot`, for `FromEnvironment` with an override, and for `FromEnvironment` without one (built from the two special folders, writing nothing). Cheaper substitute: paths built in each class that needs one, or a prefix check that passes with undo and logs under the roaming root.
- [x] Add `bool TestMode`, true exactly when `FromEnvironment` took the override, false for `ForRoot` and for the defaults, so the root and the SPEC §14 test switches follow one condition. Done when: tests with `BERTH_DATA` absent, empty and white space get the default roots and `TestMode` `false`, and with a folder get the redirected roots and `TestMode` `true`. Cheaper substitute: `TestMode` true whenever the variable exists, so an empty value enables test switches over the user's real data.
- [x] Make `LayoutFile` refuse a key that is not exactly 16 characters of `[0-9a-f]` with `ArgumentException`. Done when: tests refuse `..\x`, 15 and 17 characters, 16 uppercase hex characters, and 16 characters with a `g`, and two valid keys give two different exact paths. Cheaper substitute: accepting any string, which lets a crafted key escape the layouts folder (`SECURITY.md`).
- [x] Add `static EnsureFolderFor(string file)` that creates the folder holding `file` (with its parents) and throws the system's `IOException` or `UnauthorizedAccessException` when it cannot; each writer in §5, §6, §10 and §15 calls it for its own destination only, so a broken log folder never stops a layout save. Done when: a test calls it twice for a layout file on an empty temporary root and only the layouts folder exists, and with a file where `logs` should be, the layouts folder is still made and the log folder throws. **Corrected 2026-11-18:** one `EnsureFolders()` for both made a save depend on the log folder (round 1 P2 2). Cheaper substitute: creating folders at each write site, or once at startup, which a first `berth save` on a fresh profile could miss.
- [x] Add `tests/Berth.Tests/Core/BerthPathsTests.cs` with those cases. Done when: all pass, and the override test fails when `FromEnvironment` ignores the variable. Cheaper substitute: a test that writes into the real `%APPDATA%`.
- [x] Commit: `core: data paths that honour BERTH_DATA`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*BerthPathsTests"` passes, and fails when `FromEnvironment` ignores `BERTH_DATA` or `TestMode` is always `true`.

> **Verified:** 2026-11-18 | §3 | 99e168fa42ac9dfc6190724d637f4bb911d9feaa
> **Review:** `docs/reviews/d01-t01-s3.json`

## 4. Atomic JSON file with read-back

> **Started:** 2026-11-18T03:00:11Z

Every JSON file Berth writes goes through one routine, in the SPEC §14 "Every write" order: write a temporary file in the same folder, flush it, read it back and compare, and only then replace the target. **Corrected 2026-11-18:** said "replace, read back, compare" and "every write"; the log of §15 appends to a text file and is not one of these (plan review P3 5).

**Phase:** 1
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** checklist rewritten after the plan review (P1 1 and 2, P2 3 and 4, P3 6): the verification seam corrupts the flushed temporary file so production's own read-back must catch it; reading tells a missing file from a locked or forbidden one; a replace that fails after verification exercises the cleanup; and reading goes through `BerthJson.Parse`, so `TryRead` takes `where T : class` and `§1` joins the dependencies.

**Build order:**

1. Add `JsonFile` in `src/Berth/Core/JsonFile.cs`. Done when: a write is readable and equal.
2. Prove every failure leaves the old file. Done when: the corruption, replace-failure and serialisation-failure tests pass.

- [x] Add `JsonFile.Write<T>(string path, T value, JsonTypeInfo<T> info)`: serialise to bytes, write them to `<path>.tmp-<guid>` in the same folder, `Flush(true)`, close, read the temporary file back from disk and compare it byte for byte with what was serialised (throwing `IOException` naming `path` when it differs), and only then put it in place: `File.Replace` (ReplaceFileW, which keeps the target's security descriptor) over an existing target, `File.Move` when there is none (SPEC §14 "Every write"). An internal overload takes a per-call `Action<string>? afterFlush` (null in production) run on the flushed temporary file before the read-back; a static seam would let parallel tests corrupt each other's writes. Before writing, temporary files of the same target older than one minute are deleted, as left by a writer that was ended between its flush and its replace. **Corrected 2026-11-18:** round 1 P1 3 (permissions), P2 2 (orphans) and P3 4 (the seam's real shape). Done when: a test writes a value, reads the file and finds equal content and no `.tmp-` file; and a test whose seam flips one byte of the temporary file gets `IOException` naming the target, finds the earlier target byte for byte unchanged and no `.tmp-` file. Cheaper substitute: replacing first and verifying after, which cannot give the old file back, or a seam that throws instead of corrupting, which passes when the read-back is skipped.
- [x] Delete the temporary file in a `finally` when the write did not complete. Done when: a test that holds the existing target open with `FileShare.None`, so the final replace fails after verification, gets the system's `IOException` or `UnauthorizedAccessException` (Windows reports a target it cannot replace as denied access, and can rename over a file another handle only reads), finds the target's bytes unchanged and no `.tmp-` file; and a test whose value cannot be serialised finds no `.tmp-` file. Cheaper substitute: leaving temporary files behind for the next run to trip on.
- [x] Add `JsonFile.TryRead<T>(string path, JsonTypeInfo<T> info, out T? value, out string? reason) where T : class`, opening the file directly, refusing one larger than `JsonFile.MaxBytes` (16 MB) before allocating for it (**Corrected 2026-11-18:** round 1 P1 1), and parsing through `BerthJson.Parse` (`D01 T01 §1`, which refuses a `null` document and null elements): `false` with `reason` `missing` when the file or its folder does not exist; `false` with the system's message for a sharing violation or denied access, never `missing`; `false` with the parser's message for invalid JSON; it never deletes or rewrites the file (SPEC §14 "never overwritten or deleted"). Done when: tests cover a good file, a missing file, a missing folder (a fresh data root), a file held with `FileShare.None`, a file holding `{`, a file holding `null`, and a layout holding a null icon, each asserting the returned `value` and `reason`, and the bad files' bytes are unchanged afterwards. Cheaper substitute: catching the exception and returning a default value, which hides a corrupt layout, or reporting a locked file as missing, which a save would then overwrite.
- [x] Add `tests/Berth.Tests/Core/JsonFileTests.cs` with those cases plus: writing over an existing file replaces it; a write into a folder that does not exist throws and creates nothing. Done when: all pass, and the replace-failure test fails when the `finally` is removed and the corruption test fails when the read-back is skipped. Cheaper substitute: testing only the happy path.
- [x] Commit: `core: atomic JSON file with read-back`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*JsonFileTests"` passes, and fails when `JsonFile.Write` writes directly to the target path or skips the read-back.

> **Verified:** 2026-11-18 | §4 | cc6ff6cfd7a72629a0575b9ce16a5dd3c9b9c817
> **Review:** `docs/reviews/d01-t01-s4.json`

## 5. Layout store

> **Started:** 2026-11-18T03:26:44Z

Loads every layout, saves one, deletes one, and reports files it cannot read without touching them (SPEC §14, §9 third notice).

**Phase:** 1
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** rewritten after the plan review (P1 1 to 4, P2 5 to 7): one rule set (`LayoutRules`) decides what a readable layout is, and loading, finding, saving and deleting all apply it; a layout must carry the key of its own monitors; persistence is proven with a fresh store; and the empty profile, an unlistable folder, several bad files, `Find` and the full set of value checks are specified.

**Build order:**

1. Add `LayoutRules` and `LayoutStore` in `src/Berth/Core/`. Done when: save then load in a fresh store returns the layout.
2. Add the refusals. Done when: each bad file is reported, kept out of `Find`, never overwritten or deleted, and left byte for byte.

- [x] Add `LayoutRules.Check(Layout layout, string fileKey)` in `src/Berth/Core/LayoutRules.cs`, returning `null` for a readable layout or the reason it is not: `schemaVersion` not 1; `key` not equal to `fileKey`; `key` not `ArrangementKey.From(monitors)` (SPEC §14: the file belongs to that arrangement); `name` empty, white space only, with leading or trailing white space, or over `LayoutRules.MaxNameLength` (40, SPEC §10); `monitors` empty, or a monitor whose `width`, `height` or `scale` is not positive; an icon whose `id` is empty or white space; two icons with the same `id` ignoring case in `icons`, or in `previous.icons` (the same `id` once in each is fine). Done when: `LayoutRulesTests` has a case per rule and a valid layout gets `null`. Cheaper substitute: checking only the file name, so coordinates saved for other monitors are restored.
- [x] Add `LayoutStore(BerthPaths paths)` with `Save(Layout layout)`: refuse a layout that `LayoutRules.Check` rejects with `ArgumentException` (nothing Berth's next read would reject may replace a good file); read the destination with `JsonFile.TryRead` and, when it exists and is unreadable (parse failure, sharing violation, denied access or a rule broken), refuse with `UnreadableLayoutException` carrying its file name and reason (SPEC §14: never overwritten); otherwise call `BerthPaths.EnsureFolderFor` for `LayoutFile(layout.Key)` and write it through `JsonFile.Write` (**Corrected 2026-11-18:** the writer creates its folder, `D01 T01 §3`). Done when: tests put `{`, `schemaVersion` 2, a mismatched key, monitors that do not give the key, and a file held with `FileShare.None` at the destination, call `Save`, get the exception naming the file, and find its bytes unchanged; and an invalid incoming layout is refused with the destination untouched. Cheaper substitute: naming the file after the layout's display name, which a rename would orphan, or skipping a bad file on load but overwriting it on the next save.
- [x] Add `LoadAll()` returning `LayoutSet(IReadOnlyList<Layout> Layouts, IReadOnlyList<UnreadableFile> Unreadable)`, where `UnreadableFile(string FileName, string Reason)`, ordered by key and file name: a missing layouts folder gives two empty lists (a fresh profile, `berth list` before any save); a folder that exists but cannot be listed throws the system's `IOException` or `UnauthorizedAccessException`; every `.json` file is read with `JsonFile.TryRead` and checked with `LayoutRules.Check` against its file name. Done when: tests cover the fresh profile, an unlistable folder (a file where the folder should be), and two good files with two bad ones, asserting both bad file names and non-empty reasons. Cheaper substitute: throwing on the first bad file, which would hide every good layout, or reporting only the first.
- [x] Ignore files in the layouts folder that do not end in `.json`, ignoring case (leftover temporary files included). Done when: a test with `x.json.tmp-1` and `notes.txt` present gets neither a layout nor an unreadable entry for them. Cheaper substitute: reporting temporary files as corrupt layouts.
- [x] Add `Find(string key)`: the layout when its file is readable, `null` when there is none, and `UnreadableLayoutException` (file name and reason) when its file is unreadable, so a restore never uses it. Done when: tests with two good layouts find each by key, get `null` for a third key, and get the exception for an unreadable one that `LoadAll` still reports. Cheaper substitute: returning `null` for an unreadable file, which a later save would take as "no layout yet".
- [x] Add `Delete(string key)`: re-read and check the file at deletion time; `false` when it does not exist, `UnreadableLayoutException` when it is unreadable (its bytes kept, SPEC §14), and otherwise delete it and return `true`. Done when: tests cover a missing file, a layout listed successfully whose file then became `{` before the delete (the exception names it and its bytes survive), and a good delete after which a fresh store's `LoadAll` holds only the other layout. Cheaper substitute: deleting whatever file has the key, which removes a file Berth must never touch.
- [x] Add `tests/Berth.Tests/Core/LayoutStoreTests.cs` with those cases plus persistence through a fresh store: save two distinct layouts, replace one with changed positions and a filled `previous`, open a new `LayoutStore` on the same paths, and compare both complete records with values written in the test. Done when: all pass, and the key-mismatch and monitors-key tests fail when those checks are removed. Cheaper substitute: tests that share one folder and depend on order, or a round trip through one instance that a cache could satisfy.
- [x] Commit: `core: layout store`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutStore*"` passes, and fails when the key and file name check or the monitors key check is removed.

> **Verified:** 2026-11-18 | §5 | 17eb59545e781638727ea7afbef512942486e2f0
> **Review:** `docs/reviews/d01-t01-s5.json`

## 6. Settings store

> **Started:** 2026-11-18T03:55:09Z

`settings.json` holds the automatic restore switch and the window placement (SPEC §14).

**Phase:** 1
**Needs:** none
**Surface:** no

**Corrected 2026-11-18:** rewritten after the plan review (P1 1 to 3, P2 4): an unreadable `settings.json` has its own SPEC §18 message and exception (reported at startup by `D04 T01 §1`, refused by `D01 T01 §18`, worded by `D01 T01 §16`), `Exists` is true for any file that is there so only a missing file is a first run, and persistence is proven against the SPEC §14 JSON through a fresh store.

**Build order:**

1. Add `Settings` and `SettingsStore` in `src/Berth/Core/SettingsStore.cs`. Done when: defaults load from a missing file.
2. Add save with read-back and the refusals. Done when: a saved value loads again in a fresh store and an unreadable file is never replaced.

- [x] Add `WindowPlacement(int X, int Y, int Width, int Height)` and `Settings(int SchemaVersion, bool RestoreAutomatically, WindowPlacement? Window)` with `Settings.Default` (`1`, `false`, `null`), and register `Settings` in `BerthJson` (required values respected, as for layouts). Done when: serialising `Settings.Default` gives the SPEC §14 shape with `"window": null`. Cheaper substitute: storing "Start with Windows" here, which SPEC §11 says the registry owns.
- [x] Add `SettingsStore(BerthPaths paths)` with `Load()` returning `SettingsLoad(Settings Value, bool Exists, string? Unreadable)` read through `JsonFile.TryRead` (bounded, I/O errors reported): a missing file or folder gives defaults, `Exists` `false` and no reason; a file that is there but cannot be read (invalid JSON, `schemaVersion` not 1, a missing or wrongly typed `restoreAutomatically` or `window`, a placement missing a value or with a width or height not positive, a sharing violation) gives defaults, `Exists` `true` and the reason, and is left untouched; a good file gives its values, `Exists` `true` and no reason. Done when: tests assert all three values for missing, good, `{`, `schemaVersion` 2, no `restoreAutomatically`, `"restoreAutomatically": "yes"`, a `window` without `x`, a zero width, and a file held with `FileShare.None`, and each bad file's bytes are unchanged. Cheaper substitute: `Exists` `false` for a corrupt file, which would open the first-run window and try to save defaults over it.
- [x] Add `SettingsStore.Save(Settings value)`: refuse with `UnreadableSettingsException` (carrying `settings.json` and the reason, distinct from a write failure) when the file on disk is there but unreadable; otherwise call `BerthPaths.EnsureFolderFor(paths.SettingsFile)` and write through `JsonFile.Write` (**Corrected 2026-11-18:** the writer creates its folder, `D01 T01 §3`). Done when: a save over each bad fixture above throws that exception and leaves the bytes unchanged, and a write failure (a read-only `settings.json`) throws the system's exception instead. Cheaper substitute: a save that silently replaces a file the user may want to repair, or one exception for both cases, which would word an unreadable file as a write failure.
- [x] Add `tests/Berth.Tests/Core/SettingsStoreTests.cs` with those cases plus persistence on a fresh temporary root: save `RestoreAutomatically` `true` with `Window` `-1600, 120, 1100, 700`, compare `settings.json` structurally with the SPEC §14 JSON written in the test, load it through a new `SettingsStore` and assert every value; then save `false` and `null` over it and load again. Done when: all pass, and the missing-file test fails when `Exists` is always `true` (`D04 T01 §1` relies on it for the first run). Cheaper substitute: testing through the window, or a round trip through one instance.
- [x] Commit: `core: settings store`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SettingsStoreTests"` passes, and fails when `Load` reports `Exists` `true` for a missing file or `false` for a corrupt one.

> **Verified:** 2026-11-18 | §6 | c18ef06e65e166b6046ee3cfe5aeec966fdea0e0
> **Review:** `docs/reviews/d01-t01-s6.json`

## 7. Layout names: defaults and validation

> **Started:** 2026-11-18T05:56:00Z

The default name of a first save and the three naming rules of SPEC §10, in one place for the dialog and `--name`.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `LayoutNames` in `src/Berth/Core/LayoutNames.cs`. Done when: defaults and validation match SPEC §10 and §15.
2. Test each rule. Done when: each error is returned for its input.

- [x] Add `LayoutNames.Default(int monitorCount, IEnumerable<string> taken)` returning `1 monitor` or `N monitors`, then ` (2)`, ` (3)` and so on until the name is not in `taken` ignoring case (SPEC §15 "Save"). Done when: tests give `1 monitor` for one display and `3 monitors` for three; with `2 MONITORS` and `2 Monitors (3)` taken, exactly `2 monitors (2)`; with `(2)` taken as well, `2 monitors (4)` (**Corrected 2026-11-18:** plan review, so case, skipped suffixes and a stop at `(3)` are caught). Cheaper substitute: a timestamp name, which SPEC does not allow.
- [x] Add `LayoutNames.Validate(string name, IEnumerable<string> others)` returning `NameError.None`, `Empty`, `TooLong` or `Taken`: the name is trimmed first, 1 to `LayoutRules.MaxNameLength` characters counted as the store counts them (UTF-16 `string.Length`), and unique among `others` (trimmed) ignoring case (SPEC §10). Done when: tests cover a blank name, whitespace alone (`Empty`), 41 characters, 40 characters, a 40-character name surrounded by spaces (`None`), an emoji name at 40 units (`None`) and at 41 (`TooLong`), a case-only clash, and `" Docked "` against `DOCKED` (`Taken`) (**Corrected 2026-11-18:** plan review, so trimming before the checks and the store's own limit are proved). Cheaper substitute: comparing names case-sensitively.
- [x] Add `LayoutNames.Trimmed(string name)` used by every caller before storing. Done when: a `LayoutNamesTests` case asserts `Trimmed("  Docked ")` is exactly `Docked`, and a test shows `"  Docked "` is stored as `Docked` through `LayoutService.Rename` (asserted in §12) and through `Save` (asserted in §9) (**Corrected 2026-11-18:** plan review). Cheaper substitute: trimming in the dialog only, so `--name` stores spaces.
> **Deferred:** the stored read-back of a trimmed name through `Save` (first and later save) -> XREF: D01 T01 §9
> **Deferred:** the stored read-back of a trimmed name through `Rename` -> XREF: D01 T01 §12
- [x] Add `tests/Berth.Tests/Core/LayoutNamesTests.cs`. Done when: all pass, and the clash test fails when comparison becomes case-sensitive. Cheaper substitute: one test of the default name.
- [ ] Commit: `core: layout names`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutNamesTests"` passes, and fails when name comparison is made case-sensitive.

## 8. Desktop and monitor seams with fakes

> **Started:** 2026-11-18T06:12:10Z

The two interfaces that separate the rules from Windows, and the fakes every rule test uses (foundation plan, "Seams").

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add the interfaces in `src/Berth/Core/`. Done when: they compile with no Windows type in their signatures.
2. Add the fakes in the test project. Done when: a fake round-trips positions.

- [x] Add `IDesktopIcons` in `src/Berth/Core/IDesktopIcons.cs` with `IReadOnlyList<IconPosition> ReadIcons()`, `void SetPositions(IReadOnlyList<IconPosition> icons)`, `bool AutoArrange { get; }` and `bool TryTurnAutoArrangeOff()`, each documented to throw `DesktopUnavailableException` when the desktop cannot be reached. Done when: the layer rule test passes. Cheaper substitute: exposing a COM interface type in the signature.
- [x] Add `DesktopUnavailableException` in `src/Berth/Core/DesktopUnavailableException.cs`. Done when: a test shows it keeps a supplied inner exception (**Corrected 2026-11-18:** plan review). Cheaper substitute: letting `COMException` cross into Core.
- [x] Add `IMonitors` in `src/Berth/Core/IMonitors.cs` with `IReadOnlyList<MonitorInfo> Read()`, documented to throw `DesktopUnavailableException` when no monitor can be read (a disconnected session), never to return an empty list. Done when: it compiles (**Corrected 2026-11-18:** plan review). Cheaper substitute: reading `System.Windows.Forms.Screen` inside Core.
- [x] Add `FakeDesktopIcons` in `tests/Berth.Tests/Fakes/FakeDesktopIcons.cs`: holds a list of icons, `SetPositions` moves only icons whose `Id` exists (ignoring case) and ignores the rest, `AutoArrange` is settable, `KeepsAutoArrange` makes `TryTurnAutoArrangeOff` return `false` and leave it on, `Unavailable` makes every member throw `DesktopUnavailableException`, `SnapBy` (default 0) with `SnapIds` (null for every icon) shifts the set positions of the chosen icons to imitate Windows adjusting them, `Calls` records every `SetPositions` call and its requested icons before any move, and a move changes only the live icon's coordinates, matched by `Id` ignoring case, never by name. Done when: tests cover a set and fresh read, an unknown `Id` ignored (also one sharing a live icon's name), a differently cased `Id` with a stale name moving only that icon among two of the same name, both auto-arrange outcomes, `SnapBy` for no icon, one of three and all three with exact coordinates, a no-effect call recorded, and every member throwing when `Unavailable` with positions and auto arrange left intact (**Corrected 2026-11-18:** plan review). Cheaper substitute: a fake that accepts any position for any `Id`, hiding the missing-icon rule.
- [x] Add `FakeMonitors` in `tests/Berth.Tests/Fakes/FakeMonitors.cs` with a settable list and `Unavailable` (throws `DesktopUnavailableException`). Done when: a test reads back what it set, and another sees the exception while the desktop fake is still available (**Corrected 2026-11-18:** plan review). Cheaper substitute: none needed.
- [x] Add `tests/Berth.Tests/Fakes/FakeDesktopIconsTests.cs` for those behaviours. Done when: all pass, and the unknown-`Id` test fails when the fake starts adding unknown icons. Cheaper substitute: untested fakes whose own bugs pass as product behaviour.
- [x] Commit: `core: desktop and monitor seams with fakes`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*FakeDesktopIconsTests"` passes (the desktop, monitor and unavailable contract tests are in it; **Corrected 2026-11-18:** plan review), and `LayerRuleTests` still passes with the new interfaces in `Berth.Core`.

> **Verified:** 2026-11-18 | §8 | 121bc106552d65eff61aa6d47e5982788763a2b7
> **Review:** `docs/reviews/d01-t01-s8.json`

## 9. Save operation

> **Started:** 2026-11-18T07:40:03Z

`LayoutService.Save` reads the desktop and the monitors and writes the current arrangement's layout (SPEC §15 "Save").

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `LayoutService` and `OperationResult` in `src/Berth/Core/`. Done when: a first save creates a named layout.
2. Add the second-save and refusal paths. Done when: `previous` is filled and an unreachable desktop is a refusal.

- [x] Add `OperationResult` in `src/Berth/Core/OperationResult.cs` with `Kind` (`Saved`, `Restored`, `Undone`, `Reverted`, `Renamed`, `Deleted`, `Refused`, `Failed`), `LayoutName`, `Total`, `InPlace`, `NotPlaced` (on the desktop but not at the wanted position after read-back), `Missing`, `ReplacedEarlierSave`, `SavedAt` (the save a revert returned to), `Origin` (`null`, or text such as `automatic, display change`), `Refusal` (`None`, `AutoArrange`, `NoLayout`, `DesktopUnavailable`, `NothingToUndo`, `Busy`, `InvalidName`, `UnreadableLayout`), `NameError`, `Path` and `Detail`. A restore or undo with `NotPlaced` above 0 has `Kind` `Failed`, keeping its counts (SPEC §8, §13, §16). Done when: it compiles as a `sealed record` with static factory methods for each kind. Cheaper substitute: returning strings, which the window, tray and command line would each reword. **Corrected 2026-11-18:** D01 T01 §9 plan review: `OperationResultTests` asserts each factory's kind and payload, including a restore and an undo with `NotPlaced` above 0 giving `Failed` with every count kept.
- [x] Add `LayoutService(IDesktopIcons desktop, IMonitors monitors, LayoutStore store, BerthPaths paths, OperationLock gate, Log log, TimeProvider clock)` in `src/Berth/Core/LayoutService.cs` with `Save(string? name = null)`. A first save of an arrangement builds a `Layout` with `ArrangementKey.From`, `LayoutNames.Default` (or the validated `name`), the icons and `clock` time, and `Previous` `null`. Done when: a test with two fake monitors and three icons, on an empty temporary root whose `layouts` and `logs` folders it does not create, gets `Saved`, `Total` 3, name `2 monitors`, the file on disk and its log line (**Corrected 2026-11-18:** the fresh-profile first save, `D01 T01 §3`). Cheaper substitute: saving without reading the monitors, so every arrangement shares one file. **Corrected 2026-11-18:** D01 T01 §9 plan review: the layout read back through a fresh store matches the inputs field by field (key, monitors, icon ids, names and coordinates, `SavedAt` from the clock in whole UTC seconds, `Previous` null) and `ReplacedEarlierSave` is false; a second arrangement with the same monitor count gets `2 monitors (2)` and both files stay readable; an empty desktop saves zero icons.
- [x] On a later save of the same arrangement keep the name (or apply a validated `name`), move the earlier `Icons` and `SavedAt` into `Previous`, and set `ReplacedEarlierSave` `true`. Done when: a test saves twice with different positions and finds the first positions in `Previous`. Cheaper substitute: overwriting with no `Previous`, which loses the undo of a save. **Corrected 2026-11-18:** D01 T01 §9 plan review: with distinct clock times and icon snapshots: the second save holds the new positions and time with the first in `Previous`, a third holds exactly the second in `Previous`, a custom name survives an unnamed save, and `ReplacedEarlierSave` is true. The write is made over the version the save read (`LayoutStore.Snapshot` and `Save(layout, snapshot)`), so a valid edit or a newly arrived file between the read and the write survives byte for byte and the save is `Failed`.
- [x] Return `Refused` with `DesktopUnavailable` when `ReadIcons` or `IMonitors.Read` throws `DesktopUnavailableException`, `Refused` with `InvalidName` and the `NameError` when `name` fails `LayoutNames.Validate`, `Refused` with `UnreadableLayout` and the file name in `Path` when the arrangement's existing file is unreadable (`UnreadableLayoutException`), and `Failed` with `Path` and `Detail` when the write throws `IOException` or `UnauthorizedAccessException`. Done when: a test per case asserts the result and that no layout file was created or changed. Cheaper substitute: letting the exception escape to each caller. **Corrected 2026-11-18:** D01 T01 §9 plan review: also `Failed` with the layouts folder in `Path` when reading the layouts throws `IOException` or `UnauthorizedAccessException`, and `Failed` with the exception's message in `Detail` (SPEC §18 "Something went wrong") for any other exception, logged as one `FAIL` event with the exception and its stack; `InvalidName` with `NameError.Taken` for another arrangement's name in another case, both files unchanged, while reusing the layout's own name succeeds.
- [x] Run the whole operation inside `OperationLock.Run`, returning `Refused` with `Busy` when the lock is not acquired, and write one log line per result through `Log` (kind, counts and layout name until §16 supplies the wording). Done when: a test holding the lock from a second `OperationLock` gets `Busy` and no file change, and a successful save leaves exactly one `INFO` line. **Corrected 2026-11-18:** D01 T01 §9 plan review: the holder is another thread, seen to own the lock before `Save`, and the busy save reads neither the desktop nor the monitors and logs exactly one `WARN`. Cheaper substitute: locking and logging in the callers, so the command line and the tray app can interleave.
- [x] Save while auto arrange is on (SPEC §9: "Save current layout still works"). Done when: a test with `AutoArrange` `true` gets `Saved`. Cheaper substitute: refusing saves under auto arrange.
- [x] Store a given `name` trimmed through `LayoutNames.Trimmed`, on a first save and on a later save (**Corrected 2026-11-18:** D01 T01 §7 plan review). Done when: tests `Save("  Docked ")` for a new arrangement and read `Docked` back, then `Save("  Office ")` for the same arrangement and read `Office` back (SPEC §13 `save --name`, SPEC §10 naming). Cheaper substitute: asserting the rename path only, which never sees `--name`.
- [x] Add `tests/Berth.Tests/Core/SaveTests.cs` with those cases. Done when: all pass, and the second-save test fails when `Previous` is not written. Cheaper substitute: asserting on the in-memory result only, never the file.
- [x] Add `Log.Result(OperationResult result, string text)` choosing the level from the result kind (`INFO` for done, `WARN` for `Refused`, `FAIL` for `Failed`), and log exactly one event per save (**Corrected 2026-11-18:** D01 T01 §15 plan review). Done when: tests call `Save` for a saved, a refused and a failed result and read back exactly one line each with its level and message. Cheaper substitute: logging in each caller, so the command line and window disagree.
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
- [ ] Before moving anything, write `UndoSnapshot(int SchemaVersion, string Key, DateTimeOffset TakenAt, IReadOnlyList<IconPosition> Icons)` with the positions read now to `BerthPaths.UndoFile` through `JsonFile.Write` (registered in `BerthJson`), after `BerthPaths.EnsureFolderFor(paths.UndoFile)` creates the local root (**Corrected 2026-11-18:** `D01 T01 §3`). Done when: a test finds the pre-restore positions in `undo.json` after a restore. Cheaper substitute: keeping the snapshot in memory, so `berth undo` in a new process has nothing.
- [ ] Call `SetPositions` with every saved icon whose `Id` is on the desktop now, matched ignoring case, and with no icon that is not in the layout. Done when: a test with a saved icon missing from the desktop and an unsaved icon present sees exactly the common icons passed. Cheaper substitute: matching by display name (SPEC §15 "Matching").
- [ ] Read the desktop back and count `Total` (saved icons), `InPlace` (read-back position equals the saved position exactly), `NotPlaced` (on the desktop, read-back differs) and `Missing` (saved icons not on the desktop). Return `Restored` when `NotPlaced` is 0 and `Failed` with the same counts when it is not. Done when: a test with 3 saved icons, one missing, gets `Restored`, `Total` 3, `InPlace` 2, `Missing` 1; and with `SnapBy` 1 on the fake gets `Failed`, `InPlace` 0, `NotPlaced` 3. Cheaper substitute: reporting the number of positions sent instead of the number read back, so a restore Windows ignored reads as success.
- [ ] An existing `undo.json` that cannot be read is replaced by the new snapshot (SPEC §14: it is Berth's scratch file). Done when: a test with `{` in `undo.json` restores successfully and finds a valid snapshot afterwards. Cheaper substitute: refusing every restore until the user deletes a file they have never heard of.
- [ ] When the snapshot cannot be written, return `Failed` and move nothing. Done when: a test with a read-only local root asserts `Failed`, the `Path` of the undo file, and no `SetPositions` call. Cheaper substitute: restoring anyway, leaving the user with no way back.
- [ ] Add `tests/Berth.Tests/Core/RestoreTests.cs` with those cases. Done when: all pass, and the missing-icon test fails when matching uses `Name`. Cheaper substitute: one test with identical saved and current sets.
- [ ] Run the restore inside `OperationLock.Run`, returning `Refused` with `Busy` when the lock is not acquired (**Corrected 2026-11-18:** D01 T01 §14 plan review). Done when: a test holding the lock gets `Refused` with `Busy`, and the desktop, the layout file and `undo.json` are unchanged. Cheaper substitute: wrapping only save, so a restore can write beside another.
- [ ] Log exactly one event per restore through `Log.Result` (**Corrected 2026-11-18:** D01 T01 §15 plan review). Done when: tests read back exactly one line with its level and message for a done, a refused and a failed result. Cheaper substitute: asserting the returned result only.
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
- [ ] Run the undo inside `OperationLock.Run`, returning `Refused` with `Busy` when the lock is not acquired (**Corrected 2026-11-18:** D01 T01 §14 plan review). Done when: a test holding the lock gets `Refused` with `Busy`, and the desktop and `undo.json` are unchanged. Cheaper substitute: wrapping only save.
- [ ] Log exactly one event per undo through `Log.Result` (**Corrected 2026-11-18:** D01 T01 §15 plan review). Done when: tests read back exactly one line with its level and message for a done, a refused and a failed result. Cheaper substitute: asserting the returned result only.
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

- [ ] Add `Rename(string key, string name)`: validate against the other layouts' names with `LayoutNames.Validate`, store the trimmed name, return `Renamed`; an invalid name returns `Refused` with `InvalidName` and the `NameError`, and an unknown key returns `Refused` with `NoLayout`. Done when: tests cover success with trimming (`"  Docked "` read back from the layout file as `Docked`; **Corrected 2026-11-18:** D01 T01 §7 completion review), each `NameError`, renaming to its own name with different case (allowed), and an unknown key. Cheaper substitute: renaming the file, which is named by key and must not move.
- [ ] Add `Revert(string key)`: swap `Icons` and `SavedAt` with `Previous`, so a second revert returns to the start; return `Reverted` with `SavedAt` set to the save now current (for the SPEC §8 sentence); a layout with `Previous` `null` returns `Refused` with `NoLayout`. Done when: a test reverts twice and the file equals its starting content, and `SetPositions` is never called. Cheaper substitute: dropping the current save instead of swapping, which makes revert one-way.
- [ ] Add `Delete(string key)` removing the file and returning `Deleted`, or `Refused` with `NoLayout` for an unknown key; it never moves an icon. Done when: tests cover both and assert no `SetPositions` call. Cheaper substitute: deleting `undo.json` too, which belongs to the restore.
- [ ] Return `Failed` with `Path` and `Detail` when a write or delete throws. Done when: a test with a read-only layout file asserts `Failed` for rename and the file unchanged. Cheaper substitute: reporting success before the read-back.
- [ ] Add `tests/Berth.Tests/Core/LayoutEditTests.cs` with those cases. Done when: all pass, and the double-revert test fails when revert discards instead of swapping. Cheaper substitute: tests that assert only the returned `Kind`.
- [ ] Run rename, revert and delete inside `OperationLock.Run`, each returning `Refused` with `Busy` when the lock is not acquired (**Corrected 2026-11-18:** D01 T01 §14 plan review). Done when: tests holding the lock get `Refused` with `Busy` for each, and the desktop and the layout file are unchanged. Cheaper substitute: wrapping only save.
- [ ] Log exactly one event per rename, revert and delete through `Log.Result` (**Corrected 2026-11-18:** D01 T01 §15 plan review). Done when: tests read back exactly one line with its level and message for a done, a refused and a failed result. Cheaper substitute: asserting the returned result only.
- [ ] Commit: `core: rename, revert and delete operations`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutEditTests"` passes, and fails when `Revert` discards the current save instead of swapping.

## 13. Comparison of a layout with the desktop

> **Started:** 2026-11-18T06:43:02Z

What the position map and the status line show: each saved icon is in place, moved or missing (SPEC §7 table, §8).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `LayoutComparison` in `src/Berth/Core/LayoutComparison.cs`. Done when: three icons in three states are classified.
2. Add monitor lookup and off-screen detection. Done when: an icon outside every monitor is flagged with its nearest monitor.

- [x] Add `IconState` (`InPlace`, `Moved`, `Missing`, `Neutral`) and `ComparedIcon(IconPosition Saved, IconState State, IconPosition? Current, int MonitorNumber, bool OffScreen)`, and `LayoutComparison.Compare(Layout layout, IReadOnlyList<IconPosition> desktop)` returning the icons with counts `InPlace`, `Moved`, `Missing`. Done when: a test with one equal, one shifted and one absent icon gets one of each. Cheaper substitute: comparing display names. **Corrected 2026-11-18:** D01 T01 §13 plan review: icons match by `Id` ignoring case (SPEC §15), never by display name; tests also cover a live id differing only in case with a changed name beside another icon carrying the saved name, one-pixel moves on `X` alone and `Y` alone, each icon's `Saved`, `State` and `Current` (the live coordinates when moved, null when missing), unequal counts, a desktop-only icon left out, and an empty desktop marking every saved icon missing.
- [x] Add `LayoutComparison.Neutral(Layout layout)` (every icon `Neutral`, for another arrangement's layout and for an unreachable desktop) and `LayoutComparison.Unsaved(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<IconPosition> desktop)` (the current icons, `Neutral`). Done when: tests assert every icon is `Neutral`. Cheaper substitute: showing "in place" for a layout that cannot be compared. **Corrected 2026-11-18:** D01 T01 §13 plan review: both with non-empty fixtures, asserting the exact ids, names, plotted coordinates (saved for `Neutral`, live for `Unsaved`), monitor numbers and count, and zero in-place, moved and missing.
- [x] Number monitors left to right then top to bottom (sorted by `X` then `Y`, starting at 1) in `MonitorNumbering.Order(monitors)`, and give each icon the number of the monitor whose bounds contain its saved position (SPEC §7). Done when: a test with a monitor at `-1920,0` numbers it 1. Cheaper substitute: numbering by the order Windows returns, which changes between sessions. **Corrected 2026-11-18:** D01 T01 §13 plan review: a monitor's bounds hold `X` to `X + Width - 1`, so a shared edge belongs to the monitor on its right or below; tests number shuffled monitors including two with the same `X`, put an icon on a shared edge, and move its live position to another monitor to show the saved position decides, with `OffScreen` false.
- [x] For a saved position outside every monitor, set `OffScreen` `true` and `MonitorNumber` to the nearest monitor by distance to its rectangle (SPEC §7, last bullet). Done when: a test with an icon at `5000,5000` gets `OffScreen` and the nearest monitor. Cheaper substitute: dropping off-screen icons from the result. **Corrected 2026-11-18:** D01 T01 §13 plan review: with unequal monitors where the nearest edge belongs to a monitor other than number 1 and the nearest centre to another, asserting the exact number.
- [x] Return the icons in reading order: by `Y`, then `X` (SPEC §7 automation order). Done when: a test with shuffled input gets sorted output. Cheaper substitute: file order. **Corrected 2026-11-18:** D01 T01 §13 plan review: by saved `Y` then saved `X`, asserted as an exact id sequence with two icons on one row and a moved icon whose live position would reverse the order.
- [x] Add `tests/Berth.Tests/Core/LayoutComparisonTests.cs`. Done when: all pass, and the moved test fails when equality ignores `Y`. Cheaper substitute: one all-in-place test.
- [x] Commit: `core: comparison of a layout with the desktop`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LayoutComparisonTests"` passes, and fails when position equality ignores `Y`.

> **Verified:** 2026-11-18 | §13 | cddf524dbcb5afbe1834af24cea6d72a93e71f24
> **Review:** `docs/reviews/d01-t01-s13.json`

## 14. Operation lock across processes

> **Started:** 2026-11-18T06:58:16Z

Save, restore, undo and revert run one at a time across the tray app and the command line (SPEC §15, last bullet).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `OperationLock` in `src/Berth/Core/OperationLock.cs`. Done when: a second holder waits and is refused after the timeout.
2. Use it in `LayoutService`. Done when: a busy lock returns `Refused` with `Busy`. **Corrected 2026-11-18:** D01 T01 §14 plan review: `LayoutService` is built in §9 to §12, which depend on this section, so the wiring and its busy tests are theirs (one item each); this section ships the lock and `OperationLock.ForApp`.

- [x] Add `OperationLock(BerthPaths paths, TimeSpan wait)` with one entry point, `Run<T>(Func<T> work, Func<T> busy)`: take the named mutex `Local\Berth.Operation.<first 16 hex of SHA-256 of the data root path, lowercased>`, waiting up to `wait` (5 seconds in production), run `work` and release the mutex on the same thread, or return `busy()` when it was not acquired. `LayoutService` wraps every operation in it (§9 to §12). Done when: a test holds the mutex on a second thread (signalled with events, never with sleeps) and `Run` on the test thread with a 200 ms wait returns `busy` without calling `work`, then calls `work` once the second thread releases. Cheaper substitute: an in-process `lock`, which the command line and tray app do not share; or a test where one thread takes the mutex twice, which a mutex allows. **Corrected 2026-11-18:** D01 T01 §14 plan review: the mutex is `Global\Berth.Operation.<hash>` (shared across the user's logon sessions, since a command line in another session must not write beside the tray app), the hash is the first 16 lowercase hex of SHA-256 of the UTF-8 of the data root's full path without trailing separators, lowercased invariant, so spellings of one root share a lock and different roots do not; `OperationLock.ForApp(paths)` uses the 5-second wait. Tests: one pending `Run` acquires and calls `work` exactly once when the holder releases before its deadline; a timed-out `Run` returns `busy` once, no sooner than the wait less 50 ms; while `Run`'s own `work` runs, the helper reports `busy`, and acquires after it returns; a throwing `work` propagates and another thread acquires at once; two spellings of one root contend, a different root does not; `ForApp` waits 5 seconds and the name starts `Global\`.
- [x] Treat an abandoned mutex (the holder crashed) as acquired. Done when: a test abandons the mutex on a thread that exits and the next `Run` calls `work`. Cheaper substitute: failing forever after one crash. **Corrected 2026-11-18:** D01 T01 §14 plan review: the test keeps a second handle open across the owner thread's exit, so the mutex is really abandoned rather than created again, and `Run` calls `work` once and releases.
- [x] Prove it across processes: a small test helper process (the test assembly started with an argument, with `CreateNoWindow`, a 20 second bound and a tree kill in `finally`) holds the mutex for the same data root until told to release through its standard input, and `Run` in the test process returns `busy` while it holds. Done when: the test passes and no helper process remains. Cheaper substitute: assuming two wrapper objects in one process prove what two processes do.
- [x] Add `tests/Berth.Tests/Core/OperationLockTests.cs`. Done when: all pass, and the busy test fails when `Run` calls `work` without acquiring. Cheaper substitute: a test that sleeps and hopes.
- [x] Commit: `core: operation lock across processes`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*OperationLockTests"` passes, and fails when `OperationLock.Run` calls `work` without holding the mutex.

> **Verified:** 2026-11-18 | §14 | 81d445e5cff16bdff457349291c21f110dca7d4b
> **Review:** `docs/reviews/d01-t01-s14.json`

## 15. Log file with a size cap

> **Started:** 2026-11-18T07:23:55Z

One line per event, capped at 1 MB with one previous file (SPEC §18).

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Log` in `src/Berth/Core/Log.cs`. Done when: lines have the SPEC §18 shape.
2. Add the cap and the calls from `LayoutService`. Done when: the file rolls at 1 MB and every operation writes a line. **Corrected 2026-11-18:** D01 T01 §15 plan review: `LayoutService` is §9's and depends on this section, so the calls, the result-to-level mapping and the one-event-per-operation tests are §9 to §12's; this section ships the logger.

- [x] Add `Log(BerthPaths paths, TimeProvider clock)` with `Info`, `Warn`, `Fail(string message)` and `Fail(string message, Exception error)`, appending, after `BerthPaths.EnsureFolderFor(paths.LogFile)` (**Corrected 2026-11-18:** `D01 T01 §3`), `<UTC time ending Z> <LEVEL> <message>` as one line (the exception form adds the exception text on following lines), opening the file shared so two processes can append. Done when: a test reads back `2026-11-15T07:14:03Z INFO hello` with a fixed clock. Cheaper substitute: `Console.WriteLine`, which a tray app has nowhere to show. **Corrected 2026-11-18:** D01 T01 §15 plan review: a CR or LF in a message is written as the two characters `\r` or `\n`, so one event is one header line; the exception form adds `exception.ToString()` on continuation lines that each start with two spaces; records are UTF-8 without a byte-order mark, ending CRLF. Tests read back a message with a line break as one line, and a caught exception from a named method as the `FAIL` header, its type, message and that method's stack frame.
- [x] Before a write that would pass 1,048,576 bytes, rename `berth.log` to `berth.log.1` (replacing it) and start a new file. Done when: a test with the cap lowered through an internal constructor parameter sees the roll and exactly two files. Cheaper substitute: an unbounded log (`AGENTS.md`: a writer has a bound on what it writes). **Corrected 2026-11-18:** D01 T01 §15 plan review: the size check, rename and append run under the named mutex `Global\Berth.Log.<16 hex of the log path, as the operation lock names its root>`, so the tray app and the command line never race a roll. Tests count bytes of encoded records (a non-ASCII name, the CRLF included): a record that fits exactly stays in `berth.log`, the next starts a new file, a second roll replaces `berth.log.1` with the file just before it, both files' contents and byte lengths asserted; and two writers in separate processes crossing a roll on one temporary root leave every event once, intact, across the two files.
- [x] Never throw from a log call: a failed write is swallowed after one retry. Done when: a test with the log folder made read-only shows `Info` returning normally. Cheaper substitute: a logging failure that aborts a restore half way. **Corrected 2026-11-18:** D01 T01 §15 plan review: tests hold the log file exclusively: released after the first failed attempt, the event is written once by the retry; never released, the call returns after exactly two attempts.
- [x] Add `Log.Result(OperationResult? result, string text)` choosing the level from the result kind (`INFO` for done, `WARN` for `Refused`, `FAIL` for `Failed`), for `LayoutService` to call once per operation (§9 to §12; `OperationResult` arrives in §9, so until then the method takes the level as an enum `LogLevel` and §9 adds the overload). Done when: a test logs one line per level and reads the three levels back. **Corrected 2026-11-18:** D01 T01 §15 plan review: this section ships `Write(LogLevel level, string message)` and its test; the `OperationResult` overload and its mapping are §9's item. Cheaper substitute: logging in each caller, so the command line and window disagree.
- [x] Add `tests/Berth.Tests/Core/LogTests.cs`. Done when: all pass, and the roll test fails when the cap check is removed. Cheaper substitute: asserting the file exists.
- [x] Commit: `core: log file with a size cap`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*LogTests"` passes, and fails when the size cap check is removed.

> **Verified:** 2026-11-18 | §15 | fa164adbdd61a9b0617bd261ce5d206db329690f
> **Review:** `docs/reviews/d01-t01-s15.json`

## 16. One wording for every result

Every sentence of SPEC §8, §13 and §18 produced from an `OperationResult` in one class, so the window, the tray and the command line cannot drift.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Add `Messages` in `src/Berth/Core/Messages.cs`. Done when: each SPEC sentence is produced for its result.
2. Test against the SPEC text. Done when: every row of the SPEC §8 result rows, §13 output column and §18 table has a test with the exact string.

- [ ] Add `Messages.Status(OperationResult r, TimeZoneInfo zone)` returning the SPEC §8 sentence for `Restored`, `Undone`, `Saved` (with and without "The previous save is kept."), `Reverted` (from `r.SavedAt`), and for a `Failed` restore or undo with counts the partial sentence ("did not stay", "no longer on the desktop", in that order), with singular forms for 1 icon; and the constant `Messages.Working` (`Working`). Done when: a test per row of the SPEC §8 table and per partial combination (not placed only, missing only, both, one icon) compares the exact SPEC string. Cheaper substitute: building plurals with `(s)`.
- [ ] Add `Messages.Refusal(OperationResult r)` returning the SPEC §18 message for each `Refusal` and for `Failed` (a write failure naming `Path` and `Detail`, otherwise "Something went wrong: ..."), and the three name errors of SPEC §10 (the settings row of SPEC §18 included, added 2026-11-18 for `D01 T01 §6`). Done when: a test per row of the SPEC §18 table compares the exact string. Cheaper substitute: showing exception type names.
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
- [ ] Return `Applied` `false`, `Value` the old state and `Message` the SPEC §18 write message (`Berth could not write <path>: <reason>.` through `Messages`) when the save throws `IOException` or `UnauthorizedAccessException`, or the SPEC §18 settings message (`The settings file settings.json could not be read. It was left untouched.`) when it throws `UnreadableSettingsException`, and log one `FAIL` line with that message (**Corrected 2026-11-18:** an unreadable file was worded as a write failure, `D01 T01 §6` plan review). Done when: tests with a read-only settings file and with `{` in `settings.json` get `Applied` `false`, the message, one `FAIL` line, and the file's bytes unchanged. Cheaper substitute: letting the exception reach the tray, which would leave the check flipped.
- [ ] Add `RecordStartWithWindows(bool on, Exception? failure)`, for the caller that wrote the `Run` value (`D05 T01 §10` owns the registry): with no failure log `INFO Start with Windows turned on.` or `INFO Start with Windows turned off.` and return `Applied` `true`; with one, log `FAIL` with the SPEC §18 message `Berth could not change the Start with Windows setting: <reason>.` and return it with `Applied` `false`. Done when: tests read each line back. Cheaper substitute: logging in the view model, so the tray and window word it differently.
- [ ] Add `Messages.SettingChanged(string setting, bool on)` for the four SPEC §18 lines, used by `SettingChanges`. Done when: `MessagesTests` compares the four strings exactly. Cheaper substitute: building the sentence in `SettingChanges`, outside the one class that words results.
- [ ] Add `tests/Berth.Tests/Core/SettingChangesTests.cs` with those cases. Done when: all pass, and the read-back test fails when `SetRestoreAutomatically` skips the second load. Cheaper substitute: asserting on the returned value only.
- [ ] Commit: `core: setting changes applied, read back and logged`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*SettingChangesTests"` passes, and fails when a failed save is reported as applied.

## 19. Fake contract tests all go through the seams

> **Started:** 2026-11-18T06:24:14Z

Filed 2026-11-18 from the D01 T01 §8 completion review (P3, below the pin's blocking levels): `SnapByShiftsOnlyTheChosenIcons` called the fake's own type, while the record says every fake test exercises `IDesktopIcons`. The change was made in 9a08deb, after the reviewed candidate; this section verifies and reviews it.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Check every fake test reaches the fakes through the interfaces. Done when: no test in `FakeDesktopIconsTests` calls a desktop or monitor member on a fake's own type (**Corrected 2026-11-18:** plan review, monitors too).

- [x] Route `SnapByShiftsOnlyTheChosenIcons` through `IDesktopIcons` (`Seam(fake)`) for all three cases, keeping its exact coordinates. Done when: the test passes and every `SetPositions` and `ReadIcons` call in `tests/Berth.Tests/Fakes/FakeDesktopIconsTests.cs` is on an `IDesktopIcons`. Cheaper substitute: narrowing the record's claim instead.
- [x] Make the contract members of `FakeDesktopIcons` and `FakeMonitors` explicit interface implementations, so a test that calls them on the fake's own type does not compile; setup and inspection members (`Calls`, `Unavailable`, `SnapBy`, `Monitors`) stay on the fake (**Corrected 2026-11-18:** plan review). Done when: the suite builds and passes, and calling `fake.ReadIcons()` or `fake.Read()` in a test is a compile error. Cheaper substitute: a grep that a later edit can slip past.
- [x] Commit: `test: fake contract tests go through the seams`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*FakeDesktopIconsTests"` passes.

> **Verified:** 2026-11-18 | §19 | d87058ecaad58505ac1a8442bee52f78c535418e
> **Review:** `docs/reviews/d01-t01-s19.json`

## 20. The waiting-lock test must see the contender blocked

> **Started:** 2026-11-18T07:12:10Z

Filed 2026-11-18 from the D01 T01 §14 completion review (P3, below the pin's blocking levels): `AWaitingRunTakesTheLockWhenItIsReleasedBeforeTheDeadline` polls up to ten seconds for the contender to block, then checks only that it is alive, so a contender the scheduler delayed past the release could pass with a run that never waits.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Make seeing the blocked contender a condition of the test. Done when: the test fails when the contender is never seen blocked before the release.

- [x] In `tests/Berth.Tests/Core/OperationLockTests.cs`, assert that the contender was seen in `WaitSleepJoin` before `holder.Release()`, separately from its being alive. Done when: the test passes, and fails when `OperationLock.Run` waits 0 ms. Cheaper substitute: asserting only that the thread is alive. **Corrected 2026-11-18:** D01 T01 §20 plan review: the observation is a helper, `SeenBlocked(thread, deadline)`, that the waiting test asserts before the release, with its own tests: a thread blocked on an event is seen blocked, and a thread kept alive and busy (never waiting) through the deadline is not, so the assertion is what catches a contender that never waits.
- [x] Commit: `test: the waiting-lock test sees the contender blocked`

**Test checkpoint:** `dotnet test --solution Berth.slnx --filter-class "*OperationLockTests"` passes, and fails when `Run` waits 0 ms.

> **Verified:** 2026-11-18 | §20 | 9a534f888ab57ac71eeed3a20a84961e4cbae64a
> **Review:** `docs/reviews/d01-t01-s20.json`

## 21. The reverse adjacency names its owner

Filed 2026-11-18 from the D01 T01 §9 reconciliation (P3, below the pin's blocking levels): the Outcome's `reverse=applicable` names no owner, while the rationale gives undo of a restore to §11 and revert and delete of a save to §12. The adjacency format takes one owner per key.

**Phase:** 1
**Needs:** none
**Surface:** no

**Build order:**

1. Name the owner on the formal line. Done when: `python scripts/todo-graph.py query adjacency` shows `reverse` owned by `D01 T01 §12` for this file.

- [ ] Set `reverse=applicable @ D01 T01 §12` in `todo/01-core/TODO-01-layout-core.md` (the save's reverse paths, revert and delete) and keep the rationale naming §11 for the undo of a restore. Done when: `validate` passes and `query adjacency` lists the owner. Cheaper substitute: an owner-less `reverse=applicable`, which no tool can follow.
- [ ] Commit: `todo: the reverse adjacency names its owner`

**Test checkpoint:** `python scripts/todo-graph.py query adjacency` shows `reverse` at `D01 T01 §12` for `TODO-01-layout-core.md`.

## Verification

- With `FakeDesktopIcons`, a save, a move, a restore and an undo leave the fake desktop where it started, the layout file in the SPEC §14 shape, and one log line per operation.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
