# Phase 0 — Inventory (integration/rc1)

Written 2026-11-17 by the integration agent. **Every number below was produced by a command run in
this session**; nothing here is copied from another agent's report.

## 0.1 Folder and remote

```
folder   C:\Users\Zbook\Desktop\ziyad_Zbook\programming\DraftEngine-integration  (fresh clone, created this session)
remote   origin https://github.com/safoineziyad/DraftEngine.git
start    git branch --show-current = main, git status --short = empty
base     origin/feature/i18n-theme @ c734130 (2026-11-15 12:56) — "feat(updates): replace the silent
         self-updater with the signed update UI"
toolchain git 2.56.0.windows.2, .NET SDK 8.0.426, gh 2.98.0
```

`git fetch --all --prune` was run first. The existing `..\DraftEngine` folder was only read
(`git -C ... remote -v`) to obtain the URL; nothing in it was modified.

## 0.2 Remote branches vs `origin/feature/i18n-theme`

`git rev-list --left-right --count origin/feature/i18n-theme...<branch>` — **base-only / branch-only**.
"contained" means the branch is an ancestor of the base (0 files of its own to merge).

| Branch | base-only | branch-only | Status | Content |
|---|---|---|---|---|
| `feature/i18n-theme` | 0 | 0 | base | i18n/theme + AI profiles + signed updates UI |
| `fix/audit-data-safety` | 0 | **10** | **candidate** | DPAPI preservation, backup completeness, delete-with-analyses, duplicate jurisdiction, import hardening, filename sanitizer, async startup, clause-manager i18n, warning cleanup, protected-data banner |
| `wip/unfinished-save` | 0 | **4** | **candidate** | signed-update release tooling (`scripts/release.ps1`, `tools/UpdateKeyTool`), update end-to-end tests, `docs/NETWORK-CALLS.md`, installer/runtime-check tweaks |
| `feature/profile-mode` | 12 | **3** | **candidate, branched 12 commits before base** | local user profiles, feature matrix, first-run profile cards, Settings profile card |
| `feature/morocco-data-mode` | — | — | **MISSING** | branch does not exist on the remote (see §0.5) |
| `release/0.4.0-beta.2` | 7 | 0 | contained in base | country catalog + WAL-safe restore work already in base |
| `feature/v0.3.0`, `v0.3.0-mega`, `v0.2.1-hotfix`, `v0.2.0-verify`, `v0.2.0-backup` | n/a | 0 | contained in base | historical, superseded |
| `main` | 0 | 4 | ahead of base by the 4 `wip/unfinished-save` commits (PR #5) | — |

Last five commits of each candidate were printed in this session and are consistent with the table
above; they are reproduced per branch in §0.3.

## 0.3 Isolated build + test per candidate

Each branch was checked out in a **worktree outside this folder** (`..\wt-<name>`), built Release,
and tested with `DRAFTENGINE_DATA_DIR` pointed at a **fresh temp folder per run**. These worktrees
were removed afterwards (see end of this file).

Command per worktree:

```
dotnet build DraftEngine.sln -c Release --nologo
dotnet test  DraftEngine.sln -c Release --nologo -v q --no-build
```

| Worktree | Ref | Build | Analysis.Tests | Tests | App.Tests | Total | Failing |
|---|---|---|---|---|---|---|---|
| `wt-base` | `origin/feature/i18n-theme` | **Pass** (exit 0) | 28 / 28 | 334 / 335 | 130 / 130 | **493** | **1** |
| `wt-audit` | `origin/fix/audit-data-safety` | **Pass** (exit 0) | 31 / 31 | 339 / 339 | 143 / 143 | **513** | 0 |
| `wt-save` | `origin/wip/unfinished-save` | **Pass** (exit 0) | 28 / 28 | 335 / 335 | 140 / 140 | **503** | 0 |
| `wt-profile` | `origin/feature/profile-mode` | **Pass** (exit 0) | 28 / 28 | 232 / 232 | 151 / 151 | **411** | 0 |

All four branches build. None is rejected on build grounds.

### GATE 0 — baseline on `origin/feature/i18n-theme` (recorded)

- Build: exit 0.
- `DraftEngine.Analysis.Tests` 28 passed, 0 failed.
- `DraftEngine.Tests` 334 passed, **1 failed**, 335 total.
- `DraftEngine.App.Tests` 130 passed, 0 failed.
- **Total 493: 492 passed, 1 failed.**
- The failure is `DraftEngine.Tests.Generation.MultiTypeGenerationTests.MutualNda_AssembledText_MatchesSnapshot`.
  Reproduced alone with `--filter FullyQualifiedName~MutualNda_AssembledText_MatchesSnapshot`;
  the assertion output is a line-ending mismatch only:

  ```
  Expected: ···"me Corp & Globex Ltd\r\n\r\nThis Mutual Non-D"···
  Actual:   ···"me Corp & Globex Ltd\n\nThis Mutual Non-Dis"···
  ```

  The expected snapshot is a raw string literal in a CRLF-checked-out `.cs` file; the generated
  text uses `\n`. All three candidate branches fix it the same way
  (`Assert.Equal(Normalize(ExpectedMutualNdaSnapshot), Normalize(text))`) — content comparison stays
  exact, only line endings are normalized on both sides. Recorded as **DEFECT-001**, not a
  loosening of a behavioural assertion.

### GATE 0 note on `feature/profile-mode`

`wt-profile` has only 232 tests in `DraftEngine.Tests` because the branch is **12 commits behind the
base** (`a5ce062` "WIP: save progress" is its newest ancestor of the base). Its own 3 commits add
`ProfileService`, `ProfileFeatureMatrix`, first-run profile cards, a Settings profile card and 5 test
files. Merging it therefore has to reconcile with 12 commits of base work it has never seen.

## 0.4 Proposed merge order

1. `fix/audit-data-safety` — 10 commits, 0 behind, 513 green. Highest value, lowest risk (fast-forward).
2. `wip/unfinished-save` — 4 commits, 0 behind, 503 green. Tooling + tests; touches `.sln`, `.gitignore`, installer, `docs/`.
3. `feature/profile-mode` — 3 commits, 12 behind, 411 green. Highest conflict risk.
4. `feature/morocco-data-mode` — **skipped, does not exist** (§0.5).

Known overlap to expect while merging (from `git diff --stat` in this session):

| File | Branches that touch it |
|---|---|
| `DraftEngine.Tests/Generation/MultiTypeGenerationTests.cs` | audit, save, profile — **identical one-line change from all three** |
| `DraftEngine.App/Resources/Strings.{resx,fr,ar,es}.resx` | audit (+13 keys), profile (+16 keys) |
| `DraftEngine.App/ViewModels/MainViewModel.cs` | audit (+44), profile (+90) |
| `DraftEngine.App/ViewModels/SettingsViewModel.cs` | profile (+58) |
| `DraftEngine.App/Services/ServiceRegistration.cs` | audit (+3), profile (+4) |
| `DraftEngine.App/Views/SettingsView.xaml` | save (+2), profile (+4) |
| `DraftEngine.App.Tests/UpgradeMigrationTests.cs` | save (+6), profile (+14) |
| `DraftEngine.sln` | save (+19: `UpdateKeyTool` project) |
| `docs/PROGRESS*.md` | audit, save, profile (one file each) |

## 0.5 `feature/morocco-data-mode` — missing

- `git branch -r` lists **no** branch matching `morocco`.
- `git log --all --grep=morocco -i` returns **0 commits**.
- The only artefact anywhere in history is `docs/PROGRESS-morocco.md`, which exists **only** on
  `wip/unfinished-save` (16 lines). It records a Phase 1 network-call audit and nothing else; it
  contains no code, no endpoint-policy type and no test.
- `git log --all --grep='data mode|data-mode|AI mode' -i` returns **0 commits**.
- Consequence: the **AI data-protection mode is Not implemented**. The inventory of outbound calls
  it produced (`docs/NETWORK-CALLS.md`) *does* arrive with the `wip/unfinished-save` merge and will be
  re-audited against the merged code in Phase 1 step 4a. A grep of the base tree for
  `EndpointPolicy|AiEndpointPolicy|EndpointValidation` returns nothing, so **no endpoint policy
  exists in the merged code today**; the proposed merge order cannot create one without inventing a
  feature. Recorded in `docs/NOT-DONE.md`.

## 0.6 Observations to carry into Phase 2

- `.tmpdiag/` (`Program.cs`, `diag.csproj`) is **committed on every branch including the base** and is
  **not** referenced by `DraftEngine.sln`. It is a leftover diagnostic scratch project — repo hygiene,
  candidate Low defect.
- Test-count claims in the branches' own progress docs match what I measured
  (audit 513, save 503, profile 411, base 493). The *feature* claims were **not** trusted and are
  re-verified from code and tests in Phase 2.
- `.gitignore` already excludes `publish/`, `dist/`, `bin/`, `obj/`, `*.pfx`, `*.snk`, `.env`,
  `DE-backup-*/`. `wip/unfinished-save` adds 7 more lines.
- A full history scan (`git log --all --diff-filter=A --name-only`) for `*.pem|*.pfx|*.key|*.env|*.exe|*.zip|*.msi`,
  `publish/`, `dist/`, `/bin/`, `/obj/` returned **0 matches**. Secret-content scanning is repeated on
  the integration branch in Phase 4.

## 0.7 Cleanup

```
git worktree remove ..\wt-base --force
git worktree remove ..\wt-audit --force
git worktree remove ..\wt-save --force
git worktree remove ..\wt-profile --force
```

Temp data dirs (`..\wt-datadir-*`) are outside the repository and contain no contract data.

NEXT STEP: Phase 1 — merge `fix/audit-data-safety`, then `wip/unfinished-save`, then
`feature/profile-mode`; build + full suite after each; then GATE 1 and push `integration/rc1`.
