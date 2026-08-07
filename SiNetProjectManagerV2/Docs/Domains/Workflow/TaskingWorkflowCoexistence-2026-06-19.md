# Tasking and Workflow Coexistence

- **Date:** 19.06.2026
- **Status:** Active — Source of truth for the relationship between the old tasking model and the new workflow-first model.
- **Scope:** Documents the coexistence of `ProjectTypeTaskType` / `ProjectTypeStatus` / `TaskBehaviorDefinition` (old model) with `WorkflowDefinition` / `WorkflowStageTask` / `TaskCompletionCoordinator` (new model), including known risks and alignment plan.

## 1. Purpose
The system currently operates two complementary task management models. This document defines their relationship, explains why both are active, identifies the highest-risk integration gaps, and outlines the alignment plan. It serves as the authoritative reference for any developer or AI assistant working on task creation, task completion, or workflow advancement.

> **Cross-reference — Google Sheet Review Migration Preview (added 23.06.2026):**
> `GoogleSheetReviewMigrationPreviewService` is a **read-only consumer** of workflow and task infrastructure. In Phase 1 Preview, it reads workflow state through `WorkflowQueryService` and `WorkflowStageDefinition` / `WorkflowTransitionRule` tables to classify migration rows. It does **not** create, advance, or complete workflows or tasks. See `Docs/Domains/Migration/GoogleSheetReviewMigrationDesign-2026-06-21.md` for details.

## 2. Current old tasking model

The old model uses junction tables to define which task types and statuses are allowed per project type. It is an **active legacy mechanism** — not dead code.

### Core entities

| Entity | Table | Purpose |
|---|---|---|
| `ProjectTypeTaskType` | Junction: `JobType` ↔ `TaskType` | Defines which task types are allowed for a project type |
| `ProjectTypeStatus` | Junction: `JobType` ↔ `ProjectAssignmentStatus` | Defines which statuses are allowed for a project type |
| `TaskBehaviorDefinition` | `TaskBehaviorDefinitions` | Defines auto-create and auto-close behavior for specific task types |
| `TaskTriggerRule` | `TaskTriggerRules` | Conditions that trigger auto-creation of a task (e.g., EmailAssignedToProject) |
| `TaskCompletionRule` | `TaskCompletionRules` | Conditions that auto-close a task (e.g., AllAttachmentsTagged) |

### Active services

| Service | Role |
|---|---|
| `TaskManagementSeedService` | Seeds static lookup data on every app startup. `ResetMappingsToDefaults()` wipes and re-seeds `ProjectTypeTaskType` and `ProjectTypeStatus`. |
| `ProjectTypeRuleService` | Queries `ProjectTypeTaskTypes` and `ProjectTypeStatuses` to filter allowed types/statuses at runtime. |
| `TaskLifecycleService` | Evaluates `TaskBehaviorDefinition` + `TaskTriggerRule` + `TaskCompletionRule` for event-driven auto-create and auto-close. |
| `TaskBehaviorSeedService` | Seeds behavior definitions (MaterialFiling, ProfessionalReview). |

### Active UI

| Window | Role |
|---|---|
| `ProjectTypeRulesWindow` + `ProjectTypeRulesViewModel` | Admin UI for editing ProjectType→TaskType and ProjectType→Status checkbox mappings. |

### Task creation — old paths

| Method | File | Notes |
|---|---|---|
| `GetOrCreateTask()` | `TaskService.cs` | **Active legacy** — inline `new ProjectAssignment` + `TaskPriorityEngine`. Candidate for migration. |
| `GetOrCreateTaskWithDueDate()` | `TaskService.cs` | **Active legacy** — same inline pattern. Candidate for migration. |
| `GetOrCreateTaskWithDueDateAndStatus()` | `TaskService.cs` | **Active legacy** — same inline pattern. Candidate for migration. |
| `GetOrCreateTaskAsync()` | `TaskLifecycleService.cs` | Active — triggered by `TaskBehaviorDefinition` rules. |

### Task completion — old paths

