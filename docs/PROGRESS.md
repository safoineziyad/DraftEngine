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

NEXT STEP: Step 2a — implement live localization (`LocalizationSource` with `this[string]`, `{loc:T}` binding) and a test proving a language switch updates a bound value.

### Test counts

- v0.4.0 start: 222.
- End: 291 (222 `DraftEngine.Tests`, 41 `DraftEngine.App.Tests`, 28 `DraftEngine.Analysis.Tests`).

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
