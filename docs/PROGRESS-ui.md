# UI Polish Progress

## Baseline
- Commit: `c734130bc9d2b7761508cd7b2e84759c84b50091`
- Test Counts:
  - `DraftEngine.Analysis.Tests`: 28 passed, 0 failed
  - `DraftEngine.Tests`: 334 passed, 1 failed (`MutualNda_AssembledText_MatchesSnapshot` - pre-existing snapshot failure)
  - `DraftEngine.App.Tests`: 130 passed, 0 failed
  - Total: 492 passed, 1 failed

## FIX 1: Raw object text in dropdowns
- Status: Not reproduced (already resolved prior to HEAD; audited and verified via new automated test suite)
- Verification:
  - Settings Language ComboBox uses `DisplayMemberPath="NativeName"`
  - Settings Theme ComboBox uses `DisplayMemberPath="DisplayName"`
  - Contracts Type and Sort ComboBoxes use `DisplayMemberPath="DisplayName"`
  - Analyze contract type ComboBox uses `DisplayMemberPath="DisplayName"`
  - Added `DraftEngine.App.Tests/DropdownAuditTests.cs` (5 tests) auditing all ViewModel collection displays and verifying every bound XAML ComboBox defines `DisplayMemberPath` or `ItemTemplate`
  - Added visual tree raw text audit (`AssertNoRawObjectTextOrUnresolvedKeys`) across all 9 views in en/fr/ar/es and Light/Dark themes in `WpfPageOpenTests.cs`
- Tests: `DraftEngine.App.Tests`: 135 passed (rose from 130). Full suite: 497 passed, 1 pre-existing failed.

## FIX 2: Contracts list header
- Status: Done
- Verification (from code, not visually verified):
  - Fixed column definitions in `ContractsListView.xaml` so `ColTitle` (MinWidth="180") and `ColType` (MinWidth="140") never collapse or jam headers together.
  - Added `Margin="0,0,12,0"` to all header TextBlocks matching the data rows for consistent column alignment in LTR and RTL.
  - Verified `ContractRowViewModel.ContractType` displays human-readable localized display name via `ContractTypeRegistry.GetByDisplayName` and never raw ID or class name.
  - Verified localized `StatusText` for all `ContractStatus` values.
  - Added `ContractsListHeaderTests.cs` (10 tests) covering header localization across en/fr/ar/es and display name resolution.
  - Follow-up: header TextBlocks now use `VerticalAlignment="Center"`, matching the data rows exactly in both LTR and RTL.
- Tests: `DraftEngine.App.Tests`: 145 passed (rose from 135). Full suite: 507 passed, 1 pre-existing failed.

## FIX 3: Theme leaks
- Status: Done
- Verification (from code, not visually verified):
  - Fixed brand bar in `MainWindow.xaml`: changed foreground from `ForegroundBrush` (#FFFFFF in both themes) to `AccentForegroundBrush` (#FFFFFF in Light, #0B1220 in Dark), meeting WCAG AA contrast (>= 4.5:1) against `AccentBrush` in both themes.
  - Added full theme styling for `DatePicker`, `DatePickerTextBox`, `Calendar`, `CalendarButton`, and `CalendarDayButton` in `DraftEngine.App/Themes/Styles.xaml` using semantic theme brushes (`InputBackgroundBrush`, `PopupBackgroundBrush`, `ControlBorderBrush`, `TextPrimaryBrush`, etc.).
  - Added `PART_EditableTextBox` to `ComboBox` template in `Styles.xaml` so editable ComboBoxes (e.g. Governing law in Settings) use themed backgrounds and foregrounds in both Light and Dark themes instead of Windows default light gray.
  - Updated `NewContractView.xaml` DatePicker style to inherit the themed default style via `BasedOn`.
  - Added `DraftEngine.App.Tests/ThemeLeakTests.cs` (3 tests) validating brand bar contrast (>= 4.5:1) in both themes, DatePicker contrast in both themes, and editable ComboBox template support.
- Tests: `DraftEngine.App.Tests`: 148 passed (rose from 145). Full suite: 510 passed, 1 pre-existing failed.

## FIX 4: Clause Manager
- Status: Done
- Verification (from code, not visually verified):
  - Injected `ILocalizationService` into `ClauseManagerViewModel` constructor; eliminated all hardcoded English strings (load, duplicate, archive, save errors, confirmation and validation dialogs, and filters).
  - Added visible label text (`Ui.ClauseFilterType`, `Ui.ClauseFilterCategory`, `Ui.ClauseFilterSource`) before each filter ComboBox in `ClauseManagerView.xaml`.
  - Added dedicated column headers (Title, Category, Source, Order, Type) above the clause list matching column widths (`2*`, `1.5*`, `100`, `80`, `90`).
  - Replaced raw `StringFormat=' optional'` text with a styled localized badge (`Wizard.Optional`).
  - Added 22 new resource strings with prefix `Ui.` to `Strings.resx`, `Strings.fr.resx`, `Strings.ar.resx`, and `Strings.es.resx` with 100% key parity.
  - Added `DraftEngine.App.Tests/ClauseManagerPolishTests.cs` (3 tests) verifying localized errors, injected PageTitle, and XAML column headers/filter labels.
- Tests: `DraftEngine.App.Tests`: 151 passed (rose from 148). Full suite: 513 passed, 1 pre-existing failed.

## FIX 5: Jurisdiction default
- Status: Done
- Verification (from code, not visually verified):
  - Replaced hard-coded `"California"` default in `AppSettings.cs` (`DefaultGoverningLaw`) with the user's localized region country compose string (`CountryCatalog.DefaultCode()`).
  - Verified `NewContractViewModel` and `IntakeFieldViewModel` initialize the governing law country and subdivision from configured defaults rather than California.
  - Added `DraftEngine.App.Tests/JurisdictionDefaultTests.cs` (2 tests) verifying `AppSettings` and `NewContractViewModel` defaults.
- Tests: `DraftEngine.App.Tests`: 153 passed (rose from 151). Full suite: 515 passed, 1 pre-existing failed.

NEXT STEP: FIX 3 - Theme leaks (audit and finish remaining dynamic-resource gaps)
