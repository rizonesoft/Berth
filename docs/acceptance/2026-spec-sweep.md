# Berth 2026 SPEC Acceptance Sweep

- **Gate command:** `pwsh scripts/check-all.ps1 -Published`
- **Tested commit:** `1a1498b64e8908720e0f4b0da3ceabf3ce0cbb13`
- **Gate result:** `passed` (`check-all: 8 step(s) passed`: 251 tooling tests OK with 8 skipped, 33 script tests OK, Release build 0 warnings/0 errors, 995/997 Release tests succeeded with 2 expected skips, published `artifacts/berth.exe` 75.05 MB, Published 1/1, RealDesktop 17/18 with 1 opt-in skip owned by `D99 T01 §4`, Smoke 3/3)
- **SPEC scope:** every bullet, table row, normative prose paragraph, and schema block in `design/SPEC.md` §1 to §20 (`180` items verified by `scripts/spec-items.py --check docs/acceptance/2026-spec-sweep.md`).

## Cross-references for SPEC §1 and §20

- **SPEC §1 (Policy: tokens only, Fluent controls, fidelity, copy):** enforced by `tests/Berth.Tests/Theme/NoHexColourTests.cs`, `scripts/tests/test_design_lint.py`, `scripts/tests/test_build_tokens.py`, `tests/Berth.Tests/Window/ContrastAndScaleTests.cs`, and `tests/Berth.Tests/Release/DocsRenderTests.cs`.
- **SPEC §20 (Out of scope):** documented honestly in `README.md` (`## What does it not do?`) and verified by `scripts/tests/test_readme.py` plus architecture boundary rules in `tests/Berth.Tests/Architecture/LayerRuleTests.cs`.

## SPEC §1 to §20 item-by-item sweep

