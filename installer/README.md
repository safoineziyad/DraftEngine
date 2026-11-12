# DraftEngine Installer

Builds the Windows installer for DraftEngine using **Inno Setup 6**.

## Prerequisites

| Requirement | Needed for |
|---|---|
| **Inno Setup 6** ([download](https://jrsoftware.org/isinfo.php)) | Compiling the installer (`ISCC.exe`) |
| **.NET 8 SDK** | `dotnet publish` of the app |
| **.NET 8 Desktop Runtime** (x64) | Running the installed app (the build is framework-dependent, not self-contained) |
| Windows 10 1809+ (x64 / ARM64) | Minimum supported OS |

## Build the installer

One command, from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

The script cleans and publishes `DraftEngine.App` to `publish\` (Release, `win-x64`, framework-dependent), finds `ISCC.exe`, compiles `installer\DraftEngine.iss`, writes the installer and SHA-256 checksum to `dist\`, and prints the created paths:

```
dist\DraftEngine-Setup-0.4.0.1.exe
```

Close a running DraftEngine process before building, as Windows locks its Release executable.

## What the installer does

- **Per-user install** (no UAC/admin prompt) to `%LOCALAPPDATA%\Programs\DraftEngine`.
- Shows the MIT `LICENSE` during setup.
- Creates a **Start Menu** shortcut in the *DraftEngine* group; optionally a **desktop** shortcut (checkbox, off by default).
- Includes the published output of `dotnet publish ... -r win-x64 --self-contained false` (requires the .NET 8 Desktop Runtime on the target machine).
- Runs the app when setup finishes (can be unchecked on the last page).
- **App data stays in `%APPDATA%\DraftEngine`** (contracts, settings, logs). The installer and uninstaller never delete this folder.
- Adds an entry in *Apps & features* with the app's own icon (`DraftEngine.App.exe`).

## Updating the version

The app's display version and build stamp come from assembly metadata generated from
`DraftEngine.App.csproj`. The installer uses numeric `AppVersion` `0.4.0.1` for Windows
upgrade ordering and output file naming; `scripts\build-installer.ps1` checks the expected
installer version before building.
