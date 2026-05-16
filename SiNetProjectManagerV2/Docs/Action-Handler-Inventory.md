# Action / Handler Inventory (Phase 1)

> **Status:** Documentation only. No code changes, no DB changes, no migrations,
> no refactor, no deletion of legacy paths, no handler moves, no UI changes.
> This file is the **mapping artefact** required by Phase 1 of the roadmap
> defined in [`Action-Task-Workflow.md`](./Action-Task-Workflow.md).
>
> **Repos in scope:** `danny-isr/SiNetProjectManager`, `danny-isr/SiNetSQL`.
>
> Companion to:
> - [`Action-Task-Workflow.md`](./Action-Task-Workflow.md) — architecture overview.

---

## How to read the inventory

Each row classifies a single action / code in the system as it behaves **today**
(not as it should behave). The columns map 1:1 to the schema requested in the
Phase 1 brief:

1. **Action / Code** — display name or `ActionCode` constant.
2. **Source Family** — `SuggestedActionType`, `ActionCode`,
   `WorkflowTransitionActionType`, `ViewModel Command`, or `Mixed / Duplicate`.
3. **Defined In** — enum / file where the action is declared.
4. **Current Execution Path** — what actually runs today.
5. **Has Process Handler?** — `Yes` / `No` / `Partial` / `N/A`.
6. **Requires UI?** — `Yes` / `No` / `Sometimes` / `Unclear`.
7. **UI FollowUp / Screen** — the dialog/screen the UI continues to (or `—`).
8. **Creates Task?** — `Yes` / `No` / `Sometimes` / `Unclear`.
9. **Task Creation Path** — service used (or `—`).
10. **Completes Task?** — `Yes` / `No` / `Sometimes` / `Unclear`.
11. **Completion Path** — service used (or `—`).
12. **Advances Workflow?** — `Yes` / `No` / `Sometimes` / `Unclear`.
13. **Workflow Advance Path** — bridge used (or `—`).
14. **Execution Classification** — one of `HasHandler`, `LegacyInline`,
	`RequiresUiOnly`, `ViewModelDirect`, `WorkflowSystemOnly`, `Hybrid`,
	`DuplicatePath`, `Unknown`.
15. **Risk** — `Low` / `Medium` / `High`.
16. **Notes / Gap** — short remark.

Classification rule of thumb:

- `HasHandler` — `IProcessActionHandler` registered **and** invoked via the
  dispatcher path in production.
- `LegacyInline` — executed by a private method inside `ActionExecutor`
  (or another orchestrator) without going through `IProcessActionDispatcher`.
- `RequiresUiOnly` — `ActionExecutor` returns `ActionResult.RequiresUI(...)`
  and no backend continuation exists yet.
- `ViewModelDirect` — runs directly from a WPF ViewModel command, never
  reaches `ActionExecutor` / dispatcher.
- `WorkflowSystemOnly` — only triggered by `WorkflowActionExecutor` from a
  transition rule; not user-selectable.
- `Hybrid` — backend handler exists **and** a parallel UI/legacy path also
  exists.
- `DuplicatePath` — the same business outcome is reachable through two
  distinct, independent paths (a special case of `Hybrid`).

---

## Section A — `SuggestedActionType` (UI-driven actions)

Source enum: `SiNetSQL.DTOs.Email.SuggestedActionType` (40 values).
Primary executor: `SiNetSQL.Services.EmailContext.ActionExecutor.ExecuteCoreAsync`.

