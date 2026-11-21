# Changelog

What changed in Berth, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## 0.1.0

### Added

- **Command line:** `berth save`, `restore`, `undo`, `list`, `--version`, and `--help` with documented console output and exit codes for scripting and quick desktop recovery.
- **Tray application:** single-instance Windows 11 notification-area icon (dark and light taskbar variants), context menu, failure balloon notifications, and automatic recovery when Explorer restarts.
- **Main window:** saved layouts list, active monitor position map with icon status readout, Save, Restore, Undo, Revert to previous save, Rename, Delete, inline notices, and light, dark, and high-contrast themes.
- **Automatic restore:** optional setting (off by default) that restores the active monitor arrangement 3 seconds after display changes or Explorer restarts and updates the open window and tray tooltip.
- **Packaging:** single self-contained `win-x64` `berth.exe` executable with embedded manifest, icon, and SHA-256 checksum sidecar.
- The brand: application icon, tray icons for dark and light taskbars, logo and banners in `assets/`.
- The design contract in `design/SPEC.md`, with its tokens and mockups.
- The architecture in `docs/plans/2026-11-15-berth-foundation.md` and the build plan in `todo/`.

Third-party components and licences bundled in `berth.exe` and used in the repository are listed in [NOTICE.md](https://github.com/rizonesoft/Berth/blob/v0.1.0/NOTICE.md).
