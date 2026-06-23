# Workflow UI Screens

- **Date:** 21.06.2026
- **Status:** Active
- **Scope:** All workflow-related UI screens, their relationship to workflow definitions / instances / stage tasks / task provisioning / project creation, and connection to the Google Sheet Review Migration design

---

## 1. Purpose

Document the current Workflow UI screens and how they relate to Workflow definitions, Workflow instances, stage tasks, task provisioning, and project creation. This document connects the existing Workflow Principles (`WorkflowPrinciples-2026-05-26.md`) and Tasking Coexistence documentation (`TaskingWorkflowCoexistence-2026-06-19.md`) with the actual screens.

---

## 2. Screen inventory

### Summary

| Screen | Type | Location | Status | Role |
|---|---|---|---|---|
| WorkflowDashboardView | UserControl | `WPFUserControl/` | Active | Project-scoped instance list + start |
| WorkflowDesignerView | UserControl | `WPFUserControl/` | Active / Admin | Visual definition editor (drag-and-drop canvas) |
| WorkflowManagementWindow | Dialog | `Dialogs/` | Active / Admin | 5-tab admin hub |
| WorkflowCreateProjectWindow | Dialog | `Dialogs/` | Active | Email → Project + Workflow start |
| WorkflowStatusMonitorWindow | Floating Window | `WPF Window/` | Active | System-wide floating monitor (auto-refresh) |
| WorkflowInstanceWindow | Dialog | `WPF Window/` | Active | Single instance detail + advance/pause/complete |
| WorkflowDesignerHelpWindow | Dialog | `WPF Window/` | Active / Admin | Designer help content |
| FloatingProjectTasksView | UserControl | `WPFUserControl/` | Active | Project-centric task view (workflow + standalone) |
| TaskPanelView | UserControl | `WPFUserControl/` | Active | Employee-centric task queue |

---

## 3. Screen details

### 3.1. WorkflowDashboardView

**Files:**
- XAML: `SiNetProjectManagerV2\WPFUserControl\WorkflowDashboardView.xaml` (87 lines)
- Code-behind: `WorkflowDashboardView.xaml.cs` (61 lines)
- ViewModel: `SiNetSQL\MVVM\WorkflowDashboardViewModel.cs` (284 lines)

**What it does:**
Project-scoped dashboard showing workflow instances for a selected project. Hebrew UI ("לוח תהליכי עבודה").

**Key UI elements:**
- Project selector ComboBox
- "Show active only" checkbox
- Definition selector (only workflows allowed by `ProjectWorkflowPolicyService`)
- "▶ הפעל תהליך" (Start workflow) button
- DataGrid: WorkflowDefinition.Name, CurrentStage.Name, Status, CreatedAtUtc, Notes
- Double-click opens `WorkflowInstanceWindow`

**How a workflow is started from the UI:**
`StartWorkflowCommand` → `WorkflowDashboardViewModel.StartWorkflowAsync()` → `WorkflowTaskOrchestrator.StartWorkflowAsync()` with `WorkflowTriggerType.Manual`. After start, invokes `InstanceStarted` callback to refresh the parent view.

**Data loading:**
`LoadInstancesAsync()` → `WorkflowQueryService.GetByProjectAsync(projectId)`. Filters by active-only toggle and selected definition.

**Allowed definitions:**
`LoadAllowedDefinitionsAsync()` → `ProjectWorkflowPolicyService.GetAllowedWorkflowsAsync(projectId)`. Only definitions allowed for the project's `ProjectType` appear in the picker.

### 3.2. WorkflowDesignerView

**Files:**
- XAML: `SiNetProjectManagerV2\WPFUserControl\WorkflowDesignerView.xaml` (1016 lines)
- Code-behind: `WorkflowDesignerView.xaml.cs` (186 lines)
- ViewModel: `SiNetSQL\MVVM\WorkflowDesignerViewModel.cs` (1028 lines)

