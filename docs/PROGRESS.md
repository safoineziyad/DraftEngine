## Prompt status

- Prompt 1: Complete. Registry, canonical contract-type IDs, intake/placeholder alignment, schema migrations, placeholder audit.
- Prompt 2: Complete. Registry-driven intake/summary, type-aware composition, custom-clause selection, offline generation/export tests, Mutual NDA snapshot.
- Prompt 3: Complete. Clause Manager, AI consent/redaction/custom endpoint, editable contracts, duplicate and archive.
- Prompt 4: Complete. Wizard drafts, export history, AI diff preview with Accept/Reject, onboarding and About/Help.
- Prompt 5: Complete. Offline licensing behind a flag, notify-only update check, signing hooks, docs and 0.3.0 release metadata.

## Decisions

- Canonical registry IDs (`MutualNda`, `OneWayNda`, `IndependentContractorAgreement`, `ConsultingAgreement`)
  are stored in `Contract.ContractType` and `Clause.ContractType`; legacy display names/aliases
  are normalized through `ContractTypeRegistry.TryGetByDisplayName`.
- `ContractContentBuilder` (Core) owns pure title/summary/preamble/signature logic.
- AI edits are preview-first: `PreviewChatEditAsync` validates without writing;
  `ApplyChatEditPreviewAsync` snapshots and stores only after the user accepts.
- Redaction removes configured party names, addresses and e-mails before the AI call and
  restores the tokens in the reply.
- All commercial features default to off, so an upgraded install behaves exactly as before.

## Verification

- `dotnet build DraftEngine.sln -c Debug`: succeeded, 0 errors.
- `dotnet test DraftEngine.sln -c Debug`: 222 passed, 0 failed, 0 skipped.
- New coverage: diff computation, real-value redaction, preview non-destructiveness,
  accept snapshotting, onboarding flow, export history, license verification, update check.
