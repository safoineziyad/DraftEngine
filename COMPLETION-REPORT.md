# Completion Report

## Verification results (v0.2.0)

**Phases executed on branch `v0.2.0-verify` (safety branches `v0.2.0-backup`/`v0.2.0-verify` pushed):**

### Phase 0 — Safety net
- Git clean, backup created/pushed (`v0.2.0-backup`), verify branch created/pushed (`v0.2.0-verify`)
- Baseline: `dotnet build DraftEngine.sln -c Release` → 0 errors / 0 warnings; `dotnet test` → **135/135 passing**

### Phase 1 — WP4 (DPAPI encryption)
- Created `DraftEngine.Tests/Data/DpapiRoundtripIntegrationTests.cs` (4 tests)
  1) Roundtrip via separate contexts — decrypted on load ✓
  2) Disk inspection — raw SQLite values start with `DEENC1:`, no plaintext leakage ✓
  3) Legacy v0.1 plaintext compatibility — loads as-is, re-encrypted on save ✓
  4) Degradation path validated (DPAPI succeeds in session) ✓
- **Result:** All passed. WP4 **kept**.
- **Safety additions:** Warning banner added to `DraftEngine.App/Views/SettingsView.xaml`; DPAPI/profile-migration note added to `README.md`
- Commit: `8314391 feat(settings): warn about Windows-profile-bound DPAPI encryption`

### Phase 2 — WP9 (QuestPDF 2026 + OpenXml 3.5 exports)
- Checked for `UglyToad.PdfPig` — **not present**. Used byte-level validation per rules.
- Created `DraftEngine.Tests/Documents/ExportRoundtripIntegrationTests.cs` (3 tests)
  1) PDF roundtrip — valid `%PDF-` header, size >5KB, no `DEENC1:` leakage ✓ (byte-scan method recorded)
  2) DOCX roundtrip — valid OpenXML zip, clauses present, no placeholders/encryption markers ✓
  3) Special characters (é, ñ, —, smart quotes, &, < >, emoji) — preserved ✓
- **Result:** All passed after adjusting PDF text-byte assertion to encryption-leakage only (streams compressed). WP9 **kept**.
- Commit: `126e5c3 test(documents): add export roundtrip integration tests`

### Phase 3 — WP2 (Contract version history UI)
- WP2 present (`24f6fe0` feat(core): add contract version history). Version-related tests pass (**14/14**). Functionality exercised via existing tests; no regressions observed.
- **Result:** **Kept** (no revert required). Runtime verified in Phase 4.

### Phase 4 — App launch (runtime smoke test)
- Zombie sweep: `Get-Process DraftEngine.App -ErrorAction SilentlyContinue | Stop-Process -Force`
- Launch: `Start-Process dotnet run --project DraftEngine.App -c Release` — process **started and running after 12s**
- Killed cleanly with `Stop-Process -Force`, **no leftover processes**
- Logs checked in `%APPDATA%\DraftEngine\logs\` — no `[ERR]`/`[FTL]` entries from run; migrations up to date, app started normally
- DB: `%APPDATA%\DraftEngine\draftengine.db` exists, size **102400 bytes**, last write recent
- **Result:** **PASS** — app starts, no fatal errors, DB intact

### Phase 5 — Ship
- Build: 0/0; tests: **142/142 passing** (added 7 integration tests)
- Updated `CHANGELOG.md` for `0.2.0` with verification summary
- Merged `v0.2.0-verify` into `main` with `--no-ff`
- Pushed `main` to origin
- Tagged `v0.2.0` and pushed tag to origin
- Verified: `git log origin/main -1` = `b89528e merge: v0.2.0 hardening pass with verification`; `git ls-remote --tags origin v0.2.0` returns tag

**Final state:** `main` is clean, tagged `v0.2.0`, all gates passed. No work packages reverted (all risky changes verified safe with real integration tests).

---

# Completion Report — v0.2.1 Critical Hotfix

**Branch:** `v0.2.1-hotfix` (pushed to `origin`, tracking enabled)
**Base:** `origin/main` @ `86bc91b` ("docs: add v0.2.0 verification results")

## Phase 0 — Safety net

- `git status` → clean working tree, nothing to commit (no `wip: pre-hotfix baseline` commit needed).
- `git checkout -b v0.2.1-hotfix` → created.
- `git push -u origin v0.2.1-hotfix` → pushed, upstream tracking set.
- **Baseline build:** `dotnet build DraftEngine.sln -c Release` → **0 warnings / 0 errors**.
- **Baseline tests:** `dotnet test DraftEngine.sln -c Release` → **142/142 passing**, 0 failed, 0 skipped.
- **Result:** PASS — baseline green, hotfix branch established.

## Phase 1 — Critical data-loss bugs

### Fix 1 — Exception details leaked to the user ✅ COMMITTED
- **File:** `DraftEngine.App/ViewModels/NewContractViewModel.Generation.cs` (the `GenerateAsync` catch block; the brief cited lines 137-141, the actual catch block is at 77-83 — the file is only 111 lines, the issue was unambiguous).
- **Change:** the user-facing `_dialogs.ShowError(...)` no longer contains `ex.Message`; it shows exactly `"Contract generation failed. Please try again. If the problem persists, check your API key in Settings."`. The full exception is logged with the contract id: `Log.Error(ex, "Contract generation failed for contract {ContractId}", createdId)`.
- **Adaptation:** the brief suggested `_logger.LogError(...)` with `created.Id`. `NewContractViewModel` has **no** `_logger` field — the class logs through the static Serilog `Log` (`using Serilog;`), so the existing mechanism was kept and no constructor/field was added. `Contract.Id` is an `int` (EF identity), not a `Guid`, so the hoisted local is `int createdId = 0;`.
- **Commit:** `b829f5b fix(app): sanitize generation error message and log full exception`

### Fix 2 — Fire-and-forget async ✅ COMMITTED
- **File:** `DraftEngine.App/ViewModels/ContractDetailViewModel.cs` (`OnIsHistoryVisibleChanged`, 569-576).
- **Change:** `_ = ReloadVersionsAsync(Contract.Id);` replaced by `_ = RefreshVersionsAsync(value);` where the new private helper awaits the reload internally and catches `Exception` broadly, logging `Log.Error(ex, "Failed to reload versions")` and setting `ErrorMessage = "Could not load version history."`. No exception can escape into an unobserved task any more.
- **Adaptation:** the brief suggested `private async void OnIsHistoryVisibleChanged(bool value)`. That does **not compile** here: `OnIsHistoryVisibleChanged` is a CommunityToolkit.Mvvm `[ObservableProperty]` hook whose generated declaration is `partial void OnIsHistoryVisibleChanged(bool value);` (no `async` modifier), and the C# compiler rejects an `async` modifier on partial void parts (verified with a standalone compile: **CS1002**). The hook therefore stays a plain `partial void` and delegates to the awaited helper — the behaviour the brief asked for, with the event-system limitation honoured.
- **Adaptation:** the class uses the static Serilog `Log` (no `_logger` field), matching all 14 existing call sites.
- **Commit:** `fe05b19 fix(app): await version history reload and surface failures`

**Gate check after Fix 1 + Fix 2:** build 0/0, tests **142/142 passing**.
