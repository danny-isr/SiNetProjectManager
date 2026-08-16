# DEV backlog — open defects and implementation requests

> **Title:** Development backlog index (App.Wpf pilot gaps)
> **Date:** 03.08.2026
> **Updated:** 16.08.2026 (DEV-028 Slice E: central level הערה)
> **Status:** Active / Operational Checklist
> **Classification:** Operational Checklist (engineering index; not rollout sign-off)
> **Scope:** Single index of product/engineering items for the `development` / `release` lines. Each item points to a focused doc. Not a substitute for GitHub Issues.
> **Reconciliation:** [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md)

Related: [`ENVIRONMENTS.md`](./ENVIRONMENTS.md), [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md), [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md), [`AGENTS.md`](../AGENTS.md).

**Baseline (updated 07.08.2026):** `origin/release` = `origin/development` = `127dc0e` · `SiNet.App.Wpf` **1.0.23**.
(Original reconciliation snapshot was `3bfe152` / **1.0.22** -- Historical; see [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md) §1.)
Ship commits on `release` (e.g. `chore(release): ship SiNet.App.Wpf 1.0.xx`) indicate **intent to publish to the UNC share**. That is **not** the same as:

- pilot machines confirmed on that MSIX, or
- [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md) phase sign-off.

Status vocabulary:

| Status | Meaning |
| --- | --- |
| **Open / Planning / Implementing** | Work still needed on `development` |
| **On release tip — ops verify Needs Review** | Code is on `release` @ baseline; operator must confirm install + behavior |
| **Done** | Code on release **and** PROD/ops verify recorded (move to §3) |
| **Superseded** | Replaced by another ID |

---

## 1. How to use

1. Add a row here when PROD/ops discovers a gap that needs code on **`development`**.
2. Create a dedicated doc under `docs/` (or extend an existing Planning doc).
3. DEV agent: read the item doc → docs-first risk note → implement → bump desktop version per [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) when shipping to the share.
4. After ship + **recorded** PROD verify: set Status to **Done** and move the row to §3.
5. Do **not** mark Done only because `release` and `development` share a commit tip.

## 2. Open / in-progress items

| ID | Title | Status | Priority | Doc | Version note |
| --- | --- | --- | --- | --- | --- |
| DEV-002 | Severe AccService Autodesk-token notice (all ACC users) + admin sync startup list | Planning — **token Critical locked 16.08** | P0 | [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) | With that feature ship |
| DEV-007 | Open-with by extension (ACC / Drive / Windows Shell) | Planning (direction approved) | P2 | [`DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md`](./DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md) §4 | With that feature ship |
| DEV-008 | Project edit dialog + verified rename (FS/ACC/Drive→DB); dashboard double-click; create parity (worker + bid) | Implementing | P1 | [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) | Layer A done; Layer B/C in progress — **Needs Review** vs tip |
| DEV-009 | Gmail project label identity by `(Number)` + `Email.AutoSyncProjectLabelNames` (per mailbox); duplicate decision UI | Implementing | P1 | [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) §4 · [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) | Layer B keep/delete dialog — **Needs Review** vs tip |
| DEV-010 | «דוח קריסות תחנה» — local Event Log crash report | Implementing / partial on tip | P2 | [`DEV_PLAN_WORKSTATION_CRASH_REPORT.md`](./DEV_PLAN_WORKSTATION_CRASH_REPORT.md) | Ships appear in 1.0.20+ line — **Needs Review** completeness |
| DEV-011 | Job-type remove: strong warning, no workflow hard-delete, orphan-track mark + data-integrity list | Implementing | P2 | [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) §5 | Warning + `[ORPHAN-TRACK]` + Ops filter done; broader checklist later |
| DEV-014 | Crash report round 2 Ship 2: context form, plugin inventory, CER/WER/dump index | Ship 1 on tip; Ship 2 pending | P2 | [`DEV_PLAN_WORKSTATION_CRASH_DEEP_DIAGNOSTICS.md`](./DEV_PLAN_WORKSTATION_CRASH_DEEP_DIAGNOSTICS.md) | Ship 1 via 1.0.21 line |
| DEV-018 | Monthly MasterPlan restore: pre-ETL replica mismatch log (same `--monthly`, no extra DB) | On release tip — ops verify Needs Review | P1 | [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md) | SyncEngine 1.0.19 / App.Wpf 1.0.26 |
| DEV-019 | Reconcile orphan purge — **intent → DEV-025** (API align + 30-day JSON; drop 10%/2-sighting hard gates) | Planning — follow DEV-025 | P1 | [`DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md`](./DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md) · [`DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md`](./DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md) | SyncEngine |
| DEV-020 | Monthly bak staging: move to `N:\MasterPlanBakup` ↔ SQL `D:\SharedFolder\ProjectsData\MasterPlanBakup`, retain 10 | On release tip — ops verify Needs Review | P1 | [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md) §1b | SyncEngine **1.0.20** |
| DEV-021 | HoursReports.Hours = **milliseconds** (ETL); PHE.LastUpdated not stamped from bak; daily MERGE repair null Duration/TotalHours | Implementing — unit tests green; DB verify pending | P1 | [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md) §1c · [`DEV_CHECKLIST_MASTERPLAN_HOURS_DEV021.md`](./DEV_CHECKLIST_MASTERPLAN_HOURS_DEV021.md) | SyncEngine on `development` |
| DEV-022 | After monthly restore: ensure `SI-ENG\שרטטים` has db_datareader+db_datawriter on `Db_Mp_SiEng` and `Replica_DB` | Implementing | P1 | [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md) §1d | SyncEngine on `development` |
| DEV-023 | After successful `--monthly`, run existing API daily sync with **forced full reconcile** (internet) | Implementing | P1 | [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md) §1e | SyncEngine on `development` |
| DEV-024 | R02 July gap — live MP preferred over Replica (proven on PROD) | Diagnosis done — fix via DEV-025 Rule B (shared resolver) | P1 | [`DEV_DIAG_R02_GAP_AFTER_RECONCILE.md`](./DEV_DIAG_R02_GAP_AFTER_RECONCILE.md) | App.Wpf **1.0.29** |
| DEV-025 | Replica-first queries (all reports); orphan DELETE + 30-day JSON under MasterPlanBakup | Implementing | P1 | [`DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md`](./DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md) | SyncEngine **1.0.23** + App.Wpf **1.0.29** |
| DEV-026 | Gmail mailbox label audit — sortable table of user labels ↔ project; duplicate `(Number)` note | Implementing | P2 | [`DEV_PLAN_GMAIL_LABEL_CUTOVER_AUDIT.md`](./DEV_PLAN_GMAIL_LABEL_CUTOVER_AUDIT.md) | App.Wpf **1.0.30** |
| DEV-027 | Workstation `.secrets` import for non-admin; System Status classifies AccService 401 vs TLS; Fast/Deep health | Planning — import modes + Deep cadence locked | P1 | [`DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md`](./DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md) | Two import modes; Deep = 30 min or Refresh |
| DEV-028 | Startup proves Client log write (local + Llog); «מצב מערכת» note if central missing | Implementing — **Slice E open** | P0 | [`DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md`](./DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md) | A–D shipped 1.0.33; Slice E: הערה if central min ≠ Warning; no old-file fallback |
## 2b. On `release` tip — ops verify (Needs Review)

