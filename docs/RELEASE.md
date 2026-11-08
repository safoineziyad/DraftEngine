# Release Guide

## Version

`DraftEngine.App/DraftEngine.App.csproj` holds the single source of truth:

```xml
<Version>0.4.0</Version>
<AssemblyVersion>0.4.0.0</AssemblyVersion>
<FileVersion>0.4.0.0</FileVersion>
```

The UI reads `AppVersion.Display`, which is stamped from this value. `installer/DraftEngine.iss`
also defines `AppVersion`; bump both together. `installer/Build-Installer.ps1` reads the
`.iss` value when reporting the output file name.

## Build and test

```powershell
dotnet build DraftEngine.sln -c Release
dotnet test DraftEngine.sln -c Release
powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
```

The installer script publishes `DraftEngine.App` (framework-dependent, win-x64) to `.\publish`,
optionally signs the app binary, then compiles `installer\DraftEngine.iss` with Inno Setup 6.
Output: `installer\Output\DraftEngine-Setup-<version>.exe`.

## Code signing (optional)

Signing is skipped cleanly when no certificate is configured.

```powershell
$env:DRAFTENGINE_SIGN_CERT     = 'C:\secure\DraftEngine.pfx'
$env:DRAFTENGINE_SIGN_PASSWORD = '********'
powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
```

- `Build-Installer.ps1` signs `publish\DraftEngine.App.exe` with `signtool` (found on PATH or in
  the Windows SDK), using SHA-256 and a DigiCert timestamp.
- `DraftEngine.iss` adds an Inno Setup `SignTool` directive only when
  `DRAFTENGINE_SIGN_CERT` is set, so the installer and uninstaller are signed too.
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

1. Bump `<Version>` in the csproj and `AppVersion` in the `.iss`.
2. `dotnet build -c Release` and `dotnet test -c Release` (all green).
3. Update `CHANGELOG.md` and `docs/PROJECT-STATE.md`.
4. Build the installer (signed if a certificate is available).
5. Smoke-test on a clean Windows profile: first run, create each contract type, DOCX/PDF export,
   upgrade over a previous version's database.
6. Tag the release and attach the installer to the GitHub release.
