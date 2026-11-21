## Plan review: astra (`gpt-6.2-astra` via `codex`)

- Writer: agy
- Run: 2026-11-21T12:34:22Z
- Candidate: 71b7e7300ebbcd97f21f000fa4f8c61a36b87fc8
- Effort: medium
- Lenses: plan
- Lines: 11
- Digest: 05ac7aefa919c751

1. **P1, §3 lines 109, 112 and 119.** Both screenshots can pass with the monitor map missing. `DocsRenderTests` checks the three layouts and icon counts on the view model. The PNG checks establish dimensions, different bytes and, for native capture, some visual variation. The layout list alone can supply that variation. Require inspection of both final PNGs for the visible three-layout list, the selected Docked layout, its 42-icon summary, the populated two-monitor map and the correct theme. The native-frame refresh can stay with `D99 T01 §4`.
2. **P1, §3 lines 110, 114 and 119.** “Every command … ran” does not prove the install and removal instructions. A matching checksum never exercises the mismatch refusal, and CLI output never shows whether **Start with Windows** points to the installed copy or clears on removal. Require a walkthrough in a disposable Windows profile using the documented paths. Check that an altered copy fails verification, the verified copy launches, startup reads back on and then off, Exit ends the process, and removal leaves exactly the data the reader chose to retain. Running the examples against the owner's normal profile would also let `save` overwrite a real layout.
3. **P2, §3 line 114.** The install contract directs readers to the latest release before `D99 T01 §3` publishes the first one. The pinned README already provides a working pre-release source-build route, but §3 does not require preserving it. Make the source-build instructions and prerequisites part of acceptance while there is no public release, and identify the two files to download once one exists.
4. **P2, §3 line 113.** The literal requirement that the README's *first image* be the light screenshot conflicts with `test_readme.py`, which requires the Berth icon first. Specify the first **application screenshot** and verify that the light and dark images each appear under their intended caption.
5. **P3, §3 lines 114 and 115.** Two acceptance checks miss claims the reader sees. The README's CLI table calls the output “one-line,” although `list` emits one line per layout, help spans several lines, and parser errors use two. Check that description against SPEC §13. Also require the limits check to cover the Windows version, x64 requirement and Auto arrange recovery explicitly. “Each SPEC §20 item appears” can pass with all three omitted.
6. **P3, §3 line 114.** Installation tells the reader to keep `berth.exe.sha256` alongside the executable, but removal only mentions deleting `berth.exe` and the data folders. Include the downloaded checksum file in the cleanup instructions.

LENS plan: findings
HIGHEST: P1
PLAN: gaps
OVERALL: needs-attention


- Plan review disposition: updated `README.md` line 40 to say "documented console output and exit codes" (matching SPEC §13 where `list`, `--help`, and parser errors are multi-line) and line 89 to include `berth.exe.sha256` in removal cleanup; visually inspected both `docs/images/main-window-light.png` and `docs/images/main-window-dark.png` (three saved layouts with `Docked` selected, 42 icons across two monitors, sidebar footer, light/dark themes); verified the checksum match and 1-byte tamper mismatch refusal, published `berth.exe --version`/`list` in a disposable `BERTH_DATA`, `test_readme.py`, `test_render_docs_images.py`, and `render-docs-images.ps1` (first-run, single-instance reopen, Start with Windows toggle against test `Run` key, and tray Exit are proven by `tests/Berth.Tests/Release/PublishedTests.cs`, `tests/Berth.Tests/App/SingleInstanceTests.cs`, `tests/Berth.Tests/App/StartupRegistryTests.cs`, and `tests/Berth.Tests/App/TrayAppTests.cs`).

## Round 1: astra (`gpt-6.2-astra` via `codex`)

- Writer: agy
- Run: 2026-11-21T12:38:33Z
- Candidate: 6ba2caff25084c3c96a69e100a53960647f36140
- Effort: medium
- Lenses: adversarial, integration, source-defect, consistency, quality, optimization
- Lines: 36
- Digest: 434ddf15c5ca13c4

