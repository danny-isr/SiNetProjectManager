# SiNet — Repository documentation index (`docs/`)

> **Title:** docs/ README -- New System & ops documentation index
> **Date:** 02.08.2026
> **Updated:** 16.08.2026

Agent entry: [`AGENTS.md`](../AGENTS.md). Documentation-round rules: [`.agents/AGENTS.md`](../.agents/AGENTS.md).
As-Is reconciliation ledger: [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md).

---

## 1. Start here by role

| Role | Read first |
| --- | --- |
| **PROD machine (release + ops)** | [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) → [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) → [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) → [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md) |
| **DEV machine** | [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) → [`DEV_BACKLOG.md`](./DEV_BACKLOG.md) → [`DEV_TOOLS.md`](./DEV_TOOLS.md) → [`AI_DEVELOPMENT_GUIDE.md`](./AI_DEVELOPMENT_GUIDE.md) |
| **Architecture / cutover** | [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md) → [`APP_SHELL.md`](./APP_SHELL.md) → [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md) → [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md) |
| **Docs As-Is alignment** | [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md) |
| **Email / ACC truth** | [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) → FileMaterial: [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md) |

Deploy scripts detail (root): [`DEPLOYMENT.md`](../DEPLOYMENT.md), [`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md).

---

## 2. Environments, release & production ops

| Document | Purpose |
| --- | --- |
| [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md) | As-Is docs reconciliation ledger (dimensions, contradictions, Needs Review) |
| [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) | PROD vs DEV machine roles, config placement, allowed ops, Google/ACC isolation target |
| [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) | Publish gates, versioning, rollback; who may run `publish-all.ps1` |
| [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) | Live log tails, System Status, Workflow Ops, pilot routines |
| [`OPS_LLOG_REVIEW.md`](./OPS_LLOG_REVIEW.md) | **Agent:** incremental UNC Llog sweep (state in `artifacts/llog-review/`) |
| [`OPS_ACCSERVICE_TOKEN_REFRESH.md`](./OPS_ACCSERVICE_TOKEN_REFRESH.md) | How to restore AccService Autodesk 3-legged refresh token on PROD |
| [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) | **Planning (DEV-002 P0):** severe AccService Autodesk-token notice for ACC users + admin sync list |
| [`DEV_BACKLOG.md`](./DEV_BACKLOG.md) | Open DEV defects / implementation requests (index) |
| [`DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md`](./DEV_BUG_EMAIL_LINK_EXTERNAL_WINDOW.md) | **DEV-001:** Jumbo/body links must open external download window → ACC |
| [`DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md`](./DEV_PLAN_PROJECTWORK_TREE_BAK_RECOVER.md) | **DEV-003:** ProjectWork bak/recover hide-stale + delete stale + tree UX |
| [`DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md`](./DEV_PLAN_PROJECTWORK_SCAN_EXCLUSIONS_AND_OPEN.md) | **DEV-006/007:** editable scan exclusions; open-with by extension |
| [`DEV_PLAN_PROJECT_EDIT_AND_RENAME.md`](./DEV_PLAN_PROJECT_EDIT_AND_RENAME.md) | **DEV-008/009:** project edit + verified rename; Gmail label sync by number |
| [`DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md`](./DEV_PLAN_EMAIL_READ_STATE_AND_GMAIL_OPEN.md) | **DEV-004/005:** mark-read port + «פתח ב-Gmail» (DEV-004 trigger superseded by DEV-016) |
| [`DEV_PLAN_EMAIL_TRIAGE_TWO_STAGE.md`](./DEV_PLAN_EMAIL_TRIAGE_TWO_STAGE.md) | **DEV-016:** two-stage triage, FYI, mark-read on completion, leaf group title |
| [`DEV_PLAN_EMAIL_LIST_UX_FOLLOWUPS.md`](./DEV_PLAN_EMAIL_LIST_UX_FOLLOWUPS.md) | **DEV-017:** group order/membership, project-switch stuck, refresh, selector widths |
| [`DEV_PLAN_GMAIL_LABEL_CUTOVER_AUDIT.md`](./DEV_PLAN_GMAIL_LABEL_CUTOVER_AUDIT.md) | **DEV-026:** mailbox user-label ↔ project table; duplicate `(Number)` note |
| [`DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md`](./DEV_DIRECTIVE_WORKSTATION_SECRETS_AND_HEALTH.md) | **DEV-027 Planning:** employee `.secrets` import; AccService 401 vs TLS; Fast/Deep System Status |
| [`DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md`](./DEV_DIRECTIVE_STARTUP_LOG_WRITE_VERIFY.md) | **DEV-028 Planning:** startup must prove Llog write; System Status note if not |
| [`DEV_CHECKLIST_MASTERPLAN_HOURS_DEV021.md`](./DEV_CHECKLIST_MASTERPLAN_HOURS_DEV021.md) | **DEV-021:** test-replica verify checklist (monthly + sample IDs + R02) |
| [`DEV_DIAG_R02_GAP_AFTER_RECONCILE.md`](./DEV_DIAG_R02_GAP_AFTER_RECONCILE.md) | **DEV-024:** R02 vs MP gap after monthly+reconcile (source-split diagnosis) |
| [`DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md`](./DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md) | **DEV-025:** Replica-first reports; orphan DELETE + 30-day JSON under MasterPlanBakup |
| [`DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md`](./DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md) | **DEV-019:** safe orphan DELETE after full hours reconcile (caps / age / 2-sightings) |
| [`DEV_PLAN_WORKSTATION_CRASH_REPORT.md`](./DEV_PLAN_WORKSTATION_CRASH_REPORT.md) | **DEV-010:** «דוח קריסות תחנה» — Event Log crash report + AI export |
| [`DEV_PLAN_WORKSTATION_CRASH_DEEP_DIAGNOSTICS.md`](./DEV_PLAN_WORKSTATION_CRASH_DEEP_DIAGNOSTICS.md) | **DEV-014:** crash report round 2 — BIOS/WHEA/CER evidence, incident grouping |
| [`DEV_PLAN_WORKSTATION_CRASH_REPORT_ACCURACY.md`](./DEV_PLAN_WORKSTATION_CRASH_REPORT_ACCURACY.md) | **DEV-015:** Ship 1.1 report accuracy (WHEA corrected, microcode, labels, minidump) |
| [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md) | Pilot / expand phases and sign-off log |
| [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md) | App.Wpf replaces V2 as shipped desktop |
| [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md) | Pilot envelope / readiness notes |
| [`OPS-P0-DB-BACKUP.md`](./OPS-P0-DB-BACKUP.md) | DB backup / restore drill (P0) |
| [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md) | MasterPlan API key rotation (P0) |
| [`LOGGING.md`](./LOGGING.md) | New System logging architecture (IAppLogger + Serilog); §9.4.1 Client heartbeat |
| [`LOGGING_MATERIAL_FAILURES.md`](./LOGGING_MATERIAL_FAILURES.md) | Material failures + Client heartbeat must reach Llog at Warning+ |
| [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md) | «מצב מערכת» design and contributors |
| [`WORKFLOW_OPS_DASHBOARD.md`](./WORKFLOW_OPS_DASHBOARD.md) | «בריאות תהליכים» runtime ops |
| [`DEV_TOOLS.md`](./DEV_TOOLS.md) | DEBUG-only Reset & Seed — not for production DB |
| [`BUILD_SIBLING_PINS.md`](./BUILD_SIBLING_PINS.md) | Sibling repo pins for build/CI |
| [`DATABASE_RECOVERY_BASELINE.md`](./DATABASE_RECOVERY_BASELINE.md) | SQL recovery baseline / freeze notes |
| [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md) | SyncEngine watermarks, hours lookback window, weekly reconciliation |

---

## 3. Architecture & host

| Document | Purpose |
| --- | --- |
| [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md) | Target architecture |
| [`APP_SHELL.md`](./APP_SHELL.md) | Shell / startup; production host = App.Wpf |
| [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md) | Standalone composition |
| [`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md) | New System boundary |
| [`PROCESS_BACKBONE_FOUNDATION.md`](./PROCESS_BACKBONE_FOUNDATION.md) | Process backbone |
| [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md) | Identity & permissions target |
| [`SETTINGS.md`](./SETTINGS.md) | Settings (Stage 5) |
| [`TEST_STRATEGY.md`](./TEST_STRATEGY.md) | Test strategy |
| [`AI_DEVELOPMENT_GUIDE.md`](./AI_DEVELOPMENT_GUIDE.md) | AI-assisted development guide |

