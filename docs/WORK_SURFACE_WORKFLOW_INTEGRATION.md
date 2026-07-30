# Work Surface / Workflow / Task / Action Integration Contract

> **Status:** Readiness documentation (2026-07-04) — defines how New System and migrated windows
> connect to the existing process backbone **without** adding a parallel router, fallback, or schema
> changes.
>
> **Scope:** Integration contract + window readiness map only. **No** broad legacy window migration,
> **no** new features, **no** GmailSend, **no** Drive/Sheets, **no** ACC write.
>
> Related:
> [`MIGRATION_MAP.md`](./MIGRATION_MAP.md) (workflow-first backbone),
> [`PROCESS_BACKBONE_FOUNDATION.md`](./PROCESS_BACKBONE_FOUNDATION.md) (readiness levels + matrix),
> [`UI_WINDOW_MIGRATION_MAP.md`](./UI_WINDOW_MIGRATION_MAP.md) (visual-clone inventory),
> [`APP_SHELL.md`](./APP_SHELL.md) §10 (shell is not a workflow actor),
> [`PROJECTS.md`](./PROJECTS.md) §7 (`WorkSurfaceContext` vs Current Project),
> [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md) (G-Startup closed; G-Policy pending),
> [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md) (ACC read/operator only).

---

## 1. Role model (who owns what)

| Concept | Owner | Responsibility |
| --- | --- | --- |
| **Workflow** | `IWorkflowCommandService` / `WorkflowTaskOrchestrator` | Process backbone: stages, instances, auto-advance. **Not** mutated from WPF ViewModels. |
| **Task** | `ProjectAssignment` + `TaskType` + `TaskLink` (DB); orchestration via `WorkflowTaskOrchestrator` / `SmartTaskService` | Work item assigned to a user/group with typed interaction and work targets. |
| **Action / handler** | `IProcessActionService` (New System) / legacy `IProcessActionDispatcher` | Executes a business action when context permits. New surfaces use the Application port. |
| **Work Surface** | `src/SiNet.App.Wpf` window/view + ViewModel | UI where the user performs work. Receives **`WorkSurfaceContext`**; loads data from context only in task mode. |
| **Completion bridge** | `ITaskCompletionService` (`SqlTaskCompletionService` in New System) | Single decision point for business completion: validates event/result, updates task, may close task, routes workflow auto-advance via `IWorkflowCommandService`. Legacy `TaskCompletionCoordinator` remains in SiNetSQL for legacy UI only. |

**Hard rules:**

- ViewModels **must not** change `WorkflowStage` directly.
- ViewModels **must not** change `ProjectStatus` directly (coordinator may do so per policy).
- ViewModels **must not** close a task directly for business completion — use `ITaskCompletionService` (native in New System via `AddSiNetProcessBackbone()`).
- ViewModels **must not** call `IWorkflowCommandService.CheckAndAutoAdvanceAsync` directly — the coordinator owns that bridge.

---

## 2. Canonical open path (task-driven)

Use **existing** mechanisms only. **Do not** add a new task-window router.

```plaintext
TaskPanel / FloatingProjectTasksView / Workflow UI
  → user selects taskId
  → ITaskNavigationService.ResolveAsync(taskId)          [New System — target path]
        → SqlTaskNavigationService (Infrastructure.Sql)
        → WorkSurfaceContext
  → shell maps ComponentKey → concrete Work Surface
  → WorkSurface.ApplyContext(context) OR OpenFromTaskAsync(taskId) [Inspection harness]
  → Work Surface loads exact PrimaryWorkTargetEntityId (no first/last/default fallback)
  → user performs business work via IProcessActionService / existing handlers (migration in progress)
  → ITaskCompletionService.CompleteAsync(...)
  → SqlTaskCompletionService → IWorkflowCommandService (auto-advance when policy requires)
```

**Legacy path (reference only — do not build new Work Surfaces on this):**

```plaintext
  → TaskNavigationResolver.ResolveAsync(taskId)            [legacy UI today]
  → ILegacyTaskNavigationSource → LegacyTaskNavigationService   [temporary bridge — candidate for removal]
  → TaskCompletionCoordinator via ILegacyTaskCompletionSource   [temporary bridge — candidate for removal]
```

### Existing code map

