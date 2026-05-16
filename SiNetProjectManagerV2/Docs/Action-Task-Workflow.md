# Action → Task → Workflow Architecture

> **Status:** Documentation only. No code changes, no DB changes, no migrations,
> no refactor, no deletion of legacy paths. This file is the single source of
> truth that the team will use to *agree* on the canonical model **before** any
> implementation step is taken.
>
> **TaskCreationDialog migration note (Phase 5 extension):**
> Migrated TaskCreation actions (see
> `SiNetSQL.Domain.Actions.Continuation.TaskCreationActionCatalog`) run on a
> **typed-only path with no legacy fallback**:
> `EmailContextViewModel` → `ITaskCreationContinuationApplicationService.StartAsync`
> → `TaskCreationContinuationRequest` → `IActionContinuationUiHost`
> (`TaskCreationDraftDialog`) → `TaskCreationContinuationResult(TaskDraft)`
> → `ITaskCreationContinuationApplicationService.ContinueAsync` →
> `TaskFactory.CreateAsync` → `ProjectAssignment`.
>
> If any required piece is missing, the action **fails visibly** (clear error
> + log). The legacy chain `ActionResult.RequiresUI(ActionFollowUp.TaskCreationDialog)`
> → `AssignActionDialog` → `EmailManagementView.CreateActionTaskAsync` is
> **not** used for migrated actions; it remains physically in the codebase
> only as dead code for migrated actions, or as the live path for any
> `SuggestedActionType` that is not yet listed in `TaskCreationActionCatalog`.
>
> **Repos in scope:** `danny-isr/SiNetProjectManager`, `danny-isr/SiNetSQL`.
>
> **Companion documents:**
> - [`Action-Handler-Inventory.md`](./Action-Handler-Inventory.md) — Phase 1
>   per-action mapping table (Source Family, Execution Path, Has Handler?,
>   Creates / Completes Task?, Advances Workflow?, Classification, Risk).
> - [`Typed-Continuation-Design.md`](./Typed-Continuation-Design.md) —
>   pre-Phase-2 design for replacing `RequiresUI` + `PrefilledData` with a
>   typed Continuation Request / Result contract. Pilot recommendation,
>   open decisions, and impact on the roadmap phases.

---

## 1. Overview

### 1.1 Problem this document solves

The system currently has three loosely-coupled subsystems that together drive
business behavior:

1. **Actions** — discrete intents (user-triggered or system-triggered) that
   represent "do something now".
2. **Tasks** (`ProjectAssignment`) — durable, assignable, completable units of
   work.
3. **Workflows** (`WorkflowInstance` + stages + transitions) — long-running
   state machines that govern how a project moves between phases.

These three subsystems were introduced incrementally. The result is:

- Multiple families of "actions" exist (`SuggestedAction`,
  `WorkflowTransitionAction`, `ActionCode`, `IProcessActionHandler`).
- Multiple entry points exist for executing them (`ActionExecutor`,
  `IProcessActionDispatcher`, `WorkflowActionExecutor`, ViewModels).
- Multiple bridges back to workflows exist (`IActionLifecycleReporter` →
  `WorkflowActionCompletedHandler`, `ITaskCompletionCoordinator`, direct
  orchestrator calls).
- One specific flow has been recently closed
  (**EmailFiling task → MoveToProject → coordinator → workflow advance flag**),
  but the wider architecture still has hybrid / legacy / bypass paths.

### 1.2 Architectural goal

A single, predictable pipeline:

```
UI (intent only)
	→ ActionCode
	→ IProcessActionDispatcher
	→ IProcessActionHandler
	→ (optional) RequiresUi → UI continuation → handler resumes
	→ Shared task service (creates / updates Task)
	→ ITaskCompletionCoordinator (closes work targets / task)
	→ Workflow advance signaled via lifecycle reporter
	→ WorkflowTaskOrchestrator.ExecuteTransitionAsync
```

with **no direct task writes** outside the task service / coordinator and
**no direct workflow advance** outside the orchestrator / completion path.

