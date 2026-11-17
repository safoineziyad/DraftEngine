# Release Guide

## Version

The single source of truth is `Directory.Build.props` (the app and installer both consume it):

```xml
<DraftEngineVersion>0.4.0-beta.1</DraftEngineVersion>
<DraftEngineFileVersion>0.4.0.1</DraftEngineFileVersion>
```

`DraftEngineVersion` is the SemVer informational version shown in the UI and compared by the
update flow; `DraftEngineFileVersion` is the numeric Windows file/installer version. Change both
for a release. The sidebar and About page read the compiled assembly through `AppVersion`;
`scripts\build-installer.ps1` passes the numeric version to Inno Setup as `/DAppVersion`.

## Build and test

```powershell
dotnet build DraftEngine.sln -c Release
dotnet test DraftEngine.sln -c Release
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

The script cleans and publishes `DraftEngine.App` (framework-dependent, win-x64) to `.\publish`,
optionally signs the app and installer, and compiles `installer\DraftEngine.iss` with Inno Setup 6.
The local installer and its SHA-256 file are written to `installer\Output\`. It performs no release upload
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

## Signed updates (on by default, notify-only)

The app checks a signed `latest.json` (not the GitHub API): one HTTPS request carrying only
`User-Agent: DraftEngine/<version>`, at most once every 24 hours, 15 seconds after startup. It can
be disabled in Settings or with `Features:AutoUpdateEnabled`, which sends zero requests. Nothing
is downloaded until the user clicks Update, and nothing is installed without the confirmation
dialog.

`appsettings.json`:

```json
"Updates": {
  "ManifestUrl": "https://github.com/safoineziyad/DraftEngine/releases/latest/download/latest.json",
  "BetaManifestUrl": "",
  "PublicKey": "",
  "AllowedInstallerHosts": [ "github.com", "objects.githubusercontent.com" ],
  "RequireAuthenticode": false,
  "EnableBetaChannel": false
}
```

### One-time signing key (maintainer only)

`Updates:PublicKey` is deliberately **empty in the repository**. Nothing can be verified, and no
update is ever offered, until you generate your own key and paste its public half. The private key
never enters the repo, the app, CI or chat.

```powershell
# Choose a folder OUTSIDE the repository that is backed up.
dotnet run --project tools\UpdateKeyTool -- keygen --out C:\Users\<you>\DraftEngine-keys\update-signing-key.pem
```

- `keygen` refuses any output path inside the repository and refuses to overwrite an existing key.
- It prints the **public key** (SPKI base64) and a warning to back up the private key in **two
  separate offline places**. Losing it means no future update can ever be signed for this app.
- Paste the printed public key into **`DraftEngine.App/appsettings.json` -> `Updates:PublicKey`**
  (single line, no quotes inside). That file is committed; the private `.pem` is not.
- Back up the `.pem` yourself (encrypted drive + offline copy). Never email, commit or paste it.

To then verify a manifest locally:

```powershell
dotnet run --project tools\UpdateKeyTool -- verify --manifest latest.json --public-key <base64>
```

### Cutting a signed release

Release notes are a small JSON file with `en` required and `fr`/`ar`/`es` optional:

```json
{ "en": "What changed.", "fr": "Ce qui a changé.", "ar": "...", "es": "..." }
```

```powershell
# 1. ALWAYS dry-run first (validates version, notes and installer; signs and uploads nothing).
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 `
  -Version 0.4.0-beta.2 -NotesFile notes.json -Prerelease -DryRun

# 2. Real preparation. Refuses without the dry-run marker above; stages dist\<version>\latest.json
#    and copies the installer there. Still uploads nothing unless -Publish is passed.
$env:DRAFTENGINE_UPDATE_KEY = 'C:\Users\<you>\DraftEngine-keys\update-signing-key.pem'
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 `
  -Version 0.4.0-beta.2 -NotesFile notes.json -Prerelease

# 3. Inspect dist\<version>\latest.json, then create a DRAFT release yourself (optional -Publish).
gh release create v0.4.0-beta.2 dist\0.4.0-beta.2\DraftEngine-Setup-0.4.0.1.exe dist\0.4.0-beta.2\latest.json --draft --prerelease
```

The manifest points at an allowlisted HTTPS installer URL and includes the installer size and
SHA-256. Clients reject unsigned, tampered, downgraded or foreign-host manifests. For beta builds,
set `Updates:BetaManifestUrl` and `EnableBetaChannel: true`, and only advertise them to users who
opt into the beta channel.

## Licensing (off by default)

`Features:LicensingEnabled` defaults to `false`; with it off the app behaves exactly as before.
When on, startup loads `%APPDATA%\DraftEngine\license.dat` (DPAPI-protected), verifies the signed
key against the embedded public key, and shows an offline activation screen when no usable license
exists. Grace period: `Features:LicenseGracePeriodDays` (default 14). No server calls, no payment
code. The About page exposes license status and activation.

## Release checklist

1. Update `DraftEngineVersion` and `DraftEngineFileVersion` in `Directory.Build.props`.
2. `dotnet build -c Release` and `dotnet test -c Release` (all green).
3. Update `CHANGELOG.md` and `docs/PROJECT-STATE.md`.
4. Build the installer (`scripts\build-installer.ps1`, signed if a certificate is available).
5. Smoke-test on a clean Windows profile: first run, create each contract type, DOCX/PDF export,
   upgrade over a previous version's database.
6. Run `scripts\release.ps1 ... -DryRun`, then the real run, inspect `dist\<version>\latest.json`,
   and approve the draft GitHub release. Never skip the dry run and never commit the private key.
7. Confirm that `Updates:PublicKey` matches the key used to sign and that the release assets include
   both the installer and `latest.json`.

## Local 0.2.2 upgrade smoke test

`DraftEngine.App.Tests.UpgradeMigrationTests.Version022Database_UpgradesFromCopiedDataDirectory_AndPreservesContract`
creates a database at the last migration included in the `v0.2.2` source tag, copies it to a
temporary data directory, and starts the built app with `DRAFTENGINE_DATA_DIR` set to that copy.
The app's `--verify-upgrade` mode exits without opening the UI and reports whether the test
contract survived, migrations completed, and a pre-migration backup exists. The test also
compares every user table row-by-row (by column name and row Id) between the pre-migration
snapshot and the upgraded database, so no contract, field, clause, version, chat message or
generated document may be lost or altered. Byte equality is intentionally not used: SQLite
rebuilds tables and updates its header change counter, which changes bytes without changing
data. This validates data migration and the app's configured data path; it does not install
over the user's existing application.
