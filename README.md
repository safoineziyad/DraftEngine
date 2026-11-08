# DraftEngine

DraftEngine is a native Windows desktop application for drafting contracts, aimed at solo attorneys and small law firms. Answer a short intake form, pick your clauses, and generate a finished agreement you can review and export to DOCX or PDF. Contracts, settings and logs are stored locally under `%APPDATA%\DraftEngine` — no account required.

## Features

- **Four-step drafting wizard**: contract type → details (intake form) → clauses → review & generate.
- **Resumable wizard drafts**: an in-progress draft is encrypted and restored after navigation or a restart.
- **Clause Manager**: create, edit, duplicate and archive your own clauses alongside the read-only seeded library.
- **Multiple contract types**: Mutual NDA, Non-Disclosure Agreement (One-Way), Independent Contractor Agreement, Consulting Agreement.
- **Clause library**: Seeded clauses per contract type, grouped by category with live previews; core clauses are pre-selected and some are optional.
- **Deterministic, template-first generation**: `{{Placeholder}}` substitution from your intake answers (see `DraftEngine.Core/Services/FieldService.cs`). Works fully offline, **no API key required**.
- **Contract detail view**: edit metadata, intake answers and clause selection after creation (with a version snapshot before every regeneration), then read and export the assembled contract.
- **AI edit preview**: every proposed AI edit is shown as a line diff and must be Accepted or Rejected; the previous text is snapshotted first. Requires an OpenAI API key.
- **Export to DOCX and PDF** from the contract detail page, with per-contract export history.
- **Analyze**: import a DOCX or text-based PDF, segment it into clauses, run an offline playbook review (missing clauses, risky terms, blanks, placeholders), optionally add a consent-gated AI review with verbatim-quote grounding, then export a review report or a tracked-changes redline. Every result says "Not legal advice. Review by a licensed attorney required."
- **Portable backups**: password-encrypted AES-256-GCM export/restore of contracts, intake, clauses, versions, chat and settings, plus plain JSON/CSV "export my data".
- **Archive and duplicate**: archive contracts into a separate view (restore or permanently delete from there) and start a copy from any existing contract.
- **Contracts list** with search by title/client, contract-type filter and sorting.
- **Dashboard** with contract counts and recently updated contracts.
- **Settings**: OpenAI key and model, a custom OpenAI-compatible endpoint (Azure OpenAI, gateways, Ollama/LM Studio), party-name redaction for AI requests, AI consent revocation, plus defaults that pre-fill the new contract form (governing law, term in months).
- **Onboarding and About**: optional three-screen first-run guide, plus an About & Help page with version, log/data folder, privacy summary, notify-only update check and offline license status.
- **Local logging**: rolling Serilog files in `%APPDATA%\DraftEngine\logs`.

## Tech stack

- C# / .NET 8 (`net8.0-windows`), WPF with CommunityToolkit.Mvvm (MVVM)
- Entity Framework Core 8 + SQLite
- OpenAI API (default model `gpt-4o-mini`) — used only for chat editing and the connection test
- DocumentFormat.OpenXml (DOCX), QuestPDF (PDF)
- Serilog, Microsoft.Extensions.DependencyInjection / Hosting
- xUnit

## Getting started

### Prerequisites

- **Windows 10 1809 or later** (x64/ARM64). The app is WPF and targets `net8.0-windows`, so it builds and runs on Windows only.
- **.NET 8 SDK** — <https://dotnet.microsoft.com/download/dotnet/8.0>. The repository pins SDK `8.0.100` (`rollForward: latestFeature`) in `global.json`.

### Build

```powershell
dotnet build DraftEngine.sln
```

### Run

```powershell
dotnet run --project DraftEngine.App
```

### First run

On first launch, DraftEngine creates `%APPDATA%\DraftEngine\` if needed, applies its pending EF Core migrations to create `draftengine.db`, and seeds clauses for all supported contract types. Logs are written to `%APPDATA%\DraftEngine\logs\`. No configuration is required to draft a contract.

## Settings & OpenAI key

Contract **generation does not need an API key** — it is deterministic template substitution and works offline. An OpenAI API key is required only for:

- **Chat editing** on the contract detail page (natural-language edits of the generated text; the contract text is sent to OpenAI for this)
- The **Test Connection** button in Settings

Enter the key under **Settings → OpenAI → API key**, then **Save**. Key resolution order:

1. The key saved in Settings (takes effect immediately, no restart needed)
2. The `OPENAI_API_KEY` environment variable
3. `OpenAI:ApiKey` in `DraftEngine.App\appsettings.json` — ships empty on purpose; never commit a real key to this file

**Data safety:** Contract text, intake answers, chat history and version snapshots are encrypted at rest with Windows DPAPI using your Windows user profile. If you migrate to a new Windows profile, reset your Windows password, or copy the database to another account, encrypted contract data will be unreadable. Export important contracts to DOCX/PDF before changing Windows users. The OpenAI API key is also encrypted with Windows DPAPI scoped to the current Windows user and stored in encrypted form only. Contract text is stored locally in the SQLite database under `%APPDATA%\DraftEngine\`. Network calls are only made to OpenAI when you explicitly use AI features (chat edits/connection test), and only with your own API key.

## Tests

```powershell
dotnet test DraftEngine.sln
```

xUnit tests (291 as of v0.4.0 — run the command for the current count). All AI-dependent tests are mocked: no network calls and no API key are needed.

## Installer

A per-user Windows installer (Inno Setup 6, no admin/UAC prompt) is built with one command from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
```

See [installer/README.md](installer/README.md) for prerequisites, options, and the output location (`installer\Output\DraftEngine-Setup-<version>.exe`). Authenticode signing is optional: set `DRAFTENGINE_SIGN_CERT` (and `DRAFTENGINE_SIGN_PASSWORD`) before building to sign the application binary, installer and uninstaller; without them the build proceeds unsigned. See [docs/RELEASE.md](docs/RELEASE.md).

## Repository structure

| Project | Responsibility |
| --- | --- |
| `DraftEngine.App` | WPF application: views, view models, navigation, DI wiring, DPAPI-protected settings |
| `DraftEngine.Core` | Domain models, interfaces and core services (fields, contracts, clauses, chat, export) |
| `DraftEngine.Data` | EF Core + SQLite: DbContext, migrations, seeded clause library |
| `DraftEngine.AI` | OpenAI client used for chat editing and the connection test |
| `DraftEngine.Documents` | DOCX (Open XML) and PDF (QuestPDF) document builders, report export, tracked-change redlines |
| `DraftEngine.Analysis` | UI-agnostic document import, clause segmentation, playbooks, deterministic + grounded AI review, comparison |
| `DraftEngine.Tests` | Core/data/documents view-model tests (mocked, offline) |
| `DraftEngine.App.Tests` | Startup DI/navigation and WPF App service tests |
| `DraftEngine.Analysis.Tests` | Importers, analysis precision/recall, grounding and export tests |

## License

[MIT](LICENSE)