---

## 4. Domains (New System docs)

| Document | Purpose |
| --- | --- |
| [`PROJECTS.md`](./PROJECTS.md) | Projects domain / ProjectSelector |
| [`PROJECTS_DASHBOARD.md`](./PROJECTS_DASHBOARD.md) | «ריכוז פרויקטים» |
| [`PROJECT_CONTEXT_MIGRATION.md`](./PROJECT_CONTEXT_MIGRATION.md) | Project context migration notes |
| [`TASK_MODEL_RULES.md`](./TASK_MODEL_RULES.md) | Task model rules |
| [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) | Gmail label / ACC / DB truth |
| [`FILEMATERIAL_MOVETOPROJECT.md`](./FILEMATERIAL_MOVETOPROJECT.md) | **FileMaterial / MoveToProject** — six decisions Target (canonical) |
| [`EMAIL_LIST_MIGRATION.md`](./EMAIL_LIST_MIGRATION.md) | Email list migration |
| [`EMAIL_DETAIL_COMPONENT.md`](./EMAIL_DETAIL_COMPONENT.md) | Email detail component |
| [`EMAIL_FILING_SERVICE_DESIGN.md`](./EMAIL_FILING_SERVICE_DESIGN.md) | `IEmailFilingService` design |
| [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md) | Email → ACC Inbox ingest (N1–N5; FileMaterial pointer) |
| [`ACC_BOUNDARY.md`](./ACC_BOUNDARY.md) | ACC client / AccService boundary |
| [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md) | ACC control plane / TLS status |
| [`ACC_SERVICE_DECOUPLING.md`](./ACC_SERVICE_DECOUPLING.md) | AccService decoupling from SiNetSQL |
| [`ACC_SERVICE_TLS_VIA_VAULT.md`](./ACC_SERVICE_TLS_VIA_VAULT.md) | TLS via vault |
| [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md) | Google boundary |
| [`FILE_CATALOG_ADMIN.md`](./FILE_CATALOG_ADMIN.md) | File catalog admin |
| [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md) | Work surface ↔ workflow contract |
| [`WORKFLOW_COMMAND_SERVICE_ASSESSMENT.md`](./WORKFLOW_COMMAND_SERVICE_ASSESSMENT.md) | Workflow command service assessment |

