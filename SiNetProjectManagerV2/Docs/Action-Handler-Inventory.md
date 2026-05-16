# Action / Handler Inventory (Phase 1)

> **Status:** Documentation only. No code changes, no DB changes, no migrations,
> no refactor, no deletion of legacy paths, no handler moves, no UI changes.
> This file is the **mapping artefact** required by Phase 1 of the roadmap
> defined in [`Action-Task-Workflow.md`](./Action-Task-Workflow.md).
>
> **TaskCreationDialog migration note (Phase 5 extension):**
> The actions listed in `SiNetSQL.Domain.Actions.Continuation.TaskCreationActionCatalog`
> are **typed-only**. For those actions:
> - the only valid path is `ITaskCreationContinuationApplicationService` →
>   `IActionContinuationUiHost` → `TaskCreationDraftDialog` →
>   `TaskFactory.CreateAsync`;
> - the legacy `RequiresUI(TaskCreationDialog)` / `AssignActionDialog` /
>   `EmailManagementView.CreateActionTaskAsync` path is **not used**;
> - any inventory row below that still describes the legacy path for a
>   migrated action should be read as describing **dead code** retained only
>   until the legacy branches are deleted. Non-migrated TaskCreation rows (if
>   any) keep their legacy classification.
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
| 7 | `ForwardToDecision` | SuggestedActionType | same | **Typed Continuation** via `IDecisionContinuationApplicationService` → `WpfActionContinuationUiHost` → existing `ProjectDecisionsWindow`. `ActionExecutor` refuses legacy `RequiresUI(DecisionDialog)` via `DecisionTypedRequired()` — **no legacy fallback**. | No | Yes (typed) | `DecisionDialog` (typed `ContinuationUiKind.DecisionDialog`) | No | — | No | — | No | — | TypedContinuation | Low | Migrated in Phase 5. `ProjectDecisionsWindow` persists decisions internally via `ProjectDecisionService`; typed result carries only lifecycle outcome. |
| 8 | `FileOnly` | SuggestedActionType | same | `ActionExecutor.DispatchFileOnlyAsync` → `FileOnlyProcessActionHandler` | **Yes** | No | — | No | — | No | — | No | — | HasHandler | Low | Clean dispatcher path. |
| 9 | `RequestAuthorityInvitation` | SuggestedActionType | same | `ActionExecutor.CreateAuthorityInvitationTrackingTaskAsync` (inline) | No | No | — | **Yes** | **`TaskFactory.CreateAsync`** (verified during Phase 2 reconnaissance) | No | — | No | — | LegacyInline | Low | ~~Direct EF write bypasses task service.~~ **Correction (Phase 2 reconnaissance):** this site already routes through `TaskFactory.CreateAsync` and complies with the canonical task-creation rule. `LegacyInline` here refers only to the inline ActionExecutor branch, not to direct EF writes. |
| 10 | `AddMaterialToProject` | SuggestedActionType | same | `ActionExecutor.DispatchAddMaterialToProjectAsync` → `AddMaterialToProjectProcessActionHandler` | **Yes** | Sometimes | `FileImportDialog` when inputs missing (Deferred) | No | — | Sometimes (review WorkTarget done) | `ITaskCompletionCoordinator` (via filing service) | Sometimes | `TaskCompletionCoordinator` `WorkflowAdvanced` flag | HasHandler | Low | Phase-3 dispatcher path is live. |
| 11 | `RequestCompletion` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes (after UI) | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 12 | `PrepareResponse` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 13 | `InternalReview` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 14 | `AddNewDiscipline` | SuggestedActionType | same | **Typed Continuation** via `IDisciplineContinuationApplicationService` → `WpfActionContinuationUiHost` (confirmation prompt; no dedicated WPF surface today). `ActionExecutor` refuses legacy `RequiresUI(DisciplineDialog)` via `DisciplineTypedRequired()` — **no legacy fallback**. | No | Yes (typed) | `DisciplineDialog` (typed `ContinuationUiKind.DisciplineDialog`) | No | — | No | — | No | — | TypedContinuation | Low | Migrated in Phase 5. Confirmation-only adapter; typed result carries only lifecycle outcome. |
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
| 29 | `ApproveOrClose` | SuggestedActionType | same | Canonical (typed): VM → `IApproveOrCloseApplicationService` → `IProcessActionDispatcher` → `ApproveOrCloseProcessActionHandler`. Legacy bridge `ActionExecutor.DispatchApproveOrCloseAsync` preserved for the existing `EmailContextViewModel` retry path. | **Yes** | Yes | `WorkflowAdvanceDialog` | No | — | No | — | Yes (after UI confirms `ConfirmAdvance`) | `WorkflowActionLifecycleReporter` → `WorkflowActionCompletedHandler` | HasHandler | Low | **Phase 5 pilot closed.** Typed `WorkflowAdvanceContinuationRequest` / `WorkflowAdvanceContinuationResult` are emitted via `ProcessActionResult.Data`; legacy `PrefilledData["ConfirmAdvance"]` is kept only as a fallback for the un-migrated UI retry path. |
| 30 | `ReceiveMaterialForOpinion` | SuggestedActionType | same | `RequiresUI(FileImportDialog)` | No | Yes | `FileImportDialog` | No | — | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 31 | `AnalyzeDocuments` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 32 | `RequestMissingMaterial` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 33 | `PrepareDraftOpinion` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 34 | `UpdateOpinion` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation. |
| 35 | `SendOpinion` | SuggestedActionType | same | `RequiresUI(TaskCreationDialog)` | No | Yes | `TaskCreationDialog` | Sometimes | Unknown | No | — | No | — | RequiresUiOnly | Medium | No backend continuation, despite `CanAdvanceWorkflow=true` in registry. |
| 36 | `CloseOpinion` | SuggestedActionType | same | Canonical (typed): VM → `ICloseOpinionApplicationService` → `IProcessActionDispatcher` → `CloseOpinionProcessActionHandler`. Legacy bridge `ActionExecutor.DispatchCloseOpinionAsync` mirrors the ApproveOrClose retry path. | **Yes** | Yes | `WorkflowAdvanceDialog` | No | — | No | — | Yes (after UI confirms `ConfirmAdvance`) | `WorkflowActionLifecycleReporter` → `WorkflowActionCompletedHandler` | HasHandler | Low | **Closed (this batch).** Same typed continuation contract as `ApproveOrClose`. Legacy `PrefilledData["ConfirmAdvance"]` accepted as a fallback. |
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
| 8 | `MoveToProject` | **Dispatcher (canonical, Phase 3 closed)** | `EmailManagementViewModel.MoveToProjectViaServiceAsync` → `IEmailMoveToProjectApplicationService.MoveAsync` → `IProcessActionDispatcher.DispatchAsync(ActionCodes.MoveToProject)` → `MoveToProjectProcessActionHandler` | VM calls the application service only; the application service builds `ActionExecutionContext` and delegates to the handler via the dispatcher. VM does not reference `IProcessActionDispatcher` or the handler. | Yes (invoked through the dispatcher by the application service) | Yes (project picker) | `ProjectPicker` | No | — | **Yes** (closes EmailFiling WorkTarget via handler) | `ITaskCompletionCoordinator` (inside `MoveToProjectProcessActionHandler` when `ActionExecutionContext.EmailFilingTask` is set) | Sometimes | Coordinator `WorkflowAdvanced` flag | DispatcherCanonical | Low | Phase 3 closed. Duplicate-path issue resolved; duplicate-target validation reads DB-persisted inbox attachments inside the handler. Remaining caveats: Typed Continuation still not implemented; VM still owns UI feedback. |
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
| `StartWorkflow` | `StartWorkflowProcessActionHandler` | Returns `Deferred(RequiresUi, FollowUp=ProjectPicker)` when project missing. **Migrated:** the `RequiresUi` outcome is intercepted by `ActionExecutor.DispatchStartWorkflowAsync` and converted to `ProjectPickerTypedRequired(StartWorkflow)`; the legacy `RequiresUI(ActionFollowUp.ProjectPicker)` is no longer emitted for this action. |
| `LinkToProject` | `LinkToProjectProcessActionHandler` | Used by `SuggestedActionType.LinkToProject`. |
| (same `LinkToProject` code, separate registration) | `LinkToProjectBackendProcessActionHandler` | Backend variant used by the dispatcher path from `AssociateToExistingProject`. **Two classes resolve under the same code via the dispatcher's selection rules — verify before refactor.** |
| `CreateTask` | `CreateTaskProcessActionHandler` | Backend task creation. |
| `FileOnly` | `FileOnlyProcessActionHandler` | — |
| `ApproveOrClose` | `ApproveOrCloseProcessActionHandler` | Followed by lifecycle reporter → completed handler for workflow advance. Typed continuation via `WorkflowAdvanceContinuationRequest` / `WorkflowAdvanceContinuationResult`; legacy `ConfirmAdvance` fallback preserved. Application service: `IApproveOrCloseApplicationService`. |
| `CloseOpinion` | `CloseOpinionProcessActionHandler` | Same typed-continuation pattern as `ApproveOrClose`. Application service: `ICloseOpinionApplicationService`. |
| `AddMaterialToProject` | `AddMaterialToProjectProcessActionHandler` | Returns `Deferred(RequiresUi, FollowUp=FileImportDialog)` when inputs missing. |
| `MoveToProject` | `MoveToProjectProcessActionHandler` | **Canonical handler (Phase 3 closed).** Invoked from the WPF UI via `EmailManagementViewModel` → `IEmailMoveToProjectApplicationService` → `IProcessActionDispatcher`. Owns duplicate-target validation against DB-persisted inbox attachments and `ITaskCompletionCoordinator` reporting when `ActionExecutionContext.EmailFilingTask` is set. |
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
| `DuplicatePath` | **0** (`MoveToProject` resolved in Phase 3 — see Section C / F) |
| `Unknown` | **0** |