Code for these IDs is present on `origin/release` @ `127dc0e` (App.Wpf **1.0.23** and prior ship commits). **Do not** treat as Done until operator verify is recorded.

| ID | Title | Evidence on tip | Doc | Earliest cited ship |
| --- | --- | --- | --- | --- |
| DEV-001 | Email body links → external download window → ACC | On tip | [`DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md`](./DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md) | 1.0.4 |
| DEV-003 | ProjectWork tree bak/recover UX | On tip | [`DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md`](./DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md) | 1.0.6 |
| DEV-005 | «פתח ב-Gmail» | On tip | [`DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md`](./DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md) §4 | 1.0.5 |
| DEV-006 | ProjectWork editable scan exclusions | On tip | [`DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md`](./DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md) §3 | 1.0.8 |
| DEV-012 | ProjectWork disk-only folders | On tip (`ship 1.0.17`) | [`DEV_PLAN_PROJECTWORK_DISK_FOLDERS.md`](./DEV_PLAN_PROJECTWORK_DISK_FOLDERS.md) | 1.0.17 |
| DEV-013 | ProjectWork lazy scan | On tip (`ship 1.0.18`) | [`DEV_PLAN_PROJECTWORK_LAZY_SCAN.md`](./DEV_PLAN_PROJECTWORK_LAZY_SCAN.md) | 1.0.18 |
| DEV-015 | Crash report accuracy | On tip (`ship 1.0.22`+) | [`DEV_PLAN_WORKSTATION_CRASH_REPORT_ACCURACY.md`](./DEV_PLAN_WORKSTATION_CRASH_REPORT_ACCURACY.md) | 1.0.22 |
| DEV-016 | Email two-stage triage | On tip (`ship 1.0.22`+) | [`DEV_PLAN_EMAIL_TRIAGE_TWO_STAGE.md`](./DEV_PLAN_EMAIL_TRIAGE_TWO_STAGE.md) | 1.0.22 |
| DEV-017 | Email UX / ProjectSelector dual widths | On tip (`ship 1.0.23`) | [`DEV_PLAN_EMAIL_LIST_UX_FOLLOWUPS.md`](./DEV_PLAN_EMAIL_LIST_UX_FOLLOWUPS.md) | 1.0.23 |

| ID | Title | Notes |
| --- | --- | --- |
| DEV-004 | Mark email as read trigger | **Superseded by DEV-016** — see triage plan |

## 3. Done / cancelled

| ID | Title | Outcome | Date |
| --- | --- | --- | --- |
| — | — | *(none recorded with operator verify yet — see §2b)* | — |

## 4. Out of Scope

- Tracking every historical V2 parity gap (use domain docs / migration maps)
- Editing `Docs/Archive` as active backlog
- Auto-creating GitHub Issues from this file (manual optional)
- Inferring "users have the feature" from git tip alone

## 5. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Separate `docs/bugs/` folder tree | Postponed | Keep flat `docs/DEV_*.md` + this index for discoverability |
| Status "awaiting PROD publish" while tip already includes ship commits | Dropped wording | Replaced by §2b + Needs Review verify |

## 6. Needs Review

1. Operator confirmation that pilot PCs actually received MSIX builds through **1.0.23**.
2. Whether DEV-008/009/010/011 partial layers are fully on tip or still diverging on a local DEV workspace.
3. Whether PROD wants GitHub Issues mirrored 1:1 with this index.
4. Local workspace note (07.08.2026): checkout may lag `origin/development` by the ship commit and may hold uncommitted ProjectSelector polish — do not confuse with remote tip.