---

## 2. Terminology

| Term | Defined in | Meaning |
|---|---|---|
| **SuggestedAction** (DTO) | `SiNetSQL.DTOs.Email.SuggestedAction` | A single recommendation produced by `SuggestedActionsBuilder` from an `EmailContextResult`. Carries `ActionType`, display text, `PrefilledData`. |
| **SuggestedActionType** (enum) | `SiNetSQL.DTOs.Email.SuggestedActionType` | The closed enumeration of email-driven user actions (≈ 40 values: CreateNewProject, AddMaterialToProject, ApproveOrClose, …). |
| **ActionCode** (string) | `SiNetSQL.Domain.Actions.ActionCodes` | Stable string identifier that unifies every "action" the system can perform — across SuggestedActionType, WorkflowTransitionActionType, EmailManagementViewModel commands and FloatingInspectionViewModel commands. |
| **ActionDefinition / ActionDefinitionRegistry** | `SiNetSQL.Domain.Actions.ActionDefinitionRegistry` | Read-only metadata catalog: for each `ActionCode` describes domain area, whether it requires UI, default follow-up UI, possible outcomes, whether it can advance a workflow, etc. **Metadata only — never executes anything.** |
| **ProcessAction** | conceptual | The runtime form of an action, executed via the dispatcher/handler pair below. |
| **IProcessActionHandler** | `SiNetSQL.Domain.Actions.IProcessActionHandler` | Contract: one handler per `ActionCode`. Returns a typed `ProcessActionResult` (Completed / Deferred / Failed / NotSupported / NoOp). |
| **IProcessActionDispatcher** | `SiNetSQL.Domain.Actions.IProcessActionDispatcher` | Resolves an `IProcessActionHandler` for an `ActionCode` and invokes it. Returns `NotSupported` (never throws) when no handler is registered. |
| **WorkflowTransitionAction** (entity) | `SiNetSQL.Models.WorkflowTransitionAction` | A persisted action attached to a workflow transition rule. Has `ActionType` (`WorkflowTransitionActionType`), `SortOrder`, `ConfigJson`. |
| **WorkflowTransitionActionType** (enum) | `SiNetSQL.Models.WorkflowTransitionActionType` | The 8 system-triggered transition actions: CreateStageTasks, ClosePreviousStageTasks, SendNotification, StartSubWorkflow, SetProjectStatus, RecordTaskResult, SetBillingPending, CloseProject. |
| **Task / ProjectAssignment** | `SiNetSQL.Models.ProjectAssignment` | The durable work item. Has `TaskTypeId`, `StatusId`, `AssignedToId`, `TaskGroupId`, `LastTaskResultId`, `TaskLinks`. |
| **TaskLink** | `SiNetSQL.Models.TaskLink` | Polymorphic link from a task to a domain entity. Carries `Role` (Trigger / Source / WorkTarget), `LinkedEntityType`, `LinkedEntityId`, `IsWorkTarget`, `WorkStatus`. |
| **TaskInteractionDefinition** | `SiNetSQL.Services.Tasks.TaskInteractionDefinition` | Declarative metadata per TaskType: open mode, component key, completion policy, allowed task results, auto-close flag. |
| **ITaskCompletionCoordinator** | `SiNetSQL.Services.Tasks.ITaskCompletionCoordinator` | Single decision point for translating a UI completion event into: WorkTarget Done, ProjectAssignmentEvent (TaskResult), task closure, broad ProjectStatus change, `WorkflowAdvanced` request flag. |
| **IActionLifecycleReporter** | `SiNetSQL.Domain.Actions.IActionLifecycleReporter` | Reports Started / Completed / Failed / Cancelled / Deferred / NoOp around action execution. Composite implementation fans out to multiple subscribers. |
| **WorkflowActionLifecycleReporter** | `SiNetSQL.Services.Workflow.WorkflowActionLifecycleReporter` | Bridges *Completed* lifecycle events to `WorkflowActionCompletedHandler`. Other lifecycle events are no-ops. |
| **WorkflowActionCompletedHandler** | `SiNetSQL.Services.Workflow.WorkflowActionCompletedHandler` | Evaluates `ActionCompleted`-triggered transitions and, when mode is `Auto`, advances the workflow via the orchestrator. |
| **WorkflowTaskOrchestrator** | `SiNetSQL.Services.Workflow.WorkflowTaskOrchestrator` | The only component authorized to execute a workflow transition (create/close stage tasks, run transition actions, update instance state). |
| **Workflow advance** | conceptual | The transition of a `WorkflowInstance` from its current stage to a next stage. Allowed only via `WorkflowTaskOrchestrator.ExecuteTransitionAsync`. |
| **"RuntimeAction" / "DirectAction"** | n/a | **These terms are NOT defined in the codebase.** They appear in design discussions only. Avoid using them in code or documents from now on. |

