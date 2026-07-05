# Process Backbone Foundation

> **Status:** Foundation closure documentation (2026-07-05)  
> **Scope:** When New System Work Surfaces may start; what remains legacy; readiness matrix.  
> **Not in scope:** Email/ProjectWork/Reports implementation, schema changes, new router/fallback.

Related:
[`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md),
[`MIGRATION_MAP.md`](./MIGRATION_MAP.md),
[`WORKFLOW_COMMAND_SERVICE_ASSESSMENT.md`](./WORKFLOW_COMMAND_SERVICE_ASSESSMENT.md).

---

## When may new Work Surfaces start?

**Rule:** Do not start a Work Surface until its row in the [Work Surface readiness matrix](#work-surface-readiness-matrix) is **Can start now** or explicitly **Partial** with the surface listed under *Allowed*.

Foundation registration entry point (New System):

```csharp
services.AddSiNetProcessBackbone(); // Workflow reads + Task nav/completion/query + Action dispatcher
// Host must also bind IWorkflowCommandService when completion auto-advance is required:
// SiNetSQL AddSiNetWorkflowCommands() — temporary adapter until orchestrator migrates.
```

**LegacyBridge** is **not** target architecture. It registers **Inspection only**. Do not use LegacyBridge for new Task/Workflow/Action paths.

---

## Readiness levels (Level 0 – Level 5)

| Level | Name | Criteria |
| --- | --- | --- |
| **0** | Not ready | Contracts only, or LegacyBridge-only path |
| **1** | Navigation ready | `ITaskNavigationService` → `SqlTaskNavigationService` returns `WorkSurfaceContext` |
| **2** | Completion ready | `ITaskCompletionService` → `SqlTaskCompletionService`; auto-advance via injected `IWorkflowCommandService` |
| **3** | Workflow ready | Reads native (`IWorkflowQueryService` in Infra.Sql); writes available through Application port (adapter acceptable for foundation) |
| **4** | Action ready | `IProcessActionService` with migrated foundation handlers; no direct `SiNetSQL.Domain.Actions` from WPF |
| **5** | Work Surface ready | Levels 1–4 + `ITaskQueryService` + no missing handler for actions the surface exposes + no direct SiNetSQL dependency in `SiNet.App.Wpf` |

### Current level (as of 2026-07-05)

| Area | Level | Notes |
| --- | --- | --- |
| Task navigation | **1** ✅ | Native `SqlTaskNavigationService` |
| Task completion | **2** ✅ | Native `SqlTaskCompletionService`; needs host-bound `IWorkflowCommandService` for auto-advance |
| Task query | **5** ✅ | Native `SqlTaskQueryService` + bucket fields |
| Task queue (buckets) | **5** ✅ | `ITaskQueueService` / `SqlTaskQueueService`; legacy `TaskPriorityEngine` delegates to `TaskQueuePriorityEngine` |
| Workflow reads | **3** ✅ | `WorkflowQueryService` in Infra.Sql |
| Workflow writes | **3** ⚠️ partial | `IWorkflowCommandService` via **temporary** `WorkflowCommandServiceAdapter` in SiNetSQL |
| Actions (foundation) | **4** ⚠️ partial | SendNotification, SetProjectStatus, RecordTaskResult, CreateStageTasks (marker), SetBillingPending |
| Actions (heavy) | **0** | MoveToProject, AddMaterial, StartWorkflow, ACC/file — **deferred** |
| **Overall New System** | **~Level 4 partial** | Sufficient for read-only surfaces + task-driven Inspection; **not** Level 5 for filing/ProjectWork |

**Gap to Level 5 (full):** native `IWorkflowCommandService` impl in Infra.Sql; heavy action handlers; ACC write / Gmail send policies.

---

## Work Surface readiness matrix

| Surface | TaskNav | TaskQuery | TaskCompletion | WorkflowCommand | Actions | Gmail | ACC write | Foundation status | Can start now? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **Email read-only** | — | — | — | — | — | read | — | Gmail read slice exists | **Yes** |
| **Email filing** | ✓ | ✓ | ✓ | adapter | MoveToProject… | read | write | Blocked | **No** — needs MoveToProject + ACC write policy |
| **Inspection read-only** | ✓ | optional | — | — | — | — | — | Native nav; legacy workspace optional | **Yes** (preview) |
| **Inspection task completion** | ✓ | ✓ | ✓ | adapter | foundation | — | — | Levels 1–2 + adapter | **Yes** (with V2 host DI) |
| **ProjectWork** | ✓ | ✓ | ✓ | adapter | heavy + file | — | write | Blocked | **No** |
| **Reports** | — | — | — | — | — | — | — | Not in foundation scope | **No** (deferred) |
| **WorkflowDashboard** | — | — | — | read native; write adapter | transition | — | — | Read native; admin write legacy | **Partial** — read-only dashboard slice only |
| **TaskPanel / FloatingTasks replacement** | ✓ | ✓ bucket-aware | — | — | — | — | — | Query + queue native | **Partial** — read-only Task Panel **unblocked**; admin writes still legacy |
| **ACC operator (read)** | — | — | — | — | — | — | read | ACC read foundation closed | **Yes** |
| **ACC write / file surface** | — | — | — | — | file | — | write | ACC-Write-Policy | **No** |

---

## Task Queue Buckets foundation (2026-07-05)

Design source:
[`SiNetProjectManagerV2/Docs/Domains/ProjectWork/PersonalWorkQueuesByTaskSize-2026-06-23.md`](../SiNetProjectManagerV2/Docs/Domains/ProjectWork/PersonalWorkQueuesByTaskSize-2026-06-23.md).

Implementation:

| Layer | Location |
| --- | --- |
| Model | `ProjectAssignment.WorkQueueBucket` (default Medium=2); `TaskType.DefaultWorkQueueBucket` |
| Constants | `WorkQueueBucketCodes` in `src/SiNet.Application/Tasks/` |
| Migration | **Applied** — user-managed `AddTaskWorkQueueBuckets` (20260705083841) |
| Priority engine | `TaskQueuePriorityEngine` in `src/SiNet.Infrastructure.Sql/Services/Tasks/` |
| Legacy shim | `SiNetSQL/Services/TaskPriorityEngine.cs` delegates to shared engine |
| Read | `ITaskQueryService` returns bucket; optional bucket filter |
| Write | `ITaskQueueService` — `GetUserQueueAsync`, `MoveWithinBucketAsync`, `ChangeBucketAsync`, `ValidateAndRepairQueueAsync` |
| Legacy task ops | `TaskService.ChangeTaskBucket`, bucket-aware `ReassignTask` / `ChangeTaskStatus` / `ReorderTask` |

**Task Panel read-only pilot started (2026-07-05).** Write queue operations (`ChangeBucket`,
`MoveWithinBucket`, close/complete) remain deferred — see
[`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md) § Task Panel read-only.

