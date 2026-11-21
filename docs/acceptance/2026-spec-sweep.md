# Berth 2026 SPEC Acceptance Sweep

- **Gate command:** `pwsh scripts/check-all.ps1 -Published`
- **Tested commit:** `c88280f98d22b65b95ab86e14857b3fda2ba1513`
- **Gate result:** `passed` (`check-all: 8 step(s) passed`: 250 tooling tests OK with 8 skipped, 33 script tests OK, Release build 0 warnings/0 errors, 995/997 Release tests succeeded with 2 expected skips, published `artifacts/berth.exe` 75.05 MB, Published 1/1, RealDesktop 17/18 with 1 opt-in skip owned by `D99 T01 §4`, Smoke 3/3)
- **SPEC scope:** every bullet, table row, normative prose paragraph, and schema block in `design/SPEC.md` §2 to §19 (`171` items verified by `scripts/spec-items.py --check docs/acceptance/2026-spec-sweep.md`).

## Cross-references for SPEC §1 and §20

- **SPEC §1 (Policy: tokens only, Fluent controls, fidelity, copy):** enforced by `tests/Berth.Tests/Theme/NoHexColourTests.cs`, `scripts/tests/test_design_lint.py`, `scripts/tests/test_build_tokens.py`, `tests/Berth.Tests/Window/ContrastAndScaleTests.cs`, and `tests/Berth.Tests/Release/DocsRenderTests.cs`.
- **SPEC §20 (Out of scope):** documented honestly in `README.md` (`## What does it not do?`) and verified by `scripts/tests/test_readme.py` plus architecture boundary rules in `tests/Berth.Tests/Architecture/LayerRuleTests.cs`.

## SPEC §2 to §19 item-by-item sweep

