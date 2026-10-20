# Changelog

All notable changes to DraftEngine are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-20

Initial release.

### Added

- **Application shell**: WPF/.NET 8 desktop app using MVVM (CommunityToolkit.Mvvm), dependency injection (Microsoft.Extensions.Hosting), and Serilog rolling file logs in `%APPDATA%\DraftEngine\logs`.
- **Dashboard**: contract counts and a list of recently updated contracts.
- **New Contract wizard** with four steps (Type → Details → Clauses → Review). **Mutual NDA** is the live contract type; One-Way NDA, Independent Contractor Agreement and Consulting Agreement are listed as *Coming soon*.
- **Intake form** with required fields (parties and addresses, client name/email, effective date, governing law, term in months, purpose), pre-filled from Settings defaults.
- **Clause library**: 12 Mutual NDA clauses seeded into SQLite on first run, grouped by category, with live previews and per-clause selection (core clauses pre-selected, some optional).
- **Contract generation pipeline**: deterministic, template-first assembly via `{{Placeholder}}` substitution (`FieldService`); works with no OpenAI API key configured.
- **Contract detail view**: read-only assembled document plus **chat-based editing** with natural-language instructions (OpenAI API key required; chat history persisted locally).
- **Export**: DOCX (`DocumentFormat.OpenXml`) and PDF (QuestPDF) from the contract detail page, with export history recorded per contract.
- **Contracts list**: search by title or client, filter by contract type, sort options, status and dates.
- **Settings**: OpenAI API key entry with **Test Connection** button, model selection (`gpt-4o-mini` default, `gpt-4o`), and contract defaults (governing law, term). The API key is stored **DPAPI-encrypted for the current Windows user** in `%APPDATA%\DraftEngine\settings.json` (encrypted on disk, never logged); an `OPENAI_API_KEY` environment variable is honored as a fallback and `appsettings.json` ships with an empty key.
- **Storage**: SQLite via EF Core with automatic migration on first start to `%APPDATA%\DraftEngine\draftengine.db`.
- **Tests**: xUnit suite (46+ tests as of this writing) covering services, generation, export and AI plumbing; all AI calls mocked, no network required — run `dotnet test DraftEngine.sln`.
- **Installer**: Inno Setup 6 script and PowerShell build script (`installer\Build-Installer.ps1`) producing a per-user, no-UAC setup at `installer\Output\DraftEngine-Setup-1.0.0.exe` (see `installer/README.md`).