**What it does:**
Full visual workflow definition editor. Users **can** edit definitions from the UI. Rich drag-and-drop canvas with nodes and connectors.

**Node types:**
- Stage (rounded rectangle) — the primary workflow step
- Decision (diamond) — conditional branching
- Start / End (circle) — lifecycle markers
- Fork / Join (bar) — parallel execution
- SubWorkflow (double-border rectangle) — nested workflow reference

**Properties panel (right sidebar):**
- Node properties: Name, Code, Description, IsInitial, IsFinal
- Stage nodes: `AssignedGroupId` (responsible group), editable Tasks grid (TaskType, DefaultAssignee, IsRequired)
- Decision nodes: ConditionExpression
- SubWorkflow nodes: linked definition selector, SubWorkflowWaitMode
- Start node: Start Triggers list (EmailFiled, Manual, etc.)
- Connector properties: Label, Condition, Priority, TriggerType, ConditionType, Actions list

**Key commands:**
Add nodes (Stage/Decision/Fork/Join/SubWorkflow/Start/End), Connect/Delete, Save, Validate, AutoLayout. AddStageTask/RemoveStageTask for stage task templates. AddStartTrigger/RemoveStartTrigger. AddAction/RemoveAction for transition actions.

**Save:** `SaveCommand` → `SaveAsync()` → persists definition changes to DB.

**Whether users can edit:** Yes. The designer allows full CRUD on workflow definitions. However, this is an **admin-only** screen — it is embedded in the WorkflowManagementWindow, which is accessed from the admin menu.

### 3.3. WorkflowManagementWindow

**Files:**
- XAML: `SiNetProjectManagerV2\Dialogs\WorkflowManagementWindow.xaml` (586 lines)
- Code-behind: `WorkflowManagementWindow.xaml.cs` (3548 lines — no ViewModel, code-behind-heavy)

**What it does:**
Unified admin window with 5 tabs for complete workflow administration. Title: "ניהול תהליכי עבודה".

**5 tabs:**

1. **🏗️ בונה תהליכים ומשימות (Builder):**
   TreeView of Workflow → Stage → Task hierarchy with detail panel. Tree nodes: workflow, stage, task, transition groups (forward/backward), individual transitions, task groups. Full CRUD for creating workflows, stages, tasks, and transitions.

2. **🎨 עורך ויזואלי (Visual Designer):**
   Embeds `WorkflowDesignerView` UserControl directly.

3. **⚙️ מדיניות פר-סוג-פרויקט (Policy):**
   Maps workflows to project types. Left: ProjectType list with search. Right: 3 sub-tabs:
   - Allowed Workflows: CheckBoxes with IsEnabled/IsDefault per workflow per ProjectType.
   - Active Stages: Enable/disable stages per ProjectType with IsActive/IsRequired checkboxes.
   - Active Disciplines: Enable/disable discipline TaskTypes per ProjectType.

4. **📊 לוח תהליכים (Dashboard):**
   Inline dashboard (duplicated functionality from WorkflowDashboardView): project combo, active-only filter, definition combo, start button, instances DataGrid. Double-click opens WorkflowInstanceWindow.

5. **🧠 התנהגויות משימה (Task Behaviors):**
   Define `TaskBehaviorDefinitions`: what triggers create a task, what completes it. ListBox + detail panel.

**Architectural note:** All 5 tabs use direct code-behind (3548 lines, no ViewModel). This is **active legacy** — candidate for future MVVM refactoring.

### 3.4. WorkflowCreateProjectWindow

**Files:**
- XAML: `SiNetProjectManagerV2\Dialogs\WorkflowCreateProjectWindow.xaml` (37 lines)
- Code-behind: `WorkflowCreateProjectWindow.xaml.cs` (460 lines)

**What it does:**
Project creation from a workflow task (e.g., OpenQuoteProject). Split layout: email preview (top) + project creation form (bottom).

