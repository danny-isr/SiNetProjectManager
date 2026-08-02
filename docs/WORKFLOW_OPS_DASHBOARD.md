# Workflow Ops Dashboard — Operational health & runtime control

> **Status:** Approved target — Runtime Ops (2026-08-02)  
> **Related:** [`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md), [`APP_SHELL.md`](./APP_SHELL.md),  
> [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) (pilot ops routines),  
> [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md),  
> [`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md),  
> [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md)

## 1. Purpose

The **Workflow Ops Dashboard** («בריאות תהליכים») is the New System control center for
**business workflow instance health and runtime control**. It answers in seconds:

1. Is there an active problem?
2. Which workflow instance is affected?
3. Which stage is it on, and for how long?
4. Which project / user?
5. Can an administrator safely advance / pause / resume / complete / cancel / retry recovery?

It **complements** «מצב מערכת» (SQL / ACC / Gmail / background work). It does **not** replace it.

## 2. Closed-world definitions (locked)

| Concern | Owner | UI? |
| --- | --- | --- |
| Workflow **definitions** (stages, transitions, stage-tasks) | Seed / DevTools (`SqlWorkflowSeedService`) | **No** editor |
| Canvas layout (CanvasX/Y only) | `IWorkflowCanvasLayoutService` | Visual canvas — layout only |
| JobType ↔ Definition mapping | `IProjectTypeWorkflowPolicyAdminService` | «מדיניות סוג↔תהליך» |
| Runtime instances (start / advance / pause / resume / complete / cancel / recovery) | `IWorkflowCommandService` + `IWorkflowRecoveryService` | This dashboard + instance detail |

Changing stage/transition graphs requires a developer seed change — not an ops UI.

## 3. Audience and permissions

| Audience | Access |
| --- | --- |
| Administrator / support | Feature `Shell.OpenWorkflowOpsDashboard` (min role **Administrator**) |
| Dangerous actions | Separate feature codes (all min **Administrator**): |
| | `WorkflowOps.Advance`, `WorkflowOps.Cancel`, `WorkflowOps.Retry`, `WorkflowOps.Start` |
| Project manager (business-only view) | **Future** — narrower feature codes |
| Developer | Same as Administrator in Release; DevTools Watchdog remains DEBUG-only |

## 4. Screen structure

### 4.1 Summary cards

Computed from `IWorkflowQueryService.GetAllWorkflowInstanceSnapshotsAsync` +
`IWorkflowRecoveryService.DetectStalledAsync`:

| Card | Source |
| --- | --- |
| Overall | Green if no stalled + no infra Degraded/Stopped; Yellow if stalled or degraded infra; Red if many stalled |
| Active | `Status` in Active/Paused |
| Completed today | `Completed` with `CompletedAtUtc` on local calendar day |
| Cancelled today | `Cancelled` with `CompletedAtUtc` today |
| Suspected stalled | DetectStalled set |
| Avg duration today | Mean of `CompletedAt − CreatedAt` for completed today |
| Last refresh | Wall clock of last successful load |

### 4.2 Infrastructure strip

Read-only summary from `IRuntimeSubsystemStatusService.Current`.
Button **פתח מצב מערכת** opens the existing `SystemStatusWindow`.

### 4.3 Instance grid

| Column | Mapping |
| --- | --- |
| Id | `WorkflowInstanceDto.Id` |
| Workflow | `WorkflowDefinition.Name` |
| Project | `Project` display |
| User | `CreatedByUser` |
| Started | `CreatedAtUtc` (local) |
| Stage | `CurrentStage.Name` |
| Duration | now − Created (active) or Completed − Created |
| Status | `WorkflowStatus` + badge «חשוד כתקוע» |
| Notes | Truncated `Notes` |

Filters: status, workflow name, project/user text. Auto-refresh 20s + manual refresh.

**B2:** A project may have several active instances of the same definition (one per JobType track).
The grid must list **each** instance; do not collapse to one “best” row per project.

