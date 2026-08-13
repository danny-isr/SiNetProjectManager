# Email / ACC — Source of Truth (agent entry)

> **Status:** Approved principles summary (2026-07-20).  
> **Canonical domain docs:**  
> [`EmailSystemPrinciples` §6.1 / §6.5 / §6.6](../SiNetProjectManagerV2/Docs/Domains/Email/EmailSystemPrinciples-2026-05-26.md) ·  
> [`AccSystemPrinciples`](../SiNetProjectManagerV2/Docs/Domains/ACC/AccSystemPrinciples-2026-05-26.md)  
> **Ops / migration detail:** [`EMAIL_LIST_MIGRATION.md`](./EMAIL_LIST_MIGRATION.md) · [`ACC_BOUNDARY.md`](./ACC_BOUNDARY.md) · Llog when SoT writes fail: [`LOGGING_MATERIAL_FAILURES.md`](./LOGGING_MATERIAL_FAILURES.md)

## Three non-negotiable rules

| Concern | Source of truth | Database role |
| --- | --- | --- |
| Mailbox “email filed to project” (`IsFiledToProject`, File / Unfile / Move gate, **list badge «משויך»**) | **Gmail project label** under `פרויקטים_משרד/...` (`EmailGmailLabelNames.IsProjectLabel`) | Best-effort mirror after Gmail label attach — **not** proof of filing |
| Physical file present in ACC / Inbox | **ACC** item / version / folder (reconcile / browse / download) | `AccItemId` etc. are **cache/helper only** |
| Inbox tag / move / lock metadata on an ACC item | **ACC custom attributes** | DB mirror is helper only |

Business process (projects, tasks, workflow structure, `ProjectFile` tree) remains a **DB** concern — that does **not** override the rows above.

### List UI: «משויך» / «לא משויך»

- Green **«משויך»** on the email list card means the message has a **Gmail project label** (`IsFiledToProject`), not merely `EmailInboxMessage.ProjectId` or thread mapping in SQL.
- **`LinkedProjectBadge`** shows the project leaf / number from that Gmail label (parser), not a bare SQL id.
- SQL `ProjectId` alone → **«לא משויך»** (optional muted “קישור במסד” hint only — never green «משויך»).
- Quick **«שייך לפרויקט»** in the email action bar files via `IEmailFilingService` with an explicit `TargetProjectId` and **must not** change the app’s global `ICurrentProjectContext`.
- Detail viewer shows **all** user Gmail labels as chips (system labels filtered); `OfficeSystem_Personal` / triage labels sort last; not filtered by the active shell project.

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
- Mailbox label audit table (DEV-026): [`DEV_PLAN_GMAIL_LABEL_CUTOVER_AUDIT.md`](./DEV_PLAN_GMAIL_LABEL_CUTOVER_AUDIT.md) — read-only list of **this mailbox’s** user labels mapped by `(Number)`; duplicate labels for one project are a note only (not keep/delete)

## Mailbox label audit (DEV-026)

- Entry: Email window **«בדיקת תיוג»** after Gmail is connected (same connect gate as the list).
- Product: sortable table — one row per **user** Gmail label, columns for mapped SiNet project. A label without a project is OK. A project without a label is OK and is **not** listed as something to create.
- The only required defect: two user labels whose leaf `(Number)` maps to the same `Project.Number`. This window does **not** rename, merge, or delete (DEV-009 keep/delete stays separate).

## FileMaterial / MoveToProject (six decisions)

**Canonical Target:** [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md) (full flow, success/open gates, TotalCount, dismiss, locate, dropped mechanisms).

Summary aligned with SoT above:

| Concern | Source of truth |
| --- | --- |
| Already filed to project folder | ACC Move/Lock custom attributes — **verify target ids** before treating as success |
| Physical presence in Inbox | ACC (reconcile / recovery); DB `AccItemId` is cache only |
| Task / window close | Files verified **and** `CompleteAsync` with `TaskClosed` **and** workflow advance not pending (not `AllFilesTransferred` alone) |
| Mailbox association | Unchanged — Gmail project label |
| Email body PDF | Optional tagged `00_Email.pdf` only — never automatic required material |

## Ops: failures of these SoT writes must reach Llog

Gmail File/Unfile and ACC physical upload/Move are the operations this page defines. If they fail, operators on the PROD workstation must see Warning/Error on the central share — not only UI Status or `System.Diagnostics.Trace`. As-Is gaps (e.g. `SqlEmailFilingService` returning `EmailFilingResult(false)` without `IAppLogger`, MoveToProject Trace-only): [`LOGGING_MATERIAL_FAILURES.md`](./LOGGING_MATERIAL_FAILURES.md). Do not “fix” diagnosability by treating SQL `ProjectId` as filed.
