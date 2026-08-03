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
| DEV-003 | ProjectWork tree: exclude `.bak`, recover UX (orange/green), ignored folders, preserve expand, Collapse all | Planning | P1 (pilot) | [`DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md`](./DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md) | Yes — after slices A–G ship |
| DEV-004 | Mark email as read in Gmail when the body is opened (session toggle, default on in Release) | Planning | P1 (pilot) | [`DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md`](./DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md) §3 | With that feature ship |
| DEV-005 | «פתח ב-Gmail» button — reply / forward handled by Gmail, no in-app composer | Planning | P2 | [`DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md`](./DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md) §4 | With that feature ship |

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