| # | Action / Code | Source Family | Defined In | Current Execution Path | Has Process Handler? | Requires UI? | UI FollowUp / Screen | Creates Task? | Task Creation Path | Completes Task? | Completion Path | Advances Workflow? | Workflow Advance Path | Execution Classification | Risk | Notes / Gap |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `CreateNewProject` | SuggestedActionType | `SuggestedActionType.cs`, `ActionCodes.cs` | `ActionExecutor` returns `RequiresUI(NewProjectDialog)` | No | Yes | `NewProjectDialog` | Sometimes (after UI) | Unknown (UI path) | No | — | No | — | RequiresUiOnly | Medium | No backend continuation contract; project creation happens fully in UI. |
| 2 | `CreatePriceQuote` | SuggestedActionType | same | `ActionExecutor.StartProjectIndependentWorkflowAsync` (inline) → `WorkflowTaskOrchestrator` | No | No | — | Sometimes | `WorkflowTaskOrchestrator` (stage tasks) | No | — | Yes (starts NEW instance, does not advance existing) | Orchestrator direct | LegacyInline | Medium | Inline workflow start; no dispatcher handler. |
| 3 | `CreateNewReview` | SuggestedActionType | same | `ActionExecutor.CreateReviewChildProjectTaskAsync` (inline) | No | Sometimes | `ProjectPicker` when parent missing | **Yes** | **`TaskFactory.CreateAsync`** (verified during Phase 2 reconnaissance) | No | — | No | — | LegacyInline | Low | ~~Direct EF write bypasses `SmartTaskService` / `TaskFactory`.~~ **Correction (Phase 2 reconnaissance):** this site already routes through `TaskFactory.CreateAsync` and complies with the canonical task-creation rule. The remaining `LegacyInline` classification refers only to it being an inline ActionExecutor branch (not dispatcher-routed), not to direct EF writes. |
| 4 | `CreateOpinionProject` | SuggestedActionType | same | `ActionExecutor.StartWorkflowFromActionAsync("Opinion")` (inline) | No | No | — | Sometimes | Orchestrator | No | — | No (new instance) | Orchestrator direct | LegacyInline | Medium | Inline workflow start. |
| 5 | `AssociateToExistingProject` | SuggestedActionType | same | If `ProjectId` present → `DispatchLinkToProjectAsync` (handler). Else → `RequiresUI(ProjectPicker)` | Yes (when project known) | Sometimes | `ProjectPicker` | No | — | No | — | No | — | Hybrid | Medium | UI re-issues action with `ProjectId` after picker — informal continuation contract. |
| 6 | `CollectMaterial` | SuggestedActionType | same | `ActionExecutor` returns `RequiresUI(FileImportDialog)` | No | Yes | `FileImportDialog` | No | — | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 7 | `ForwardToDecision` | SuggestedActionType | same | `ActionExecutor` returns `RequiresUI(DecisionDialog)` | No | Yes | `DecisionDialog` | No | — | No | — | No | — | RequiresUiOnly | Low | UI-only feature today. |
| 8 | `FileOnly` | SuggestedActionType | same | `ActionExecutor.DispatchFileOnlyAsync` → `FileOnlyProcessActionHandler` | **Yes** | No | — | No | — | No | — | No | — | HasHandler | Low | Clean dispatcher path. |
| 9 | `RequestAuthorityInvitation` | SuggestedActionType | same | `ActionExecutor.CreateAuthorityInvitationTrackingTaskAsync` (inline) | No | No | — | **Yes** | **`TaskFactory.CreateAsync`** (verified during Phase 2 reconnaissance) | No | — | No | — | LegacyInline | Low | ~~Direct EF write bypasses task service.~~ **Correction (Phase 2 reconnaissance):** this site already routes through `TaskFactory.CreateAsync` and complies with the canonical task-creation rule. `LegacyInline` here refers only to the inline ActionExecutor branch, not to direct EF writes. |
| 10 | `AddMaterialToProject` | SuggestedActionType | same | `ActionExecutor.DispatchAddMaterialToProjectAsync` → `AddMaterialToProjectProcessActionHandler` | **Yes** | Sometimes | `FileImportDialog` when inputs missing (Deferred) | No | — | Sometimes (review WorkTarget done) | `ITaskCompletionCoordinator` (via filing service) | Sometimes | `TaskCompletionCoordinator` `WorkflowAdvanced` flag | HasHandler | Low | Phase-3 dispatcher path is live. |
| 11 | `RequestCompletion` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes (after UI) | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 12 | `PrepareResponse` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 13 | `InternalReview` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 14 | `AddNewDiscipline` | SuggestedActionType | same | `RequiresUI(DisciplineDialog)` | No | Yes | `DisciplineDialog` | No | — | No | — | No | — | RequiresUiOnly | Low | UI-only. |
| 15 | `HandleComments` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 16 | `UploadNewVersion` | SuggestedActionType | same | `RequiresUI(FileImportDialog)` | No | Yes | `FileImportDialog` | No | — | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 17 | `UpdateDesign` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 18 | `PrepareSubmission` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 19 | `CoordinateWithConsultants` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 20 | `SendUpdatedMaterial` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 21 | `ReceiveSupplementaryMaterial` | SuggestedActionType | same | `RequiresUI(FileImportDialog)` | No | Yes | `FileImportDialog` | No | — | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 22 | `ReceiveMaterialForReview` | SuggestedActionType | same | `RequiresUI(FileImportDialog)` | No | Yes | `FileImportDialog` | No | — | Sometimes (when material lands on a Review WorkTarget) | `ITaskCompletionCoordinator` (downstream, via filing) | Sometimes | `TaskCompletionCoordinator` flag | RequiresUiOnly | Medium | Completion is achieved by a *different* action (filing); coupling is implicit. |
| 23 | `OpenReviewRound` | SuggestedActionType | same | `ActionExecutor.StartWorkflowFromActionAsync("Review")` (inline) | No | No | — | Sometimes | Orchestrator (stage tasks) | No | — | No (starts/opens round) | Orchestrator direct | LegacyInline | Medium | Inline workflow start. |
| 24 | `PerformReview` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 25 | `WriteComments` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 26 | `SendComments` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 27 | `TrackCorrections` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 28 | `ReceiveCorrectedVersion` | SuggestedActionType | same | `RequiresUI(FileImportDialog)` | No | Yes | `FileImportDialog` | No | — | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 29 | `ApproveOrClose` | SuggestedActionType | same | `ActionExecutor.DispatchApproveOrCloseAsync` → `ApproveOrCloseProcessActionHandler` (+ lifecycle reporter bridge) | **Yes** | Yes | `WorkflowAdvanceDialog` | No | — | No | — | Yes (after UI confirms `ConfirmAdvance`) | `WorkflowActionLifecycleReporter` → `WorkflowActionCompletedHandler` | HasHandler | Medium | UI continuation contract relies on `PrefilledData["ConfirmAdvance"]` — informal. |
| 30 | `ReceiveMaterialForOpinion` | SuggestedActionType | same | `RequiresUI(FileImportDialog)` | No | Yes | `FileImportDialog` | No | — | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 31 | `AnalyzeDocuments` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 32 | `RequestMissingMaterial` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 33 | `PrepareDraftOpinion` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 34 | `UpdateOpinion` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 35 | `SendOpinion` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation, despite `CanAdvanceWorkflow=true` in registry. |
| 36 | `CloseOpinion` | SuggestedActionType | same | `RequiresUI(WorkflowAdvanceDialog)` | No | Yes | `WorkflowAdvanceDialog` | No | — | No | — | Unclear | (none today) | RequiresUiOnly | **High** | Registry says `CanAdvanceWorkflow=true`, but no backend path takes the UI confirmation. |
| 37 | `StartWorkflow` | SuggestedActionType | same | `ActionExecutor.DispatchStartWorkflowAsync` → `StartWorkflowProcessActionHandler` | **Yes** | Sometimes | `ProjectPicker` when project missing (Deferred) | Sometimes | Orchestrator (stage tasks) | No | — | No (new instance) | Orchestrator direct | HasHandler | Low | Clean dispatcher path. |
| 38 | `LinkToProject` | SuggestedActionType | same | `ActionExecutor.DispatchLinkToProjectAsync` → `LinkToProjectProcessActionHandler` | **Yes** | No | — | No | — | No | — | No | — | HasHandler | Low | Clean dispatcher path. |
| 39 | `CreateTask` | SuggestedActionType | same | `ActionExecutor.DispatchCreateTaskAsync` → `CreateTaskProcessActionHandler` | **Yes** | No | — | **Yes** | `CreateTaskProcessActionHandler` (uses task service inside) | No | — | No | — | HasHandler | Low | Single-purpose backend creator. |

