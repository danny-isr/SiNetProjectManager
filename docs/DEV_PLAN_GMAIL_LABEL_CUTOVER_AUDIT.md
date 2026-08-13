# DEV-026 — Gmail mailbox label audit (label ↔ project table)

> **Title:** Cutover audit — sortable table of the signed-in mailbox’s Gmail labels vs SiNet projects  
> **Date:** 13.08.2026  
> **Status:** Planning / Implementing  
> **Scope:** Signed-in Gmail mailbox only. Read-only scan + WPF table. No SQL schema. No label create/rename/delete. No Place catalog writes.  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) · [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) §4 (DEV-009)

---

## 1. Product (locked)

The deliverable is a **sortable table** of **all user labels** in the connected mailbox. Each row shows which SiNet project that label maps to (or empty).

| Situation | Product |
| --- | --- |
| Label with no project number / no SiNet project | **OK** — empty project columns |
| SiNet project with no Gmail label | **OK** — not listed as a gap; not a row to create |
| Two or more labels mapping to the **same** `Project.Number` | **Not OK** — note on every row in the group |

v1 does **not** create labels, does **not** update Place titles, and does **not** run DEV-009 rename / keep-delete.

Mailbox filed remains Gmail project label only. SQL `ProjectId` is not used as filing proof.

## 2. Existing mechanisms (reuse)

| Mechanism | Role |
| --- | --- |
| `IConnectorAuthService.IsAuthenticated` / Email «חבר Gmail» | Gate: do not scan until connected |
| `IEmailGateway` | New `GetAllUserLabelsAsync` — **all user labels**. Do **not** change `GetMailboxLabelsAsync` (still INBOX + root for the filter dropdown) |
| `EmailProjectLabelParser` (`^\((\d+)\)` on the leaf) | Label → `Project.Number` |
| `EmailGmailLabelNames.RootLabel` (`פרויקטים_משרד`) | Hierarchy notes only (outside-root, place segment) |
| `IProjectQueryService.SearchProjectsAsync` (`IncludeClosed: true`) | Resolve number → display name (closed projects still show) |
| `IPlaceCatalogService.ListAsync` | Optional place-similarity note only |
| `IProjectGmailLabelSyncService` / `GmailDuplicateLabelDecisionDialog` | **Not called** from this window |

## 3. Table

**Rows:** every Gmail **user** label. Skip system labels (`INBOX`, `SENT`, `DRAFT`, `SPAM`, `TRASH`, `UNREAD`, `STARRED`, `IMPORTANT`, `CATEGORY_*`, and API `Type=system`). Include `OfficeSystem_*`, the office root, and personal labels outside the tree.

**Columns (all sortable):**

| Column | Source |
| --- | --- |
| תווית | Gmail `Name` (full path) |
| מספר פרויקט | Parser on leaf, or empty |
| פרויקט | `ProjectLabelName` / name from SiNet, or empty |
| יישוב | First segment under the root, if any |
| הערה | Duplicate (required); optional: number not in SiNet; `(Number)` outside root; place one-character drift vs catalog |

Default sort: duplicate notes first, then project number, then label name. Local text filter above the grid. Copy selected / copy all **label paths** (not a “missing labels” list).

**Duplicate identity:** two user labels whose leaf `(Number)` maps to the **same existing** SiNet `Project.Number`. Labels that share a number with **no** matching project are not a duplicate group (optional “מספר לא במערכת” only).

## 4. Entry

Button **«בדיקת תיוג»** on the email filter bar (next to «סנכרן שמות לייבלים»). If Gmail is not connected: `Gmail לא מחובר. התחבר ונסה שוב.` — no scan, no second Gmail client.

## 5. Out of scope (v1)

- Creating missing labels (`GetOrCreateProjectLabelAsync`)
- Writing `Place.Title` or renaming a Gmail folder
- Opening DEV-009 keep/delete from a duplicate row
- Scanning anyone else’s mailbox
- Treating SQL `ProjectId` as filed

## 6. Dropped / postponed

| Item | Status | Why |
| --- | --- | --- |
| “What to add” / copy missing folder names as the primary product | Dropped from v1 | Operator asked for a label→project table; projects without labels are OK |
| Auto-fix Place catalog from a close Gmail folder name | Postponed | Suggestion-only note; catalog stays SoT until an explicit write is approved |
| Auto-create the office root / place / leaf tree | Postponed | Copy of **existing** label names only |

## 7. Code anchors (target)

- Matcher: `src/SiNet.Application/Email/GmailMailboxLabelAuditMatcher.cs`
- Port: `IGmailMailboxLabelAuditService`, `IEmailGateway.GetAllUserLabelsAsync`
- UI: `GmailMailboxLabelAuditWindow` from `EmailListViewModel`
- Tests: matcher (no Gmail) — empty project, mapped number, duplicate note on both rows, system labels excluded

## 8. Needs Review

- Operator verify on a real mailbox after DEV merge: duplicate grouping, system labels absent, connect gate.
