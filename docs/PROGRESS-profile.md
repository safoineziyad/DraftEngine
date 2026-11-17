# Profile mode — progress

Branch: `feature/profile-mode` (worktree sibling of the main checkout; another agent owns
`feature/morocco-data-mode`).

## Phase 1 — model and service (complete)

- `UserProfile { Lawyer, Business, Mediator }` and `FeatureKey` for the eight pages that actually
  exist (`Dashboard`, `NewContract`, `Contracts`, `Archive`, `Analyze`, `Clauses`, `Settings`,
  `About`). `Templates`/`PlaybookEditor` were audited and do not exist, so they are not modelled.
- `ProfileFeatureMatrix` is the one source of truth: Lawyer sees all eight; Business and Mediator
  see all except `Clauses`; `null` (pre-profile installs) behaves as Lawyer.
- `IProfileService`/`ProfileService`: `Current` (nullable), `SetProfileAsync`, `ProfileChanged`,
  `IsFeatureVisible`; persisted through the existing `IAppSettingsService`.
- `AppSettings.Profile` is the only settings-model addition. `AppSettingsService` stores it as a
  readable string and reloads it; an optional settings-path constructor parameter enables
  temp-file tests without touching `%APPDATA%`.
- DI registers `IProfileService` as a singleton.
- `docs/PROFILES.md` records the matrix and the "never delete data" promise.

### Tests

`ProfileServiceTests` covers all 24 profile×feature cells, null-as-Lawyer for every feature,
the Business notice and Mediator note predicates, save/event/no-op behaviour, and a real
settings-file round trip (missing file → no profile; saved Business → reloaded Business; JSON uses
`"Profile": "Business"`).

### Notes

- `UpgradeMigrationTests` had a pre-existing failure in this branch: the v0.2.2 upgrade test
  compared every column of the seeded `Clauses` table against the pre-migration backup, but the
  `AlignClauseData` migration legitimately normalizes clause `ContractType` values. The assertion
  now matches the newer repository version: `Clauses` is reference data, so rows must be retained
  (and may be appended/normalized), while every user table is still compared value-for-value.
- Affected tests: `DraftEngine.App.Tests` 138 passed, 0 failed.
- Not visually verified (no profile UI exists yet).

NEXT STEP: Phase 2 — first-run profile step (language, theme, profile cards with Skip→Lawyer),
live sidebar gating with Dashboard fallback and inert shortcuts, Business notice on Dashboard /
New Contract / Analyze, Mediator dashboard note, and profile-specific Getting started content.
