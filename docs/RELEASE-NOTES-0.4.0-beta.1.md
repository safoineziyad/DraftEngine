# DraftEngine 0.4.0 beta 1

This is a **pre-release** for testing. `v0.2.2` remains the latest stable release.

## What changed

- **Contract types.** Multi-type drafting with per-type intake, clauses, fields and template generation.
- **Analysis.** DOCX/PDF/TXT import, clause segmentation, four playbooks, deterministic checks, grounded AI review and a comparison/redline report.
- **Localization.** English, French, Arabic and Spanish, with live language switching, RTL layout and European digits for Arabic. Navigation and most screens are migrated; **Clause Manager, Analyze, About and the license activation window are still English-only** in this build.
- **Themes.** Follow Windows, Light and Dark, plus automatic High Contrast support using Windows system colors.
- **Backup and restore.** Pre-upgrade database snapshots and portable encrypted backups.

## Upgrade data-loss fix

Upgrading a 0.2.2 database could appear to lose contracts. The cause was **not** a migration deleting the user: EF Core leaves SQLite in WAL mode, and copying only `draftengine.db` dropped committed rows that lived in the `-wal` file. DraftEngine now:

- snapshots the database through the SQLite online backup API **before** any migration;
- restores that snapshot automatically if a migration fails, and shows a localized error window with a content-free **Copy diagnostics** button;
- enforces `Restrict` on every foreign key from contracts to the user;
- ships a corrective migration for the old cascade foreign key (released migrations are unchanged).

Verified on a copy of a real data folder: `Users 1→1`, `Contracts 1→1`, `ContractFields 9→9`, `ContractClauses 9→9`, `ContractVersions 1→1`.

## Before installing

**Back up `%APPDATA%\DraftEngine` first** (database, settings, generated documents and logs). This is a beta build; keep that copy until you are satisfied with the upgrade.

## Windows SmartScreen

The installer is **not code-signed yet**, so Windows shows "Windows protected your PC" / unknown publisher. Choose **More info → Run anyway** only if you downloaded the file from this release page and the SHA-256 matches.

## SHA-256

Installer: `DraftEngine-Setup-0.4.0.1.exe`

```
40aaee43fe6c98e896114fe3eacacd03c5c0b023fbf52cca11b2d4d9902e5948
```

The same value is attached as `DraftEngine-Setup-0.4.0.1.exe.sha256`.

## Known limitations

- Some screens are not yet localized (see above); translations have not had a human review.
- No code signing.
- UI layout in all four languages has not been visually reviewed on every screen.
