# DraftEngine — Progress

## v0.4.0 (Phase 2A)

| Stage | Status | Notes |
|---|---|---|
| 0 — Vision + startup safety net | Complete | `docs/VISION.md`; `DraftEngine.App.Tests` (DI, templates, navigation, page loads). |
| 1 — Data safety + polish | Partial | Backup/restore, JSON/CSV export, body search, shortcuts and diagnostics done. Reusable templates, letterhead and a dedicated dark mode were not built; dark mode is superseded by the i18n/theme work. |
| A — Audit + gap report | Complete | GAP-REPORT.md and I18N-INVENTORY.md written; 291 tests; zero warnings. All localization, theme, export and left-over gaps catalogued with evidence. |
| B — Real localization | **In progress — earlier report invalid** | Verified 2026-11-08: `LocalizationExtension.ProvideValue` returned a **static string** (no live binding); SettingsViewModel had been reduced to empty stubs and Dashboard/NewContract lost load logic, so those three files were reverted from HEAD. Only 26 resx keys/language vs **505** candidate literals (`docs/I18N-INVENTORY.md`). No RTL, digits, font fallback or language picker yet. |
| 2 — Analysis engine | Complete | DOCX/PDF/TXT import, segmentation, four playbooks, deterministic checks, grounded AI review, comparison, 25 tests including 10 fixtures and precision/recall. |
| 3 — Analyze UI + exports | Mostly complete | Analyze page, encrypted persistence, archive, report DOCX/PDF, redline builder. No library-clause insertion button and no side-by-side compare page. |
| 4 — Web readiness | Complete | `docs/WEB-ARCHITECTURE.md` plus coupling audit; Analysis has no UI/EF/DPAPI references. |
| 5 — Audit + release candidate | Complete with caveats | Version 0.4.0, docs updated, 291 tests. Manual UI/legal/signing steps remain. |

### Localization remediation (limit-proof batches)

Verified baseline 2026-11-08 on `feature/i18n-theme`:

