---
schema_version: 1
id: packaging
domain: 07-release
status: active
title: "TODO-01 -- Packaging and release"
depends_on: ["dotnet-foundation", "command-line", "main-window", "automatic-restore"]
---

# TODO-01 -- Packaging and release

> **Goal:** One self-contained `berth.exe` comes out of one command and out of a release workflow, the README describes the app that exists with real screenshots, and a sweep proves every line of `design/SPEC.md` is working.

> **Current state:** Measured 2026-11-15. `dotnet build` produces a framework-dependent `berth.exe` with its assemblies beside it. The README says the app is not built and shows design mockups. `CHANGELOG.md` lists only the brand, contract and plan. There is no release workflow and no release.

## Inputs

- `docs/plans/2026-11-15-berth-foundation.md`: "Tech stack" (single-file, self-contained, `win-x64`).
- `design/SPEC.md`, all of it, for the sweep; §20 for what is out of scope.
- `README.md`, `CHANGELOG.md`, `.github/workflows/build.yml`.
- -> XREF: D00 T01 §5 -- the build workflow the release workflow reuses
- -> XREF: D03 T01 §8 -- the process tests run against the published file here
- -> XREF: D05 T01 §14 -- the smoke test runs against the published file here
- -> XREF: D06 T01 §5 -- the last behaviour the sweep covers
- -> XREF: D99 T01 §3 -- the owner tags and publishes the first release

## Outcome

- `pwsh scripts/publish.ps1` writes `artifacts/berth.exe`, one file that runs on a machine with no .NET installed.
- Pushing a tag `v*` builds, tests and attaches that file and its checksum to a draft GitHub release.

**Adjacency:** list=not-applicable (no records); document=applicable @ D07 T01 §3; settings=not-applicable (no setting of its own); reporting=not-applicable (no summaries); notifications=not-applicable (nothing notified); permissions=not-applicable (no roles); audit=applicable @ D07 T01 §5; exchange=not-applicable (nothing imported or exported); reverse=not-applicable (a release is withdrawn by the owner on GitHub, D99 T01 §3)

**Adjacency rationale:** The README and the release notes are what a user carries away. The acceptance sweep is the record that each SPEC line was checked.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Single-file self-contained publish | D05 T01 §14, D06 T01 §5 | [x] |
| 2 | §2 | Release workflow on a version tag | §1, D00 T01 §5 | [x] |
| 3 | §3 | README for the built app with real screenshots | §1 | [ ] |
| 4 | §4 | Changelog and third-party notices | §3 | [ ] |
| 5 | §5 | Acceptance sweep against the SPEC | §2, §4 | [ ] |

## 1. Single-file self-contained publish

One command, one file, proven by running the published file itself.

**Phase:** 7
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Add publish properties and `scripts/publish.ps1`. Done when: `artifacts/berth.exe` is the only file needed.
2. Run the process and smoke tests against it. Done when: both pass on the published file.

- [x] Add a publish profile to `src/Berth/Berth.csproj` (or `Properties/PublishProfiles/Release.pubxml`): `PublishSingleFile` `true`, `SelfContained` `true`, `RuntimeIdentifier` `win-x64`, `IncludeNativeLibrariesForSelfExtract` `true`, `EnableCompressionInSingleFile` `true`, `DebugType` `embedded`, and no trimming (WPF does not support it). Done when: `dotnet publish src/Berth -c Release` writes a folder whose only file is `berth.exe`. Cheaper substitute: trimming to shrink the file, which breaks WPF at run time.
- [x] Add `scripts/publish.ps1`: clean `artifacts/`, publish, copy `berth.exe` to `artifacts/berth.exe`, write `artifacts/berth.exe.sha256` (the lowercase SHA-256, two spaces, the file name), and print the size in MB. Done when: `pwsh scripts/publish.ps1` exits 0 and both files exist. Cheaper substitute: publishing by hand with remembered flags.
- [x] Let `BerthProcess` and `RunningApp` take the executable from the environment variable `BERTH_EXE` when set, and add `-Published` to `scripts/check-all.ps1` that publishes, sets `BERTH_EXE` to `artifacts/berth.exe`, and runs the tests with traits `RealDesktop`, `Smoke` and `Published`, failing when the `Published` selection finds no test. Done when: `pwsh scripts/check-all.ps1 -Published` passes on the build machine and its output names at least one `Published` test. Cheaper substitute: assuming the single file behaves like the build output (embedded resources, the manifest and the icon can differ).
- [x] Assert on the published file: its embedded manifest holds `consoleAllocationPolicy` and `PerMonitorV2`, its icon resource is present, and `berth.exe --version` prints `Berth 0.1.0`. Done when: a test with trait `Published` passes under `-Published`. Cheaper substitute: none needed.
- [x] Record the published size in the README's "How is it built?" section as a measured fact with its date. Done when: the README names the size printed by the script. Cheaper substitute: an estimate.
- [x] Commit: `release: single-file self-contained publish`