### 3. Top-10 gaps / risks

1. ~~**`MoveToProject` duplicate path**~~ — **Resolved (Phase 3 closed).** Canonical path is `EmailManagementViewModel` → `IEmailMoveToProjectApplicationService` → `IProcessActionDispatcher` → `MoveToProjectProcessActionHandler` → `IProjectFileFilingService` → `ITaskCompletionCoordinator` (when `EmailFilingTask` context exists). Duplicate validation now reads DB attachments inside the handler. Remaining caveats: Typed Continuation still not implemented; VM still owns UI feedback. _(Section C, F)_
2. ~~**Inline task creation in `ActionExecutor`**~~ — **Resolved on inspection (Phase 2 reconnaissance).** Both `CreateReviewChildProjectTaskAsync` and `CreateAuthorityInvitationTrackingTaskAsync` already call `TaskFactory.CreateAsync`; no direct `db.ProjectAssignments.Add` exists at those sites. _(Section A rows 3, 9)_
3. **Inline task status edit in Task VMs** — bypasses `ITaskCompletionCoordinator`; coordinator is not the sole writer of `StatusId` today. _(Section E row 1)_
4. ~~**Inline task creation in `TaskPanelViewModel`**~~ — **Re-scoped (Phase 2 reconnaissance).** `TaskPanelViewModel.CreateTask` calls `TaskService.GetOrCreateTaskWithDueDateAndStatusAsync`, not the `DbContext` directly. The remaining centralization gap is that `TaskService.GetOrCreate*` duplicates `TaskFactory`'s body instead of delegating to it. Tracked as **Phase 2B**, see §7. _(Section E row 2)_
5. **27 RequiresUI-only suggested actions** — no documented backend continuation contract. UI follow-up returns are informal and per-action. _(Section A)_
6. ~~**`CloseOpinion` says `CanAdvanceWorkflow=true` but has no backend**~~ — **Resolved (this batch).** `CloseOpinionProcessActionHandler` + `ICloseOpinionApplicationService` now provide the same typed-continuation workflow-advance path as `ApproveOrClose`. _(Section A row 36, Section F)_
7. **`ClosePreviousStageTasks` bulk-close bypass** — closes tasks without calling `ITaskCompletionCoordinator`; intentional today, undocumented. _(Section B row 2)_
8. **`CloseProject` bulk-close bypass** — same as above, larger blast radius. _(Section B row 8)_
9. **`LinkToProject` has two registered handler classes** for the same code (`LinkToProjectProcessActionHandler` + `LinkToProjectBackendProcessActionHandler`). The dispatcher's duplicate-code validation does not flag this because they share the code constant; behavior depends on registration order / selection. _(Section F)_
10. ~~**`ApproveOrClose` workflow-advance contract is informal**~~ — **Resolved (Phase 5 pilot closed).** Typed `WorkflowAdvanceContinuationRequest` / `WorkflowAdvanceContinuationResult` contracts are now the canonical handler↔application-service bridge; legacy `PrefilledData["ConfirmAdvance"]` is preserved only for the un-migrated `EmailContextViewModel` retry path. _(Section A row 29)_

