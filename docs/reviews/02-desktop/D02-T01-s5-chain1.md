## Plan review: astra (`gpt-6.2-astra` via `codex`)

- Writer: agy
- Run: 2026-11-20T10:27:24Z
- Candidate: 42a810f6b9ac7b71d075456f3555a015c1c5f612
- Effort: medium
- Lenses: plan
- Lines: 10
- Digest: f8d1e994e7297058

1. **P1, lines 163-164 and 167.** The opt-in round trip can pass with a no-op setter when auto arrange starts off. The checkpoint also accepts a run that skips this test entirely. Require one opt-in run on a disposable interactive desktop that starts with auto arrange confirmed on. Call the public method, assert it returns `true`, independently read the flag as off, then restore the original bit in `finally` and verify it reads back as on. This proves the transition without risking the owner's icon layout.
2. **P1, lines 161-162 and 164.** Explorer can disappear after `DesktopView.Open()` succeeds, including between the write and its read-back. The tests require a typed failure for the fallback HRESULT, but do not require it for a failing `GetCurrentFolderFlags`, `SetCurrentFolderFlags`, or post-write read-back. §8's failing-factory test cannot exercise those calls. Inject a failure at each call and assert that the public member throws `DesktopUnavailableException`, preserves the cause and releases the acquired view. A failed read-back must never report success.
3. **P2, lines 161-162 and 167.** The flag tests can pass with `0` and `FWF_AUTOARRANGE` as their only inputs. A getter implemented as `flags != 0`, or a clear verified as `flags == 0`, would then pass and give the wrong answer on a desktop with another folder flag set. Keep an unrelated bit set in the fake across both reads and assert the result depends only on `FWF_AUTOARRANGE`.
4. **P2, lines 158 and 161.** The acceptance criterion promises agreement with Explorer's View menu, but the test compares two shell API readings. Record a read-only comparison with the actual “Auto arrange icons” checkmark, or change the criterion to the API agreement the test measures.
5. **P3, lines 163-164.** Line 163 gates *any* real-desktop call on `BERTH_TEST_ALLOW_AUTOARRANGE`, while line 164 requires the live read by default. Limit the opt-in condition to writes. State that the read-only test runs by default in an interactive session and skips when no interactive desktop exists. Qualify the one-skip checkpoint accordingly.

LENS plan: findings  
HIGHEST: P1  
PLAN: gaps  
OVERALL: needs-attention

- Planned: opt-in round trip confirmed-on precondition + independent read-back + restore verification in implementation commit
- Planned: mid-call COM failure wrapping + view release tests in implementation commit
- Planned: unrelated folder-flag bit masking tests in implementation commit
- Planned: shell API agreement criterion in implementation commit
- Planned: write-only opt-in guard wording in implementation commit

## Round 1: astra (`gpt-6.2-astra` via `codex`)

- Writer: agy
- Run: 2026-11-20T10:34:41Z
- Candidate: 9ad4ab15f8137416820eb57264942a2c8e308e87
- Effort: medium
- Lenses: adversarial, integration, source-defect, consistency, quality, optimization
- Lines: 26
- Digest: 377eddcdb2cce178

### adversarial: needs-attention

1. **P3** `tests/Berth.Tests/Desktop/AutoArrangeTests.cs:24` compares readings taken from two separately opened desktop views. Pause the test after line 18, toggle **View > Auto arrange icons** on a disposable desktop, then resume. Both shell APIs agree on the new state, but line 24 fails because `publicValue` holds the old state. Keep the live agreement assertion within one view acquisition. The unit tests already cover the public getter.

### integration: approve

### source-defect: needs-attention

2. **P3** `todo/02-desktop/TODO-01-desktop-access.md:167` accepts a run that skips the only test of the real setter. This matters because Microsoft documents `FVO_CUSTOMPOSITION` as a prerequisite for `SetCurrentFolderFlags` on supported Windows versions. The call at `src/Berth/Desktop/ShellDesktopIcons.cs:89` relies on Explorer already supplying that option. Run the checkpoint with `BERTH_TEST_ALLOW_AUTOARRANGE` unset and it can pass without testing that assumption. Record one opt-in run on a disposable Windows 11 desktop that starts with auto arrange on and verifies the on/off/on transition. I did not run that test or observe a setter failure. [Microsoft’s `SetCurrentFolderFlags` remarks](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setcurrentfolderflags)

### consistency: approve

### quality: needs-attention

3. **P3** `tests/Berth.Tests/Desktop/AutoArrangeTests.cs:20` can report agreement between two shell APIs after exercising only one. On the supported `View2 == null` path, both calls invoke `IFolderView.GetAutoArrange()`. The existing `FakeFolderViewOnly(S_OK)` reproduces this: both tuple values are `true` and the assertions pass. Require a non-null `View2` for the comparison, or explicitly skip it when the second interface is unavailable.

### optimization: approve

LENS adversarial: findings  
LENS integration: clean  
LENS source-defect: findings  
LENS consistency: clean  
LENS quality: findings  
LENS optimization: clean  
HIGHEST: P3  
OVERALL: needs-attention


## Writer note: Round 1 fixes