**How workflow project creation works:**
1. Opens with an email message ID (and optionally a task context).
2. Top panel: `EmailViewerControl` displays the originating email.
3. Bottom panel: `CreateProjectUserControl` — standard project creation form.
4. On project creation (`OnProjectCreated`):
   - Applies Gmail label.
   - Starts continuation workflow via `WorkflowTaskOrchestrator.StartWorkflowAsync()`.
   - Gets allowed workflows via `ProjectWorkflowPolicyService.GetAllowedWorkflowsAsync()`.
   - Picks first active, starts with `WorkflowTriggerType.Email`.
   - Special case: Review workflow starts at `REV.MaterialIntake`.
   - Triggers local UI refresh.

### 3.5. WorkflowStatusMonitorWindow

**Files:**
- XAML: `SiNetProjectManagerV2\WPF Window\WorkflowStatusMonitorWindow.xaml` (281 lines)
- Code-behind: `WorkflowStatusMonitorWindow.xaml.cs` (77 lines)
- ViewModel: `SiNetSQL\MVVM\WorkflowStatusViewModel.cs` (266 lines)

**What it does:**
Floating always-on-top window monitoring ALL workflow instances across the system. Auto-refreshes every 15 seconds.

**Key UI elements:**
- Toolbar: Search text, workflow type filter, status filter (All/Active/Completed/Draft), Refresh, auto-refresh toggle, pin toggle.
- Master DataGrid: WorkflowName, ProjectDisplay, StatusText (color-coded), CurrentStageName, ProgressText, Pipeline dots (green=completed, blue=current, gray=future).
- Detail panel (right side): Workflow info, progress bar, vertical pipeline view, "📂 פתח תהליך" button.
- Double-click → opens WorkflowInstanceWindow.

**Data loading:**
`WorkflowQueryService.GetAllWorkflowInstanceSnapshotsAsync()` — loads ALL instances with stages and transitions. Client-side filtering by type, status, search text.

### 3.6. WorkflowInstanceWindow

**Files:**
- XAML: `SiNetProjectManagerV2\WPF Window\WorkflowInstanceWindow.xaml` (136 lines)
- Code-behind: `WorkflowInstanceWindow.xaml.cs` (40 lines)
- ViewModel: `SiNetSQL\MVVM\WorkflowInstanceViewModel.cs` (333 lines)

**What it does:**
Detail view for a single workflow instance. Shows stages, transition history, and action controls.

**Key UI elements:**
- Header: Definition name, status (Hebrew), current stage name.
- Stages overview: Visual pipeline WrapPanel with colored badges (green=initial, blue=final).
- Transition History DataGrid: ToStage, TransitionedByUser, TransitionedAtUtc, Notes.
- Advance controls: ComboBox for AllowedNextStages, "▶ קדם" (Advance) button, notes TextBox.
- Action buttons: ⏸ Pause, ▶ Resume, ✔ Complete, ✖ Cancel.

**How workflow status is displayed:**
Status is shown as a Hebrew label with color coding. Current stage is displayed prominently. Pipeline shows completed/current/future stages as colored badges.

**How stage advancement works from UI:**
User selects next stage from `AllowedNextStages` ComboBox → clicks "▶ קדם" → `WorkflowInstanceViewModel.AdvanceStageAsync()` → `WorkflowTaskOrchestrator.AdvanceWithTasksAsync()` → advances AND creates next-stage tasks.

**Lifecycle controls:**
- Pause: `WorkflowEngine.PauseAsync()` — sets Status=Paused.
- Resume: `WorkflowEngine.ResumeAsync()` — sets Status=Active.
- Complete: `WorkflowEngine.CompleteAsync()` — sets Status=Completed.
- Cancel: `WorkflowEngine.CancelAsync()` — sets Status=Cancelled.

### 3.7. FloatingProjectTasksView

