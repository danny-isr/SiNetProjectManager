# Workflow Command / Application-Service Assessment (Option 1)

> **Status:** Assessment complete (P0) — **P1, P2, P3b, P4, and P6 executed** (port introduced behind an
> adapter; pilot consumer switched in P2; all WPF service-locator orchestrator usages removed and
> the command registration modularized in P3b; same-assembly write-path callers migrated in P4;
> result-record boundary consolidated and the last write-path orchestrator dependency
> (`TaskCompletionCoordinator`) moved onto the command port in **P6**). The optional full-relocation
> **P5** remains a future approval gate.
> **Scope guard:** This document is the agreed deliverable for the "Option 1" next round.
> Each remaining phase below is its own future approval gate.
> **Builds on:** the accepted `Workflow write API DTO boundary: accepted transitional state`
> (see `MIGRATION_MAP.md` §A3).

---

## 1. Purpose

The read slice (A1/A2) moved workflow **queries** behind an Application-layer port
(`SiNet.Application.Workflow.IWorkflowQueryService`) that returns DTOs only; EF entities never
cross it. A3 then made the **write** orchestrator's *public result records* DTO-clean, but the
write **operations** themselves are still invoked as concrete methods on a concrete
infrastructure class (`SiNetSQL.Services.Workflow.WorkflowTaskOrchestrator`).

This assessment answers: **what would it take to move workflow writes behind an
Application-layer command/handler port, mirroring the read pattern — and what is the coupling
that makes the naive version unsafe?** It produces a target sketch, a coupling map, and a
phased cut plan, so the deferred "full engine + provisioning conversion" can be sized before
any high-blast-radius code is touched.

---

## 2. Current write surface (verified)

### 2.1 Public command-shaped methods on `WorkflowTaskOrchestrator`

`SiNetSQL/Services/Workflow/WorkflowTaskOrchestrator.cs` — the de-facto write application service today:

| Line | Method | Returns | Command-like? |
| --- | --- | --- | --- |
| 42 | `StartWorkflowAsync(definitionId, projectId, triggerType, triggerEntityId, userId, notes, ct, isProjectBound, initialStageCode)` | `WorkflowStartResult` | ✅ command |
| 87 | `AdvanceWithTasksAsync(instanceId, targetStageId, userId, notes, ct)` | `WorkflowAdvanceResult` | ✅ command |
| 216 | `CheckAndAutoAdvanceAsync(taskId, userId, ct)` | `StageCompletionResult?` | ✅ command (event-driven) |
| 306 | `CheckAndAutoAdvanceStalledWorkflowAsync(instanceId, userId, ct)` | `StageCompletionResult?` | ✅ command (watchdog) |
| 441 | `ExecuteTransitionAsync(rule, instanceId, userId, ct)` | `StageCompletionResult` | ✅ command (internal+public) |
| 486 | `EvaluateManualTransitionsAsync(...)` | `StageCompletionResult?` | ◐ query/command hybrid |
| 172 | `CreateStageTasksAsync(instanceId, stageId, userId, ct)` | `List<ProjectAssignment>` | ◐ provisioning helper |
| 187 | `IsStageCompleteAsync(instanceId, ct)` | `bool` | ○ query |
| 507 | `GetStageTaskProgressAsync(...)` | `StageTaskProgress` | ○ query |
| 688 | `PreflightAdvanceAsync(instanceId, targetStageId, ct)` | `void` | ○ guard |

**Result records (already DTO-clean after A3)** — `WorkflowTaskOrchestrator.cs` lines 812-845:

- `WorkflowStartResult(WorkflowInstanceDto Instance, IReadOnlyList<ProjectAssignmentSummaryDto> CreatedTasks)`
- `WorkflowAdvanceResult(WorkflowInstanceDto Instance, IReadOnlyList<ProjectAssignmentSummaryDto> CreatedTasks)`
- `StageCompletionResult(int InstanceId, int CompletedStageId, StageCompletionAction Action, WorkflowInstanceDto? AdvancedInstance, int? TargetStageId, int? TransitionRuleId)`
- `StageTaskProgress(...)` (pure value record)
- `enum StageCompletionAction { AutoAdvanced, ManualAdvanceRequired, AutoAdvanceFailed, ConfirmationRequired }`

