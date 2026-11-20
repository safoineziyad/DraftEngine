# Using DraftEngine profiles

A profile is a **local preference** that chooses which existing pages DraftEngine shows and how
the first-run and dashboard guidance is worded. It is **not an account**, is never transmitted,
and creates no telemetry. The choice lives in `settings.json` next to the other settings.

## Profiles

| Profile | Intended use |
|---|---|
| Lawyer | Full drafting toolkit, including the clause library tools. |
| Business | General information and drafting for business users; clause-library tools are hidden. |
| Mediator | Mediation use: shared drafting pages today, with a mediation workspace planned. |

## Feature matrix

Only pages that exist today are listed. `Templates` and `PlaybookEditor` do not exist in the
application, so they are deliberately absent rather than invented.

| Feature key | Page | Lawyer | Business | Mediator |
|---|---|---|---|---|
| Dashboard | Dashboard | yes | yes | yes |
| NewContract | New Contract | yes | yes | yes |
| Contracts | Contracts | yes | yes | yes |
| Archive | Archived contracts | yes | yes | yes |
| Analyze | Analyze | yes | yes | yes |
| Clauses | Clause manager | yes | no | no |
| Settings | Settings | yes | yes | yes |
| About | About & Help | yes | yes | yes |

`ProfileFeatureMatrix` is the single source of truth for this table; tests assert every cell.

## Where the choice is made

- **First run:** language and theme, then the profile cards. Skip, or finishing without a card,
  saves Lawyer. The choice is written immediately when the user leaves the profile step.
- **Settings → Profile:** the same three choices in a card. Changing one updates the sidebar
  immediately and shows a short confirmation; it never requires a restart.

## Rules

- **Existing installs have no profile.** A missing/profile value `null` behaves exactly like
  `Lawyer`, so upgrading never removes a page. First-run onboarding offers the three cards, and
  Settings always shows the current choice; skipping onboarding keeps Lawyer.
- **Switching never deletes data.** Hiding a page only removes its sidebar entry and navigation
  target; contracts, clauses and other saved data remain in the database and return if the user
  switches back.
- **Navigation is gated, not just the sidebar.** Navigating to a hidden page key falls back to the
  Dashboard, and keyboard shortcuts for hidden pages do nothing.
- **Business notice.** Business users see a persistent notice on Dashboard, New Contract and
  Analyze: general information and drafting tools, not legal advice; consult a licensed lawyer.
  Lawyer and Mediator users do not see it.
- **Mediator note.** Mediator users see a short dashboard note that a mediation workspace is
  planned. No new page or feature is implied or built.
- Profiles are included in settings backup/restore and in "Export my data" whenever settings are
  included; the private signing key and unrelated secrets are unaffected.
