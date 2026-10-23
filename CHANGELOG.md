# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-10-21

### Added
- Contract version history with snapshots before AI edits/regenerations and one-click restore in Contract Detail (WP2)
- DPAPI encryption for contract text, intake answers, chat history, and version snapshots at rest (WP4) with Windows-profile migration warnings
- App version shown from csproj; accessibility names added to controls
- AI data sharing disclosure with first-chat confirmation
- IHttpClientFactory for OpenAI traffic; placeholder widening; unresolved placeholder surfacing
- Navigation guard, export path sanitization, and "open export" action
- Chat history cap (200) and placeholder validation

### Changed
- Dependency updates in .NET 8 band (SQLitePCLRaw advisory patch)
- Export/rendering compatibility adjustments

### Security
- At-rest encryption of sensitive contract data with Windows DPAPI (current user)
- Safer export paths and navigation guards

### Verification (this release)
- Safety branches `v0.2.0-backup` and `v0.2.0-verify` created/pushed before changes
- Baseline: build 0 errors/0 warnings, tests 135/135 green (expanded to 142/142 during verification)
- DPAPI: real disk-level integration tests (roundtrip, raw SQLite inspection for `DEENC1:`, legacy v0.1 plaintext compatibility, degradation) — all passed; Windows-profile warnings added to Settings and README
- Exports: real roundtrip tests for PDF (valid `%PDF-`, no leakage) and DOCX (valid OpenXML zip, clauses present) plus special characters — passed with byte-scan validation (no PdfPig dependency)
- Version history: implemented and functional; runtime app launch verified (started, ran >10s, killed cleanly), no fatal log entries, DB intact
- All merges/reverts via `git revert` only; no history rewrites; tagged/pushed after gates passed
## [0.2.1] - 2026-10-21

### Fixed
- **A1 (Data)**: Seed the local owner user (Users) via HasData and SeedDefaultUser migration so contracts can be created without FOREIGN KEY constraint failed (wizard sets UserId = 1).
- **A1 (Tests)**: Regression tests for FK-only insert and full migrated create→generate→reload flow (ContractOwnerUserTests, ContractCreationIntegrationTests).
- **A2 (App)**: ContractDetailViewModel implements INavigationAware so navigation passes OnNavigatedTo(parameter) and the detail page loads.
- **A3 (Wizard)**: New Contract wizard reads Settings.DefaultGoverningLaw and Settings.DefaultTermMonths as intake defaults (re-applied on each open/reset).
- **B1 (UI)**: ContractsListView header row placed in its own grid row to prevent overlap with contract rows.
- **B2 (UI)**: DashboardView header uses two-column grid so title/subtitle stay left and **New Contract** button stays right/aligned.

