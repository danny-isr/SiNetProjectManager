# Project Documentation Rules

When working on documentation or making meaningful changes in this project, adhere strictly to the following rules:

1. **Documentation-first rule**: If a change is meaningful (alters mechanics, UI, config, permissions, external dependencies, or removes features), check and update the documentation in the same round. If no update is needed, explicitly state in the report: `Documentation checked — no update required`.
2. **Every new document must be indexed**: Whenever a new document is added under `Docs/`, the main index `SiNetProjectManagerV2/Docs/README.md` MUST be updated. Do not just update the local domain README.
3. **Where to link documents**: Update the main index, the local domain README (if it exists), and add cross-references to related Principles/Catalog documents. Do not duplicate large chunks of text between documents. Do not update `Docs/Archive` as an active source.
4. **Required metadata**: Every document must start with a Title, Date, Status (Active, Draft, Documentation-only, Planning, Superseded, Archived), and Scope.
5. **Documentation-only rounds**: If a round is "Documentation Only" or "Documentation First", ABSOLUTELY NO CODE CHANGES ARE ALLOWED. No XAML, ViewModel, DB, DI, testing, or class changes. You may only create/update documents. Your final report must explicitly state: `No code changes were made.`
6. **Document existing mechanisms before proposing new ones**: Before proposing a new mechanism (e.g., UI, Window, Framework), document the `Existing mechanism` covering files, classes, refresh loops, models, etc. Reuse the existing mechanism if possible.
7. **Out of scope section**: Every significant document or plan must include an explicitly named `Out of Scope` section detailing what is intentionally excluded from the round.
8. **Dropped / cancelled / postponed section**: Every document must include a `Dropped / Cancelled / Postponed` (או בעברית: דברים שירדו / בוטלו / הושהו) section explaining what was removed/delayed and why. Do not silently delete mechanisms without documenting them.
9. **Use precise setting keys and code names**: Do not guess configuration keys or class names. Look them up in the code. If unsure, mark as `Needs Review`.
10. **Documentation cross-reference rule**: When extending an existing document via a new document, add short bidirectional pointers instead of copying content.
11. **README update requirements**: When updating `Docs/README.md`, update the `Updated:` date, add the new document to the appropriate table, and preserve the reading structure. Do not move things to Archive without permission.
12. **No Archive edits as active documentation**: `Docs/Archive` is historical. Do not update it as a source of truth. If documents conflict, the one in `Domains` or `Decisions` wins.
13. **End report format**: At the end of a documentation round, report: what was created/updated, whether `Docs/README.md` was updated, local README updates, cross-references added, code changes (must be none), DB changes, Out of Scope items, Dropped/Postponed items, Needs Review items, and recommended next steps.

## Email / ACC source of truth

Before changing email filing, MoveToProject eligibility, or ACC Inbox presence logic, read:

- `docs/EMAIL_ACC_SOURCE_OF_TRUTH.md`
- `SiNetProjectManagerV2/Docs/Domains/Email/EmailSystemPrinciples-2026-05-26.md` (§6.1, §6.6)
- `SiNetProjectManagerV2/Docs/Domains/ACC/AccSystemPrinciples-2026-05-26.md`

**Mailbox filed = Gmail project label only. Physical file = ACC. DB = helper.** Do not “fix” Move/File by treating SQL `ProjectId` as filed.
