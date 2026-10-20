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
powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
```

The script cleans and publishes `DraftEngine.App` to `publish\` (Release, `win-x64`, framework-dependent), finds `ISCC.exe`, compiles `installer\DraftEngine.iss`, and prints the output path:

```
installer\Output\DraftEngine-Setup-<version>.exe
```

To recompile without re-publishing (faster iterations on the `.iss`):

```powershell
powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1 -SkipPublish
```

## What the installer does

- **Per-user install** (no UAC/admin prompt) to `%LOCALAPPDATA%\Programs\DraftEngine`.
- Shows the MIT `LICENSE` during setup.
- Creates a **Start Menu** shortcut in the *DraftEngine* group; optionally a **desktop** shortcut (checkbox, off by default).
- Includes the published output of `dotnet publish ... -r win-x64 --self-contained false` (requires the .NET 8 Desktop Runtime on the target machine).
- Runs the app when setup finishes (can be unchecked on the last page).
- **App data stays in `%APPDATA%\DraftEngine`** (contracts, settings, logs). A normal uninstall does *not* touch it; on uninstall you are asked once whether to delete it too (default: **No**).
- Adds an entry in *Apps & features* with the app's own icon (`DraftEngine.App.exe`).

## Updating the version

The version lives in **one place**: the `#define AppVersion` line at the top of
`installer\DraftEngine.iss`. It drives `AppVersion`, the version info on the
setup exe, and the output file name (`DraftEngine-Setup-<version>.exe`).
