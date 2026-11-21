# Third-party notices

Berth is licensed under the [GNU General Public License, version 3 or later](LICENSE). This file lists the third-party components bundled in the self-contained `berth.exe` release and used in this repository.

## Shipped in berth.exe

| Component | Version | Licence | Where it is used |
| --- | --- | --- | --- |
| .NET 11 Runtime | 11.0 | MIT | Bundled by the self-contained `win-x64` publish of `berth.exe` |
| .NET 11 Windows Desktop Runtime | 11.0 | MIT | Bundled for WPF main window and Windows Forms tray icon in `berth.exe` |

## Repository and test suite (not shipped in berth.exe)

| Component | Version | Licence | Where it is used |
| --- | --- | --- | --- |
| Microsoft.CodeAnalysis.CSharp | 5.12.0 | MIT | `tests/Berth.Tests` only: parses C# sources to enforce Core layer rules |
| xunit.v3 | 4.0.3 | Apache-2.0 | `tests/Berth.Tests` only: runs the automated unit and UI test suite |
| Contributor Covenant | 2.1 | CC BY 4.0 | `CODE_OF_CONDUCT.md`: community code of conduct text |
