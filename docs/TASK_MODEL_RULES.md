# Task Model Rules (ProjectAssignment)

Technical reference for how tasks, queues, and identity constraints work in the New System.
Derived from `ProjectAssignment` entity, `SiNetSQLDbContext` index configuration, and `ITaskQueueService` / `TaskQueuePriorityEngine`.

## What is a task?

A task is a row in **`ProjectAssignment`**. It represents work assigned to a user on a project, with a type, status, optional parent, and optional queue position.

| Field | Role |
|-------|------|
| `ProjectId` | Which project the task belongs to |
| `AssignedToId` | Employee responsible for the task |
| `TaskTypeId` | What kind of work (stage / task type) |
| `StatusId` | Lifecycle state via `ProjectAssignmentStatus` (`IsOpen`, `IsActionable`) |
| `ParentAssignmentId` | Hierarchy: `null` = top-level; non-null = child under parent |
| `WorkQueueBucket` | Personal queue bucket: Quick / Medium / Long (operational only) |
| `WorkPriority` | Position within assignee + bucket queue (`1` = top). `null` = not in queue |
| `Priority` | **Legacy** string field (“importance”). **Not** used for queue ordering |

## Priority vs WorkPriority vs WorkQueueBucket

| Concept | Field | Used for queue? |
|---------|-------|-----------------|
| Legacy importance | `Priority` (string) | **No** — display / legacy only |
| Queue position | `WorkPriority` (int?) | **Yes** — `1..N` within bucket |
| Queue bucket | `WorkQueueBucket` (int) | **Yes** — which of the three personal queues |

**Rule:** All queue mutations go through `ITaskQueueService` (`MoveUpAsync`, `MoveDownAsync`, `RepairQueueAsync`, etc.). UI must not assign `WorkPriority` directly.

## Top-level vs child tasks

| | Top-level | Child (sub-task) |
|---|-----------|------------------|
| `ParentAssignmentId` | `null` | FK to parent `ProjectAssignment.Id` |
| Identity in unique index | `(ProjectId, AssignedToId, TaskTypeId, null)` | `(ProjectId, AssignedToId, TaskTypeId, ParentAssignmentId)` |

**Can there be multiple sub-tasks under the same parent?**

**Yes**, when they differ by `TaskTypeId` or by being distinct child rows (different `ParentAssignmentId` chain / different task ids).

**Can there be two identical open tasks?**

**No**, for the same combination:

- `ProjectId`
- `AssignedToId`
- `TaskTypeId`
- `ParentAssignmentId`

while the row is considered “open” for the unique index (see below).

## IX_ProjectAssignment_UniqueOpenTask

Defined in `SiNetSQLDbContext` and migrations (e.g. `20260502191844_AddSmartTasksHierarchyP1`).

**Columns (unique together):**

1. `ProjectID` (`ProjectId`)
2. `AssignedToID` (`AssignedToId`)
3. `TaskTypeID` (`TaskTypeId`)
4. `ParentAssignmentID` (`ParentAssignmentId`)

**Filter (SQL Server filtered unique index):**

```sql
[ProjectID] IS NOT NULL
AND [AssignedToID] IS NOT NULL
AND [TaskTypeID] IS NOT NULL
AND [WorkPriority] IS NOT NULL
```

**Meaning:**

- Only rows with all four identity columns set **and** `WorkPriority IS NOT NULL` participate in uniqueness.
- “Open in queue” is approximated by `WorkPriority IS NOT NULL` — closed / non-actionable tasks clear `WorkPriority`, so the same `(project, user, type, parent)` can be created again after closure.
- **`WorkQueueBucket` is NOT part of task identity.** The same user cannot have two open rows with the same `(ProjectId, AssignedToId, TaskTypeId, ParentAssignmentId)` in different buckets.

## WorkQueueBucket and identity