| Method | File | Notes |
|---|---|---|
| `ChangeTaskStatus()` | `TaskService.cs` | Core status change engine. **Does not signal workflow advance.** |
| `CompleteTask()` | `TaskService.cs` | Convenience — delegates to `ChangeTaskStatus()`. |
| `SetWaitingForExternal()` | `TaskService.cs` | Status change via `ChangeTaskStatus()`. |
| `ReopenTask()` | `TaskService.cs` | Status change via `ChangeTaskStatus()`. |
| `ApplyCompletionStatusAsync()` | `TaskLifecycleService.cs` | Auto-close when completion rules are satisfied. |

## 3. Current workflow-first model

The workflow-first model is a fully wired engine with 14 registered DI services, 5 defined workflow types, comprehensive admin UI (Builder, Visual Designer, Policy, Dashboard, Behaviors, Help), and visual designer support.

### Core entities

| Entity | Purpose |
|---|---|
| `WorkflowDefinition` | Named workflow template (PLN, MAT, REV, PRP, OPN) |
| `WorkflowStageDefinition` | Stage within a workflow (with visual designer properties, sub-workflow support) |
| `WorkflowInstance` | Running instance of a workflow bound to a project. **B2 target:** project-bound tracks carry explicit JobType identity; active uniqueness is `Project + WorkflowDefinition + JobType` (not one instance per definition). See [`PROJECT_TYPE_WORKFLOW_POLICY.md`](../../../../docs/manual-tests/PROJECT_TYPE_WORKFLOW_POLICY.md). |
| `WorkflowTransitionRule` | Rule governing stage-to-stage transitions (trigger, condition, evaluation mode) |
| `WorkflowStageTask` | Template linking a stage to a `TaskType` with default assignee |
| `WorkflowStartTrigger` | Defines what starts a workflow. Infrastructure exists; runtime auto-evaluation is postponed. |
| `ProjectTypeWorkflowDefinition` | Maps project types to allowed workflow definitions |
| `ProjectTypeWorkflowStage` | Per-project-type stage activation and requirement. **B2 target:** runtime source of truth for which stages a JobType track provisions/advances (not seed-only). |
| `ProjectTypeDiscipline` | Per-project-type discipline activation |
| `TaskResultDefinition` | Standardized result codes for task completion |

### Active services

| Service | Role |
|---|---|
| `WorkflowEngine` | Lifecycle: Start, Advance, Pause, Resume, Complete, Cancel |
| `WorkflowTaskOrchestrator` | Bridge between Engine and TaskSystem. Start+CreateTasks, Advance+CreateTasks, CheckAutoAdvance. |
| `WorkflowTransitionEvaluator` | Evaluates triggers and conditions on transition rules. |
| `WorkflowActionExecutor` | Executes transition actions (SetProjectStatus, CreateStageTasks, etc.). |
| `WorkflowStageTaskProvisioningService` | Creates `ProjectAssignment` tasks from `WorkflowStageTask` templates via `TaskFactory.CreateAsync()`. |
| `ProjectWorkflowPolicyService` | Resolves allowed workflows per project type. |
| `WorkflowQueryService` | Read-only queries for dashboard and monitor. |
| `WorkflowSeedService` | Idempotent seeding of all **6** workflows (`PlanningWorkflow`, `Review`, `MaterialIntake`, `Proposal`, `Opinion`, `Outsourcing`), mappings, stages, and disciplines. |
| `TaskCompletionCoordinator` | Centralized task completion. Signals `WorkflowTaskOrchestrator.CheckAndAutoAdvanceAsync()`. |
| `TaskFactory` | Canonical factory for creating `ProjectAssignment` entities with proper audit trail. |

### Task creation — workflow path

| Method | File | Notes |
|---|---|---|
| `CreateStageTasksAsync()` | `WorkflowStageTaskProvisioningService.cs` | Creates from `WorkflowStageTask` templates via `TaskFactory.CreateAsync()`. |
| `GetOrCreateTaskWithDueDateAsync()` | `TaskService.cs` | Already migrated — delegates to `TaskFactory.CreateAsync()`. |
| `GetOrCreateTaskWithDueDateAndStatusAsync()` | `TaskService.cs` | Already migrated — delegates to `TaskFactory.CreateAsync()`. |

### Task completion — workflow-aware path

