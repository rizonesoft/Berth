# Third-party notices

Berth is licensed under the [GNU General Public License, version 3 or later](LICENSE). This file lists the third-party components bundled in the self-contained `berth.exe` release and used in this repository.

## Shipped in berth.exe

The self-contained `win-x64` publish of `berth.exe` bundles the .NET 11.0.0 runtime and Windows Desktop runtime (WPF and Windows Forms). In addition to Microsoft's MIT-licensed runtime code, those runtimes include upstream third-party libraries (such as Unicode data, zlib-ng, and Brotli) documented in the version-matched upstream notices: [.NET Runtime 11.0.0 THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/runtime/blob/v11.0.0/THIRD-PARTY-NOTICES.TXT), [WPF 11.0.0 THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/wpf/blob/v11.0.0/THIRD-PARTY-NOTICES.TXT), and [Windows Forms 11.0.0 THIRD-PARTY-NOTICES.TXT](https://github.com/dotnet/winforms/blob/v11.0.0/THIRD-PARTY-NOTICES.TXT).

| Component | Version | Licence | Where it is used |
| --- | --- | --- | --- |
| .NET 11 Runtime | 11.0 | MIT + upstream notices | Bundled by the self-contained `win-x64` publish of `berth.exe` |
| .NET 11 Windows Desktop Runtime | 11.0 | MIT + upstream | Bundled for WPF and WinForms in `berth.exe` |

## Repository and test suite (not shipped in berth.exe)

| Component | Version | Licence | Where it is used |
| --- | --- | --- | --- |
| Microsoft.CodeAnalysis.CSharp | 5.12.0 | MIT | `tests/Berth.Tests` only: C# parser for Core layer rules |
| xunit.v3 | 4.0.3 | Apache-2.0 | `tests/Berth.Tests` only: automated unit and UI test suite |
| Pillow | build-time | MIT-CMU | `assets/build.py` only: generates icon and banner assets |
| Contributor Covenant | 2.1 | CC BY 4.0 | `CODE_OF_CONDUCT.md`: community code of conduct text |
