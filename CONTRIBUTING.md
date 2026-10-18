# Contributing

Thanks for your interest in TrayFolderMenu! Bug reports, ideas and pull requests are welcome.

## Reporting bugs

Open an [issue](https://github.com/damiankowalski/TrayFolderMenu/issues/new/choose) and include:

- the TrayFolderMenu version (settings window header, or file properties of the `.exe`),
- your Windows version (`winver`),
- steps to reproduce,
- relevant lines from `%LOCALAPPDATA%\TrayFolderMenu\log.txt`.

## Building

```cmd
build.cmd
```

The build uses the C# compiler that ships with Windows (.NET Framework 4.8), so no Visual Studio or SDK is required.
The output is `dist\TrayFolderMenu.exe`.

## Ground rules for code

- **C# 5 only.** The built-in compiler does not support newer syntax: no string interpolation (`$"…"`), `?.`, `nameof`,
  expression-bodied members, `out var`, pattern matching or tuples.
- **Zero dependencies.** Only assemblies that are part of .NET Framework 4.8.
- **One source file** (`src/TrayFolderMenu.cs`) by design – it keeps the project easy to audit and to build anywhere.
- **No network access** and no telemetry.
- User interface strings are currently Polish; keep new strings consistent until localization lands.

## Pull requests

1. Keep changes focused; one topic per pull request.
2. Test on Windows 10 or 11, ideally in both light and dark mode.
3. Add a line to the `[Unreleased]` section of [CHANGELOG.md](CHANGELOG.md).
4. The CI build must pass.

## Releasing (maintainers)

1. Update `AssemblyVersion` and `AssemblyFileVersion` in `src/TrayFolderMenu.cs`.
2. Move the `[Unreleased]` entries in `CHANGELOG.md` to a new version section and update the links at the bottom.
3. Commit, then tag and push:

   ```cmd
   git tag -a v1.2.0 -m "TrayFolderMenu 1.2.0"
   git push origin v1.2.0
   ```

GitHub Actions builds the executable, checks that the tag matches the version in the source, and publishes the release with the
executable, a `.zip` package, `SHA256SUMS.txt` and the notes from `CHANGELOG.md`.
