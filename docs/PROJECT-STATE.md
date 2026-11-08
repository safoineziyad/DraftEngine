# DraftEngine 0.3.0 — Project State

## What it is

A local-first Windows desktop app (WPF, .NET 8, MVVM) for drafting contracts from templates.
SQLite + EF Core for storage, DPAPI for secrets and contract content, OpenXML for DOCX,
QuestPDF for PDF. AI editing is optional and never required for generation.

## Architecture

| Project | Responsibility |
| --- | --- |
| `DraftEngine.Core` | Models, interfaces, registry, field/clause/contract services, licensing verifier, update comparison, text diff, redaction |
| `DraftEngine.Data` | `DraftEngineDbContext`, model configuration, EF migrations, seeded clause library |
| `DraftEngine.AI` | OpenAI-compatible chat client (custom base URL supported), retries, typed exceptions |
| `DraftEngine.Documents` | DOCX and PDF builders (depend on the `"N. Title"` heading convention) |
| `DraftEngine.App` | WPF shell, views/view models, navigation, DI, DPAPI settings, licensing/update services |
| `DraftEngine.Tests` | xUnit suite; AI is always faked, no network calls |

Key design points:

- `ContractTypeRegistry` is the single source of truth for the four contract types and their
  canonical ids: `MutualNda`, `OneWayNda`, `IndependentContractorAgreement`, `ConsultingAgreement`.
  Legacy display names are normalized through `TryGetByDisplayName`.
- `ContractContentBuilder` owns pure title/summary/preamble/signature logic so the wizard and the
  generation pipeline cannot drift apart.
- Page view models are singletons and reload state on every navigation.
- Every destructive text change (AI edit accept, regeneration, revert) stages a version snapshot
  in the same transaction.
- Sensitive data is encrypted at rest (contract text, intake, chat, settings, API key, wizard
  drafts, license file) with Windows DPAPI for the current user.

## Features in 0.3.0

- Four contract types with type-aware intake, preamble, recitals, role labels and signature blocks.
- Deterministic offline generation and DOCX/PDF export.
- Editable contracts after creation (metadata, intake, clause selection) with snapshot + regenerate.
- Clause Manager: create, edit, duplicate, archive user clauses; seeded clauses are read-only.
- Wizard draft persistence (encrypted, survives navigation and restart).
- Contract archive/restore/permanent delete, duplicate ("start from this"), export history.
- AI edit preview: line diff with Accept/Reject; acceptance snapshots first; consent required.
- AI privacy: configurable OpenAI-compatible endpoint, party-name/address redaction before send,
  consent revocation in Settings, no request/response logging.
- Optional onboarding (3 screens), About & Help page with version, folders, privacy, update check.
- Offline signed licensing behind `Features:LicensingEnabled=false`.
- Notify-only update check behind `Features:UpdateCheckEnabled=false`.
- Optional Authenticode signing in the installer build.

## Database

Upgrade an existing database by starting the app or running `dotnet ef database update`.
The v0.3.0 migrations:

- Add `Clauses.IsUserDefined` / `Clauses.IsArchived`, `Contracts.IsArchived` / `ArchivedAt`,
  and the `WizardDrafts` table.
- Canonicalize contract-type strings and align clause placeholder keys.
- Seed clauses 13–42 for the additional contract types.

`DraftEngine.Tests/Data/MigrationClauseSeedTests.cs` builds a database at the previous migration
and applies the new ones, asserting the columns, table and clauses exist.

## Constraints preserved

- Template generation works fully offline; no key or network needed.
- No telemetry. Network only for user-triggered AI calls, the configured AI endpoint, and the
  optional update check.
- `CurrentUserId = 1` remains the single-user assumption.
- The `"N. Title"` heading convention is unchanged.

## Known gaps / manual work

- The seeded clause wording is a drafting baseline and needs review by a qualified attorney.
- WPF visual layout and full click-through are verified manually; automated tests cover
  Core/Data/Documents/ViewModels but not rendered pixels.
- A real code-signing certificate must be purchased; the build signs only when
  `DRAFTENGINE_SIGN_CERT` is provided.
- Payments/subscription fulfilment are intentionally out of scope; licensing is offline keys only.
- AI features require the user's own API key or a local OpenAI-compatible server.
