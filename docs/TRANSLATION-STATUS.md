# Translation Status

Updated: 2026-11-08

## Resource coverage

| Language | Resource file | Keys | Key parity | Human review |
|---|---|---:|---|---|
| English | `DraftEngine.App/Resources/Strings.resx` | 373 | Pass: `LocalizationResourceTests.AllLanguages_HaveTheSameKeys` | Source language |
| French | `DraftEngine.App/Resources/Strings.fr.resx` | 373 | Pass | Native-speaker review still required |
| Arabic | `DraftEngine.App/Resources/Strings.ar.resx` | 373 | Pass | Native-speaker and RTL layout review still required |
| Spanish | `DraftEngine.App/Resources/Strings.es.resx` | 373 | Pass | Native-speaker review still required |

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

The shrinking XAML literal baseline is **90** candidates across the five pending screens, tracked
by `docs/i18n-baseline.txt` and `LocalizationResourceTests.PendingLiteralCount_DoesNotExceedBaseline`.
This scan does not measure remaining C# literals or clause/legal text. WPF rendering and all
translations are **not visually verified** and have not received human linguistic review.