> **Key observation:** the public *outputs* are already DTOs/value records. **C6 is now closed**
> (P1): `CreatedTasks` is `IReadOnlyList<ProjectAssignmentSummaryDto>`. The only remaining
> infrastructure tie is the **location and type** of the methods (concrete class in the
> `SiNetSQL` project) — addressed by the later phases.

### 2.2 Engine lifecycle methods (entity-returning, internal by design)

`SiNetSQL/Services/Workflow/WorkflowEngine.cs`:

| Line | Method | Returns |
| --- | --- | --- |
| 34 | `StartAsync(...)` | `WorkflowInstance` (EF entity) |
| 111 | `AdvanceStageAsync(...)` | `WorkflowInstance` |
| 177 | `PauseAsync(...)` | `WorkflowInstance` |
| 201 | `ResumeAsync(...)` | `WorkflowInstance` |
| 225 | `CompleteAsync(...)` | `WorkflowInstance` |
| 250 | `CancelAsync(...)` | `WorkflowInstance` |

These are **intentionally entity-based** and were left untouched in A3.

---

## 3. The coupling map (why "engine returns DTOs" is unsafe)

The blocker is a **recursive entity round-trip** between the orchestrator, the provisioning
service, and the engine. Verified in
`SiNetSQL/Services/Workflow/WorkflowStageTaskProvisioningService.cs` lines 49-104:

```
StartWorkflowAsync (orchestrator, line 58)
  └─> _engine.StartAsync(...) ──────────────► WorkflowInstance  (entity)
  └─> _provisioning.EnsureInitialStageTasksAsync(instance, …)   (line 64; entity IN)
		├─ AutoAdvancePastStartNodeAsync(instance, …)           (line 58; entity IN/OUT)
		├─ if stage.NodeType == "SubWorkflow":
		│     └─> _engine.StartAsync(subDefId, …)               (line 82; entity OUT)
		│           └─> EnsureInitialStageTasksAsync(subInstance, …)  ◄── RECURSION (line 92)
		└─ else: CreateStageTasksAsync(instance.Id, stageId, …) (line 100)
		returns (WorkflowInstance, List<ProjectAssignment>)     (line 104; entity OUT)
```

### Coupling facts

| # | Fact | Source | Consequence for a command port |
| --- | --- | --- | --- |
| C1 | `EnsureInitialStageTasksAsync` takes **and returns** a live `WorkflowInstance`. | Provisioning 49-104 | The engine cannot return a DTO without breaking this signature chain. |
| C2 | It calls `_engine.StartAsync` **recursively** for `SubWorkflow` nodes and re-enters itself. | Provisioning 82, 92 | Sub-workflow auto-start must stay inside one entity-tracked unit of work. |
| C3 | `AutoAdvancePastStartNodeAsync` is entity-in/entity-out. | Provisioning 58 | Start-node skip is part of the same entity flow. |
| C4 | Orchestrator's parent-notification (`NotifyParentOfSubWorkflowCompletionAsync`) reads `childInstance.ParentWorkflowInstanceId` and calls `ExecuteTransitionAsync` mid-advance. | Orchestrator 107-158 | Auto-advance chaining is interleaved with entity state. |
| C5 | The provisioning service deliberately has **minimal deps** (`IDbContextFactory`, `WorkflowEngine`) to **avoid a DI cycle** with action handlers. | Provisioning 22-28 doc | A command/handler layer must not reintroduce that cycle. |
| C6 | ~~`CreatedTasks` is `List<ProjectAssignment>` — an **EF model type** still on the public output.~~ **CLOSED (P1):** now `IReadOnlyList<ProjectAssignmentSummaryDto>`. (`ProjectAssignment` type is `SiNetSQL.Models`, physically in `SiNet.Infrastructure.Sql`.) | Orchestrator 812-818 + `ProjectAssignmentSummaryDto` | ✅ Resolved — write contract is now fully DTO. |

**Conclusion:** the engine + provisioning recursion is a single cohesive **entity-tracked
transaction**. A command port must wrap this unit at the **orchestrator boundary** (where A3
already maps to DTOs), not push DTOs down into the engine. This validates the A3 decision and
sets the seam for any future extraction.

---

## 4. Target port sketch (illustrative — NOT to be created in this round)

Mirroring `IWorkflowQueryService`, a write port would live in `SiNet.Application.Workflow` and
expose DTO-only inputs/outputs. Two viable shapes:

### Option A — single command application service (lowest churn)