**Test checkpoint:** `pwsh scripts/check-all.ps1 -Published` exits 0 on the build machine, and fails when `PublishSingleFile` is turned off (the process tests then cannot find a lone `artifacts/berth.exe`). **Corrected 2026-11-21:** the single-file check rejects extra files in `scripts/publish.ps1` with exit 1 before replacing `artifacts/berth.exe` and its `.sha256` sidecar or running the process tests.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §1 | fda1a10f96d465b78783acbf4959d7d689c548ad
> **Review:** `docs/reviews/d07-t01-s1.json`

## 2. Release workflow on a version tag

A tag builds, tests and drafts a release. Publishing the draft is the owner's step (`D99 T01 §3`).

**Phase:** 7
**Needs:** none
**Surface:** no

**Build order:**

1. Add `.github/workflows/release.yml`. Done when: it is valid and names the right steps.
2. Dry-run it without a tag. Done when: `workflow_dispatch` builds the artifact and creates no release.

- [x] Add `.github/workflows/release.yml` named `release`, triggered by tags matching `v[0-9]+.[0-9]+.[0-9]+` and by `workflow_dispatch`, on `windows-latest`, with `permissions: contents: write`: checkout with full history, set up .NET, Python and Node as `build.yml` does, run `pwsh scripts/check-all.ps1`, run `pwsh scripts/publish.ps1`, and upload `artifacts/berth.exe` and its `.sha256` as a workflow artifact. Done when: the workflow file passes `gh workflow view release.yml` after the push. Cheaper substitute: a release built on a developer's machine.
- [x] On a tag only, fail unless the tag equals `v` plus `Version` from `Directory.Build.props`, then create a draft release with `gh release create "$tag" --draft --verify-tag --title "Berth <version>" --notes-file <the tag's section of CHANGELOG.md>` and attach both files. Done when: the step is guarded by `if: startsWith(github.ref, 'refs/tags/')` and a script `scripts/changelog-section.py <version>` prints that version's section (with a unit test in `scripts/tests/`). Cheaper substitute: a published (not draft) release nobody looked at.
- [x] Run the workflow once through `gh workflow run release.yml` on `main` and confirm it is green and created no release. Done when: `gh run list --workflow release.yml --limit 1` shows `completed success` and `gh release list` is empty. Cheaper substitute: pushing a real tag to test, which only the owner does (`D99 T01 §3`).
- [x] Add `release.yml` to nothing in `.conclave/ci.json`: it is not a per-push gate. Done when: `python scripts/ci_status.py` still lists only `build.yml` and `plan.yml`. Cheaper substitute: watching a workflow that runs only on tags, which would read as unknown.
- [x] Commit: `ci: release workflow on a version tag`

**Test checkpoint:** `gh run list --workflow release.yml --limit 1 --json conclusion` reports `success` for the manual run, and `python -m unittest discover -s scripts/tests` passes with the changelog script's test, which fails when the wrong version's section is returned.

> **Started:** 2026-11-21

> **Verified:** 2026-11-21 | §2 | 18445977503af6da46d5ea5374c2b97f6efdbc45
> **Review:** `docs/reviews/d07-t01-s2.json`

## 3. README for the built app with real screenshots

The README stops describing a plan and describes the program (`README.md`).

