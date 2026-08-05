# DEV plan — Project edit + verified rename (+ label sync + type orphan)

> **Title:** Project edit / rename / label sync / type-removal orphans  
> **Date:** 05.08.2026  
> **Updated:** 05.08.2026 (closure plan A–C after code audit)  
> **Status:** Implementing  
> **Backlog:** DEV-008 (edit/rename), DEV-009 (Gmail label sync), DEV-011 (type-removal orphan + data integrity — new)  
> **Related:** [`PROJECTS.md`](./PROJECTS.md), [`PROJECTS_DASHBOARD.md`](./PROJECTS_DASHBOARD.md), [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md), [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md)

V2 `WindowEditProject` / `ProjectRenameService` remain **behavior reference only** — not hosted.

---

## 1. Product goal

- Double-click a row in «ריכוז פרויקטים» opens **עדכון פרויקט** (new-system dialog).
- Toolbar «פתח פרויקט» keeps Current Project + Project Work browse.
- Edit metadata, job types (add/remove), per-type admin worker + contract value (`Bid.BidValue`).
- Project **number** is immutable.
- **Rename** is a dedicated flow: FileServer → ACC Docs folder → Drive project root → only then DB `Title` (`NameAndNumber` trigger).
- Gmail labels are **not** part of centralized rename (per-user mailboxes). Identity = `(Number)` at leaf start; optional auto-rename when `Email.AutoSyncProjectLabelNames` is on.
- Removing a job type from a project **must not** hard-delete workflow instances. Warn first; mark orphans; repair later in a maintenance surface.

---

## 2. Current implementation status (audit 05.08.2026)

| Piece | Status |
| --- | --- |
| Edit dialog + dashboard double-click + `Project.Update` | Done |
| Create parity (worker + bid + «למי הוגש») | Done |
| FileServer rename / create | Done |
| Google Drive root rename | Done (`GoogleDriveProjectRootRenameService`) |
| ACC Docs rename | **Done** — connector + Local + AccService Remote (`POST .../folders/{id}/rename`); deploy AccService binary before PROD Remote rename |
| DB `Title` after storage success | Done |
| Orchestrator order ACC→Drive→FS→DB | Done (Layer A) |
| Drive missing → Skipped | Done |
| Gmail sync by `(Number)` + setting + menu button | Mostly done |
| Gmail duplicate decision UI (merge / keep / delete) | **Missing** — warn-only MessageBox |
| Job-type remove warning + orphan mark | **Done** — confirm in edit save; Notes `[ORPHAN-TRACK]`; Ops filter «מסלול יתום (סוג הוסר)» |
| Data-integrity / maintenance window | **Missing** |

**Historical defect (fixed in Layer A):** older builds ran FileServer before ACC; ACC hard-failed and left local disk ahead of ACC/DB. Order is now ACC → Drive → FileServer → DB.

---

## 3. Centralized rename checklist (target)

| Step | Target | Success criteria |
| --- | --- | --- |
| 1 | FileServer | `Directory.Move` old → new under place (or create if missing) |
| 2 | ACC Docs | Rename folder by id (`AccTargetFolderId`); refresh `AccTargetFolderPath` (id stable) |
| 3 | Google Drive | Rename project root under ProjectsRoot; **missing source folder → Skipped** (not Failed) |
| 4 | DB | `Title` update → `NameAndNumber` trigger |
| — | Gmail | **Out of this orchestrator** |

### 3.1 Failure / repair rules

1. On any **required** storage failure: stop, report per-step results, **do not** update DB.
2. Prefer **order that reduces split-brain**: ACC (and Drive) before FileServer when ACC mapping exists — **or** rollback FileServer when ACC fails and the old path is free.
3. If rollback is unsafe (destination occupied / files locked): leave storages as-is, **do not** update DB, show a clear **manual-repair** message listing each step status and the two path names.
4. Drive folder not found under ProjectsRoot: **Skipped** with message (create-on-demand is out of rename scope).

### 3.2 ACC rename (Layer A — P0)

ACC is a shared office store — rename **must** be part of the centralized checklist.

**Required capability (sibling + host):**

- Autodesk Data Management: rename folder (PATCH) — **added** as `Bim360Service.RenameFolderAsync` in `SiOffice.AutodeskConnector`.
- Application port `IAccFolderRenameService` — **added**.
- Local mode wired through `Bim360AccTransferConnector` + `LocalAccFolderRenameService`.
- `ProjectRenameOrchestrator` order: **ACC → Drive → FileServer → DB**; Drive missing → Skipped.
- AccService remote HTTP mirror — **Done** (`POST /v1/acc/projects/{projectId}/folders/{folderId}/rename` + `RemoteAccFolderRenameService`).

