# Berth interface and behaviour spec

This file is the design contract. It says what Berth looks like and what it does, and a TODO section with `Surface: yes` cites a section here on its `Fidelity:` line. A surface is done only when every control, state and message named below is working, or deferred to a named owner section. This file wins over the mockups, the README and memory.

Decisions behind it were agreed with the owner on 2026-11-15: the "Grid" identity (icon, blue, Segoe UI Variable) with the "Chart" interface (two panes and a plotted position map), in light and dark.

- Mockups: [mockups/main-window-light.png](./mockups/main-window-light.png) and [mockups/main-window-dark.png](./mockups/main-window-dark.png), with their HTML sources beside them. They show the data state of §5 to §8 and the tray menu of §12 with sample data. They were drawn before the "More" button of §6 and the version caption of §5 were decided, so they lack both: this file wins.
- Tokens: [tokens.json](./tokens.json).
- Brand assets: [`assets/`](../assets), listed in the README.

## 1. Policy: how the contract binds

- **Design first.** A change to how something looks or behaves is made here first, dated, naming the section that made it, and the code follows in the same commit range. Code never leads the spec, and a surface is never tuned to a value this file and `tokens.json` do not name.
- **Tokens only.** Every colour, size, radius and type style a Berth surface sets comes from `tokens.json` through the generated resource dictionaries (§2). No XAML or C# file under `src/` holds a hex colour, and the test of §2 fails when one does.
- **Fluent controls stay Fluent.** Buttons, switches, text boxes, menus, dialogs, list selection and the title bar use the WPF Fluent theme as it ships, with the Windows accent colour. Berth does not restyle them. Berth's own blue is for the icon, the position map and the Active badge.
- **Every state is drawn.** Each surface has the states listed in §19. A state this file lists and the surface lacks is a missing feature.
- **Never colour alone.** Every status has a word, and a marker's state also changes its shape (§7).
- **Fidelity.** A surface is faithful when its layout matches the mockup's structure and this file's measurements within 2 px at 100% scale, its text matches the copy given here exactly, and it holds in light, dark and high contrast at 100% and 150% scale.
- **Deviation.** Code that cannot match gets a dated line in this file under the section it deviates from, with the reason and the TODO section that will close it. A silent deviation is a defect.
- **Copy.** Sentence case, no exclamation marks, no "please", no ellipsis in button labels unless a dialog follows. Numbers of icons are always stated ("42 icons"), never "all".

## 2. Brand and tokens

- Icon, tray icons, wordmark, colours and type are as the README's "Brand and interface" section records, and the files are in `assets/`.
- `assets/icon/berth.ico` is the application icon (the executable, the window, the taskbar). The tray uses `berth-tray-dark.ico` on a dark taskbar and `berth-tray-light.ico` on a light one (§12).
- `tokens.json` is the only source of Berth's own values. A build step generates the resource dictionaries under `src/Berth/Theme/` from it (`Tokens.Shared.xaml` for sizes, radii, type and timing, and `Tokens.Light.xaml`, `Tokens.Dark.xaml` and `Tokens.HighContrast.xaml` for colours), and the generated files are never edited by hand. Berth merges the colour dictionary that matches the Windows theme and swaps it live when the theme changes. A test regenerates the files and fails when a committed one differs.
- High contrast: Berth's own colours are replaced by the system colours (`WindowText`, `Window`, `Highlight`, `GrayText`), so the map and badges stay legible in every high contrast theme.

## 3. Shared rules

