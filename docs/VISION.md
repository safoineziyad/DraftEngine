# DraftEngine Vision

## Phase 1 — Draft (shipped)

A local-first Windows desktop app that turns a short intake into a complete first draft of four
common contract types, offline, with optional AI editing. Everything is stored on the user's
machine and encrypted with Windows DPAPI. No account, no telemetry.

## Phase 2A — Analyze in the desktop app (current)

Lawyers already produce drafts. The next problem is checking someone else's draft before signing
it. Phase 2A adds an **Analyze** workspace inside the existing desktop app:

- Import a DOCX or PDF and segment it into clauses.
- Run deterministic, offline checks against a per-contract-type playbook: missing clauses,
  one-sided terms, blank fields, unresolved placeholders, missing governing law.
- Optionally run an AI review through the existing consent, redaction and configurable-endpoint
  pipeline. AI findings are grounded: every quote must appear verbatim in the document.
- Review findings, insert suggested library clauses, mark findings resolved/ignored, export a
  review report and a redline.

Phase 2A deliberately excludes legal citations: the product does not generate case law,
statutes or section numbers, because a wrong citation is worse than no citation. Every analysis
surface carries "Not legal advice. Review by a licensed attorney required."

Everything in Phase 2A remains useful with no API key and no network connection.

## Phase 2B — Web version and shared review link (not started)

Only after real users exist. Criteria to start:

1. At least 10 lawyers actively using the desktop app, measured by opt-in feedback or direct
   interviews (no telemetry in the product).
2. At least some of them paying for the desktop app, proving willingness to pay.
3. A validated request for collaboration: sharing a review with a client or colleague is the
   most likely trigger.

Scope when it starts: a hosted ASP.NET Core API, PostgreSQL with per-tenant isolation, a Next.js
front end, managed auth, encrypted object storage, and a read-only shared review link. The
analysis engine and generation logic are written with no UI or OS coupling so they can move
behind an API without a rewrite. See `docs/WEB-ARCHITECTURE.md`.

## Phase 3 — Dispute resolution (parked)

Parked, not planned. Reasons:

- Strong incumbents with network effects and integrations.
- The EU AI Act treats many dispute-resolution uses as high-risk, bringing conformity
  assessment, logging and human-oversight obligations that a solo/small-team product cannot
  carry.
- Citation hallucination and jurisdictional accuracy make the liability profile unacceptable
  for a small vendor.

Revisit only with a domain co-founder, a legal-compliance budget, and a narrow jurisdiction.

## Product principles

- Offline by default; AI optional and consent-gated.
- No telemetry, ever.
- Local data protection with DPAPI at rest; portable backups are opt-in and password-encrypted.
- Never present machine-translated legal text as vetted; mark it for legal review.
- Never fabricate legal authority.
