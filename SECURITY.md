# Security policy

## Supported versions

Only the [latest release](https://github.com/damiankowalski/TrayFolderMenu/releases/latest) receives fixes.

## Reporting a vulnerability

Please **do not** open a public issue. Report it privately through
[GitHub private vulnerability reporting](https://github.com/damiankowalski/TrayFolderMenu/security/advisories/new)
(*Security* tab → *Report a vulnerability*). Include the version, steps to reproduce and the impact you see.

You can expect an initial response within a week.

## Scope notes

TrayFolderMenu runs with the user's own rights, makes no network connections and writes only to the user profile and `HKCU`.
It registers a global hotkey and, only while a menu is kept open after launching an item, a low-level mouse hook that never
blocks input. See [docs/deployment.md](docs/deployment.md#application-control-and-security-notes) for details.