| Step | Existing mechanism | Location |
| --- | --- | --- |
| Task → navigation request | `SqlTaskNavigationService` | `src/SiNet.Infrastructure.Sql/Services/Tasks/SqlTaskNavigationService.cs` |
| TaskType → UI contract | `ReviewTaskInteractionRegistry` + `TaskInteractionDefinition` | `src/SiNet.Infrastructure.Sql/Services/Tasks/` (migrated from SiNetSQL) |
| Stable screen keys | `TaskComponentKeys` | `src/SiNet.Infrastructure.Sql/Services/Tasks/TaskInteractionDefinition.cs` |
| Work targets | `TaskLink` rows on `ProjectAssignment` | DB (read by navigation service) |
| New System port | `ITaskNavigationService` | `src/SiNet.Application/Tasks/ITaskNavigationService.cs` |
| DI registration | `AddSiNetProcessBackbone()` | `src/SiNet.Infrastructure.Sql/ProcessBackboneServiceCollectionExtensions.cs` |
| Temporary bridge (not target) | `LegacyTaskNavigationService` | `src/SiNet.LegacyBridge/Tasks/` — **not registered** in `AddSiNet()` |
| Legacy resolver (legacy UI only) | `TaskNavigationResolver` | `SiNetSQL/Services/Tasks/TaskNavigationResolver.cs` |
| Runtime context DTO | `WorkSurfaceContext` | `src/SiNet.Application/WorkSurfaces/WorkSurfaceContext.cs` |
| Legacy shell routing | `FloatingProjectTasksView.OnOpenTaskNavigationRequested` | `SiNetProjectManagerV2/WPFUserControl/FloatingProjectTasksView.xaml.cs` |
| New System actions port | `IProcessActionService` | `src/SiNet.Application/Actions/IProcessActionService.cs` |
| Legacy business actions | `IProcessActionDispatcher` | SiNetSQL — handlers migrate one-by-one |
| Task creation (workflow) | `WorkflowTaskOrchestrator`, `TaskFactory` | `SiNetSQL/Services/Workflow/` (still legacy) |
| Completion (New System) | `ITaskCompletionService` / `SqlTaskCompletionService` | `src/SiNet.Infrastructure.Sql/Services/Tasks/SqlTaskCompletionService.cs` |
| Completion (legacy UI) | `TaskCompletionCoordinator` | `SiNetSQL/Services/Tasks/TaskCompletionCoordinator.cs` |
| Inspection pilot (New System) | `InspectionShellViewModel.OpenFromTaskAsync` | `src/SiNet.App.Wpf/Inspection/InspectionShellViewModel.cs` |

**Resolver output fields** (via `TaskNavigationRequest` → `WorkSurfaceContext`):

- `ComponentKey` (from `TaskInteractionDefinition`)
- `ProjectId`
- `TaskId`
- `WorkflowInstanceId`
- `PrimaryWorkTargetEntityId`
- `AllowedResultCodes` / `AllowedTaskResultCodes`
- `CompletionEventCode`, `ActingUserId`, `TaskTypeCode` (when host supplies them)

---

## 3. Two open modes

### 3.1 Project-centric (normal)

- Window opened from shell menu, factory, or user navigation.
- **No** `TaskId` in context.
- **No** automatic task completion.
- Data driven by `ICurrentProjectContext` / user project selection where applicable.
- If the surface is a **singleton**, clear any previous task context before reuse (`ApplyContext(null)` or equivalent).

### 3.2 Task-driven

- Window opened because the user activated a task.
- **Must** receive `WorkSurfaceContext` (via resolver — never hand-built in UI).
- **Must** have `ComponentKey` matching the surface.
- **Must** have a concrete work target when the task type requires one (`PrimaryWorkTargetEntityId`, or validated `TaskLink`).
- If resolver fails, interaction definition is missing, or work target is absent → **clear error + logging**. **No** first/last/default entity fallback.
- Completion (when implemented) goes through `ITaskCompletionCoordinator` with a result from `AllowedResultCodes`.

---

## 4. Completion path (back to Workflow)

```plaintext
Work Surface (user confirms business outcome)
  → ITaskCompletionCoordinator.CompleteAsync(
        taskId,
        completionEventCode,
        taskResultCode,          // must ∈ AllowedResultCodes when required
        completedTaskLinkIds,
        payload,
        userId)
  → TaskCompletionCoordinator validates against ReviewTaskInteractionRegistry + ReviewCompletionEventBehavior
  → records ProjectAssignmentEvent, updates LastTaskResultId, may close task
  → IWorkflowCommandService.CheckAndAutoAdvanceAsync (when coordinator policy requires)
```

New System port (when seam bound): `ITaskCompletionService` → `LegacyTaskCompletionService` → host `ILegacyTaskCompletionSource`.