### 4. Top-5 first targets for Phase 2+

1. ~~**Unify task creation** (Phase 2 in roadmap)~~ — **Closed without code changes** after Phase 2 reconnaissance. The three originally-named sites already comply: both `ActionExecutor` inline methods route through `TaskFactory.CreateAsync`, and `TaskPanelViewModel` routes through `TaskService` (not the `DbContext`). See §7 for the residual **Phase 2B** centralization gap inside `TaskService`.
2. ~~**Decide and unify `MoveToProject`**~~ (Phase 3) — **Closed.** Application service now delegates through `IProcessActionDispatcher` to `MoveToProjectProcessActionHandler`; VM boundary unchanged.
3. **Close coordinator bypasses for status edits** (Phase 4): route the inline Task VM status edits through `ITaskCompletionCoordinator` (or formally exempt them).
4. **Formalize the RequiresUI continuation contract** (Phase 5 preparation): define how the UI returns control after a `RequiresUI(...)` result. Until that is decided, all 27 `RequiresUiOnly` rows stay where they are.
5. ~~**`CloseOpinion`**~~ — **Closed (this batch).** Implemented as the second typed-continuation flow after the `ApproveOrClose` pilot.

### 5. Inaccuracies found in `Action-Task-Workflow.md`

Two minor clarifications, not contradictions:

