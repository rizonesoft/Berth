# Berth 2026 SPEC Acceptance Sweep

- **Gate command:** `pwsh scripts/check-all.ps1 -Published`
- **Tested commit:** `<PENDING_COMMIT>`
- **Gate result:** `<PENDING_RESULT>`
- **SPEC scope:** every bullet, table row, normative prose paragraph, and schema block in `design/SPEC.md` §2 to §19 (`171` items verified by `scripts/spec-items.py --check docs/acceptance/2026-spec-sweep.md`).

## Cross-references for SPEC §1 and §20

- **SPEC §1 (Policy: tokens only, Fluent controls, fidelity, copy):** enforced by `tests/Berth.Tests/Theme/NoHexColourTests.cs`, `scripts/tests/test_design_lint.py`, `scripts/tests/test_build_tokens.py`, `tests/Berth.Tests/Window/ContrastAndScaleTests.cs`, and `tests/Berth.Tests/Release/DocsRenderTests.cs`.
- **SPEC §20 (Out of scope):** documented honestly in `README.md` (`## What does it not do?`) and verified by `scripts/tests/test_readme.py` plus architecture boundary rules in `tests/Berth.Tests/Architecture/LayerRuleTests.cs`.

## SPEC §2 to §19 item-by-item sweep

| Ref | Requirement | Proof | Status |
| --- | --- | --- | --- |
| §2.1 [077d3810] | Brand assets in assets/ | `scripts/tests/test_readme.py` | yes |
| §2.2 [1b86aa3a] | App icon & dark/light tray icons | `tests/Berth.Tests/App/TrayIconTests.cs` | yes |
| §2.3 [7f0942c7] | tokens.json & generated Theme XAML | `scripts/tests/test_build_tokens.py` | yes |
| §2.4 [9ef15e84] | High contrast system colour tokens | `tests/Berth.Tests/Theme/TokenKeysTests.cs` | yes |
| §3.1 [47c907aa] | System theme light/dark/HC live swap | `tests/Berth.Tests/App/BerthApplicationTests.cs` | yes |
| §3.2 [c3043bab] | PerMonitorV2 DPI & physical pixels | `tests/Berth.Tests/Release/PublishedTests.cs` | yes |
| §3.3 [c1d549b4] | Tab order, focus, 32px/19px targets | `tests/Berth.Tests/Window/AccessibilityTests.cs` | yes |
| §3.4 [b02113b3] | Text wraps; name ellipsis + tooltip | `tests/Berth.Tests/Window/ContrastAndScaleTests.cs` | yes |
| §3.5 [89d089bb] | Relative & absolute date formatting | `tests/Berth.Tests/Core/FormatsTests.cs` | yes |
| §3.6 [b7a3c8ab] | Monitor sizes formatting | `tests/Berth.Tests/Core/FormatsTests.cs` | yes |
| §3.7 [e4e5c268] | Never moves icons unrequested | `tests/Berth.Tests/Core/RestoreTests.cs` | yes |
| §3.8 [4f988ab2] | Writes only Berth data, Run, flag | `tests/Berth.Tests/Core/BerthPathsTests.cs` | yes |
| §4.1 [77be23e2] | Title, 980x640/760x520, placement | `tests/Berth.Tests/Window/WindowPlacementTests.cs` | yes |
| §4.2 [924cc43d] | Two panes 250px + 1px divider | `tests/Berth.Tests/Window/MainWindowFrameTests.cs` | yes |
| §4.3 [2a2f65f2] | Close hides to tray; Esc ignored | `tests/Berth.Tests/Window/MainWindowFrameTests.cs` | yes |
| §4.4 [e3cb0e66] | Open from tray, 2nd launch, 1st run | `tests/Berth.Tests/App/StartupPlanTests.cs` | yes |
| §4.5 [2e6fd4af] | 5s desktop poll visible; idle hidden | `tests/Berth.Tests/Window/DesktopPollerTests.cs` | yes |
| §5.1 [d41a0f33] | Heading Layouts bodyStrong | `tests/Berth.Tests/Window/LayoutsPaneTests.cs` | yes |
| §5.2 [94d3c590] | Sorted rows with monitor/icon count | `tests/Berth.Tests/Window/LayoutsPaneTests.cs` | yes |
| §5.3 [4a249dbb] | Active layout carries Active badge | `tests/Berth.Tests/Window/LayoutsPaneTests.cs` | yes |
| §5.4 [0ae24708] | Unsaved arrangement first row | `tests/Berth.Tests/Window/LayoutsPaneTests.cs` | yes |
| §5.5 [e2c467d0] | Single selection + 3px accent bar | `tests/Berth.Tests/Window/LayoutsPaneTests.cs` | yes |
| §5.6 [980a805e] | Unreadable layout omitted + notice | `tests/Berth.Tests/Window/NoticesTests.cs` | yes |
| §5.7 [6fec9a18] | Bottom switches + version caption | `tests/Berth.Tests/Window/SettingsTests.cs` | yes |
| §6.1 [6f2f2fc9] | Title: the layout's name (title),... | `tests/Berth.Tests/Window/DetailHeaderTests.cs` | yes |
| §6.2 [85530ea0] | Under it (caption, secondary): th... | `tests/Berth.Tests/Window/DetailHeaderTests.cs` | yes |
| §6.3 [443b9249] | For the active layout the header'... | `tests/Berth.Tests/Window/DetailHeaderTests.cs` | yes |
| §6.4 [3f24b472] | For the unsaved current arrangeme... | `tests/Berth.Tests/Window/DetailHeaderTests.cs` | yes |
| §6.5 [383131ea] | For a layout saved for another ar... | `tests/Berth.Tests/Window/DetailHeaderTests.cs` | yes |
| §6.6 [c17f7615] | Save current layout & previous kept | `tests/Berth.Tests/Window/MainViewModelTests.cs` | yes |
| §6.7 [96d1555c] | Restore icons & status line update | `tests/Berth.Tests/Window/MainViewModelTests.cs` | yes |
| §6.8 [e4b34b70] | More menu Rename/Revert/Delete | `tests/Berth.Tests/Window/RevertDeleteTests.cs` | yes |
| §6.9 [48f137aa] | Buttons disabled & Working state | `tests/Berth.Tests/Window/DetailHeaderTests.cs` | yes |
| §7.1 [18744270] | Map panel min height & 20px grid | `tests/Berth.Tests/Window/ContrastAndScaleTests.cs` | yes |
| §7.2 [6d83b4ff] | Monitor rects scaled, padded, labels | `tests/Berth.Tests/Window/MapGeometryTests.cs` | yes |
| §7.3 [ccf5aa82] | Icon markers 11px square 3px radius | `tests/Berth.Tests/Window/MarkerTests.cs` | yes |
| §7.4 [de58e513] | Active layout marker comparison | `tests/Berth.Tests/Core/LayoutComparisonTests.cs` | yes |
| §7.5 [435dac80] | In-place marker & readout word | `tests/Berth.Tests/Window/MarkerTests.cs` | yes |
| §7.6 [7f26f00f] | Moved marker, line & readout word | `tests/Berth.Tests/Window/MarkerTests.cs` | yes |
| §7.7 [7a26d59e] | Missing dashed marker & readout | `tests/Berth.Tests/Window/MarkerTests.cs` | yes |
| §7.8 [be961fea] | Neutral markers for other/unsaved | `tests/Berth.Tests/Window/PositionMapTests.cs` | yes |
| §7.9 [e7df31e6] | Tooltip, select ring, dashed drop | `tests/Berth.Tests/Window/IconReadoutTests.cs` | yes |
| §7.10 [39ee807a] | Readout name/coords/default text | `tests/Berth.Tests/Window/IconReadoutTests.cs` | yes |
| §7.11 [df14f18a] | Bottom-right in-place/icon count | `tests/Berth.Tests/Window/PositionMapTests.cs` | yes |
| §7.12 [f5afaaf8] | UIA list & marker reading order | `tests/Berth.Tests/Window/AccessibilityTests.cs` | yes |
| §7.13 [5d2d267e] | Off-screen clamp & border inset | `tests/Berth.Tests/Window/MapGeometryTests.cs` | yes |
| §8.1 [7287565b] | Status card square, sentence, right | `tests/Berth.Tests/Window/StatusViewModelTests.cs` | yes |
| §8.2 [09413348] | An operation is running "Working"... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.3 [d8b1c619] | A restore just finished "Restored... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.4 [1dc5ad1a] | An undo just finished "Put 42 ico... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.5 [f731eff2] | A save just finished "Saved 42 ic... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.6 [b7512d16] | A revert just finished "Reverted... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.7 [e5b0c8da] | An operation was refused or faile... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.8 [e3fb6c7d] | Active layout, nothing moved or m... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.9 [92676767] | Active layout, some moved and som... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.10 [1b9363a5] | Active layout, some moved, none m... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.11 [be9fae3a] | Active layout, some missing, none... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.12 [52567266] | Unsaved arrangement "This monitor... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.13 [15498590] | Another arrangement's layout "Sav... | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §8.14 [f1370863] | Partial restore sentence & order | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §8.15 [a93f451b] | Partial undo sentence & singular | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §8.16 [c050a2eb] | Singular wording & clear rules | `tests/Berth.Tests/Window/StatusViewModelTests.cs` | yes |
| §9.1 [e00df1b0] | A notice is a bar between the hea... | `tests/Berth.Tests/Window/NoticesTests.cs` | yes |
| §9.2 [b9de0902] | Auto arrange is on Warning "Auto... | `tests/Berth.Tests/Window/NoticesTests.cs` | yes |
| §9.3 [b17029c0] | The desktop cannot be reached Err... | `tests/Berth.Tests/Window/NoticesTests.cs` | yes |
| §9.4 [f5c402dc] | A layout file cannot be read Erro... | `tests/Berth.Tests/Window/NoticesTests.cs` | yes |
| §9.5 [60791535] | Turn it off clears/re... | `tests/Berth.Tests/Desktop/AutoArrangeTests.cs` | deferred (D99 T01 §4) |
| §9.6 [93232d61] | While auto arrange is on, "Restor... | `tests/Berth.Tests/Window/NoticesTests.cs` | yes |
| §9.7 [eeb4ae84] | While the desktop cannot be reach... | `tests/Berth.Tests/Window/NoticesTests.cs` | yes |
| §9.8 [886cb96d] | "Open folder" opens the layouts f... | `tests/Berth.Tests/Window/NoticesTests.cs` | yes |
| §10.1 [baacc7b0] | Rename dialog, 1-40 chars, unique | `tests/Berth.Tests/Window/RenameTests.cs` | yes |
| §10.2 [39636615] | Revert swaps saves, moves no icon | `tests/Berth.Tests/Window/RevertDeleteTests.cs` | yes |
| §10.3 [d64cdef3] | Delete dialog & selects unsaved | `tests/Berth.Tests/Window/RevertDeleteTests.cs` | yes |
| §10.4 [6f963fc9] | Disk write & read-back before UI | `tests/Berth.Tests/Core/LayoutEditTests.cs` | yes |
| §11.1 [e9df808f] | Two switches at the bottom of the... | `tests/Berth.Tests/Window/SettingsTests.cs` | yes |
| §11.2 [e738e2fb] | Restore automatically off default | `tests/Berth.Tests/Core/SettingsStoreTests.cs` | yes |
| §11.3 [8f897ce3] | Start with Windows HKCU Run value | `tests/Berth.Tests/Desktop/RunValueTests.cs` | yes |
| §11.4 [eaab04b6] | Switch write, read-back, rollback | `tests/Berth.Tests/Core/SettingChangesTests.cs` | yes |
| §12.1 [473391d2] | Tray icon dark/light taskbar swap | `tests/Berth.Tests/App/TrayIconTests.cs` | yes |
| §12.2 [b6625ada] | Tray tooltip saved or no layout | `tests/Berth.Tests/App/TrayIconTests.cs` | yes |
| §12.3 [2f9111bc] | Left/double click opens main window | `tests/Berth.Tests/App/TrayIconTests.cs` | yes |
| §12.4 [0f9504f4] | Right-click menu exact item order | `tests/Berth.Tests/App/TrayMenuTests.cs` | yes |
| §12.5 [833c6616] | Save/Restore menu enable rules | `tests/Berth.Tests/App/TrayMenuTests.cs` | yes |
| §12.6 [073236b0] | Exit removes icon & ends process | `tests/Berth.Tests/App/TrayMenuTests.cs` | yes |
| §12.7 [f7581bb6] | TaskbarCreated re-adds tray icon | `tests/Berth.Tests/App/SystemEventsTests.cs` | yes |
| §12.8 [8549b0be] | Failure balloon & 255-char trim | `tests/Berth.Tests/App/BalloonTests.cs` | deferred (D99 T01 §4) |
| §13.1 [b3386690] | CLI runs, prints, exits headless | `tests/Berth.Tests/Cli/ProcessTests.cs` | yes |
| §13.2 [d7542d82] | berth save [--name <name>] | `tests/Berth.Tests/Cli/SaveCommandTests.cs` | yes |
| §13.3 [fb03b105] | berth restore | `tests/Berth.Tests/Cli/RestoreCommandTests.cs` | yes |
| §13.4 [b9e42c07] | berth undo | `tests/Berth.Tests/Cli/UndoCommandTests.cs` | yes |
| §13.5 [9616ed17] | berth list | `tests/Berth.Tests/Cli/ListCommandTests.cs` | yes |
| §13.6 [e23565b8] | berth --help, -h, /? | `tests/Berth.Tests/Cli/UsageTests.cs` | yes |
| §13.7 [1ef00f7e] | berth --version | `tests/Berth.Tests/Cli/UsageTests.cs` | yes |
| §13.8 [5c51a851] | berth list columns & empty text | `tests/Berth.Tests/Cli/ListCommandTests.cs` | yes |
| §13.9 [fae8038f] | --name forms & empty value rules | `tests/Berth.Tests/Cli/CliArgumentsTests.cs` | yes |
| §13.10 [5aad0410] | Parser usage diagnostics (exit 2) | `tests/Berth.Tests/Cli/CliArgumentsTests.cs` | yes |
| §13.11 [4d41ac36] | Restore missing icons prints, exit 0 | `tests/Berth.Tests/Cli/RestoreCommandTests.cs` | yes |
| §13.12 [8d8530a3] | Did-not-stay stderr, exit 1, keep | `tests/Berth.Tests/Cli/UndoCommandTests.cs` | yes |
| §13.13 [cb59ed29] | Partial undo stdout/stderr exits | `tests/Berth.Tests/Cli/UndoCommandTests.cs` | yes |
| §13.14 [1cfb160f] | Control/newline sanitization | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §13.15 [1ba7d814] | stdout/stderr & 2-line usage err | `tests/Berth.Tests/Cli/UsageTests.cs` | yes |
| §13.16 [988d6862] | Exit code 0 Done | `tests/Berth.Tests/Cli/SaveCommandTests.cs` | yes |
| §13.17 [28f75623] | Exit code 1 Error | `tests/Berth.Tests/Cli/RestoreCommandTests.cs` | yes |
| §13.18 [ee013c26] | Exit code 2 Wrong usage | `tests/Berth.Tests/Cli/CliArgumentsTests.cs` | yes |
| §13.19 [299e4119] | Exit code 3 Refused | `tests/Berth.Tests/Cli/RestoreCommandTests.cs` | yes |
| §13.20 [d443256d] | Exit 3 Busy & UnreadableLayout | `tests/Berth.Tests/Cli/SaveCommandTests.cs` | yes |
| §13.21 [c5ea042f] | list reports unreadable, exits 0 | `tests/Berth.Tests/Cli/ListCommandTests.cs` | yes |
| §13.22 [11f4d33b] | Detached console manifest Win11 24H2 | `tests/Berth.Tests/Cli/ConsoleBehaviourTests.cs` | yes |
| §14.1 [10364d5c] | Data/local roots & BERTH_DATA | `tests/Berth.Tests/Core/BerthPathsTests.cs` | yes |
| §14.2 [54885e09] | Test mode env vars scoped | `tests/Berth.Tests/Desktop/ScopedDesktopIconsTests.cs` | yes |
| §14.3 [0c62717b] | layouts/<key>.json per arrangement | `tests/Berth.Tests/Core/LayoutStoreTests.cs` | yes |
| §14.4 [c2452336] | Layout JSON schema v1 | `tests/Berth.Tests/Core/LayoutJsonTests.cs` | yes |
| §14.5 [a3e306ba] | Example key c7f017905ce518e6 | `tests/Berth.Tests/Core/ArrangementKeyTests.cs` | yes |
| §14.6 [61e61454] | id, name, previous schema fields | `tests/Berth.Tests/Core/LayoutJsonTests.cs` | yes |
| §14.7 [9fe743a4] | settings.json schema & window px | `tests/Berth.Tests/Core/SettingsStoreTests.cs` | yes |
| §14.8 [fb63b1ca] | undo.json snapshot & delete on undo | `tests/Berth.Tests/Core/UndoTests.cs` | yes |
| §14.9 [444adcf9] | logs/berth.log under local root | `tests/Berth.Tests/Core/LogTests.cs` | yes |
| §14.10 [5f2ec81e] | Temp write, flush, read-back, swap | `tests/Berth.Tests/Core/JsonFileTests.cs` | yes |
| §14.11 [9a0b29ff] | Invalid JSON untouched & skipped | `tests/Berth.Tests/Core/LayoutStoreTests.cs` | yes |
| §14.12 [e1d06847] | 1 to 64 monitors schema bound | `tests/Berth.Tests/Core/LayoutJsonTests.cs` | yes |
| §14.13 [d0ebcbfd] | Locked in-place save & redo note | `tests/Berth.Tests/Core/JsonFileTests.cs` | yes |
| §14.14 [7a24bb14] | Unreadable undo.json treated missing | `tests/Berth.Tests/Core/UndoTests.cs` | yes |
| §14.15 [f2add737] | SHA-256 16-hex arrangement key | `tests/Berth.Tests/Core/ArrangementKeyTests.cs` | yes |
| §15.1 [9e90d4e6] | Read icons via IFolderView only | `tests/Berth.Tests/Desktop/ReadIconsTests.cs` | yes |
| §15.2 [b11a3063] | Save naming & previous rotation | `tests/Berth.Tests/Core/SaveTests.cs` | yes |
| §15.3 [2f1dd597] | Restore refusal order & read-back | `tests/Berth.Tests/Core/RestoreTests.cs` | yes |
| §15.4 [b1833ca3] | In place exact read-back match | `tests/Berth.Tests/Core/RestoreTests.cs` | yes |
| §15.5 [3f64919c] | Undo checks key, deletes on full | `tests/Berth.Tests/Core/UndoTests.cs` | yes |
| §15.6 [0ea223e6] | Physical pixel coordinate roundtrip | `tests/Berth.Tests/Desktop/SetPositionsTests.cs` | yes |
| §15.7 [7e151644] | Match icons by parsing name CI | `tests/Berth.Tests/Core/RestoreTests.cs` | yes |
| §15.8 [c780a142] | 5s operation lock across tray/CLI | `tests/Berth.Tests/Core/OperationLockTests.cs` | yes |
| §16.1 [ab840655] | Tray only while Restore auto is on | `tests/Berth.Tests/App/AutoRestoreTests.cs` | yes |
| §16.2 [5a40f065] | WM_DISPLAYCHANGE/Taskbar... | `tests/Berth.Tests/App/AutoRestoreTests.cs` | deferred (D99 T01 §2) |
| §16.3 [f45fdf73] | No layout does & saves nothing | `tests/Berth.Tests/App/AutoRestoreTests.cs` | yes |
| §16.4 [4ee9a9bb] | WARN/FAIL/INFO log & balloon trigger | `tests/Berth.Tests/App/BalloonTests.cs` | yes |
| §16.5 [7f7a620d] | Visible window status + Undo | `tests/Berth.Tests/App/AutoRestoreWindowTests.cs` | yes |
| §17.1 [0a4852f8] | First run opens window; later tray | `tests/Berth.Tests/App/StartupPlanTests.cs` | yes |
| §17.2 [472dc3f7] | Single instance pipe activates open | `tests/Berth.Tests/App/SingleInstanceTests.cs` | yes |
| §17.3 [855386a9] | CLI runs alongside tray; poll sees | `tests/Berth.Tests/Cli/ProcessTests.cs` | yes |
| §17.4 [4e141acf] | Unknown arg exits 2, no tray start | `tests/Berth.Tests/Cli/CliArgumentsTests.cs` | yes |
| §18.1 [02380469] | Every refusal and failure has one... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.2 [f03b960a] | Auto arrange is on "Auto arrange... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.3 [bab0ac05] | No layout for this arrangement "T... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.4 [c780542e] | No previous save "This layout has... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.5 [60de514c] | The desktop cannot be reached "Be... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.6 [ff8a03e5] | The desktop is lost while placing... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.7 [a2d67f36] | The displays changed mid-operatio... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.8 [f3148959] | Nothing to undo "There is nothing... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.9 [cf0aa620] | Icons did not stay after a restor... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.10 [61afc81f] | Another operation is running "Ber... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.11 [7be11449] | A file cannot be written "Berth c... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.12 [4e69599e] | A folder cannot be read "Berth co... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.13 [9d13933f] | A file cannot be read "The layout... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.14 [765f638e] | The settings file cannot be read... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.15 [6d30f445] | Auto arrange would not turn off "... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.16 [9bc3e9ab] | The Run value cannot be written "... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.17 [e02ba2da] | Anything else "Something went wro... | `tests/Berth.Tests/Core/MessagesTests.cs` | yes |
| §18.18 [11017d24] | berth.log format INFO/WARN/FAIL | `tests/Berth.Tests/Core/LogTests.cs` | yes |
| §18.19 [acadd001] | Unreadable settings.json WARN | `tests/Berth.Tests/Core/SettingsStoreTests.cs` | yes |
| §18.20 [5212e53f] | Setting change INFO/FAIL log lines | `tests/Berth.Tests/Core/SettingChangesTests.cs` | yes |
| §18.21 [fcb61bf3] | Window edit logs & reason trimming | `tests/Berth.Tests/Core/LayoutEditTests.cs` | yes |
| §18.22 [7b270322] | 1 MB log rotation & [cut] cap | `tests/Berth.Tests/Core/LogTests.cs` | yes |
| §18.23 [0d028262] | Unhandled exception log, box, exit 1 | `tests/Berth.Tests/App/CrashHandlerTests.cs` | yes |
| §18.24 [db2176d4] | No previous save revert message | `tests/Berth.Tests/Core/LayoutEditTests.cs` | yes |
| §19.1 [ffefec80] | Layouts pane 5 states rendered | `tests/Berth.Tests/Window/LayoutsPaneTests.cs` | yes |
| §19.2 [aa0b84e6] | Detail header 4 states rendered | `tests/Berth.Tests/Window/DetailHeaderTests.cs` | yes |
| §19.3 [4270ccc2] | Position map 8 states rendered | `tests/Berth.Tests/Window/PositionMapTests.cs` | yes |
| §19.4 [f7fb3245] | Status line all §8 states rendered | `tests/Berth.Tests/Window/StatusLineTests.cs` | yes |
| §19.5 [1c02cd79] | Notices none/single/stacked | `tests/Berth.Tests/Window/NoticesTests.cs` | yes |
| §19.6 [3b3d0892] | Dialogs Rename errors & Delete | `tests/Berth.Tests/Window/RenameTests.cs` | yes |
| §19.7 [7d0ed768] | Tray dark/light, menu, checkmark | `tests/Berth.Tests/App/TrayMenuTests.cs` | yes |

## Operator-owned live-hardware observations

Three rows above carry `deferred (D99 T01 §N)` because their automated unit/fake suites pass while live-hardware or unlocked-desktop observation requires the owner:

- `§9.5` -> `D99 T01 §4` (`todo/99-manual/TODO-01-operator-steps.md`): opt-in Auto Arrange round-trip (`BERTH_TEST_ALLOW_AUTOARRANGE=1`) on a disposable interactive Windows 11 desktop.
- `§12.8` -> `D99 T01 §4` (`todo/99-manual/TODO-01-operator-steps.md`): live unlocked-desktop Windows 11 warning notification capture and click-to-open check.
- `§16.2` -> `D99 T01 §2` (`todo/99-manual/TODO-01-operator-steps.md`): physical dock and undock walkthrough on real monitors.

## Filed follow-ups

- No new follow-up items were filed by this sweep; the three operator-only hardware/unlocked-desktop checks above were already tracked in `D99 T01 §2` and `D99 T01 §4`, and `todo/findings.md` has no new unrelated defects.