---

## Section B — `WorkflowTransitionActionType` (system-triggered)

Source enum: `SiNetSQL.Models.WorkflowTransitionActionType` (8 values).
Primary executor: `SiNetSQL.Services.Workflow.WorkflowActionExecutor.ExecuteActionsAsync` —
strict dispatcher routing, no silent fallback.

| # | Action / Code | Source Family | Defined In | Current Execution Path | Has Process Handler? | Requires UI? | UI FollowUp / Screen | Creates Task? | Task Creation Path | Completes Task? | Completion Path | Advances Workflow? | Workflow Advance Path | Execution Classification | Risk | Notes / Gap |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `CreateStageTasks` | WorkflowTransitionActionType | `WorkflowTransitionActionType.cs` | `WorkflowActionExecutor` → dispatcher → `CreateStageTasksProcessActionHandler` (NoOp; task creation handled by orchestrator) | **Yes** | No | — | **Yes** | `WorkflowTaskOrchestrator` (creates stage tasks during transition) | No | — | No (it is *part of* a transition, not an advance) | — | WorkflowSystemOnly | Low | Documented NoOp handler; real work is in orchestrator. |
| 2 | `ClosePreviousStageTasks` | WorkflowTransitionActionType | same | `WorkflowActionExecutor` → dispatcher → `ClosePreviousStageTasksProcessActionHandler` | **Yes** | No | — | No | — | **Yes (bulk)** | **Direct close (bypasses `ITaskCompletionCoordinator`)** | No | — | WorkflowSystemOnly | **High** | Coordinator bypass. Intentional today but undocumented. |
| 3 | `SendNotification` | WorkflowTransitionActionType | same | `WorkflowActionExecutor` → dispatcher → `SendNotificationProcessActionHandler` | **Yes** | No | — | No | — | No | — | No | — | WorkflowSystemOnly | Low | — |
| 4 | `StartSubWorkflow` | WorkflowTransitionActionType | same | `WorkflowActionExecutor` → dispatcher → `StartSubWorkflowProcessActionHandler` | **Yes** | No | — | Sometimes (via spawned workflow) | Orchestrator | No | — | No (spawn) | — | WorkflowSystemOnly | Low | — |
| 5 | `SetProjectStatus` | WorkflowTransitionActionType | same | `WorkflowActionExecutor` → dispatcher → `SetProjectStatusProcessActionHandler` | **Yes** | No | — | No | — | No | — | No | — | WorkflowSystemOnly | Low | — |
| 6 | `RecordTaskResult` | WorkflowTransitionActionType | same | `WorkflowActionExecutor` → dispatcher → `RecordTaskResultProcessActionHandler` | **Yes** | No | — | No | — | No (records result, does not close) | — | No | — | WorkflowSystemOnly | Low | Writes `ProjectAssignmentEvent` + `LastTaskResultId` directly — by design. |
| 7 | `SetBillingPending` | WorkflowTransitionActionType | same | `WorkflowActionExecutor` → dispatcher → `SetBillingPendingProcessActionHandler` | **Yes** | No | — | No | — | No | — | No | — | WorkflowSystemOnly | Low | Thin wrapper over `SetProjectStatus`. |
| 8 | `CloseProject` | WorkflowTransitionActionType | same | `WorkflowActionExecutor` → dispatcher → `CloseProjectProcessActionHandler` | **Yes** | No | — | No | — | **Yes (bulk)** | Direct close (similar to `ClosePreviousStageTasks`) | No | — | WorkflowSystemOnly | Medium | Coordinator bypass for closing remaining tasks. |

