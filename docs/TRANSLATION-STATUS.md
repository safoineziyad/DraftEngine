# Translation Status

Updated: 2026-11-12

## Resource coverage

| Language | Resource file | Keys | Key parity | Human review |
|---|---|---:|---|---|
| English | `DraftEngine.App/Resources/Strings.resx` | 499 | Pass: `LocalizationResourceTests.AllLanguages_HaveTheSameKeys` | Source language |
| French | `DraftEngine.App/Resources/Strings.fr.resx` | 499 | Pass | Native-speaker review still required |
| Arabic | `DraftEngine.App/Resources/Strings.ar.resx` | 499 | Pass | Native-speaker and RTL layout review still required |
| Spanish | `DraftEngine.App/Resources/Strings.es.resx` | 499 | Pass | Native-speaker review still required |

`LocalizationResourceTests.NoTranslationValue_IsEmpty` and
`LocalizationResourceTests.EveryLocMarkupKey_ExistsInEveryLanguage` enforce non-empty
translations and XAML key coverage. Passing tests do not certify translation quality.

## Migrated screens

Main window/navigation, Settings, onboarding, Dashboard, Contracts list, the New Contract
wizard, and Contract detail have localization-key coverage. The New Contract wizard adds 66 keys
and Contract detail adds 94 keys per language.
The wizard migration is covered by `LocalizationResourceTests.MigratedScreens_ContainNoLiteralUserFacingStrings`,
`LocalizationTests.NewContractDynamicLabels_RerenderWhenLanguageChanges`,
`LocalizationTests.NewContractSummaryUsesLocalizedFieldLabels`, and
`ContractContentBuilderTests.BuildSummary_ReturnsFieldKeysRatherThanDisplayLabels`.
Contract detail's live metadata/status, history rows and failure-message switching are covered by
`ContractDetailLocalizationTests.ContractDetailAndHistoryRows_RerenderAfterLanguageChange`.

The XAML literal baseline is now **0**: Clause Manager, Analyze, About and License Activation
were migrated in Phase 4, so every screen uses `{loc:T}` keys. `docs/i18n-baseline.txt` and
`LocalizationResourceTests.PendingLiteralCount_DoesNotExceedBaseline` keep it at zero.

## Remaining known gaps (Phase 4)

- **C# UI strings.** `ClauseManagerViewModel`, `AnalyzeViewModel`, `AboutViewModel` and
  `LicenseService` still build some user-visible status/dialog sentences in English (error
  banners, update status, analysis progress, license evaluation messages). They are tracked in
  `docs/I18N-INVENTORY.md`; page titles for the migrated screens now use resource keys.
- **Clause and legal text** is seed data and is intentionally English-only until a lawyer
  reviews each language.
- **Visual verification.** No screen has been checked visually in any language; Arabic RTL
  is wired through the shared window style (`FlowDirection` binding) but has not been seen.
- **Human review.** All non-English values remain machine-drafted.
