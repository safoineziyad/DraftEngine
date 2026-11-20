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

NEXT STEP: FIX 2 - Contracts list header