- Theme follows Windows (`ThemeMode="System"`): light, dark and high contrast, changing live with no restart.
- Per-monitor DPI awareness version 2. Every position Berth stores or shows is in physical pixels of the virtual desktop, as the shell reports it.
- Every interactive control is reachable by Tab in reading order, shows the theme's focus visual, has an automation name, and is at least 32 px high. Map markers are the one exception: a marker's target is 19 px square (the 11 px marker plus 4 px on each side), because markers sit as close together as the desktop's own icon grid, and every marker is reachable with the arrow keys (§7).
- Text wraps rather than clips. Only a layout's name may be trimmed with an ellipsis, in the layouts pane and in the detail header, and its tooltip then gives the full name.
- Dates read "today, 09:14", "yesterday, 17:02" or "12 November" (or "12 November 2025" when the year differs from the current year in the user's time zone, with "today" and "yesterday" taking precedence across New Year), in the user's time zone and 24-hour time, with English month names from the invariant culture. Clarified 2026-11-18 (D01 T01 §17): SPEC §3 showed only the same-year absolute date without stating the other-year form, the New Year precedence of "today" and "yesterday", or invariant English month names.
- Monitor sizes read "2560 × 1440", joined by " and " for two monitors and by commas with a final " and " for more.
- Berth never moves an icon except in a restore, an undo or a revert the user or the automatic restore asked for.
- Berth writes only under its own data folders (§14), the `Run` registry value of §11 and, on the user's click, the desktop's auto arrange flag (§9).

## 4. Main window

- Title "Berth", the standard Windows title bar and the application icon. Default size 980 × 640, minimum 760 × 520. The window remembers its size and position (§14) and opens on a visible monitor: a remembered position that is now off screen is ignored.
- Two panes: the layouts pane (§5), 250 px wide, on the left, and the layout detail (§6 to §9) filling the rest. A 1 px divider separates them.
- Closing the window hides it and leaves the tray app running. `Esc` does not close it.
- The window is opened from the tray (§12), by starting `berth.exe` again with no arguments (§17), and on the very first run.
- While the window is visible Berth re-reads the desktop every 5 seconds and when the window is activated. A hidden window reads nothing.

## 5. Layouts pane

- Heading "Layouts" (`bodyStrong`).
- One row per saved layout, sorted with the active layout first and the rest by name. Each row shows the name (`bodyStrong`) and a second line "2 monitors · 42 icons" (`caption`, secondary text). One monitor reads "1 monitor".
- The active layout is the one saved for the monitor arrangement connected now. Its row carries the badge "Active" (§2 colours, `caption`, weight 600).
- When the current arrangement has no saved layout, a first row reads "This arrangement" with the second line "Not saved yet", and it is selected when the window opens.
- Exactly one row is selected. The selected row has the theme's list selection and a 3 px accent marker on its left edge. Arrow keys move the selection.
- A layout whose file cannot be read is not listed. The detail pane shows the notice of §9 naming the file.
- Below the rows, pinned to the bottom of the pane: the two switches of §11, then the caption "Berth 0.1.0" with the running version.

## 6. Layout detail: header and actions

- Title: the layout's name (`title`), or "Not saved yet" for the unsaved current arrangement.
- Under it (`caption`, secondary): the monitor sizes, then " · saved today, 09:14". The unsaved arrangement shows only the monitor sizes.
- For the active layout the header's right side holds, in order: "Restore" (standard button), "Save current layout" (accent button) and a "More" button (three dots, automation name "More options").
- For the unsaved current arrangement it holds only "Save current layout" (accent button).
- For a layout saved for another arrangement it holds only "More", and a line under the header reads "Saved for another monitor arrangement. Connect those monitors to restore it."
- "Save current layout" saves the desktop as it is now to the current arrangement's layout (§15), selects that layout, and sets the status line of §8. It asks nothing: the save it replaces is kept for "Revert to previous save".
- "Restore" puts the icons back (§15) and sets the status line of §8.
- "More" opens a menu: "Rename", "Revert to previous save" and "Delete" (§10). "Revert to previous save" is enabled only when the layout holds a previous save.
- While a save, restore, undo or revert runs, the three buttons are disabled and the status line reads "Working".

## 7. Position map

- A panel under the header that fills the remaining height (minimum 220 px), with a faint 20 px grid, drawn from the tokens of `tokens.json`.
- It draws every monitor of the layout's arrangement as a rectangle, scaled together to fit the panel with their real relative positions and proportions, centred, with 24 px of padding. Above each rectangle's top-left corner a label reads "1 · 2560 × 1440" (`mono`), numbered left to right then top to bottom, and the primary monitor's label ends " · primary".
- Each saved icon is plotted as a marker at its saved position: an 11 px square with 3 px corners.
- For the active layout a marker's state compares the save with the desktop now:

| State | Marker | Readout word |
| ----- | ------ | ------------ |
| In place | Filled, `markerInPlace` | "in place" |
| Moved | Outlined 2 px, `markerMoved`, with a 1 px line to where the icon is now | "moved" |
| Missing | Outlined 1 px dashed, `markerMissing` | "not on the desktop" |

- For a layout of another arrangement every marker is filled `markerNeutral` and carries no state. For the unsaved arrangement the map plots where the icons are now, filled `markerNeutral`.
- Hovering a marker shows a tooltip with the icon's name. Clicking it, or moving to it with the arrow keys, selects it: the marker turns `markerSelected` with a 23 px ring, and a dashed line drops to the readout.
- The readout, bottom left of the panel: the icon's name (`monoStrong`), and under it "x 1436 · y 702 · monitor 1 · in place" (`mono`, secondary). With nothing selected it reads "Select an icon to see its position."
- The count, bottom right (`mono`, secondary): "42 of 42 in place" for the active layout, "42 icons" otherwise.
- The map is a list to assistive technology: each marker is an item named "Notes.txt, x 1436, y 702, monitor 1, in place", in reading order (top to bottom, then left to right).
- An icon whose saved position lies outside every monitor of the arrangement is plotted at the nearest edge of the nearest monitor and its readout adds " · off screen".

## 8. Status line

A card under the map with a 10 px square in `markerInPlace`, a sentence, and on the right either a time (`caption`, secondary) or an action. The sentence is the first of these that applies:

| When | Sentence | Right side |
| ---- | -------- | ---------- |
| An operation is running | "Working" | nothing |
| A restore just finished | "Restored 42 icons.", or the partial sentence below | "Undo" button |
| An undo just finished | "Put 42 icons back where they were before the restore.", or "Put 40 of 42 icons back. 2 did not stay where they were." | nothing, or "Undo" again when some did not stay |
| A save just finished | "Saved 42 icons." or, over an earlier save, "Saved 42 icons. The previous save is kept." | nothing |
| A revert just finished | "Reverted to the save from 12 November." | nothing |
| An operation was refused or failed | The message of §18 | "Retry" button when retrying can help |
| Active layout, nothing moved or missing | "Every icon is at its saved position." | "Checked 09:14" |
| Active layout, some moved and some missing | "3 icons have moved and 2 are no longer on the desktop." | "Checked 09:14" |
| Active layout, some moved, none missing | "3 icons have moved since this layout was saved." | "Checked 09:14" |
| Active layout, some missing, none moved | "2 saved icons are no longer on the desktop." | "Checked 09:14" |
| Unsaved arrangement | "This monitor arrangement has no saved layout yet." | nothing |
| Another arrangement's layout | "Saved 12 November for another monitor arrangement." | nothing |

A partial restore reads "Restored 38 of 42 icons." followed by one or both of "2 did not stay where they were saved." and "2 are no longer on the desktop.", in that order. An icon "did not stay" when it is on the desktop but its position read back after the restore is not its saved position.

A partial undo (when some snapshot icons did not stay or are no longer on the desktop) reads "Put 40 of 42 icons back." followed by one or both of "2 did not stay where they were." ("1 did not stay where it was." for one) and "2 are no longer on the desktop." ("1 is no longer on the desktop." for one), in that order, with no layout name. Clarified 2026-11-19 (D01 T01 §16): §15 counts an undo with missing icons and none out of place as done, while §8 showed only the not-placed partial undo.

One icon reads "1 icon has moved", "1 saved icon is no longer on the desktop", "1 did not stay where it was saved." and "Restored 1 icon." A just-finished sentence stays until the next operation, a selection change in the layouts pane, or the next desktop read that finds a change.

## 9. Notices

A notice is a bar between the header and the map with an icon, a sentence and at most one button. Several notices stack, most serious first.

| Notice | Kind | Text | Button |
| ------ | ---- | ---- | ------ |
| Auto arrange is on | Warning | "Auto arrange icons is on. Windows ignores saved positions while it is." | "Turn it off" |
| The desktop cannot be reached | Error | "Berth cannot reach the desktop. Explorer may be restarting." | "Retry" |
| A layout file cannot be read | Error | "The layout file `<file name>` could not be read. It was left untouched." | "Open folder" |

- "Turn it off" clears the desktop's auto arrange flag, reads it back, and removes the notice only when the read-back says it is off. If it is still on the notice stays and the status line carries the §18 message.
- While auto arrange is on, "Restore" is disabled and its tooltip repeats the notice. "Save current layout" still works.
- While the desktop cannot be reached, "Restore" and "Save current layout" are disabled, the map shows the saved positions with no state, and Berth retries on its 5 second read.
- "Open folder" opens the layouts folder in File Explorer with the file selected.

## 10. Rename, revert and delete

- **Rename** opens a dialog titled "Rename layout" with one text box holding the current name, selected, and the buttons "Rename" (default) and "Cancel". A name is 1 to 40 characters after trimming and unique among layouts, ignoring case. A name that breaks a rule shows its reason under the box ("Enter a name.", "A name can be at most 40 characters.", "Another layout already has this name.") and "Rename" stays disabled until it is fixed. `Enter` renames, `Esc` cancels.
- **Revert to previous save** swaps the layout's current save with its previous one, so choosing it twice returns to where it started. It moves no icon: "Restore" does that.
- **Delete** opens a dialog titled "Delete layout" with the text "Delete the layout "Docked"? Its saved positions are removed. This cannot be undone." and the buttons "Delete" and "Cancel" (default). Deleting the active layout leaves the icons where they are and selects "This arrangement".
- A rename, revert or delete is written to disk and read back before the window shows it (§14).

## 11. Settings switches

Two switches at the bottom of the layouts pane, label on the left and switch on the right, with no On or Off text.

- **Restore automatically.** On: Berth restores the active layout when the monitor arrangement changes or Explorer restarts (§16). Off by default.
- **Start with Windows.** On: the value `Berth` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` holds the quoted path of the running `berth.exe`. Off: the value does not exist. The switch shows what the registry holds: On only when the value names the running executable. Off by default.
- A switch applies at once, is written and read back, and returns to its old state with the §18 message in the status line when the write fails.

## 12. Tray icon and menu

- One icon in the notification area while Berth runs. It uses `berth-tray-dark.ico` when the taskbar is dark and `berth-tray-light.ico` when it is light (`SystemUsesLightTheme` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`), and switches live when that setting changes.
- Tooltip: "Berth: Docked, saved today, 09:14", or "Berth: no layout for this monitor arrangement".
- A left click or a double click opens the main window and brings it to the front.
- A right click opens the menu, in this order: "Save layout", "Restore layout", "Restore automatically" (checked when on), a separator, "Open Berth" (bold, the default) and "Exit".
- "Save layout" and "Restore layout" do what the window's buttons do. "Restore layout" is disabled when the current arrangement has no layout or auto arrange is on.
- "Exit" removes the icon and ends the process.
- After Explorer restarts (the `TaskbarCreated` message) Berth adds its icon again.
- Berth shows a balloon only for a failure nobody would otherwise see: an automatic restore that was refused or failed (§16). Its title is "Berth could not restore your icons", its text the §18 message, and clicking it opens the main window. A success shows nothing.