| Method | File | Notes |
|---|---|---|
| `CompleteAsync()` | `TaskCompletionCoordinator.cs` | Validates event, records result, closes task, signals workflow auto-advance. |
| `AdminSetCompletedAsync()` | `TaskCompletionCoordinator.cs` | Simple admin close with workflow advance flag. |
| `CloseTasksAsSystemAsync()` | `TaskCompletionCoordinator.cs` | Bulk system close **without** workflow advance (intentional). |
| `CheckAndAutoAdvanceAsync()` | `WorkflowTaskOrchestrator.cs` | After task status change, evaluates transition triggers and auto-fires if evaluation mode is Auto. |

## 4. Why both models currently coexist

The two models serve different granularity levels and are not fully redundant:

- **Event-driven micro-tasks** (e.g., auto-create a filing task when an email is assigned to a project) are handled by `TaskBehaviorDefinition` + `TaskTriggerRule` + `TaskCompletionRule`. These react to low-level application events.
- **Stage-driven process tasks** (e.g., create a review task when the workflow enters the MaterialIntake stage) are handled by `WorkflowStageTask`. These are orchestrated by the workflow engine.
- **Admin filtering** of allowed task types and statuses per project type is handled by `ProjectTypeTaskType` and `ProjectTypeStatus`. The workflow engine does not currently replace this filtering.
- The **Behavior tab** in `WorkflowManagementWindow` manages `TaskBehaviorDefinition` CRUD, confirming the system intends them to coexist under the workflow umbrella.

The long-term architectural direction is **Workflow-first**: suggested actions and project processes should start workflows, and tasks should be created from workflow stages. The old model remains active during the gradual alignment.

## 5. Known risks

### 5.1. Dual task-completion paths (HIGH RISK)
`TaskService.ChangeTaskStatus()` and `TaskService.CompleteTask()` can close tasks without signaling `WorkflowTaskOrchestrator`. If a task is closed through these old paths while a workflow is active, the workflow will not auto-advance. This is the highest-priority integration gap.

### 5.2. Sync task creation bypasses TaskFactory (MEDIUM RISK)
`TaskService.GetOrCreateTask()`, `GetOrCreateTaskWithDueDate()`, and `GetOrCreateTaskWithDueDateAndStatus()` use inline `new ProjectAssignment` instead of `TaskFactory.CreateAsync()`. Tasks created this way may miss audit events or future factory-level logic.

### 5.3. ProjectType filtering without workflow awareness (MEDIUM RISK)
`ProjectTypeRuleService` filters allowed task types per project type using the old junction table. If a workflow stage defines a task type that is not in the `ProjectTypeTaskType` mapping, the old filter could block it.

### 5.4. WorkflowStartTrigger not fully wired (LOW RISK — by design)
`WorkflowStartTrigger` entities are seeded but the runtime auto-start evaluation service is not yet fully wired. Workflows are currently started manually (Dashboard), email-driven (`WorkflowCreateProjectWindow`), or as sub-workflows. This is intentional, not a bug.

## 6. Alignment plan

### Phase 1: Unify task completion (highest priority)
Ensure that `TaskService.ChangeTaskStatus()` signals `WorkflowTaskOrchestrator.CheckAndAutoAdvanceAsync()` when a workflow instance is active for the task's project. Some system closes intentionally avoid workflow advance (e.g., `CloseTasksAsSystemAsync`), so the integration must be selective, not blanket.

### Phase 2: Migrate sync task creation
Migrate the three remaining sync methods in `TaskService` to delegate to `TaskFactory.CreateAsync()`, matching the pattern already established in the async variants. Preserve existing callers; mark sync methods as legacy compatibility wrappers.

### Phase 3: Harmonize ProjectType filtering
Evaluate whether `ProjectTypeRuleService` should also consider `WorkflowStageTask` definitions when determining allowed task types. This may be a policy decision rather than a code change.

### Phase 4: Evaluate consolidation (future, requires product decision)
Once all task creation goes through `TaskFactory` and all completion goes through `TaskCompletionCoordinator`, evaluate whether `ProjectTypeTaskType` / `ProjectTypeStatus` can be derived from workflow definitions rather than maintained as separate junction tables.

## 7. What must not be deleted yet

