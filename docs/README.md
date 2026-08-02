# SiNet — Repository documentation index (`docs/`)

> **Title:** docs/ README — New System & ops documentation index  
> **Date:** 02.08.2026  
> **Updated:** 02.08.2026  
> **Status:** Active  
> **Scope:** Entry point for markdown under `docs/` (migration, architecture, environments, ops). Domain principles for the legacy tree remain under [`SiNetProjectManagerV2/Docs/README.md`](../SiNetProjectManagerV2/Docs/README.md).

Agent entry: [`AGENTS.md`](../AGENTS.md). Documentation-round rules: [`.agents/AGENTS.md`](../.agents/AGENTS.md).

---

## 1. Start here by role

| Role | Read first |
| --- | --- |
| **PROD machine (release + ops)** | [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) → [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) → [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) → [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md) |
| **DEV machine** | [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) → [`DEV_TOOLS.md`](./DEV_TOOLS.md) → [`AI_DEVELOPMENT_GUIDE.md`](./AI_DEVELOPMENT_GUIDE.md) |
| **Architecture / cutover** | [`ARCHITECTURE_TARGET.md`](./ARCHITECTURE_TARGET.md) → [`APP_SHELL.md`](./APP_SHELL.md) → [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md) → [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md) |
| **Email / ACC truth** | [`EMAIL_ACC_SOURCE_OF_TRUTH.md`](./EMAIL_ACC_SOURCE_OF_TRUTH.md) |

Deploy scripts detail (root): [`DEPLOYMENT.md`](../DEPLOYMENT.md), [`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md).

---

## 2. Environments, release & production ops

| Document | Purpose |
| --- | --- |
| [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) | PROD vs DEV machine roles, config placement, allowed ops, Google/ACC isolation target |
| [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) | Publish gates, versioning, rollback; who may run `publish-all.ps1` |
| [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) | Live log tails, System Status, Workflow Ops, pilot routines |
| [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md) | Pilot / expand phases and sign-off log |
| [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md) | App.Wpf replaces V2 as shipped desktop |
| [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md) | Pilot envelope / readiness notes |
| [`OPS-P0-DB-BACKUP.md`](./OPS-P0-DB-BACKUP.md) | DB backup / restore drill (P0) |
| [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md) | MasterPlan API key rotation (P0) |
| [`LOGGING.md`](./LOGGING.md) | New System logging architecture (IAppLogger + Serilog) |
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
| [`EMAIL_LIST_MIGRATION.md`](./EMAIL_LIST_MIGRATION.md) | Email list migration |
| [`EMAIL_DETAIL_COMPONENT.md`](./EMAIL_DETAIL_COMPONENT.md) | Email detail component |
| [`EMAIL_FILING_SERVICE_DESIGN.md`](./EMAIL_FILING_SERVICE_DESIGN.md) | `IEmailFilingService` design |
| [`NATIVE_EMAIL_ACC_INGEST.md`](./NATIVE_EMAIL_ACC_INGEST.md) | Email → ACC Inbox ingest |
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
