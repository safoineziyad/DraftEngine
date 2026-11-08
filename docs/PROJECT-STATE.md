# DraftEngine 0.4.0 — Project State

## What it is

A local-first Windows desktop app (WPF, .NET 8, MVVM) for drafting and reviewing contracts.
SQLite + EF Core for storage, DPAPI for secrets and contract content, OpenXML for DOCX,
QuestPDF for PDF. AI editing and AI review are optional and never required.

## Architecture

| Project | Responsibility |
| --- | --- |
| `DraftEngine.Core` | Models, interfaces, registry, field/clause/contract services, licensing verifier, update comparison, text diff, redaction |
| `DraftEngine.Data` | `DraftEngineDbContext`, model configuration, EF migrations, seeded clause library, DPAPI interceptor |
| `DraftEngine.AI` | OpenAI-compatible chat client (custom base URL), retries, typed exceptions, free-form prompt support |
| `DraftEngine.Documents` | DOCX/PDF builders, analysis report export, tracked-change redline builder |
| `DraftEngine.Analysis` | Standalone net8.0 analysis engine: DOCX/PDF import, clause segmentation, playbooks, deterministic checks, grounded AI review, document comparison. No WPF/EF/DPAPI references. |
| `DraftEngine.App` | WPF shell, views/view models, navigation, DI, DPAPI settings, licensing/update, backups, analysis persistence |
| `DraftEngine.Tests` | Existing Core/Data/Documents/VM tests |
| `DraftEngine.App.Tests` | Startup DI/navigation, backup, analysis persistence and search tests |
| `DraftEngine.Analysis.Tests` | 10+ synthetic contracts, precision/recall, grounding, citation filtering, exports |

Key design points:

- `ContractTypeRegistry` is the single source of truth for the four contract types.
- Page view models are singletons and reload on navigation.
- Every destructive text change snapshots first.
- DPAPI encrypts contract text, intake, chat, versions, analyses and templates; backups are
  AES-256-GCM with PBKDF2 (600k iterations).
- Analysis AI findings must quote the source verbatim or they are discarded; citation-like
  content is stripped from AI explanations/fixes.

## Features in 0.4.0

- All 0.3.0 drafting features (four types, wizard, clause manager, drafts, archive, AI edit
  preview, export history, onboarding, About, optional licensing/update check).
- Analyze workspace: import, progress, findings by severity, click-to-highlight quote,
  deterministic playbooks for the four types, AI toggle (default off, consent-gated), covered
  by encrypted persistence and archive.
- Review report export (DOCX/PDF) with the mandatory disclaimer, and tracked-change DOCX
  redlines.
- Portable encrypted backup/restore with pre-import restore point, wrong-password and tamper
  detection, and no plaintext content.
- Plain JSON and CSV data export with explicit unencrypted warnings.
- Contract search covers body text, title, client and governing law.
- Content-free diagnostics copy in About; keyboard shortcuts Ctrl+N (new), Ctrl+F (contracts),
  Ctrl+K (analyze).
- Startup safety net: every page/view model/service resolves and every navigation key has a
  view.

## Database

v0.4.0 adds `ContractAnalyses` (encrypted file name + result JSON). Upgrade by starting the
app; existing data is untouched. The analysis table is created by the
`AddContractAnalyses` migration.

## Constraints preserved

- Deterministic review and drafting work fully offline.
- No telemetry; network only for user-triggered AI calls and the optional update check.
- No generated or displayed legal citations.
- Machine-translated legal text would never be presented as vetted (see i18n work).
- DPAPI at rest, snapshots before destructive changes, singleton page VMs reload on navigation.

## Known gaps / manual work

- Reusable contract templates, letterhead/branding in exports, and a playbook editor UI are
  specified for 0.4.0 but not implemented in this build.
- The Analyze page can copy a suggested fix but cannot yet insert a clause from the library.
- Redline generation is implemented and tested, but there is no side-by-side "compare two
  versions" page yet.
- OCR for scanned PDFs is detected and reported, not supported.
- Seeded clause wording and playbooks need review by a qualified attorney.
- WPF layout and click-through are not covered by automated tests.
