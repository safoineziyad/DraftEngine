# Audit data-safety progress

Branch: `fix/audit-data-safety` in `DraftEngine-audit` (separate checkout).

## Step 0 — baseline (verified)

- Folder: `C:\Users\Zbook\Desktop\ziyad_Zbook\programming\DraftEngine-audit`
- Remote: `https://github.com/safoineziyad/DraftEngine.git`
- Branch: `fix/audit-data-safety`, tracking `origin/fix/audit-data-safety`; `git pull` reported up to date.
- Worktree: this checkout is the only one named `DraftEngine-audit`. Other sibling worktrees exist for other agents and were not touched.
- Leftover from the previous session: untracked `DraftEngine.Data/ProtectedDataUnavailableException.cs` (builds cleanly; kept, not discarded).
- `dotnet build DraftEngine.sln -c Release` → 0 errors.
- Baseline `dotnet test DraftEngine.sln -c Release --nologo -v q` with a fresh `DRAFTENGINE_DATA_DIR`:
  - `DraftEngine.Analysis.Tests`: 28 passed, 0 failed.
  - `DraftEngine.Tests`: 334 passed, 1 failed (`MultiTypeGenerationTests.MutualNda_AssembledText_MatchesSnapshot`, expected CRLF vs generated LF).
  - `DraftEngine.App.Tests`: 130 passed, 0 failed.
  - Total: 492 passed, 1 failed, 0 skipped.
- The one failure is a pre-existing line-ending assertion in this branch; the expected snapshot was not normalized. Fixed by normalizing both sides (the newer repository version does the same); no assertion was removed.

## FIX 1 — undecryptable DPAPI data is preserved (done)

- Red test first: `SensitiveDataEncryptionTests.UndecryptableCiphertext_IsPreservedWhenAnUnrelatedFieldIsSaved` failed because materialization replaced the value with `""`.
- Fix: `SensitiveDataInterceptor` now leaves the original ciphertext on the entity, records a `ProtectedDataUnavailableException` in `UnavailableColumns`, and raises `ProtectedDataUnavailable`. `EncryptChangedValues` already skips marker-prefixed values, so later saves write the ciphertext back unchanged. No schema change.
- Updated the pre-existing test that asserted the old scrub-to-empty behaviour; it now asserts preservation, the typed flag, and byte-identical ciphertext after an attach-and-update-everything save (`DpapiRoundtripIntegrationTests.DecryptFailure_WithCorruptedCiphertext_PreservesTheStoredValueAndFlagsIt`). Same coverage, corrected expectation.
- Added `Audit.ProtectedDataUnavailable` to EN/FR/AR/ES. The flag/event is exposed for the UI; no screen consumes it yet (not wired, not visually verified).
- Full suite: **494 passed, 0 failed** (336 + 130 + 28), exit 0.

NEXT STEP: FIX 2 — add ContractAnalyses and decrypted WizardDraft payloads to portable backup/restore/export, with DbSet coverage and WAL/protected-field tests.