- Build: 0 errors, 9 warnings. Tests: **291** (222 `DraftEngine.Tests`, 41 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`).
- Hardcoded candidates: **505** (285 XAML, 220 C#) vs **26** resx keys per language — see `docs/I18N-INVENTORY.md`.
- Reverted broken prior work: `SettingsViewModel` (109-line stub), `DashboardViewModel` (lost navigation reload), `NewContractViewModel` (lost logic), `MainWindow` title regression.

Historical checkpoint next step (superseded): implement live localization.

### Step 2 — live localization (Done, 2026-11-08, not visually verified)

Build 0 errors, **299 tests** (222 + 49 + 28) — up from 291.

- **2a Live binding — Done.** `LocalizationSource` (singleton, `this[string]`, raises `Item[]`) + `TExtension` (`{loc:T Key}`) returns a `Binding` to the source; `LocalizedFormatConverter` applies `{0}` args. Evidence: `DraftEngine.App/Services/Localization/LocalizationExtension.cs`, `LocalizationTests.SetLanguage_RaisesEvent_AndUpdatesLiveSource`.
- **2b ViewModels — Partial.** `SettingsViewModel.PageTitle` now comes from `GetString("Settings.PageTitle")` and is re-raised on `LanguageChanged`; other page titles are still hardcoded and migrate in Step 3.
- **2c Culture/digits — Done.** `SetLanguageAsync` sets `CurrentCulture`/`CurrentUICulture` (synchronous so it reaches the caller) and every window style sets `NumberSubstitution.Substitution=European`; startup overrides `FrameworkElement.LanguageProperty` metadata. Evidence: `LocalizationTests.SetLanguage_UpdatesCurrentCultureAndUiCulture`, `Arabic_IsRightToLeft_AndUsesEuropeanDigits`.
- **2d RTL — Done (logic), not visually verified.** `LocalizedWindowStyle` binds `FlowDirection`/`Language` on MainWindow, OnboardingWindow and LicenseActivationWindow; sidebar divider flips `IsRtl`. Evidence: `App.xaml`, window roots; RTL test above.
- **2e Font fallback — Done (declared).** `UiFontFamily = Segoe UI, Tahoma, Arial, Traditional Arabic`; rendering not visually verified.
- **2f Pickers — Done.** Settings card and first onboarding screen list English/Français/العربية/Español, apply immediately, persist via `RegistryLanguagePreferenceStore`; default is Windows language if supported else English. Evidence: `SettingsView.xaml`, `OnboardingWindow.xaml`, `OnboardingViewModel`, `LocalizationTests.LanguageChoice_PersistsAndReloads`, `Initialize_UsesWindowsLanguage_WhenSupported_ElseEnglish`.
- **2g Tests — Done.** 8 new tests cover live switch notification, RTL/LTR, European digits, culture, persistence round trip, Windows/default fallback, English fallback, missing-key `[Key]` (Debug), and format args.

Historical checkpoint next step (superseded): migrate MainWindow and sidebar literals.

### Step 3 — screen migration (in progress)

- **MainWindow + sidebar — Done.** All nav labels, AutomationProperties names and the version label now use `{loc:T}`; `MainWindow.xaml` passes the literal guard. Sidebar divider mirrors for RTL. Evidence: `MainWindow.xaml`, `LocalizationResourceTests.MainWindow_HasNoLiteralUserFacingStrings`.
- **Step 4 guard tests — Done.** `LocalizationResourceTests`: key parity across 4 languages, no empty values, every XAML key exists everywhere, and a shrinking pending-list guard for literal strings. Test count 299 → **304** (222 + 54 + 28).
- **Settings — Done (not visually verified), 2026-11-08.** `SettingsView.xaml` uses `{loc:T}` for every literal and `SettingsView.xaml` is removed from `PendingMigration`, so the literal guard now covers it. `SettingsViewModel` builds API-key/consent/term status and all dialogs, filters, warnings and error messages from `GetString`/format keys (no concatenated sentences); status text is stored as a key + arguments and re-rendered on `LanguageChanged`, so an in-flight result also switches language. 86 `Settings.*` keys added in EN/FR/AR/ES (128 keys per language). `LocalizationTests.SettingsViewModel_RerendersComposedText_OnLanguageChange` covers the live path. Test count 304 → **305** (222 + 55 + 28); build 0 errors. Translation status/glossary recorded in `docs/TRANSLATIONS.md`.
- **Partial:** provider-supplied connection failure text (`result.Message`) is displayed as received and is not translated; WPF rendering/layout of the migrated Settings screen was not visually verified.
- **Onboarding — Done (not visually verified), 2026-11-08.** `OnboardingWindow.xaml` buttons and automation names use `{loc:T}`; `OnboardingViewModel` stores step title/description/bullet keys and resolves them, plus the progress label and primary button, through `GetString`; all are re-raised on `LanguageChanged`. Falls back to a default English service when the shared source is not initialized (keeps the existing unit tests valid). 19 keys added (147 per language). `OnboardingWindow.xaml` removed from `PendingMigration`. Test count 305 → **306** (222 + 56 + 28); build 0 errors. Not visually verified.
- **Dashboard — Done (not visually verified), 2026-11-08.** `DashboardView.xaml` uses `{loc:T}` for the subtitle, stats, recent-contract header/total, empty state, loading text and New Contract buttons; the row automation name is now computed in the view model. `DashboardViewModel` localizes the page title and load-error banner (stored as a flag, so it re-renders on language change), rebuilds rows on `LanguageChanged`, and passes the service to `ContractRowViewModel`. `ContractRowViewModel` localizes the untitled fallback, status enum, accessible name and a culture-aware date format (`Common.DateTimeFormat`). 17 keys added (164 per language). Test count 306 → **307** (222 + 57 + 28); build 0 errors. Not visually verified.
- **Contracts list — Done (not visually verified), 2026-11-08.** The list view and view model use localized labels/status text, with rows and filter labels refreshed on language changes. 11 keys added to EN/FR/AR/ES. Evidence: `ContractsListView.xaml`, `ContractsListViewModel.cs`, `LocalizationTests.ContractsList_RerendersRowsAndFilterLabels_OnLanguageChange`. Full solution build/test passed with 308 tests (222 + 58 + 28).
- **New Contract wizard — Done (not visually verified), 2026-11-08.** The view, step labels, contract type cards, intake labels, summary fields, clause counts, accessibility names, confirmations, and error messages use localization keys in EN/FR/AR/ES. Core summary rows now return stable intake field keys. Added 66 keys per language and shrank the XAML literal baseline from 158 to 134. Evidence: `NewContractView.xaml`, `NewContractViewModel*.cs`, `ContractContentBuilderTests.BuildSummary_ReturnsFieldKeysRatherThanDisplayLabels`, `LocalizationTests.NewContractDynamicLabels_RerenderWhenLanguageChanges`, `LocalizationResourceTests.PendingLiteralCount_DoesNotExceedBaseline`. Release suite: **318** tests (225 + 65 + 28). Not visually verified.
- **Contract detail — Done (not visually verified), 2026-11-08.** Labels, automation names, diff summary, export/version history, dialogs, file filters, statuses and screen messages use keys in EN/FR/AR/ES; contract type/status and history rows refresh when language changes. Added 94 keys per language. Removed `ContractDetailView.xaml` from `PendingMigration`; the remaining five pending screens now have 90 literal candidates (down from 134). Evidence: `ContractDetailView.xaml`, `ContractDetailViewModel.cs`, `ContractDetailLocalizationTests.ContractDetailAndHistoryRows_RerenderAfterLanguageChange`, and `LocalizationResourceTests.MigratedScreens_ContainNoLiteralUserFacingStrings`. Release build succeeded in an isolated output directory because the running DraftEngine process locked the normal output. All three Release test assemblies passed individually: **326 total** (225 + 73 + 28). Not visually verified; human translation review remains.
- Clause Manager, Analyze, About, Archived Contracts and License Activation — pending; migrate one screen per batch.

### Step 1 — warning fixes (Done, 2026-11-08)

- Portable backup SQL identifiers are fixed literals selected only from a hard-coded list; the table-name predicate is parameterized. Unknown JSON members are rejected during authenticated payload deserialization, before the pre-import snapshot or database access. `PortableBackupTests.EncryptedBackupWithUnexpectedSqlIdentifiers_IsRejectedBeforeDatabaseChanges` uses a correctly re-encrypted malicious payload and confirms unchanged database contents.
- Restore database changes and settings persistence remain inside one transaction; failed restore steps propagate, trigger rollback, and log only the exception type. The broad SQLite-error swallow during sequence reset was removed. Evidence: `PortableBackupService.ReplaceAllAsync`, `ResetSequencesAsync`, `PortableBackupTests.FailedRestoreStep_RollsBackDatabaseChanges`, `PortableBackupTests.FailedSettingsRestore_RollsBackDatabaseChanges`.
- `IContractEditService` now explicitly returns nullable contracts for update/duplicate. Existing detail/list callers handle missing contracts; `ContractEditServiceTests` covers both missing-contract paths.
- `JsonException` no longer binds an unused exception variable. Full solution build/test passed: **313 tests** (224 + 61 + 28); five regression tests added since the 308-test recovery baseline. One unrelated pre-existing `CS8625` warning remains in `DraftEngine.Tests/Commercial/UpdateCheckServiceTests.cs`.

### Stage 0 — status audit (2026-11-08)

- Release build plus all tests passed: **313** (224 `DraftEngine.Tests`, 61 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`). Six warnings remain; three are production nullable warnings in `DraftEngine.Core/Services/UpdateCheckService.cs`.
- Wrote `docs/STATUS-AUDIT.md` with evidence-based statuses for the requested feature areas and `docs/BUG-LOG.md` with three fixed defects and one open documentation inconsistency.
- Resource count: 207 keys per language. The pending-XAML literal guard finds 158 candidates across seven pending screens; this excludes C# literals and is not a complete user-facing string count.
- Release build/test evidence is from `dotnet test DraftEngine.sln -c Release --no-restore`; no UI was visually verified.

