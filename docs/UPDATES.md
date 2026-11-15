# DraftEngine Updates

Status: design of record for the update flow introduced in 0.4.0. Applies to the
`feature/i18n-theme` branch and every later release. Supersedes the GitHub-API check described in
older `docs/RELEASE.md` revisions.

## 1. Audit of the previous implementation

Two independent checks existed before this work.

| Piece | Behaviour | Verdict |
|---|---|---|
| `Services/Updates/UpdateService.cs` (commit `0d94c21`) | `GET https://api.github.com/repos/safoineziyad/DraftEngine/releases/latest` at startup, picks the first asset named `DraftEngine-Setup-*.exe` on `github.com`, downloads it, then launches it with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS` and calls `Application.Current.Shutdown()`. | **Replaced.** Silent install with no integrity check, no signature, no database snapshot, no confirmation, no skip/remind persistence and no cadence. Repository and API host are hardcoded. |
| `Services/Updates/UpdateCheckService.cs` | Optional notify-only `GET` of a static manifest URL (`Features:UpdateCheckEnabled`, off by default); compares the version and returns `UpdateInfo`. | **Replaced** by the signed-manifest service. It validated HTTPS but not the host, had no signature, hash or size checks, and its version comparison ignored prerelease ordering. |
| `MainViewModel` banner | Shows "A new version (X) is available!" plus "Update Now", downloads into `%TEMP%\DraftEngineUpdate.exe` and starts the silent installer. Dismiss only persists for the session. | **Replaced.** The banner stays, but update/download/install are now separate, consent-gated steps with skip and later persistence and a version indicator on the About item. |
| `Core/Updates/UpdateVersionComparer.cs` | Truncates everything after `-`/`+` before comparing, so `0.4.0-beta.9` counts as `0.4.0`. | **Replaced** by a SemVer parser with prerelease ordering. |
| Installer script | `installer\DraftEngine.iss` hardcoded `#define AppVersion "0.4.0.1"`. | **Fixed.** The version now comes from `Directory.Build.props` and is passed to ISCC as `/DAppVersion`. |

Flags found: silent install, no integrity or Authenticode verification, no code-signing of the
installer, hardcoded API/asset URLs, no downgrade protection, no opt-out persistence, and no
snapshot before replacing application files. None of those paths survive Phase 2/3.

## 2. Design

### 2.1 Manifest

One small JSON document published per channel. It is downloaded over HTTPS from
`Updates:ManifestUrl` (default
`https://github.com/safoineziyad/DraftEngine/releases/latest/download/latest.json`). The founder
may point it at a separate public releases repository through configuration; no rebuild is needed.

```json
{
  "schemaVersion": 1,
  "version": "0.4.0-beta.2",
  "publishedAt": "2026-11-20T10:00:00Z",
  "minimumSupportedVersion": "0.2.2",
  "releaseNotes": {
    "en": "…",
    "fr": "…",
    "ar": "…",
    "es": "…"
  },
  "installer": {
    "url": "https://github.com/safoineziyad/DraftEngine/releases/download/v0.4.0-beta.2/DraftEngine-Setup-0.4.0.2.exe",
    "fileName": "DraftEngine-Setup-0.4.0.2.exe",
    "sizeBytes": 84567890,
    "sha256": "9f2c…64 hex characters…"
  },
  "signature": "base64(ECDSA-P256-SHA256 signature)"
}
```

Rules:

- `schemaVersion` is `1`; unknown values are rejected.
- `version` is SemVer 2.0.0 (`MAJOR.MINOR.PATCH[-prerelease][+build]`). A leading `v` is accepted
  for the release tag, not inside the manifest.
- `releaseNotes` is a map of language code to plain text. Missing languages fall back to `en`;
  an empty map is allowed but shows "no notes provided".
- `installer.url` must be HTTPS and its host must be allowlisted (§2.4). `fileName` is a leaf
  name only (no path, no separators) and must end in `.exe`.