---

## 5. Migration maps & planning

| Document | Purpose |
| --- | --- |
| [`MIGRATION_MAP.md`](./MIGRATION_MAP.md) | Migration map |
| [`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md) | Master plan → standalone |
| [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md) | UI window migration map |
| [`PHASE_E_GATED.md`](./PHASE_E_GATED.md) | Phase E gated follow-ups |
| [`P2-TECH-DEBT-BACKLOG.md`](./P2-TECH-DEBT-BACKLOG.md) | P2 tech debt backlog |
| [`GITHUB-REMEDIATION-BOARD.md`](./GITHUB-REMEDIATION-BOARD.md) | GitHub remediation staging board |

---

## 6. Audits & reconciliations

| Document | Purpose |
| --- | --- |
| [`AUDIT-Architecture-Migration-2026-07-27.md`](./AUDIT-Architecture-Migration-2026-07-27.md) | Architecture / migration audit |
| [`AUDIT-REMEDIATION-MATRIX-2026-07-28.md`](./AUDIT-REMEDIATION-MATRIX-2026-07-28.md) | Remediation matrix |
| [`AUDIT-Race-Close-Logging-2026-07-09.md`](./AUDIT-Race-Close-Logging-2026-07-09.md) | Race / close / logging audit |
| [`users_system_audit_reconciliation_2026-07-02.md`](./users_system_audit_reconciliation_2026-07-02.md) | Users system audit reconciliation |

---

## 7. Manual tests

Folder: [`manual-tests/`](./manual-tests/).

Notable gates:

- [`manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md`](./manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md)  
- [`manual-tests/STANDALONE_PILOT_SMOKE.md`](./manual-tests/STANDALONE_PILOT_SMOKE.md)  
- [`manual-tests/STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](./manual-tests/STANDALONE_WORKFLOW_PRODUCTION_GATE.md)  
- [`manual-tests/DB_RESTORE_REHEARSAL_CHECKLIST.md`](./manual-tests/DB_RESTORE_REHEARSAL_CHECKLIST.md)  
- [`manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md)  

---

## 8. Related documentation trees

| Location | Role |
| --- | --- |
| [`SiNetProjectManagerV2/Docs/`](../SiNetProjectManagerV2/Docs/README.md) | Domain principles, Decisions, Archive (authoritative for domain rules) |
| Root `DEPLOYMENT.md` / `SECRETS-MANAGEMENT.md` | Deploy and secrets install |
| `.cursor/rules/` | Always-on agent constraints |

**Conflict rule:** for domain source-of-truth (Email, ACC, …), `SiNetProjectManagerV2/Docs/Domains/...Principles-...md` wins over informal notes. For **machine roles / release / monitoring**, the documents in §2 of this index win.

---

## 9. Out of Scope (this index)

- Duplicating full domain principles from `SiNetProjectManagerV2/Docs`  
- Archiving or deleting older `docs/*.md` files  

## 10. Dropped / Cancelled / Postponed

| Item | Status |
| --- | --- |
| Living without a `docs/README.md` while `APP_SHELL.md` linked to it | Fixed (this file, 02.08.2026) |
