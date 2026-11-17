# Audit data-safety progress

Branch: `fix/audit-data-safety` in `DraftEngine-audit` (separate checkout).

## Step 0 — baseline (verified)

- Folder: `C:\Users\Zbook\Desktop\ziyad_Zbook\programming\DraftEngine-audit`
- Remote: `https://github.com/safoineziyad/DraftEngine.git`
- Branch: `fix/audit-data-safety`, tracking `origin/fix/audit-data-safety`; `git pull` reported up to date.
- Worktree: this checkout is the only one named `DraftEngine-audit`. Other sibling worktrees exist for other agents and were not touched.
- Leftover from the previous session: untracked `DraftEngine.Data/ProtectedDataUnavailableException.cs` (builds cleanly; kept, not discarded).
- `dotnet build DraftEngine.sln -c Release` → 0 errors.
- Baseline `dotnet test DraftEngine.sln -c Release --nologo -v q` with a fresh `DRAFTENGINE_DATA_DIR`:
  - `DraftEngine.Analysis.Tests`: 28 passed, 0 failed.
  - `DraftEngine.Tests`: 334 passed, 1 failed (`MultiTypeGenerationTests.MutualNda_AssembledText_MatchesSnapshot`, expected CRLF vs generated LF).
  - `DraftEngine.App.Tests`: 130 passed, 0 failed.
  - Total: 492 passed, 1 failed, 0 skipped.
- The one failure is a pre-existing line-ending assertion in this branch; the expected snapshot was not normalized. Fixed by normalizing both sides (the newer repository version does the same); no assertion was removed.

FIX 1 — not started.

NEXT STEP: FIX 1 — preserve undecryptable DPAPI ciphertext and expose a typed warning flag; write the failing test first.
