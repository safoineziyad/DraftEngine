# DraftEngine 0.4.0 — Progress

## Stage status

| Stage | Status | Notes |
|---|---|---|
| 0 — Vision + startup safety net | Complete | `docs/VISION.md`; `DraftEngine.App.Tests` resolves all page VMs/services, checks every navigation key has a DataTemplate, and loads every page against empty and seeded SQLite DBs. |
| 1 — Data safety + polish | In progress | Backup/restore, data export, reusable templates, letterhead, search/shortcuts/diagnostics, theme. |
| 2 — Analysis engine | Not started | UI-agnostic `DraftEngine.Analysis`. |
| 3 — Analyze UI + exports | Not started | |
| 4 — Web readiness | Not started | |
| 5 — Audit + release candidate | Not started | |

## Test counts

- Start of v0.4.0: 222 (`DraftEngine.Tests`).
- After Stage 0: 254 total — 222 Core/Data/Documents/App-VM tests + 32 App startup tests.

## Decisions

- The App test project targets `net8.0-windows` with `UseWPF`, but the startup tests avoid
  instantiating visual elements: they parse the linked `App.xaml` markup for DataTemplates and
  resolve the real DI graph against a temporary SQLite file. This keeps them fast and headless.
- `NavigationService.Pages` is exposed read-only so the startup test can assert that every key
  maps to a view template; the registry remains private otherwise.
- Stage 1's theme toggle is deferred to the dedicated i18n/theme work so there is one theme
  implementation, not two.

## Prior release

- v0.3.0 complete: 222 tests passing, multi-type generation, clause manager, drafts, archive,
  AI diff/redaction, onboarding, About, licensing behind a flag.