| Entity/Service | Reason |
|---|---|
| `ProjectTypeTaskType` table and model | Actively used by `ProjectTypeRuleService` and admin UI |
| `ProjectTypeStatus` table and model | Actively used by `ProjectTypeRuleService` and admin UI |
| `TaskBehaviorDefinition` + rules | Actively used by `TaskLifecycleService` for event-driven tasks |
| `TaskManagementSeedService` | Runs on every startup; seeds static lookup data |
| `ProjectTypeRulesWindow` | Active admin UI for configuring allowed types/statuses |
| `TaskService` sync methods | Still called by sync callers; migration must be gradual |
| `DevDataResetService` | DEBUG-only but essential for dev resets; re-seeds both old and new models |

## 8. Legacy / candidate-for-future-alignment items

| Item | Status |
|---|---|
| `TaskService.GetOrCreateTask()` (sync, inline) | **Active legacy** — candidate for migration to `TaskFactory.CreateAsync()` |
| `TaskService.GetOrCreateTaskWithDueDate()` (sync, inline) | **Active legacy** — candidate for migration |
| `TaskService.GetOrCreateTaskWithDueDateAndStatus()` (sync, inline) | **Active legacy** — candidate for migration |
| `TaskManagementSeedService.ResetMappingsToDefaults()` | Active — review-worthy after code alignment; still needed while junction tables are in use |
| `TaskManagementSeedService.ResetAllToDefaults()` | Active but destructive — should only be used in dev reset flows |
| `WorkflowStartTrigger` auto-evaluation | Infrastructure exists; runtime behavior postponed |

## 8a. Task navigation and opening (workflow review tasks → feature windows)

Completing a workflow task is only half of the integration. The other half is **opening** the task on the exact entity it targets. This uses a single, read-only resolution path — never a per-feature router.

> Related doc: the inspection report window itself (open modes, creation rules,
> task context, link service, and completion) is documented in
> `Docs/Domains/PlanReview/InspectionReportUiAndLifecycle-2026-06-21.md` §3.1.2–§3.1.6.
> The two documents must stay consistent.

### Single resolution path

| Component | Role |
|---|---|
| `ReviewTaskInteractionRegistry` | Maps each task type code (e.g. `PerformProfessionalReview`, `FixReportPerManager`) to its `TaskOpenMode`, `TaskComponentKeys.*`, `TaskWorkTargetEntityType`, `TaskCompletionPolicy`, and allowed result codes. Single source of truth for "how does this task open and complete?". |
| `TaskNavigationResolver` | **Read-only.** Translates a `taskId` into a `TaskNavigationRequest`, deriving `PrimaryWorkTargetEntityId` from the task's work-target `TaskLink`. The UI must not bypass it. |
| `TaskComponentKeys` | Stable component keys the UI switches on to choose the host window. |

### Inspection-report review tasks

Tasks whose component key is `InspectionReport` / `ManagerReviewApproval` open in the existing **FloatingInspectionView**, not a generic project report list:

1. `FloatingProjectTasksView.OnOpenTaskNavigationRequested(...)` switches on `request.ComponentKey` and delegates to `OpenInspectionReportTask(...)`.
2. The helper checks `request.IsSuccess`. If the resolver could not resolve a work target (e.g. **no `InspectionReport` `TaskLink`**), it shows a clear Hebrew message and does **not** open a report. There is **no first/last-report fallback**.
3. It sets the active project, obtains the window via `MainWindow.ShowFloatingInspectionWindow()`, builds an `InspectionReportTaskContext` (report id taken only from `request.PrimaryWorkTargetEntityId`; `null` when no report exists yet), and calls `FloatingInspectionViewModel.OpenForTaskAsync(context)`.
4. When `PrimaryReportId` is `null`, creation mode is **only** allowed for task types whose first action is producing the report (`PerformProfessionalReview`). Follow-up report tasks (`FixReportPerManager`, `RecheckPlan`, `ApproveReviewReport`, `ResubmitToManager`) require an already-linked report; if none exists the VM shows the clear Hebrew "task is not linked to an existing report" message and does **not** enter creation mode. After a report is created in an allowed creation-mode task, `InspectionReportTaskLinkService` idempotently creates the `TaskLinkEntityType.InspectionReport` work-target link so the task now points at the concrete report.
5. On export/send, the VM calls `TaskCompletionCoordinator.CompleteAsync(...)` with the resolved work-target link id. The coordinator marks the link Done, closes the task per policy, and signals workflow auto-advance. The VM never mutates `WorkflowStage` directly.

