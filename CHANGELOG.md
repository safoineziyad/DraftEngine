# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.2] - 2026-10-24

### Fixed
- Contract versioning: version allocation, snapshot insertion and contract mutation now run inside a transaction with an execution strategy and rollback, removing the `MAX(VersionNumber)+1` race for generate, AI chat edit and revert.
- Data: a DPAPI decryption failure no longer exposes raw `DEENC1:` ciphertext; the affected in-memory value is scrubbed and the failure is logged with entity/property names only. Added `PropertyInfo` caching for encrypted properties.
- Documents: QuestPDF font initialization moved to a thread-safe helper, document writes are atomic (temp file + replace), empty documents are rejected with a clear error, and signature lines are derived from the contract parties.
- App: startup failures no longer surface raw exception details; the contract detail page reloads version history without unobserved fire-and-forget exceptions; single-party titles no longer end with a dangling `&`.

### Added
- Contract detail: chat sends on Enter (Shift+Enter inserts a newline), the input is disabled when no API key is configured, and a warning banner links directly to Settings.
- Contracts list: a "Clear filters" action that resets search, type filter and sort.
- Settings: API key status indicator with masked preview, inline term-months validation (1–120), and Save disabled while the term is invalid.
- New contract wizard: explicit note that only Mutual NDA is currently available.

### Changed
- `IDocumentExportService` moved to `DraftEngine.Core/Interfaces`; shared document constants and file-writing helpers extracted.
- Version alignment across the app and installer (`0.2.2`).

### Verification
- `dotnet build DraftEngine.sln -c Release --no-incremental` → 0 warnings / 0 errors
- `dotnet test DraftEngine.sln -c Release` → 156/156 passing

## [0.2.1] - 2026-10-21

### Fixed
- **A1 (Data)**: Seed the local owner user (Users) via HasData and SeedDefaultUser migration so contracts can be created without FOREIGN KEY constraint failed (wizard sets UserId = 1).
- **A1 (Tests)**: Regression tests for FK-only insert and full migrated create→generate→reload flow (ContractOwnerUserTests, ContractCreationIntegrationTests).
- **A2 (App)**: ContractDetailViewModel implements INavigationAware so navigation passes OnNavigatedTo(parameter) and the detail page loads.
- **A3 (Wizard)**: New Contract wizard reads Settings.DefaultGoverningLaw and Settings.DefaultTermMonths as intake defaults (re-applied on each open/reset).
- **B1 (UI)**: ContractsListView header row placed in its own grid row to prevent overlap with contract rows.
- **B2 (UI)**: DashboardView header uses two-column grid so title/subtitle stay left and **New Contract** button stays right/aligned.

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

