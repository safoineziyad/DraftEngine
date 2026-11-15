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

NEXT STEP: Update work Phase 4 — release tooling: `tools/UpdateKeyTool` (keygen/sign/verify), `scripts/release.ps1` (-Version -NotesFile -Prerelease -DryRun), move/build `scripts/build-installer.ps1`, docs/RELEASE.md key backup, .gitignore key patterns and a history scan. Phase 3 UI is complete but not visually verified.

(Historical, previous round) Phases 1–4 were complete (GATE 1: 372/372; GATE 2: installer + SHA-256; pushed to `feature/i18n-theme`; `v0.4.0-beta.1` published as a pre-release with `v0.2.2` still Latest). Remaining known work: migrate Clause Manager / Analyze / About / License Activation, human translation review, code signing, and click-through of the installed beta (not visually verified).

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

### Phase 1 — 0.2.2 upgrade data-loss fix (2026-11-12)

**Root cause (reproduced, not the reported one).** No migration deletes or re-inserts the user and no cascade fires. `dotnet ef migrations script` contains no `DELETE FROM "Users"` / `DROP TABLE "Users"` (0 matches; only the `InitialCreate` seed insert). A step-by-step migration harness kept `Contracts = 1` after every migration. The loss happened earlier: EF `Migrate()` leaves SQLite in WAL mode (`PRAGMA journal_mode = wal`, `-wal` file 457,352 bytes), and the test's `File.Copy` of only `draftengine.db` produced a stale/incomplete database, so the verifier saw `Contracts = 0`. Fresh databases default to `journal_mode = delete`; only after the first `Migrate()` does WAL appear.

**Fixes.**
- All database copies/snapshots now use the SQLite online backup API (`SqliteConnection.BackupDatabase`), never `File.Copy`.
- `DatabaseService.EnsureDatabaseCreated` takes the snapshot **before** any migration, aborts the upgrade if the snapshot cannot be created, and restores the snapshot (clearing pools, `-wal`, `-shm`) if a migration throws. The exception carries `DatabaseService.RestoredDataKey = true`.
- Startup shows a localized `StartupErrorWindow` with a “Copy diagnostics” button that copies only version, Windows, .NET, exception type and the restored flag (`StartupDiagnostics`); no messages, paths or contract data.
- `Contracts.UserId → Users.Id` is now `DeleteBehavior.Restrict` via the new corrective migration `20261110212129_RestrictUserCascade` (released migrations untouched; `Clauses.OwnerUserId` was already Restrict).

