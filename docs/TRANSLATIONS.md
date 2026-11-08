# DraftEngine — Translation Status and Glossary

**Status:** translation source of truth for the `feature/i18n-theme` branch.

## Status by language

English (`Strings.resx`) is the reference. The other files were drafted from the
English source by the implementation assistant and have **not** been reviewed by a
native speaker or a lawyer.

| Language | File | Keys | Status |
|---|---|---|---|
| English | `DraftEngine.App/Resources/Strings.resx` | 164 | Source / reviewed by authors |
| French | `DraftEngine.App/Resources/Strings.fr.resx` | 164 | **Machine-drafted, needs native review** |
| Arabic | `DraftEngine.App/Resources/Strings.ar.resx` | 164 | **Machine-drafted, needs native review** — also needs RTL layout review |
| Spanish | `DraftEngine.App/Resources/Strings.es.resx` | 164 | **Machine-drafted, needs native review** |

Parity is enforced by `DraftEngine.App.Tests/LocalizationResourceTests` (same keys,
no empty values, every `{loc:T}` key present in all four files). Parity does not mean
a translation is correct; it only means no key is missing.

Translation scope so far: the global window chrome/sidebar, the Settings screen, the
onboarding dialog and the Dashboard. Remaining screens are tracked by
`PendingMigration` in the same test file and in `docs/PROGRESS.md`.

## Review process

1. A native speaker reviews each non-English file against the English source.
2. Legal/contract wording is reviewed by a lawyer qualified in the target market.
3. Only then remove the "machine-drafted" status for that language.
4. Never translate product names, provider names (`OpenAI`), model ids
   (`gpt-4o-mini`, `gpt-4o`), file extensions or URL literals.

## Glossary (preferred terms)

Keep these terms consistent as more screens migrate.

| English | French | Arabic | Spanish |
|---|---|---|---|
| contract | contrat | عقد | contrato |
| clause | clause | بند | cláusula |
| party / parties | partie / parties | طرف / أطراف | parte / partes |
| governing law | droit applicable | القانون الحاكم | ley aplicable |
| term (duration) | durée | مدة | plazo |
| intake answers | réponses du questionnaire | إجابات الاستبيان | respuestas del cuestionario |
| AI | IA | الذكاء الاصطناعي | IA |
| AI polish | reformulation par IA | تحسين بالذكاء الاصطناعي | pulido con IA |
| backup | sauvegarde | نسخة احتياطية | copia de seguridad |
| restore | restaurer | استعادة | restaurar |
| encrypted | chiffré | مشفَّر | cifrado |
| settings | paramètres | الإعدادات | configuración |
| save | enregistrer | حفظ | guardar |
| export | exportation / exporter | تصدير | exportación / exportar |
| unsaved changes | modifications non enregistrées | تغييرات غير محفوظة | cambios sin guardar |

## Formatting and layout rules

- Placeholders (`{0}`, `{1}`) are positional; translators may reorder them but must
  keep every placeholder and the format must remain valid.
- Use separate plural keys rather than concatenating counts into words.
- Arabic uses right-to-left layout and **European digits** (`NumberSubstitution`), so
  numbers in translated text use `0-9`.
- Dates, numbers and currency are formatted with the active `CurrentCulture`.
- Do not put legal citations or jurisdiction-specific claims in resource strings;
  legal text stays in the contract templates and the always-visible disclaimer.