- The architecture document originally listed `MoveToProjectProcessActionHandler`
  as a "future canonical" path; as of Phase 3 closure it **is** the canonical
  backend path, reached through `IEmailMoveToProjectApplicationService` +
  `IProcessActionDispatcher`. The application service is preserved as the
  VM-facing boundary for UI-shaped result mapping. Documented here in
  Sections C and F.
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

## 7. Phase 2B — Partially implemented: `TaskService` / `TaskFactory` unification

> **Status:** Async variants implemented and characterization-tested. Sync
> variants remain deferred. See §7.4 for the implemented scope and §7.5 for
> what remains open.

### 7.1 Gap (original)

`TaskService.GetOrCreateTaskWithDueDate`,
`TaskService.GetOrCreateTaskWithDueDateAndStatus`, and their `*Async`
counterparts construct `ProjectAssignment` and call
`TaskPriorityEngine.InsertWithAutoPriority(Async)` + `LogTaskEvent(Async)`
themselves instead of delegating to `TaskFactory.CreateAsync`. This is a
**service-level centralization gap**, not a ViewModel-direct or
ActionExecutor-direct DB write.

### 7.2 Why a literal Phase 2B rewrite is not behavior-neutral

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
treated as a mechanical refactor across both sync and async paths.

### 7.3 Implementation decision

The async paths can safely delegate to `TaskFactory.CreateAsync` if
`TaskService` continues to own:

- Idempotency lookup via `GetTask(...)`.
- Title generation via `GenerateTaskTitle(...)`.
- Status existence validation (throwing `InvalidOperationException`).
- Pre-computation of the rich creation note (passed as `eventNote`).

With those responsibilities retained, `TaskFactory.CreateAsync` provides the
shared insert + priority + event + optional link work. The sync variants
have no sync `TaskFactory` entry point, so changing them would require a
broader decision and is intentionally out of scope for this round.

### 7.4 Implemented scope (async)

- `TaskService.GetOrCreateTaskWithDueDateAsync` now delegates creation to
  `TaskFactory.CreateAsync` after building the `ProjectAssignment` and the
  precomputed note (`"משימה נוצרה, תאריך יעד: …, סטטוס: …"`).
- `TaskService.GetOrCreateTaskWithDueDateAndStatusAsync` does the same with
  the caller-provided status (validated first).
- Idempotency, title format, status validation, note text, and work-priority
  behavior are preserved and pinned by
  `SiNetSQL.Tests/Services/TaskServiceGetOrCreateAsyncTests.cs`
  (7 characterization tests, all passing).
- Full `SiNetSQL.Tests` suite remained green after the refactor (334 passed
  / 0 failed / 2 pre-existing `ExecuteDeleteAsync` InMemory skips).

### 7.5 Open Phase 2B decisions (still deferred — sync path)

1. Should the sync `GetOrCreateTaskWithDueDate` /
   `GetOrCreateTaskWithDueDateAndStatus` be removed entirely in favor of the
   async variants, or should a sync `TaskFactory.Create` be introduced to
   mirror the async delegation?
2. Is the divergence where `TaskFactory.CreateAsync` only inserts priority
   when `AssignedToId.HasValue` (vs. `TaskService` always inserting) still
   intentional for callers that pass an assignee? (Async tests confirm
   current behavior; no change made.)
3. Should the event timestamp policy be unified (Local vs. UTC) across both
   layers, or do we keep `TaskFactory`'s `DateTime.UtcNow` for events and
   `DateTime.Now` for `Created`?
4. Does any future `TaskDraft` DTO (per Typed-Continuation-Design §6 and
   D-6) share a shape with the `TaskService` parameters, or are they
   intentionally distinct?

Sync Phase 2B remains unscheduled. It will be reconsidered once one of the
following triggers occurs: (a) Phase 5 introduces typed
`TaskCreationContinuationResult` flows that need a unified factory across
both sync and async callers, or (b) an independent decision is made to
remove the sync `TaskService.GetOrCreate*` duplication.