```
// ILLUSTRATIVE ONLY — do not add in this round
namespace SiNet.Application.Workflow;

public interface IWorkflowCommandService
{
	ValueTask<WorkflowStartResultDto>     StartAsync(StartWorkflowCommand cmd, CancellationToken ct);
	ValueTask<WorkflowAdvanceResultDto>   AdvanceAsync(AdvanceWorkflowCommand cmd, CancellationToken ct);
	ValueTask<StageCompletionResultDto?>  CheckAndAutoAdvanceAsync(TaskClosedCommand cmd, CancellationToken ct);
	ValueTask<StageCompletionResultDto?>  CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand cmd, CancellationToken ct);
}
```

- Inputs become small command DTOs (the current long parameter lists collapse into records).
- `WorkflowStartResult`/`WorkflowAdvanceResult`/`StageCompletionResult` graduate from the
  `SiNetSQL` namespace into `SiNet.Application.Workflow` (they are already DTO-backed; only
  `List<ProjectAssignment>` needs a DTO replacement — see C6).
- Implementation = today's `WorkflowTaskOrchestrator`, relocated/wrapped behind the port; the
  engine + provisioning chain stays **exactly as-is** underneath.

### Option B — discrete command + handler per operation (CQRS-style)

Higher ceremony (one command/handler/result per operation). Only worth it if the team wants
per-command pipeline behaviors (validation, logging, transactions) as cross-cutting decorators.
**Not recommended** for this codebase's current size — Option A matches the existing read-port
style and the DI conventions already in place.

### Result-DTO gap to close first (C6)

`CreatedTasks: List<ProjectAssignment>` is the only non-DTO member on the write contract. Before
any port extraction, a `ProjectAssignmentSummaryDto` (id, title, assignee, status, priority,
link role) would need to exist and a mapper added — analogous to `WorkflowDtoMappings`. This is
the smallest, safest *first* code increment if/when execution is approved.

---

## 5. Consumers & DI (blast radius sizing)

**DI registration:** `WorkflowTaskOrchestrator` is registered in the **legacy** host composition
— `SiNetProjectManagerV2/App.xaml.cs:268` (`services.AddTransient<…WorkflowTaskOrchestrator>()`),
**not** in the clean `SiNet.App.Composition` root. A future port would register through a new
`AddSiNetWorkflowCommands()` module (parallel to `AddSiNetWorkflowReads()`), then the legacy
forwarder would be removed. *(This overlaps Option 2 — composition cleanup — and is a natural
sequencing dependency.)*

**Direct callers of the write methods (verified earlier in A3 audit + this round):**