- `installer.sizeBytes` and `installer.sha256` (lowercase hex) describe the exact file.
- `minimumSupportedVersion` is optional. When present and newer than the running version, the
  update is offered as **required** style copy, but installation still needs a click.
- Unknown fields are ignored so the schema can grow additively.

### 2.2 Canonical form and signature

The signature covers the canonical UTF-8 serialization of the manifest object **without** the
`signature` field:

1. Serialize the object with the fixed field order
   `schemaVersion`, `version`, `publishedAt`, `minimumSupportedVersion`, `releaseNotes`,
   `installer`, and inside `installer`: `url`, `fileName`, `sizeBytes`, `sha256`
   (release notes language keys sorted ordinally).
2. UTF-8, no BOM, no insignificant whitespace, JSON escaping of `"` `\` and control characters
   only; all other characters (including non-ASCII) are emitted literally. Numbers have no
   leading zeros or `+`; `publishedAt` is normalized to UTC `yyyy-MM-ddTHH:mm:ssZ`.
3. Sign the SHA-256 digest with **ECDSA P-256** (`ES256`, IEEE P1363 signature format) using the
   founder's offline private key.

The app embeds only the **public** key (SPKI, base64) as a configuration constant. A manifest is
rejected when the signature field is missing, is not valid base64, has the wrong length, or fails
verification against the embedded key, and when any covered field differs from what was signed.

`tools/UpdateKeyTool` implements `keygen`, `sign` and `verify` with the same canonicalization.

### 2.3 Trust and transport

- HTTPS only; plain `http://` manfiest or installer URLs are rejected.
- No redirects to non-allowlisted hosts. The downloader follows redirects only while the final
  and every intermediate host is allowlisted.
- The installer host allowlist defaults to `github.com` and `objects.githubusercontent.com`,
  extendable through configuration (`Updates:AllowedInstallerHosts`). Matching is by exact host
  name, case-insensitive; look-alikes such as `github.com.evil.example`, `evilgithub.com` and
  `github.com@evil.example` are rejected. User-info in URLs is rejected.
- The manifest host itself is trusted by configuration; its response is still signature-checked.

### 2.4 Version policy

- An update is offered only when the manifest version is **strictly newer** than the running
  informational version (SemVer ordering, prerelease aware).
- Downgrades (manifest older than the running version) are refused and logged.
- The stable channel ignores prerelease manifest versions; the beta channel accepts them. Beta
  is opt-in in Settings and uses `Updates:BetaManifestUrl` from configuration.
- "Skip this version" persists the exact version string in settings; that version is not offered
  again until a newer one appears. "Remind me later" suppresses the banner for the session and
  leaves the About/Settings entry points available.

### 2.5 Cadence

- Automatic check on startup **15 seconds after** the main window is shown, never blocking
  startup, and at most once per 24 hours (persisted `lastCheckedAt`; clock skew backwards still
  allows a manual check).
- A manual "Check now" in About/Settings always checks.
- 10-second HTTP timeout for the manifest; no retries. When automatic checks are disabled in
  Settings, no request is made at startup or on navigation.

### 2.6 Download and verification

1. Explicit user click required for both download and install; nothing is installed by a check
   or by the banner appearing.
2. Stream to `%LOCALAPPDATA%\DraftEngine\updates\<version>.exe.partial` with progress. A
   `.partial` file is deleted on cancel, failure, size mismatch or hash mismatch.
3. Verify `Content-Length` (when provided) and `sizeBytes`, then the SHA-256 of the streamed
   bytes. A truncation or extra bytes is rejected.
4. Optionally verify Authenticode with `WinVerifyTrust` when `Updates:RequireAuthenticode` is
   true (off until the installer is code-signed). An unsigned or invalid signature fails the
   update.
5. Only after all checks pass is the file moved (atomic replace) to
   `%LOCALAPPDATA%\DraftEngine\updates\DraftEngine-Setup-<version>.exe`.

### 2.7 Installing

- A confirmation dialog states that DraftEngine will close, the installer will run, the app will
  restart, and that a database snapshot is taken first.