## 13. Command line

`berth.exe` with a command does the work, prints the result, and exits. It never shows a window or a tray icon, and works whether or not the tray app is running.

| Command | Does | Prints on success |
| ------- | ---- | ----------------- |
| `berth save [--name <name>]` | Saves the desktop to the current arrangement's layout | `Saved 42 icons to "Docked".` |
| `berth restore` | Restores the current arrangement's layout | `Restored 42 icons from "Docked".` |
| `berth undo` | Puts icons back where they were before the last restore | `Put 42 icons back.` |
| `berth list` | Lists the saved layouts | One line per layout (below) |
| `berth --help`, `-h`, `/?` | Prints the usage text | The usage text |
| `berth --version` | Prints the version | `Berth 0.1.0` |

- `berth list` prints, per layout, an asterisk for the active one, the name, the monitor count, the icon count and the save date: `* Docked        2 monitors  42 icons  2026-11-15 09:14`. With no layouts it prints `No layouts saved yet.`
- `--name` on a new layout names it, and on an existing one renames it, under the rules of §10.
- A restore where some saved icons are no longer on the desktop prints `Restored 40 of 42 icons from "Docked". 2 are no longer on the desktop.` and still exits 0.
- A restore or undo where an icon that is on the desktop did not stay at its position prints the §8 partial sentence (with ` from "Docked"` after the count for a restore) to standard error and exits 1. An undo that ends this way keeps its snapshot, so `berth undo` can be run again.
- An undo where some snapshot icons are no longer on the desktop and none failed to stay prints the §8 partial undo sentence (for example `Put 2 of 3 icons back. 1 is no longer on the desktop.`) to standard output and exits 0; when an icon did not stay, it prints the §8 partial undo sentence (for example `Put 1 of 3 icons back. 1 did not stay where it was. 1 is no longer on the desktop.`) to standard error and exits 1, with no layout name in either case. Clarified 2026-11-19 (D01 T01 §16).
- Any internal line break (`\r\n`, `\r` or `\n`, or a contiguous run of them) inside a layout name, path or origin in a single-line CLI, status or log sentence is written as a single space (so `"Work\r\nDesk"` appears as `"Work Desk"`), and any other nonprinting control character (`char.IsControl`, such as backspace or escape) is written as lowercase `\uXXXX` (so `"AB\bC"` appears as `"AB\u0008C"`). Clarified 2026-11-19 (D01 T01 §16).
- Results go to standard output, and refusals and errors to standard error, each as one line.

