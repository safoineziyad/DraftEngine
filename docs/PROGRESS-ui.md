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
- Tests: `DraftEngine.App.Tests`: 145 passed (rose from 135). Full suite: 507 passed, 1 pre-existing failed.

NEXT STEP: FIX 3 - Theme leaks