- Before launching, a consistent database snapshot is written with
  `SqliteConnection.BackupDatabase` (the same mechanism as the pre-migration snapshot) to
  `%LOCALAPPDATA%\DraftEngine\updates\pre-update-<version>.db`.
- Launch: `DraftEngine-Setup-<version>.exe /SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`.
  `/SILENT` shows progress without questions; `/CLOSEAPPLICATIONS` closes the running app if the
  process is still alive; `/RESTARTAPPLICATIONS` lets Inno Setup restart it. This is the only
  place an installer is started, and only from a button press plus confirmation.
- If the process cannot start (missing file, UAC refusal surfaces as `Win32Exception` with
  `ERROR_CANCELLED`), a localized message is shown and the app keeps running.

### 2.8 Failure handling

Every failure maps to a distinct, localized message: offline/network, timeout, 404, malformed
JSON, invalid signature, wrong key, downgrade, hash mismatch, truncated download, disk full,
blocked host, and installer launch failure. Failures never crash the app and never block
startup; automatic checks log only the version string and the error type.

## 3. Privacy

Update checks send one plain HTTPS `GET` for the manifest with a `User-Agent` of
`DraftEngine/<informational version>`. No query string, cookie, installation id, contract data,
party names or settings are sent. The installer download is the only other request, and it is
user-initiated. Checks are **on by default**, disclosed in the first-run screen and in About, and
can be turned off in Settings, after which the app makes no update request at all.

## 4. Release process

1. Update `Directory.Build.props`: `DraftEngineVersion` (SemVer, e.g. `0.4.0-beta.2`) and
   `DraftEngineFileVersion` (numeric, must increase, e.g. `0.4.0.2`).
2. Add release notes (English required; fr/ar/es optional) and run `scripts\release.ps1`
   (Phase 4) with `-Version`, `-NotesFile`, `-Prerelease` and `-DryRun` first.
3. The script runs the full suite, builds the installer via `scripts\build-installer.ps1`
   (which passes the version to Inno Setup), computes SHA-256, writes `latest.json`, signs it with
   `DRAFTENGINE_UPDATE_KEY`, verifies it, and prints the exact `gh release create` command.
4. Upload the versioned installer, `DraftEngine-Setup-latest.exe`, `latest.json` and the
   `.sha256` file. A prerelease must never be marked latest.
5. After upload, re-download the public `latest.json` and verify its signature again.

Private key handling: generated once by `tools/UpdateKeyTool keygen` into a path outside the
repository, backed up in two physically separate places, never committed, never printed, never
copied into build output. Losing it means publishing a new public key in a signed app release
before further updates can be trusted.

## 5. Threat model

| Threat | Mitigation |
|---|---|
| Compromised GitHub release assets or repository | Installer and manifest are signed with an ECDSA P-256 key held offline; the app refuses unsigned or wrongly signed manifests and verifies the installer hash from the signed manifest. |
| Hostile network / TLS interception | HTTPS only; signature verification is independent of TLS; redirects restricted to allowlisted hosts. |
| DNS or host look-alike attack | Exact-host allowlist; user-info strings rejected; HTTP downgrade rejected. |
| Malicious manifest that points elsewhere | Installer URL allowlist plus signature; a modified URL invalidates the signature. |
| Truncated or swapped installer | size + SHA-256 + optional Authenticode; `.partial` removed on failure. |
| Forced downgrade to a vulnerable version | Downgrades refused; `minimumSupportedVersion` can mark old clients. |
| Silent background install | Installation requires an explicit click plus confirmation dialog; installer runs only after a database snapshot. |
| Key theft on the build machine | Signing key is not on build machines by default; release signing uses `DRAFTENGINE_UPDATE_KEY` pointing at the offline-backed file, and the key is backed up offline. |
| Privacy leak through the check | Plain GET, fixed User-Agent, no identifiers, no query string, disabled setting makes zero requests. |

Residual risks: the embedded public key can only be rotated through a signed app release; the
installer is not code-signed yet, so SmartScreen may warn; a user tricked into running a
downloaded installer manually bypasses these checks entirely.