---

## Section C — `EmailManagementViewModel` commands (UI direct)

Source: `SiNetSQL.MVVM.EmailManagementViewModel` (commands referenced in
`ActionCodes.cs`). These commands are triggered by the email context-menu /
toolbar; many of them parallel a `SuggestedAction` path.

| # | Action / Code | Source Family | Defined In | Current Execution Path | Has Process Handler? | Requires UI? | UI FollowUp / Screen | Creates Task? | Task Creation Path | Completes Task? | Completion Path | Advances Workflow? | Workflow Advance Path | Execution Classification | Risk | Notes / Gap |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `FileEmail` | ViewModel Command | `EmailManagementViewModel`, `ActionCodes.cs` | VM → `EmailFilingService` | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | Shared service is also called by the dispatcher `FileOnly` handler — consistent. |
| 2 | `UnfileEmail` | ViewModel Command | same | VM → `EmailFilingService.UnfileAsync` | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 3 | `FileToThreadProject` | ViewModel Command | same | VM direct (thread-aware filing) | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 4 | `MarkAsPending` | ViewModel Command | same | VM → `EmailStatusService` | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 5 | `MarkAsPersonal` | ViewModel Command | same | VM → `EmailStatusService` | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 6 | `MarkAsIrrelevant` | ViewModel Command | same | VM → `EmailStatusService` | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 7 | `TagAttachment` | ViewModel Command | same | VM → `IAttachmentProjectFilePicker` + EF write | No | Yes | (attachment picker) | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 8 | `MoveToProject` | **Mixed / Duplicate** | `EmailManagementViewModel.MoveToProjectAsync` **and** `MoveToProjectProcessActionHandler` | **VM path is the live one used from UI**; the dispatcher handler exists but is **not invoked from any UI today** | Partial (registered, not called) | Yes (project picker) | `ProjectPicker` | No | — | **Yes** (closes EmailFiling WorkTarget) | `ITaskCompletionCoordinator` (VM path) | Sometimes | Coordinator `WorkflowAdvanced` flag | **DuplicatePath** | **High** | Two implementations of the same business action. Future decision required (see §7 of architecture doc). |
| 9 | `ShowAttachmentInAcc` | ViewModel Command | same | VM → browser launch | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | UI-only, not lifecycle-emitting. |
| 10 | `OpenLocalFile` | ViewModel Command | same | VM → shell launch | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | UI-only. |

---

## Section D — `FloatingInspectionViewModel` commands (UI direct)

Source: `SiNetSQL.MVVM.FloatingInspectionViewModel` (commands referenced in
`ActionCodes.cs`). All commands are inspection-domain operations; none of them
participate in `IProcessActionDispatcher`, `ITaskCompletionCoordinator`, or
workflow advance today.

