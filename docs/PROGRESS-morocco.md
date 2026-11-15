# Morocco data-handling mode progress

## Phase 1 - network-call audit

- Read `docs/INVARIANTS.md` before starting.
- Confirmed AI provider profiles already exist; the Phase 2 policy must cover all active/editing profiles without adding another profile feature.
- Added the complete outbound-call inventory to `docs/NETWORK-CALLS.md`.
- Baseline full suite before the audit docs: 503 passed across the Analysis, App, and core test projects; 0 failed.
- `dotnet build DraftEngine.sln -c Release --nologo -v q`: succeeded, 0 errors (7 existing warnings).
- Full-suite gate after the audit docs: failed, 368 passed and 2 failed (370 total). `UpgradeMigrationTests.Version022Database_UpgradesFromCopiedDataDirectory_AndPreservesContract` reports a `Clauses.ContractType` value changed from `Mutual NDA` to `MutualNda`; `MultiTypeGenerationTests.MutualNda_AssembledText_MatchesSnapshot` reports a CRLF/LF snapshot mismatch. Both failures reproduced when rerun individually. No application or test code was changed for this documentation-only phase.
- Working-folder constraint: continuing in this open folder on `wip/unfinished-save` as explicitly authorized; the required feature branch is checked out in a different folder and was not touched.

NEXT STEP: Resolve the two full-suite failures without weakening tests, rerun the Gate 1 full suite, and do not begin Phase 2 until Gate 1 is green and the user says continue.
