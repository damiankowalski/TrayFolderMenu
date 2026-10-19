# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.2.0] - 2026-10-19

### Added

- English user interface. A language selector – the globe button in the top-right corner of the settings window – switches
  between English and Polish instantly, without restarting. By default the app follows the Windows display language.
- `Language` setting in `config.json` (`"en"` or `"pl"`) and a `/language:en|pl` switch for `/install`.
- GitHub link in the settings window; the *Installed apps* entry links to the project page.

### Changed

- Everything the app writes is now English regardless of the interface language: log messages, the Start menu shortcut
  description and the file description. Configuration keys and registry values were already language-neutral and are unchanged,
  so existing settings keep working.

## [1.1.0] - 2026-10-18

### Added

- Option to keep the menu open after launching an item, so several items can be started from one menu
  (Settings → *Zachowanie menu*, or right-click the tray icon).
- <kbd>Ctrl</kbd>+click inverts the keep-open setting for a single click.
- A short accent highlight confirms the launched item while the menu stays open.

### Changed

- Clicking the tray icon again, or pressing the hotkey again, closes an open menu.

## 1.0.0 - 2026-10-18

First internal version (not published as a release).

### Added

- Cascading menus built from folders, one tray icon per folder; subfolders become submenus.
- Global hotkey (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>A</kbd> by default, configurable in the settings window) that opens the
  menu at the mouse cursor.
- Settings window with light and dark theme, drag & drop of folders and a modern folder picker.
- Per-user installation with autostart, a Start menu shortcut and an *Installed apps* entry; silent `/install` and `/uninstall`.
- JSON configuration in `%APPDATA%\TrayFolderMenu` and a rotating log in `%LOCALAPPDATA%\TrayFolderMenu`.

[Unreleased]: https://github.com/damiankowalski/TrayFolderMenu/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/damiankowalski/TrayFolderMenu/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/damiankowalski/TrayFolderMenu/releases/tag/v1.1.0
