# Release Guide

## Version

`DraftEngine.App/DraftEngine.App.csproj` holds the assembly/package version:

```xml
<Version>0.4.0-beta.1</Version>
<AssemblyVersion>0.4.0.1</AssemblyVersion>
<FileVersion>0.4.0.1</FileVersion>
```

The sidebar and About page read version/build metadata from the compiled assembly through
`AppVersion`. The installer uses numeric `AppVersion` `0.4.0.1` for Windows upgrade ordering;
the build script checks it against the current beta installer version and stamps the source
commit and UTC build time into assembly metadata.

## Build and test

```powershell
dotnet build DraftEngine.sln -c Release
dotnet test DraftEngine.sln -c Release
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

The script cleans and publishes `DraftEngine.App` (framework-dependent, win-x64) to `.\publish`,
optionally signs the app and installer, and compiles `installer\DraftEngine.iss` with Inno Setup 6.
The local installer and its SHA-256 file are written to `.\dist\`. It performs no release upload
or tagging. Close a running DraftEngine instance before building so Windows does not lock its
Release executable.

## Code signing (optional)

Signing is skipped cleanly when no certificate is configured.

```powershell
$env:DRAFTENGINE_SIGN_CERT     = 'C:\secure\DraftEngine.pfx'
$env:DRAFTENGINE_SIGN_PASSWORD = '********'
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

- `build-installer.ps1` signs `publish\DraftEngine.App.exe` with `signtool` (found on PATH or in
  the Windows SDK), using SHA-256 and a DigiCert timestamp.
- `DraftEngine.iss` adds an Inno Setup `SignTool` directive only when both
  `DRAFTENGINE_SIGN_CERT` and `DRAFTENGINE_SIGN_PASSWORD` are set, so the installer and
  uninstaller are signed too.
- Without the variables the build is unsigned and still succeeds.

A code-signing certificate must be purchased separately (see the manual steps in the release
notes). Never commit the `.pfx` or its password.

## GitHub auto-update (enabled by default)

`appsettings.json`:

```json
"Features": {
  "AutoUpdateEnabled": true
}
```

At startup, DraftEngine checks `https://api.github.com/repos/safoineziyad/DraftEngine/releases/latest`
for a release newer than the running app. Debugger-attached runs skip this check. Set
`Features:AutoUpdateEnabled` to `false` to disable it. When a newer release is found, the banner
downloads the `DraftEngine-Setup-<version>.exe` release asset after the user clicks Update Now,
then launches the Inno Setup installer silently and relaunches the app. No contract data or
identifiers are sent. Attach the installer asset to every stable GitHub release.

```json
DraftEngine-Setup-0.4.1.exe
```

## Licensing (off by default)

`Features:LicensingEnabled` defaults to `false`; with it off the app behaves exactly as before.
When on, startup loads `%APPDATA%\DraftEngine\license.dat` (DPAPI-protected), verifies the signed
key against the embedded public key, and shows an offline activation screen when no usable license
exists. Grace period: `Features:LicenseGracePeriodDays` (default 14). No server calls, no payment
code. The About page exposes license status and activation.

## Release checklist

1. Update `<Version>`, numeric assembly/file versions, and the installer numeric `AppVersion`.
2. `dotnet build -c Release` and `dotnet test -c Release` (all green).
3. Update `CHANGELOG.md` and `docs/PROJECT-STATE.md`.
4. Build the installer (signed if a certificate is available).
5. Smoke-test on a clean Windows profile: first run, create each contract type, DOCX/PDF export,
   upgrade over a previous version's database.
6. Only after explicit release approval, follow the repository's release publication process.

## Local 0.2.2 upgrade smoke test

`DraftEngine.App.Tests.UpgradeMigrationTests.Version022Database_UpgradesFromCopiedDataDirectory_AndPreservesContract`
creates a database at the last migration included in the `v0.2.2` source tag, copies it to a
temporary data directory, and starts the built app with `DRAFTENGINE_DATA_DIR` set to that copy.
The app's `--verify-upgrade` mode exits without opening the UI and reports whether the test
contract survived, migrations completed, and a pre-migration backup exists. The test also
compares the backup bytes with the original copied database. This validates data migration and
the app's configured data path; it does not install over the user's existing application.