| Exit code | Meaning |
| --------- | ------- |
| 0 | Done |
| 1 | An error: a write failed, an icon did not stay where it was put, or something unexpected; the message says which |
| 2 | Wrong usage: an unknown command or option, a missing or invalid `--name` |
| 3 | Refused: auto arrange is on, no layout for this arrangement, nothing to undo, or the desktop cannot be reached |

- The executable is a console program whose manifest sets the console allocation policy to detached, so starting it from the Start menu, a shortcut or the `Run` key opens no console window, while a command typed in a terminal prints there and the terminal waits for it. Only recent Windows 11 builds honour that policy: Berth's stated minimum is Windows 11 version 24H2, and `D03 T01 §2` proves the behaviour on the build machine and records the lowest build it was shown on.

## 14. Storage

- Data root: `%APPDATA%\Berth`. Local root: `%LOCALAPPDATA%\Berth`. When the environment variable `BERTH_DATA` is set, both roots are that folder (`<BERTH_DATA>\roaming` and `<BERTH_DATA>\local`): tests set it and never touch the user's own data.
- Test mode exists only while `BERTH_DATA` is set, so it can never touch a user's real data. In it, `BERTH_TEST_ICON_PREFIX` limits Berth to the desktop icons whose display name starts with that text (it reads, saves and moves no other icon), `BERTH_TEST_HOLD_MS` keeps a command alive that many milliseconds (at most 5000) before it exits, and `BERTH_TEST_REPORT` names a file the process writes one line to, `console=0` or `console=1`, saying whether it has a console.
- `layouts\<key>.json` under the data root: one file per monitor arrangement.