**Files:**
- XAML: `SiNetProjectManagerV2\WPFUserControl\FloatingProjectTasksView.xaml` (~56KB)
- Code-behind: `FloatingProjectTasksView.xaml.cs` (~40KB)
- ViewModel: `SiNetSQL\MVVM\FloatingProjectTasksViewModel.cs` (1063 lines)

**What it does:**
Project-centric floating task window. Shows all tasks for the active project — both workflow-created and standalone.

**How workflow tasks differ from standalone tasks:**
- `IsWorkflowTask` property: Returns true when `SelectedTask` has both workflow AND source `TaskLink`s (checks `HasWorkflowAndSourceLinks(id)`).
- Workflow tasks show an action button with task-type-specific Hebrew labels (e.g., "📂 פתח פרויקט בדיקה" for OpenReviewProject, "✅ בדוק שלמות חומר" for CheckQuoteMaterialCompleteness).
- No explicit "workflow badge" — differentiation is through action button presence and `TaskLink` data.

**Task row visual states:**
- Active (default style)
- Waiting (yellow, italic — `IsOpen=true, IsActionable=false`)
- Closed (gray)

**How workflow tasks are linked back to WorkflowInstance:**
Via `TaskLink` entities:
- `TaskLink(LinkedEntityType=WorkflowInstance, Role=Trigger)` — links task to its parent workflow.
- `TaskLink(LinkedEntityType=*, Role=Source)` — links task to its work target (project, email, report).

**Dependencies:**
`WorkflowTaskOrchestrator`, `ITaskCompletionCoordinator`, `TaskStatusService`, `TaskNavigationResolver`, `TaskWorkflowResolver`.

### 3.8. TaskPanelView

**Files:**
- XAML: `SiNetProjectManagerV2\WPFUserControl\TaskPanelView.xaml` (593 lines)
- Code-behind: `TaskPanelView.xaml.cs`
- ViewModel: `SiNetSQL\MVVM\TaskPanelViewModel.cs` (1364 lines)

**What it does:**
Employee-centric task management panel. Primary entity is Employee — shows their work queue.

**Key features:**
- Employee selector ComboBox with "Include closed tasks" and "Full notes" toggles.
- Create task bar: Project selector, TaskType, Status, DueDate, Create button.
- Task DataGrid: WorkPriority (drag-reorder), TaskType, Title, Status (inline edit), Project (inline searchable), DueDate, Created, LastTaskResult, Notes preview.
- Row details: Open Action button (workflow tasks), Event History, Add Note, TaskLinks display.
- Task row visual states: Active / Waiting (yellow) / Closed (gray).

> **Planned (design):** The single work queue is proposed to split into three personal task-size buckets (Quick / Medium / Long) shown as tabs or a filter, each ordered by `WorkPriority` within `AssignedToId + WorkQueueBucket`. See `Docs/Domains/ProjectWork/PersonalWorkQueuesByTaskSize-2026-06-23.md`.

**Dependencies:** `ITaskCompletionCoordinator`, `TaskStatusService`.

---

## 4. Key workflow services used by the UI

### 4.1. WorkflowTaskOrchestrator (`SiNetSQL\Services\Workflow\WorkflowTaskOrchestrator.cs`, 848 lines)

The central orchestrator bridging workflows and tasks:

| Method | Purpose | Called by UI |
|---|---|---|
| `StartWorkflowAsync()` | Preflight → `WorkflowEngine.StartAsync()` → `EnsureInitialStageTasksAsync()` | Dashboard, ManagementWindow, CreateProjectWindow |
| `AdvanceWithTasksAsync()` | Preflight → `AdvanceStageAsync()` → `CreateStageTasksAsync()` | WorkflowInstanceWindow "Advance" |
| `CheckAndAutoAdvanceAsync(taskId)` | Evaluates auto-advance triggers after task completion | `ITaskCompletionCoordinator` |
| `CheckAndAutoAdvanceStalledWorkflowAsync()` | Watchdog for stalled workflows | Background job |
| `IsStageCompleteAsync()` | Checks all required tasks are closed | Auto-advance logic |

