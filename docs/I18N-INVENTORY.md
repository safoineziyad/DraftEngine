# DraftEngine v0.4.0 — Internationalization Inventory

**Purpose**: Inventory every hardcoded user-facing string in XAML and C#, compare against resx
language resources, and document the gaps for Stage B (Real localization).

---

## Inventory Method

- **XAML scanned**: `Text`, `Content`, `Header`, `ToolTip`, `Title`, `Watermark`, `AutomationProperties.Name`
- **C# scanned**: `MessageBox.Show`, status bar strings, validation error messages, enum display names, `StringFormat` literals
- **Resx compared**: 4 files × 15 keys = 60 total (EN/FR/AR/ES)

---

## A: Hardcoded strings in XAML

### MainWindow.xaml (8 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 40 | `DraftEngine` | Title |
| 92 | `Settings` | Content |
| 95 | `Settings` | Content (duplicate) |

### Views/SettingsView.xaml (20 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 18 | `Connect DraftEngine to OpenAI and choose the defaults used for new contracts.` | Label |
| 24 | `OpenAI` | Section header |
| 26-34 | Data handling explanation | Long description |
| 38 | `API key` | Label |
| 39 | `ApiKeyStatusText` | Binding (not hardcoded) |
| 62 | `OpenAI API key` | ToolTip |
| 76 | `OpenAiApiKey` | Text box (data binding) |
| 79 | `OpenAI API key` | ToolTip (duplicate) |
| 81 | `ShowHideKeyText` | Button content (bound) |
| 84 | `Test Connection` | Button |
| Settings section labels continue...

### Views/OnboardingWindow.xaml (6 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 7 | `Welcome to DraftEngine` | Title |
| 76 | `Skip` | Button |
| 83 | `Back` | Button |
| 91 | `PrimaryButtonText` | Binding |

### Views/DashboardView.xaml (15+ hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 73 | `Your agreements and drafting activity at a glance.` | Description |
| 77 | `New Contract` | Button |
| 113 | `ErrorMessage` | Foreground (bound) |
| 127-132 | `Recent contracts` + count | Toolbar |
| 158-162 | `Title`, `ContractType`, `StatusText` | Fields |
| 185 | `UpdatedAtText` | Field |

### Views/NewContractView.xaml (20+ hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 148 | `Draft a new agreement in four steps.` | Subtitle |
| 151 | `Discard draft` | Button |
| 183 | `Choose a contract type` | Label |
| 185 | `All contract types are available. Select the agreement you want to draft.` | Description |
| 220 | `Coming soon` | Note |
| 238 | `Contract details` | Section header |
| 240 | `Answer the questions below. These values fill the clauses and the final document.` | Description |
| 284 | `Standard clauses are pre-selected. Expand any clause to preview its text with your answers.` | Feature description |

### Views/ContractDetailView.xaml (15+ hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 35-36 | `Client: ` + ContractName | Field labels |
| 40 | `Save & regenerate` | Button |
| 42 | `AI polish` | Button |
| 51 | `Export DOCX` | Button |
| 55 | `Export PDF` | Button |
| 96-97 | `Contract document` + note | viewer labels |

### Views/ContractsListView.xaml (15+ hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 29 | `Search, review and manage your saved contracts.` | Description |
| 32 | `+ New Contract` | Button |
| 51 | `Search by title or client…` | Placeholder |
| 76-82 | `Type`, `Sort` | Column headers |
| 82 | `Clear filters` | Button |
| 125-128 | `Title`, `Contract type`, `Status`, `Effective date`, `Term`, `Updated` | Table headers |

### Views/ClauseManagerView.xaml (12+ hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 23 | `Create, edit and archive your clause templates.` | Description |
| 60 | `+ New` | Button |
| 64 | `Edit` | Button |
| 69 | `Duplicate` | Button |
| 74 | `Archive` | Button |
| 124 | `No clauses yet — create your first one.` | Empty state |
| 127 | `+ New Clause` | Button |
| 152 | `Title` | Column header |
| 176 | `PlaceholderWarning` | Foreground message |

### Views/AnalyzeView.xaml (10+ hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 20 | `Review a contract against a playbook. Deterministic checks work offline; AI review is optional and consent-gated.` | Subtitle |
| 42 | `DOCX or text-based PDF. Scanned PDFs need OCR, which is not supported yet.` | Warning |
| 45 | `Contract type (playbook)` | Label |
| 64 | `AI review (optional)` | Checkbox label |
| 67 | `AiConsentHint` | Binding (not hardcoded in current scan) |
| 83 | `Grouped by severity. Select a finding to highlight its quote.` | Instruction |
| 86 | `Export report (DOCX)` | Button |

### Views/LicenseActivationWindow.xaml (8 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 4 | `Activate DraftEngine` | Title |
| 14 | `Paste the signed license key you received. Activation is verified offline on this computer; no account or network connection is needed.` | Description |
| 17 | `License key` | Label |
| 27 | `Quit` | Button |
| 30 | `Activate` | Button |

### Views/ArchivedContractsView.xaml (12+ hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 28 | `Restore an archived contract or permanently delete it and its history.` | Description |
| 47 | `Archived contracts` | Section header |
| 50 | `No archived contracts.` | Empty state |
| 67-68 | `Title`, `ContractType` | Field labels |
| 70-73 | `Restore`, `Permanently delete` | Buttons |