**Ops follow-up:** bump AutodeskConnector pin after committing the sibling (`BUILD_SIBLING_PINS.md`). Deploy AccService with the rename endpoint wherever hosts use `AccServiceMode.Remote` before treating rename as pilot-ready there.

---

## 4. Gmail label sync (DEV-009 — Layer B)

- Match leaves under configured root/place tree whose name starts with `^\((\d+)\)`.
- Map digits to `Project.Number`.
- If `Email.AutoSyncProjectLabelNames` (or explicit «סנכרן שמות לייבלים») and leaf ≠ current `NameAndNumber` → rename leaf (same number, updated title).
- Runs on email enter / label refresh / explicit sync — **never** inside project rename.

### 4.1 Duplicate `(Number)` in one mailbox — target UX

Today: keep/delete dialog (`GmailDuplicateLabelDecisionDialog`) when multiple leaves share `(Number)`. **Target (shipped in Layer B):**

1. List every leaf that shares the same `(Number)`.
2. User chooses per conflict group:
   - **Keep one, delete the others** (no silent merge).
3. After the decision, re-run sync for that number so the survivor matches `NameAndNumber` when auto-sync is on.
4. DB number ambiguity (one leaf, multiple project rows) remains informational MessageBox — not a Gmail delete action.

### 4.2 Label change journal (approved — implemented)

**Goal:** After auto-sync rename or duplicate keep/delete («מיזוג»), the operator can see what changed in *their* mailbox and (where possible) reverse renames / re-attach after delete. Not a full Gmail backup; not a shared office store.

**Status:** Writer + 30-day prune + mandatory `MessageIds` before delete — **shipped**. Undo UI for rename/delete — later.

**Storage (per mailbox email, not per Windows login):**

| Item | Value |
| --- | --- |
| Path | `%LocalAppData%\SiNet\GmailLabelJournal\{sanitized-mailbox-email}.json` |
| Format | Single JSON file per mailbox |
| Retention | Keep entries with `UtcNow - ChangedAtUtc ≤ 30 days`; prune older on every write (and optionally on read). **Hard cap: 30 days** — no longer history. |

**Each journal entry records:**

| Field | Meaning |
| --- | --- |
| `LabelId` | Gmail label id (stable key while the label exists) |
| `Action` | `Renamed` \| `Deleted` |
| `OldFullPath` | Full label path before the change |
| `NewFullPath` | Full path after rename; `null` on delete |
| `ProjectNumber` | Digits from `^(Number)` when known |
| `ChangedAtUtc` | When SiNet performed the change |
| `Source` | `AutoSync` \| `ManualSync` \| `DuplicateResolve` (keep-one / delete-others — product language «מיזוג»; journal action remains `Deleted`) |
| `MessageIds` | **Required on `Deleted` only** (including duplicate «merge»): Gmail message ids that had this label **immediately before** delete (empty array if none). Omit / empty on rename. |

**Delete / merge capture rule:** Before calling `DeleteLabelAsync` (whether from an explicit delete or from duplicate keep/delete «merge»), list all messages with that `labelId` (Gmail `users.messages.list`, paginate) and persist their ids on the journal entry, then delete the label. If listing fails, **do not delete** (fail closed) — operator must see the error; better than losing association without a record. Cap / soft limit: if a label has an extreme message count, still attempt full pagination; if Gmail quota/timeout aborts, fail closed and do not delete.

**Write points:** immediately after a successful `RenameLabelAsync`, or after a successful pre-delete message list + `DeleteLabelAsync`, from `ProjectGmailLabelSyncService` (and resolve). Failures of journal I/O after a successful Gmail change must be logged and **must not** invent a rollback of Gmail; for delete, prefer writing the journal entry (with message ids) **before** the Gmail delete call so a crash mid-delete still leaves a usable record (mark entry `PendingDelete` → `Deleted` or write once after both succeed — prefer: list → write journal as `Deleted` with ids → delete label; if delete fails, append a follow-up or leave entry with note that label may still exist).

**Restore scope (v1):**

- **Rename:** UI/command can rename back `NewFullPath` → `OldFullPath` using `LabelId` when the label still exists (best-effort).
- **Delete:** journal keeps `LabelId`, old path, **and `MessageIds`**. Recreate-label + re-attach from that list is a **follow-up** (not required in first code slice of the journal writer); without `MessageIds` full restore is impossible — that is why listing is mandatory before delete.

**Out of scope for this slice:** exporting the entire label tree snapshot; cloud sync of the journal; DB table; retention setting UI (fixed 30 days unless later approved); Undo UI for delete (journal + message ids first).