---

## 3. Current Architecture

### 3.1 Entry points

```
┌──────────────────────────────────────────────────────────────────┐
│                              UI                                  │
│                                                                  │
│   EmailContextView      EmailManagementView       Floating /     │
│   (chooses action)      (commands on inbox)       TaskPanel      │
└───────┬─────────────────────────┬────────────────────────┬───────┘
		│                         │                        │
		▼                         ▼                        ▼
 SuggestedAction         EmailManagementVM            TaskPanelVM /
 chosen                  commands (Move/File/Tag)     FloatingProjectTasksVM
		│                         │                        │
		▼                         ▼                        ▼
 ActionExecutor            (in some cases reach           Resolver-driven
 .ExecuteAsync             into services directly)        open + inline edit
		│
		├──► [legacy inline switch] ──► returns ActionResult / RequiresUI
		│
		└──► IProcessActionDispatcher.DispatchAsync
							│
							▼
				  IProcessActionHandler (per ActionCode)
							│
							▼
				   ProcessActionResult
```

```
┌──────────────────────────────────────────────────────────────────┐
│                    Workflow transition path                      │
└──────────────────────────────────────────────────────────────────┘

 WorkflowTaskOrchestrator.ExecuteTransitionAsync
		│
		▼
 WorkflowActionExecutor.ExecuteActionsAsync
		│  (always — no fallback)
		▼
 IProcessActionDispatcher.DispatchAsync   ← MapFromWorkflowTransitionActionType
		│
		▼
 IProcessActionHandler (per ActionCode)
		│
		▼
 TranslateResult → ActionExecutionResult
```

```
┌──────────────────────────────────────────────────────────────────┐
│                  Task completion & advance path                  │
└──────────────────────────────────────────────────────────────────┘

 UI (task closed by user)
		│
		▼
 ITaskCompletionCoordinator.CompleteAsync(taskId, eventCode, resultCode, ...)
		│
		├── validate event ↔ task type ↔ result via ReviewCompletionEventBehavior
		├── mark TaskLinks (IsWorkTarget) Done
		├── add ProjectAssignmentEvent (TaskResult / StatusChange)
		├── if AutoCloseOnCompletion → close ProjectAssignment
		├── update Project.ProjectStatusId (broad codes only)
		└── return TaskCompletionResult { WorkflowAdvanced = true|false }

 Caller (e.g. EmailManagementVM) composes the orchestrator step:
		if (result.WorkflowAdvanced)
			→ WorkflowTaskOrchestrator advances
```

```
┌──────────────────────────────────────────────────────────────────┐
│                  Action → workflow advance path                  │
│             (independent of ITaskCompletionCoordinator)          │
└──────────────────────────────────────────────────────────────────┘

 ActionExecutor reports lifecycle
		│
		▼
 IActionLifecycleReporter (Composite)
		│
		▼
 WorkflowActionLifecycleReporter.ReportCompletedAsync
		│  (gates: WorkflowInstanceId present, ActionCode known,
		│   ActionDefinition.CanAdvanceWorkflow == true)
		▼
 WorkflowActionCompletedHandler.HandleAsync
		│
		▼
 WorkflowTransitionEvaluator (trigger = ActionCompleted)
		│
		▼
 If mode = Auto → WorkflowTaskOrchestrator.ExecuteTransitionAsync
 Else            → returned to caller for confirmation
```