**Task Panel read-only** must consume `ITaskQueryService` (+ optional `ITaskQueueService` later for
reorder) — **not** a single flat list.

**Not created:** `Queue` table, `QueueItem` table, new router, AI bucket classification.

### First recommended Work Surface (after this closure)

1. **Email read-only** — already on native Gmail port; no task/action foundation required  
2. **Inspection task-driven** — needs V2 host with `AddSiNetProcessBackbone()` + `AddSiNetWorkflowCommands()`  
3. **Task list panel (read-only)** — needs bucket-aware `ITaskQueryService` (+ optional `ITaskQueueService` for reorder later)  

**Do not start first:** Email filing, ProjectWork, ACC write surfaces.

---

## Answer: can we start Work Surfaces now?

**Partial**

| Allowed to start | Blocked |
| --- | --- |
| Email read-only (`EmailWindowView` read slice) | Email filing |
| Inspection task open + complete (V2 New System host) | ProjectWork |
| Read-only task list for a project/user (**three personal bucket queues**) | Reports |
| ACC operator read surfaces | ACC write / upload |
| Workflow dashboard **read** slices | Any surface needing MoveToProject / AddMaterial |

**Missing before blocked items:** MoveToProject / AddMaterial handler migration; ACC write policy; Gmail send/modify (G-Policy); native workflow command impl (optional — adapter sufficient for Inspection completion).

---

## Closure roadmap

| Phase | Status | Summary |
| --- | --- | --- |
| 1 — TaskQuery | ✅ Done | `SqlTaskQueryService` + DTOs + DI + tests |
| 2 — Minimal actions | ✅ Done | Foundation handlers + catalog + tests |
| 3 — WorkflowCommand | ⚠️ Documented | Temporary adapter in SiNetSQL; host binds `AddSiNetWorkflowCommands()` |
| 4 — Readiness signoff | ✅ Done | This document + matrix + tests |
| 4b — Task Queue Buckets | ✅ Done | Bucket-aware queue engine + ports; user-managed migration applied |
| 5 — First Work Surface | **In progress** | Email read-only ✅; **Task Panel read-only pilot started**; Inspection optional |

See full component tables in [`MIGRATION_MAP.md`](./MIGRATION_MAP.md) § Process backbone foundation migration.

---

## Workflow component decisions

| Component | Decision | Blocker if deferred |
| --- | --- | --- |
| `IWorkflowQueryService` / `WorkflowQueryService` | **Move now** ✅ | — |
| `WorkflowCommandServiceAdapter` | **Wrap temporarily** | None if host binds adapter |
| `WorkflowEngine` | **Defer** — orchestrator internals | Advanced workflow admin UI |
| `WorkflowTaskOrchestrator` | **Defer** — provisioning graph | Creating tasks from New System |
| `WorkflowActionExecutor` | **Wrap temporarily** | Legacy transition runtime only |
| `WorkflowStageTaskProvisioningService` | **Defer** | Same as orchestrator |
| `WorkflowTransitionEvaluator` | **Defer with blocker** — pure rules but coupled to engine | Native workflow command impl |
| `WorkflowSeedService` | **Migrated (DEBUG)** — New System `SqlWorkflowSeedService`; legacy SiNetSQL copy retained | Dev/seed tooling |

---

## Dev Tools seed/reset migrated to New System (2026-07-05)

See [`DEV_TOOLS.md`](./DEV_TOOLS.md).

| Layer | Location |
| --- | --- |
| Ports | `IDevDataResetService`, `IStaticSeedService` in `SiNet.Application.DevTools` |
| Reset | `SqlDevDataResetService` |
| Task seed | `SqlTaskManagementSeedService` |
| Workflow seed | `SqlWorkflowSeedService` |
| Demo tasks | `SqlTaskDemoSeedService` |
| UI | NewShell DEBUG menu via `DevToolsCoordinator` |

Legacy `SiNetSQL.Services.DevDataResetService` / `MainWindow.DevResetData_Click` remain — not target architecture.

## Tests

`src/SiNet.App.Wpf.Tests/Boundary/ProcessBackboneBoundaryTests.cs` — DI, navigation, completion, query, actions, doc guards, WPF boundary.

`src/SiNet.App.Wpf.Tests/Boundary/TaskQueueBucketsFoundationTests.cs` — bucket scenarios from design doc §13.

`src/SiNet.App.Wpf.Tests/Boundary/DevToolsBoundaryTests.cs` — dev reset/seed migration guards.

---
