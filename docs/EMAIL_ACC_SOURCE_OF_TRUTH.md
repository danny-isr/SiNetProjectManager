# Email / ACC — Source of Truth (agent entry)

> **Status:** Approved principles summary (2026-07-20).  
> **Canonical domain docs:**  
> [`EmailSystemPrinciples` §6.1 / §6.5 / §6.6](../SiNetProjectManagerV2/Docs/Domains/Email/EmailSystemPrinciples-2026-05-26.md) ·  
> [`AccSystemPrinciples`](../SiNetProjectManagerV2/Docs/Domains/ACC/AccSystemPrinciples-2026-05-26.md)  
> **Ops / migration detail:** [`EMAIL_LIST_MIGRATION.md`](./EMAIL_LIST_MIGRATION.md) · [`ACC_BOUNDARY.md`](./ACC_BOUNDARY.md)

## Three non-negotiable rules

| Concern | Source of truth | Database role |
| --- | --- | --- |
| Mailbox “email filed to project” (`IsFiledToProject`, File / Unfile / Move gate) | **Gmail project label** under `פרויקטים_משרד/...` (`EmailGmailLabelNames.IsProjectLabel`) | Best-effort mirror after Gmail label attach — **not** proof of filing |
| Physical file present in ACC / Inbox | **ACC** item / version / folder (reconcile / browse / download) | `AccItemId` etc. are **cache/helper only** |
| Inbox tag / move / lock metadata on an ACC item | **ACC custom attributes** | DB mirror is helper only |

Business process (projects, tasks, workflow structure, `ProjectFile` tree) remains a **DB** concern — that does **not** override the rows above.

## Forbidden (do not “fix” this way)

- Inferring “משויך לפרויקט” / clearing `MoveBlockReason` from SQL `EmailInboxMessage.ProjectId`, thread mapping, or workflow association alone.
- Helpers like `IsEffectivelyFiled*` that OR SQL state into `IsFiledToProject`.
- Treating a stored `AccItemId` as proof the file still exists in ACC without reconciliation.
- Using Gmail as a Storage Destination / writing file **content** back to Gmail.  
  **Allowed:** Gmail **label** modify for project filing/unfiling and triage (`OfficeSystem_*`) — that write **is** the mailbox association SoT.

## Context (why this page exists)

During FileQuoteMaterial QA (2026-07), a proposed fix treated SQL `ProjectId` as “already filed” so Move could proceed without a Gmail project label. That approach was **rejected**. Mailbox association stays Gmail-label-only; if File fails, surface the real Gmail error — do not bypass the label.

## Project label identity (per mailbox)

- Gmail project labels live **per user mailbox** — they are not a shared office tree. A centralized project rename **must not** rename labels for all users.
- **Identity** of a project leaf label is the **number in parentheses at the start of the leaf name** (`^\((\d+)\)` → `Project.Number`), not the full display string after the number. Parser: `EmailProjectLabelParser`.
- Leaves are only considered under the configured root / place hierarchy (`Gmail.RootLabel` / `פרויקטים_משרד/...`).
- Optional SystemSetting **`Email.AutoSyncProjectLabelNames`**: when on, for the **signed-in mailbox only**, rename leaf labels whose `(Number)` matches a project so the leaf equals current `NameAndNumber`. Duplicate numbers in one mailbox require an **explicit keep/delete decision UI** (no silent merge; warn-only MessageBox is insufficient — see [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) §4.1).
- **Label change journal:** per-mailbox JSON under `%LocalAppData%\SiNet\GmailLabelJournal\` logging `LabelId` + old/new full path for renames/deletes performed by SiNet, retained **at most 30 days**. On **delete / duplicate merge**, also store the Gmail **message id list** that had that label before removal (mandatory capture; fail closed if list or journal write cannot be obtained). See plan §4.2.
- When the setting is off, filing/association still uses `(Number)`; leaf titles may lag after a project rename until the user syncs.

## Code anchors

- Label predicate: `src/SiNet.Application/Email/EmailGmailLabelNames.cs`
- Number extract: `EmailProjectLabelParser.TryExtractProjectIdFromDisplaySegment` (maps to `Project.Number`)
- Row mapping: `EmailListRowMapper` → `IsFiledToProject` from Gmail `labelNames`
- Filing order: `SqlEmailFilingService` — Gmail attach first, SQL sync best-effort, compensate by removing label if SQL fails
- Move gate: `EmailDetailViewModel` passes `_selectedEmail.IsFiledToProject` into eligibility
- ACC move: `NativeEmailMoveToProjectExecutor` verifies ACC; Move/Lock attributes are SoT for “already moved”
- Label name sync: `IProjectGmailLabelSyncService` (DEV-009)