**Phase:** 7
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Capture real screenshots. Done when: `docs/images/` holds the window in light and dark with sample data.
2. Rewrite the status, usage and install sections. Done when: nothing in the README says "not built".

- [x] Add a render test (trait `Docs`) that renders the full main window in the data state with the sample data of the mockups (three layouts, 42 icons on two monitors) in light and dark, and a script (`scripts/render-docs-images.ps1`) that validates both PNGs and replaces `docs/images/main-window-light.png` and `main-window-dark.png` via per-file `File.Replace` under a machine-wide lock in `%ProgramData%\Berth\locks` so canonical README paths never disappear on interruption and retry restores a matched pair. On an unlocked desktop `DocsRenderTests` captures the live framed window via `PrintWindow(PW_RENDERFULLCONTENT)`; on an unattended locked desktop where DWM returns a blank surface, it measures the live `MainWindow`'s exact Win32 client bounds and renders `window.RootGrid` at those exact bounds inside the 980x640 frame (with the operator's unlocked-desktop `-RequireNative` refresh owned by `D99 T01 §4`). Done when: both files exist, decode as valid 980 by 640 PNGs, are committed, and `scripts/tests/test_render_docs_images.py` passes. Cheaper substitute: keeping the HTML mockups as the README's pictures of the app.
- [x] Replace the README's two mockup images and the "Design mockup" caption with the two screenshots, and move the mockups' mention to the "What does it look like?" section as the design reference. Done when: the README's first image is `docs/images/main-window-light.png`. Cheaper substitute: none needed. **Corrected 2026-11-21:** the first application screenshot is `docs/images/main-window-light.png` (following the brand icon in the header).
- [x] Rewrite "What works today?" as a table of what the release does, and "How will I use it?" as "How do I use it?", adding "How do I install it?": pre-release source-build via `pwsh scripts/publish.ps1` (PowerShell 7 and .NET 11 SDK) and post-release download of `berth.exe` + `berth.exe.sha256` with `Get-FileHash` verification, put it anywhere, run it, and turn on "Start with Windows" if wanted; and "How do I remove it?": turn off "Start with Windows", exit from the tray, delete `berth.exe` (and `berth.exe.sha256`), the two data folders, and optionally `%TEMP%\.net\berth`. Done when: every runnable command in the README (`pwsh scripts/publish.ps1`, `Get-FileHash` match and 1-byte tamper mismatch refusal, `.\berth.exe --version`/`save`/`restore`/`undo`/`list`, `python assets/build.py`) ran on the build machine with matching output, `TrayAppTests` proves `ExitItem.PerformClick()` exits cleanly, and the disposable published walkthrough confirms first-run window launch, file lock while running, hide-to-tray on close, `%TEMP%\.net\berth`, and post-exit deletion (interactive taskbar right-click -> **Exit** and `scripts/start-run.ps1` are operator-only under `AGENTS.md` and owned by `D99 T01 §4`). Cheaper substitute: instructions nobody ran.
- [x] State the limits honestly in a "What does it not do?" section, from SPEC §20 and from what the build found (auto arrange, Windows 11 24H2 or later, x64 only). Done when: each SPEC §20 item appears. Cheaper substitute: leaving a reader to find the limits by trial.
- [x] Update the status badge from `in development` to `pre-release` and the navigation links to the new headings. Done when: every anchor in the navigation row resolves to a heading. Cheaper substitute: dead links.
- [ ] Commit: `docs: README for the built app with real screenshots`

**Test checkpoint:** A script check in `scripts/tests/test_readme.py` passes: every relative link and image in `README.md` exists on disk and every in-page anchor matches a heading; it fails when an image path is misspelled.

> **Started:** 2026-11-21T13:09Z

**Corrected 2026-11-21:** re-planned with `todo-graph.py replan`; the build first began at 2026-11-21T12:51Z; its review chain is kept as `docs/reviews/07-release/D07-T01-s3-chain4.md`.

**Corrected 2026-11-21:** re-planned with `todo-graph.py replan`; the build first began at 2026-11-21T12:29Z; its review chain is kept as `docs/reviews/07-release/D07-T01-s3-chain3.md`.

