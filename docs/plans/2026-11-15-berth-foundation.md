# Berth foundation plan

Agreed with the owner on 2026-11-15. This is the architecture and the milestones the TODO tree in [`todo/`](../../todo/TODO-00-INDEX.md) executes. What Berth looks like and does is in [`design/SPEC.md`](../../design/SPEC.md), which wins where the two disagree on behaviour.

## What Berth is

One executable, `berth.exe`, for Windows 11 version 24H2 or later, x64. It saves the positions of the desktop icons to a layout file per monitor arrangement and puts them back. It has three faces: a tray app (no arguments), a main window opened from the tray, and a command line (`berth save`, `berth restore`, `berth undo`, `berth list`).

## Tech stack

| Part | Choice |
| --- | --- |
| Language and runtime | C# on .NET 11 (`global.json` pins SDK 11.0.100, roll forward to the latest feature band) |
| Main window | WPF with the built-in Fluent theme, `ThemeMode="System"` |
| Tray icon | Windows Forms `NotifyIcon`, hosted in the WPF app (`UseWPF` and `UseWindowsForms`) |
| Icon positions | The shell's `IFolderView` and `IFolderView2` COM interfaces |
| Storage | JSON through `System.Text.Json` source generation |
| Tests | xUnit v3 on Microsoft.Testing.Platform |
| Packaging | Single-file, self-contained, `win-x64` |

WPF was chosen over WinUI 3 because it publishes cleanly as one self-contained file, starts without a window for the tray and command-line modes, and matches the owner's other desktop projects. No third-party UI framework, MVVM library or dependency injection container is used: the app is small enough for hand-written view models and constructor arguments.

## Console and window from one executable

`berth.exe` is a console program (`OutputType` `Exe`) whose application manifest sets `consoleAllocationPolicy` to `detached`. Windows 11 24H2 and later then gives it no console when it is started from the Start menu, a shortcut or the `Run` key, while a command typed in a terminal inherits that terminal, prints there and is waited for. This is why 24H2 is the minimum. `D03 T01 §2` proves both halves on the build machine with real processes. If that proof fails, the fallback is a Windows program (`WinExe`) that attaches to its parent's console, at the cost that a terminal does not wait for it: the section records which one shipped.

## Layout of the repository

```text
Berth.slnx
global.json  Directory.Build.props  Directory.Packages.props
src/Berth/
  Berth.csproj        assembly name berth, the application icon, app.manifest
  Program.cs          entry point: a command goes to Cli, no arguments to App
  Core/               models, arrangement key, stores, save and restore rules: no WPF, no COM
  Desktop/            the shell's folder view, monitors, the Run value, taskbar theme
  Cli/                argument parsing, the commands, exit codes
  App/                tray app, single instance, crash handling, automatic restore
  Window/             the main window, its view models, the position map, dialogs
  Theme/Tokens.xaml   generated from design/tokens.json by scripts/build-tokens.py
tests/Berth.Tests/    unit, process and UI tests
scripts/              plan tooling, check-all.ps1, build-tokens.py, start-run.ps1
design/               SPEC.md, tokens.json, mockups
assets/               icons, logo, banners and their build script
```

One application project, as the owner asked: the layers are folders and namespaces, and `Core` never references WPF, Windows Forms or COM types, which a test enforces by reading the compiled types. Tests reach internals through `InternalsVisibleTo`.

## Seams that make it testable

- `IDesktopIcons` (read every icon, set positions, read and clear auto arrange) has two implementations: `ShellDesktopIcons` over `IFolderView`, and `FakeDesktopIcons` for tests.
- `IMonitors` (the connected monitors with bounds, scale and primary) has `SystemMonitors` and a fake.
- `BerthPaths` resolves the data and local roots and honours `BERTH_DATA`, so no test touches the user's own files.
- `LayoutService` joins them: save, restore, undo, revert, rename and delete, each returning a result the window, tray and command line all word the same way.
- A real-desktop test creates its own file on the desktop, moves only that icon and deletes it afterwards. No test moves an icon it did not create.

## How icon positions are read and written

- Get the desktop view: `IShellWindows.FindWindowSW` with `CSIDL_DESKTOP` and `SWC_DESKTOP`, then `IServiceProvider.QueryService(SID_STopLevelBrowser)` for `IShellBrowser`, then `QueryActiveShellView`, then cast to `IFolderView`.
- Read with `IFolderView.Items` and `IFolderView.GetItemPosition`. Write with `IFolderView.SelectAndPositionItems` and `SVSI_POSITIONITEM`.
- Identify each icon by its parsing name (`IShellFolder.GetDisplayNameOf` with `SHGDN_FORPARSING`), not its display name: the desktop merges the user's desktop, the public desktop and virtual items, and display names can collide.
- Auto arrange is `FWF_AUTOARRANGE` in `IFolderView2.GetCurrentFolderFlags`, cleared with `SetCurrentFolderFlags`.
- Never read positions by sending `LVM_GETITEMPOSITION` to Explorer's `SysListView32` window through `VirtualAllocEx` and `ReadProcessMemory`: that depends on Explorer's internal window layout and breaks between Windows builds.

## Milestones

| Phase | Name | What is true when it is done |
| --- | --- | --- |
| 0 | Repository and tooling | The solution builds and tests in one command and in CI |
| 1 | Core | Layouts, settings and the save and restore rules work against fakes |
| 2 | Desktop access | Berth reads and sets real icon positions and reads the monitors |
| 3 | Command line | `berth save`, `restore`, `undo` and `list` work from a terminal |
| 4 | Application shell | The tray app runs once per user, with its menu, window frame and theme |
| 5 | Main window | Every control and state of SPEC §5 to §11 works |
| 6 | Automatic restore | Icons return after a display change or an Explorer restart |
| 7 | Release | One self-contained `berth.exe`, documented, with a release workflow |
| 99 | Manual, operator only | Steps only the owner can take |

## Decisions that were open in the README and are now settled

| Question | Decision | Where |
| --- | --- | --- |
| Layout location | `%APPDATA%\Berth\layouts\`, one JSON file per arrangement | SPEC §14 |
| Monitor arrangement key | First 16 hex characters of a SHA-256 over each monitor's bounds and scale | SPEC §14 |
| Auto arrange | Detected, explained, and turned off on the user's click; restore is refused while it is on | SPEC §9, §15 |
| Icons added or removed since the save | Restore the ones that still exist, leave new ones, report the missing | SPEC §15 |
| Automatic restore trigger | `WM_DISPLAYCHANGE` and `TaskbarCreated`, after 3 quiet seconds | SPEC §16 |