| # | Action / Code | Source Family | Defined In | Current Execution Path | Has Process Handler? | Requires UI? | UI FollowUp / Screen | Creates Task? | Task Creation Path | Completes Task? | Completion Path | Advances Workflow? | Workflow Advance Path | Execution Classification | Risk | Notes / Gap |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `CreateReport` | ViewModel Command | `FloatingInspectionViewModel`, `ActionCodes.cs` | VM direct (inspection services) | No | Yes (template picker) | (inspection dialogs) | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 2 | `OpenTemplate` | same | same | VM direct | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 3 | `ExportReport` | same | same | VM direct (export pipeline) | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 4 | `AddNote` | same | same | VM direct | No | Yes (note dialog) | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 5 | `DeleteReport` | same | same | VM direct | No | Yes (confirm) | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 6 | `AddDrawing` | same | same | VM direct (file picker) | No | Yes | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 7 | `RemoveDrawing` | same | same | VM direct | No | Yes (confirm) | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 8 | `StampDrawings` | same | same | VM direct (PDF stamping) | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 9 | `UnlockReport` | same | same | VM direct (status flip on inspection entity) | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 10 | `ImportPlannerResponses` | same | same | VM direct | No | Sometimes | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 11 | `MarkResponseReceived` | same | same | VM direct | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 12 | `RepullPlannerResponses` | same | same | VM direct | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 13 | `OpenSourceReport` | same | same | VM direct | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 14 | `SelectReviewedPlan` | same | same | VM direct | No | Yes (picker) | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 15 | `OpenNoteLinkedFile` | same | same | VM direct | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 16 | `SetNoteLinkedFile` | same | same | VM direct | No | Yes (picker) | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 17 | `ClearNoteLinkedFile` | same | same | VM direct | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 18 | `AttachScreenshot` | same | same | VM direct (clipboard / screen capture) | No | Yes | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 19 | `OpenLastAttachment` | same | same | VM direct | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |
| 20 | `ShareReportAnyoneWithLink` | same | same | VM direct (Drive sharing) | No | No | — | No | — | No | — | No | — | ViewModelDirect | Low | — |

---

## Section E — Task ViewModels (status-edit bypass surface)

These ViewModels are not listed in `ActionCodes.cs` because they do not expose
named action codes, but they directly manipulate `ProjectAssignment.StatusId`
today and therefore belong in this inventory as **completion-path bypasses**.

| # | Action / Code | Source Family | Defined In | Current Execution Path | Has Process Handler? | Requires UI? | UI FollowUp / Screen | Creates Task? | Task Creation Path | Completes Task? | Completion Path | Advances Workflow? | Workflow Advance Path | Execution Classification | Risk | Notes / Gap |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Inline task status edit | ViewModel Command | `TaskPanelViewModel`, `FloatingProjectTasksViewModel` | VM writes `ProjectAssignment.StatusId` directly via `DbContext` | No | Yes (status combo / button) | — | No | — | **Yes** | **Direct StatusId update — bypasses `ITaskCompletionCoordinator`** | No | — | ViewModelDirect | **High** | Coordinator is **not** the sole writer of task status today. |
| 2 | Inline task creation | ViewModel Command | `TaskPanelViewModel` | VM → `TaskService.GetOrCreateTaskWithDueDateAndStatusAsync` (no direct `DbContext.ProjectAssignments.Add` in the VM) | No | Yes (task dialog) | — | **Yes** | `TaskService.GetOrCreate*` (a service, but **not** `TaskFactory.CreateAsync` — see Phase 2B note in §7) | No | — | No | — | ViewModelDirect | Medium | ~~Not routed through `SmartTaskService` / `TaskFactory`.~~ **Correction (Phase 2 reconnaissance):** the ViewModel does not write `ProjectAssignment` directly; it goes through `TaskService`. However, `TaskService.GetOrCreateTaskWithDueDateAndStatusAsync` still does not delegate to `TaskFactory.CreateAsync` — it duplicates priority insertion and event-logging. Tracked as **Phase 2B**, not as a VM-direct DB write. |

---

## Section F — `IProcessActionHandler` registry (DI snapshot)

Single source of truth: `SiNetProjectManagerV2\App.xaml.cs` (composition root).