```json
{
  "schemaVersion": 1,
  "key": "c7f017905ce518e6",
  "name": "Docked",
  "savedAt": "2026-11-15T07:14:03Z",
  "monitors": [
    { "x": 0, "y": 0, "width": 2560, "height": 1440, "scale": 100, "primary": true }
  ],
  "icons": [
    { "id": "::{645FF040-5081-101B-9F08-00AA002F954E}", "name": "Recycle Bin", "x": 76, "y": 2 }
  ],
  "previous": null
}
```

- Corrected 2026-11-18 (D01 T01 §2): the example's `key` read `3f9a61c2d0b47e15`, which is not the arrangement key of its one monitor; `c7f017905ce518e6` is (the last bullet of this section).
- `id` is the icon's parsing name and `name` its display name. `previous` is `null` or `{ "savedAt": ..., "icons": [...] }`, the one save this one replaced.
- `settings.json` under the data root: `{ "schemaVersion": 1, "restoreAutomatically": false, "window": { "x": 100, "y": 100, "width": 980, "height": 640 } }`, where `window` is `null` until the window has been closed once. `window` is in physical pixels of the virtual desktop, like every other position Berth stores.
- `undo.json` under the local root: the positions read just before the last restore, with the arrangement key and the time. An undo deletes it.
- `logs\berth.log` under the local root (§18).
- Every write goes to a temporary file in the same folder, is flushed, is read back and compared, and only then replaces the target in one step. A write that fails at any point leaves the old file as it was.
- A layout or settings file that is not valid JSON, has another `schemaVersion`, or breaks the schema is never overwritten or deleted by Berth: it is reported (§9, §18) and skipped, and a save that would replace it is refused with the "could not be read" message of §18. A missing `settings.json` means the defaults.
- A layout names 1 to 64 monitors; a file naming more breaks the schema. No real arrangement comes near it, and the limit bounds the work of comparing a damaged file with the desktop. Added 2026-11-18 (D01 T01 §13).
- A save over an existing file locks it so that no other program can read, write, rename or delete it, checks under that lock that it still holds exactly what Berth read, and only then writes the new contents in place, which keeps the file's own permissions: in one write padded with trailing spaces to at least the old length, then one trim, so that a process ending at any moment leaves the old contents or the new ones, never a mixture (a power loss below the file system can still tear a write). A file that changed or that another program holds is never written. A redo note (`<name>.berth-pending`) taken first lets the next read finish a save that a crash interrupted, and only over the version it had read: a file that changed after the crash is left exactly as it is, with Berth's intended save beside it as `<name>.berth-replaced-<id>`, which Berth never deletes. Added 2026-11-18 (D01 T01 §6); corrected the same day from a replace-and-roll-back design that let another program glimpse the proposed file.
- `undo.json` is Berth's own scratch file: one that cannot be read counts as missing and is replaced by the next restore.
- The arrangement key is the first 16 lowercase hexadecimal characters of the SHA-256 of the UTF-8 text of the monitors, each written `x,y,width,height,scale` in plain decimal integers (ASCII `-` for a negative value, whatever the regional format) and sorted by `x`, then `y`, then `width`, `height` and `scale`, joined by `;`. The same monitors in the same places at the same scale always give the same key. Clarified 2026-11-18 (D01 T01 §2): the text format, the case and the tie-breakers after `y` were unstated.