### 3.2 What is wired today

| Concern | Component | Status |
|---|---|---|
| Email suggestion → action choice | `SuggestedActionsBuilder` | Active |
| User-action execution (email-bound) | `ActionExecutor` | Active — **hybrid** (legacy inline + dispatcher) |
| Workflow-transition action execution | `WorkflowActionExecutor` → `IProcessActionDispatcher` | Active — **strict** (no fallback) |
| Action lifecycle reporting | `CompositeActionLifecycleReporter` + `WorkflowActionLifecycleReporter` | Active |
| Action-driven workflow advance | `WorkflowActionCompletedHandler` | Active (Auto mode only) |
| Task completion / closure | `ITaskCompletionCoordinator` | Active for the EmailFiling family; **not** the only writer of `ProjectAssignment.Status` |
| Task interaction metadata | `TaskInteractionDefinition` + `ReviewTaskInteractionRegistry` | Active for Review TaskTypes |
| Task navigation from UI shell | `TaskNavigationResolver` | Active |

### 3.3 Action handlers actually registered (Process path)

Only a small subset of action codes currently have an `IProcessActionHandler`:

- `AddMaterialToProjectProcessActionHandler`
- `MoveToProjectProcessActionHandler`
- Handlers for each `WorkflowTransitionActionType` (CreateStageTasks,
  ClosePreviousStageTasks, SendNotification, StartSubWorkflow,
  SetProjectStatus, RecordTaskResult, SetBillingPending, CloseProject) —
  used exclusively by `WorkflowActionExecutor`.

The remaining ~30 `SuggestedActionType` values are still handled **inline**
inside `ActionExecutor.ExecuteCoreAsync`, mostly returning
`ActionResult.RequiresUI(...)`.

---

## 4. Canonical Desired Flow

> This section is **the target**. Today's code does not fully match it yet.

1. **UI chooses intent.** The UI emits an `ActionCode` (string) plus a
   context payload. It never decides task status, project status, or
   workflow stage.
2. **Dispatcher executes.** `IProcessActionDispatcher.DispatchAsync` resolves
   exactly one `IProcessActionHandler` and runs it. Missing handler =
   loud failure (no silent fallback).
3. **Handler may need UI.** When inputs are missing the handler returns
   `ProcessActionResult.Deferred(..., RequiresUi)` with a follow-up UI hint
   (currently `FollowUpUi` constants). The UI re-issues the same `ActionCode`
   with the missing data filled in. The handler is the only place that
   actually performs the business mutation.
4. **Task creation is centralized.** Any handler that needs to create a
   `ProjectAssignment` does so through `SmartTaskService` / `TaskFactory` —
   never via direct `db.ProjectAssignments.Add(...)`.
5. **Task completion is centralized.** Any handler / UI that needs to mark a
   task done calls `ITaskCompletionCoordinator.CompleteAsync` with a
   well-known `completionEventCode`. The coordinator is the only writer of
   `ProjectAssignment.StatusId = Completed`, of `WorkTargetStatus.Done`,
   and of broad `ProjectStatus` updates triggered by task completion.
6. **Workflow advance is centralized.** Only `WorkflowTaskOrchestrator.
   ExecuteTransitionAsync` advances a workflow. Callers reach it through
   either:
   - the `ITaskCompletionCoordinator.WorkflowAdvanced` signal (task path), or
   - `WorkflowActionLifecycleReporter` + `WorkflowActionCompletedHandler`
	 (action path).
7. **Lifecycle reporting wraps every action.** `ActionExecutor` /
   `WorkflowActionExecutor` always report Started + terminal event so the
   bridges above are reliably invoked.

---

## 5. Known Working Flow — `EmailFiling Task → Workflow Advance`

