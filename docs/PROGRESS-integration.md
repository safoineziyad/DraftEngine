# Integration rc1 — progress

Branch: `integration/rc1` (from `origin/feature/i18n-theme` @ `c734130`).
Owner: integration agent. One line per batch; the NEXT STEP line is the resume point.

## Phase 0 — inventory (done)

- Inventory, build/test matrix and merge order in `docs/INVENTORY.md`.
- Baseline GATE 0 on `origin/feature/i18n-theme`: build exit 0, **493 tests / 492 passed / 1 failed**
  (`28 Analysis + 335 Tests (334 pass) + 130 App`). The 1 failure is the CRLF snapshot assertion
  `MultiTypeGenerationTests.MutualNda_AssembledText_MatchesSnapshot`.
- Branches measured in isolated worktrees: audit 513/0 fail, save 503/0 fail, profile 411/0 fail.
- `feature/morocco-data-mode` does not exist anywhere on the remote.

## Phase 1 — integrate

Not started.

NEXT STEP: merge `fix/audit-data-safety` into `integration/rc1`, build Release, run the full suite,
commit, then repeat for `wip/unfinished-save` and `feature/profile-mode`.
