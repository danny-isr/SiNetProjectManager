# DEV backlog — open defects and implementation requests

> **Title:** Development backlog index (App.Wpf pilot gaps)  
> **Date:** 03.08.2026  
> **Updated:** 03.08.2026  
> **Status:** Active  
> **Scope:** Single index of **open** product/engineering items for the `development` branch. Each item points to a focused doc (bug / planning). Implement on DEV; absorb into `release` only after PROD acceptance. Work the list top-down; mark Done / remove rows as slices land. Not a substitute for GitHub Issues — use both if desired.

Related: [`ENVIRONMENTS.md`](./ENVIRONMENTS.md), [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md), [`AGENTS.md`](../AGENTS.md).

---

## 1. How to use

1. Add a row here when PROD/ops discovers a gap that needs code on **`development`**.
2. Create a dedicated doc under `docs/` (or extend an existing Planning doc).
3. DEV agent: read the item doc → docs-first risk note → implement → bump desktop version per [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) when shipping to the share.
4. After ship + PROD verify: set Status to **Done** and move the row to §3.

## 2. Open items

| ID | Title | Status | Priority | Doc | Requested version bump |
| --- | --- | --- | --- | --- | --- |
| DEV-001 | Email body links navigate in-place; Jumbo/WeTransfer must open external download window → ACC | Fixed on `development` — awaiting PROD publish + verify | P1 (pilot) | [`DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md`](./DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md) | Done — `SiNet.App.Wpf` 1.0.4 |
| DEV-002 | Admin startup alerts (sync / AccService token / ops) | Planning | P2 | [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) | With that feature ship |
| DEV-003 | ProjectWork tree: `.bak` exclude, hide stale recover, green newer recover, delete stale (not orphans), preserve expand, Collapse all (ignore-folders postponed) | Fixed on `development` — awaiting PROD publish + verify | P1 (pilot) | [`DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md`](./DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md) | Done — `SiNet.App.Wpf` 1.0.6 |
| DEV-006 | ProjectWork editable scan exclusions (extensions + `~$` locks) via System Settings | Implemented on `development` — awaiting PROD publish + verify | P1 | [`DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md`](./DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md) §3 | Done — `SiNet.App.Wpf` 1.0.8 |
| DEV-007 | Open-with by extension (ACC / Drive / Windows Shell) | Planning (direction approved) | P2 | [`DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md`](./DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md) §4 | With that feature ship |
| DEV-004 | Mark email as read in Gmail when the body is opened (session toggle, default on in Release) | Fixed on `development` — awaiting PROD publish + verify | P1 (pilot) | [`DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md`](./DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md) §3 | Done — `SiNet.App.Wpf` 1.0.5 |
| DEV-005 | «פתח ב-Gmail» button — reply / forward handled by Gmail, no in-app composer | Fixed on `development` — awaiting PROD publish + verify | P2 | [`DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md`](./DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md) §4 | Done — `SiNet.App.Wpf` 1.0.5 |
| DEV-008 | Project edit dialog + verified rename (FS/ACC/Drive→DB); dashboard double-click; create parity (worker + bid) | Implementing | P1 | [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) | Layer A done; Layer B/C in progress |
| DEV-009 | Gmail project label identity by `(Number)` + `Email.AutoSyncProjectLabelNames` (per mailbox); duplicate decision UI | Implementing | P1 | [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) §4 · [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) | Layer B keep/delete dialog |
| DEV-010 | «דוח קריסות תחנה» — local Event Log crash report (Civil 3D + machine), CSV + Markdown for AI, shared folder per machine | Implementing | P2 | [`DEV_PLAN_WORKSTATION_CRASH_REPORT.md`](./DEV_PLAN_WORKSTATION_CRASH_REPORT.md) | With that feature ship |
| DEV-011 | Job-type remove: strong warning, no workflow hard-delete, orphan-track mark + data-integrity list | Implementing | P2 | [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) §5 | Warning + `[ORPHAN-TRACK]` + Ops filter done; broader integrity checklist later |
| DEV-012 | ProjectWork: show disk-only folders/files, purple/gray colors, delete empty user folders only | Implemented on `development` — awaiting PROD publish + verify | P1 | [`DEV_PLAN_PROJECTWORK_DISK_FOLDERS.md`](./DEV_PLAN_PROJECTWORK_DISK_FOLDERS.md) | With that feature ship |
| DEV-013 | ProjectWork lazy scan: expand-on-demand, unload on collapse, probe colors, DOP-4 parallel IO | Implemented on `development` — awaiting PROD publish + verify | P1 | [`DEV_PLAN_PROJECTWORK_LAZY_SCAN.md`](./DEV_PLAN_PROJECTWORK_LAZY_SCAN.md) | With that feature ship |
| DEV-014 | Crash report round 2: incident grouping (fix «incidents per day»), WHEA bank/address payload, BIOS+DIMM facts — then context form, plugin inventory, CER/WER/dump index | Ship 1 (C+B+A) implemented on `development` — Ship 2 pending | P2 | [`DEV_PLAN_WORKSTATION_CRASH_DEEP_DIAGNOSTICS.md`](./DEV_PLAN_WORKSTATION_CRASH_DEEP_DIAGNOSTICS.md) | Two ships, one bump each |

## 3. Done / cancelled

| ID | Title | Outcome | Date |
| --- | --- | --- | --- |
| — | — | — | — |

## 4. Out of Scope

- Tracking every historical V2 parity gap (use domain docs / migration maps)
- Editing `Docs/Archive` as active backlog
- Auto-creating GitHub Issues from this file (manual optional)

## 5. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Separate `docs/bugs/` folder tree | Postponed | Keep flat `docs/DEV_*.md` + this index for discoverability |

## 6. Needs Review

- Whether PROD wants GitHub Issues mirrored 1:1 with this index.
