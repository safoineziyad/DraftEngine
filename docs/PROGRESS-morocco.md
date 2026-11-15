# Morocco data-handling mode progress

## Phase 1 - network-call audit

- Read `docs/INVARIANTS.md` before starting.
- Confirmed AI provider profiles already exist; the Phase 2 policy must cover all active/editing profiles without adding another profile feature.
- Added the complete outbound-call inventory to `docs/NETWORK-CALLS.md`.
- The initial full-suite invocation reported 503 passed, but a clean Release rebuild discovered 370 tests (368 passed, 2 failed); the clean build's test output is the reliable current baseline.
- Fixed the migration preservation assertion to expect the migration's intentional display-name-to-canonical-ID conversion while still requiring every row and all other column values to be preserved.
- Fixed the snapshot comparison to normalize line endings on both expected and actual text; document content remains an exact comparison.
- `dotnet build DraftEngine.sln -c Release --nologo -v q`: succeeded, 0 errors (6 existing warnings).
- Both affected tests passed individually after the fixes. The full-suite gate after these fixes is pending.
- Working-folder constraint: continuing in this open folder on `wip/unfinished-save` as explicitly authorized; the required feature branch is checked out in a different folder and was not touched.

NEXT STEP: Commit and push the audit and test corrections, run `dotnet test DraftEngine.sln -c Release --nologo -v q`, and do not begin Phase 2 until Gate 1 is green and the user says continue.