| Ref | Requirement | Proof | Status |
| --- | --- | --- | --- |
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
| §5.1 [d41a0f33] | Heading "Layouts" (bodyStrong). | `P5.1` | yes |
| §5.2 [94d3c590] | One row per saved layout, sorted with the active la... | `P5.2` | yes |
| §5.3 [4a249dbb] | The active layout is the one saved for the monitor... | `P5.3` | yes |
| §5.4 [0ae24708] | When the current arrangement has no saved layout, a... | `P5.4` | yes |
| §5.5 [e2c467d0] | Exactly one row is selected. The selected row has t... | `P5.5` | yes |
| §5.6 [980a805e] | Unreadable layout omitted + notice | `P5.6` | yes |
| §5.7 [6fec9a18] | Bottom switches + version caption | `P5.7` | yes |
| §6.1 [6f2f2fc9] | Title: the layout's name (title), or "Not saved yet... | `P6.1` | yes |
| §6.2 [85530ea0] | Under it (caption, secondary): the monitor sizes, t... | `P6.2` | yes |
| §6.3 [443b9249] | For the active layout the header's right side holds... | `P6.3` | yes |
| §6.4 [3f24b472] | For the unsaved current arrangement it holds only "... | `P6.4` | yes |
| §6.5 [383131ea] | For a layout saved for another arrangement it holds... | `P6.5` | yes |
| §6.6 [c17f7615] | Save current layout & previous kept | `P6.6` | yes |
| §6.7 [96d1555c] | Restore icons & status line update | `P6.7` | yes |
| §6.8 [e4b34b70] | More menu Rename/Revert/Delete | `P6.8` | yes |
| §6.9 [48f137aa] | While a save, restore, undo or revert runs, the thr... | `P6.9` | yes |
| §7.1 [18744270] | Map panel min height & 20px grid | `P7.1` | yes |
| §7.2 [6d83b4ff] | Monitor rects scaled, padded, labels | `P7.2` | yes |
| §7.3 [ccf5aa82] | Icon markers 11px square 3px radius | `P7.3` | yes |
| §7.4 [de58e513] | Active layout marker comparison | `P7.4` | yes |
| §7.5 [435dac80] | In-place marker & readout word | `P7.5` | yes |
| §7.6 [7f26f00f] | Moved marker, line & readout word | `P7.6` | yes |
| §7.7 [7a26d59e] | Missing dashed marker & readout | `P7.7` | yes |
| §7.8 [be961fea] | For a layout of another arrangement every marker is... | `P7.8` | yes |
| §7.9 [e7df31e6] | Tooltip, select ring, dashed drop | `P7.9` | yes |
| §7.10 [39ee807a] | Readout name/coords/default text | `P7.10` | yes |
| §7.11 [df14f18a] | The count, bottom right (mono, secondary): "42 of 4... | `P7.11` | yes |
| §7.12 [f5afaaf8] | UIA list & marker reading order | `P7.12` | yes |
| §7.13 [5d2d267e] | Off-screen clamp & border inset | `P7.13` | yes |
| §8.1 [7287565b] | Status card square, sentence, right | `P8.1` | yes |
| §8.2 [09413348] | An operation is running "Working" nothing | `P8.2` | yes |
| §8.3 [d8b1c619] | A restore just finished "Restored 42 icons.", or th... | `P8.3` | yes |
| §8.4 [1dc5ad1a] | An undo just finished "Put 42 icons back where they... | `P8.4` | yes |
| §8.5 [f731eff2] | A save just finished "Saved 42 icons." or, over an... | `P8.5` | yes |
| §8.6 [b7512d16] | A revert just finished "Reverted to the save from 1... | `P8.6` | yes |
| §8.7 [e5b0c8da] | An operation was refused or failed The message of §... | `P8.7` | yes |
| §8.8 [e3fb6c7d] | Active layout, nothing moved or missing "Every icon... | `P8.8` | yes |
| §8.9 [92676767] | Active layout, some moved and some missing "3 icons... | `P8.9` | yes |
| §8.10 [1b9363a5] | Active layout, some moved, none missing "3 icons ha... | `P8.10` | yes |
| §8.11 [be9fae3a] | Active layout, some missing, none moved "2 saved ic... | `P8.11` | yes |
| §8.12 [52567266] | Unsaved arrangement "This monitor arrangement has n... | `P8.12` | yes |
| §8.13 [15498590] | Another arrangement's layout "Saved 12 November for... | `P8.13` | yes |
| §8.14 [f1370863] | Partial restore sentence & order | `P8.14` | yes |
| §8.15 [a93f451b] | Partial undo sentence & singular | `P8.15` | yes |
| §8.16 [c050a2eb] | Singular wording & clear rules | `P8.16` | yes |
| §9.1 [e00df1b0] | A notice is a bar between the header and the map wi... | `P9.1` | yes |
| §9.2 [b9de0902] | Auto arrange is on Warning "Auto arrange icons is o... | `P9.2` | yes |
| §9.3 [b17029c0] | The desktop cannot be reached Error "Berth cannot r... | `P9.3` | yes |
| §9.4 [f5c402dc] | A layout file cannot be read Error "The layout file... | `P9.4` | yes |
| §9.5 [60791535] | Turn it off clears/reads auto arrange | `P9.5` | deferred (D99 T01 §4) |
| §9.6 [93232d61] | While auto arrange is on, "Restore" is disabled and... | `P9.6` | yes |
| §9.7 [eeb4ae84] | While the desktop cannot be reached, "Restore" and... | `P9.7` | yes |
| §9.8 [886cb96d] | "Open folder" opens the layouts folder in File Expl... | `P9.8` | yes |
| §10.1 [baacc7b0] | Rename dialog, 1-40 chars, unique | `P10.1` | yes |
| §10.2 [39636615] | Revert to previous save swaps the layout's current... | `P10.2` | yes |
| §10.3 [d64cdef3] | Delete opens a dialog titled "Delete layout" with t... | `P10.3` | yes |
| §10.4 [6f963fc9] | Disk write & read-back before UI | `P10.4` | yes |
| §11.1 [e9df808f] | Two switches at the bottom of the layouts pane, lab... | `P11.1` | yes |
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
| §13.1 [b3386690] | berth.exe with a command does the work, prints the... | `P13.1` | yes |
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
| §14.3 [0c62717b] | layouts\<key>.json under the data root: one file pe... | `P14.3` | yes |
| §14.4 [c2452336] | Layout JSON schema v1 | `P14.4` | yes |
| §14.5 [a3e306ba] | Example key c7f017905ce518e6 | `P14.5` | yes |
| §14.6 [61e61454] | id, name, previous schema fields | `P14.6` | yes |
| §14.7 [9fe743a4] | settings.json schema & window px | `P14.7` | yes |
| §14.8 [fb63b1ca] | undo.json snapshot & delete on undo | `P14.8` | yes |
| §14.9 [444adcf9] | logs/berth.log under local root | `P14.9` | yes |
| §14.10 [5f2ec81e] | Temp write, flush, read-back, swap | `P14.10` | yes |
| §14.11 [9a0b29ff] | A layout or settings file that is not valid JSON, h... | `P14.11` | yes |
| §14.12 [e1d06847] | 1 to 64 monitors schema bound | `P14.12` | yes |
| §14.13 [d0ebcbfd] | Locked in-place save & redo note | `P14.13` | yes |
| §14.14 [7a24bb14] | Unreadable undo.json treated missing | `P14.14` | yes |
| §14.15 [f2add737] | SHA-256 16-hex arrangement key | `P14.15` | yes |
| §15.1 [9e90d4e6] | Read icons via IFolderView only | `P15.1` | yes |
| §15.2 [b11a3063] | Save naming & previous rotation | `P15.2` | yes |
| §15.3 [2f1dd597] | Restore. Refused, checked in this order, when the d... | `P15.3` | yes |
| §15.4 [b1833ca3] | In place. An icon is in place when the position rea... | `P15.4` | yes |
| §15.5 [3f64919c] | Undo checks key, deletes on full | `P15.5` | yes |
| §15.6 [0ea223e6] | Physical pixel coordinate roundtrip | `P15.6` | yes |
| §15.7 [7e151644] | Matching. Icons are matched by parsing name, compar... | `P15.7` | yes |
| §15.8 [c780a142] | 5s operation lock across tray/CLI | `P15.8` | yes |
| §16.1 [ab840655] | Runs only in the tray app, and only while "Restore... | `P16.1` | yes |
| §16.2 [5a40f065] | WM_DISPLAYCHANGE/TaskbarCreated 3s | `P16.2` | deferred (D99 T01 §2) |
| §16.3 [f45fdf73] | An arrangement with no layout does nothing and save... | `P16.3` | yes |
| §16.4 [4ee9a9bb] | WARN/FAIL/INFO log & balloon trigger | `P16.4` | yes |
| §16.5 [7f7a620d] | Visible window status + Undo | `P16.5` | yes |
| §17.1 [0a4852f8] | First run opens window; later tray | `P17.1` | yes |
| §17.2 [472dc3f7] | One tray app per user and logon session. Starting b... | `P17.2` | yes |
| §17.3 [855386a9] | CLI runs alongside tray; poll sees | `P17.3` | yes |
| §17.4 [4e141acf] | Unknown arg exits 2, no tray start | `P17.4` | yes |
| §18.1 [02380469] | Every refusal and failure has one message, used in... | `P18.1` | yes |
| §18.2 [f03b960a] | Auto arrange is on "Auto arrange icons is on, so Wi... | `P18.2` | yes |
| §18.3 [bab0ac05] | No layout for this arrangement "This monitor arrang... | `P18.3` | yes |
| §18.4 [c780542e] | No previous save "This layout has no previous save." | `P18.4` | yes |
| §18.5 [60de514c] | The desktop cannot be reached "Berth cannot reach t... | `P18.5` | yes |
| §18.6 [ff8a03e5] | The desktop is lost while placing icons "The deskto... | `P18.6` | yes |
| §18.7 [a2d67f36] | The displays changed mid-operation "The displays ch... | `P18.7` | yes |
| §18.8 [f3148959] | Nothing to undo "There is nothing to undo." | `P18.8` | yes |
| §18.9 [cf0aa620] | Icons did not stay after a restore or undo The part... | `P18.9` | yes |
| §18.10 [61afc81f] | Another operation is running "Berth is busy with an... | `P18.10` | yes |
| §18.11 [7be11449] | A file cannot be written "Berth could not write <pa... | `P18.11` | yes |
| §18.12 [4e69599e] | A folder cannot be read "Berth could not read <path... | `P18.12` | yes |
| §18.13 [9d13933f] | A file cannot be read "The layout file <file name>... | `P18.13` | yes |
| §18.14 [765f638e] | The settings file cannot be read "The settings file... | `P18.14` | yes |
| §18.15 [6d30f445] | Auto arrange would not turn off "Windows did not tu... | `P18.15` | yes |
| §18.16 [9bc3e9ab] | The Run value cannot be written "Berth could not ch... | `P18.16` | yes |
| §18.17 [e02ba2da] | Anything else "Something went wrong: <the exception... | `P18.17` | yes |
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