**Test evidence.** Full suite **372 passed, 0 failed** (232 `DraftEngine.Tests`, 112 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`), up from 363 with 1 failure. New/updated coverage:
- `UpgradeMigrationTests` passes and compares every user table row-by-row, by column name and row Id, between the pre-upgrade snapshot and the upgraded database (byte equality is invalid because SQLite rebuilds tables). The seeded `Clauses` library may grow/normalize; every original clause Id must survive.
- `UpgradeDataPreservationTests`: real 0.2.2 schema fixture (3 contracts, fields, clauses, versions, chat messages, generated documents) and a 0.3.x-era fixture; identical row counts for every user table and retained contract text/clause ids.
- `DatabaseBackupTests.CreateSnapshot_FromWalDatabase_IncludesCommittedRows` reproduces the WAL root cause.
- `DatabaseUpgradeFailureTests`: a deliberately failing migration restores the original database, keeps the contract row and reports the restore.
- `MigrationUserSafetyTests`: scans every migration `UpOperation` and fails on `DeleteData`/`DropTable`/SQL delete targeting Users or Contracts.
- `CascadeProtectionTests`: every FK to `User` is `Restrict`; deleting the user throws and the contract survives.
- `StartupDiagnosticsTests`: diagnostics contain no contract text, key or path.

**Real data-folder copy check (counts only).** A copy of `DE-backup-20261110-2126` was migrated through `DatabaseService` in a temp folder (source untouched): Users 1→1, Contracts 1→1, ContractFields 9→9, ContractClauses 9→9, ContractVersions 1→1, Clauses 42→42, ChatMessages 0→0, GeneratedDocuments 0→0, ContractAnalyses 0→0; `__EFMigrationsHistory` 10→11. **Not visually verified.**

### Phases 2–4 — installer and beta pre-release (2026-11-12)

- Versions agree: assembly/file `0.4.0.1`, informational `0.4.0-beta.1`, installer `AppVersion 0.4.0.1`; the UI reads the assembly. Installer `AppId` is the v0.2.2 GUID and there is no `[UninstallDelete]`, so `%APPDATA%\DraftEngine` is never removed. The build script fails if either invariant changes.
- Kept the framework-dependent publish (`--self-contained false`): the installer is 14.2 MB instead of ~70 MB+, and `InitializeSetup` now checks for the x64 .NET 8 Desktop Runtime, stops with a clear message and opens the download page when it is missing.
- `installer/DraftEngine.iss` excludes `*.pdb`; `scripts/build-installer.ps1` cleans, publishes, locates ISCC, skips signing cleanly, writes the `.sha256` and prints every path. Output moved from `dist/` to `installer/Output/` (both ignored).
- Installer: `installer/Output/DraftEngine-Setup-0.4.0.1.exe` (14,256,171 bytes), SHA-256 `40aaee43fe6c98e896114fe3eacacd03c5c0b023fbf52cca11b2d4d9902e5948`.
- Published `v0.4.0-beta.1` as a **pre-release** from `feature/i18n-theme` with the installer and checksum assets; `v0.2.2` remains Latest. Release notes: `docs/RELEASE-NOTES-0.4.0-beta.1.md`.
- `installer/Output/`, `publish/` and `dist/` are git-ignored; no installer, `.pdb`, key or secret is committed. Release upload was slow but completed; asset digest matches the local hash.
- **Not visually verified:** the installer wizard (including the runtime check), the installed app, and the localized startup error window.

### Phase 1 — SQLite backup safety audit (2026-11-12, batch 2)

| Path | Code | Mechanism | WAL-safe |
|---|---|---|---|
| Pre-migration snapshot | `DatabaseService.BackupBeforePendingMigrations` → `CreateSnapshot` | `SqliteConnection.BackupDatabase` | Yes |
| Migration-failure restore | `DatabaseService.TryRestoreBackup` → `RestoreSnapshot` | Opens/validates the snapshot, deletes the live file and `-wal`/`-shm`, then `BackupDatabase` into a fresh destination (was `File.Copy`) | Yes |
| Portable encrypted backup | `PortableBackupService.ExportEncryptedAsync` → `CollectAsync` | Reads entities/rows into an encrypted JSON payload; never copies the file | Yes (no file copy) |
| Portable restore | `PortableBackupService.ImportEncryptedAsync` → `ReplaceAllAsync` | Row deletes/inserts in one transaction plus an encrypted pre-import payload | Yes (no file copy) |
| “Export my data” (JSON / CSV) | `PortableBackupService.WriteJsonAsync` / `WriteCsvAsync` | Queries rows through EF; no database file access | Yes (no file copy) |
| Diagnostics | `StartupDiagnostics` | Version/OS/.NET/exception types only; no database access | N/A |
| Tests | `UpgradeMigrationTests.CopyDatabase`, `DatabaseService.CreateSnapshot` in `UpgradeDataPreservationTests` / `DatabaseBackupTests` | `BackupDatabase` | Yes |

- New test `DatabaseBackupTests.WalDatabase_BackupAndRestore_PreservesEveryTable`: builds the real schema, inserts into `Contracts`, `ContractFields`, `ContractClauses`, `ChatMessages`, `ContractVersions`, `GeneratedDocuments` with `journal_mode=WAL` and `wal_autocheckpoint=0` (asserts a non-empty `-wal`), snapshots, damages the live database, restores through `RestoreSnapshot`, and asserts row counts are identical for **every** table in `sqlite_master`.
- Affected tests: 9/9 pass. **Not visually verified** (no UI change).

### Phase 2 — country catalog (2026-11-12, batch 3)

- Bundled `DraftEngine.Core/Data/countries.json`: 264 CLDR country/territory entries with alpha-2 codes and names in en/fr/ar/es, embedded as a resource (no runtime network). Source and Unicode License v3 recorded in `docs/LICENSES.md`.
- `CountryCatalog`: culture-aware sorting (Arabic included), diacritic-insensitive prefix/word search, Windows-region default with a US fallback.
- `SubdivisionCatalog`: US states + DC (51), Canadian provinces/territories (13), Australian states/territories (8), UK nations (4); every other country uses free text.
- `JurisdictionResolver`: composes the "Country — Subdivision" noun phrase and maps legacy values (`California`, `Ontario`, `England and Wales`, localized country names); unknown text is never discarded.
- Tests: `CountryCatalogTests` 16/16 pass (four-language coverage, no duplicate codes, per-culture sort order, diacritic and Arabic search, subdivision counts, legacy mapping). **Not visually verified.**

### Phase 2 — structured jurisdiction storage (2026-11-12, batch 4)

- `Contract` gains `GoverningLawCountry` (ISO alpha-2), `GoverningLawSubdivision`, `VenueCountry` and `VenueCity`; the original `GoverningLaw` display text is retained.
- Migration `20261115112708_AddJurisdictionFields` adds the columns and maps known legacy values (US states/DC, Canadian provinces, Australian states, UK nations and “England and Wales”) without altering the original text. Unknown values stay free text.
- Portable backup, encrypted restore, JSON export and CSV export now carry the four fields; settings and `BackupSettings` gained default country/subdivision with a Windows-region default.
- Tests: `JurisdictionMigrationTests.LegacyFreeText_IsMapped_AndUnchanged` (3 legacy contracts, including an unknown value) and `PortableBackupTests.EncryptedBackup_RoundTripsJurisdictionFields` pass. **Not visually verified.**

### Phase 2 — jurisdictions (complete, 2026-11-12)

- **Country data.** 264 CLDR country/territory entries in en/fr/ar/es bundled as `DraftEngine.Core/Data/countries.json` (Unicode License v3 in `docs/LICENSES.md`); no runtime network.
- **Pickers.** The wizard's governing-law field is now a `Jurisdiction` picker: editable country combo with type-ahead, diacritic-insensitive filtering, culture-aware sort (Arabic included), recent countries first, Windows-region default, built-in subdivisions only for US (+DC), Canada, Australia and the four UK nations, free-text subdivision elsewhere. Courts/venue use a country picker plus free-text city. The Settings default governing law uses the same country list.
- **Storage.** `GoverningLawCountry`, `GoverningLawSubdivision`, `VenueCountry`, `VenueCity` columns; migration `20261115112708_AddJurisdictionFields` maps old free text (US states, Canadian provinces, Australian states, UK nations, “England and Wales”) without changing it; unknown text stays free text. Backup, restore, JSON and CSV export carry the fields; settings persist the default country/subdivision and recent countries.
- **Clause wording.** Governing-law clauses now read “This Agreement is governed by the laws of the following jurisdiction: {{GoverningLaw}}.” and are flagged `NeedsLegalReview` by migration `20261115113011_AddClauseLegalReviewFlag` (no user-authored clause is touched). The review step shows the localized notice in EN/FR/AR/ES (`Wizard.JurisdictionDisclaimer`).
- **Evidence.** GATE 2: `dotnet test DraftEngine.sln -c Release --nologo -v q` → exit 0, **399 passed, 0 failed** (251 + 120 + 28), up from 373. New tests: `CountryCatalogTests` (16), `JurisdictionMigrationTests` (2), `IntakeFieldViewModelTests` (7), `PortableBackupTests.EncryptedBackup_RoundTripsJurisdictionFields`; `ContractServiceTests` and the generation snapshot assert the reworded clause. **Not visually verified** (wizard/settings pickers, RTL layout).

### Phase 3 — AI profiles (complete, 2026-11-12)

- **Profiles.** `AiProfile` (Core) stores name, preset, base URL, model and key. Presets: OpenAI-compatible, Azure OpenAI, Ollama, LM Studio, custom. Settings persists any number of profiles with each key DPAPI-encrypted; the legacy single key/model/custom-URL file migrates into one profile on load, and callers that save a fresh settings object cannot wipe the list.
- **No hardcoded models or prices.** The model picker is editable and populated only from the provider's `GET {base}/models` (OpenAI `data[].id` and Ollama `models[].name` shapes) plus whatever id the user types. Azure deployments that expose no model list are verified with one minimal chat request.
- **Connection test.** `AiProviderClient` classifies invalid key (401), access denied (403), quota vs rate limit (429 body), network/timeout, wrong URL (404 without a model error), model not found, and 5xx, with human-readable sentences and no key or response body in the message.
- **Consent.** `SettingsBackedAiService` now refuses every AI call with `OpenAiConsentRequiredException` until `AiDataConsentAccepted` is true, so no request can be sent without consent even if a caller forgets its own check.
- **Backup.** Portable backups carry profile metadata (id, name, preset, base URL, model) without keys; restore merges them and keeps a local key only when the same profile id already exists on the machine.
- **Azure/local.** Azure uses the `api-key` header; loopback base URLs may run without any key, while remote endpoints still require one.
- **Evidence.** GATE 3: `dotnet test DraftEngine.sln -c Release --nologo -v q` → exit 0, **418 passed, 0 failed** (264 + 126 + 28), up from 399. New tests: `AiProviderClientTests` (12: parsing, 401/403-shaped access, quota, 5xx, network, wrong URL, model-not-found, keyless local, Azure header, chat fallback, base URL/model in chat), `AiProfileSettingsTests` (3), `AiConsentGateTests` (2), `EncryptedBackup_RestoresAiProfiles_WithoutKeys`. **Not visually verified** (Settings profile editor, preset picker, RTL layout).

### Phase 4 — localization audit (complete, 2026-11-12)

- **All screens migrated.** Clause Manager, Analyze, About and License Activation replaced every literal `Text`/`Content`/`ToolTip`/`Title`/`AutomationProperties.Name` with `{loc:T}` keys. `PendingMigration` is empty and `docs/i18n-baseline.txt` is **0**; the tests now forbid any literal user-facing string in any screen.
- **New keys.** 84 keys added per language (About 18, Analyze 24, Clause 29, License 11, plus three page titles), for **499 keys per language** with parity, no empty values and every XAML key present in all four files.
- **Page titles and license messages.** Clause Manager, Analyze and About page titles plus the license activation window's status sentences now come from resources (English fallback only if the localization service is unavailable).
- **Theme/RTL audit.** No hardcoded colours remain in XAML (`Background`/`Foreground`/`BorderBrush`/`Fill="#…"`: none); every brush still resolves in Light, Dark and High Contrast (`ThemeDictionaryTests`), and RTL is applied through the shared window style's `FlowDirection` binding, so the migrated screens mirror with the sidebar.
- **Known gaps recorded.** Remaining English C# status strings in `ClauseManagerViewModel`, `AnalyzeViewModel`, `AboutViewModel` and `LicenseService`, English-only clause/legal seed text, and human translation review are listed in `docs/TRANSLATION-STATUS.md` and `docs/I18N-INVENTORY.md`.
- **Evidence.** GATE 4: `dotnet test DraftEngine.sln -c Release --nologo -v q` → exit 0, **418 passed, 0 failed** (264 + 126 + 28), including `LocalizationResourceTests`, `WpfPageOpenTests` and `ThemeDictionaryTests`. **Not visually verified** in any language; Arabic RTL has not been seen.

### Update work Phase 1 — audit and design (complete, 2026-11-12)

- **Baseline recorded at HEAD before any update change:** `dotnet test DraftEngine.sln -c Release --nologo -v q` → exit 0, **418 passed, 0 failed** (264 + 126 + 28).
- **Audit.** `UpdateService` (commit `0d94c21`) pulled `api.github.com/repos/safoineziyad/DraftEngine/releases/latest`, downloaded the first `DraftEngine-Setup-*.exe` from `github.com` and launched it with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS`, then exited — silent install, no hash, no signature, no Authenticode check, no snapshot, no confirmation, no skip/remind, no cadence, hardcoded repo URLs. `UpdateCheckService` was a notify-only static-manifest check (off by default) with HTTPS but no host allowlist, signature or integrity. `UpdateVersionComparer` ignored prerelease tags. The Inno script hardcoded `AppVersion`. Recorded in `docs/UPDATES.md` §1; those paths are replaced in Phases 2–3.
- **Design of record** written to `docs/UPDATES.md`: manifest schema (schemaVersion, version, publishedAt, minimumSupportedVersion, per-language releaseNotes, installer {url, fileName, sizeBytes, sha256}, signature), canonical UTF-8 serialization and ECDSA P-256 signature, HTTPS plus exact-host allowlist, downgrade refusal, 15-second/24-hour cadence, verified `.partial` download, snapshot + `/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS` launch after explicit confirmation, error taxonomy, privacy, release process and threat model.
- **Single version source.** New `Directory.Build.props` holds `DraftEngineVersion` (0.4.0-beta.1) and `DraftEngineFileVersion` (0.4.0.1); the app csproj consumes them for Version/AssemblyVersion/FileVersion/InformationalVersion, and `installer/Build-Installer.ps1` passes the numeric version to Inno Setup as `/DAppVersion`, with an `#ifndef` fallback in the .iss.
- **Invariant updated.** `docs/INVARIANTS.md` #9 now states update checks are on by default and notify-only, can be disabled (zero requests), and are disclosed.
- **GATE 1:** full suite green at 418/418, exit 0. **Not visually verified.**

### Update work Phase 2 — core logic (complete, 2026-11-12)

- **New UI-free library `DraftEngine.Updates`** (net8.0, references Core only, no WPF): `SemanticVersion` (SemVer 2.0.0, prerelease ordering, build metadata ignored), `UpdateManifest` + `ManifestCanonicalizer`, `EcdsaManifestVerifier` (P-256, SPKI public key), `UpdateManifestParser` + `UpdateHostPolicy`, `UpdateService`, `ProcessInstallerLauncher`, `WinVerifyTrustAuthenticodeVerifier`, `UpdateOptions`, clock/state abstractions and the `UpdateErrorCode` taxonomy mapped to resource keys.
- **Behaviour.** Checks: disabled → zero HTTP; automatic checks throttled to 24 h; manual bypasses cadence; 10 s manifest timeout with no retries; 1 MiB manifest cap; stable channel ignores prereleases; downgrades refused; skipped version remembered; logs contain only versions and error types. Downloads: HTTPS + exact-host allowlist, `.partial` streaming with progress, size and SHA-256 verification, Authenticode when configured, deletion on every failure, atomic move into `%LOCALAPPDATA%\DraftEngine\updates`. Install: caller-supplied snapshot callback runs first, then `/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`; a declined UAC prompt or launch failure returns a typed error and never exits the app by itself.
- **Tests.** New suites `DraftEngine.Tests/Updates/SemanticVersionTests` (ordering matrix, invalid versions, build metadata) and `UpdateFlowTests` (parser validation incl. look-alike hosts/HTTP, canonical stability, valid/tampered/wrong-key/missing signatures, cadence, manual override, disabled zero-request, downgrade, skip, 404/timeout/offline/malformed, hash mismatch, truncation, blocked host, Authenticode-required, snapshot-then-launch ordering, snapshot/launch failures, log hygiene): **73 new tests, all passing**.
- **Evidence.** GATE 2: `dotnet test DraftEngine.sln -c Release --nologo -v q` → exit 0, **491 passed, 0 failed** (337 + 126 + 28), up from 418. The previous App `UpdateService`/`UpdateCheckService` are still present and unused by the new library; Phase 3 removes them from the UI path.

### Update work Phase 3 — UI flow and privacy (complete, 2026-11-15, not visually verified)

- **Old path removed.** `UpdateService`/`IUpdateService` (GitHub API, silent `/VERYSILENT` install) and `UpdateCheckService`/`IUpdateCheckService` were deleted, along with their tests; `ServiceRegistration` now registers a named `updates` HttpClient, `WpfUpdatePrompt`, `UpdateSnapshotService` and the singleton `UpdateFlowViewModel`. `StartupResolutionTests` resolves the new graph.
- **UI.** `UpdateFlowViewModel` drives the banner (version line, "What's new" notes panel in the selected language with English fallback, Update/Later/Skip, progress bar), About and Settings controls (check now, automatic checks toggle, beta toggle, last checked, current version), and localized error text from `Update.Error.*`; the About sidebar item gets a dot. Installation requires `IUpdatePrompt.ConfirmInstall` (defaults to No); declining leaves the app running and never launches the installer. `UpdateSnapshotService` copies the live SQLite database with `BackupDatabase` before launch. Settings persist `UpdateChecksEnabled`, `UpdateIncludeBeta`, `SkippedUpdateVersion`, `LastUpdateCheckUtc`; `SettingsViewModel.SaveAsync` preserves them.
- **Startup behaviour.** 15-second delayed check, at most one automatic check per 24 hours, no retries; disabled means zero HTTP. `appsettings.json` has an empty `Updates:PublicKey` until the release key is generated, so every manifest fails verification and no update is offered.
- **Privacy.** The disclosure sentence was appended to `About.PrivacyBody`, `Settings.PrivacyDescription` and a new onboarding bullet in all four languages; 48 new update/settings keys per language remain machine-drafted.
- **Tests.** New `UpdateFlowViewModelTests` (banner/notes, decline never launches, confirm launches verified installer after snapshot, disabled zero requests, skip/remind persistence, invalid-signature localized error, settings toggles, 24 h throttle, language switch, four-language key parity). **GATE 3: exit 0, 493 passed, 0 failed** (335 + 130 + 28), up from 491.
- **Not verified:** no update UI was opened or visually checked; the real signing key and release manifest do not exist yet (Phase 4).
