# DraftEngine v0.4.0 — Stage 0 Status Audit

Audit date: 2026-11-08  
Branch: `feature/i18n-theme`  
Scope: documentation and code inspection only; no feature code changed.

## Baseline

- `git status`: clean before this documentation batch; branch was at `8b7bc40` and tracked `origin/feature/i18n-theme`.
- Last 20 commits inspected. The latest feature commits are the Contracts-list localization migration (`2a0d29e`) and backup/edit-service warning fixes (`8b7bc40`).
- `dotnet test DraftEngine.sln -c Release --no-restore`: **313 passed, 0 failed** — 224 `DraftEngine.Tests`, 61 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`.
- Release build succeeded with six warnings: three production `CS8625` nullable warnings in `DraftEngine.Core/Services/UpdateCheckService.cs`; test warnings `CS8602` and `CS1998` in Analysis.Tests and one `CS8625` in `DraftEngine.Tests/Commercial/UpdateCheckServiceTests.cs`.
- Resources: **207 keys per language** in `Strings.resx`, `Strings.fr.resx`, `Strings.ar.resx`, and `Strings.es.resx` (828 key/value entries total). Resource parity is covered by `LocalizationResourceTests.AllLanguages_HaveTheSameKeys`.
- Current XAML literal-guard candidates: **158** in the seven views/windows still listed as pending in `LocalizationResourceTests.PendingMigration`: NewContract (24), Contract detail (44), Clause Manager (30), Analyze (24), About (21), Archived Contracts (6), License Activation (9). This is the guard's XAML attribute/inline-text scan only; it is **not** a count of remaining C# literals or a complete semantic count of user-facing text.
- A hex/RGB-style scan finds **7 color-literal matches** in views: Contract detail (5), Contracts list (1), Dashboard (1); `NewContractView.xaml` also contains the named color `White` in its type-card style. `docs/color-baseline.txt` is absent, so no shrinking color ratchet is in place.

## Requested item-by-item status

| Item | Status | Evidence |
|---|---|---|
| Live language switching | **Done (logic; not visually verified)** | `DraftEngine.App/Services/Localization/LocalizationExtension.cs`; `DraftEngine.App.Tests/LocalizationTests.cs`, including `SetLanguage_RaisesEvent_AndUpdatesLiveSource`. |
| RTL behavior | **Partial** | `LocalizationService.GetFlowDirection`, `App.xaml` localized window style and European digit substitution; `LocalizationTests.Arabic_IsRightToLeft_AndUsesEuropeanDigits`. Window coverage is limited and actual Arabic layout is **not visually verified**. |
| Every screen migrated to localization keys | **Partial** | `LocalizationResourceTests.PendingMigration` lists seven remaining XAML files; literal scan records 158 candidates there. Four resource files have 207 keys each and parity is tested. C# user-facing literals have not been comprehensively counted. |
| Light/Dark/Follow Windows on every screen | **Partial** | `DraftEngine.App/Themes/ThemeService.cs`, `ThemeMode.cs`, `Light.xaml`, `Dark.xaml`, and persisted `AppSettings.Theme` implement switching/follow-Windows logic. The application initializes theme before showing the main window (see `App.xaml.cs`). Color literals remain in views; no High Contrast handling or WCAG contrast test was found. All popups/windows are not proven themed; not visually verified. |
| Contract language, `Clause.Language` migration, Arabic-correct DOCX/PDF | **Not done** | `DraftEngine.Core/Models/Clause.cs` has no `Language` property; no corresponding migration exists in `DraftEngine.Data/Migrations`. No contract-language wizard selection or Arabic DOCX/PDF bidi/font implementation was found. |
| Save as reusable contract template | **Not done** | Wizard drafts persist transient wizard state (`NewContractViewModel.Drafts.cs`); no reusable contract-template save flow or card exists. `docs/PROJECT-STATE.md` records reusable templates as a gap. |
| Letterhead / branding | **Not done** | No branding settings surface or export branding implementation found in App views/view models; `docs/PROJECT-STATE.md` records this as a gap. |
| Analyze: finding highlight | **Done (logic; not visually verified)** | `AnalyzeViewModel.OnSelectedFindingChanged` computes quote offsets; `AnalyzeView.xaml.cs` applies the highlight when selection/offset changes. |
| Analyze: insert suggested library clause | **Not done** | `AnalyzeViewModel` exposes copy-suggested-fix, but no library insertion command/control exists in `AnalyzeView.xaml` / `AnalyzeViewModel.cs`. |
| Analyze: mark resolved/ignored | **Not done** | No finding resolution/ignore state or control found in `AnalyzeViewModel` or `AnalyzeView.xaml`. |
| Analyze: AI toggle default OFF and consent-gated | **Done (code path)** | `AnalyzeViewModel.UseAi` is a default-false `bool`; `CanUseAi` checks `AiDataConsentAccepted`, the checkbox is disabled without consent, and analysis receives `UseAi && CanUseAi`. Evidence: `AnalyzeViewModel.cs`, `AnalyzeView.xaml`. UI behavior is not visually verified. |
| Analyze: redline/report export wired | **Partial** | Report DOCX/PDF buttons bind to `ExportReportCommand`; `AnalyzeViewModel.ExportReportAsync` calls `AnalysisReportBuilder`. A tracked-change builder exists in Documents, but no redline export command/button is wired in the Analyze UI. |
| Analyze: scanned-PDF message | **Done (text present)** | `AnalyzeView.xaml` says scanned PDFs need OCR and that OCR is not supported yet. |
| Playbook editor | **Not done** | `DraftEngine.Analysis/PlaybookCatalog.cs` supplies built-in playbooks; no playbook editor UI exists in App. |
| Side-by-side compare page | **Not done** | `DraftEngine.Analysis/DocumentComparison.cs` provides comparison logic; no compare page or navigation entry exists. |
| `VISION.md` and `WEB-ARCHITECTURE.md` | **Done (documents present; not implementation verification)** | `docs/VISION.md` describes Phase 1, current Phase 2A, web Phase 2B criteria (10 active lawyers plus validation), and parked Phase 3 risks. `docs/WEB-ARCHITECTURE.md` describes a future web architecture and states it is not started. |
| Backup EF1002 SQL fix | **Done** | `PortableBackupService.ResetSequencesAsync` uses seven hard-coded SQL statements and parameterized table-name values. Regression: `PortableBackupTests.EncryptedBackupWithUnexpectedSqlIdentifiers_IsRejectedBeforeDatabaseChanges`. Fix commit: `8b7bc40`. |
| Backup swallowed-exception / failed-restore handling | **Done** | Restore exceptions propagate; database and settings restore steps execute before transaction commit. Regressions: `PortableBackupTests.FailedRestoreStep_RollsBackDatabaseChanges` and `FailedSettingsRestore_RollsBackDatabaseChanges`. Fix commit: `8b7bc40`. |
| `ContractEditService` nullable returns | **Done** | `IContractEditService` explicitly declares nullable duplicate/update results; callers handle missing contracts. Regression tests: `ContractEditServiceTests.DuplicateContractAsync_ReturnsNullWhenSourceContractIsMissing` and `UpdateContractAsync_ReturnsNullWhenContractIsMissing`. Fix commit: `8b7bc40`. |

## Documentation discrepancies

`docs/GAP-REPORT.md` and `docs/I18N-INVENTORY.md` predate the current localization work: they describe missing localization infrastructure and 15/26 keys, while current code has live localization and 207 keys per language. Their old findings are not current status; this audit is the current evidence snapshot. The audit does not claim that all gaps have been fixed.

## Release-candidate caveats

- Release build and tests passed, but the warnings above remain.
- No WPF page/window has been visually verified in this audit.
- No contract-language migration or RTL exports, full theme audit, automated binding trace harness, or complete C# literal inventory was performed.
- Stage 1 is still substantial; this is a status snapshot, not a release-candidate sign-off.

## Post-audit progress (2026-11-08)

- The New Contract wizard is now migrated with 66 EN/FR/AR/ES keys. `NewContractView.xaml` was removed from the pending literal guard. The baseline fell from 158 to **134** candidates across the six remaining pending screens.
- Resource files now have **273 keys per language** (1,092 key/value entries total), and all four key sets remain equal under `LocalizationResourceTests.AllLanguages_HaveTheSameKeys`.
- Release tests now pass at **318** (225 core, 65 App.Tests, 28 Analysis.Tests). Wizard-specific coverage includes dynamic label language refresh, translated type/field labels, invariant integer validation, and core summary labels returned as stable field keys.
- The new screen has **not been visually verified**. Translation quality still needs native-speaker and legal review where relevant.

## Contract-detail localization update (2026-11-08)

- `ContractDetailView.xaml` and its directly related view-model output are migrated to localization keys; all four resource files contain **373 keys** with parity enforced by `LocalizationResourceTests.AllLanguages_HaveTheSameKeys`.
- The Contract-detail view was removed from the pending literal guard. The remaining five views/windows contain **90** candidates, measured by `LocalizationResourceTests.PendingLiteralCount_DoesNotExceedBaseline` against `docs/i18n-baseline.txt`.
- `ContractDetailLocalizationTests.ContractDetailAndHistoryRows_RerenderAfterLanguageChange` exercises contract type/status, history rows, and load-error language refresh. Release build succeeded in an isolated output directory because the running DraftEngine process locked the normal output. All three Release test assemblies passed individually: **326** total (225 core, 73 App.Tests, 28 Analysis.Tests).
- Contract detail is **Done in code, not visually verified**. French/Arabic/Spanish copy needs human review, especially long French strings and Arabic RTL layout.
- This working branch also retains `0d94c21` (auto-update check/banner and silent-installer work). No merge or tag was made.