NEXT STEP: none for this batch — Tasks 1–4 are implemented. Remaining known work: the pre-existing 0.2.2 upgrade data loss (see Task 1 note) and migrating Clause Manager / Analyze / About / License Activation.

### Final verification (2026-11-10)

- Release build: **0 errors**, 7 warnings (pre-existing nullable warnings).
- Full suite: **363 tests, 362 pass, 1 fail** — `DraftEngine.Tests` 225/225, `DraftEngine.Analysis.Tests` 28/28, `DraftEngine.App.Tests` 109/110.
- The single failure is the **pre-existing** `UpgradeMigrationTests.Version022Database_UpgradesFromCopiedDataDirectory_AndPreservesContract`, reproduced on the clean baseline before any edits (upgrading a 0.2.2 database leaves `Contracts=0`). It is unrelated to Tasks 1–4 and must be fixed separately.
- Test count rose from **329** at baseline to **363** (+34).
- XAML was rendered on an STA thread in Light, Dark and High Contrast for all 9 pages with no exceptions; **no screen was visually reviewed** in this environment.

### Task 2c — High Contrast (Done, not visually verified, 2026-11-10)

- Added `Themes/HighContrast.xaml` mapping every semantic brush to Windows system colors; `ThemeService` selects it automatically when `SystemParameters.HighContrast` is true (even with an explicit Light/Dark choice), never forces a dark title bar in that mode, and re-evaluates on High Contrast changes. `IsHighContrast` is exposed for tests.
- Tests: `HighContrast_DefinesEveryReferencedBrush`, `HighContrast_OverridesTheSelectedPalette`. Affected suite 25/25 pass.

