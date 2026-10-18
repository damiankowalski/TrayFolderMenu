# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

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

[Unreleased]: https://github.com/damiankowalski/TrayFolderMenu/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/damiankowalski/TrayFolderMenu/releases/tag/v1.1.0