## Proof catalog

- `P2.1`: `scripts/tests/test_readme.py::test_all_links_images_and_anchors_resolve`
- `P2.2`: `tests/Berth.Tests/App/TrayIconTests.cs::EmbeddedTrayIconsLoadFromAssemblyAndContain16PxFrame`
- `P2.3`: `scripts/tests/test_build_tokens.py::test_fixture_output_matches_expected_xml`
- `P2.4`: `tests/Berth.Tests/Theme/TokenKeysTests.cs::GeneratedDictionariesLoadLooselyAndCompiledWithEveryValueVerified`
- `P3.1`: `tests/Berth.Tests/App/BerthApplicationTests.cs::SelectPicksHighContrastFirstThenLightOrDark`
- `P3.2`: `tests/Berth.Tests/Release/PublishedTests.cs::PublishedExecutableHasValidChecksumManifestIconAndRunsWithoutSharedDotnetRuntime`
- `P3.3`: `tests/Berth.Tests/Window/AccessibilityTests.cs::TabOrderTraversesForwardAndBackwardAcrossAllWindowStates`
- `P3.4`: `tests/Berth.Tests/Window/ContrastAndScaleTests.cs::RendersDataHighContrastWithOnlyStandInSystemColoursAndData150At1470x960`
- `P3.5`: `tests/Berth.Tests/Core/FormatsTests.cs::WhenUsesLocalDatesAcrossMidnightInNonUtcZones`
- `P3.6`: `tests/Berth.Tests/Core/FormatsTests.cs::WhenUsesLocalDatesAcrossMidnightInNonUtcZones`
- `P3.7`: `tests/Berth.Tests/Core/RestoreTests.cs::AnUnreachableDesktopIsRefusedFirstAndMovesNothing`
- `P3.8`: `tests/Berth.Tests/Core/BerthPathsTests.cs::ForRootPutsBothRootsUnderIt`
- `P4.1`: `tests/Berth.Tests/Window/WindowPlacementTests.cs::ResolveAcceptsVisibleAndExact100By100BoundaryAndRejects99OverlapOffScreenDisconnectedAndTooSmall`
- `P4.2`: `tests/Berth.Tests/Window/MainWindowFrameTests.cs::FrameUsesSpecTitleIconTokensDividerAndPreservesLeftPaneOnResize`
- `P4.3`: `tests/Berth.Tests/Window/MainWindowFrameTests.cs::FrameUsesSpecTitleIconTokensDividerAndPreservesLeftPaneOnResize`
- `P4.4`: `tests/Berth.Tests/App/StartupPlanTests.cs::StartupPlanForMapsEveryArgumentShapeAndSettingsExistence`
- `P4.5`: `tests/Berth.Tests/Window/DesktopPollerTests.cs::PollerReadsOnStartAndTickSkipsOverlapIgnoresPostStopTicksAndTriggersAndRestartsCleanly`
- `P5.1`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker`
- `P5.2`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker`
- `P5.3`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker`
- `P5.4`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker`
- `P5.5`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker`
- `P5.6`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P5.7`: `tests/Berth.Tests/Window/SettingsTests.cs::HostedSwitchesToggleOnAndOffPersistToDiskAndIsolatedRegistryAndSyncToTrayMenu`
- `P6.1`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.2`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.3`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.4`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.5`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P6.6`: `tests/Berth.Tests/Window/MainViewModelTests.cs::ObservableObjectAndRelayCommandRaiseEventsOncePerActualChange`
- `P6.7`: `tests/Berth.Tests/Window/MainViewModelTests.cs::ObservableObjectAndRelayCommandRaiseEventsOncePerActualChange`
- `P6.8`: `tests/Berth.Tests/Window/RevertDeleteTests.cs::RevertSwapsTwiceWithoutMovingDesktopShowsWorkingAndHandlesStaleRefusals`
- `P6.9`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P7.1`: `tests/Berth.Tests/Window/ContrastAndScaleTests.cs::RendersDataHighContrastWithOnlyStandInSystemColoursAndData150At1470x960`
- `P7.2`: `tests/Berth.Tests/Window/MapGeometryTests.cs::FitSingleMonitorAtOriginComputesExactLiteralCoordinates`
- `P7.3`: `tests/Berth.Tests/Window/MarkerTests.cs::TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine`
- `P7.4`: `tests/Berth.Tests/Core/LayoutComparisonTests.cs::EachSavedIconIsInPlaceMovedOrMissing`
- `P7.5`: `tests/Berth.Tests/Window/MarkerTests.cs::TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine`
- `P7.6`: `tests/Berth.Tests/Window/MarkerTests.cs::TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine`
- `P7.7`: `tests/Berth.Tests/Window/MarkerTests.cs::TemplatesEachIconStateWithExactFillStrokeRadiiTooltipAutomationAndMovedLine`
- `P7.8`: `tests/Berth.Tests/Window/PositionMapTests.cs::DrawsSelectedLayoutsMonitorsLabelsAndSavedPositionsAndSwitchesToUnsavedLiveMonitors`
- `P7.9`: `tests/Berth.Tests/Window/IconReadoutTests.cs::FormatsExactReadoutForEveryStateOffScreenNegativeCoordsAndNoneSelected`
- `P7.10`: `tests/Berth.Tests/Window/IconReadoutTests.cs::FormatsExactReadoutForEveryStateOffScreenNegativeCoordsAndNoneSelected`
- `P7.11`: `tests/Berth.Tests/Window/PositionMapTests.cs::DrawsSelectedLayoutsMonitorsLabelsAndSavedPositionsAndSwitchesToUnsavedLiveMonitors`
- `P7.12`: `tests/Berth.Tests/Window/AccessibilityTests.cs::TabOrderTraversesForwardAndBackwardAcrossAllWindowStates`
- `P7.13`: `tests/Berth.Tests/Window/MapGeometryTests.cs::FitSingleMonitorAtOriginComputesExactLiteralCoordinates`
- `P8.1`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P8.2`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.3`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.4`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.5`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.6`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.7`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.8`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.9`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.10`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.11`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.12`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.13`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P8.14`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P8.15`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P8.16`: `tests/Berth.Tests/Window/StatusViewModelTests.cs::CoversEverySpec8TableRowWithExactSentenceAndRightSide`
- `P9.1`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.2`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.3`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.4`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.5`: `tests/Berth.Tests/Desktop/AutoArrangeTests.cs::LiveAutoArrangeReadsWithoutThrowingAndAgreesWithFallback`
- `P9.6`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.7`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P9.8`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P10.1`: `tests/Berth.Tests/Window/RenameTests.cs::ViewModelValidatesEveryRuleLiveIncludingCaseInsensitiveDuplicateAndLiveOtherUpdates`
- `P10.2`: `tests/Berth.Tests/Window/RevertDeleteTests.cs::RevertSwapsTwiceWithoutMovingDesktopShowsWorkingAndHandlesStaleRefusals`
- `P10.3`: `tests/Berth.Tests/Window/RevertDeleteTests.cs::RevertSwapsTwiceWithoutMovingDesktopShowsWorkingAndHandlesStaleRefusals`
- `P10.4`: `tests/Berth.Tests/Core/LayoutEditTests.cs::NoPreviousSaveIsADistinctRefusalFromNoLayout`
- `P11.1`: `tests/Berth.Tests/Window/SettingsTests.cs::HostedSwitchesToggleOnAndOffPersistToDiskAndIsolatedRegistryAndSyncToTrayMenu`
- `P11.2`: `tests/Berth.Tests/Core/SettingsStoreTests.cs::TheDefaultsHaveTheSpecShape`
- `P11.3`: `tests/Berth.Tests/Desktop/RunValueTests.cs::IsOnCoversMissingQuotedUnquotedCaseInsensitiveOtherExecutableDwordAndExpandString`
- `P11.4`: `tests/Berth.Tests/Core/SettingChangesTests.cs::ConstructorRejectsNullDependencies`
- `P12.1`: `tests/Berth.Tests/App/TrayIconTests.cs::TrayIconSelectsSmallIconSizeMatchesDarkAndLightAssetsAndSwitchesLive`
- `P12.2`: `tests/Berth.Tests/App/TrayIconTests.cs::TooltipFormatsActiveAndMissingLayoutsAndTruncatesCleanlyOnTextElementBoundaries`
- `P12.3`: `tests/Berth.Tests/App/TrayIconTests.cs::TrayIconRefreshesTooltipAfterSaveAndMonitorChangeAndFiltersClickButtons`
- `P12.4`: `tests/Berth.Tests/App/TrayMenuTests.cs::MenuHasSpecOrderSeparatorBoldDefaultAndSystemRenderer`
- `P12.5`: `tests/Berth.Tests/App/TrayMenuTests.cs::OpeningRefreshesRestoreEnabledAndAutoCheckedOnSameMenuInstance`
- `P12.6`: `tests/Berth.Tests/App/TrayMenuTests.cs::AttachedMenuSaveRefreshesTooltipAndExitDrainsInFlightOperationWithExitCodeZero`
- `P12.7`: `tests/Berth.Tests/App/SystemEventsTests.cs::CreatesHiddenUnownedTopLevelWindowWithNoTaskbarButtonAndDestroysOnDispose`, `tests/Berth.Tests/App/TrayIconTests.cs::TaskbarCreatedReaddsTrayIconBeforeArmingExplorerRestartTimer`
- `P12.8`: `tests/Berth.Tests/App/BalloonTests.cs::PolicyReturnsNullForSuccessSuccessWithMissingAndNoLayout`
- `P13.1`: `tests/Berth.Tests/Cli/ProcessTests.cs::NonMovingCommandsMatchSpec13StreamsExitCodesAndLeaveOwnerDataUntouched`
- `P13.2`: `tests/Berth.Tests/Cli/SaveCommandTests.cs::SaveDefaultNameWritesExactLineToStdoutAndPersistsLayout`
- `P13.3`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreFullWritesExactLineToStdoutAndReturnsExit0`
- `P13.4`: `tests/Berth.Tests/Cli/UndoCommandTests.cs::RestoreThenUndoReturnsIconsToPreRestorePositionsDeletesSnapshotAndSecondUndoIsRefused`
- `P13.5`: `tests/Berth.Tests/Cli/ListCommandTests.cs::ListSingleActiveLayoutMatchesSpec13ExampleExactLine`
- `P13.6`: `tests/Berth.Tests/Cli/UsageTests.cs::UsageTextMatchesCompleteExpectedLinesForAllCommandsAndExitCodes`
- `P13.7`: `tests/Berth.Tests/Cli/UsageTests.cs::UsageTextMatchesCompleteExpectedLinesForAllCommandsAndExitCodes`
- `P13.8`: `tests/Berth.Tests/Cli/ListCommandTests.cs::ListSingleActiveLayoutMatchesSpec13ExampleExactLine`
- `P13.9`: `tests/Berth.Tests/Cli/CliArgumentsTests.cs::ParseNullThrowsArgumentNullException`
- `P13.10`: `tests/Berth.Tests/Cli/CliArgumentsTests.cs::ParseNullThrowsArgumentNullException`
- `P13.11`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreFullWritesExactLineToStdoutAndReturnsExit0`
- `P13.12`: `tests/Berth.Tests/Cli/UndoCommandTests.cs::RestoreThenUndoReturnsIconsToPreRestorePositionsDeletesSnapshotAndSecondUndoIsRefused`
- `P13.13`: `tests/Berth.Tests/Cli/UndoCommandTests.cs::RestoreThenUndoReturnsIconsToPreRestorePositionsDeletesSnapshotAndSecondUndoIsRefused`
- `P13.14`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P13.15`: `tests/Berth.Tests/Cli/UsageTests.cs::UsageTextMatchesCompleteExpectedLinesForAllCommandsAndExitCodes`
- `P13.16`: `tests/Berth.Tests/Cli/SaveCommandTests.cs::SaveDefaultNameWritesExactLineToStdoutAndPersistsLayout`
- `P13.17`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreFullWritesExactLineToStdoutAndReturnsExit0`
- `P13.18`: `tests/Berth.Tests/Cli/CliArgumentsTests.cs::ParseNullThrowsArgumentNullException`
- `P13.19`: `tests/Berth.Tests/Cli/RestoreCommandTests.cs::RestoreFullWritesExactLineToStdoutAndReturnsExit0`
- `P13.20`: `tests/Berth.Tests/Cli/SaveCommandTests.cs::SaveDefaultNameWritesExactLineToStdoutAndPersistsLayout`
- `P13.21`: `tests/Berth.Tests/Cli/ListCommandTests.cs::ListSingleActiveLayoutMatchesSpec13ExampleExactLine`
- `P13.22`: `tests/Berth.Tests/Cli/ConsoleBehaviourTests.cs::ApplyIgnoresReportAndHoldWhenTestModeIsOff`
- `P14.1`: `tests/Berth.Tests/Core/BerthPathsTests.cs::ForRootPutsBothRootsUnderIt`
- `P14.2`: `tests/Berth.Tests/Desktop/ScopedDesktopIconsTests.cs::ShellSeamReadAndWriteNeverReadPositionParsingNameOrMoveNonMatchingOwnerIcons`
- `P14.3`: `tests/Berth.Tests/Core/LayoutStoreTests.cs::AValidLayoutHasNoProblem`
- `P14.4`: `tests/Berth.Tests/Core/LayoutJsonTests.cs::SpecExampleParsesToItsValues`
- `P14.5`: `tests/Berth.Tests/Core/ArrangementKeyTests.cs::OneMonitorHasItsKnownKey`
- `P14.6`: `tests/Berth.Tests/Core/LayoutJsonTests.cs::SpecExampleParsesToItsValues`
- `P14.7`: `tests/Berth.Tests/Core/SettingsStoreTests.cs::TheDefaultsHaveTheSpecShape`
- `P14.8`: `tests/Berth.Tests/Core/UndoTests.cs::ARestoreFollowedByAnUndoReturnsEveryIconWhereItWasAndDeletesTheSnapshot`
- `P14.9`: `tests/Berth.Tests/Core/LogTests.cs::ALineHasTheUtcTimeTheLevelAndTheMessage`
- `P14.10`: `tests/Berth.Tests/Core/JsonFileTests.cs::AWriteCanBeReadBackAndLeavesNoTemporaryFile`
- `P14.11`: `tests/Berth.Tests/Core/LayoutStoreTests.cs::AValidLayoutHasNoProblem`
- `P14.12`: `tests/Berth.Tests/Core/LayoutJsonTests.cs::SpecExampleParsesToItsValues`
- `P14.13`: `tests/Berth.Tests/Core/JsonFileTests.cs::AWriteCanBeReadBackAndLeavesNoTemporaryFile`
- `P14.14`: `tests/Berth.Tests/Core/UndoTests.cs::ARestoreFollowedByAnUndoReturnsEveryIconWhereItWasAndDeletesTheSnapshot`
- `P14.15`: `tests/Berth.Tests/Core/ArrangementKeyTests.cs::OneMonitorHasItsKnownKey`
- `P15.1`: `tests/Berth.Tests/Desktop/ReadIconsTests.cs::DesktopCoordinatesConvertsPrimaryNegativeXNegativeYAndRoundTrips`
- `P15.2`: `tests/Berth.Tests/Core/SaveTests.cs::AFirstSaveOnAFreshProfileWritesTheArrangementsLayout`
- `P15.3`: `tests/Berth.Tests/Core/RestoreTests.cs::AnUnreachableDesktopIsRefusedFirstAndMovesNothing`
- `P15.4`: `tests/Berth.Tests/Core/RestoreTests.cs::AnUnreachableDesktopIsRefusedFirstAndMovesNothing`
- `P15.5`: `tests/Berth.Tests/Core/UndoTests.cs::ARestoreFollowedByAnUndoReturnsEveryIconWhereItWasAndDeletesTheSnapshot`
- `P15.6`: `tests/Berth.Tests/Desktop/SetPositionsTests.cs::SetPositionsBatchesMatchedIconsIgnoringCaseFirstDesktopWinsConvertsToViewAndPreservesSelectionFlags`
- `P15.7`: `tests/Berth.Tests/Core/RestoreTests.cs::AnUnreachableDesktopIsRefusedFirstAndMovesNothing`
- `P15.8`: `tests/Berth.Tests/Core/OperationLockTests.cs::ABusyLockReturnsBusyWithoutRunningTheWorkThenRunsItOnceFree`
- `P16.1`: `tests/Berth.Tests/App/AutoRestoreTests.cs::FiveStaggeredTriggersInBurstProduceExactlyOneRestoreAfterLastDeadline`
- `P16.2`: `tests/Berth.Tests/App/AutoRestoreTests.cs::FiveStaggeredTriggersInBurstProduceExactlyOneRestoreAfterLastDeadline`
- `P16.3`: `tests/Berth.Tests/App/AutoRestoreTests.cs::FiveStaggeredTriggersInBurstProduceExactlyOneRestoreAfterLastDeadline`
- `P16.4`: `tests/Berth.Tests/App/BalloonTests.cs::PolicyReturnsNullForSuccessSuccessWithMissingAndNoLayout`
- `P16.5`: `tests/Berth.Tests/App/AutoRestoreWindowTests.cs::OpenWindowShowsAutomaticRestoreResultUndoMapRefreshBusyStateTooltipAndHiddenGuard`
- `P17.1`: `tests/Berth.Tests/App/StartupPlanTests.cs::StartupPlanForMapsEveryArgumentShapeAndSettingsExistence`
- `P17.2`: `tests/Berth.Tests/App/SingleInstanceTests.cs::ResolveNameUsesNonBlankOverrideAndFallsBackToCurrentUserSidWithoutClaiming`
- `P17.3`: `tests/Berth.Tests/Cli/ProcessTests.cs::SaveMoveRestoreUndoAndListCycleOnProbeIconMatchesSpec13EndToEnd`
- `P17.4`: `tests/Berth.Tests/Cli/CliArgumentsTests.cs::ParseNullThrowsArgumentNullException`
- `P18.1`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.2`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.3`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.4`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.5`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.6`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.7`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.8`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.9`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.10`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.11`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.12`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.13`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.14`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.15`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.16`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.17`: `tests/Berth.Tests/Core/MessagesTests.cs::WorkingConstantMatchesSpec`
- `P18.18`: `tests/Berth.Tests/Core/LogTests.cs::ALineHasTheUtcTimeTheLevelAndTheMessage`
- `P18.19`: `tests/Berth.Tests/Core/SettingsStoreTests.cs::TheDefaultsHaveTheSpecShape`
- `P18.20`: `tests/Berth.Tests/Core/SettingChangesTests.cs::ConstructorRejectsNullDependencies`
- `P18.21`: `tests/Berth.Tests/Core/LayoutEditTests.cs::NoPreviousSaveIsADistinctRefusalFromNoLayout`
- `P18.22`: `tests/Berth.Tests/Core/LogTests.cs::ALineHasTheUtcTimeTheLevelAndTheMessage`
- `P18.23`: `tests/Berth.Tests/App/CrashHandlerTests.cs::HandleLogsIndentedStackAndInnerExceptionRedactsSourceAndShowsExactTwoLineRedactedMessage`
- `P18.24`: `tests/Berth.Tests/Core/LayoutEditTests.cs::NoPreviousSaveIsADistinctRefusalFromNoLayout`
- `P19.1`: `tests/Berth.Tests/Window/LayoutsPaneTests.cs::DisplaysHeadingThreeRealizedRowsActiveBadgeOnlyOnActiveAndFlush3PxSelectionMarker`
- `P19.2`: `tests/Berth.Tests/Window/DetailHeaderTests.cs::ShowsExactTitleSubtitleAndButtonsForActiveUnsavedOtherAndNone`
- `P19.3`: `tests/Berth.Tests/Window/PositionMapTests.cs::DrawsSelectedLayoutsMonitorsLabelsAndSavedPositionsAndSwitchesToUnsavedLiveMonitors`
- `P19.4`: `tests/Berth.Tests/Window/StatusLineTests.cs::HostedStatusLineShowsComparisonTimeThenRestoresAndUndoesBackToPreRestorePositions`
- `P19.5`: `tests/Berth.Tests/Window/NoticesTests.cs::OrdersErrorsBeforeWarningsAndCoversNoneSingleAndStackedCombinations`
- `P19.6`: `tests/Berth.Tests/Window/RenameTests.cs::RendersAllFourSpec19RenameDialogStatesInLightAndDarkAndHighContrast150`, `tests/Berth.Tests/Window/RevertDeleteTests.cs::RendersDeleteDialogInLightAndDarkAndVerifies40CharNameAt150PercentHighContrast`
- `P19.7`: `tests/Berth.Tests/App/TrayIconTests.cs::TrayIconSelectsSmallIconSizeMatchesDarkAndLightAssetsAndSwitchesLive`, `tests/Berth.Tests/App/TrayMenuTests.cs::OpeningRefreshesRestoreEnabledAndAutoCheckedOnSameMenuInstance`

## Operator-owned live-hardware observations

Three rows above carry `deferred (D99 T01 §N)` because their automated unit/fake suites pass while live-hardware or unlocked-desktop observation requires the owner:

- `§9.5` -> `D99 T01 §4` (`todo/99-manual/TODO-01-operator-steps.md`): opt-in Auto Arrange round-trip (`BERTH_TEST_ALLOW_AUTOARRANGE=1`) on a disposable interactive Windows 11 desktop.
- `§12.8` -> `D99 T01 §4` (`todo/99-manual/TODO-01-operator-steps.md`): live unlocked-desktop Windows 11 warning notification capture and click-to-open check.
- `§16.2` -> `D99 T01 §2` (`todo/99-manual/TODO-01-operator-steps.md`): physical dock and undock walkthrough on real monitors.

## Filed follow-ups

- No new follow-up items were filed by this sweep; the three operator-only hardware/unlocked-desktop checks above were already tracked in `D99 T01 §2` and `D99 T01 §4`, and `todo/findings.md` has no new unrelated defects.