## 15. Save and restore rules

- **Reading the desktop.** Berth reads every icon's parsing name, display name and position through the shell's folder view of the desktop (`IFolderView`), as the README's "How icon positions are read and written" says, and never through Explorer's list-view window.
- **Save.** Reads every icon and the monitors, and writes the layout of the current arrangement. A first save of an arrangement names it "1 monitor" or "N monitors", adding " (2)", " (3)" and so on when the name is taken. A later save keeps the name and moves the earlier `icons` and `savedAt` into `previous`.
- **Restore.** Refused, checked in this order, when the desktop cannot be reached, when the current arrangement has no layout, or while auto arrange is on. Otherwise Berth writes `undo.json` with the positions now, sets every saved icon that is still on the desktop to its saved position, leaves icons that are not in the layout where they are, reads the desktop back, and reports how many saved icons are at their saved positions, how many are on the desktop but did not stay there, and how many are no longer on the desktop.
- **In place.** An icon is in place when the position read back equals the saved position exactly.
- **Undo.** Restores the positions in `undo.json` when its arrangement key is the current one, reads the desktop back, and deletes the file only when every icon that is still on the desktop is back in place. Refused, with "There is nothing to undo.", when the file is missing, cannot be read, or was written for another arrangement.
- **Positions.** A stored position is the icon's position in physical pixels of the virtual desktop. Berth converts from and to the folder view's own coordinates in one place, so a position read, stored and written back is the position the view reported.
- **Matching.** Icons are matched by parsing name, compared ignoring case. A display name is never used to match.
- Save, restore, undo and revert are one at a time across the tray app and the command line: a second one waits up to 5 seconds for the first and is then refused with the §18 message.

