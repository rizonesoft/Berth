---
schema_version: 1
id: operator-steps
domain: 99-manual
status: active
title: "TODO-01 -- Operator steps"
---

# TODO-01 -- Operator steps

> **Goal:** The few steps only the owner can take are written down, each with exactly what to do and how to tell it worked, so an unattended run parks them instead of guessing.

> **Current state:** Measured 2026-11-15. The repository `rizonetech/Berth` is public. GitHub offers no API for the social preview image, so `assets/banner/github-social-preview.png` is not yet uploaded. No release exists.

## Inputs

- `assets/banner/github-social-preview.png` (1280 by 640).
- `design/SPEC.md` §16 (automatic restore), which the walkthrough exercises on real hardware.
- -> XREF: D06 T01 §2 -- the automatic restore the walkthrough exercises
- -> XREF: D07 T01 §2 -- the release workflow the tag starts

## Outcome

- The repository's link preview shows the Berth banner.
- The owner has seen icons return after a real dock and undock, and the first release is published.

**Adjacency:** all=not-applicable (operator steps: no code, records or surface of their own)

**Adjacency rationale:** Each section is an action on GitHub or on the owner's hardware. The code they exercise is owned by `D06 T01` and `D07 T01`.

## Implementation Order

| Order | Section | Deliverable | Depends On | Status |
| ----- | ------- | ----------- | ---------- | ------ |
| 1 | §1 | Social preview image on the GitHub repository | -- | [ ] |
| 2 | §2 | Dock and undock walkthrough on real monitors | D06 T01 §5 | [ ] |
| 3 | §3 | First release tagged and published | D07 T01 §5 | [ ] |

## 1. Social preview image on the GitHub repository

GitHub sets a repository's social preview only through its settings page, so this is the owner's click.

**Phase:** 99
**Needs:** operator
**Surface:** no

**Build order:**

1. Upload `assets/banner/github-social-preview.png` in the repository's settings. Done when: the settings page shows the banner.
2. Confirm through the API. Done when: `usesCustomOpenGraphImage` is `true`.

- [ ] Open `https://github.com/rizonetech/Berth/settings`, scroll to "Social preview", choose "Edit" then "Upload an image", and pick `assets/banner/github-social-preview.png`. Done when: the settings page shows the Berth banner. Cheaper substitute: none, GitHub has no API for this.
- [ ] Run `gh repo view rizonetech/Berth --json usesCustomOpenGraphImage`. Done when: it prints `{"usesCustomOpenGraphImage":true}`. Cheaper substitute: assuming the upload saved.
- [ ] Commit: `docs: record the social preview upload`

**Test checkpoint:** `gh repo view rizonetech/Berth --json usesCustomOpenGraphImage --jq .usesCustomOpenGraphImage` prints `true`, and prints `false` before the upload.

## 2. Dock and undock walkthrough on real monitors

The automated tests post a display-change message. Only real hardware shows that Windows and Berth agree after an actual change.

**Phase:** 99
**Needs:** attended, operator
**Surface:** no

**Build order:**

1. Save a layout in each of two real monitor arrangements. Done when: `berth list` shows both.
2. Switch between them with "Restore automatically" on. Done when: the icons return each time.

- [ ] With the external monitors connected, arrange the desktop icons, run `berth save --name Docked`, disconnect them, arrange the icons again, and run `berth save --name "Laptop only"`. Done when: `berth list` prints both layouts, with an asterisk on the connected one. Cheaper substitute: two saves on one arrangement, which exercises nothing.
- [ ] Turn on "Restore automatically", reconnect the monitors, and wait 10 seconds. Done when: the icons are where "Docked" saved them and the window's status line reads "Every icon is at its saved position." Cheaper substitute: clicking Restore by hand.
- [ ] Disconnect the monitors again and wait 10 seconds. Done when: the icons are where "Laptop only" saved them. Cheaper substitute: none needed.
- [ ] Restart Explorer from Task Manager and wait 10 seconds. Done when: Berth's tray icon is back and the icons are in place. Cheaper substitute: none needed.
- [ ] Record the Windows build (`winver`), the two arrangements and the outcome of each step in `docs/acceptance/2026-dock-walkthrough.md`, and file anything that failed through `add-todo`. Done when: the file exists with the four outcomes. Cheaper substitute: an unrecorded "it worked".
- [ ] Commit: `docs: dock and undock walkthrough`

**Test checkpoint:** `Test-Path docs/acceptance/2026-dock-walkthrough.md` is `True` and the file records four outcomes; it is `False` before the walkthrough.

## 3. First release tagged and published

Publishing is outward-facing and the owner's decision: the run prepares everything and stops here.

**Phase:** 99
**Needs:** operator
**Surface:** no

**Build order:**

1. Tag `v0.1.0` on the commit the acceptance sweep passed. Done when: the release workflow drafts a release.
2. Review and publish the draft. Done when: the release is public with its file and checksum.

- [ ] Set the date on the `## 0.1.0` heading of `CHANGELOG.md`, commit, then run `git tag v0.1.0` and `git push origin v0.1.0`. Done when: `gh run list --workflow release.yml --limit 1` shows a run for the tag that completes with success. Cheaper substitute: tagging a commit the sweep did not cover.
- [ ] Open the draft release, check the notes and that `berth.exe` and `berth.exe.sha256` are attached, download the file on the build machine, and compare `Get-FileHash berth.exe` with the checksum. Done when: the two hashes are equal. Cheaper substitute: publishing unseen.
- [ ] Publish the release. Done when: `gh release view v0.1.0 --json isDraft --jq .isDraft` prints `false`. Cheaper substitute: none needed.
- [ ] Commit: `release: 0.1.0`

**Test checkpoint:** `gh release view v0.1.0 --json isDraft --jq .isDraft` prints `false`, and fails with "release not found" before the release exists.

## Verification

- The repository's preview image, the walkthrough record and the published release all exist.
- `python scripts/todo-graph.py validate` and `python scripts/todo-graph.py plan --check` pass.