This flow is considered **closed** and is the reference implementation for
the canonical pipeline.

```
1. Workflow creates the task
   WorkflowTaskOrchestrator (during a transition)
   → handler for CreateStageTasks (no-op; orchestrator creates tasks)
   → ProjectAssignment with TaskType e.g. FileInitialMaterials
   → TaskLinks: Trigger=WorkflowInstance, Source=EmailInboxMessage,
				WorkTarget=EmailInboxMessage(s) with IsWorkTarget=true

2. User opens the task from FloatingProjectTasksView
   TaskNavigationResolver.ResolveAsync(taskId)
   → reads TaskInteractionDefinition for the TaskType
   → returns TaskNavigationRequest { ComponentKey=EmailFiling, ... }
   → shell opens EmailManagementView
   → EmailManagementViewModel.ActiveTaskContext = EmailFilingTaskContext

3. User triggers MoveToProject in EmailManagementView
   EmailManagementViewModel.MoveToProjectViaServiceAsync
   → IEmailMoveToProjectApplicationService.MoveAsync (VM-facing boundary,
     builds MoveToProjectRequest, filters IsPlacedHint items, owns UI feedback)
   → IProcessActionDispatcher.DispatchAsync(ActionCodes.MoveToProject,
     ActionExecutionContext { EmailFilingTask, Data["ExcludedAttachmentIds"] })
   → MoveToProjectProcessActionHandler.ExecuteAsync
     → duplicate-target validation against DB-persisted inbox attachments
     → IProjectFileFilingService files each tagged attachment
     → flips EmailInboxMessage.Status = Moved

4. Handler reports completion to the coordinator (when EmailFilingTask context exists)
   ITaskCompletionCoordinator.CompleteAsync(
	   taskId,
	   completionEventCode = ReviewCompletionEvents.ReviewMaterialFiled,
	   taskResultCode = null,
	   completedTaskLinkIds = [<the just-filed work targets>],
	   ...)
   → marks TaskLinks Done
   → if all WorkTargets Done → close task (AutoCloseOnCompletion=true)
   → returns TaskCompletionResult { WorkflowAdvanced }

5. Caller (or downstream bridge) advances workflow when signaled
```

---

## 6. Known Gaps / Bypasses

Each item below is **documented, not fixed**. Phase plan in §8.

### 6.1 RequiresUI without a clear backend continuation
Most `SuggestedActionType` cases in `ActionExecutor.ExecuteCoreAsync` return
`ActionResult.RequiresUI(...)` with a follow-up UI hint and stop there. There
is **no documented callback contract** describing:
- Which view consumes which `ActionFollowUp` value.
- Which handler / service is responsible for finalizing the action after the
  UI dialog closes.
- Where the resulting `ProjectAssignment` (if any) is created.

This is the largest open hole in the model.

### 6.2 Duplicate `MoveToProject` flow — **RESOLVED (Phase 3, end of Step 5)**

The duplicate-path issue is closed. The canonical pipeline is now:

```
EmailManagementViewModel
  → IEmailMoveToProjectApplicationService.MoveAsync
    → IProcessActionDispatcher.DispatchAsync(ActionCodes.MoveToProject,
                                             ActionExecutionContext)
      → MoveToProjectProcessActionHandler.ExecuteAsync
        → IProjectFileFilingService.FileAsync (per attachment)
        → ITaskCompletionCoordinator.CompleteAsync
          (only when ActionExecutionContext.EmailFilingTask is non-null)
```

The application service is preserved as the **VM-facing boundary** and is
responsible for: building `MoveToProjectRequest` -> `ActionExecutionContext`,
filtering `IsPlacedHint` items via `ExcludedAttachmentIds`, mapping
`ProcessActionResult` back to `MoveToProjectResult` for the VM, and emitting
UI-shaped status / `Progress<string>` updates. The handler owns the actual
business operation, duplicate-target validation (against DB-persisted inbox
attachments), and `ITaskCompletionCoordinator` reporting.