### Views/AboutView.xaml (15+ hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 22 | `DraftEngine` | Label |
| 24 | `Version {0}` | Format |
| 26 | `DraftEngine drafts contracts locally from templates. Generation works fully offline and never requires an AI key.` | Description |
| 29-32 | `Open log folder`, `Open data folder` | Buttons |
| 41 | `Diagnostics` | Section header |
| 43 | `Version, Windows and .NET only. No contract data, paths or identifiers.` | Note |
| 45-57 | Full diagnostics copy | Long description |
| 55 | `Privacy summary` | Section header |
| 57-66 | Privacy-text + AI editing explanation | Long description |
| 59 | `Updates` | Section header |

---

## B: Hardcoded strings in C#

### App.xaml.cs (3 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 147-151 | `MessageBox.Show(..., MessageBoxButton.OK, MessageBoxImage.Error)` | Error dialog |
| 169-173 | `MessageBox.Show(..., MessageBoxButton.OK, MessageBoxImage.Warning)` | Warning dialog |

### ViewModels/ContractDetailViewModel.cs (2 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 967 | `MessageBoxImage.Information` | Information dialog |
| 1163 | `MessageBoxImage.Information` | Information dialog |

### ViewModels/ClauseManagerViewModel.cs (8 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 181-185 | `MessageBox.Show(YesNo, Warning)` | Archive confirmation |
| 215-216 | `MessageBox.Show(Please enter title and body, Clause Manager, OK, Warning)` | New clause prompt |
| 225-230 | `MessageBox.Show(YesNo, Warning)` | Proceed confirmation |

### Views/SettingsView.xaml (StringFormat literals)
| Line | String | Type |
|------|--------|------|
| 162 | `AiConsentGranted, StringFormat=AI data-sharing consent: {0}` | Format string |

### Views/DashboardView.xaml (StringFormat + AutomationProperties.Name)
| Line | String | Type |
|------|--------|------|
| 191-201 | `StringFormat={}{0:MMM d, yyyy}`, `StringFormat={}{0} months`, various date formats | Date formatting |
| 196-214 | `StringFormat=Open contract {0}`, `Duplicate contract {0}`, `Delete contract {0}` | AutomationProperties.Name |

### ViewModels/SettingsViewModel.cs (3 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 217-223 | `MessageBox.Show(warning, "Export my data", YesNo, Warning, No)` | Export data confirmation |
| 273 | `MessageBox.Show(warning, "Export my data", ...)` | Repeat call |

### Views/NewContractView.xaml (3 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 198 | `Select contract type {0}` | AutomationProperties.Name format |
| 338-355 | `Include {0} in the contract`, `Show or hide the preview of {0}` | AutomationProperties.Name |

### Services/WizardDialogService.cs (3 hardcoded strings)
| Line | String | Type |
|------|--------|------|
| 17 | `Confirm(string message, string title = "DraftEngine", ...)` | Default title |
| 28 | `MessageBox.Show(message, title, OK, Error)` | Helper |
| 32-34 | `MessageBox.Show(message, title, YesNo, Warning)` | Confirm overload |

---

## C: Resx file comparison

| File | Keys | Status |
|------|------|--------|
| `Resources/Strings.resx` (English) | 15 | ✅ Neutral base |
| `Resources/Strings.fr.resx` (French) | 15 | ✅ Translated |
| `Resources/Strings.ar.resx` (Arabic) | 15 | ✅ Translated (RTL) |
| `Resources/Strings.es.resx` (Spanish) | 15 | ✅ Translated |
| **Total** | **60** | **All present** |

### Gap: 0% of 60 keys have parity-verified C#/XAML coverage.
- Hardcoded strings far exceed resx key count.
- Many critical strings (legal terms, consent texts, disclaimers) are only in resx; many others are only hardcoded.

---

## Summary counts

| Category | Hardcoded strings found | Resx keys | Gap |
|---|---|---|---|
| XAML user-facing strings | ~180+ | 60 | **~120+ strings not in resx** |
| C# user-facing strings | ~25+ | 60 | **60 keys have no C# source coverage (good), but 25+ C# strings not in resx** |
| **Total uncovered** | **~205** | **60** | **~145 strings missing from resx or over-hardcoded** |

**Key insight**: The resx files are a good start (60 keys for 4 languages), but ~145+ user-facing strings remain hardcoded in XAML and C# and must be migrated into the resx system with localized versions.

---

## Next steps (Stage B)

1. Move ALL inventoried strings into `Strings.resx` (neutral English), `Strings.fr/ar/es.resx`.
2. Group keys by area (Nav, Settings, Analyze, Common, etc.).
3. Replace all literal strings in XAML with `{loc:T Key}` markup extension (to be implemented).
4. Replace all literal strings in C# with `Loc.Key` or `StringRes.Key` helper.
5. Use format strings with `{n}` placeholders only; no free-form concatenation.
6. Plurals via separate keys (not in scope for v0.4.0 initial pass).
7. Mark all languages "machine-drafted, needs native review" in `docs/TRANSLATION-STATUS.md`.
8. Priority for human review: privacy/consent/disclaimer texts.

**Test requirement (Stage B)**: key parity across languages (no missing/empty keys, identical `{n}` placeholders); XAML/C# scan test failing on literal user-facing strings (documented allow-list); App.Tests: switching language raises the event, updates a bound property, flips FlowDirection for Arabic; every page opens in every language without exceptions.