| Consumer | Methods used | Notes |
| --- | --- | --- |
| `SiNetSQL/Services/EmailContext/ActionExecutor.cs` | `IWorkflowCommandService.StartAsync` | ✅ **Migrated (P2)** — now depends on the Application port; reads `.Instance.Id`, `.CreatedTasks.Count` from `WorkflowStartResultDto`. |
| `SiNetProjectManagerV2/Dialogs/WorkflowManagementWindow.xaml.cs` | `StartWorkflowAsync` (dashboard tab) | ✅ **Migrated (P3b)** — `_dashboardOrchestrator` replaced with `IWorkflowCommandService`; starts via `StartWorkflowCommand` + `WorkflowTriggerTypeDto.Manual`. |
| `SiNetProjectManagerV2/Dialogs/WorkflowCreateProjectWindow.xaml.cs` | `StartWorkflowAsync` (continuation auto-start) | ✅ **Migrated (P3b)** — local orchestrator resolve replaced with `IWorkflowCommandService`; passes `InitialStageCode` via `StartWorkflowCommand`. |
| `SiNetProjectManagerV2/WPFUserControl/WorkflowDashboardView.xaml.cs` → `WorkflowDashboardViewModel` | `StartWorkflowAsync` | ✅ **Migrated (P3b)** — view-model now injects `IWorkflowCommandService`; code-behind resolves the port instead of the orchestrator. |
| `SiNetProjectManagerV2/WPF Window/WorkflowInstanceWindow.xaml.cs` → `WorkflowInstanceViewModel` | `AdvanceWithTasksAsync` + stage progress | ✅ **Migrated (P3b)** — VM now injects `IWorkflowCommandService` (advance) **and** `IWorkflowQueryService` (`GetStageTaskProgressAsync`); `StageProgress` retyped to `StageTaskProgressDto`. `WorkflowEngine` retained for pause/resume (out of scope). |
| `SiNetSQL/Domain/Actions/Handlers/StartWorkflowProcessActionHandler.cs` | `StartWorkflowAsync` | ✅ **Migrated (P4)** — injects `IWorkflowCommandService`; starts via `StartWorkflowCommand` + `WorkflowTriggerTypeDto.Email`; result retyped to `WorkflowStartResultDto`. |
| `SiNetSQL/Services/TaskLifecycle/TaskLifecycleService.cs` | `StartWorkflowAsync`, `CheckAndAutoAdvanceAsync` | ✅ **Migrated (P4)** — primary-ctor now injects `IWorkflowCommandService`; auto-start via `StartWorkflowCommand`, task-closed via `TaskClosedCommand`. |
| `SiNetSQL/Services/Workflow/StalledWorkflowWatchdog.cs` | `CheckAndAutoAdvanceStalledWorkflowAsync`, `CheckAndAutoAdvanceAsync`, `CreateStageTasksAsync` | ✅ **Migrated (P4, partial)** — both auto-advance calls now go through `IWorkflowCommandService` (`StalledWorkflowCommand` / `TaskClosedCommand`). **Orchestrator dependency retained** for `CreateStageTasksAsync` (no port equivalent). |
| `SiNetSQL/Services/Tasks/TaskCompletionCoordinator.cs` | `CheckAndAutoAdvanceAsync` | ✅ **Migrated (P6)** — now injects `IWorkflowCommandService?` and routes auto-advance via `TaskClosedCommand`. The carrier `TaskCompletionResult.StageAdvanceResult` was retyped to `StageCompletionResultDto?` (verified write-only: zero member-access reads across both repos), so the public contract is now DTO-clean. This removes the **last** production write-path dependency on the concrete orchestrator. |
| `SiNetSQL/Services/Workflow/WorkflowActionCompletedHandler.cs` | `ExecuteTransitionAsync` | ⚪ **Retained on orchestrator (P4)** — `ExecuteTransitionAsync` has **no** Application write-port equivalent; stays public. |
| `SiNetSQL/Services/ReviewWorkflowInitiationService.cs` | `StartWorkflowAsync` | ⚪ **Not migrated (P4)** — entire file is dead code (`#if false`, zero callers, not in DI); left untouched per safety rules. |
| `Domain/Actions/Handlers/StartSubWorkflowProcessActionHandler.cs` | `EnsureInitialStageTasksAsync` (via provisioning, not orchestrator) | The DI-cycle-avoidance path (C5). |
| Tests: `WorkflowTaskOrchestratorTests`, `SubWorkflowOrchestrationRuntimeTests`, `Proposal*`/`ApproveOrClose*`/`AllActiveWorkflowsExecution` E2E | start/advance/auto-advance | ⚪ **Updated for DTO results (P6)** — still call the orchestrator directly via existing `InternalsVisibleTo("SiNetSQL.Tests"/"SiNetSQL.E2ETests")`; assertions retyped to `WorkflowStartResultDto` and `StageCompletionActionDto.AutoAdvanced` to match the new DTO-returning signatures. No behavior change. |

**Sizing correction (discovered during P3b):** the original inventory mis-identified the
service-locator targets as `TaskOperationHelper` / `FloatingProjectTasksViewModel`; neither
references `WorkflowTaskOrchestrator`. The real concrete-orchestrator consumers are the four WPF
sites above (two windows + one user-control view-model + one instance view-model). All four were
migrated in P3b. **P4 (done)** then migrated the same-assembly production write-path callers that
have a port equivalent (`StartWorkflowProcessActionHandler`, `TaskLifecycleService`, and the two
auto-advance calls in `StalledWorkflowWatchdog`) and marked the four port-covered orchestrator
write methods `internal`. **P6 (done)** consolidated the result-record boundary and migrated
`TaskCompletionCoordinator` onto the command port, so the only same-assembly callers still bound to
the concrete orchestrator are `StalledWorkflowWatchdog.CreateStageTasksAsync` and
`WorkflowActionCompletedHandler.ExecuteTransitionAsync` — both methods that have **no** port
equivalent and remain public by design.

---

## 6. Phased cut plan (each phase = its own approval gate)

> Ordering favors smallest-safe-first. No phase below is authorized by this document.