**Verification (Phase 3 Step 5):**
- `EmailManagementViewModel` does **not** reference `IProcessActionDispatcher`
  or `MoveToProjectProcessActionHandler`. The only service it resolves on the
  MoveToProject path is `IEmailMoveToProjectApplicationService`.
- Duplicate validation now reads inbox attachments from the DB inside the
  handler, not from in-memory UI state.
- Tests: `EmailMoveToProjectApplicationServiceTests` (12/12) and
  `MoveToProjectProcessActionHandlerTests` (11/11) green; full `SiNetSQL.Tests`
  green except for 2 pre-existing unrelated InMemory `ExecuteDeleteAsync`
  skips.

**Remaining caveats (intentionally not addressed in Phase 3):**
- **Typed Continuation:** Implemented end-to-end **only** for `ApproveOrClose`
  and `CloseOpinion` via `IActionContinuationUiHost` (WPF host:
  `WpfActionContinuationUiHost`, `ContinuationUiKind.WorkflowAdvanceDialog`
  only). Other `Deferred(RequiresUi)` results are still routed through the
  legacy `ActionFollowUp` / `PrefilledData` path; full migration remains a
  Phase 5 effort. See [`Typed-Continuation-Design.md`](./Typed-Continuation-Design.md) §14.
- **VM still owns UI feedback** (`MessageBox` for duplicate-target, status
  message text, refresh hop via `Dispatcher.BeginInvoke`). This is by design
  for Phase 3 and is the boundary the application service preserves.
- **Tag / alternative persistence is eventually-consistent.** Attachment
  `TaggedProjectFileId` and `SelectedAlternativeId` are persisted via
  `AttachmentTaggingService.SetAttachmentTagAsync` /
  `SetAttachmentAlternativeAsync` from `OnAttachmentTagChanged` (an async
  fire-and-forget event handler). The VM does **not** await those saves
  before calling `MoveAsync`. In normal use the saves complete well before
  the user clicks the move button, but no explicit barrier exists. If a user
  changes a picker and immediately clicks MoveToProject, the handler's
  duplicate-target validation reads from DB and may see the previous value.
  This is reported, not fixed, per the Step 5 constraint.

### 6.3 Manual task creation inside `ActionExecutor` (resolved on inspection)
- `ActionExecutor.CreateReviewChildProjectTaskAsync`
- `ActionExecutor.CreateAuthorityInvitationTrackingTaskAsync`

**Correction (Phase 2 reconnaissance):** both methods already route through
`TaskFactory.CreateAsync` and do **not** call `db.ProjectAssignments.Add`
directly. They therefore comply with the §4 step-4 rule. They remain
`LegacyInline` only in the sense that they are inline `ActionExecutor`
branches rather than dispatcher-routed handlers (a Phase 5 concern, not a
task-creation concern).

The residual centralization gap moved to **§6.3a Phase 2B** below.

### 6.3a Phase 2B — `TaskService` / `TaskFactory` unification (deferred)

`TaskService.GetOrCreateTaskWithDueDate[AndStatus][Async]` (`TaskService.cs`
lines 391 / 440 / 501 / 552) builds `ProjectAssignment` and calls
`TaskPriorityEngine.InsertWithAutoPriority(Async)` + `LogTaskEvent(Async)`
directly instead of delegating to `TaskFactory.CreateAsync`. This is a
**service-level** centralization gap, not a ViewModel-direct or
ActionExecutor-direct DB write.

It is **deferred** because a literal delegation would change observable
behavior (idempotency by `(ProjectId, AssignedToId, TaskTypeId)`, auto title
generation, richer creation-event note, `DateTime.Now` vs `DateTime.UtcNow`,
status existence validation, and priority insertion for unassigned tasks).
Full details and open Phase 2B decisions live in
[`Action-Handler-Inventory.md`](./Action-Handler-Inventory.md) §7.

### 6.4 Direct task-status edits in the UI
- `FloatingProjectTasksViewModel` (status combobox / inline edits)
- `TaskPanelViewModel` (edit-task panel)

