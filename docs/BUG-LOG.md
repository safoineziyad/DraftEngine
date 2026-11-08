# DraftEngine Bug Log

Evidence log for defects found during the current v0.4.0 work. Sensitive payloads and secret values are intentionally never recorded.

| ID | Severity | How found | Evidence | Fix commit | Regression test |
|---|---|---|---|---|---|
| BUG-001 | High | EF analyzer warning review of portable restore SQL | Backup restore previously interpolated table identifiers into raw SQL. | `8b7bc40` | `PortableBackupTests.EncryptedBackupWithUnexpectedSqlIdentifiers_IsRejectedBeforeDatabaseChanges` |
| BUG-002 | High | Restore transaction/error-path review | A failed restore step could be swallowed during SQLite sequence reset; errors now propagate and roll back the restore transaction. | `8b7bc40` | `PortableBackupTests.FailedRestoreStep_RollsBackDatabaseChanges`; `PortableBackupTests.FailedSettingsRestore_RollsBackDatabaseChanges` |
| BUG-003 | Medium | Nullable analysis of contract edit service and its callers | Duplicate/update methods returned null when a contract was missing while their public types declared non-nullable results. | `8b7bc40` | `ContractEditServiceTests.DuplicateContractAsync_ReturnsNullWhenSourceContractIsMissing`; `ContractEditServiceTests.UpdateContractAsync_ReturnsNullWhenContractIsMissing` |
| BUG-004 | Low | Stage 0 documentation comparison | `GAP-REPORT.md` and `I18N-INVENTORY.md` contain stale localization claims and old key counts (15/26) that conflict with current code and 207 keys per resource file. | Not fixed in Stage 0; tracked for a documentation batch | N/A — documentation consistency |

## Verification notes

- BUG-001 through BUG-003 have regression tests in the current solution and are included in the 313-test Release run recorded in `docs/STATUS-AUDIT.md`.
- BUG-004 remains open until the older audit/inventory documents are revised or clearly archived as historical.
- This log is not a claim that the repository has no other defects; automated and targeted defect hunting remains pending.