Until the completion seam is bound in a given host, surfaces report **Unavailable** — they must not invent a local completion path.

---

## 5. Window readiness map

Legend: **Connect now** = safe to wire task context using existing ports (read-only or pilot). **Deferred** = do not connect until named policy/slice closes.

| Surface | Opens today | ApplyContext / OpenForTask | Expected ComponentKey(s) | Work target | Can complete task? | Completion event/result | Read/Write | Gmail / ACC deps | Connect now? |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **EmailWindowView** | New Shell menu (factory); legacy `MainWindow.NavigateToEmail` for filing | `ApplyContext` loads optional project filter + selects inbox target on current Gmail page | `Component.EmailFiling`, `Component.EmailComposeToPlanner`, `Component.ProjectCreationFromEmail`, `Component.ReviewProjectSetupFromEmail` | `EmailInboxMessage` / `EmailThread` id | Legacy: yes via coordinator after MoveToProject; New: **no** | e.g. `ReviewMaterialFiled` + filing results | Read native (V2: paged INBOX + link badges); write deferred | Gmail read native; send/modify legacy | **Pilot** — V2 lists all emails; filing write needs `IEmailFilingService`; no `CreateTaskFromEmail` / ProjectWork jump |
| **InspectionWindowView** (visual clone) | Not production entry; design data | `ApplyContext` placeholder | `Component.InspectionReport`, `Component.ManagerReviewApproval` | `InspectionReport` id | Stub only | Per registry (e.g. report received / approved) | Read stubs; write deferred | None in clone | **Deferred** — use `InspectionShellViewModel` harness for task pilot first |
| **InspectionShellView** (harness) | V2 admin preview `OpenInspectionFromTask_Click` | `OpenFromTaskAsync(taskId)` | `Component.InspectionReport` (via `WorkSurfaceComponentKeys`) | `InspectionReport` id | Partial (`ITaskCompletionService` when bound) | From context / metadata port | Read-only harness | None | **Pilot only** — navigation half |
| **ProjectWork** | Legacy `MainWindow.ShowProjectWork` | **Live** — `ProjectWorkWindowView` / `IProjectWorkSurfaceHost` (menu בעבודה 2 + task float) | `Component.ProjectWork`, `Component.MaterialChecklist`, `Component.PoliceSubmission` | Project / checklist / email source | Yes (result picker on task strip) | Workflow result codes per registry | Partial write | FileServer upload/DnD/delete; ACC write gated | **Parity gaps:** rename/delete user folders; from-template alternative. **Catalog admin** («ניהול קבצים» / create-edit catalog file defs) is a **separate** Admin surface — see [`FILE_CATALOG_ADMIN.md`](./FILE_CATALOG_ADMIN.md) (not ProjectWork). **Live:** context-menu «יצירת תיקייה» via `IProjectFolderWriteService` (DB + best-effort FileServer). Required catalog slot `QuoteEstimate` («אומדן הצעה» under folder «הצעת מחיר», JobType חומר כללי) is seeded idempotently (never deletes; re-parents folder under project root when needed). Calculation task gates completion until physical omdan exists. File visible in ProjectWork only when the project has that JobType in `TypeOfProjectInProject`. |
| **TaskPanelView** | Legacy embedded panel | N/A (orchestrator UI) | N/A (lists tasks) | N/A | Opens tasks via resolver | N/A | Mixed | None direct | **Next** — read-only native surface; consume bucket-aware `ITaskQueryService` / `ITaskQueueService` |
| **FloatingProjectTasksView** | Legacy floating per-project list | `OnOpenTaskNavigationRequested` | All `TaskComponentKeys.*` | Per resolver | Dispatches to hosts; completion in target surface | Per target surface | Mixed | Email/Inspection/ProjectWork | **Legacy reference** — New System must reuse resolver, not duplicate switch |
| **WorkflowDashboard / WorkflowInstance** | Legacy dialogs | N/A | N/A | N/A | Admin/operator views | N/A | Mixed | None direct | **Deferred** — admin surfaces not migrated |
| **AccControlPlaneStatusWindow** | New Shell + Settings ACC tab | No task integration | None (operator) | Optional item ref for browse | No | N/A | **Read-only** | ACC read/control-plane | **Connect now** (operator read) — not task-driven |
| **AccReadOnlyDocumentBrowser** (embedded) | ACC status window | No task integration | None | `AccItemRef` | No | N/A | Read-only | ACC read | **Connect now** (read browse) |