| `ActionCode` | Handler class | Notes |
|---|---|---|
| `StartWorkflow` | `StartWorkflowProcessActionHandler` | Returns `Deferred(RequiresUi, FollowUp=ProjectPicker)` when project missing. |
| `LinkToProject` | `LinkToProjectProcessActionHandler` | Used by `SuggestedActionType.LinkToProject`. |
| (same `LinkToProject` code, separate registration) | `LinkToProjectBackendProcessActionHandler` | Backend variant used by the dispatcher path from `AssociateToExistingProject`. **Two classes resolve under the same code via the dispatcher's selection rules — verify before refactor.** |
| `CreateTask` | `CreateTaskProcessActionHandler` | Backend task creation. |
| `FileOnly` | `FileOnlyProcessActionHandler` | — |
| `ApproveOrClose` | `ApproveOrCloseProcessActionHandler` | Followed by lifecycle reporter → completed handler for workflow advance. |
| `AddMaterialToProject` | `AddMaterialToProjectProcessActionHandler` | Returns `Deferred(RequiresUi, FollowUp=FileImportDialog)` when inputs missing. |
| `MoveToProject` | `MoveToProjectProcessActionHandler` | **Registered but not invoked from UI today** (duplicate of VM path). |
| `CreateStageTasks` | `CreateStageTasksProcessActionHandler` | NoOp by design. |
| `ClosePreviousStageTasks` | `ClosePreviousStageTasksProcessActionHandler` | Bulk close — bypasses `ITaskCompletionCoordinator`. |
| `SendNotification` | `SendNotificationProcessActionHandler` | — |
| `StartSubWorkflow` | `StartSubWorkflowProcessActionHandler` | — |
| `SetProjectStatus` | `SetProjectStatusProcessActionHandler` | — |
| `RecordTaskResult` | `RecordTaskResultProcessActionHandler` | — |
| `SetBillingPending` | `SetBillingPendingProcessActionHandler` | — |
| `CloseProject` | `CloseProjectProcessActionHandler` | Bulk close. |

