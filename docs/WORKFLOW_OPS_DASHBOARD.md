# Workflow Ops Dashboard — Operational health

> **Status:** Approved target — MVP Pilot (2026-07-31)  
> **Related:** [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md), [`APP_SHELL.md`](./APP_SHELL.md),  
> [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md),  
> [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md)

## 1. Purpose

The **Workflow Ops Dashboard** («בריאות תהליכים») is the New System control center for
**business workflow instance health**. It answers in seconds:

1. Is there an active problem?
2. Which workflow instance is affected?
3. Which stage is it on, and for how long?
4. Which project / user?

It **complements** «מצב מערכת» (SQL / ACC / Gmail / background work). It does **not** replace it.

## 2. Audience and permissions (MVP)

| Audience | MVP access |
| --- | --- |
| Administrator / support | Feature `Shell.OpenWorkflowOpsDashboard` (min role **Administrator**, same bar as system settings) |
| Project manager (business-only view) | **Future** — narrower feature code |
| Developer | Same as Administrator in Release; DevTools Watchdog remains DEBUG-only |

Manual Retry / Cancel / Audit are **out of MVP** (see §6).

## 3. MVP screen structure

### 3.1 Summary cards

Computed from `IWorkflowQueryService.GetAllWorkflowInstanceSnapshotsAsync` +
`IWorkflowRecoveryService.DetectStalledAsync`:

| Card | Source |
| --- | --- |
| Overall | Green if no stalled + no infra Degraded/Stopped; Yellow if stalled or degraded infra; Red if many stalled |
| Active | `Status` in Active/Paused |
| Completed today | `Completed` with `CompletedAtUtc` on local calendar day |
| Cancelled today | `Cancelled` with `CompletedAtUtc` today (no separate Failed status in domain) |
| Suspected stalled | DetectStalled set |
| Avg duration today | Mean of `CompletedAt − CreatedAt` for completed today |
| Last refresh | Wall clock of last successful load |

Colors are secondary; every card also has Hebrew text.

### 3.2 Infrastructure strip

Read-only summary from `IRuntimeSubsystemStatusService.Current` (counts Running/Idle/Degraded).
Button **פתח מצב מערכת** opens the existing `SystemStatusWindow`.

### 3.3 Instance grid

| Column | Mapping |
| --- | --- |
| Id | `WorkflowInstanceDto.Id` |
| Workflow | `WorkflowDefinition.Name` |
| Track / JobType | B2: JobType track identity on the instance (display name); empty for genuinely project-level / unbound instances |
| Project | `Project` display |
| User | `CreatedByUser` |
| Started | `CreatedAtUtc` (local) |
| Stage | `CurrentStage.Name` |
| Duration | now − Created (active) or Completed − Created |
| Status | `WorkflowStatus` + badge «חשוד כתקוע» |
| Notes | Truncated `Notes` |
| Tasks | Optional `GetStageTaskProgressAsync` text when detail selected |

No fake progress %. Filters: status, workflow name, project/user text. Auto-refresh 20s + manual refresh.

**B2:** A project may have several active instances of the same definition (one per JobType track). The grid must list **each** instance; do not collapse to one “best” row per project. Policy: [`PROJECT_TYPE_WORKFLOW_POLICY.md`](./manual-tests/PROJECT_TYPE_WORKFLOW_POLICY.md) §7.

### 3.4 Detail panel

On selection: ordered `StageTransitions`, current stage task progress, **Copy instance id**.

### 3.5 Dangerous actions (MVP)

Retry / Cancel / AttemptRecovery: **disabled** with tooltip «בקרוב — לא ב-MVP».
Do **not** call `IWorkflowCommandService` or `AttemptRecoveryAsync` from this window in Release.

## 4. Data sources (no parallel stack)

```text
IWorkflowQueryService.GetAllWorkflowInstanceSnapshotsAsync
IWorkflowQueryService.GetStageTaskProgressAsync          (detail)
IWorkflowRecoveryService.DetectStalledAsync              (stalled badge)
IRuntimeSubsystemStatusService                           (infra strip only)
```

**Last activity (MVP):** latest `WorkflowStageTransition.TransitionedAtUtc` when present;
otherwise `CreatedAtUtc`. No `LastActivityAt` / `CorrelationId` columns (schema deferred).

## 5. Stalled definition (MVP)

Reuse existing watchdog detection (`DetectStalledAsync`): Active instance whose current stage
has no open Trigger tasks. UI badge only — no auto-recovery from this screen.

## 6. Explicitly out of MVP

- EF migrations / `LastActivityAt` / `CorrelationId` / rich stage telemetry tables
- Retry, Cancel, Mark-resolved, Audit log writes
- Teams / email alerts, SignalR push
- 90-day archive policy tables
- Business-only permission tier
- Migrating legacy `WorkflowDashboard` **write** into New Shell

## 7. Future (phase 2+)

As in the product brief: heartbeat column, CorrelationId, safe Retry for idempotent flows,
Audit, alerts, SLA, project-manager view — each behind a separate docs + migration approval.

## 8. Acceptance (MVP)

An Administrator can:

- Open **מנהלה → בריאות תהליכים**
- See summary cards and whether any instance is stalled
- See infra strip and open מצב מערכת
- Filter the grid and open detail (transitions + task progress)
- Copy instance id
- Confirm Retry/Cancel are not executable

## 9. Shell menu

| Item | Feature code | Min role | Opens |
| --- | --- | --- | --- |
| בריאות תהליכים | `Shell.OpenWorkflowOpsDashboard` | Administrator | `WorkflowOpsDashboardWindow` |