### Notes on legacy vs New System routing

- **Legacy production** task open: `FloatingProjectTasksView` → `TaskNavigationResolver` → `switch (ComponentKey)` → legacy windows (`EmailManagementView`, `FloatingInspectionView`, `WorkflowCreateProjectWindow`, etc.).
- **New System** must converge on: `ITaskNavigationService` → `WorkSurfaceContext` → `ApplyContext` / `OpenFromTaskAsync` on native surfaces — **same resolver contract**, different hosts.
- **No new router.** Extend `TaskNavigationResolver` / `ReviewTaskInteractionRegistry` when new task types appear; map `ComponentKey` in shell/factory only.

- **Task Panel read-only** must list **three personal bucket queues** (Quick / Medium / Long) via `ITaskQueryService.GetOpenTasksForUserByBucketAsync` — not one flat employee list. Queue mutations use `ITaskQueueService`. Design:
  [`PersonalWorkQueuesByTaskSize-2026-06-23.md`](../SiNetProjectManagerV2/Docs/Domains/ProjectWork/PersonalWorkQueuesByTaskSize-2026-06-23.md); implementation in `TaskQueuePriorityEngine` + `SqlTaskQueueService`.

---

## 6. Gmail / ACC integration guardrails (for window wiring)

| Area | Rule |
| --- | --- |
| Gmail read | Native: `IEmailGateway` + `IConnectorAuthService`. G-Startup silent restore in V2 New System startup. Email List V3 (`EmailListView`) is a **standalone read-only component** — account bar, paging, filters, Outlook cards; **no** task completion or link/unlink wiring. |

### Email List V3 — workflow gaps (read-only component)

The standalone `EmailListView` / `EmailListViewModel` is hosted by `EmailWindowView` for inbox browsing. It does **not** participate in workflow completion or task routing beyond optional `ApplyContext` correlation on the current page.

| Action | Status |
| --- | --- |
| Task open → pre-filter inbox | Partial — `EmailWindowViewModel.ApplyContext` sets optional project filter + selects matching row on **current page only** |
| Unlinked mail → triage task | **Not wired** |
| Linked mail → ProjectWork | **Not wired** |
| File / unlink mail | **Deferred** — `IEmailFilingService` design only |
| Cross-page task mail selection | **Not supported** — current Gmail page only |
| Gmail send / modify / delete | **Deferred** — G-Policy |

See [`EMAIL_LIST_MIGRATION.md`](./EMAIL_LIST_MIGRATION.md) for component scope and deferred items.
| Gmail send/modify | **Deferred** — requires G-Policy. No `IEmailSender` in WPF until approved. |
| Drive / Sheets | **Legacy/deferred** — not part of Work Surface integration yet. |
| ACC read | Native ports (`IAccDocumentService`, browse, reconciliation). |
| ACC write/upload/provisioning | **Deferred** — server-only / ACC-Write-Policy. No upload/provisioning ports from New System WPF. |

---

## 7. Pilot recommendation (next slice, not this task)

1. Align `InspectionShellViewModel.InspectionComponentKey` with `TaskComponentKeys.InspectionReport` (or map in adapter).
2. Bind `ILegacyTaskCompletionSource` in V2 for one completion path from Inspection harness.
3. Add New Shell entry: task list → `ITaskNavigationService` → single read-only surface (Inspection or ACC status) — **one** task-aware pilot before Email/ProjectWork.

---

## 8. Deferred / out of scope (this document)

| Item | Status |
| --- | --- |
| Broad legacy window migration | **Suspended** — needs this contract + one pilot |
| New task-window router | **Rejected** — use `TaskNavigationResolver` / `ITaskNavigationService` |
| Direct completion from ViewModel | **Rejected** — use `ITaskCompletionCoordinator` |
| Direct WorkflowStage / ProjectStatus mutation from UI | **Rejected** |
| GmailSend / Reply / Forward | **Suspended** — G-Policy |
| Drive / Sheets / Reports | **Suspended** — legacy/deferred |
| ACC write / provisioning / upload | **Suspended** — ACC-Write-Policy |
| Deleting old tasking model (`ProjectTypeTaskType`, etc.) | **Not approved** — still active; document only |

---

## 9. G-Startup cross-check (unchanged by this doc)

V2 New System startup performs silent Gmail restore via `StartNewSystemConnectorAuthRestore()` →
`IConnectorAuthService.TryRestoreSessionAsync()` off the UI thread. No interactive login, no fallback,
no scope changes. See [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md) §3.5.