---

## 8. Status summary

| Item | Status |
|---|---|
| Phase 0 — Documentation | **Completed.** |
| Phase 1 — Handler / Action inventory | **Completed.** |
| Typed Continuation Design | **Completed (documentation).** |
| Architecture decisions (Canonical path, RequiresUI typed continuation, runtime task creation, Coordinator policy, etc.) | **Approved.** |
| **Original Phase 2 — Unify runtime task creation** | **Closed with no code changes** (reconnaissance verified compliance). |
| **Phase 2B — `TaskService` / `TaskFactory` unification** | **Partially implemented.** Async `GetOrCreateTaskWithDueDateAsync` and `GetOrCreateTaskWithDueDateAndStatusAsync` now delegate creation to `TaskFactory.CreateAsync` (idempotency, title, status validation, and note text retained in `TaskService`); behavior pinned by `TaskServiceGetOrCreateAsyncTests`. Sync variants remain deferred per §7.5. |
| Phase 3 — Unify `MoveToProject` | **Closed.** Application service delegates to dispatcher → handler; VM unchanged. Caveats: Typed Continuation still not implemented; VM still owns UI feedback; tag/alternative persistence is eventually-consistent (see Action-Task-Workflow §6.2). |
| Phase 4 — Close coordinator bypasses | **Completed for completion paths.** `TaskStatusService` is the single entry point for regular status updates; `ITaskCompletionCoordinator` (`AdminSetCompletedAsync`, `CloseTasksAsSystemAsync`) is the single entry point for completed/admin/system/bulk-close. Inline Task-VM status edits route through `TaskStatusService` (regular) and `ITaskCompletionCoordinator` (completion). The bulk-close handlers (`ClosePreviousStageTasksProcessActionHandler`, `CloseProjectProcessActionHandler`) are formally documented as the system bulk-close authority. |
| Phase 5 — Migrate `RequiresUI` actions (typed continuation) | **Pilot + CloseOpinion + TaskCreation family + FileImport family + ProjectPicker family + NewProject family closed.** `ApproveOrClose` and `CloseOpinion` migrated to `WorkflowAdvanceContinuationRequest`/`Result`. The TaskCreation family is end-to-end typed via `TaskCreationActionCatalog` → `ITaskCreationContinuationApplicationService` → `TaskCreationDraftDialog` → `TaskFactory.CreateAsync`. The FileImport family (`UploadNewVersion`, `ReceiveSupplementaryMaterial`, `ReceiveMaterialForReview`, `ReceiveCorrectedVersion`, `ReceiveMaterialForOpinion`) is end-to-end typed via `FileImportActionCatalog` → `IFileImportContinuationApplicationService` → `FileImportDialog` (draft-only) → per-selection `AddMaterialToProject` dispatch. The ProjectPicker family (`AssociateToExistingProject`, `StartWorkflow`, `CreateNewReview`, `CreateOpinionProject`, `OpenReviewRound`) is end-to-end typed via `ProjectPickerActionCatalog` → `IProjectPickerContinuationApplicationService` → existing `ProjectSelectorDialog` (reused unchanged via `WpfActionContinuationUiHost`) → re-dispatch through `ActionExecutor.ExecuteAsync` with `PrefilledData["ProjectId"]`. The NewProject family (`CreateNewProject` only) is end-to-end typed via `NewProjectActionCatalog` → `INewProjectContinuationApplicationService` → existing `CreateProjectUserControl` (reused unchanged, hosted in a modal `Window` by `WpfActionContinuationUiHost` which subscribes to `CreateProjectViewModel.ProjectCreated` to capture the new `ProjectId`/`ProjectName`). The dialog still owns persistence, email-link, and workflow-advance side effects — the typed result reports only the lifecycle outcome and `CreatedProjectId`. `ActionExecutor` enforces no-legacy-fallback via `TaskCreationTypedRequired(...)`, `FileImportTypedRequired(...)`, `ProjectPickerTypedRequired(...)`, and `NewProjectTypedRequired(...)`. `CollectMaterial`, `AddMaterialToProject`, and `CreatePriceQuote` intentionally remain on their existing paths. Remaining un-migrated `RequiresUI` families are `DecisionDialog` and `DisciplineDialog` per `Typed-Continuation-Design.md` §15.4. |
| Phase 6 — Move `ActionFollowUp` to `Domain/Actions` | Not started (may collapse into Typed Continuation D-4). |

**No runtime code was changed as part of this Phase 2 closure.**
