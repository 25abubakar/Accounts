# Project implementation conventions

- Follow the user's requested business fields and labels exactly. Do not introduce visible fields, options, or workflows without confirming them.
- Preserve the existing Accounts UI/UX and shared grid components unless the user explicitly requests a design change.
- Forms are collapsed by default unless the user explicitly requests otherwise. A closed form uses a plus/Show action; an open form uses a minus/Hide action.
- Proactively verify form toggle states, validation, loading, empty/error states, save/edit refresh, responsive layout, permissions, API contracts, database persistence, builds, and focused tests.
- When a requirement is materially ambiguous, point out the missing decision and ask before implementing business behavior.
