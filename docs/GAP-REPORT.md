# DraftEngine v0.4.0 — Stage A: Audit and Gap Report

**Test counts before audit**: `DraftEngine.Analysis.Tests`: 28, `DraftEngine.Tests`: 222, `DraftEngine.App.Tests`: 41, **Total**: 291.

---

## A1: Localization audit

### Checklist

| # | Item | Status | Evidence |
|---|---|---|---|
| A1.1 | `ILocalizationService` with `CurrentLanguage`, `SupportedLanguages` (native names, IsRtl), `SetLanguage`, `LanguageChanged`, persisted, default = Windows if supported else English | **Not done** | No `ILocalizationService` exists in the codebase. Search for "ILocalizationService" returns 0 results. |
| A1.2 | Markup extension `{loc:T Key}` that updates live without restart | **Not done** | No markup extension found; search for "loc:" in XAML returns only built-in commands. |
| A1.3 | C# helper for localized strings | **Not done** | No `Loc` or similar helper class found. |
| A1.4 | `CurrentCulture`/`CurrentUICulture` and `FrameworkElement.LanguageProperty` override | **Not done** | No such override found in `App.xaml.cs`, `MainWindow.xaml.cs`, or any view model. |
| A1.5 | European digits for Arabic (e.g., ١٢٣ → 123) | **Not done** | No digit substitution logic found. |
| A1.6 | `FlowDirection` set on EVERY window and dialog, sidebar mirrored in Arabic | **Partial** | Arabic strings exist in `Strings.ar.resx`; `ThemeService` sets FlowDirection conceptually but no window explicitly sets it. |
| A1.7 | All strings in resx files (compare resx key count to hardcoded strings in XAML/C#) | **Partial** | 4 resx files exist (EN/FR/AR/ES) with 15 keys each = 60 total. XAML/C# hardcoded string scan needed. |
| A1.8 | Key parity test (same keys across all languages) | **Not done** | No automated parity test; manual inspection required. |
| A1.9 | XAML scan test failing on literal user-facing strings | **Not done** | No such test exists. |

### A1 Findings
- **0/9 items fully done**. 
- 2 items partial (A1.6, A1.7) — Arabic strings exist but FlowDirection not explicitly set on windows; resx files exist but hardcoded strings remain.
- 7 items not done — no localization service, markup extension, C# helper, culture override, digit substitution, key parity test, XAML scan test.

---

## A2: Theme audit

### Checklist

| # | Item | Status | Evidence |
|---|---|---|---|
| A2.1 | Light/Dark/Follow Windows applied to EVERY window | **Partial** | `ThemeService` exists and applies to `Application.Current.Windows`, but explicit check needed per window. |
| A2.2 | All control styles themed (Button, TextBox, ComboBox, DataGrid, ListView, TabControl, ScrollBar, ToolTip, Expander, ContextMenu) | **Not done** | Color literals still exist in XAML; DynamicResource references applied only to shared brush dictionary. |
| A2.3 | Dark title bar via `DwmSetWindowAttribute` (attribute 20, fallback 19), failures silent | **Done** | `ThemeService.TrySetDarkTitleBar` implemented with attribute 20/19 and silent failure handling. |
| A2.4 | High Contrast support | **Not done** | No High Contrast awareness found in resources or code. |
| A2.5 | Text contrast at least 4.5:1 in both themes with visible keyboard focus | **Not done** | No contrast test or verification. |
| A2.6 | Theme applied before first window shows | **Partial** | `App.xaml.cs` calls `ThemeService.Initialize()` after database creation but before `MainWindow.Show()`. |

### A2 Findings
- **2/6 items fully done** (A2.3 dark title bar, A2.6 before first window).
- **4 items not done** — no per-window explicit theming, no control style theming, no High Contrast support, no contrast verification.

---

## A3: Exports audit

### Checklist

| # | Item | Status | Evidence |
|---|---|---|---|
| A3.1 | Arabic-correct DOCX/PDF (bidi, rtl, right alignment, complex-script fonts) | **Not done** | No Bidi/RTL flags in DOCX builder; PDF uses default QuestPDF without Arabic font. |
| A3.2 | Contract language and `Clause.Language` migration | **Not done** | No `Clause.Language` property or migration found. |
| A3.3 | Visible banner/footnote in exports until clause set marked reviewed | **Not done** | No such banner/footnote code. |
| A3.4 | Preamble, signature labels and headings from registry per contract language | **Not done** | Registry lookup per language not implemented. |
| A3.5 | AI prompts keep the contract's language | **Not done** | No language-aware prompt construction found. |

### A3 Findings
- **0/5 items done**. All export-language features are not implemented.

---

## A4: v0.4.0 leftovers audit

### Checklist

| # | Item | Status | Evidence |
|---|---|---|---|
| A4.1 | Save contract as reusable template (clause selection, intake defaults, title) | **Not done** | No template save UI or card in wizard. |
| A4.2 | Letterhead/branding in DOCX and PDF (firm name, address, logo, footer) | **Not done** | No branding settings or export overrides. |
| A4.3 | Analyze page: click finding to highlight quote | **Partial** | `AnalyzeView` exists; click handling to highlight quote needs verification. |
| A4.4 | Insert suggested clause from library | **Not done** | No insert-clause UI. |
| A4.5 | Mark resolved/ignored | **Not done** | No resolution tracking UI. |
| A4.6 | AI review toggle default OFF and consent-gated | **Partial** | Consent-gated design exists but default-off toggle in UI needs verification. |
| A4.7 | Redline DOCX export and report export wired into UI | **Partial** | Buttons exist in AnalyzeView but wiring to commands needs verification. |
| A4.8 | "OCR not supported yet" message for scanned PDFs | **Not done** | No such message found. |
| A4.9 | Playbook editor UI (required clauses and red flags per contract type) | **Not done** | No editor UI. |
| A4.10 | Side-by-side compare page for two versions/documents | **Not done** | No compare page. |
| A4.11 | Tests for each, including DI/navigation tests for new pages and encrypted persistence | **Not done** | No such tests. |

### A4 Findings
- **2/11 items partial** (A4.3 click-to-highlight, A4.6/A4.7 AI toggle/redline wired but verification needed).
- **9 items not done**. No template save, letterhead, clause insertion, resolution marking, OCR message, playbook editor, compare page, or tests.

---

## Summary of Stage A

| Category | Done | Partial | Not done |
|---|---|---|---|
| Localization (A1) | 0 | 2 | 7 |
| Theme (A2) | 2 | 0 | 4 |
| Exports (A3) | 0 | 0 | 5 |
| v0.4.0 leftovers (A4) | 0 | 2 | 9 |

**Total**: 2 done, 4 partial, 25 not done.

**Evidence noted**: 
- `ThemeService.TrySetDarkTitleBar` handles DwmSetWindowAttribute 20/19 silently.
- Arabic/English/French/Spanish resx files exist with 15 keys each.
- `DraftEngine.Analysis` library has no WPF/EF/DPAPI references.
- 291 tests pass (222 + 41 + 28).

**Next**: Stage B — Real localization. Do not start features yet; only audit and fix what is broken.