## 16. Automatic restore

- Runs only in the tray app, and only while "Restore automatically" is on.
- Triggered by a display change (`WM_DISPLAYCHANGE`) and by Explorer restarting (`TaskbarCreated`). Berth waits until 3 seconds have passed with no further trigger, so Explorer finishes its own rearranging first, then restores the layout of the arrangement connected at that moment.
- An arrangement with no layout does nothing and saves nothing.
- A refusal writes a WARN line and a failure a FAIL line to the log, and either shows the balloon of §12, once per trigger. A restore where an icon did not stay at its saved position counts as a failure. A success writes an INFO line and shows nothing. Each line ends with the trigger, as in ` (automatic, display change)`.
- When the main window is visible, its status line shows the result as for a manual restore, with "Undo".

## 17. Single instance and startup

- `berth.exe` with no arguments starts the tray app. On the very first run (no `settings.json`) it also opens the main window. On later runs it starts in the tray only.
- One tray app per user and logon session. Starting `berth.exe` with no arguments while one is running asks the running one to open its main window and exits with code 0, having shown nothing itself.
- `berth.exe` with a command (§13) runs that command and exits, whether or not the tray app is running. The tray app notices a layout the command line changed the next time it reads (§4).
- An unknown first argument is wrong usage (§13): it never starts the tray app.

## 18. Errors and the log

Every refusal and failure has one message, used in the status line, on standard error and in the balloon:

| Cause | Message |
| ----- | ------- |
| Auto arrange is on | "Auto arrange icons is on, so Windows would ignore the saved positions. Turn it off and try again." |
| No layout for this arrangement | "This monitor arrangement has no saved layout." |
| No previous save | "This layout has no previous save." |
| The desktop cannot be reached | "Berth cannot reach the desktop. Explorer may be restarting." |
| The desktop is lost while placing icons | "The desktop could not be reached while the icons were being placed. Undo puts back what was there." (for a restore), or "The desktop could not be reached while the icons were being placed." (for an undo) |
| The displays changed mid-operation | "The displays changed before any icon was moved." (for a restore), or "The displays changed before the undo could be checked." (for an undo) |
| Nothing to undo | "There is nothing to undo." |
| Icons did not stay after a restore or undo | The partial sentence of §8 |
| Another operation is running | "Berth is busy with another save or restore. Try again in a moment." |
| A file cannot be written | "Berth could not write `<path>`: `<the system's reason>`." |
| A folder cannot be read | "Berth could not read `<path>`: `<the system's reason>`." |
| A file cannot be read | "The layout file `<file name>` could not be read. It was left untouched." |
| The settings file cannot be read | "The settings file `settings.json` could not be read. It was left untouched." |
| Auto arrange would not turn off | "Windows did not turn auto arrange off. Turn it off from the desktop: right-click, View, Auto arrange icons." |
| The `Run` value cannot be written | "Berth could not change the Start with Windows setting: `<the system's reason>`." |
| Anything else | "Something went wrong: `<the exception's message>`. The details are in the log." |