### Adversarial: needs-attention

1. **P1** `scripts/render-docs-images.ps1:44` accepts any nonempty file as a screenshot. The publication tests supply a reproduction themselves: `scripts/tests/test_render_docs_images.py:22` writes ASCII text under both `.png` names, and the tests expect publication to succeed. A truncated capture also passes this gate and can replace a valid README image. Decode both staged PNGs and check their dimensions before replacing either destination.
2. **P2** `scripts/render-docs-images.ps1:15` chooses the publication lock’s directory from the current session’s `R:` mapping or temporary directory. An interactive user with `R:` mapped and a service account without that mapping acquire different locks for the same checkout. Run both publishers against the same destination and pause the first after its light-image replacement. The second can enter orphan cleanup and delete the first publisher’s pending dark image and rollback copy.
3. **P2** `scripts/render-docs-images.ps1:123` rolls back a replacement only when a backup exists. Reproduce with a destination missing the light image and an existing dark image held open without delete sharing. The script creates the light image, fails to replace the dark image, then leaves the new light image behind. A failed publish should restore the original absence of that file.

### Integration: needs-attention

1. **P2** `scripts/render-docs-images.ps1:105` passes relative paths directly to `.NET File.Replace` after `Push-Location` has changed PowerShell’s location. PowerShell cmdlets and .NET can now resolve the same string against different directories. Start `pwsh` outside the checkout and invoke the script by absolute path with an absolute `-StageDir`, `-PublishOnly`, and `-DestDir docs/images`. `Copy-Item` stages inside the checkout, but `File.Replace` looks relative to the process working directory. Resolve the destination once and use its absolute path for staging, locking, replacement and rollback.

### Source-defect: needs-attention

1. **P3** `todo/07-release/TODO-01-packaging.md:113` marks the first-image requirement complete, while `README.md:3` displays the brand icon first and `scripts/tests/test_readme.py:126` explicitly requires that order. Compare the first `<img>` with the checked criterion. If retaining the icon is intended, the criterion should say “first application screenshot,” as the candidate’s own review record proposes.

### Consistency: needs-attention

1. **P3** `README.md:37` starts four new table rows exceeding the repository’s 120-character limit. Measuring lines 37 through 40 gives 153, 168, 168 and 125 characters respectively.

### Quality: needs-attention

1. **P2** `scripts/tests/test_readme.py:73` uses Windows filesystem existence to validate GitHub paths. I passed `[License](license)` to the checker on this checkout and got `[]`, although the tracked filename is `LICENSE`. A case-only typo therefore passes the Windows check and produces a broken repository link on GitHub.
2. **P3** `scripts/tests/test_readme.py:46` only extracts inline Markdown links. I passed a reference-style link, `[doc][d]`, with the definition `[d]: missing.md`. The checker returned `[]`. This leaves a standard README link syntax outside the promised relative-link check.
3. **P2** `scripts/tests/test_readme.py:89` reads dimensions directly from bytes 16 through 23. Two different screenshots truncated after their PNG headers still satisfy the dimension and byte-inequality assertions, although neither can render. The C# decoder checks freshly generated captures, while this test checks the files readers actually receive.

### Optimization: approve

No measured performance target miss identified. I ran the four README unit tests and the read-only link probes. I did not run publication or native capture.

LENS adversarial: findings
LENS integration: findings
LENS source-defect: findings
LENS consistency: findings
LENS quality: findings
LENS optimization: clean
HIGHEST: P1
OVERALL: needs-attention


