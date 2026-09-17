# DEV-026 — Gmail mailbox label audit (label ↔ project table)

> **Title:** Cutover audit — sortable table of the signed-in mailbox’s Gmail labels vs SiNet projects  
> **Date:** 13.08.2026  
> **Updated:** 17.09.2026  
> **Status:** Active (1.0.42 — ProjectNumber identity, misplaced move, duplicate merge)  
> **Scope:** Signed-in Gmail mailbox only. Existing **«בדיקת תיוג»** window is Label Management. No SQL schema. No second organizer window.  
> **Backlog:** [`DEV_BACKLOG.md`](./DEV_BACKLOG.md)  
> Related: [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) · [`EMAIL_GMAIL_LABEL_CATALOG.md`](./EMAIL_GMAIL_LABEL_CATALOG.md) · [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) §4 (DEV-009)

---

## 1. Product (locked)

The deliverable is a **sortable table** of **all user labels** in the connected mailbox. Each row shows which SiNet project that label maps to (or empty).

| Situation | Product |
| --- | --- |
| Label with no project number / no SiNet project | **OK** — empty project columns |
| SiNet project with no Gmail label | **OK** — not listed as a gap; not a row to create |
| Two or more labels mapping to the **same** `Project.Number` | **Not OK** — note on every row in the group |

1.0.42 **does** rename a **misplaced** unique project label (same `LabelId`) and **merge** duplicates (attach → verify → delete source). It does **not** auto-create missing project labels from this window and does **not** write Place titles.

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
| `IEmailGmailModifyService.RenameLabelAsync` / `MergeProjectLabelsAsync` | Misplaced move (same LabelId); duplicate merge |

## 3. Table

**Rows:** every Gmail **user** label. Skip system labels (`INBOX`, `SENT`, `DRAFT`, `SPAM`, `TRASH`, `UNREAD`, `STARRED`, `IMPORTANT`, `CATEGORY_*`, and API `Type=system`). Include `OfficeSystem_*`, the office root, and personal labels outside the tree.

**Columns (all sortable):**

| Column | Source |
| --- | --- |
| תווית | Gmail `Name` (current full path) |
| מספר פרויקט | Parser on leaf, or empty |
| פרויקט | `ProjectLabelName` / name from SiNet, or empty |
| יישוב | First segment under the root, if any |
| נתיב צפוי | Canonical `Root/place/(Number)…` when a SiNet project matches |
| סטטוס | Correct / Misplaced / Duplicate / Unknown |
| הודעות | `MessagesTotal` from the catalog snapshot (no body scan) |
| הערה | Duplicate (required); optional: number not in SiNet; `(Number)` outside root; place one-character drift vs catalog |

Default sort: duplicate notes first, then project number, then label name. Local text filter above the grid. Copy selected / copy all **label paths** (not a “missing labels” list).

**Duplicate identity (1.0.42):** two or more **project labels under `RootLabel`** whose leaf `(Number)` is the same. Path is ignored. Labels **outside** the root are not part of this uniqueness set. A unique leaf whose path ≠ expected path is **Misplaced**, not a duplicate.

## 4. Entry

Button **«בדיקת תיוג»** on the email filter bar (next to «סנכרן שמות לייבלים»). If Gmail is not connected: `Gmail לא מחובר. התחבר ונסה שוב.` — no scan, no second Gmail client.

## 5. Out of scope

- Creating missing labels from this window (`GetOrCreate` stays on the filing path)
- Writing `Place.Title`
- Auto-choosing a duplicate survivor or combining merge + move in one click
- Scanning anyone else’s mailbox
- Treating SQL `ProjectId` as filed

## 6. Dropped / postponed

| Item | Status | Why |
| --- | --- | --- |
| “What to add” / copy missing folder names as the primary product | Dropped from v1 | Operator asked for a label→project table; projects without labels are OK |
| Auto-fix Place catalog from a close Gmail folder name | Postponed | Suggestion-only note; catalog stays SoT until an explicit write is approved |
| Auto-create the office root / place / leaf tree | Postponed | Copy of **existing** label names only |

## 7. Code anchors (target)

- Parser / index: `EmailProjectLabelParser.TryParseProjectLabel`, `GmailProjectLabelIndex`, `GmailProjectLabelResolver`
- Catalog: `GmailLabelCatalog.GetProjectLabelIndex` (derived from snapshot; session-isolated)
- GetOrCreate: `GmailEmailModifyService.GetOrCreateProjectLabelAsync(..., projectNumber)`
- Merge: `GmailProjectLabelMerge` (attach → verify → delete source)
- Matcher: `src/SiNet.Application/Email/GmailMailboxLabelAuditMatcher.cs`
- Port: `IGmailMailboxLabelAuditService`, `IEmailGateway.GetAllUserLabelsAsync`
- UI: existing `GmailMailboxLabelAuditWindow` from `EmailListViewModel` (move + merge)
- Tests: identity resolver, merge safety, matcher Correct/Misplaced/Duplicate, catalog index refresh/session

## 8. Needs Review

- Operator verify on a real mailbox after DEV merge: duplicate grouping, system labels absent, connect gate.