> Handler classes are co-located in
> `SiNetSQL\Domain\Actions\Handlers\` (`AddMaterialToProjectProcessActionHandler.cs`,
> `ApproveOrCloseProcessActionHandler.cs`, `CreateTaskProcessActionHandler.cs`,
> `FileOnlyProcessActionHandler.cs`, `LinkToProjectProcessActionHandler.cs`,
> `MoveToProjectProcessActionHandler.cs`, `StartWorkflowProcessActionHandler.cs`,
> `WorkflowTransitionProcessActionHandlers.cs`).

---

## Summary

### 1. Actions scanned

- **Section A** — `SuggestedActionType`: **39** rows.
- **Section B** — `WorkflowTransitionActionType`: **8** rows.
- **Section C** — `EmailManagementViewModel` commands: **10** rows.
- **Section D** — `FloatingInspectionViewModel` commands: **20** rows.
- **Section E** — Task-VM status-edit bypasses: **2** rows.
- **Total: 79 distinct action rows.**

### 2. Classification counts

| Classification | Count |
|---|---|
| `HasHandler` | **7** (Section A: `FileOnly`, `AddMaterialToProject`, `ApproveOrClose`, `StartWorkflow`, `LinkToProject`, `CreateTask`, `AssociateToExistingProject` post-picker) |
| `LegacyInline` | **4** (`CreatePriceQuote`, `CreateNewReview`, `CreateOpinionProject`, `OpenReviewRound`) |
| `RequiresUiOnly` | **27** (all Design/Review/Opinion task-creation suggestions plus `CreateNewProject`, `CollectMaterial`, `ForwardToDecision`, `AddNewDiscipline`, etc.) |
| `ViewModelDirect` | **31** (9 email VM + 20 inspection VM + 2 task-VM status-edit bypasses) |
| `WorkflowSystemOnly` | **8** (all `WorkflowTransitionActionType` values) |
| `Hybrid` | **1** (`AssociateToExistingProject` — counted in `HasHandler` above; also straddles `RequiresUiOnly`) |
| `DuplicatePath` | **1** (`MoveToProject`) |
| `Unknown` | **0** |

### 3. Top-10 gaps / risks

1. **`MoveToProject` duplicate path** — VM path is the live one, backend handler is registered but unused. Risk: divergence over time. _(Section C)_
2. ~~**Inline task creation in `ActionExecutor`**~~ — **Resolved on inspection (Phase 2 reconnaissance).** Both `CreateReviewChildProjectTaskAsync` and `CreateAuthorityInvitationTrackingTaskAsync` already call `TaskFactory.CreateAsync`; no direct `db.ProjectAssignments.Add` exists at those sites. _(Section A rows 3, 9)_
3. **Inline task status edit in Task VMs** — bypasses `ITaskCompletionCoordinator`; coordinator is not the sole writer of `StatusId` today. _(Section E row 1)_
4. ~~**Inline task creation in `TaskPanelViewModel`**~~ — **Re-scoped (Phase 2 reconnaissance).** `TaskPanelViewModel.CreateTask` calls `TaskService.GetOrCreateTaskWithDueDateAndStatusAsync`, not the `DbContext` directly. The remaining centralization gap is that `TaskService.GetOrCreate*` duplicates `TaskFactory`'s body instead of delegating to it. Tracked as **Phase 2B**, see §7. _(Section E row 2)_
5. **27 RequiresUI-only suggested actions** — no documented backend continuation contract. UI follow-up returns are informal and per-action. _(Section A)_
6. **`CloseOpinion` says `CanAdvanceWorkflow=true` but has no backend** — currently `RequiresUiOnly`; nothing wires the UI confirmation to a workflow advance. _(Section A row 36)_
7. **`ClosePreviousStageTasks` bulk-close bypass** — closes tasks without calling `ITaskCompletionCoordinator`; intentional today, undocumented. _(Section B row 2)_
8. **`CloseProject` bulk-close bypass** — same as above, larger blast radius. _(Section B row 8)_
9. **`LinkToProject` has two registered handler classes** for the same code (`LinkToProjectProcessActionHandler` + `LinkToProjectBackendProcessActionHandler`). The dispatcher's duplicate-code validation does not flag this because they share the code constant; behavior depends on registration order / selection. _(Section F)_
10. **`ApproveOrClose` workflow-advance contract is informal** — relies on the UI writing `PrefilledData["WorkflowInstanceId"]` + `PrefilledData["ConfirmAdvance"]` after a dialog. Easy to break. _(Section A row 29)_

### 4. Top-5 first targets for Phase 2+

1. ~~**Unify task creation** (Phase 2 in roadmap)~~ — **Closed without code changes** after Phase 2 reconnaissance. The three originally-named sites already comply: both `ActionExecutor` inline methods route through `TaskFactory.CreateAsync`, and `TaskPanelViewModel` routes through `TaskService` (not the `DbContext`). See §7 for the residual **Phase 2B** centralization gap inside `TaskService`.
2. **Decide and unify `MoveToProject`** (Phase 3): pick the canonical path (VM or handler), keep both alive during the transition. Both paths already exist and behave; this is an organizational decision.
3. **Close coordinator bypasses for status edits** (Phase 4): route the inline Task VM status edits through `ITaskCompletionCoordinator` (or formally exempt them).
4. **Formalize the RequiresUI continuation contract** (Phase 5 preparation): define how the UI returns control after a `RequiresUI(...)` result. Until that is decided, all 27 `RequiresUiOnly` rows stay where they are.
5. **`CloseOpinion`** specifically: cheapest single example of "RequiresUI without backend continuation" — a good pilot for the contract.

### 5. Inaccuracies found in `Action-Task-Workflow.md`

Two minor clarifications, not contradictions:

- The architecture document lists `MoveToProjectProcessActionHandler` as a
  "future canonical" path; the inventory confirms more precisely that **it is
  registered in DI but currently has zero callers from the WPF UI**. Documented
  here in Sections C and F.
- The architecture document treats `ITaskCompletionCoordinator` as the
  "single decision point for task completion"; the inventory shows this is
  the **intended** model but **not the enforced** model — Section B rows 2/8
  and Section E rows 1/2 are live bypasses today.

No other inaccuracies were found. The `Action-Task-Workflow.md` document remains
consistent with what the code does.

### 6. Phase 2 readiness

**Status (post-reconnaissance): Phase 2 closed with no code changes.**

Phase 2 reconnaissance verified the three originally-named runtime task-creation
sites in source and concluded that each one **already complies** with the
approved canonical rule:

- `ActionExecutor.CreateReviewChildProjectTaskAsync` → calls `TaskFactory.CreateAsync`
  (no direct `db.ProjectAssignments.Add`).
- `ActionExecutor.CreateAuthorityInvitationTrackingTaskAsync` → calls
  `TaskFactory.CreateAsync` (no direct `db.ProjectAssignments.Add`).
- `TaskPanelViewModel.CreateTask` → calls
  `TaskService.GetOrCreateTaskWithDueDateAndStatusAsync` (a service, not the
  `DbContext`). The ViewModel itself does not write `ProjectAssignment`.

The full runtime audit (`new ProjectAssignment` + `db.ProjectAssignments.Add`)
found the following non-exempt sites; all of them route through `TaskFactory`:

| Site | TaskFactory used? |
|---|---|
| `Domain/Actions/Handlers/CreateTaskProcessActionHandler.cs` | Yes |
| `Services/EmailContext/ActionExecutor.cs` (`CreateReviewChildProjectTaskAsync`) | Yes |
| `Services/EmailContext/ActionExecutor.cs` (`CreateAuthorityInvitationTrackingTaskAsync`) | Yes |
| `Services/Workflow/WorkflowTaskOrchestrator.cs` (×2) | Yes |
| `Services/TaskLifecycle/TaskLifecycleService.cs` | Yes |
| `Services/TaskImport/TaskImportService.cs` | **Exempt** — migration/import path per the architecture decisions. |
| `Services/TaskService.cs` (×4: `GetOrCreateTaskWithDueDate[AndStatus][Async]`) | **No** — see Phase 2B below. |

**Conclusion:** the original Phase 2 scope is fulfilled in code already; no
refactor was required and no code was changed. Phases 3–6 remain gated on the
Decision Points in `Action-Task-Workflow.md` §7.

---

## 7. Phase 2B — Deferred: `TaskService` / `TaskFactory` unification

> **Status:** Deferred. Documentation-only entry. No code changes, no DB
> changes, no tests added.

### 7.1 Gap

`TaskService.GetOrCreateTaskWithDueDate`,
`TaskService.GetOrCreateTaskWithDueDateAndStatus`, and their `*Async`
counterparts (`TaskService.cs` lines 391, 440, 501, 552) construct
`ProjectAssignment` and call `TaskPriorityEngine.InsertWithAutoPriority(Async)`
+ `LogTaskEvent(Async)` themselves instead of delegating to
`TaskFactory.CreateAsync`. This is a **service-level centralization gap**,
not a ViewModel-direct or ActionExecutor-direct DB write.

### 7.2 Why Phase 2B is not behavior-neutral

A literal "delegate to `TaskFactory.CreateAsync`" rewrite would change
observable behavior in at least the following ways:

| Aspect | `TaskService.GetOrCreate*` (today) | `TaskFactory.CreateAsync` (today) |
|---|---|---|
| **Idempotency** | Reuses an existing task by `(ProjectId, AssignedToId, TaskTypeId)` via `GetTask(...)`. | No idempotency check; caller's responsibility. |
| **Title generation** | Auto-generates `TASK-{p}-{a}-{tt}-{ts}` via `GenerateTaskTitle`. | Caller-supplied; no fallback. |
| **Creation note** | Rich note: `"משימה נוצרה, תאריך יעד: …, סטטוס: …"`. | Default `"משימה נוצרה"` unless `eventNote` passed in. |
| **`Created` timestamp** | `DateTime.Now` (Local). | Caller's value (or `DateTime.Now` if null); event `CreatedDate = DateTime.UtcNow`. |
| **Status validation** | Validates the status exists; throws `InvalidOperationException` if not. | No status validation. |
| **Priority insertion** | `TaskPriorityEngine.InsertWithAutoPriority(Async)` is always called. | Only called when `AssignedToId.HasValue`; unassigned tasks skip it. |

Any of these changes would be visible in the audit trail, in the priority
queue, in the UI, or in error semantics. Therefore Phase 2B **cannot** be
treated as a mechanical refactor and is deferred until each item above has
an explicit decision.

### 7.3 Open Phase 2B decisions (deferred)

1. Should `TaskService` become a thin idempotency + title-generation wrapper
   that delegates the actual insert/event/link work to `TaskFactory.CreateAsync`?
2. Should the richer creation-event note continue to be produced (passed via
   `eventNote`), or do we accept `TaskFactory`'s default note?
3. Should the event timestamp move from `DateTime.Now` (Local) to `DateTime.UtcNow`?
4. Should `TaskFactory.CreateAsync` be extended to insert priority even when
   `AssignedToId` is `null` (matching `TaskService` semantics), or is the
   current divergence intentional?
5. Should status existence still be validated, and at which layer?
6. Does any new `TaskDraft` DTO (per Typed-Continuation-Design §6 and D-6)
   share a shape with the `TaskService` parameters, or are they intentionally
   distinct?

Phase 2B is not scheduled. It will be reconsidered once one of the following
triggers occurs: (a) Phase 5 introduces typed `TaskCreationContinuationResult`
flows that need a unified factory, or (b) an independent decision is made to
remove `TaskService.GetOrCreate*` duplication.

---

## 8. Status summary

| Item | Status |
|---|---|
| Phase 0 — Documentation | **Completed.** |
| Phase 1 — Handler / Action inventory | **Completed.** |
| Typed Continuation Design | **Completed (documentation).** |
| Architecture decisions (Canonical path, RequiresUI typed continuation, runtime task creation, Coordinator policy, etc.) | **Approved.** |
| **Original Phase 2 — Unify runtime task creation** | **Closed with no code changes** (reconnaissance verified compliance). |
| **Phase 2B — `TaskService` / `TaskFactory` unification** | **Deferred** (not behavior-neutral; open decisions per §7.3). |
| Phase 3 — Unify `MoveToProject` | Not started. |
| Phase 4 — Close coordinator bypasses | Not started. |
| Phase 5 — Migrate `RequiresUI` actions (typed continuation) | Not started. |
| Phase 6 — Move `ActionFollowUp` to `Domain/Actions` | Not started (may collapse into Typed Continuation D-4). |

**No runtime code was changed as part of this Phase 2 closure.**