**Complexity / risks:** medium (local JSON + prune + message list pagination before delete). Risk: wrong mailbox file if email not yet known — gate writes on connected account email. Risk: large labels → slower delete + bigger JSON (accept; prune after 30 days). Risk: users expect instant Undo delete — v1 is audit + data for later restore.

---

## 5. Job-type remove + orphan workflows (DEV-011 — Layer C)

### 5.1 What exists today

`SqlProjectUpdateService.SaveAsync` removes unselected `TypeOfProjectInProject` and matching `Bid` rows. It does **not** touch `WorkflowInstance`. There is no confirmation. Catalog JobType has no delete API; `WorkflowInstance.JobTypeId` FK is `Restrict`.

### 5.2 Target policy (approved)

1. Before removing one or more types from a project, query active/paused `WorkflowInstance` rows for that `ProjectId` + those `JobTypeId`s (and linked open tasks if cheap).
2. If any exist: **strong warning** — removing a type is significant; **not recommended**; workflows will **not** be deleted.
3. If the user cancels → abort the whole save (or abort only the type removals — prefer abort whole save for simplicity).
4. If the user confirms → remove type links + Bids as today; **do not** cancel/delete workflow instances.
5. Mark affected instances as **orphaned track** (type no longer assigned):
   - **Shipped:** prepend `[ORPHAN-TRACK]` to `WorkflowInstance.Notes` (no schema migration).
   - Ops: filter «מסלול יתום (סוג הוסר)» on Workflow Ops dashboard.
6. **Do not** cascade-delete `WorkflowDefinition` or catalog `JobType`.

### 5.3 Maintenance / data-integrity window (partial in Layer C)

**Shipped:** orphan-track filter in «בריאות תהליכים».

**Still later (out of Layer C minimum):** dedicated integrity checklist for ACC/FS/Drive/Gmail mismatches.

---

## 6. Create parity

`ProjectCreateDialog` captures per selected job type: admin worker + contract value; optional «למי הוגש» (`ApproveDescription`). Already implemented — keep covered by tests.

---

## 7. Ports / feature codes

| Port / key | Role |
| --- | --- |
| `IProjectUpdateService` | Load/save edit DTO |
| `IProjectRenameOrchestrator` | Analyze + execute checklist |
| `IProjectDriveRootRenameService` | Drive ProjectsRoot rename |
| `IProjectGmailLabelSyncService` | Per-mailbox leaf sync |
| `IAccFolderRenameService` (or extended `IAccItemService`) | **New** — ACC folder rename |
| Feature `Project.Update` | Edit + rename UI |
| Setting `Email.AutoSyncProjectLabelNames` | Auto leaf rename |

---

## 8. Closure layers (ship order)

| Layer | Slice | Gate |
| --- | --- | --- |
| **A — P0** | ACC rename API + orchestrator wiring; FS/ACC ordering or rollback; Drive missing → Skipped; DI verified | Safe rename on ACC-mapped project |
| **B — P1** | Duplicate-label decision dialog with keep/delete | **Done** — `GmailDuplicateLabelDecisionDialog` + `ResolveDuplicateLeavesAsync` |
| **C — P2** | Type-remove warning + orphan mark; integrity list in Ops/maintenance | **Done** (orphan Notes + Ops filter; full ACC/FS integrity checklist later) |
| **D** | Commit/push; promote `development` → `release`; publish from **PROD** only | Pilot can test |

Do **not** publish rename to production until Layer A is done.

---

## 9. Out of scope

- Moving a project between Places (tree relocate of FS/ACC/Drive trees)
- In-grid status edits on the dashboard
- Hosting V2 `WindowEditProject`
- Hard-delete of workflow instances or definitions when removing a type
- Creating a missing Drive/ACC folder as part of rename (ensure belongs to create/provision)
- In-app AI analysis of crash reports (separate DEV-010)

---

## 10. Risk & complexity (Layer A)

| Risk | Mitigation |
| --- | --- |
| Rename API lives in pinned sibling AutodeskConnector / AccService | Implement API there first; bump `build/sibling-pins.json`; no invented REST in WPF |
| Autodesk DM rename permissions / locked folders | Surface connector error; leave DB unchanged |
| FS already moved (current bug) | Reorder or rollback before shipping |
| AccService remote vs local mode | Same port for both; remote HTTP mirror of local connector method |

**Effort (Layer A):** medium — sibling API + port + orchestrator + tests + one live ACC smoke on an `SI` place project (DEV only).

**Effort (Layer B):** small–medium — dialog + sync actions.  
**Effort (Layer C):** medium — warning in edit save + orphan signal + Ops list; schema only if a new table is approved (prefer Notes/flag first to avoid migration if possible).