### Task 3 + ratchet — pickers and a lower literal baseline (Done, not visually verified, 2026-11-10)

- Language picker already offered English/Français/العربية/Español in their own names, applied immediately and persisted; new tests pin that behaviour (`SettingsPickerTests`). The theme picker added in 2a follows the active language ("Clair" after switching to French).
- Migrated **Archived Contracts** to `{loc:T}`: subtitle, refresh, header, empty state, restore/delete buttons plus page title and all error/confirm text now come from 13 new `Archived.*` keys in EN/FR/AR/ES. Removed it from `PendingMigration` and lowered `docs/i18n-baseline.txt` **90 → 84**. The color-literal ratchet is now fixed at **0**.
- Tests: `SettingsPickerTests` (3). Build 0 errors; affected tests 42/42 (LocalizationResource + WpfPageOpen + StartupResolution + SettingsPicker) pass.

### Task 2b — control styles, popups and color literals (Done, rendered-not-visually-verified, 2026-11-10)

- Added 24 semantic brush keys to **both** Light and Dark (control/input surfaces, popup, hover/selected, tooltip, row alternate, accent foreground, warning, diff foreground, success surface, badge, scrollbar thumb, tab selected, danger/success/accent-muted foreground). Light and Dark key sets are identical.
- `Themes/Styles.xaml` now themes Button (quiet/primary), TextBox, PasswordBox, CheckBox, ComboBox **and its dropdown popup**, ComboBoxItem, ListView/ListViewItem, DataGrid/ColumnHeader/Row/Cell, Expander, ContextMenu/MenuItem, ToolTip, TabControl/TabItem and ScrollBar. Accent foreground is now a theme brush so dark primary buttons use dark text on light blue.
- Removed **all 22** color literals from views/styles (regex count 22 → 0); error banners use `DangerForegroundBrush`/`SuccessForegroundBrush`, the diff uses `DiffAdded/RemovedForegroundBrush`, the API-key warning uses the warning brushes.
- Tests: new `ThemeDictionaryTests` (key parity, 15 WCAG AA pairs in both themes, every referenced `*Brush` exists in both, zero color literals) and `WpfPageOpenTests` (opens all 9 pages under en/fr/ar/es × Light/Dark on an STA thread, asserting RTL FlowDirection and a resolved theme brush). A temporary STA harness also rendered all 10 views/windows in both themes with 0 failures; it was removed after use. Affected suite 35/35 pass. **Rendered without exceptions; not visually verified.**

