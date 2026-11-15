# Security policy

Berth reads and sets the positions of your desktop icons, writes small files under your own profile and, when you ask it to, one value under your own `Run` registry key. It needs no administrator rights and uses no network. If you find a way to make it write somewhere else, run something it should not, or act outside your own user account, please tell us privately.

## Which versions get fixes?

| Version | Security fixes |
| ------- | -------------- |
| `main` | Yes, as there is no release yet |

## How do I report a vulnerability?

Use GitHub private vulnerability reporting: open the repository's **Security** tab and choose **Report a vulnerability**. Only the maintainers see the report.

Do not open a public issue, pull request or discussion about a vulnerability.

A useful report includes the commit or version you tested, the steps, and what an attacker gains.

## What happens next?

- We aim to respond within 3 working days, confirming we received the report and who is handling it.
- We reproduce the problem, agree on its severity with you, and keep you updated in the private advisory until it is fixed.
- We ask you to keep the details private until the fix ships. We credit you in the advisory if you want that.

## What is in scope?

- `berth.exe`: the tray app, the main window and the command line.
- The layout, settings and undo files it reads and writes, including a file crafted to mislead it.
- The `Start with Windows` registry value.

A process running as the same user can already do anything that user can, so a problem that needs that level of access is out of scope unless Berth gives it more.
