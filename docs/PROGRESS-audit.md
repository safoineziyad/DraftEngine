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

## FIX 2 — backup completeness (done)

- Added `ContractAnalyses` to the backup payload, export, import/ReplaceAll (children deleted first), sequence reset and JSON "Export my data"; CSV remains contract-centric.
- Wizard drafts are decrypted into `BackupWizardDraft.Payload` and re-protected with DPAPI on import. An undecryptable draft is skipped with a counted, content-free warning (`BackupSummary.SkippedWizardDrafts`) instead of aborting; export summary reports it.
- Added `BackupSummary.ContractAnalyses`/`SkippedWizardDrafts` (trailing defaults, so existing constructions compile).
- Tests: analyses + draft round trip with readable protected fields after import, backup bytes contain no synthetic plaintext markers, undecryptable draft skipped, WAL-mode committed row included, and a reflection DbSet-coverage test (`Users` explicitly excluded with reason).
- Full suite: **498 passed, 0 failed** (336 + 134 + 28), exit 0.

## FIX 3 — delete with analyses (done, no migration)

- Red test first: `ContractServiceTests.DeleteAsync_RemovesContractAnalyses_AndLeavesOtherContractsIntact` failed with SQLite error 19.
- Fix: `ContractService.DeleteAsync` loads the contract's analyses and removes them in the same `SaveChanges`, alongside clauses, fields, chat, documents and versions. Standalone analyses (`ContractId == null`) and other contracts are untouched. No schema change; `Users` keeps Restrict.
- Test covers archive-then-permanent-delete, direct delete with all child rows, no orphans and another contract's analysis surviving.

## FIX 4 — duplicate copies jurisdiction (done)

- Red test first: `ContractEditServiceTests.DuplicateContractAsync_CopiesJurisdictionFields` failed.
- Fix: `DuplicateContractAsync` now copies `GoverningLawCountry`, `GoverningLawSubdivision`, `VenueCountry` and `VenueCity`.
- Added `DuplicateContract_EveryPersistedPropertyIsEitherCopiedOrExplicitlyExcluded`, a reflection guard over every settable `Contract` property with explicit copied/not-copied sets; it fails if a new property is added without a decision.

## GATE 1

- Full suite: **501 passed, 0 failed** (339 + 134 + 28), exit 0. Baseline was 492 passed / 1 pre-existing failure.
- No migration was needed. `IF NOT EXISTS`-style upgrade paths were not touched.
- Phase 2 (FIX 5–9) is not started; UI changes are not visually verified.

NEXT STEP: Phase 2 — FIX 5 (hostile import limits and zip-bomb guards).
