# Deployment guide

TrayFolderMenu installs **per user**, without admin rights. Installation only touches the user profile and `HKCU`, which makes it
straightforward to deploy with Microsoft Intune, Configuration Manager or a sign-in script.

## What `/install` does

1. Closes a running instance of TrayFolderMenu, if any.
2. Copies the executable to `%LOCALAPPDATA%\Programs\TrayFolderMenu\TrayFolderMenu.exe`.
3. Creates a Start menu shortcut and an *Installed apps* entry
   (`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\TrayFolderMenu`).
4. Registers autostart: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `TrayFolderMenu` = `"…\TrayFolderMenu.exe" /autostart`.
5. Adds the folder passed with `/folder:` to the user's configuration (optional).
6. Starts the app – silently in the tray with `/quiet`, otherwise with the settings window open.

No service, driver, scheduled task, `HKLM` key or `Program Files` folder is involved.

## Command line

| Command | Effect |
|---|---|
| `TrayFolderMenu.exe /install /quiet` | Silent per-user install |
| `TrayFolderMenu.exe /install /quiet /folder:"%USERPROFILE%\Menu"` | Silent install with a preset menu folder |
| `TrayFolderMenu.exe /uninstall /quiet` | Silent uninstall; user settings are kept |
| `TrayFolderMenu.exe /exit` | Closes the running instance |

Environment variables in `/folder:` are expanded by the application itself, so they work even when the command is not run through
`cmd.exe`.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Install or uninstall failed – details in `%LOCALAPPDATA%\TrayFolderMenu\log.txt` |
| `2` | Install failed: the running instance could not be closed within 10 seconds |

## Microsoft Intune (Win32 app)

1. Put `TrayFolderMenu.exe` in an empty folder and wrap it:

   ```cmd
   IntuneWinAppUtil.exe -c .\source -s TrayFolderMenu.exe -o .\output
   ```

2. Create a **Windows app (Win32)** with these settings:

| Setting | Value |
|---|---|
| Install command | `TrayFolderMenu.exe /install /quiet` (optionally `/folder:"%USERPROFILE%\Menu"`) |
| Uninstall command | `TrayFolderMenu.exe /uninstall /quiet` |
| Install behavior | **User** |
| Device restart behavior | No specific action |
| Return codes | `0` Success · `1` Failed · `2` Failed |
| Requirements | Windows 10 1903 or later |

3. **Detection rule** – use the registry rather than the file:

| Field | Value |
|---|---|
| Rule type | Registry |
| Key path | `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\TrayFolderMenu` |
| Value name | `DisplayVersion` |
| Detection method | String comparison → Equals → `1.1.0` (the version being deployed) |
| Associated with a 32-bit app on 64-bit clients | No |

> [!NOTE]
> A running executable cannot delete itself, so after uninstallation the program folder is removed a few seconds after the
> process exits. The registry entry is removed synchronously, which makes it the reliable detection signal.

**Updating:** publish the new version with the same commands and either use supersedence or bump the detection value. `/install`
closes the running instance, replaces the executable and keeps the user's settings.

## Pre-configuring folders

- **One folder:** pass it during installation with `/folder:"…"`. Existing folders in the user's configuration are kept.
- **Full configuration:** deploy `%APPDATA%\TrayFolderMenu\config.json` before the first start:

  ```json
  {
    "Folders": [
      { "Path": "\\\\fileserver\\Shortcuts\\Finance", "Enabled": true },
      { "Path": "%USERPROFILE%\\Menu", "Enabled": true }
    ],
    "HotkeyEnabled": true,
    "Hotkey": "Ctrl+Shift+A",
    "KeepMenuOpen": false
  }
  ```

  Paths may use environment variables. Missing fields fall back to defaults; an unreadable file is backed up as
  `config.json.bad` and replaced with defaults.

- **Central shortcut management:** point the menu to a network share (`\\server\share\Menu`). Every change in the share is visible
  the next time a user opens the menu.

## Application control and security notes

- The executable is **not code-signed**. For AppLocker or WDAC publisher rules, sign it with your own certificate – `build.cmd`
  contains a `signtool` line to start from – or allow it by file hash.
- The installed copy runs from `%LOCALAPPDATA%\Programs`, a user-writable location. If your policy blocks executables there, add an
  explicit allow rule.
- Windows APIs worth knowing about when reviewing EDR alerts:
  - `RegisterHotKey` for the global hotkey;
  - a low-level mouse hook (`WH_MOUSE_LL`) on a dedicated thread, **only** while a menu is kept open after launching an item –
    it is removed when the menu closes and never blocks input; there is no keyboard hook;
  - `HKCU\…\Run` for autostart.
- The application makes no network connections.
