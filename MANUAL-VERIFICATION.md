# Manual Verification — v0.2.1 Critical Hotfix

**Branch:** 0.2.1-hotfix  
**Release:** 0.2.1  
**Date:** 2026-10-21  
**Tester:** __________________________  
**Environment:** Windows 10/11, .NET 8 Desktop Runtime

> This checklist is used to verify the four showstoppers and two layout fixes against a real local SQLite database and the running WPF app. Automated gates (build 0/0, tests green) are required but not sufficient; the items below must be satisfied before tagging/pushing.

## Preconditions
- [ ] Working tree clean of uncommitted changes related to this release (other concurrent work left untouched).
- [ ] dotnet build DraftEngine.sln -c Release → **0 errors / 0 warnings**
- [ ] dotnet test DraftEngine.sln -c Release --no-build → **all green** (includes ContractOwnerUserTests and ContractCreationIntegrationTests)
- [ ] Close the DraftEngine app if running.

## A1 — Contracts can be created (Users seed + migration)

1. [ ] **Backup existing DB (safety):** Copy %APPDATA%\DraftEngine\draftengine.db to a safe location.
2. [ ] **Reset DB:** Delete %APPDATA%\DraftEngine\draftengine.db (and draftengine.db-wal / draftengine.db-shm if present).
3. [ ] **Launch app:** Start DraftEngine.App\bin\Release\net8.0-windows\DraftEngine.App.exe. Wait for startup to complete (no crash).
4. [ ] **Verify seed:** Open the DB and confirm:
   - SELECT COUNT(*) FROM Users; → **1**
   - SELECT COUNT(*) FROM Clauses; → **12**
   - (Optional) SELECT Id, Email FROM Users; → owner present
5. [ ] **Create a contract end-to-end:** Use **New Contract** wizard → fill Party 1/2, Client, Effective Date, Governing Law, Term, Purpose → proceed through Clauses → **Generate Contract**.
6. [ ] **Persist and reload:** Save/navigate away and reopen the contract from **Contracts** list. The document must load without foreign-key errors or crashes.
7. [ ] **Generate produces output:** AssembledText is non-empty; no {{ ... }} placeholders remain.

## A2 — Contract Detail page loads (INavigationAware)

8. [ ] **Navigate to detail:** From **Dashboard** or **Contracts** list, open an existing contract. The **Contract Detail** page must render (no blank page / navigation failure).
9. [ ] **Code check (automated):** ContractDetailViewModel implements INavigationAware and NavigationService calls OnNavigatedTo when implemented (verified in code).

## A3 — Wizard uses Settings defaults

10. [ ] **Set defaults:** Open **Settings**, set **Default Governing Law** = Texas (or another non-default) and **Default Term Months** = 36, save.
11. [ ] **Open New Contract:** Launch the wizard fresh. On **Details** step, **Governing Law** should default to the saved value and **Term (months)** should default to 36. Changing settings and reopening applies the latest values.

## B1 — Contracts list header no longer overlaps content

12. [ ] **Layout check:** Open **Contracts**. The column headers (e.g. Title/Type/Status/Modified) must remain in their own header row and not overlap the contract rows. Resize the window to confirm no overlap.

## B2 — Dashboard header/button alignment

13. [ ] **Layout check:** Open **Dashboard**. The page title/subtitle sit on the left; **New Contract** button sits on the right in the same header row, aligned vertically, with no overlap.

## Notes / Evidence
- Build/Test: __________________________
- DB verification (Users/Clauses): __________________________
- Screenshots attached: ☐
- Issues found: __________________________

**Sign-off:** __________________________  **Date:** __________________________