### Creation-mode rules by task type

`OpenForTaskAsync` decides creation mode via `ReportTaskAllowsCreationWhenMissing(taskTypeCode)`:

| Task type | `PrimaryReportId = null` allowed? | Behavior when no report linked |
|---|---|---|
| `PerformProfessionalReview` | ✅ Yes | Enter creation mode; link is created on report creation. |
| `FixReportPerManager` | ❌ No | Clear error; existing report required. |
| `RecheckPlan` | ❌ No | Clear error; existing report required. |
| `ApproveReviewReport` | ❌ No | Clear error; existing report required. |
| `ResubmitToManager` | ❌ No | Clear error; existing report required. |

There is never a first/last-report fallback for any task type.

### Task-mode lifecycle in the singleton window

`FloatingInspectionView` is a reused singleton, so task context must not leak between opens:

- `_taskContext` is set only by `OpenForTaskAsync(...)`; `IsTaskMode` reflects it.
- `ClearTaskMode()` nulls `_taskContext` (and the completion guard) and is called at the **start of `ApplyActiveProject(...)`** — the normal/project-centric open path — so reusing the window for a project drops any prior task.
- After a successful completion, `TryCompleteReportTaskAsync(...)` records `_completedTaskId`, nulls `_taskContext`, and refuses to complete the same task again if the user exports/sends a second time.

### Completion mapping scope

`ResolveCompletionForTaskType(...)` only auto-completes single-outcome report tasks from export/send:

| Task type | Auto-completed from export? | Completion event / result |
|---|---|---|
| `PerformProfessionalReview` | ✅ | `ReviewProfessionalReviewCompleted` / `ProfessionalReviewCompleted` |
| `FixReportPerManager` | ✅ | `ReviewProfessionalReviewCompleted` / `ProfessionalReviewCompleted` |
| `RecheckPlan` | ❌ (by design) | Two outcomes (`ReviewRecheckPassed` vs `ReviewRecheckRequiresMoreCorrections`) cannot be chosen from a plain export; handled by its own decision flow. |
| `ApproveReviewReport` / `ResubmitToManager` | ❌ | Approval decisions handled by the manager-approval flow. |

Unmapped types return `(null, null)`, so `TryCompleteReportTaskAsync` no-ops and the task is simply not closed from export.

### Supporting services (task-open path)

| Service | Role |
|---|---|
| `InspectionReportTaskContext` | Runtime context passed from the task shell into `FloatingInspectionViewModel.OpenForTaskAsync(...)`. Mirrors `EmailFilingTaskContext`. `PrimaryReportId` is nullable to support creation mode. |
| `InspectionReportTaskLinkService` | Idempotently creates/repairs the `InspectionReport` work-target `TaskLink` (`Role=Related`, `IsWorkTarget=true`, `WorkStatus=Pending`) and resolves its id for completion. If a stale link to the same report exists with the **wrong `Role`** (e.g. `Source`/`Trigger`), it repairs the `Role` to `Related` in place rather than creating a duplicate, so `TaskNavigationResolver` (which filters by `RequiredTaskLinkRole == Related`) can still resolve it. Uses the **existing** `TaskLink` table — no new link table. |

### Constraints (must not regress)

- Do **not** add a parallel navigation router; `TaskNavigationResolver` is the only resolver.
- Do **not** create a new task↔report link table; reuse `TaskLink` with `TaskLinkEntityType.InspectionReport`.
- Do **not** auto-pick a report when no `TaskLink` exists; surface the clear "task is not linked to a report" message instead.
- Do **not** close tasks or advance workflow from the inspection window; go through `TaskCompletionCoordinator`.
- Do **not** let the reused singleton window keep a previous task's context; the normal open path (`ApplyActiveProject`) must clear task mode.
- Do **not** auto-complete multi-outcome tasks (`RecheckPlan`) or approval tasks from a plain export; only single-outcome review tasks are mapped.
- The normal project-centric way of opening `FloatingInspectionView` (not task-driven) remains unchanged; task-completion services are optional and only used when a task context is supplied.