### 4.2. WorkflowEngine (`SiNetSQL\Services\Workflow\WorkflowEngine.cs`, 272 lines)

Pure lifecycle management:

| Method | Purpose |
|---|---|
| `StartAsync(initialStageCode?)` | Creates instance, sets initial stage |
| `AdvanceStageAsync()` | Validates transition rule, moves to next stage, auto-completes if IsFinal |
| `PauseAsync()` | Sets Status=Paused |
| `ResumeAsync()` | Sets Status=Active |
| `CompleteAsync()` | Sets Status=Completed, CompletedAtUtc |
| `CancelAsync()` | Sets Status=Cancelled |

### 4.3. WorkflowStageTaskProvisioningService (`SiNetSQL\Services\Workflow\WorkflowStageTaskProvisioningService.cs`, 524 lines)

Task creation from workflow stage templates:

| Method | Purpose |
|---|---|
| `EnsureInitialStageTasksAsync()` | Walks past Start node, provisions tasks. For SubWorkflow nodes: auto-starts child |
| `AutoAdvancePastStartNodeAsync()` | If current stage is Start node, advances via first outgoing transition |
| `CreateStageTasksAsync()` | Reads WorkflowStageTask templates → resolves assignees (template or stage group) → `TaskFactory.CreateAsync()` → `TaskLink` creation (Trigger role to WorkflowInstance, Source role for back-links) |

### 4.4. TaskFactory (`SiNetSQL\Services\Tasks\TaskFactory.cs`, 118 lines)

Centralized task creation: auto-priority via `TaskPriorityEngine`, `TaskLink` creation, audit event recording.

### 4.5. ITaskCompletionCoordinator (`SiNetSQL\Services\Tasks\`)

Task completion → workflow advancement:
1. Validates completion event against `TaskBehaviorDefinition`.
2. Validates task result against interaction registry.
3. Marks work targets.
4. Records events, updates `LastTaskResultId`.
5. Optionally closes task.
6. Signals `WorkflowTaskOrchestrator.CheckAndAutoAdvanceAsync(taskId)`.

### 4.6. WorkflowQueryService (`SiNetSQL\Services\Workflow\WorkflowQueryService.cs`, 291 lines)

| Method | Used by |
|---|---|
| `GetActiveDefinitionsAsync()` | Designer, Builder |
| `GetByProjectAsync()` | Dashboard |
| `GetActiveByProjectAsync()` | Project task views |
| `GetInstanceDetailAsync()` | InstanceWindow |
| `GetAllowedNextStagesAsync()` | InstanceWindow advance ComboBox |
| `GetAllProjectWorkflowSnapshotsAsync()` | Dashboard (project-centric) |
| `GetAllWorkflowInstanceSnapshotsAsync()` | Monitor (instance-centric) |

---

## 5. Current behavior — key workflows

### 5.1. How a workflow is started

Three paths:
1. **Manual from Dashboard:** User selects project + definition → "▶ הפעל תהליך" → `WorkflowTaskOrchestrator.StartWorkflowAsync(triggerType: Manual)`.
2. **From WorkflowManagementWindow Dashboard tab:** Same as above (duplicated inline).
3. **Auto on project creation:** `WorkflowCreateProjectWindow` → on project created → `StartWorkflowAsync(triggerType: Email)`.

### 5.2. How task results advance the workflow

Flow: Task status change → `ITaskCompletionCoordinator.CompleteAsync()` → records result → `WorkflowTaskOrchestrator.CheckAndAutoAdvanceAsync(taskId)` → evaluates transitions (`AllRequiredTasksClosed` / `TaskStatusChanged` triggers) → for Auto: `ExecuteTransitionAsync()` → `AdvanceWithTasksAsync()` → creates new stage tasks.