### Task 2a — theme service wiring + picker (Done, not visually verified, 2026-11-10)

- `IThemeService`/`ISystemThemeProvider` are now registered in DI and initialized in `App.OnStartup` **before** the first window is created (`themeService.Initialize(settings.Theme)`), so there is no light flash. `ThemeService.TrySetDarkTitleBar` is applied to MainWindow (DWM attribute 20 with 19 fallback, failures silent).
- `ThemeService.Apply` now removes every Light/Dark theme dictionary (including the static `Themes/Light.xaml` fallback) before inserting the applied one, so a dark theme is never shadowed by the light fallback. `RegistrySystemThemeProvider` live-switches only in Follow-Windows mode.
- Settings gains a theme picker (Follow Windows/Light/Dark) that applies and persists immediately; a manual Settings save now preserves `Theme`/`Language` instead of resetting them. New `Settings.ThemeDescription` key in EN/FR/AR/ES.
- Tests: `ThemeTests` (17 incl. the resolution matrix, persistence round trip, live System switch, explicit-mode isolation and the picker). Affected suite 44/44 pass; build 0 errors. **Not visually verified.**

### Task 1 — sidebar (Done, not visually verified, 2026-11-10)

Verified baseline on `feature/i18n-theme` before any edits: Release build 0 errors, full suite **329 tests** (225 `DraftEngine.Tests`, 76 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`). One **pre-existing** failure: `UpgradeMigrationTests.Version022Database_UpgradesFromCopiedDataDirectory_AndPreservesContract` — upgrading a 0.2.2 database leaves `Contracts=0` (the seeded default user is replaced and cascades). Reproduced on the clean tree at `9963a66`, unrelated to this work.

- Sidebar regrouped: **Workspace** (Dashboard, New Contract, Contracts, Archived), **Tools** (Analyze, Clause Manager), bottom-pinned **Settings** and optional **About**. Exactly one Settings entry (test asserts it).
- Every item has a Segoe Fluent Icons glyph (Segoe MDL2 Assets fallback) via `IconFontFamily`; labels, automation names and `ToolTip` come from `NavigationItemViewModel`/`{loc:T}`. Active page shows an accent bar on the leading edge and a `NavActiveBrush` tint; hover and keyboard-focus states added. Layout mirrors under RTL because each row is a `FlowDirection`-aware grid.
- Footer shows `AppVersion.Display` (never hardcoded) and the build commit hash when `AppVersion.HasCommitHash` is true (`Main.CommitLabel` localized). New keys `Nav.GroupWorkspace`, `Nav.GroupTools`, `Main.CommitLabel` added in EN/FR/AR/ES.
- Tests: new `SidebarTests` (6) covering single Settings, grouping, icons, active marker, live label switch and About gating. Build 0 errors; affected tests 12/12 pass. **Not visually verified.**

### Test counts

- v0.4.0 start: 222.
- End of Contracts-list recovery batch: **308** (222 `DraftEngine.Tests`, 58 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`).
- End of Step 1: **313** (224 `DraftEngine.Tests`, 61 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`).
- End of New Contract wizard batch: **318** (225 `DraftEngine.Tests`, 65 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`).
- Current branch after the update-check commit and Contract-detail localization: **326** (225 `DraftEngine.Tests`, 73 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`).

### Audit notes (verified in code)

- No logging of contract text, intake, chat, keys, AI bodies or analysis content; the only new
  logs are counts and generic failures.
- Analysis AI is consent-gated twice (analyzer options and `AppAiReviewer`), uses the existing
  redaction service, grounds quotes verbatim and strips citation-like text.
- Deterministic analysis makes zero network calls (no HTTP client in `DraftEngine.Analysis`).
- New sensitive stores are encrypted: `ContractAnalyses.FileName/ResultJson` (DPAPI) and
  portable backups (AES-256-GCM). Tests assert no plaintext in the backup container.
- Existing snapshot-before-destructive-change and navigation-reload invariants are unchanged.

## v0.3.0

Complete: 222 tests, multi-type generation, clause manager, drafts, archive, AI diff/redaction,
onboarding, About, licensing behind a flag.
