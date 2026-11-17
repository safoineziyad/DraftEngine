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

## Phase 2 — first-run profile and live gating (done)

- Onboarding is now four screens: language + theme on the first screen, then the profile cards,
  then the two existing walkthrough screens. The profile step offers Lawyer, Business and
  Mediator cards; Skip or finishing without a selection saves Lawyer.
- Theme choice applies immediately through `IThemeService`; language still applies immediately.
- `MainViewModel` filters Workspace/Tools/Bottom by `ProfileFeatureMatrix`. A `ProfileChanged`
  event re-filters live, raises the collection properties, and navigates to Dashboard when the
  current page is no longer visible. Hidden navigation commands are inert (including the
  Clause Manager shortcut), so no command can open a hidden page.
- Business profiles see `Profile.Notice.NotLegalAdvice` on Dashboard, New Contract and Analyze.
  Mediator profiles see `Dashboard.MediationPlanned` on Dashboard.
- All new strings exist in EN/FR/ES/AR; the count rose from 21 to 37 onboarding/profile keys.
- Tests: `ProfileModeTests` (visibility, live switch, fallback, inert shortcut, notice predicates)
  and `OnboardingProfileTests` (screen order, cards, Skip→Lawyer, saved choice, live theme).
  Existing onboarding tests were updated for four screens without losing coverage.
- Full suite: **409 passed, 0 failed** (232 + 149 + 28).
- Not visually verified: no desktop window was opened; cards, RTL and dark theme are unchecked.
- Profile-specific Getting started copy is not yet added.

## Phase 3 — Settings card, tests and docs (done)

- New `ProfileSettingsCard` UserControl embedded in Settings, bound to `SettingsViewModel`.
  `ProfileOptions` (Lawyer/Business/Mediator) start from the saved choice; changing the selection
  calls `IProfileService.SetProfileAsync`, which raises `ProfileChanged`, re-filters the sidebar
  and shows the localized `Settings.ProfileChanged` confirmation (no restart).
- `SettingsProfileTests` covers the three options, starting from the saved value, persistence and
  a localized confirmation without the `{0}` placeholder.
- `docs/PROFILES.md` now records where the choice is made (onboarding and Settings) and the
  no-data-deletion promise; all profile strings are in EN/FR/ES/AR.
- Full suite: **411 passed, 0 failed** (232 + 151 + 28).
- Not visually verified: the Settings card, onboarding cards, RTL mirroring and dark theme were
  not opened in a desktop window.
- Still missing: profile-specific Getting started copy (recorded as follow-up).

NEXT STEP: none scoped; profile mode Phase 1–3 complete pending visual review.