- Finding 1 (P3, two-view race in `LiveAutoArrangeReadsWithoutThrowingAndAgreesWithFallback`): fixed in `tests/Berth.Tests/Desktop/AutoArrangeTests.cs` by exercising `icons.AutoArrange` without throwing and comparing `ReadAutoArrange(desktop.View, desktop.View2)` against `ReadAutoArrange(desktop.View, null)` within a single `DesktopView` acquisition.
- Finding 3 (P3, `View2 == null` in live agreement test): fixed in `tests/Berth.Tests/Desktop/AutoArrangeTests.cs` by asserting `Assert.NotNull(desktop.View2)` before comparing the two shell APIs.
- Finding 2 (P3, opt-in live setter test skipped by default): intentional per `AGENTS.md` ("Never change the flag in an automated test on the real desktop"); running with `BERTH_TEST_ALLOW_AUTOARRANGE=1` requires a disposable desktop where auto arrange is already enabled and cannot be run unattended on the owner's desktop.

## Completion review: astra (`gpt-6.2-astra` via `codex`)

- Writer: agy
- Run: 2026-11-20T10:37:21Z
- Candidate: 50c6acfd41bdf18f4675de067e4d00b37a2bfa22
- Effort: low
- Lenses: plan-delivery, record
- Lines: 22
- Digest: b3eb6f9a5b417205

## Part 1. Completeness

1. `present` The getter reads the auto-arrange bit independently of unrelated flags and falls back to `IFolderView.GetAutoArrange()` when `IFolderView2` is unavailable. Tests cover both states and a failing HRESULT. The live test compares both APIs on one acquired view. See `ShellDesktopIcons.cs:48` and `AutoArrangeTests.cs:13`, `:83`, `:100`, `:117`.
2. `present` The setter clears only `FWF_AUTOARRANGE`, reads the flags back, and returns `false` if the bit stays set or `IFolderView2` is unavailable. Tests assert the exact mask, value and read-back call. See `ShellDesktopIcons.cs:81` and `AutoArrangeTests.cs:138`, `:156`, `:175`.
3. `present` Both public members use the shared STA worker. Injected failures during the initial read, fallback read, write and verification read preserve the cause in `DesktopUnavailableException`. The tests check that the view is released once on the worker. See `ShellDesktopIcons.cs:42`, `AutoArrangeTests.cs:203` and `StaWorkerTests.ShellDesktopIconsRoutesAllPublicMembersThroughSharedWorkerBeforeDisposal`.
4. `present` The default live test only reads. The opt-in test requires an interactive desktop with auto arrange already on, independently verifies the clear, and restores and verifies the original bit in `finally`. Commit `50c6acf` reports the default checkpoint as 74 passed, 1 skipped, 0 failed. I inspected that record without rerunning the tests. See `AutoArrangeTests.cs:31`.
5. `missing-in-scope` The real Explorer write remains unproven. The plan review required one disposable-desktop run, but the implementation commit claimed all five findings resolved after skipping the only test that exercises the real setter. The writer note explains why the owner's desktop was unsuitable and gives neither a successful result nor an owner for the outstanding run. See `docs/reviews/02-desktop/D02-T01-s5.md:11` and `:70`, and commit `9ad4ab1`.

## Part 2. Excellence

**Alternative:** Windows Explorer exposes the setting directly under **View > Auto arrange icons**, where the user can inspect and toggle its checkmark. Berth's user-facing notice belongs to `D05 T01 §7` (`todo/02-desktop/TODO-01-desktop-access.md:24`).

1. `in-scope` Berth has yet to demonstrate that its clear operation delivers the same observable result as Explorer's toggle. The measurable target is one passing `OptInRoundTripTurnsAutoArrangeOffReadsBackAndRestores` run on a disposable Windows 11 desktop, with the flag observed on, then off, then restored to on. This is the same evidence gap as Part 1, item 5.

## Gap list

1. `missing-in-scope` / `in-scope` · **P1** · Record one passing real Explorer on/off/on round trip. Until then, keep the live-write finding open with a named owner.

LENS plan-delivery: findings  
LENS record: findings  
HIGHEST: P1  
COMPLETION: gaps


- Filed: D99 T01 §4 (real Explorer on/off/on auto arrange round trip on a disposable Windows 11 desktop; cannot run unattended on the owner's desktop per `AGENTS.md`)

## Reconciliation review: astra (`gpt-6.2-astra` via `codex`)

- Writer: agy
- Run: 2026-11-20T10:41:05Z
- Candidate: eb4b419c3147acc510727010eeb738beec9acb6e
- Effort: low
- Lenses: adversarial, integration, plan-delivery, record
- Lines: 9
- Digest: c5810748856d420e

1. **open (P1)**. The completion review requires one passing real Explorer on/off/on round trip on a disposable Windows 11 desktop (`docs/reviews/02-desktop/D02-T01-s5.md:98`). The supplied gate passed 74 tests and explicitly skipped `OptInRoundTripTurnsAutoArrangeOffReadsBackAndRestores`. The writer recorded `- Filed:` at `docs/reviews/02-desktop/D02-T01-s5.md:106`. The repair contains neither a passing run nor an `- Implemented:` entry.
2. **New finding (P1)**. The repair changes the reviewed section by marking that proof `Deferred` at `todo/02-desktop/TODO-01-desktop-access.md:150`. The operator section resolves, but its row at `todo/99-manual/TODO-01-operator-steps.md:39` makes it depend on §5 finishing first. That creates a circular acceptance condition. Restore the original §5 contract and record the passing disposable-desktop run under §5 before stamping it.

LENS adversarial: clean  
LENS integration: clean  
LENS plan-delivery: findings  
LENS record: findings  
HIGHEST: P1  
RECONCILIATION: gaps
