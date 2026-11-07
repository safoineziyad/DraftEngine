## Prompt 1..5 status
- Prompt 1: Complete. Registry, canonical contract-type IDs, intake/placeholder alignment, schema migrations, and placeholder audit are in place.
- Prompt 2: Automated implementation and verification complete. Registry-driven intake/summary, type-aware composition, custom-clause selection, offline generation/export tests, and a Mutual NDA snapshot are in place. Manual WPF click-through remains pending.
- Prompt 3: Not started.
- Prompt 4: Not started.
- Prompt 5: Not started.

## Decisions
- Task 0 checkpoint is `e0d3d8f`; `wip/multi-type-baseline` and `feature/v0.3.0` point to it.
- The pre-Prompt 1 test baseline is 168 passed, 0 failed on .NET SDK 8.0.426.
- Canonical registry IDs (`MutualNda`, `OneWayNda`, `IndependentContractorAgreement`, `ConsultingAgreement`) are stored in `Contract.ContractType` and `Clause.ContractType`. Legacy display names/aliases are normalized through `ContractTypeRegistry.TryGetByDisplayName`, so existing rows keep working without a data reset.
- Party placeholder keys are type-specific: Mutual/One-Way use `Party1`/`Party2`; Independent Contractor uses `ContractorName`/`ClientName`; Consulting uses `ConsultantName`/`ClientName`. Address and email keys follow the same pattern.
- `ContractContentBuilder` (Core) owns the pure title/summary/preamble/signature logic so the wizard and the generation pipeline cannot drift apart and the review step can be unit-tested without WPF.
- The wizard builds type cards, intake fields, clause lists, titles, summaries and parties payloads from the registry. No contract-type string literals remain in the wizard flow.
- User-created clauses (`IsUserDefined`, not archived) are returned alongside seeded system clauses by `ClauseService`, so they appear in step 3.
- DOCX/PDF builders render their own role-labelled signature block. `ContractTextParser.ParseBody` strips the assembled registry signature so it is not duplicated in exports.
- Title pages resolve canonical IDs to display names via `DocumentDefaults.FormatContractType`.

## Migrations
- `20261106211644_Task3_WizardDraftAndCanonicalContractTypes`: adds the wizard draft table and canonicalizes contract-type values.
- `20261106211906_AlignClauseData`: re-adds `Clauses.IsUserDefined` after the earlier `Phase4_SoftDelete` SQLite table rebuild, canonicalizes clause type values, migrates contractor/consulting clause bodies to role-specific placeholder keys, and backfills `IsUserDefined = 0` for seeded clauses. The already-applied `Phase4_SoftDelete` migration was deliberately not edited.
- Upgrade path: run `dotnet ef database update` (or start the app) against an existing database; both migrations are idempotent at the SQL level.

## Verification
- `dotnet build -c Release`: succeeded, 0 errors, 0 warnings.
- `dotnet test -c Release`: 190 total, 190 passed, 0 failed, 0 skipped.
- Test coverage added: registry definitions/aliases, placeholder alignment for every type (fails if a seeded clause uses an unknown or uncollected key), offline generation per type, DOCX (opens via Open XML) and PDF (page count > 0) export per type, title/summary incl. blank optional fields, and a Mutual NDA assembled-text snapshot.

## Manual click-through (WPF UI)
The Release app was launched and remained responsive (`DraftEngine` window). Interactive workflow verification remains manual:
1. Launch the app, open New Contract, and confirm all four type cards are selectable and show registry descriptions.
2. Select each type and confirm step 2 shows only that type's fields with the expected labels.
3. Leave an optional field blank and confirm step 4 omits it from the summary without breaking layout.
4. Confirm step 3 shows seeded clauses plus any user-created clause, and that archived clauses are hidden.
5. Generate each type and confirm the title, preamble, numbered `"N. Title"` headings, and role-labelled signature block (Discloser/Recipient, Contractor/Client, Consultant/Client) are correct.
6. Export DOCX and PDF for each type and confirm the signature block appears once and the title page shows the display name rather than the canonical ID.
7. Open an existing pre-upgrade contract and confirm it loads and exports normally after migration.

## Open issues
- Manual UI click-through above is still pending; automated coverage exercises registry/Core/Data/Documents paths but cannot validate visual layout or end-user navigation.
- The Release WPF app is currently open for the user to click through.

## Test counts
- Pre-Prompt 1 baseline: 168 total, 168 passed, 0 failed.
- After Prompt 1: 171 total, 171 passed, 0 failed.
- Prompt 2 before this verification pass: 185 total, 185 passed, 0 failed, 0 skipped.
- Prompt 2 current: 190 total, 190 passed, 0 failed, 0 skipped.