| Phase | Scope | Risk | Reversibility | Stop condition |
| --- | --- | --- | --- | --- |
| **P0** *(this doc)* | Assessment: surface, coupling map, target sketch, plan. | None | Total | ✅ Done — this file. |
| **P1** | Add `ProjectAssignmentSummaryDto` + mapper (close C6). Keep methods where they are; just change result records' `List<ProjectAssignment>` → `IReadOnlyList<ProjectAssignmentSummaryDto>` at the orchestrator boundary (same pattern as A3). | Low | High | ✅ **Done** — build green + 21/21 workflow tests green; no engine/provisioning edits. |
| **P2** | Introduce `IWorkflowCommandService` (Option A) in `SiNet.Application.Workflow` and **adapter-implement** it by delegating to the existing `WorkflowTaskOrchestrator` (no logic moved yet). | Low–Med | High | ✅ **Done** — port + command/result DTOs added; `WorkflowCommandServiceAdapter` delegates to the orchestrator; `ActionExecutor` switched as the pilot consumer (now depends on the port, not the concrete orchestrator); full solution build green + 60/60 workflow tests green. |
| **P3b** | Add a modular `AddSiNetWorkflowCommands()` registration; move the write-port registration out of the inline `App.xaml.cs` block; remove **all** WPF service-locator orchestrator usages (4 sites) in favor of the injected `IWorkflowCommandService`; expand the read port with `GetStageTaskProgressAsync` → `StageTaskProgressDto` so `WorkflowInstanceViewModel` can leave the concrete orchestrator. | Medium *(runtime DI)* | Medium | ✅ **Done** — solution build green; 41 workflow tests green (engine, execution, health, continuation, policy read-port, approve/close E2E). **Placement note:** `AddSiNetWorkflowCommands()` had to live in the **SiNetSQL** assembly (`Services/Workflow/WorkflowCommandsServiceCollectionExtensions.cs`), not `SiNet.Infrastructure.Sql`, because the infra project references only `SiNet.Application` + `SiNet.Domain` and cannot see `WorkflowTaskOrchestrator`/`WorkflowCommandServiceAdapter`. The infra read-port impl mirrors the stage tag inline (`$"Stage:{id}"`) for the same reason (`WorkflowConstants` lives in SiNetSQL). |
| **P4** | Migrate the same-assembly production write-path callers that have a port equivalent (`StartWorkflowProcessActionHandler`, `TaskLifecycleService`, the two auto-advance calls in `StalledWorkflowWatchdog`) to `IWorkflowCommandService`; mark the four port-covered orchestrator write methods `internal`. | Medium | Medium | ✅ **Done** — solution build green; 29/29 workflow E2E tests green. The four write methods (`StartWorkflowAsync`, `AdvanceWithTasksAsync`, `CheckAndAutoAdvanceAsync`, `CheckAndAutoAdvanceStalledWorkflowAsync`) are now `internal`; `IWorkflowCommandService`/`WorkflowCommandServiceAdapter` is the public seam. **Retained on the orchestrator (same-assembly access):** `StalledWorkflowWatchdog.CreateStageTasksAsync` and `WorkflowActionCompletedHandler.ExecuteTransitionAsync` (no port equivalent), and `TaskCompletionCoordinator.CheckAndAutoAdvanceAsync` (its result feeds the public `TaskCompletionResult.StageAdvanceResult`). Tests left as-is — covered by existing `InternalsVisibleTo`. `ExecuteTransitionAsync` + provisioning/query helpers stay public. |
| **P5** *(optional/deferred)* | Only if desired: relocate orchestration logic fully into the Application/Infrastructure split. Engine + provisioning recursion stays entity-based regardless (C1–C5). | High | Low | Separate proposal; not in scope of the command-port effort. |
| **P6** *(Workflow Consolidation Block)* | Consolidate the write result-record boundary and reduce the remaining concrete-orchestrator footprint inside the Workflow boundary: retarget the four `internal` write methods to return Application DTOs directly (`WorkflowStartResultDto` / `WorkflowAdvanceResultDto` / `StageCompletionResultDto?`); delete the orphaned infra `WorkflowStartResult` / `WorkflowAdvanceResult` records; collapse the adapter to thin pass-throughs; migrate `TaskCompletionCoordinator` to `IWorkflowCommandService` and retype `TaskCompletionResult.StageAdvanceResult` to the DTO. | Medium | Medium | ✅ **Done** — full `SiNetProjectManager.sln` build green (VS MSBuild, exit 0); **64/64** workflow tests green across two runs. Infra→DTO mapping now lives once in the orchestrator (`MapToDto`); the adapter only maps the inbound `WorkflowTriggerTypeDto`. `ExecuteTransitionAsync` / `EvaluateManualTransitionsAsync` deliberately stay infra-typed (`StageCompletionResult`) because `WorkflowActionCompletedHandler` consumes that contract — porting it needs the deferred P5 redesign. No schema / migration / `ModelSnapshot` changes. |