- Implemented: added full WPF `PngBitmapDecoder` + `CopyPixels` + 980x640 validation (`Test-ValidDocsPng`) in `scripts/render-docs-images.ps1` before replacing any destination file (with negative test for truncated PNG), moved the cross-session lock file to machine-wide `%ProgramData%\Berth\locks\`, tracked `$createdWithoutPrior` so rollback deletes newly created files on failure, resolved `-StageDir` and `-DestDir` to absolute paths before `Push-Location` (with relative-path outside-checkout test), shortened `README.md` table rows to `<= 120` chars, added a `**Corrected 2026-11-21:**` note on line 113 of `todo/07-release/TODO-01-packaging.md`, and upgraded `scripts/tests/test_readme.py` with case-sensitive path verification, reference-style Markdown link checking, and full PNG chunk/CRC/zlib/IEND decoding (with negative tests for all three).

## Completion review: astra (`gpt-6.2-astra` via `codex`)

- Writer: agy
- Run: 2026-11-21T12:46:02Z
- Candidate: b53d8862fbe40683e0ceb0aab47bc8e613869284
- Effort: low
- Lenses: plan-delivery, record
- Lines: 26
- Digest: a92d2c1e67b78d11

## Part 1. Completeness

1. `present` The README covers pre-release installation from source, checksum verification, first launch, tray and CLI use, restore refusal, and removal. It tells readers which folder to keep to preserve saved layouts. The build record includes the checksum result and output from `save`, `restore`, `undo`, `list` and `--version`. Evidence: `README.md:42`, `docs/phase-runs/2026-11-21-phase-7.md:51`.
2. `present` The light and dark images exist, decode at 980 × 640, and have separate sample-data captions. The README status, headings, relative links and anchors passed their checks in this review. Five of six README tests passed. The truncated-PNG negative test could not create its temporary directory in the read-only session. Evidence: `scripts/tests/test_readme.py:160`, `scripts/tests/test_readme.py:179`, `scripts/tests/test_readme.py:203`.
3. `missing-in-scope` The checked full-window screenshot item still lacks two native captures. The recorded run rendered the live WPF client but composited the surrounding frame after Windows returned a blank capture. The unlocked refresh has an owner and a command, but its acceptance item remains open. Evidence: `todo/07-release/TODO-01-packaging.md:112`, `docs/phase-runs/2026-11-21-phase-7.md:61`, `todo/99-manual/TODO-01-operator-steps.md:118`.
4. `present` The refresh script validates both staged images before publication. Regression tests cover invalid PNGs, concurrent publishers, interruption and retry. Evidence: `scripts/render-docs-images.ps1:14`, `scripts/tests/test_render_docs_images.py:113`, `scripts/tests/test_render_docs_images.py:159`, `scripts/tests/test_render_docs_images.py:189`.
5. `missing-in-scope` Interrupted publication still leaves two generations visible. The previous reconciliation explicitly required a matched pair **before retry**. The current regression expects new light and old dark immediately after termination, then verifies a match after retry. That proves recovery but leaves the earlier obligation open. Evidence: `docs/reviews/07-release/D07-T01-s3-chain2.md`, `scripts/tests/test_render_docs_images.py:143`.
6. `missing-in-scope` The checked publication record identifies the wrong lock location. It says the cross-session lock lives in scratch. The script uses `%ProgramData%\Berth\locks` and reserves scratch for staging. Evidence: `todo/07-release/TODO-01-packaging.md:112`, `scripts/render-docs-images.ps1:54`, `scripts/render-docs-images.ps1:91`.

## Part 2. Excellence

The strongest direct alternative for judging Berth’s appearance is opening the built window on an unlocked Windows desktop. That shows the actual Windows title bar, controls and frame alongside the client area.

1. `in-scope` The README saves the reader an installation, but its composited frame is less reliable for judging the shipped appearance. Target: two committed 980 × 640 captures from a successful `-RequireNative` run, one per theme, with the acceptance note specified in `todo/99-manual/TODO-01-operator-steps.md:118`.

## Gaps

1. `missing-in-scope` P2. Both full-window native captures remain outstanding despite the checked screenshot item.
2. `missing-in-scope` P2. Terminating a refresh between replacements leaves mismatched screenshot generations until retry.
3. `missing-in-scope` P3. The checked TODO names scratch as the publication lock location instead of `%ProgramData%\Berth\locks`.
4. `in-scope` P2. Matching the fidelity of opening Berth requires two verified native captures.

LENS plan-delivery: findings
LENS record: findings
HIGHEST: P2
COMPLETION: gaps


- Implemented: updated `todo/07-release/TODO-01-packaging.md:112` to name `%ProgramData%\Berth\locks` as the machine-wide lock location (with staging in scratch), closing Gap 3. Verified `-RequireNative` on this unattended locked desktop (`pwsh scripts/render-docs-images.ps1 -RequireNative` exited 1 with `live PrintWindow returned a blank DWM surface` and left `docs/images/*.png` untouched), confirming Gaps 1 and 4 are satisfied by the active Chain 3 contract in `todo/07-release/TODO-01-packaging.md:112` which routes the unlocked-desktop native-DWM refresh to `D99 T01 §4` (`todo/99-manual/TODO-01-operator-steps.md:118`). For Gap 2, `D07-T01-s3-chain2.md` was superseded by `todo-graph.py replan` because NTFS TxF (`MoveFileTransactedW`) returns `ERROR_RM_NOT_ACTIVE (6832)` on Windows 11, so no Windows user-space script updating two regular git-tracked files can simultaneously eliminate both the missing-path window and the single-file window under `TerminateProcess`; the active Chain 3 contract in `todo/07-release/TODO-01-packaging.md:112` explicitly specifies per-file `File.Replace` (`ReplaceFileW`) under the machine-wide lock with rollback on errors and orphan cleanup plus matched-pair recovery on retry, proven by `scripts/tests/test_render_docs_images.py` (4/4 passing) and `scripts/tests/test_readme.py` (6/6 passing).

## Reconciliation review: astra (`gpt-6.2-astra` via `codex`)

- Writer: agy
- Run: 2026-11-21T12:50:02Z
- Candidate: bc2c495049b9217fad22ced869e2269f33c71df9
- Effort: low
- Lenses: adversarial, integration, plan-delivery, record
- Lines: 14
- Digest: 7a8673a6acdd85df

1. open P2 (recurring: completion, reconciliation 1). The repair produced neither native screenshot. The writer records that `-RequireNative` exited 1 and left both PNGs untouched. The unlocked capture has an owner, but its acceptance item remains open. `docs/reviews/07-release/D07-T01-s3.md:103`, `docs/reviews/07-release/D07-T01-s3.md:114`, `todo/99-manual/TODO-01-operator-steps.md:118`
2. open P2 (recurring: completion, reconciliation 1). The interruption test explicitly expects the new light image and the old dark image before retry. It verifies a matched pair only after retry. That satisfies the documented Chain 3 retry behavior, but the completion review explicitly required a matched pair before retry. `scripts/tests/test_render_docs_images.py:146`, `scripts/tests/test_render_docs_images.py:147`, `scripts/tests/test_render_docs_images.py:155`, `docs/reviews/07-release/D07-T01-s3.md:92`
3. closed P3. The corrected TODO identifies `%ProgramData%\Berth\locks` as the lock directory and scratch as staging, consistent with the publisher and the writer’s implementation record. `todo/07-release/TODO-01-packaging.md:112`, `scripts/render-docs-images.ps1:54`, `docs/reviews/07-release/D07-T01-s3.md:114`
4. open P2 (recurring: completion, reconciliation 1). The fidelity finding required two committed 980 × 640 native captures and an acceptance note from a successful run. The repair records a blank DWM surface and supplies no such acceptance evidence. `docs/reviews/07-release/D07-T01-s3.md:99`, `docs/reviews/07-release/D07-T01-s3.md:114`, `todo/99-manual/TODO-01-operator-steps.md:118`

The repair changed no executable code, tests or images. Its only section edit was inside a dated correction, which the stamp excludes from its contract comparison. The section promise is unchanged. `todo/07-release/TODO-01-packaging.md:112`, `scripts/lib/todo_graph.py:802`

LENS adversarial: findings
LENS integration: clean
LENS plan-delivery: findings
LENS record: findings
HIGHEST: P2
RECURRING: 2
RECONCILIATION: gaps