### 5.3. How workflow-created tasks differ from legacy standalone tasks

| Aspect | Workflow-created tasks | Legacy standalone tasks |
|---|---|---|
| Created by | `WorkflowStageTaskProvisioningService.CreateStageTasksAsync()` → `TaskFactory` | Inline `new ProjectAssignment` + `TaskPriorityEngine` |
| Linked to workflow | Yes — `TaskLink(WorkflowInstance, Trigger)` | No |
| Has work target link | Yes — `TaskLink(*, Source)` | No |
| Advances workflow on completion | Yes — via `ITaskCompletionCoordinator` → `CheckAndAutoAdvanceAsync` | No |
| Visual differentiation | Action button with type-specific label | No action button |
| Status managed by | `TaskStatusService` with `TaskBehaviorDefinition` | Direct status field update |

### 5.4. How workflow dashboard queries data

`WorkflowDashboardViewModel.LoadInstancesAsync()` → `WorkflowQueryService.GetByProjectAsync(projectId)` → returns all instances with includes (`WorkflowDefinition`, `CurrentStage`). Client-side filtered by active-only toggle and selected definition.

### 5.5. Whether users can edit workflow definitions from UI

**Yes.** The `WorkflowDesignerView` (Tab 2 of WorkflowManagementWindow) provides a full visual editor. Users can add/remove nodes, create connections, edit properties, add stage tasks, and save changes to DB. This is an **admin-only** screen.

---

## 6. Relationship to Review Workflow and Google Sheet Review Migration

### 6.1. How Workflow UI relates to Review Workflow

The Review Workflow (`REV.*`) is a seeded workflow definition that follows the same lifecycle as any other workflow:
- Visible in Dashboard and Monitor.
- Instances can be viewed in `WorkflowInstanceWindow`.
- Stage tasks are provisioned by `WorkflowStageTaskProvisioningService`.
- Tasks appear in `FloatingProjectTasksView` and `TaskPanelView`.
- Review workflow is started from `WorkflowCreateProjectWindow` at `REV.MaterialIntake` when creating a project from email.

### 6.2. How Workflow UI relates to the migration design

The migration design (`Docs/Domains/Migration/GoogleSheetReviewMigrationDesign-2026-06-21.md`) reuses these UI/service mechanisms:

| Migration step | Existing mechanism |
|---|---|
| Start workflow at arbitrary stage | `WorkflowTaskOrchestrator.StartWorkflowAsync(initialStageCode)` |
| Complete workflow for final statuses | `WorkflowEngine.CompleteAsync()` |
| Provision stage tasks | `WorkflowStageTaskProvisioningService.CreateStageTasksAsync()` |
| Task reassignment | `TaskService.ReassignTask()` (active, with same-group constraint) |
| Workflow lookup for existing instances | `WorkflowQueryService.GetByProjectAsync()` |
| Advance existing workflow forward | `WorkflowTaskOrchestrator.AdvanceWithTasksAsync()` |

### 6.3. Current gaps before migration implementation

| # | Gap | Severity |
|---|---|---|
| 1 | No task reassignment UI in FloatingProjectTasksView or TaskPanelView | LOW — `TaskService.ReassignTask()` exists, UI is in `TaskViewModelBase.ReassignSelectedTaskCommand` |
| 2 | `StartAsync` does not auto-complete for IsFinal stages | LOW — migration calls `CompleteAsync` separately |
| 3 | No direct report-to-workflow link in UI | LOW — indirect via shared ProjectId |
| 4 | WorkflowDashboardView duplicated inline in WorkflowManagementWindow | LOW — architectural duplication |
| 5 | WorkflowManagementWindow is 3548 lines of code-behind with no ViewModel | MEDIUM — active legacy, candidate for refactoring |

---

## 7. Existing mechanisms to reuse

Before adding any new workflow UI mechanism:

| Mechanism | Service/Screen | Purpose |
|---|---|---|
| Workflow start | `WorkflowTaskOrchestrator.StartWorkflowAsync()` | Start workflows with `initialStageCode` support |
| Workflow advance | `WorkflowTaskOrchestrator.AdvanceWithTasksAsync()` | Advance stage with task provisioning |
| Workflow lifecycle | `WorkflowEngine` (Pause/Resume/Complete/Cancel) | Manage workflow state |
| Task provisioning | `WorkflowStageTaskProvisioningService.CreateStageTasksAsync()` | Create tasks from stage templates |
| Task creation | `TaskFactory.CreateAsync()` | Centralized task creation with auto-priority |
| Task completion | `ITaskCompletionCoordinator.CompleteAsync()` | Complete task → auto-advance workflow |
| Task reassignment | `TaskService.ReassignTask()` | Reassign with audit |
| Workflow query | `WorkflowQueryService` | All query patterns |
| Policy check | `ProjectWorkflowPolicyService.GetAllowedWorkflowsAsync()` | Workflow-to-ProjectType mapping |
| Instance detail view | `WorkflowInstanceWindow` | View/advance/manage single instance |
| System monitor | `WorkflowStatusMonitorWindow` | Real-time monitoring |
| Dashboard | `WorkflowDashboardView` | Project-scoped overview |

---

## 8. Active legacy behavior

| Mechanism | Status | Notes |
|---|---|---|
| WorkflowManagementWindow code-behind (3548 lines) | **Active legacy** | No ViewModel. Candidate for future MVVM refactoring. |
| Dashboard duplication (standalone UserControl + inline in Management Window) | **Active legacy** | Both are functional. Candidate for unification. |
| Old standalone ProjectAssignment creation | **Active legacy** | Remains active alongside workflow-created tasks. Not deleted. See `TaskingWorkflowCoexistence-2026-06-19.md`. |
| `TaskBehaviorDefinitions` tab in Management Window | **Active** | Defines task completion triggers and behaviors. |

---

## 9. Known gaps

| # | Gap | Severity |
|---|---|---|
| 1 | WorkflowManagementWindow is 3548 lines of code-behind | MEDIUM — large, no ViewModel, hard to test |
| 2 | Dashboard view duplicated inline | LOW — both work, but maintenance overhead |
| 3 | No ViewModel for Builder, Policy, Behavior tabs | MEDIUM — all logic in code-behind |
| 4 | Task reassignment not surfaced prominently in workflow task views | LOW — command exists in `TaskViewModelBase` but not always visible |
| 5 | No "migration mode" in Dashboard or Monitor | LOW — migration uses services directly, not UI |
| 6 | Auto-advance confirmation UI not clearly documented | LOW — AutoWithConfirm transitions return ConfirmationRequired status |

---

## 10. Out of Scope

- Implementing migration code.
- Refactoring WorkflowManagementWindow to MVVM.
- Adding new workflow stages.
- Changing task provisioning behavior.
- Adding monitoring for migration progress.

---

## 11. Dropped / cancelled / postponed

| Item | Status |
|---|---|
| Creating a parallel workflow task mechanism | **Cancelled** |
| Deleting old standalone task path | **Not approved** — remains active legacy |
| Automatically moving workflows backwards | **Not approved** |
| Adding new workflow stage for police-comments review | **Not approved** — existing stage used with documented mismatch |
| Refactoring WorkflowManagementWindow to MVVM | **Postponed** |
| Unifying duplicated dashboard views | **Postponed** |
| Code changes | **Not approved** |
| DB changes | **Not approved** |

---

## 12. No-code-change confirmation

- **No code was changed.**
- **No DB was changed.**
- **No Google Sheet was changed.**
- **No data was imported.**
- **No reports, tasks, workflows, or TaskLinks were created.**
- **No old mechanisms were deleted or disabled.**