## 8a. Project-type track instances (B2 — cross-reference)

Approved product target (2026-08-01): one independent `WorkflowInstance` per
`Project + WorkflowDefinition + JobType` track — not one instance per task, and not
dedupe-by-definition as the product rule. Tasks, navigation, and reuse must follow the
Trigger-linked instance. Full policy and gates:
[`docs/manual-tests/PROJECT_TYPE_WORKFLOW_POLICY.md`](../../../../docs/manual-tests/PROJECT_TYPE_WORKFLOW_POLICY.md)
and [`WorkflowPrinciples-2026-05-26.md`](WorkflowPrinciples-2026-05-26.md) § Project-type track instances.

Runtime/schema for B2 remain postponed until the documentation approval checkpoint passes.

## 9. Dropped / cancelled / postponed
- Deleting `ProjectTypeTaskType` / `ProjectTypeStatus` — **not approved**; still actively used.
- Treating old tasking model as dead code — **cancelled**; it is still active.
- Removing `TaskBehaviorDefinition` system — **not approved**; actively used for event-driven tasks.
- Removing `TaskManagementSeedService` — **not approved**; runs on every startup.
- Replacing old model in one large step — **postponed**; alignment must be gradual.
- `WorkflowStartTrigger` runtime auto-evaluation — **postponed**; infrastructure exists but runtime evaluation is future work.
- Full consolidation of `ProjectTypeTaskType` into workflow definitions — **postponed** pending product decision after Phases 1-4.
- Adding fallback task creation — **not approved**.
- Creating a parallel workflow/tasking mechanism — **not approved**.
- Changing `DevDataResetService` behavior — **not approved** until code alignment is reviewed.

## 10. Manual verification checklist
When validating the tasking/workflow integration, verify:
- [ ] Tasks created through `WorkflowStageTaskProvisioningService` appear correctly in the task list.
- [ ] Tasks created through `TaskLifecycleService` (event-driven) appear correctly.
- [ ] Task completion through `TaskCompletionCoordinator.CompleteAsync()` advances the active workflow.
- [ ] Task completion through `TaskService.CompleteTask()` does NOT advance workflow (current behavior — to be addressed in Phase 1).
- [ ] `ProjectTypeRuleService` correctly filters allowed task types per project type.
- [ ] `WorkflowManagementWindow` Behavior tab correctly manages `TaskBehaviorDefinition` CRUD.
- [ ] `DevDataResetService` correctly re-seeds both old mappings and new workflows after a wipe.
- [ ] `TaskFactory.CreateAsync()` produces tasks with correct audit trail.
- [ ] Workflow auto-advance fires correctly when all required stage tasks are closed.
- [ ] Sub-workflow completion correctly notifies and advances the parent workflow.
- [ ] Opening a `PerformProfessionalReview` task whose `InspectionReport` `TaskLink` exists opens `FloatingInspectionView` on the **exact** linked report (series + report selected), not the project's generic report list.
- [ ] Opening an inspection-report review task with **no** `InspectionReport` `TaskLink` shows the clear "task is not linked to a report" message and does NOT auto-pick a report.
- [ ] Opening an inspection-report review task before a report exists enters creation mode, and creating the report establishes the `InspectionReport` work-target `TaskLink` via `InspectionReportTaskLinkService`.
- [ ] Exporting/sending the report from a task-opened inspection window closes the task through `TaskCompletionCoordinator` and advances the workflow.
- [ ] Opening `FloatingInspectionView` the normal (non-task) project-centric way is unchanged, and clears any prior task context (`ClearTaskMode` on `ApplyActiveProject`).
- [ ] Opening a `FixReportPerManager` / `RecheckPlan` / `ApproveReviewReport` / `ResubmitToManager` task with **no** linked report shows the clear error and does NOT enter creation mode (only `PerformProfessionalReview` may).
- [ ] Exporting/sending the same task-opened report twice completes the task only once (re-completion guard).
- [ ] A stale `InspectionReport` `TaskLink` with the wrong `Role` is repaired to `Related` by `InspectionReportTaskLinkService` and remains resolvable by `TaskNavigationResolver`.
- [ ] A `RecheckPlan` task is NOT silently closed by export (no completion mapping); its decision flow remains responsible for completion.