Both can mutate `ProjectAssignment.StatusId` without going through
`ITaskCompletionCoordinator`. This is acceptable for manual admin-style
edits, but it means the coordinator is **not** the single writer of task
status. We must decide whether to:
- accept this as a documented administrative override, or
- route administrative edits through a dedicated coordinator method.

### 6.5 `ClosePreviousStageTasks` direct close
The handler for `WorkflowTransitionActionType.ClosePreviousStageTasks` closes
tasks **directly**, not through `ITaskCompletionCoordinator`. This is
intentional (bulk system close at a transition boundary) but bypasses the
single-decision-point pattern. We should either:
- formally document it as an allowed system-only bypass, or
- extend the coordinator with a bulk `CloseAllForStageAsync(...)` operation.

### 6.6 `ActionFollowUp` enum location
`ActionFollowUp` lives in `SiNetSQL.Services.EmailContext.ActionExecutor` and
is referenced from `SiNetSQL.Domain.Actions.ActionDefinitionRegistry`. The
registry currently carries an explicit comment marking this as a temporary
architectural concession: Domain should not depend on `Services.EmailContext`.

### 6.7 Two non-overlapping bridges back to the workflow
- **Task path:** coordinator → `WorkflowAdvanced` flag → caller invokes
  orchestrator.
- **Action path:** `WorkflowActionLifecycleReporter` →
  `WorkflowActionCompletedHandler` → orchestrator.

Both exist for valid reasons, but no document explained that until now. New
contributors reasonably ask "which one fires for X?". The answer:
- Task closure (UI marking a task done) → task path.
- Action completion that is **not** tied to a task (e.g. `ApproveOrClose`) →
  action path.

### 6.8 Lifecycle hooks treated as "infrastructure-only" but wired in DI
`WorkflowActionLifecycleReporter` and `WorkflowActionCompletedHandler` carry
XML doc comments suggesting they are "infrastructure-only at this stage" /
"pilot", yet they are registered in DI (`App.xaml.cs`) and consumed in
production. We should either remove the misleading comments or formally
declare these components stable.

---

## 7. Decision Points

The following questions **must be answered by the team** before any of the
Phase 2+ refactor work in §8 starts. None of them have a default answer.

1. **Canonical execution surface.** Should every business action eventually
   pass through `IProcessActionDispatcher`, including those triggered by
   ViewModels (not only `ActionExecutor` and `WorkflowActionExecutor`)?
   - Option A: VM → dispatcher (dispatcher is the only execution surface).
   - Option B: VM → service → dispatcher only when emitting cross-cutting
	 domain events.
2. **UI continuation contract.** When a handler returns
   `Deferred(RequiresUi)`, what is the exact contract for the UI to come
   back? Is it always "re-issue the same `ActionCode` with completed
   `PrefilledData`", or do we introduce a typed "continuation" object?
3. **Task creation policy.** Is `SmartTaskService` (or `TaskFactory`) the
   single API for *every* `ProjectAssignment` creation, including
   administrative one-offs from `TaskPanelViewModel`?
4. **Task closure policy.** Is `ITaskCompletionCoordinator` the single API
   for *every* `ProjectAssignment` status transition to Completed, or do
   we permit a documented administrative override?
5. **Bulk close.** How do we want `ClosePreviousStageTasks` to look in the
   end state: a documented system-only bypass, or a new coordinator method
   `CloseAllForStageAsync(...)`?
6. **Status-edit bypass.** Should inline status edits in
   `FloatingProjectTasksViewModel` / `TaskPanelViewModel` route through
   the coordinator (with a new "admin override" event code), or be left
   as a deliberate manual escape hatch?
7. **`ActionFollowUp` location.** Are we ready to move it from
   `Services.EmailContext.ActionExecutor` to `Domain/Actions/` and absorb
   the small ripple in `ActionExecutor` / its callers?
8. **Lifecycle reporters' status.** Do we declare
   `WorkflowActionLifecycleReporter` + `WorkflowActionCompletedHandler`
   production-stable and remove the "pilot/infra-only" XML doc warnings?

