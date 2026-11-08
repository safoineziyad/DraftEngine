# Changelog

## 0.3.0
- Four contract types: Mutual NDA, One-Way NDA, Independent Contractor Agreement, Consulting Agreement.
- Registry-driven intake, clause defaults, type-aware preamble, recitals and signature blocks.
- Offline template generation and DOCX/PDF export for every type (no API key required).
- Schema migrations: archive flags, user-defined clause flags, wizard drafts, canonical contract-type strings, seed clauses 13-42, plus an upgrade test.
- Clause Manager: create, edit, duplicate and archive user clauses; seeded clauses stay read-only.
- Editable contracts after creation with snapshot-then-regenerate; duplicate, archive/restore/permanent delete; export history.
- Encrypted wizard draft persistence across navigation and restarts.
- AI editing: preview diff with Accept/Reject, consent gate, configurable OpenAI-compatible endpoint, party-name/address redaction, consent revocation in Settings.
- Optional three-screen onboarding and About & Help page (version, folders, privacy, update check, license status).
- Offline signed licensing behind Features:LicensingEnabled (off by default); notify-only update check (off by default); optional Authenticode signing in the installer build.
- Version bumped to 0.3.0; test suite now 222 tests.

## 0.2.2
- Previous release
