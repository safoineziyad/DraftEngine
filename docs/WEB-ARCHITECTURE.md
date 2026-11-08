# Web Architecture (Phase 2B, not started)

This document describes the target architecture for the future web version. No web code is
included in this repository yet; Phase 2B starts only when the criteria in `docs/VISION.md`
are met.

## Shape

A modular monolith, not microservices:

```
Next.js web app ──HTTPS──> ASP.NET Core API ──> PostgreSQL (pgvector)
                                   │
                                   ├── S3-compatible encrypted object storage
                                   ├── Hangfire background jobs
                                   └── AI provider abstraction (zero-retention)
```

## Backend

- **ASP.NET Core** (.NET 8) with minimal APIs grouped by module: Identity, Contracts,
  Analysis, Documents, Billing, Sharing.
- **PostgreSQL** with the `pgvector` extension for clause/finding similarity search.
- **Tenancy**: every row carries `tenant_id`; PostgreSQL row-level security policies enforce
  isolation even if application code forgets a filter. Each tenant gets a per-tenant data
  encryption key (envelope encryption: data key wrapped by a KMS master key).
- **Auth**: managed identity (WorkOS, Clerk or Auth0) with OIDC; the API validates tokens and
  maps the subject to a local user. No passwords stored.
- **Storage**: S3-compatible object storage, server-side encryption with per-tenant keys,
  short-lived pre-signed URLs. Uploaded originals and derived reports are separate objects.
- **Jobs**: Hangfire for import, OCR (future), report generation and exports; jobs are
  idempotent and tenant-scoped.
- **AI**: provider-abstracted (`IAiProvider`) with zero-retention terms, per-tenant quota,
  redaction before send, and the same grounding rule as the desktop app: a finding's quote
  must appear verbatim in the source or it is discarded. Provider credentials live in a
  secret manager, never in the database or client.
- **Billing**: Paddle or Stripe with webhook-driven entitlements; no card data touches the
  API.
- **Observability**: OpenTelemetry traces and metrics with content logging disabled at the
  exporter. Logs contain ids, durations and status codes only.

## Frontend

- Next.js (App Router) with server components for read-heavy pages.
- Same review UX as the desktop Analyze page: import, findings by severity, click-to-quote,
  redline download, report export.
- Shared review links are read-only, expiring, and revocable; they never expose raw API
  endpoints or storage URLs.

## Reuse from the desktop codebase

`DraftEngine.Analysis` has no WPF, EF Core or DPAPI dependencies and is the intended shared
engine. Generation is split between pure builders (`ContractContentBuilder`, registry) and
EF-backed services; the refactor plan below makes the pure half shareable.

## Coupling audit (current)

| Area | Coupling | Plan |
|---|---|---|
| `DraftEngine.Analysis` | None to UI/EF/DPAPI. Only OpenXML + PdfPig. | Move as-is behind the API. |
| `DraftEngine.Core` models and `ContractContentBuilder`, `ContractTypeRegistry`, `ContractTextDiff` | Project references `Microsoft.EntityFrameworkCore` for the service implementations in the same assembly. | Split `DraftEngine.Domain` (models, registry, pure builders, diff) from `DraftEngine.Application` (EF services). Domain becomes dependency-free. |
| `DraftEngine.Documents` | References Core (for models), so it transitively drags EF Core even though the builders use none. | After the split, reference Domain only. |
| `DraftEngine.Data` | SQLite-specific. | Replace with PostgreSQL + EF provider; keep `IDatabaseContextFactory` boundary. |
| DPAPI, file paths, `ApplicationData` | App-only (`DpapiSecretProtector`, settings, backup). | Replace with KMS + object storage; keep `ISecretProtector`-style abstraction server-side. |
| `IAiReviewer`, `IContractAiService` | Interfaces are clean; desktop implementations are app-local. | Implement an HTTP provider server-side. |

Trivial fixes applied now: the Analysis library is a standalone net8.0 project with no
project references, and its tests run without a Windows desktop or database.
