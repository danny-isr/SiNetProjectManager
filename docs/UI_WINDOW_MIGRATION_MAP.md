# UI Window Migration Map

> **Purpose** - Inventory of the existing (legacy) WPF windows and the plan to recreate each one as a
> clean **visual clone** under `src/SiNet.App.Wpf/Surfaces/...`, then reconnect behavior to clean
> Application services **one action at a time**.
>
> **Guiding principle (visual-clone first):** preserve the *existing visible windows* - same layout,
> same RTL/Hebrew UX, same buttons, same panels, same flow - while stripping hidden legacy logic from
> behind them. We do **not** redesign the UX and we do **not** change DB / schema / migrations /
> `ModelSnapshot` / EF mappings as part of this work.
>
> Related: `docs/MIGRATION_MAP.md` (workflow-first process model), `docs/ARCHITECTURE_TARGET.md`,
> [`docs/PROJECTS.md`](./PROJECTS.md) (Project domain / Project Context target state),
> [`docs/WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md) (canonical
> Task → WorkSurface → Completion contract; window readiness map).

## Status legend

| Symbol | Meaning |
| --- | --- |
| Not started | No work yet |
| Started / partial | Visual shell exists, logic not connected |
| Reconnected | Visual parity reached, behavior reconnected to clean services |
| Frozen | Developer harness only, NOT a product screen |

## Developer harness vs. visual-clone target

- **`src/SiNet.App.Wpf/Inspection/InspectionShellView(.xaml/.cs)` + `InspectionShellViewModel` - Frozen (harness only).**
  This is a developer harness for exercising the workflow task-completion seam. It is **not** the
  target Inspection UX and must **not** be expanded into the product screen or replace
  `FloatingInspectionView`.
- **`src/SiNet.App.Wpf/Surfaces/Inspection/InspectionWindowView` - the visual-clone target.**
  This is the clean visual clone of the legacy `FloatingInspectionView` and is where Inspection UI
  work continues.
- **`src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView` - the Email visual-clone target.**
  This is the clean visual clone of the legacy `EmailManagementView`. The old `EmailManagementView`
  remains the **visual reference / legacy source** and must **not** be modified as part of clone work.

## Inventory of major legacy windows

| Window (legacy) | Path | Purpose | Visual areas | Logic to extract (heaviest first) |
| --- | --- | --- | --- | --- |
| FloatingInspectionView | SiNetProjectManagerV2/WPFUserControl/FloatingInspectionView.xaml | Floating/dockable inspection-report workspace (RTL) | Header chrome (pin/dock/refresh/collapse/close), create-report strip, locked-report action row, metadata + reviewed-plan row, questionnaire tree, drawings section, report cards list, status bar | FloatingInspectionViewModel (~4.5k lines): report CRUD, report generation/export, Gmail/planner response pull, ACC/file actions, drawing stamping |
| EmailManagementView | SiNetProjectManagerV2/WPFUserControl/EmailManagementView.xaml | Inbox / email triage and move-to-project | Mail list, attachments, project mapping, actions | EmailManagementViewModel (~6k lines): Gmail, ACC inbox, move-to-project |
| ProjectWorkView | SiNetProjectManagerV2/WPFUserControl/ProjectWorkView.xaml | Project work window (files/alternatives/versions) | File tree, alternatives, versions, actions | ProjectWorkViewModel (~2.2k lines): ACC files, project file instances |
| TaskPanelView | SiNetProjectManagerV2/WPFUserControl/TaskPanelView.xaml | Task panel / queue | Task list, status, actions | TaskPanelViewModel (~1.2k lines): workflow tasks, completion |
| FloatingProjectTasksView | SiNetProjectManagerV2/WPFUserControl/FloatingProjectTasksView.xaml | Floating per-project task list | Floating chrome, task list | FloatingProjectTasksViewModel (~0.9k lines) |
| WorkflowManagementWindow | SiNetProjectManagerV2/Dialogs/WorkflowManagementWindow.xaml | Workflow administration dialog | Workflow definitions, stages | Workflow admin services |

## Migration map (target surfaces)

| Legacy window | Target surface (new) | Status |
| --- | --- | --- |
| FloatingInspectionView | src/SiNet.App.Wpf/Surfaces/Inspection/InspectionWindowView | Partial structural parity |
| EmailManagementView | src/SiNet.App.Wpf/Surfaces/Email/EmailWindowView | Started / partial (read-only production pilot polish) |
| ProjectWorkView | src/SiNet.App.Wpf/Surfaces/ProjectWork/... (TBD) | Not started |
| TaskPanelView | src/SiNet.App.Wpf/Surfaces/Tasks/... (TBD) | Not started |
| FloatingProjectTasksView | src/SiNet.App.Wpf/Surfaces/Tasks/... (TBD) | Not started |
| WorkflowManagementWindow | src/SiNet.App.Wpf/Surfaces/Workflow/... (TBD) | Not started |

## First target - FloatingInspectionView

Chosen first because it is self-contained (a single floating window) and the Inspection workflow
seam is already proven by prior slices. The visual clone lives at
`src/SiNet.App.Wpf/Surfaces/Inspection/`.

### Slice 1 - initial visual clone (done)

| Aspect | Status | Notes |
| --- | --- | --- |
| FloatingInspectionView visual clone | Done | InspectionWindowView(.xaml/.cs) + InspectionWindowViewModel created. Borderless RTL floating window; header (pin/dock/refresh/collapse/close), create-report strip, report action row (mark received / re-pull / open source / unlock / share / select reviewed plan), metadata row, notes area, report cards list, status bar. |
| Logic extraction | Not connected yet | No report CRUD/generation/export, no Gmail/planner pull, no ACC/file actions, no drawing stamping. Commands are stubbed ("not wired yet"). |
| Real data | Not connected yet | Fake/design-time data only (InspectionWindowDesignData): sample templates, reports, notes. No DB, no IInspectionReportService, no IInspectionWorkspace. |
| Workflow / Task integration | Pending | InspectionWindowView.ApplyContext(WorkSurfaceContext?) placeholder exists so the window can later be opened from a task; no task opening or workflow mutation implemented. |

### Slice 2 - questionnaire tree structural parity (current)

| Aspect | Status | Notes |
| --- | --- | --- |
| FloatingInspectionView visual clone | Partial structural parity | Flat notes ListBox replaced with a hierarchical TreeView (Chapter -> Section -> Note) matching the legacy InspectionTree shape (ChapterTreeItem -> SectionTreeItem -> NoteTreeItem). RTL/Hebrew, terminology, and card-like density preserved. |
| Questionnaire tree | Visual / fake-data implemented | TreeView with 3 HierarchicalDataTemplate/DataTemplate levels. Note rows show reorder arrows, linked-file + screenshot buttons, index label, planner-response indicator, status combo, and note text. Fake data via InspectionWindowDesignData.BuildSampleTree(). |
| Real data | Not connected | Tree is built from in-memory fake records only. No DB, no IInspectionReportService, no IInspectionWorkspace, no planner-response pull. |
| Actions | Stubbed only | Add note, move up/down, screenshot, open linked file, status select -> all stubbed (set StatusMessage to the "not wired yet" text or no-op). No file/ACC/Gmail/workflow side effects. |
| Workflow / Task integration | Pending | Unchanged from Slice 1; ApplyContext placeholder remains, no task opening. |

### Visual gaps deferred for later slices

- Questionnaire **TreeView** hierarchy is now implemented (Chapter -> Section -> Note) with status
  combo, planner-response indicator, and linked-file/screenshot buttons. Still deferred within the
  tree: the rich-text note editor (currently a wrapped TextBlock), the section/note sync-state badges
  (missing/mismatch/new from template), the General-Data chapter (Chapter 0), attachment count tooltips,
  and the manager-review export-blocking highlight.
- Drawings section (add/list/remove + "stamp all").
- Per-report-card action buttons (open template, export, send email, delete) shown on selection.
- [DEBUG] fill button, help (?) buttons, "reset size" chrome, and the work-window/file-service
  warning banners.
- Inspector/admin pickers, series picker, manual template URL, reviewed-version textbox, and the
  shared application font-size dynamic resources.

## Email window - EmailManagementView

Second target chosen after Inspection. The visual clone lives at `src/SiNet.App.Wpf/Surfaces/Email/`.
The old `EmailManagementView` is the **visual reference / legacy source** only; it is not modified.

### Slice 1 - initial visual clone (done)

| Aspect | Status | Notes |
| --- | --- | --- |
| EmailManagementView visual clone | Done | EmailWindowView(.xaml/.cs) + EmailWindowViewModel + EmailWindowDesignData created. Borderless RTL window mirroring the 3-row source: top status + project/Gmail filters strip, selected-project info strip, and the 3-pane content area (email list on the right, viewer in the center, context/calendar placeholder on the left), plus a bottom status bar. |
| Real email data | Connected (read-only) | `EmailWindowViewModel` now loads real Gmail summaries, full plain-text body, and attachment metadata through `IEmailGateway` and `IConnectorAuthService`, using the selected project's canonical Gmail label leaf. No DB write path, no Outlook path, no ACC inbox coupling, and no file-system side effects. |
| Actions | Mixed | `Connect`, `Refresh`, `Search`, `ClearSearch`, and selected-email detail loading are real read-only actions. Write/workflow UI hidden via `ShowDeferredWriteActions`. Visual placeholders (pagination, calendar, help, date pickers) hidden via `ShowDeferredVisualPlaceholders`. `LinkToProject`, `CreateTaskFromEmail`, `MarkHandled`, `Archive`, `Reply`, `Forward`, `OpenAttachment`, and `CompleteTask` remain explicitly deferred/stubbed. |
| Production pilot polish | Done | Misleading placeholders hidden/disabled; production notice copy; Hebrew empty state; unread badge when count > 0. No pagination/date/calendar/help real integration. |
| Workflow / Task integration | Pending | EmailWindowView.ApplyContext(WorkSurfaceContext?) placeholder exists so the window can later be opened from a task; no task opening, no task completion, and no workflow mutation implemented. |

### Email visual gaps deferred for later slices

- The legacy screen is highly decomposed into custom controls (EmailListControl, EmailViewerControl,
  EmailActionBarControl, EmailTopStatusBarControl, EmailPaginationControl, SearchableProjectSelector,
  EmailContextPanel). The clone reproduces the visual *shape* and terminology inline, not those exact
  controls.
- WebView2 calendar sidebar (real embedded calendar) - shown as a static context/calendar placeholder.
- EmailContextPanel workflow visuals (HasContext / ProjectDisplay / SuggestedActions / ExecuteAction) -
  shown as stubbed suggested-action buttons only.
- Grouped-by-project expanders with assigned/unassigned header coloring and the per-row context menu
  (file / unfile / mark pending / mark personal / mark irrelevant).
- Real pagination, unread counts, Job Type / Status / User project filters bound to live data.
- Move-to-project block-reason warnings and "all attachments placed" state.
- Attachment open/download behavior, Gmail modify/labels, and any send/reply/forward behavior.

> **Cross-application note:** the project selector + Job Type / Status / User filters and the
> "selected project" concept are **not** Email-only. They are shared across Email, ProjectWork,
> Tasks, Workflow and the MainWindow title. Their target design (shared `ProjectSelectorView`,
> `IProjectQueryService`, `ICurrentProjectContext`) is tracked separately in
> [`PROJECT_CONTEXT_MIGRATION.md`](./PROJECT_CONTEXT_MIGRATION.md). The Email clone should consume
> that shared mechanism rather than growing its own local project-selection state.

## DB / schema confirmation

This work changes **UI only**. No migrations, no `ModelSnapshot`, no `*.Designer.cs`, no `DbContext` /
`DbSet` changes, and no schema changes were made.

## Workflow / Task / Action integration (readiness)

Task-driven opens, completion, and ComponentKey routing are defined in
[`WORK_SURFACE_WORKFLOW_INTEGRATION.md`](./WORK_SURFACE_WORKFLOW_INTEGRATION.md). Summary:

- **Do not** add a new task-window router — reuse `TaskNavigationResolver` /
  `ITaskNavigationService`.
- **Do not** complete tasks or mutate workflow from ViewModels — use
  `ITaskCompletionCoordinator`.
- Native surfaces expose `ApplyContext(WorkSurfaceContext?)` placeholders; only
  `InspectionShellViewModel.OpenFromTaskAsync` implements the canonical navigation half today.
- See the integration doc §5 for per-window readiness (Email, Inspection, ProjectWork, Tasks,
  Workflow, ACC operator surfaces).
- Production pilot envelope: [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md).