- The log is `logs\berth.log` under the local root: one line per event, `2026-11-15T07:14:03Z INFO Saved 42 icons to "Docked".`, with the level INFO, WARN or FAIL. Every save, restore, undo, revert, rename, delete, setting change, automatic restore and failure writes a line. An unexpected error also writes the exception with its stack.
- An unreadable `settings.json` is logged as a `WARN` line with its message when the tray app starts, Berth runs on the defaults without writing the file, and a setting change is refused with that message instead of a write failure. Added 2026-11-18 (D01 T01 §6): SPEC §14 asked for the file to be reported without saying how.
- A setting change writes `INFO Restore automatically turned on.`, `INFO Restore automatically turned off.`, `INFO Start with Windows turned on.` or `INFO Start with Windows turned off.`, from the window or the tray alike; one that fails writes a `FAIL` line with its message from the table above. Added 2026-11-18 (D01 T01 §18): the bullet above asks for a line per setting change without giving its words.
- Window-only layout edits log one `INFO` sentence each: `INFO Reverted "<name>" to the save from <UTC savedAt>.` (using `yyyy-MM-ddTHH:mm:ssZ`), `INFO Renamed layout to "<name>".` and `INFO Deleted layout "<name>".`. Mid-operation desktop-loss, mid-operation display-change and folder-read failures use their explicit table rows above (not "Something went wrong"). In `<the system's reason>` and `<the exception's message>`, line breaks collapse to single spaces, all trailing spaces and full stops are trimmed together before the sentence's own full stop so no message ends with `..`, and a blank reason reads `unknown error`; internal line breaks and nonprinting control characters in names, paths, origins and reasons follow §13 (`\uXXXX`). Clarified 2026-11-19 (D01 T01 §16): SPEC §18 requires those lines and embeds OS/exception strings without specifying the three sentences, mid-operation failure rows or punctuation/control normalization.
- The log is capped at 1 MB: when it passes that, it is renamed `berth.log.1`, replacing the last one, and a new file starts. Corrected 2026-11-18 (D01 T01 §15): the event that would take the file past 1 MB is the first line of the new file, so neither file passes 1 MB; one event larger than 1 MB on its own is cut to fit and ends ` [cut]`.
- An unhandled exception in the tray app is logged, shown in a message box that names the log file, and ends the process with code 1.
- Added 2026-11-18 (D01 T01 §12): the "No previous save" row covers a revert whose layout has no previous save (such as when a fresh save replaced a deleted layout after the More menu opened), instead of saying the monitor arrangement has no saved layout.

## 19. States of each surface

| Surface | States that must be drawn |
| ------- | ------------------------- |
| Layouts pane | No saved layouts (only "This arrangement"), one layout, several, active saved, active unsaved |
| Detail header | Active layout, unsaved arrangement, another arrangement's layout, operation running |
| Position map | In place, moved, missing, selected, nothing selected, neutral, off screen, desktop unreachable |
| Status line | Every row of the §8 table |
| Notices | None, each notice alone, two stacked |
| Dialogs | Rename with a valid name, with each error; Delete |
| Tray | Dark taskbar, light taskbar, menu with restore enabled and disabled, the checkmark on and off |

## 20. Out of scope

Decided on 2026-11-15, so a later session does not add them unasked: saving automatically, more than one layout per monitor arrangement, syncing layouts between machines, per-virtual-desktop layouts, arranging icons into patterns, importing other tools' layouts, an installer (Berth is one file), and telemetry or any network use.
