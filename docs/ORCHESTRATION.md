# Orchestration Checkpoint

## Waves
- Wave 1: A1 Registry, A2 Data (foundation)
- Wave 2: A3 Fields, A4 Composer, A5 Wizard, A6 Clauses, A7 AI-Privacy, A8 Editing, A9 Drafts+History, A10 AI-Diff, A11 Commercial, A12 Onboarding
- Wave 3: A13 Integration, A14 Auditor+Docs

## Status
| Agent | Status | Notes |
|---|---|---|
| A1 | merged | Registry in Core/ContractTypes |
| A2 | merged | Migrations/schema/seeds |
| A3 | merged | FieldService alignment |
| A4 | merged | Preamble/signature type-aware |
| A5 | merged | Wizard refactor |
| A6 | merged | Clause Manager |
| A7 | merged | Consent, redaction, custom endpoint, privacy settings |
| A8 | merged | Editable contracts, duplicate, archive |
| A9 | merged | Wizard drafts, export history |
| A10 | merged | Diff preview with Accept/Reject |
| A11 | merged | Licensing, update check, signing hooks (off by default) |
| A12 | merged | Onboarding + About/Help |
| A13 | merged | DI/navigation integration; solution builds; 222 tests pass |
| A14 | merged | Docs, version 0.3.0, audit notes |

## Decisions
- Started from v0.3.0-mega; baseline branch `wip/multi-type-baseline`.
- Completed the remaining v0.3.0 work directly (no sub-agents in this environment), keeping
  the shared-file ownership discipline.
- AI edits are preview-first: `ContractService.PreviewChatEditAsync` validates without writing,
  `ApplyChatEditPreviewAsync` snapshots and stores only after the user accepts.
- Redaction hides configured party names/addresses/e-mails before the AI call and restores the
  tokens in the reply; it is enabled from Settings and synced at startup.

## Verification
- `dotnet build DraftEngine.sln -c Debug`: succeeded.
- `dotnet test DraftEngine.sln -c Debug`: 222 passed, 0 failed.
- New tests: line diff, redaction of real values, AI preview non-destructiveness and accept
  snapshotting, onboarding flow.