**Corrected 2026-11-21:** re-planned with `todo-graph.py replan`; the build first began at 2026-11-21T12:07Z; its review chain is kept as `docs/reviews/07-release/D07-T01-s3-chain2.md`.

**Corrected 2026-11-21:** re-planned with `todo-graph.py replan`; the build first began at 2026-11-21; its review chain is kept as `docs/reviews/07-release/D07-T01-s3-chain1.md`.

## 4. Changelog and third-party notices

What 0.1.0 contains, and whose work it includes.

**Phase:** 7
**Needs:** none
**Surface:** no

**Build order:**

1. Write the 0.1.0 section of `CHANGELOG.md`. Done when: `scripts/changelog-section.py 0.1.0` prints it.
2. Add `NOTICE.md`. Done when: every package in `Directory.Packages.props` is listed.

- [ ] Move the "Unreleased" entries of `CHANGELOG.md` into a `## 0.1.0` section (no date until the owner tags it; the release workflow's notes come from this section) and add what the plan built: the command line, the tray app, the main window, the automatic restore, in user terms. Done when: `python scripts/changelog-section.py 0.1.0` prints the section. Cheaper substitute: a list of commit subjects.
- [ ] Add `NOTICE.md` listing each third-party component in the shipped file and in the repository (every `PackageReference` with its licence, the Contributor Covenant in `CODE_OF_CONDUCT.md`, the .NET runtime bundled by the self-contained publish) with where it is used. Done when: a test in `scripts/tests/test_notice.py` reads `Directory.Packages.props` and fails when a package is missing from `NOTICE.md`. Cheaper substitute: a notice written once and never checked.
- [ ] Link `NOTICE.md` from the README's licence section. Done when: the link resolves under the §3 README check. Cheaper substitute: none needed.
- [ ] Commit: `docs: changelog and third-party notices`

**Test checkpoint:** `python -m unittest discover -s scripts/tests` passes, and `test_notice.py` fails when a package in `Directory.Packages.props` is absent from `NOTICE.md`.

## 5. Acceptance sweep against the SPEC

The plan's acceptance bar, checked line by line: every control, state and command in `design/SPEC.md` works end to end.

**Phase:** 7
**Needs:** windows-host, display-session
**Surface:** no

**Build order:**

1. Build the sweep table. Done when: every SPEC bullet and table row has a row naming its proof.
2. Close every gap. Done when: no row is unproven.

- [ ] Create `docs/acceptance/2026-spec-sweep.md` with one row per bullet and table row of `design/SPEC.md` §2 to §19: the SPEC reference, what it requires in a few words, the test or render that proves it (file and test name), and `yes` or `no`. Done when: the number of rows equals the number of bullets and table rows counted by `scripts/spec-items.py`, a small script that lists them (with a unit test). Cheaper substitute: a paragraph saying everything was checked.
- [ ] For every `no`, either add the missing test and fix what it finds in the owning layer, or, when the behaviour is wrong in the SPEC rather than the code, change the SPEC with a dated line first. Done when: the table holds no `no`. Cheaper substitute: marking a row `yes` on the strength of reading the code.
- [ ] Run `pwsh scripts/check-all.ps1 -Published` and record its result and the commit in the sweep file's header. Done when: the header names the commit and "passed". Cheaper substitute: none needed.
- [ ] Record anything found that belongs to the owner in `todo/99-manual/TODO-01-operator-steps.md` through `add-todo`, and anything unrelated in `todo/findings.md` with `file:line`. Done when: the sweep file's last section lists what was filed, or says nothing was. Cheaper substitute: leaving findings in the run's chat.
- [ ] Commit: `docs: acceptance sweep against the SPEC`

**Test checkpoint:** `python scripts/spec-items.py --check docs/acceptance/2026-spec-sweep.md` exits 0 (every SPEC item has a row and every row says `yes`), and exits 1 when a SPEC bullet is added without a row.

## Verification

- `pwsh scripts/check-all.ps1 -Published` passes on the build machine, the manual release workflow run is green, and the sweep table has no unproven row.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