**Hard invariants for every phase (carried from repo rules + A3):**

- No schema / migration / `ModelSnapshot` / EF-mapping changes.
- `WorkflowEngine` and `WorkflowStageTaskProvisioningService` recursion stay entity-based.
- No new DI cycle between action handlers and the provisioning/command layer (C5).
- Do not touch `TokenProvider`, `Bim360Service`, ACC bootstrap, or auth flows.

---

## 7. Recommendation

- **The A3 boundary is the correct seam.** The coupling map confirms writes must be wrapped at
  the orchestrator level, not pushed into the engine.
- **P1, P2, P3b, P4, and P6 are done.** The `ProjectAssignment` → DTO gap is closed, a DTO-only
  `IWorkflowCommandService` now fronts the orchestrator via a thin adapter, **all WPF consumers
  that previously reached the concrete orchestrator through the service locator now depend on the
  Application ports** (`IWorkflowCommandService` for writes, `IWorkflowQueryService` for the new
  stage-progress read), the same-assembly production write-path callers with a port equivalent
  were migrated in P4, and **P6** consolidated the result-record boundary while moving the last
  write-path caller (`TaskCompletionCoordinator`) onto the command port.
- **P4 tightened visibility and P6 consolidated the result boundary:** the four port-covered
  orchestrator write methods are `internal` **and now return Application DTOs directly**, so
  `IWorkflowCommandService`/`WorkflowCommandServiceAdapter` is the only public write seam and the
  adapter carries no result mapping. The orphaned infra result records (`WorkflowStartResult`,
  `WorkflowAdvanceResult`) were deleted; infra→DTO mapping is centralized in the orchestrator.
- **Remaining Workflow debt (intentional):** `WorkflowActionCompletedHandler.ExecuteTransitionAsync`
  and `StalledWorkflowWatchdog.CreateStageTasksAsync` stay on the concrete orchestrator because they
  have no port equivalent and are entity-coupled (`WorkflowStageTransition`, `ProjectAssignment`).
  `ExecuteTransitionAsync` / `EvaluateManualTransitionsAsync` therefore keep the infra
  `StageCompletionResult` contract. Closing these requires the deferred **P5** redesign.
- **Only P5 remains** (optional/deferred): relocating orchestration logic fully into the
  Application/Infrastructure split. It is a separate proposal, not part of the command-port effort.

---

## 8. Execution status

P0 (assessment) is the original deliverable. **P1, P2, P3b, P4, and P6 have since been executed** under
explicit approval: the write contract is fully DTO, `IWorkflowCommandService` exists in the
Application layer, `WorkflowCommandServiceAdapter` implements it over the unchanged orchestrator,
and every WPF service-locator orchestrator usage now resolves the Application ports instead. The
read port gained `GetStageTaskProgressAsync` (returning `StageTaskProgressDto`) so the instance
view-model no longer touches the concrete orchestrator. The command registration is modularized in
the SiNetSQL assembly (placement forced by the infra project's reference graph — see the P3b row).
**P4** migrated the same-assembly production write-path callers with a port equivalent and marked
the four port-covered orchestrator write methods `internal`. **P6 (Workflow Consolidation Block)**
then retargeted those four `internal` methods to return Application DTOs directly, deleted the
orphaned infra `WorkflowStartResult` / `WorkflowAdvanceResult` records, reduced the adapter to thin
pass-throughs, and migrated `TaskCompletionCoordinator` onto `IWorkflowCommandService` (retyping
`TaskCompletionResult.StageAdvanceResult` to `StageCompletionResultDto?`). Full-solution build is
green (VS MSBuild, exit 0) and the workflow test suite is green (**64/64** across two runs). No
schema / migration / `ModelSnapshot` changes were made.
**Only P5 remains** (optional/deferred) — await explicit approval before any further code change.