| Ref | Requirement | Proof | Status |
| --- | --- | --- | --- |
| §1.1 [2c6b3312] | Design first: spec leads code | `P1.1` | yes |
| §1.2 [ace61f9c] | Tokens only: zero hex literals | `P1.2` | yes |
| §1.3 [4ab8d6c2] | Fluent controls stay Fluent | `P1.3` | yes |
| §1.4 [230024a5] | Every §19 state is drawn | `P1.4` | yes |
| §1.5 [9cbd37bb] | Never colour alone (word + shape) | `P1.5` | yes |
| §1.6 [4f9751d1] | Fidelity light/dark/HC 100%/150% | `P1.6` | yes |
| §1.7 [94ef95b7] | Dated deviation policy | `P1.7` | yes |
| §1.8 [2d59d7a5] | Copy rules & explicit icon counts | `P1.8` | yes |
| §2.1 [077d3810] | Brand assets in assets/ | `P2.1` | yes |
| §2.2 [1b86aa3a] | App icon & dark/light tray icons | `P2.2` | yes |
| §2.3 [7f0942c7] | tokens.json & generated Theme XAML | `P2.3` | yes |
| §2.4 [9ef15e84] | High contrast system colour tokens | `P2.4` | yes |
| §3.1 [47c907aa] | System theme light/dark/HC live swap | `P3.1` | yes |
| §3.2 [c3043bab] | PerMonitorV2 DPI & physical pixels | `P3.2` | yes |
| §3.3 [c1d549b4] | Tab order, focus, 32px/19px targets | `P3.3` | yes |
| §3.4 [b02113b3] | Text wraps; name ellipsis + tooltip | `P3.4` | yes |
| §3.5 [89d089bb] | Relative & absolute date formatting | `P3.5` | yes |
| §3.6 [b7a3c8ab] | Monitor sizes formatting | `P3.6` | yes |
| §3.7 [e4e5c268] | Never moves icons unrequested | `P3.7` | yes |
| §3.8 [4f988ab2] | Writes only Berth data, Run, flag | `P3.8` | yes |
| §4.1 [77be23e2] | Title, 980x640/760x520, placement | `P4.1` | yes |
| §4.2 [924cc43d] | Two panes 250px + 1px divider | `P4.2` | yes |
| §4.3 [2a2f65f2] | Close hides to tray; Esc ignored | `P4.3` | yes |
| §4.4 [e3cb0e66] | Open from tray, 2nd launch, 1st run | `P4.4` | yes |
| §4.5 [2e6fd4af] | 5s desktop poll visible; idle hidden | `P4.5` | yes |
| §5.1 [d41a0f33] | Heading Layouts bodyStrong | `P5.1` | yes |
| §5.2 [94d3c590] | Sorted rows with monitor/icon count | `P5.2` | yes |
| §5.3 [4a249dbb] | Active layout carries Active badge | `P5.3` | yes |
| §5.4 [0ae24708] | Unsaved arrangement first row | `P5.4` | yes |
| §5.5 [e2c467d0] | Single selection + 3px accent bar | `P5.5` | yes |
| §5.6 [980a805e] | Unreadable layout omitted + notice | `P5.6` | yes |
| §5.7 [6fec9a18] | Bottom switches + version caption | `P5.7` | yes |
| §6.1 [6f2f2fc9] | Header title or Not saved yet | `P6.1` | yes |
| §6.2 [85530ea0] | Subtitle monitor sizes + saved date | `P6.2` | yes |
| §6.3 [443b9249] | Active header Restore, Save, More | `P6.3` | yes |
| §6.4 [3f24b472] | Unsaved header Save only | `P6.4` | yes |
| §6.5 [383131ea] | Other arrangement More + caption | `P6.5` | yes |
| §6.6 [c17f7615] | Save current layout & previous kept | `P6.6` | yes |
| §6.7 [96d1555c] | Restore icons & status line update | `P6.7` | yes |
| §6.8 [e4b34b70] | More menu Rename/Revert/Delete | `P6.8` | yes |
| §6.9 [48f137aa] | Buttons disabled & Working state | `P6.9` | yes |
| §7.1 [18744270] | Map panel min height & 20px grid | `P7.1` | yes |
| §7.2 [6d83b4ff] | Monitor rects scaled, padded, labels | `P7.2` | yes |
| §7.3 [ccf5aa82] | Icon markers 11px square 3px radius | `P7.3` | yes |
| §7.4 [de58e513] | Active layout marker comparison | `P7.4` | yes |
| §7.5 [435dac80] | In-place marker & readout word | `P7.5` | yes |
| §7.6 [7f26f00f] | Moved marker, line & readout word | `P7.6` | yes |
| §7.7 [7a26d59e] | Missing dashed marker & readout | `P7.7` | yes |
| §7.8 [be961fea] | Neutral markers for other/unsaved | `P7.8` | yes |
| §7.9 [e7df31e6] | Tooltip, select ring, dashed drop | `P7.9` | yes |
| §7.10 [39ee807a] | Readout name/coords/default text | `P7.10` | yes |
| §7.11 [df14f18a] | Bottom-right in-place/icon count | `P7.11` | yes |
| §7.12 [f5afaaf8] | UIA list & marker reading order | `P7.12` | yes |
| §7.13 [5d2d267e] | Off-screen clamp & border inset | `P7.13` | yes |
| §8.1 [7287565b] | Status card square, sentence, right | `P8.1` | yes |
| §8.2 [09413348] | Status: Working | `P8.2` | yes |
| §8.3 [d8b1c619] | Status: Restored + Undo | `P8.3` | yes |
| §8.4 [1dc5ad1a] | Status: Undone + optional Undo | `P8.4` | yes |
| §8.5 [f731eff2] | Status: Saved / previous kept | `P8.5` | yes |
| §8.6 [b7512d16] | Status: Reverted to save | `P8.6` | yes |
| §8.7 [e5b0c8da] | Status: Refused/failed + Retry | `P8.7` | yes |
| §8.8 [e3fb6c7d] | Status: Every icon in place | `P8.8` | yes |
| §8.9 [92676767] | Status: Moved and missing | `P8.9` | yes |
| §8.10 [1b9363a5] | Status: Moved only | `P8.10` | yes |
| §8.11 [be9fae3a] | Status: Missing only | `P8.11` | yes |
| §8.12 [52567266] | Status: Unsaved arrangement | `P8.12` | yes |
| §8.13 [15498590] | Status: Another arrangement | `P8.13` | yes |
| §8.14 [f1370863] | Partial restore sentence & order | `P8.14` | yes |
| §8.15 [a93f451b] | Partial undo sentence & singular | `P8.15` | yes |
| §8.16 [c050a2eb] | Singular wording & clear rules | `P8.16` | yes |
| §9.1 [e00df1b0] | Notice stacking most serious first | `P9.1` | yes |
| §9.2 [b9de0902] | Auto arrange warning notice | `P9.2` | yes |
| §9.3 [b17029c0] | Desktop unreachable error notice | `P9.3` | yes |
| §9.4 [f5c402dc] | Unreadable layout error notice | `P9.4` | yes |
| §9.5 [60791535] | Turn it off clears/reads auto arrange | `P9.5` | deferred (D99 T01 §4) |
| §9.6 [93232d61] | Auto arrange disables Restore only | `P9.6` | yes |
| §9.7 [eeb4ae84] | Unreachable disables Save & Restore | `P9.7` | yes |
| §9.8 [886cb96d] | Open folder selects file in Explorer | `P9.8` | yes |
| §10.1 [baacc7b0] | Rename dialog, 1-40 chars, unique | `P10.1` | yes |
| §10.2 [39636615] | Revert swaps saves, moves no icon | `P10.2` | yes |
| §10.3 [d64cdef3] | Delete dialog & selects unsaved | `P10.3` | yes |
| §10.4 [6f963fc9] | Disk write & read-back before UI | `P10.4` | yes |
| §11.1 [e9df808f] | Two bottom switches without On/Off | `P11.1` | yes |
| §11.2 [e738e2fb] | Restore automatically off default | `P11.2` | yes |
| §11.3 [8f897ce3] | Start with Windows HKCU Run value | `P11.3` | yes |
| §11.4 [eaab04b6] | Switch write, read-back, rollback | `P11.4` | yes |
| §12.1 [473391d2] | Tray icon dark/light taskbar swap | `P12.1` | yes |
| §12.2 [b6625ada] | Tray tooltip saved or no layout | `P12.2` | yes |
| §12.3 [2f9111bc] | Left/double click opens main window | `P12.3` | yes |
| §12.4 [0f9504f4] | Right-click menu exact item order | `P12.4` | yes |
| §12.5 [833c6616] | Save/Restore menu enable rules | `P12.5` | yes |
| §12.6 [073236b0] | Exit removes icon & ends process | `P12.6` | yes |
| §12.7 [f7581bb6] | TaskbarCreated re-adds tray icon | `P12.7` | yes |
| §12.8 [8549b0be] | Failure balloon & 255-char trim | `P12.8` | deferred (D99 T01 §4) |
| §13.1 [b3386690] | CLI runs, prints, exits headless | `P13.1` | yes |
| §13.2 [d7542d82] | berth save [--name <name>] | `P13.2` | yes |
| §13.3 [fb03b105] | berth restore | `P13.3` | yes |
| §13.4 [b9e42c07] | berth undo | `P13.4` | yes |
| §13.5 [9616ed17] | berth list | `P13.5` | yes |
| §13.6 [e23565b8] | berth --help, -h, /? | `P13.6` | yes |
| §13.7 [1ef00f7e] | berth --version | `P13.7` | yes |
| §13.8 [5c51a851] | berth list columns & empty text | `P13.8` | yes |
| §13.9 [fae8038f] | --name forms & empty value rules | `P13.9` | yes |
| §13.10 [5aad0410] | Parser usage diagnostics (exit 2) | `P13.10` | yes |
| §13.11 [4d41ac36] | Restore missing icons prints, exit 0 | `P13.11` | yes |
| §13.12 [8d8530a3] | Did-not-stay stderr, exit 1, keep | `P13.12` | yes |
| §13.13 [cb59ed29] | Partial undo stdout/stderr exits | `P13.13` | yes |
| §13.14 [1cfb160f] | Control/newline sanitization | `P13.14` | yes |
| §13.15 [1ba7d814] | stdout/stderr & 2-line usage err | `P13.15` | yes |
| §13.16 [988d6862] | Exit code 0 Done | `P13.16` | yes |
| §13.17 [28f75623] | Exit code 1 Error | `P13.17` | yes |
| §13.18 [ee013c26] | Exit code 2 Wrong usage | `P13.18` | yes |
| §13.19 [299e4119] | Exit code 3 Refused | `P13.19` | yes |
| §13.20 [d443256d] | Exit 3 Busy & UnreadableLayout | `P13.20` | yes |
| §13.21 [c5ea042f] | list reports unreadable, exits 0 | `P13.21` | yes |
| §13.22 [11f4d33b] | Detached console manifest Win11 24H2 | `P13.22` | yes |
| §14.1 [10364d5c] | Data/local roots & BERTH_DATA | `P14.1` | yes |
| §14.2 [54885e09] | Test mode env vars scoped | `P14.2` | yes |
| §14.3 [0c62717b] | layouts/<key>.json per arrangement | `P14.3` | yes |
| §14.4 [c2452336] | Layout JSON schema v1 | `P14.4` | yes |
| §14.5 [a3e306ba] | Example key c7f017905ce518e6 | `P14.5` | yes |
| §14.6 [61e61454] | id, name, previous schema fields | `P14.6` | yes |
| §14.7 [9fe743a4] | settings.json schema & window px | `P14.7` | yes |
| §14.8 [fb63b1ca] | undo.json snapshot & delete on undo | `P14.8` | yes |
| §14.9 [444adcf9] | logs/berth.log under local root | `P14.9` | yes |
| §14.10 [5f2ec81e] | Temp write, flush, read-back, swap | `P14.10` | yes |
| §14.11 [9a0b29ff] | Invalid JSON untouched & skipped | `P14.11` | yes |
| §14.12 [e1d06847] | 1 to 64 monitors schema bound | `P14.12` | yes |
| §14.13 [d0ebcbfd] | Locked in-place save & redo note | `P14.13` | yes |
| §14.14 [7a24bb14] | Unreadable undo.json treated missing | `P14.14` | yes |
| §14.15 [f2add737] | SHA-256 16-hex arrangement key | `P14.15` | yes |
| §15.1 [9e90d4e6] | Read icons via IFolderView only | `P15.1` | yes |
| §15.2 [b11a3063] | Save naming & previous rotation | `P15.2` | yes |
| §15.3 [2f1dd597] | Restore refusal order & read-back | `P15.3` | yes |
| §15.4 [b1833ca3] | In place exact read-back match | `P15.4` | yes |
| §15.5 [3f64919c] | Undo checks key, deletes on full | `P15.5` | yes |
| §15.6 [0ea223e6] | Physical pixel coordinate roundtrip | `P15.6` | yes |
| §15.7 [7e151644] | Match icons by parsing name CI | `P15.7` | yes |
| §15.8 [c780a142] | 5s operation lock across tray/CLI | `P15.8` | yes |
| §16.1 [ab840655] | Tray only while Restore auto is on | `P16.1` | yes |
| §16.2 [5a40f065] | WM_DISPLAYCHANGE/TaskbarCreated 3s | `P16.2` | deferred (D99 T01 §2) |
| §16.3 [f45fdf73] | No layout does & saves nothing | `P16.3` | yes |
| §16.4 [4ee9a9bb] | WARN/FAIL/INFO log & balloon trigger | `P16.4` | yes |
| §16.5 [7f7a620d] | Visible window status + Undo | `P16.5` | yes |
| §17.1 [0a4852f8] | First run opens window; later tray | `P17.1` | yes |
| §17.2 [472dc3f7] | Single instance pipe activates open | `P17.2` | yes |
| §17.3 [855386a9] | CLI runs alongside tray; poll sees | `P17.3` | yes |
| §17.4 [4e141acf] | Unknown arg exits 2, no tray start | `P17.4` | yes |
| §18.1 [02380469] | Every refusal/failure has 1 message | `P18.1` | yes |
| §18.2 [f03b960a] | §18 msg: Auto arrange is on | `P18.2` | yes |
| §18.3 [bab0ac05] | §18 msg: No layout for this arrangement | `P18.3` | yes |
| §18.4 [c780542e] | §18 msg: No previous save | `P18.4` | yes |
| §18.5 [60de514c] | §18 msg: The desktop cannot be reached | `P18.5` | yes |
| §18.6 [ff8a03e5] | §18 msg: The desktop is lost while placing icons | `P18.6` | yes |
| §18.7 [a2d67f36] | §18 msg: The displays changed mid-operation | `P18.7` | yes |
| §18.8 [f3148959] | §18 msg: Nothing to undo | `P18.8` | yes |
| §18.9 [cf0aa620] | §18 msg: Icons did not stay after a restore or undo | `P18.9` | yes |
| §18.10 [61afc81f] | §18 msg: Another operation is running | `P18.10` | yes |
| §18.11 [7be11449] | §18 msg: A file cannot be written | `P18.11` | yes |
| §18.12 [4e69599e] | §18 msg: A folder cannot be read | `P18.12` | yes |
| §18.13 [9d13933f] | §18 msg: A file cannot be read | `P18.13` | yes |
| §18.14 [765f638e] | §18 msg: The settings file cannot be read | `P18.14` | yes |
| §18.15 [6d30f445] | §18 msg: Auto arrange would not turn off | `P18.15` | yes |
| §18.16 [9bc3e9ab] | §18 msg: The `Run` value cannot be written | `P18.16` | yes |
| §18.17 [e02ba2da] | §18 msg: Anything else | `P18.17` | yes |
| §18.18 [11017d24] | berth.log format INFO/WARN/FAIL | `P18.18` | yes |
| §18.19 [acadd001] | Unreadable settings.json WARN | `P18.19` | yes |
| §18.20 [5212e53f] | Setting change INFO/FAIL log lines | `P18.20` | yes |
| §18.21 [fcb61bf3] | Window edit logs & reason trimming | `P18.21` | yes |
| §18.22 [7b270322] | 1 MB log rotation & [cut] cap | `P18.22` | yes |
| §18.23 [0d028262] | Unhandled exception log, box, exit 1 | `P18.23` | yes |
| §18.24 [db2176d4] | No previous save revert message | `P18.24` | yes |
| §19.1 [ffefec80] | Layouts pane 5 states rendered | `P19.1` | yes |
| §19.2 [aa0b84e6] | Detail header 4 states rendered | `P19.2` | yes |
| §19.3 [4270ccc2] | Position map 8 states rendered | `P19.3` | yes |
| §19.4 [f7fb3245] | Status line all §8 states rendered | `P19.4` | yes |
| §19.5 [1c02cd79] | Notices none/single/stacked | `P19.5` | yes |
| §19.6 [3b3d0892] | Dialogs Rename errors & Delete | `P19.6` | yes |
| §19.7 [7d0ed768] | Tray dark/light, menu, checkmark | `P19.7` | yes |
| §20.1 [b96f508d] | Out-of-scope exclusions enforced | `P20.1` | yes |