---

## 8. Refactor Roadmap (phases only — no implementation now)

| Phase | Goal | Touches |
|---|---|---|
| **Phase 0 — Documentation** *(this file)* | Single architectural document agreed by the team. | Docs only. |
| **Phase 1 — Handler inventory** | Add a property / column to `ActionDefinition` that classifies each `ActionCode` as: HasHandler / RequiresUiOnly / LegacyInline / SystemOnly. Pure metadata. | `ActionDefinitionRegistry` (additive). |
| **Phase 2 — Unify task creation** | **Closed (no code changes).** Phase 2 reconnaissance verified that `CreateReviewChildProjectTaskAsync`, `CreateAuthorityInvitationTrackingTaskAsync`, and `TaskPanelViewModel.CreateTask` all already comply (the two ActionExecutor methods call `TaskFactory.CreateAsync`; the VM calls `TaskService`, not the `DbContext`). | None. |
| **Phase 2B — `TaskService` / `TaskFactory` unification** | **Deferred.** Not behavior-neutral. Tracked in [`Action-Handler-Inventory.md`](./Action-Handler-Inventory.md) §7. | `Services/TaskService.cs`. |
| **Phase 3 — Unify `MoveToProject`** | **Closed.** Canonical path is `EmailManagementViewModel → IEmailMoveToProjectApplicationService → IProcessActionDispatcher → MoveToProjectProcessActionHandler → IProjectFileFilingService → ITaskCompletionCoordinator` (when `EmailFilingTask` context exists). The application service is preserved as the VM-facing boundary; the handler owns business execution and coordinator reporting. See §6.2. | `EmailMoveToProjectApplicationService`, `MoveToProjectProcessActionHandler`, `ActionExecutionContext`. |
| **Phase 4 — Close coordinator bypasses** | Either document inline status edits as admin overrides or route them through the coordinator. Add `CloseAllForStageAsync` (or formally accept the existing direct close). | `FloatingProjectTasksViewModel`, `TaskPanelViewModel`, `ClosePreviousStageTasksProcessActionHandler`, `ITaskCompletionCoordinator`. |
| **Phase 5 — Migrate `RequiresUI` actions** | **Pilot closed.** Typed continuation is now end-to-end for `ApproveOrClose` and `CloseOpinion` through `IActionContinuationUiHost` / `WpfActionContinuationUiHost` (only `ContinuationUiKind.WorkflowAdvanceDialog`). Remaining `RequiresUI` action families (`CreateTask`, `FileImport`, `ProjectPicker`, etc.) still flow through the legacy `ActionFollowUp` / `PrefilledData` path; they will be migrated one family at a time. | `ActionExecutor`, new handlers, new WPF continuation hosts. |
| **Phase 6 — Move `ActionFollowUp` to `Domain/Actions`** | Mechanical move; update references. Removes the architectural concession comment in `ActionDefinitionRegistry`. | `ActionFollowUp`, `ActionExecutor`, `ActionDefinitionRegistry`. |

Phases are **strictly sequential**: do not begin Phase N until Phase N-1's
decisions are agreed and reflected back into this document.

---

## 9. Do Not Change Now

Until this document is reviewed and the Decision Points in §7 are answered:

- **Do not** change code in any of the listed files.
- **Do not** change the database schema.
- **Do not** create migrations.
- **Do not** delete or rename any legacy path (including the duplicate
  `MoveToProject` flow and the inline `RequiresUI` branches in
  `ActionExecutor`).
- **Do not** refactor `ActionFollowUp`'s namespace.
- **Do not** alter the `WorkflowDesigner` UI, the workflow seed data, or
  any `Models/Workflow*` entity.
- **Do not** widen or narrow the public surface of
  `ITaskCompletionCoordinator`, `IProcessActionDispatcher`, or
  `IActionLifecycleReporter`.

Documentation-only changes (extending or correcting this file) are allowed
and encouraged.