`WorkQueueBucket` is **operational classification only** (Quick / Medium / Long). It does **not** appear in `IX_ProjectAssignment_UniqueOpenTask`.

Changing bucket moves the task between queues via `ITaskQueueService.ChangeBucketAsync` (compact old bucket, append to new).

## Queue model

| Concept | Definition |
|---------|------------|
| Queue identity | `AssignedToId` + `WorkQueueBucket` |
| Queue position | `WorkPriority` (contiguous `1..N` per queue after repair) |
| In queue | Actionable status **and** `WorkPriority` not null |

Implementation: `TaskQueuePriorityEngine`, `SqlTaskQueueService`.

### Lifecycle effects on the queue

| Event | Queue behavior |
|-------|----------------|
| **Complete / close** (non-actionable) | `WorkPriority = null`; compact assignee+bucket queue |
| **Delete** | Remove row; compact queue |
| **Reassign user** | Compact old user’s queue; append to new user’s queue end |
| **Change bucket** | Compact old bucket; append to new bucket end |
| **Repair queue** | Assign missing priorities, dedupe, close gaps, clear stale priorities on non-actionable rows |

## StatusId vs queue membership

`StatusId` drives `ProjectAssignmentStatus.IsOpen` / `IsActionable`. Only **actionable** open tasks belong in the queue (`WorkPriority` set). Closed or non-actionable tasks should have `WorkPriority = null`.

## UI (Task Workbench)

- Column **“מיקום בתור”** binds to `WorkPriority`.
- Legacy `Priority` is not shown as queue position (if shown elsewhere, label as “חשיבות”).
- Move up/down and repair use `ITaskQueueService` only.
- **Project selection** uses the shared `ProjectSelectorView` → `ICurrentProjectContext` (no duplicate project ComboBox).
- Task cards show **`#TaskId`** (tooltip «מספר משימה N»); AutomationId stays `Task.{TaskId}`.
- Project line uses **business** `ProjectNumber — ProjectName` (via `ProjectNumberFormatting`), not internal `ProjectId` as the main label (`ProjectId=…` may appear in tooltip only).
- Card zebra uses theme brushes (`SiBackgroundBrush` / `SiSurfaceBrush`); selection Primary border remains stronger than zebra.

## Task creation flow (Task Workbench)

| Field | Source |
|-------|--------|
| `ProjectId` | Shared **Project Selector** → `ICurrentProjectContext.CurrentProject` |
| `AssignedToId` | Current user, or admin scope selection |
| `TaskTypeId` | Active task types dropdown |
| `StatusId` | First open **actionable** status (auto-selected) |
| `WorkQueueBucket` | User bucket choice; defaults from `TaskType.DefaultWorkQueueBucket` |
| `WorkPriority` | **Not set in ViewModel** — `TaskQueuePriorityEngine.InsertWithAutoPriorityAsync` via `ITaskWorkbenchService` |
| `ParentAssignmentId` | Optional; `null` for top-level tasks |
| `Title` / `Body` | User input |

**Rules:** no project → **"לא נבחר פרויקט"**; no silent first-project fallback; duplicate identity rejected; `WorkQueueBucket` not part of identity.

## Related code

| Area | Location |
|------|----------|
| Entity | `src/SiNet.Infrastructure.Sql/Models/ProjectAssignment.cs` |
| Index | `src/SiNet.Infrastructure.Sql/Data/SiNetSQLDbContext.cs` |
| Queue engine | `src/SiNet.Infrastructure.Sql/Services/Tasks/TaskQueuePriorityEngine.cs` |
| Queue service | `src/SiNet.Infrastructure.Sql/Services/Tasks/SqlTaskQueueService.cs` |
| Workbench UI | `src/SiNet.App.Wpf/Surfaces/Tasks/` |

## Out of scope (current work)

- Schema / migration changes
- Drag-and-drop reorder
- Complete/close workflow in Workbench
- Using legacy `Priority` as queue position