## Proof catalog

- `P1.1`: `scripts/tests/test_spec_items.py::test_spec_policy_dated_clarifications_and_deviations_have_dates_and_todo_refs`, `scripts/tests/test_spec_items.py::test_modifying_existing_spec_row_without_updating_sweep_fails`
- `P1.2`: `tests/Berth.Tests/Theme/NoHexColourTests.cs::SourceTreeContainsNoHexColoursOrColorFromRgbCalls`, `scripts/tests/test_design_lint.py::test_auto_strict_for_token_contract_markers`
- `P1.3`: `tests/Berth.Tests/App/BerthApplicationTests.cs::StartupMergesTokensWithFluentThemeRestylesLiveControlAndPreservesUnrelatedDictionaries`, `tests/Berth.Tests/App/BerthApplicationTests.cs::ProductionControlsUseFluentStylesWithoutLocalTemplateOverrides`
- `P1.4`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::RendersAllFiveSpec19LayoutsPaneStatesInLightAndDarkDifferentFromEmptyFrame`, `tests/Berth.Tests/Window/MarkerTests.cs::RendersAllEightSpec19MapStatesInLightAndDark`, `tests/Berth.Tests/Window/StatusLineTests.cs::RendersAllSpec8StatusRowsInLightAndDark`
- `P1.5`: `tests/Berth.Tests/Window/MarkerTests.cs::TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine`
- `P1.6`: `tests/Berth.Tests/Window/ContrastAndScaleTests.cs::RendersDataHighContrastWithOnlyStandInSystemColoursAndData150At1470x960`, `tests/Berth.Tests/Window/ContrastAndScaleTests.cs::MinimumSize760x520FitsWorstCaseStringsWithoutClippingAcrossAllThemesAndScales`
- `P1.7`: `scripts/tests/test_spec_items.py::test_spec_policy_dated_clarifications_and_deviations_have_dates_and_todo_refs`
- `P1.8`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`, `tests/Berth.Tests/Core/FormatsTests.cs::CountsFormatSingularAndPluralAndRejectOutOfRange`
- `P2.1`: `scripts/tests/test_readme.py::test_all_links_images_and_anchors_resolve`
- `P2.2`: `tests/Berth.Tests/App/TrayIconTests.cs::EmbeddedTrayIconsLoadFromAssemblyAndContain16PxFrame`, `tests/Berth.Tests/App/TrayIconTests.cs::TrayIconSelectsSmallIconSizeMatchesDarkAndLightAssetsAndSwitchesLive`
- `P2.3`: `scripts/tests/test_build_tokens.py::test_overlap_between_shared_and_color_keys_raises`, `tests/Berth.Tests/App/BerthApplicationTests.cs::UserPreferenceChangedSwitchesBetweenLightDarkAndHighContrastLive`
- `P2.4`: `tests/Berth.Tests/Theme/TokenKeysTests.cs::HighContrastBrushesFollowSystemColorsDynamically`
- `P3.1`: `tests/Berth.Tests/App/BerthApplicationTests.cs::SelectPicksHighContrastFirstThenLightOrDark`, `tests/Berth.Tests/App/BerthApplicationTests.cs::UserPreferenceChangedSwitchesBetweenLightDarkAndHighContrastLive`
- `P3.2`: `tests/Berth.Tests/Release/PublishedTests.cs::PublishedExecutableHasValidChecksumManifestIconAndRunsWithoutSharedDotnetRuntime`, `tests/Berth.Tests/Desktop/SetPositionsTests.cs::SetPositionsBatchesMatchedIconsIgnoringCaseFirstDesktopWinsConvertsToViewAndPreservesSelectionFlags`
- `P3.3`: `tests/Berth.Tests/Window/AccessibilityTests.cs::TabOrderTraversesForwardAndBackwardAcrossAllWindowStates`, `tests/Berth.Tests/Window/AccessibilityTests.cs::AutomationPeerTreeNamesEveryControlMoreMenuAndModalDialogs`, `tests/Berth.Tests/Window/AccessibilityTests.cs::InteractiveControlsMeet32PxMinimumAndMarkerTargetsAreExactly19PxSquare`
- `P3.4`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::MinWidthLongNameTrimsWithTooltipWrapsSubtitleAndMeets32PxControlHeight`, `tests/Berth.Tests/Window/StatusLineTests.cs::StatusLineWrapsLongSentenceCleanlyAtMinWidthHighContrastAnd150PercentScale`
- `P3.5`: `tests/Berth.Tests/Core/FormatsTests.cs::WhenUsesLocalDatesAcrossMidnightInNonUtcZones`, `tests/Berth.Tests/Core/FormatsTests.cs::WhenGivesYesterdayPrecedenceAcrossNewYearAndAddsYearOtherwise`, `tests/Berth.Tests/Core/FormatsTests.cs::AllFormatsUseInvariantEnglishRegardlessOfCurrentCulture`
- `P3.6`: `tests/Berth.Tests/Core/FormatsTests.cs::MonitorSizesOrdersAndJoinsWithMultiplicationSign`
- `P3.7`: `tests/Berth.Tests/Core/SaveTests.cs::AFirstSaveOnAFreshProfileWritesTheArrangementsLayout`, `tests/Berth.Tests/Core/UndoTests.cs::CanUndoLifecycleNeverTouchesTheDesktop`, `tests/Berth.Tests/Window/RevertDeleteTests.cs::RevertSwapsTwiceWithoutMovingDesktopShowsWorkingAndHandlesStaleRefusals`, `tests/Berth.Tests/Core/RestoreTests.cs::AnUnreachableDesktopIsRefusedFirstAndMovesNothing`
- `P3.8`: `tests/Berth.Tests/Core/BerthPathsTests.cs::ForRootPutsBothRootsUnderIt`, `tests/Berth.Tests/Desktop/RunValueTests.cs::SetWritesQuotedRegSzReadsBackAndDeletesCleanly`
- `P4.1`: `tests/Berth.Tests/Window/MainWindowFrameTests.cs::FrameUsesSpecTitleIconTokensDividerAndPreservesLeftPaneOnResize`, `tests/Berth.Tests/Window/WindowPlacementTests.cs::StoredPlacementAppliesViaSetWindowPosAndNullOrOffScreenCentersOnPrimaryWorkArea`
- `P4.2`: `tests/Berth.Tests/Window/MainWindowFrameTests.cs::FrameUsesSpecTitleIconTokensDividerAndPreservesLeftPaneOnResize`
- `P4.3`: `tests/Berth.Tests/Window/MainWindowFrameTests.cs::ClosingRaisesClosingEventHidesWindowAndKeepsItReusableByShow`, `tests/Berth.Tests/Window/MainWindowFrameTests.cs::EscapeKeyDoesNotHideOrCloseWindow`
- `P4.4`: `tests/Berth.Tests/App/StartupPlanTests.cs::FirstRunPersistsDefaultSettingsOnlyAfterWindowOpenedCallbackAndSecondStartDoesNotOpenWindow`, `tests/Berth.Tests/App/StartupPlanTests.cs::SecondBerthExeWithSameInstanceSignalsPrimaryAndExits0Within10Seconds`
- `P4.5`: `tests/Berth.Tests/Window/DesktopPollerTests.cs::PollerReadsOnStartAndTickSkipsOverlapIgnoresPostStopTicksAndTriggersAndRestartsCleanly`
- `P5.1`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker`
- `P5.2`: `tests/Berth.Tests/Window/MainViewModelTests.cs::RowsFollowSpecOrderForNoLayoutsActiveLayoutAndUnsavedBesideSaved`, `tests/Berth.Tests/Core/FormatsTests.cs::CountsFormatSingularAndPluralAndRejectOutOfRange`
- `P5.3`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker`
- `P5.4`: `tests/Berth.Tests/Window/MainViewModelTests.cs::RowsFollowSpecOrderForNoLayoutsActiveLayoutAndUnsavedBesideSaved`
- `P5.5`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker`, `tests/Berth.Tests/Window/AccessibilityTests.cs::ArrowHomeEndKeysNavigateBothListsAndMapPreservesFocusAcrossRefresh`
- `P5.6`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::CorruptCurrentLayoutFileIsOmittedFromListBoxAndExposedOnUnreadable`
- `P5.7`: `tests/Berth.Tests/Window/SettingsTests.cs::HostedSwitchesToggleOnAndOffPersistToDiskAndIsolatedRegistryAndSyncToTrayMenu`, `tests/Berth.Tests/AppInfoTests.cs::VersionIsThreeNumbers`
- `P6.1`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.2`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.3`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.4`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.5`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.6`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::BoundSaveAndRestoreButtonsWriteLayoutMoveFakeDesktopDisableAllThreeWhileBusyAndPreventReentry`, `tests/Berth.Tests/Core/SaveTests.cs::ALaterSaveKeepsTheNameAndMovesTheEarlierSaveIntoPrevious`
- `P6.7`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::BoundSaveAndRestoreButtonsWriteLayoutMoveFakeDesktopDisableAllThreeWhileBusyAndPreventReentry`, `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P6.8`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::MoreMenuListsThreeItemsInSpecOrderAndEnablesRevertOnlyWithPreviousSave`
- `P6.9`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::BoundSaveAndRestoreButtonsWriteLayoutMoveFakeDesktopDisableAllThreeWhileBusyAndPreventReentry`, `tests/Berth.Tests/Window/DetailHeaderTests.cs::TrayOperationDisablesOpenWindowHeaderButtonsUntilComplete`
- `P7.1`: `tests/Berth.Tests/Window/PositionMapTests.cs::ResizesProportionallyKeeps220PxMinCardHeightAndKeepsPortraitAndStackedLabelsInsideCard`
- `P7.2`: `tests/Berth.Tests/Window/MapGeometryTests.cs::FitTwoSideBySideMonitorsCentersHorizontallyAndPreservesProportions`, `tests/Berth.Tests/Window/MapGeometryTests.cs::FormatMonitorLabelFormatsNumberDimensionsAndPrimarySuffix`
- `P7.3`: `tests/Berth.Tests/Window/MarkerTests.cs::TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine`
- `P7.4`: `tests/Berth.Tests/Core/LayoutComparisonTests.cs::EachSavedIconIsInPlaceMovedOrMissing`
- `P7.5`: `tests/Berth.Tests/Window/MarkerTests.cs::TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine`
- `P7.6`: `tests/Berth.Tests/Window/MarkerTests.cs::TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine`
- `P7.7`: `tests/Berth.Tests/Window/MarkerTests.cs::TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine`
- `P7.8`: `tests/Berth.Tests/Window/PositionMapTests.cs::DrawsSelectedLayoutsMonitorsLabelsAndSavedPositionsAndSwitchesToUnsavedLiveMonitors`
- `P7.9`: `tests/Berth.Tests/Window/MarkerTests.cs::SelectionShowsRingDropLineAndReadoutSurvivesRefreshAndClearsOnLayoutSwitch`
- `P7.10`: `tests/Berth.Tests/Window/IconReadoutTests.cs::FormatsExactReadoutForEveryStateOffScreenNegativeCoordsAndNoneSelected`
- `P7.11`: `tests/Berth.Tests/Window/IconReadoutTests.cs::FormatsCountForComparedPartialNeutralUnsavedAndNull`
- `P7.12`: `tests/Berth.Tests/Window/AccessibilityTests.cs::MapMarkersOrderInReadingOrderAcrossTwoMonitorsAndSpeakExactSpec7Names`
- `P7.13`: `tests/Berth.Tests/Window/MapGeometryTests.cs::ClampProjectsOffScreenPointsOntoNearestMonitorInclusiveEdge`
- `P8.1`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P8.2`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P8.3`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`, `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.4`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`, `tests/Berth.Tests/Window/StatusLineTests.cs::PartialUndoOffersUndoAgainAndRetryRepeatsRefusedSaveAfterDesktopRecovers`
- `P8.5`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`, `tests/Berth.Tests/Core/MessagesTests.cs::StatusSavedFormatsCountsAndPreviousSaveClause`
- `P8.6`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`, `tests/Berth.Tests/Core/MessagesTests.cs::RevertedRenamedAndDeletedFormatForStatusAndLogAndRejectCli`
- `P8.7`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`, `tests/Berth.Tests/Window/StatusLineTests.cs::PartialUndoOffersUndoAgainAndRetryRepeatsRefusedSaveAfterDesktopRecovers`
- `P8.8`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P8.9`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P8.10`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P8.11`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P8.12`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P8.13`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P8.14`: `tests/Berth.Tests/Core/MessagesTests.cs::StatusAndCliRestoredMatchEverySpecCaseAndOrder`
- `P8.15`: `tests/Berth.Tests/Core/MessagesTests.cs::StatusCliAndLogUndoneMatchFullAndPartialRulesWithoutLayoutName`
- `P8.16`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::KeepsStickySentenceOnUnchangedRefreshAndClearsOnBusySelectionOrChangedComparison`
- `P9.1`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.2`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.3`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.4`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.5`: `tests/Berth.Tests/Window/NoticesTests.cs::TurnAutoArrangeOffClearsNoticeWhenOffAndShowsSpec18MessageWhenStillOn`
- `P9.6`: `tests/Berth.Tests/Window/NoticesTests.cs::OpenFolderPassesExactSecondFilePathAndHostedControlsRespectDisablesAndTooltips`
- `P9.7`: `tests/Berth.Tests/Window/NoticesTests.cs::RetryWaitsForInFlightStalePollAndClearsUnavailableNoticeImmediately`, `tests/Berth.Tests/Window/MarkerTests.cs::UnreachableDesktopShowsNeutralMarkersAndIconCountAndRecoversOnRefresh`
- `P9.8`: `tests/Berth.Tests/Window/NoticesTests.cs::OpenFolderPassesExactSecondFilePathAndHostedControlsRespectDisablesAndTooltips`
- `P10.1`: `tests/Berth.Tests/Window/RenameTests.cs::ViewModelValidatesEveryRuleLiveIncludingCaseInsensitiveDuplicateAndLiveOtherUpdates`, `tests/Berth.Tests/Window/RenameTests.cs::MoreMenuRenameModalHandlesOwnerSelectionInvalidEnterEscAndValidTrimmedRename`
- `P10.2`: `tests/Berth.Tests/Window/RevertDeleteTests.cs::RevertSwapsTwiceWithoutMovingDesktopShowsWorkingAndHandlesStaleRefusals`
- `P10.3`: `tests/Berth.Tests/Window/RevertDeleteTests.cs::DeleteDialogDefaultsToCancelAndCancelEscEnterAndClosePreserveExactFileBytes`, `tests/Berth.Tests/Window/RevertDeleteTests.cs::DeleteNonActiveSelectsActiveAndPreservesAdjacentAndDeleteActiveSelectsThisArrangement`
- `P10.4`: `tests/Berth.Tests/Core/LayoutEditTests.cs::DoubleRevertSwapsAndReadsBackBothStatesWithoutMovingIcons`
- `P11.1`: `tests/Berth.Tests/Window/SettingsTests.cs::RendersBothSwitchStatesInLightAndDarkAndVerifiesOppositeThumbPositionsAndHighContrast150`
- `P11.2`: `tests/Berth.Tests/Window/SettingsTests.cs::HostedSwitchesToggleOnAndOffPersistToDiskAndIsolatedRegistryAndSyncToTrayMenu`
- `P11.3`: `tests/Berth.Tests/Desktop/RunValueTests.cs::SetWritesQuotedRegSzReadsBackAndDeletesCleanly`
- `P11.4`: `tests/Berth.Tests/Window/SettingsTests.cs::FailedOnToOffWritesSnapHostedSwitchesBackLogSingleFailAndDeliverWhenBusyClears`
- `P12.1`: `tests/Berth.Tests/App/TrayIconTests.cs::TrayIconSelectsSmallIconSizeMatchesDarkAndLightAssetsAndSwitchesLive`
- `P12.2`: `tests/Berth.Tests/App/TrayIconTests.cs::TooltipFormatsActiveAndMissingLayoutsAndTruncatesCleanlyOnTextElementBoundaries`
- `P12.3`: `tests/Berth.Tests/App/TrayIconTests.cs::TrayIconRefreshesTooltipAfterSaveAndMonitorChangeAndFiltersClickButtons`
- `P12.4`: `tests/Berth.Tests/App/TrayMenuTests.cs::MenuHasSpecOrderSeparatorBoldDefaultAndSystemRenderer`
- `P12.5`: `tests/Berth.Tests/App/TrayMenuTests.cs::OpeningRefreshesRestoreEnabledAndAutoCheckedOnSameMenuInstance`
- `P12.6`: `tests/Berth.Tests/App/TrayMenuTests.cs::AttachedMenuSaveRefreshesTooltipAndExitDrainsInFlightOperationWithExitCodeZero`
- `P12.7`: `tests/Berth.Tests/App/TrayIconTests.cs::TaskbarCreatedReaddsTrayIconBeforeArmingExplorerRestartTimer`
- `P12.8`: `tests/Berth.Tests/App/BalloonTests.cs::TrayAppShowsOneWarningBalloonOnAutoArrangeAndPartialFailureSilentOnSuccessAndOpensWindowOnClick`, `tests/Berth.Tests/App/BalloonTests.cs::PolicyShortensOverlongMessagesWithin255CharactersPreservingReasonAndClosingGuidance`
- `P13.1`: `tests/Berth.Tests/App/StartupPlanTests.cs::CliCommandsAndUsageErrorsBypassTrayStartupAndDoNotLoadMalformedSettings`, `tests/Berth.Tests/Cli/ProcessTests.cs::SaveMoveRestoreUndoAndListCycleOnProbeIconMatchesSpec13EndToEnd`, `tests/Berth.Tests/Core/OperationLockTests.cs::AnotherProcessHoldingTheLockMakesRunBusyAndSeesItHeldDuringWork`
- `P13.2`: `tests/Berth.Tests/Cli/SaveCommandTests.cs::SaveDefaultNameWritesExactLineToStdoutAndPersistsLayout`, `tests/Berth.Tests/Cli/SaveCommandTests.cs::SaveWithExplicitNameNamesNewLayoutAndRenamesExistingKeepingPrevious`
- `P13.3`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreFullWritesExactLineToStdoutAndReturnsExit0`
- `P13.4`: `tests/Berth.Tests/Cli/UndoCommandTests.cs::RestoreThenUndoReturnsIconsToPreRestorePositionsDeletesSnapshotAndSecondUndoIsRefused`
- `P13.5`: `tests/Berth.Tests/Cli/ListCommandTests.cs::ListSingleActiveLayoutMatchesSpec13ExampleExactLine`
- `P13.6`: `tests/Berth.Tests/Cli/UsageTests.cs::UsageTextMatchesCompleteExpectedLinesForAllCommandsAndExitCodes`, `tests/Berth.Tests/Cli/UsageTests.cs::HelpAndVersionFormsWorkIdenticallyThroughCliRunnerAndProgramRunWithoutTouchingStorageOrDesktop`
- `P13.7`: `tests/Berth.Tests/Cli/UsageTests.cs::HelpAndVersionFormsWorkIdenticallyThroughCliRunnerAndProgramRunWithoutTouchingStorageOrDesktop`
- `P13.8`: `tests/Berth.Tests/Cli/ListCommandTests.cs::ListSingleActiveLayoutMatchesSpec13ExampleExactLine`, `tests/Berth.Tests/Cli/ListCommandTests.cs::ListEmptyFolderWritesNoLayoutsSavedYetAndReturnsExit0`
- `P13.9`: `tests/Berth.Tests/Cli/CliArgumentsTests.cs::ParseSaveAcceptsNameInSpaceAndEqualsFormsIncludingSpacesAndExplicitEmptyValue`, `tests/Berth.Tests/Cli/SaveCommandTests.cs::SaveInvalidNamesReturnExit2WithExactSpecMessagesAndEmptyStdout`
- `P13.10`: `tests/Berth.Tests/Cli/CliArgumentsTests.cs::ParseRejectsUnknownCommandsWrongCasePrefixesEmptyFirstArgAndUnknownFirstArgOptions`, `tests/Berth.Tests/Cli/CliArgumentsTests.cs::ParseRejectsBadOptionsMissingOrDuplicateNameAndExtraArguments`
- `P13.11`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestorePartialWithMissingIconWritesExactLineToStdoutAndReturnsExit0`
- `P13.12`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreNotPlacedWritesExactLineToStderrWithEmptyStdoutAndReturnsExit1`, `tests/Berth.Tests/Cli/UndoCommandTests.cs::UndoPlacementFailuresWriteExactLinesToStderrPreserveSnapshotBytesAndReturnExit1`
- `P13.13`: `tests/Berth.Tests/Cli/UndoCommandTests.cs::UndoPartialWithOneMissingIconWritesExactLineToStdoutDeletesSnapshotAndReturnsExit0`, `tests/Berth.Tests/Cli/UndoCommandTests.cs::UndoPlacementFailuresWriteExactLinesToStderrPreserveSnapshotBytesAndReturnExit1`
- `P13.14`: `tests/Berth.Tests/Core/MessagesTests.cs::MultilineNamesPathsOriginsAndReasonsCollapseLineBreaksAndTrailingPeriods`, `tests/Berth.Tests/Core/MessagesTests.cs::ControlCharactersInNamesPathsOriginsAndReasonsAreEscapedAsUnicodeHex`
- `P13.15`: `tests/Berth.Tests/Cli/UsageTests.cs::UsageErrorsPrintTwoLineDiagnosticToStderrWithEmptyStdoutAndExit2ThroughBothEntryPoints`
- `P13.16`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::ExitCodesMatchSpec13NumericContract`, `tests/Berth.Tests/Cli/SaveCommandTests.cs::SaveDefaultNameWritesExactLineToStdoutAndPersistsLayout`, `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreFullWritesExactLineToStdoutAndReturnsExit0`
- `P13.17`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::ExitCodesMatchSpec13NumericContract`, `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreNotPlacedWritesExactLineToStderrWithEmptyStdoutAndReturnsExit1`, `tests/Berth.Tests/Cli/SaveCommandTests.cs::SaveWriteFailureReturnsExit1WithExactStderrAndEmptyStdout`
- `P13.18`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::ExitCodesMatchSpec13NumericContract`, `tests/Berth.Tests/Cli/UsageTests.cs::UsageErrorsPrintTwoLineDiagnosticToStderrWithEmptyStdoutAndExit2ThroughBothEntryPoints`, `tests/Berth.Tests/Cli/SaveCommandTests.cs::SaveInvalidNamesReturnExit2WithExactSpecMessagesAndEmptyStdout`
- `P13.19`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::ExitCodesMatchSpec13NumericContract`, `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreRefusalsWriteExactSpec18MessagesToStderrWithEmptyStdoutAndReturnExit3`, `tests/Berth.Tests/Cli/UndoCommandTests.cs::UndoRefusalsWriteExactSpec18MessagesToStderrWithEmptyStdoutAndReturnExit3`
- `P13.20`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreRefusalsWriteExactSpec18MessagesToStderrWithEmptyStdoutAndReturnExit3`
- `P13.21`: `tests/Berth.Tests/Cli/ListCommandTests.cs::ListReportsUnreadableFilesToStderrStillListsReadableAndHandlesCorruptOnly`
- `P13.22`: `tests/Berth.Tests/Cli/ConsoleBehaviourTests.cs::DetachedLaunchAllocatesNoConsoleForBerthAndAllocatesOneWhenPolicyIsStripped`, `tests/Berth.Tests/Cli/ConsoleBehaviourTests.cs::InteractiveTerminalInheritsConsolePrintsVersionWaitsForHoldAndCapturesLiveExitCode`
- `P14.1`: `tests/Berth.Tests/Core/BerthPathsTests.cs::TheOverrideRedirectsEveryPathAndTurnsTestModeOn`, `tests/Berth.Tests/Core/BerthPathsTests.cs::WithoutAnOverrideThePathsAreTheUsersOwnAndTestModeIsOff`, `tests/Berth.Tests/Core/BerthPathsTests.cs::TheDefaultRootsComeFromTheGivenFolders`
- `P14.2`: `tests/Berth.Tests/Desktop/ScopedDesktopIconsTests.cs::SetPositionsRefreshesLiveScopeWhenProbeIsRemovedOrRenamedOutsidePrefix`, `tests/Berth.Tests/Cli/ConsoleBehaviourTests.cs::ApplyIgnoresReportAndHoldWhenTestModeIsOff`, `tests/Berth.Tests/Cli/ConsoleBehaviourTests.cs::ApplyWritesConsoleReportBeforeHoldingAndCapsHoldAtFiveSecondsWhenTestModeIsOn`
- `P14.3`: `tests/Berth.Tests/Core/LayoutStoreTests.cs::LayoutsSurviveInAFreshStoreWithEveryValue`
- `P14.4`: `tests/Berth.Tests/Core/LayoutJsonTests.cs::SpecExampleParsesToItsValues`, `tests/Berth.Tests/Core/LayoutJsonTests.cs::WritingGivesTheExpectedDocument`
- `P14.5`: `tests/Berth.Tests/Core/ArrangementKeyTests.cs::TheSpecExampleKeyIsItsMonitorsKey`
- `P14.6`: `tests/Berth.Tests/Core/LayoutJsonTests.cs::PreviousSaveParsesToItsValues`
- `P14.7`: `tests/Berth.Tests/Core/SettingsStoreTests.cs::TheDefaultsHaveTheSpecShape`, `tests/Berth.Tests/Core/SettingsStoreTests.cs::SettingsSurviveInAFreshStoreAndCanBeCleared`
- `P14.8`: `tests/Berth.Tests/Core/UndoTests.cs::ARestoreFollowedByAnUndoReturnsEveryIconWhereItWasAndDeletesTheSnapshot`
- `P14.9`: `tests/Berth.Tests/Core/LogTests.cs::ALineHasTheUtcTimeTheLevelAndTheMessage`
- `P14.10`: `tests/Berth.Tests/Core/JsonFileTests.cs::AWriteCanBeReadBackAndLeavesNoTemporaryFile`, `tests/Berth.Tests/Core/JsonFileTests.cs::ACorruptedReadBackIsRefusedAndTheOldFileStays`
- `P14.11`: `tests/Berth.Tests/Core/LayoutStoreTests.cs::ASaveOverAnUnreadableFileIsRefusedAndTheFileKept`, `tests/Berth.Tests/Core/SettingsStoreTests.cs::AnUnreadableFileExistsGivesTheDefaultsAndIsNeverReplaced`
- `P14.12`: `tests/Berth.Tests/Core/LayoutStoreTests.cs::ALayoutMayNameAtMost64Monitors`
- `P14.13`: `tests/Berth.Tests/Core/JsonFileTests.cs::ANotePendingDeletionDoesNotFailRecoveryOrNextWrite`
- `P14.14`: `tests/Berth.Tests/Core/RestoreTests.cs::AnUnreadableUndoFileIsReplaced`, `tests/Berth.Tests/Core/UndoTests.cs::InvalidOrForeignSnapshotsRefuseWithNothingToUndoAndDisableCanUndo`
- `P14.15`: `tests/Berth.Tests/Core/ArrangementKeyTests.cs::TheExpectedKeysAreTheShaOfTheHandWrittenStrings`, `tests/Berth.Tests/Core/ArrangementKeyTests.cs::ThreeMonitorsInAnyOrderHaveOneKey`, `tests/Berth.Tests/Core/ArrangementKeyTests.cs::TheCultureDoesNotChangeTheKey`, `tests/Berth.Tests/Core/ArrangementKeyTests.cs::AKeyIsSixteenLowercaseHexCharacters`
- `P15.1`: `tests/Berth.Tests/Desktop/ReadIconsTests.cs::ReadIconsAssignsExactSignedCoordinatesIncludingOffScreenAndPreservesRecycleBinId`
- `P15.2`: `tests/Berth.Tests/Core/SaveTests.cs::AFirstSaveOnAFreshProfileWritesTheArrangementsLayout`, `tests/Berth.Tests/Core/SaveTests.cs::ASecondArrangementWithAsManyMonitorsGetsTheNextName`, `tests/Berth.Tests/Core/SaveTests.cs::ALaterSaveKeepsTheNameAndMovesTheEarlierSaveIntoPrevious`
- `P15.3`: `tests/Berth.Tests/Core/RestoreTests.cs::AnUnreachableDesktopIsRefusedFirstAndMovesNothing`, `tests/Berth.Tests/Core/RestoreTests.cs::NoLayoutIsRefusedBeforeAutoArrange`, `tests/Berth.Tests/Core/RestoreTests.cs::OnlySavedIconsStillOnTheDesktopAreMovedToTheirSavedPositions`, `tests/Berth.Tests/Core/RestoreTests.cs::CountsComeFromTheDesktopReadBack`
- `P15.4`: `tests/Berth.Tests/Core/RestoreTests.cs::CountsComeFromTheDesktopReadBack`, `tests/Berth.Tests/Core/RestoreTests.cs::IconsWindowsMovedMakeTheRestoreAFailureWithItsCounts`
- `P15.5`: `tests/Berth.Tests/Core/UndoTests.cs::ARestoreFollowedByAnUndoReturnsEveryIconWhereItWasAndDeletesTheSnapshot`, `tests/Berth.Tests/Core/UndoTests.cs::IconsWindowsMovedMakeTheUndoAFailureAndKeepTheSnapshot`, `tests/Berth.Tests/Core/UndoTests.cs::InvalidOrForeignSnapshotsRefuseWithNothingToUndoAndDisableCanUndo`
- `P15.6`: `tests/Berth.Tests/Desktop/SetPositionsTests.cs::SetPositionsBatchesMatchedIconsIgnoringCaseFirstDesktopWinsConvertsToViewAndPreservesSelectionFlags`
- `P15.7`: `tests/Berth.Tests/Desktop/SetPositionsTests.cs::SetPositionsBatchesMatchedIconsIgnoringCaseFirstDesktopWinsConvertsToViewAndPreservesSelectionFlags`, `tests/Berth.Tests/Core/RestoreTests.cs::OnlySavedIconsStillOnTheDesktopAreMovedToTheirSavedPositions`
- `P15.8`: `tests/Berth.Tests/Core/OperationLockTests.cs::TheAppWaitsFiveSecondsOnALockSharedAcrossSessions`, `tests/Berth.Tests/Core/OperationLockTests.cs::ATimedOutRunIsBusyOnceAndOnlyAfterTheWait`
- `P16.1`: `tests/Berth.Tests/App/AutoRestoreTests.cs::OffSettingDoesNothingAndLogsNothingWhileRuntimeFlipToOnTakesEffectImmediately`
- `P16.2`: `tests/Berth.Tests/App/AutoRestoreTests.cs::FiveStaggeredTriggersInBurstProduceExactlyOneRestoreAfterLastDeadline`, `tests/Berth.Tests/App/AutoRestoreTests.cs::TrayAppRestoresOnDisplayChangeAndExplorerRestartHonoursRuntimeSettingOffAndDrainsCleanlyOnExit`
- `P16.3`: `tests/Berth.Tests/App/AutoRestoreTests.cs::NoLayoutForCurrentArrangementIsSilentCreatesNoLayoutAndLeavesUndoUntouched`
- `P16.4`: `tests/Berth.Tests/App/AutoRestoreTests.cs::SavedLayoutWithAutoArrangeOnDeliversNonSilentAutoArrangeRefusalWithSingleWarnLine`, `tests/Berth.Tests/App/BalloonTests.cs::PolicyReturnsNullForSuccessSuccessWithMissingAndNoLayout`, `tests/Berth.Tests/App/BalloonTests.cs::PolicyReturnsSpecTitleAndExactMessageForRefusalsFailuresAndPartialRestores`
- `P16.5`: `tests/Berth.Tests/App/AutoRestoreWindowTests.cs::OpenWindowShowsAutomaticRestoreResultUndoMapRefreshBusyStateTooltipAndHiddenGuard`
- `P17.1`: `tests/Berth.Tests/App/StartupPlanTests.cs::FirstRunPersistsDefaultSettingsOnlyAfterWindowOpenedCallbackAndSecondStartDoesNotOpenWindow`
- `P17.2`: `tests/Berth.Tests/App/SingleInstanceTests.cs::FirstClaimSucceedsSecondOnSameThreadFailsAndSignalFirstRaisesActivated`, `tests/Berth.Tests/App/StartupPlanTests.cs::SecondBerthExeWithSameInstanceSignalsPrimaryAndExits0Within10Seconds`
- `P17.3`: `tests/Berth.Tests/App/StartupPlanTests.cs::CliCommandsAndUsageErrorsBypassTrayStartupAndDoNotLoadMalformedSettings`, `tests/Berth.Tests/Cli/ProcessTests.cs::SaveMoveRestoreUndoAndListCycleOnProbeIconMatchesSpec13EndToEnd`
- `P17.4`: `tests/Berth.Tests/App/StartupPlanTests.cs::CliCommandsAndUsageErrorsBypassTrayStartupAndDoNotLoadMalformedSettings`, `tests/Berth.Tests/Cli/CliArgumentsTests.cs::ParseRejectsUnknownCommandsWrongCasePrefixesEmptyFirstArgAndUnknownFirstArgOptions`
- `P18.1`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.2`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.3`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.4`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.5`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.6`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.7`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.8`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.9`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.10`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.11`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.12`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.13`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.14`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.15`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.16`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.17`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P18.18`: `tests/Berth.Tests/Core/LogTests.cs::ALineHasTheUtcTimeTheLevelAndTheMessage`
- `P18.19`: `tests/Berth.Tests/App/StartupPlanTests.cs::UnreadableSettingsLogsOneWarnRunsOnDefaultsWithOpenWindowFalseAndPreservesFileBytes`
- `P18.20`: `tests/Berth.Tests/Core/SettingChangesTests.cs::ExhaustedConcurrentReplaceRetriesPreserveWinningFileAndLogFail`
- `P18.21`: `tests/Berth.Tests/Core/MessagesTests.cs::RevertedRenamedAndDeletedFormatForStatusAndLogAndRejectCli`, `tests/Berth.Tests/Core/MessagesTests.cs::MultilineNamesPathsOriginsAndReasonsCollapseLineBreaksAndTrailingPeriods`
- `P18.22`: `tests/Berth.Tests/Core/LogTests.cs::AnEventLargerThanTheCapIsCutToFit`
- `P18.23`: `tests/Berth.Tests/App/CrashHandlerTests.cs::HandleLogsIndentedStackAndInnerExceptionRedactsSourceAndShowsExactTwoLineRedactedMessage`, `tests/Berth.Tests/App/BerthApplicationTests.cs::AttachedCrashHandlerCatchesDispatcherExceptionLogsFailShowsDialogAndRequestsExit1`
- `P18.24`: `tests/Berth.Tests/Core/MessagesTests.cs::RefusalAndHelpersCoverEveryRowOfSpec18AndSpec10`
- `P19.1`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::RendersAllFiveSpec19LayoutsPaneStatesInLightAndDarkDifferentFromEmptyFrame`
- `P19.2`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::RendersAllFourSpec19HeaderStatesInLightAndDark`
- `P19.3`: `tests/Berth.Tests/Window/MarkerTests.cs::RendersAllEightSpec19MapStatesInLightAndDark`
- `P19.4`: `tests/Berth.Tests/Window/StatusLineTests.cs::RendersAllSpec8StatusRowsInLightAndDark`
- `P19.5`: `tests/Berth.Tests/Window/NoticesTests.cs::RendersAllFiveSpec19NoticeStatesInLightAndDark`
- `P19.6`: `tests/Berth.Tests/Window/RenameTests.cs::RendersAllFourSpec19RenameDialogStatesInLightAndDarkAndHighContrast150`, `tests/Berth.Tests/Window/RevertDeleteTests.cs::RendersDeleteDialogInLightAndDarkAndVerifies40CharNameAt150PercentHighContrast`
- `P19.7`: `tests/Berth.Tests/App/TrayIconTests.cs::TrayIconSelectsSmallIconSizeMatchesDarkAndLightAssetsAndSwitchesLive`, `tests/Berth.Tests/App/TrayMenuTests.cs::OpeningRefreshesRestoreEnabledAndAutoCheckedOnSameMenuInstance`

- `P20.1`: `scripts/tests/test_readme.py::test_readme_screenshots_badge_sections_and_limits`, `tests/Berth.Tests/App/AutoRestoreTests.cs::NoLayoutForCurrentArrangementIsSilentCreatesNoLayoutAndLeavesUndoUntouched`, `tests/Berth.Tests/Core/LayoutStoreTests.cs::LayoutsSurviveInAFreshStoreWithEveryValue`, `tests/Berth.Tests/Release/PublishedTests.cs::PublishedExecutableHasValidChecksumManifestIconAndRunsWithoutSharedDotnetRuntime`, `tests/Berth.Tests/Architecture/LayerRuleTests.cs::CoreUsesOnlyCoreAndTheBaseRuntime`

## Operator-owned live-hardware observations

Three rows above carry `deferred (D99 T01 §N)` because their automated unit/fake suites pass while live-hardware or unlocked-desktop observation requires the owner (each row stays deferred until its recorded `D99 T01` acceptance file is linked back):

- `§9.5` -> `D99 T01 §4` (`todo/99-manual/TODO-01-operator-steps.md`): opt-in Auto Arrange round-trip (`BERTH_TEST_ALLOW_AUTOARRANGE=1`) on a disposable interactive Windows 11 desktop.
- `§12.8` -> `D99 T01 §4` (`todo/99-manual/TODO-01-operator-steps.md`): live unlocked-desktop Windows 11 warning notification capture and click-to-open check.
- `§16.2` -> `D99 T01 §2` (`todo/99-manual/TODO-01-operator-steps.md`): physical dock and undock walkthrough on real monitors.

## Filed follow-ups

- No new follow-up items were filed by this sweep; the three operator-only hardware/unlocked-desktop checks above were already tracked in `D99 T01 §2` and `D99 T01 §4`, and `todo/findings.md` has no new unrelated defects.