**Double-click** (or **פתח מופע**) opens the **instance detail window** (§5).

### 4.4 Detail panel (inline)

On selection: ordered `StageTransitions`, current stage task progress, **Copy instance id**.

### 4.5 Dangerous actions (enabled)

| Action | Port | Confirm? | Feature |
| --- | --- | --- | --- |
| Retry (recovery) | `IWorkflowRecoveryService.AttemptRecoveryAsync` for selected stalled candidate | Yes | `WorkflowOps.Retry` |
| Cancel | `IWorkflowCommandService.CancelAsync` | Yes | `WorkflowOps.Cancel` |
| Start workflow | `IWorkflowCommandService.StartAsync` after project + allowed definition | Yes | `WorkflowOps.Start` |

Buttons are enabled only when the current user has the feature **and** the selection is valid
(Retry only when selected row is stalled; Cancel only when status is Active/Paused/Draft).

## 5. Instance detail window

`WorkflowInstanceDetailWindow` — opened from the ops dashboard.

| Area | Behavior |
| --- | --- |
| Header | Instance id, definition name, project, status, current stage |
| Transitions | Ordered history |
| Task progress | `GetStageTaskProgressAsync` |
| Advance | Combo of `GetAllowedNextStagesAsync` + notes → `AdvanceAsync` (`WorkflowOps.Advance`) |
| Pause / Resume | Status-gated → `PauseAsync` / `ResumeAsync` (`WorkflowOps.Advance`) |
| Complete | Confirm → `CompleteInstanceAsync` (`WorkflowOps.Cancel` — same admin bar) |
| Cancel | Confirm → `CancelAsync` (`WorkflowOps.Cancel`) |

After a successful write, the detail window refreshes and notifies the parent dashboard to refresh.

## 6. Manual start dialog

`WorkflowStartDialog`:

1. Pick a project (`IProjectQueryService.SearchProjectsAsync`).
2. Load allowed definitions (`IProjectWorkflowPolicyService.GetAllowedWorkflowsAsync`).
3. Confirm → `StartAsync` with `WorkflowTriggerTypeDto.Manual` and current user id.

## 7. Data sources

```text
IWorkflowQueryService.*
IWorkflowCommandService.StartAsync / AdvanceAsync / PauseAsync / ResumeAsync /
                        CompleteInstanceAsync / CancelAsync
IWorkflowRecoveryService.DetectStalledAsync / AttemptRecoveryAsync
IProjectWorkflowPolicyService.GetAllowedWorkflowsAsync
IProjectQueryService.SearchProjectsAsync
ICurrentUserContext.UserId
IAuthorizationQueryService.CanCurrentUserAccessFeatureAsync
IRuntimeSubsystemStatusService
```

UI must **not** reference `SiNetSQL`, `IDbContextFactory`, or call `SaveChanges` directly.

## 8. Stalled definition

Reuse existing watchdog detection (`DetectStalledAsync`): Active instance whose current stage
has no open Trigger tasks. UI badge + optional Retry recovery from this screen.

## 9. Explicitly out of scope (still)

- Visual / tree **definition editor** (stages, transitions, task templates)
- EF migrations / `LastActivityAt` / `CorrelationId` / rich stage telemetry tables
- Teams / email alerts, SignalR push
- 90-day archive policy tables
- Business-only permission tier

## 10. Acceptance

An Administrator can:

- Open **מנהלה → בריאות תהליכים**
- See summary cards and whether any instance is stalled
- Filter the grid and open detail (transitions + task progress)
- Copy instance id
- Double-click → instance detail → Advance / Pause / Resume / Complete / Cancel (with confirms)
- Retry recovery on a stalled row (with confirm)
- Cancel from the toolbar (with confirm)
- Start a new workflow for a project from allowed definitions

## 11. Shell menu

| Item | Feature code | Min role | Opens |
| --- | --- | --- | --- |
| בריאות תהליכים | `Shell.OpenWorkflowOpsDashboard` | Administrator | `WorkflowOpsDashboardWindow` |
