# Personal Work Queues by Task Size

**Date:** 23.06.2026
**Status:** Proposed / Documentation Design
**Scope:** `ProjectAssignment` work priority, personal employee work queues, task-size buckets, queue movement rules, and future implementation constraints.

> **Documentation-only.** This document defines a design. No code, schema, or migration is implemented by it. Per repository policy, model/EF changes that this design eventually requires (new columns) must be made as model changes only and reported as requiring `Add-Migration` — migration files must never be authored or edited by hand.

---

## 1. Purpose

This document defines the planned extension of the **existing** personal task queue mechanism.

The application already uses `ProjectAssignment.WorkPriority` to represent the position of a task in an employee's work queue. The goal of this design is to **keep** that mechanism and add **one** dimension: task-size queue buckets.

Each employee will have three personal work queues:

- **Quick** — short tasks, usually expected to take up to about one hour.
- **Medium** — medium tasks, usually expected to take a few hours.
- **Long** — long tasks, usually expected to continue beyond a single working session or over several days.

This is **not** a technical background job queue.
This is **not** a group queue.
This is **not** a department queue.
This is **not** a new workflow engine.

This is a personal work-organization mechanism built on top of the existing `ProjectAssignment` + `WorkPriority` model.

---

## 2. Existing mechanism (source of truth)

The system already contains the core mechanism required for personal work queues. The bucket feature is an **extension**, not a replacement.

- `ProjectAssignment.WorkPriority` (`int?`) stores the task's position in the employee's work queue.
- Open/actionable tasks receive a `WorkPriority`.
- Closed or non-actionable tasks have `WorkPriority = null`.
- New actionable tasks are appended to the end of the employee's queue (`Max(WorkPriority) + 1`).
- When a task leaves the queue, the remaining queue is compacted.

Existing components that manage queue behavior today:

| Component | Role today | Bucket-aware today? |
|---|---|---|
| `TaskPriorityEngine` | Authoritative append / compact / validate / reindex algorithm. | No — scoped by `AssignedToId` only. |
| `WorkPriorityService` | Standalone queue CRUD (`GetWorkQueue`, `MoveTask`, `InsertInQueue`, `RemoveFromQueue`, `GetNextPriority`, `IsQueueValid`, `ValidateAndRepairQueue`). | No — scoped by `employeeId` only. |
| `TaskService` | Task-level operations (`GetOrCreate*`, `ChangeTaskStatus`, `ReassignTask`, `ReorderTask`). | No. |
| `TaskFactory` | Central async creation; calls `TaskPriorityEngine.InsertWithAutoPriorityAsync`. | No. |
| `TaskOperationHelper.UpdateTaskPriority` | UI inline-reorder entry point; delegates to `TaskService.ReorderTask`. | Inherits from `ReorderTask`. |

**Current behavior is bucket-blind.** Today, queue ordering is scoped by:

```
AssignedToId
```

After this design is implemented, queue ordering must be scoped by:

```
AssignedToId + WorkQueueBucket
```

---

## 3. Core principle: a task is a single object

A task is a **single** `ProjectAssignment` row.

- Moving a task between queues must **not** create a new task.
- Changing a task's bucket must **not** duplicate the task.
- The same task must **not** appear in two queues at the same time.

When a task moves from one queue to another:

1. The task is removed from the old queue.
2. The old queue is compacted.
3. The **same** task receives the new `WorkQueueBucket`.
4. The **same** task is inserted into the new queue.
5. The task receives a new `WorkPriority` inside the new queue.

A queue move is a **mutation** of the existing task. It is not a copy and not a recreation. `ProjectAssignment.Id` stays the same, and all history, events, links (workflow, report, email, `TaskLink`), notes, and project associations remain attached to the same row.

---

## 4. Task identity and the existing unique open-task index

The existing filtered unique index (defined in `SiNetSQLDbContext`):

```
IX_ProjectAssignment_UniqueOpenTask
```

enforces open-task uniqueness on:

```
ProjectId, AssignedToId, TaskTypeId, ParentAssignmentId
```

with the SQL filter (verbatim):

```
[ProjectID] IS NOT NULL AND [AssignedToID] IS NOT NULL AND [TaskTypeID] IS NOT NULL AND [WorkPriority] IS NOT NULL
```

**`WorkQueueBucket` must NOT be added to this index.**

Reason: the bucket is an operational classification of the **same** task, not an identity dimension. If `WorkQueueBucket` were added to the unique key, the database would permit multiple open tasks with the same project, assignee, task type, and parent as long as they sat in different buckets — directly violating the single-object rule in §3.

Therefore:

- `WorkQueueBucket` is part of **queue ordering**.
- `WorkQueueBucket` is **not** part of **task identity**.
- Moving between buckets updates the existing row; it never creates a new `ProjectAssignment`.

> Note: because identity excludes the bucket, the existing index naturally supports "same task moves between buckets." No index change is required by this feature.

---

## 5. Queue buckets

| Value | Code | Hebrew display | Meaning |
|---|---|---|---|
| 1 | Quick | קצר | Short task, usually up to about one hour |
| 2 | Medium | בינוני | Medium task, usually a few hours |
| 3 | Long | ארוך | Long task, usually several days or ongoing |

**General default: `Medium` (2).**

---

## 6. Proposed data fields

### 6.1 `ProjectAssignment`

Add a field:

```
WorkQueueBucket   (int, NOT NULL, default 2 = Medium)
```

Classifies the task into one of the employee's three personal queues. A non-null default is required so existing rows resolve to `Medium` after the model change.

### 6.2 `TaskType`

Add a field:

```
DefaultWorkQueueBucket   (int?, nullable; recommended Medium)
```

Defines the default bucket for newly created tasks of this type. If a `TaskType` has no explicit default, the system uses `Medium`.

> Both fields are **future** model changes requiring `Add-Migration`. They are documented here only; no migration is created by this document.

---

## 7. Default bucket by task type

The employee should not normally pick the bucket on every creation. Instead, each `TaskType` defines a default.

When a new task is created:

1. The system reads `TaskType.DefaultWorkQueueBucket`.
2. `ProjectAssignment.WorkQueueBucket` is set from that value.
3. If no value exists, the fallback is `Medium`.
4. If the task is actionable, it is appended to the end of the matching personal queue.
5. If the task is not actionable, it gets no queue position (`WorkPriority` stays `null`).

The employee may later change the bucket manually (see §10.4).

**Example:** A task type defaults to `Quick`. The task is created in the employee's Quick queue. Later the employee realizes it is larger and changes it to `Medium` or `Long`. The **same** task moves to the selected bucket — it is not duplicated.

---

## 8. Meaning of `WorkPriority` after this change

`WorkPriority` continues to mean "position in a queue," but the scope narrows from "all the employee's actionable tasks" to a single bucket:

```
AssignedToId + WorkQueueBucket
```

Each employee therefore has three independent sequences:

```
Quick : 1, 2, 3, ...
Medium: 1, 2, 3, ...
Long  : 1, 2, 3, ...
```

The same employee may legitimately hold `WorkPriority = 1` in Quick, Medium, **and** Long simultaneously, because the queues are separate.

There must be **no duplicate `WorkPriority`** within the same `AssignedToId + WorkQueueBucket`.

---

## 9. Default insertion rule

Whenever a task enters a new queue location, the default insertion position is **the end of the target queue** (`Max + 1` within that bucket).

This applies to:

- New task creation.
- Any bucket change (Quick→Medium, Medium→Long, Long→Quick, etc.).
- Reassigning to another employee.
- Returning from a non-actionable status to an actionable status.

A future UI may allow choosing a specific position during a move. Absent an explicit selection, the behavior is always **append to the end**.

---

## 10. Transition rules

### 10.1 Creating a new task

1. Read the task type's default bucket; set `WorkQueueBucket` (fallback `Medium`).
2. If actionable: `WorkPriority = Max + 1` within `AssignedToId + WorkQueueBucket`.
3. If not actionable: `WorkPriority` stays `null`.
4. Append to the end of the target bucket queue.
5. **No** additional task is created.

> Implementation note (two creation paths): synchronous `GetOrCreateTaskWithDueDate` / `GetOrCreateTaskWithDueDateAndStatus` call `TaskPriorityEngine.InsertWithAutoPriority` directly; the async overloads delegate to `TaskFactory.CreateAsync`, which internally calls `InsertWithAutoPriorityAsync`. In **both** paths the bucket must be set on the `ProjectAssignment` **before** priority insertion.

### 10.2 Closing a task / moving to a non-actionable status

1. Remove the task from its current queue.
2. Clear `WorkPriority`.
3. Compact the old queue **only** within `AssignedToId + WorkQueueBucket`.

Other buckets for the same employee, and other employees' queues, are untouched.

### 10.3 Reopening / returning to an actionable status

1. Use the task's existing `WorkQueueBucket`.
2. If it has none, read `TaskType.DefaultWorkQueueBucket`.
3. If no default exists, use `Medium`.
4. Append to the end of `AssignedToId + WorkQueueBucket`.

The previous position is **not** automatically restored (out of scope; may be revisited).

### 10.4 Changing a task's bucket (e.g., Quick → Long)

The system must:

1. Keep the **same** `ProjectAssignment`.
2. (If supported) record old bucket and old priority for audit (see §12).
3. Remove the task from the old bucket queue.
4. Compact the old bucket queue.
5. Set the new `WorkQueueBucket`.
6. Insert the **same** task at the end of the new bucket queue (`Max + 1`).

Default insertion is the end of the target queue; a future UI may allow an explicit position.

The system must **not**: create a new `ProjectAssignment`, copy the task, leave it in the old bucket, or allow it to appear in two buckets.

### 10.5 Reassigning a task to another employee

1. Keep the **same** `ProjectAssignment`.
2. Remove from the old employee's queue.
3. Compact the old employee's queue within the old `AssignedToId + WorkQueueBucket`.
4. Keep the task's bucket (resolve from `TaskType.DefaultWorkQueueBucket`, then `Medium`, if absent).
5. Insert at the end of `NewAssignedToId + WorkQueueBucket` (`Max + 1`).

The system must **not**: create a new `ProjectAssignment`, leave the task with the old employee, allow it in two employees' queues, or carry the old numeric position to the new employee.

> **Existing known gap (must be addressed during implementation):**
> `TaskService.ReassignTask(...)` today changes only `AssignedToId` (it sets the new assignee and saves) and **does not update `WorkPriority` at all** — it neither compacts the old assignee's queue nor inserts into the new one. This is a pre-existing bug, already documented in `Docs/Domains/Migration/GoogleSheetReviewMigrationDesign-2026-06-21.md` (gap table and risk register). The bucket design does not introduce this gap; it **widens its scope** because the compact/insert must now be bucket-scoped. Reference that document; do not re-describe it as a new behavior.

### 10.6 Reordering inside the same queue

1. Same employee, same bucket.
2. Only `WorkPriority` changes.
3. Affected tasks in the same `AssignedToId + WorkQueueBucket` shift accordingly.
4. The sequence stays continuous (`1, 2, 3, ...`).

No other bucket and no other employee queue is affected.

> Implementation note: `TaskService.ReorderTask` clamps the requested position to `count + 1`, where `count` is the candidate set for the employee. After bucketing, both the candidate set **and** the clamp ceiling must be computed per `AssignedToId + WorkQueueBucket`, or the ceiling will be wrong.

---

## 11. Code areas that must become bucket-aware

> **Extend, do not replace.** Before introducing any new queue concept, confirm the behavior can be achieved by extending the components below.

### 11.1 `TaskPriorityEngine` (authoritative algorithm)

`GetNextPriority(Async)`, `InsertWithAutoPriority(Async)`, `CompactAfterRemoval(Async)`, `ValidateAndReindex(Async)`, `ValidateAndReindexAll`.

Today every method scopes by `AssignedToId` only. All must scope by `AssignedToId + WorkQueueBucket`. In particular, `ValidateAndReindexAll` must iterate `(employee × bucket)` rather than employee alone, or it will merge three sequences into one.

### 11.2 `WorkPriorityService` (standalone queue CRUD)

`GetWorkQueue`, `ValidateAndRepairQueue`, `MoveTask`, `InsertInQueue`, `RemoveFromQueue`, `GetNextPriority`, `IsQueueValid`.

Each must either accept a `bucket` parameter or expose bucket-aware overloads. This service currently **duplicates** parts of the `TaskPriorityEngine` logic (see §15) and must stay consistent with it until a future consolidation.

### 11.3 `TaskService` (task-level operations)

`GetOrCreateTaskWithDueDate`, `GetOrCreateTaskWithDueDateAndStatus`, `GetOrCreateTaskWithDueDateAsync`, `GetOrCreateTaskWithDueDateAndStatusAsync`, `ChangeTaskStatus`, `ReassignTask`, `ReorderTask`, `ReorderPrioritiesAfterRemoval`.

- Creation must set `WorkQueueBucket` from `TaskType.DefaultWorkQueueBucket` **before** insertion.
- Status transitions must compact/append within the correct bucket.
- Reassignment must move the same task between bucket queues (and fix the §10.5 gap).
- Manual reorder must operate inside a single bucket.

### 11.4 `TaskFactory` (central async creation)

`TaskFactory.CreateAsync` calls `TaskPriorityEngine.InsertWithAutoPriorityAsync`. The async `GetOrCreate*` overloads route through it. The bucket must be populated on the `ProjectAssignment` passed to `CreateAsync`. No separate priority logic is needed here — only ensure the bucket is set upstream.

### 11.5 `TaskOperationHelper` (UI inline edit)

`UpdateTaskPriority` delegates to `TaskService.ReorderTask`, so it becomes bucket-correct automatically once `ReorderTask` is fixed. No independent change is required here.

### 11.6 UI areas

`TaskPanelView` / `TaskPanelViewModel`, `FloatingProjectTasksView` / `FloatingProjectTasksViewModel`.

- Employee-centric queue screen: show three buckets via tabs or filter — Quick / קצר, Medium / בינוני, Long / ארוך — each listing one employee + one bucket, ordered by `WorkPriority`.
- Project-centric views: show a small bucket badge (Quick / Medium / Long).
- If the UI offers a bucket change, it must move the **same** task and append to the target queue by default.

---

## 12. Event and audit expectations

Queue movement should be auditable through the **existing** `ProjectAssignmentEvent` mechanism. Do not build a parallel audit channel.

**Current schema reality:** `ProjectAssignmentEvent` exposes `EventType` (string, centralized in `TaskEventTypes`), `Note`, `Old/NewStatusId`, plus contact/company/link/result fields. It has **no** columns for bucket or priority values. `TaskEventTypes` currently defines: `Created`, `StatusChange`, `Note`, `ProjectChange`, `DueDateChange`, `EmailLink`, `ExternalWait`, `AssigneeChange`.

Therefore, for the first implementation:

- Reuse the existing event row + `Note` text to capture before/after values (e.g., `"bucket: Quick → Long"`, `"priority: 3 → 5"`).
- Add a new constant such as `BucketChange` to `TaskEventTypes` (idiomatic extension of the existing enum-like class). This is the only audit-side addition needed and does **not** require a schema change.
- Storing **structured** old/new bucket and old/new priority **columns** would require a schema change and is therefore **out of scope** for this design; if desired later, document it as a separate, explicitly approved change.

Recommended event semantics (encoded in `EventType` + `Note`):

- **Bucket changed:** old bucket, new bucket, old priority, new priority.
- **Priority changed within the same bucket:** old priority, new priority.
- **Assignee changed:** old assignee, new assignee, old bucket, new bucket, old position, new position.

---

## 13. Required verification scenarios

1. A new task receives `WorkQueueBucket` from `TaskType.DefaultWorkQueueBucket`.
2. With no task-type default, the task receives `Medium`.
3. A new actionable task is appended to the end of the correct bucket queue.
4. A non-actionable task receives no `WorkPriority`.
5. Closing a task clears `WorkPriority`.
6. Closing a task compacts **only** the old bucket queue.
7. Reopening appends to the end of the correct bucket queue.
8. Changing bucket moves the **same** task to the end of the new bucket queue.
9. Changing bucket creates **no** new `ProjectAssignment`.
10. Changing bucket leaves **no** copy in the old bucket.
11. Reassigning moves the **same** task to the end of the new employee's matching bucket queue.
12. Reassigning compacts the old employee's old bucket queue.
13. Reassigning creates **no** new `ProjectAssignment`.
14. Reordering within a bucket affects only that bucket.
15. Each `employee + bucket` queue stays continuous (`1, 2, 3, ...`).
16. The same employee may hold `WorkPriority = 1` in multiple buckets.
17. No duplicate `WorkPriority` within the same `employee + bucket`.
18. `WorkQueueBucket` is **not** added to `IX_ProjectAssignment_UniqueOpenTask`.
19. No task appears in two buckets simultaneously.
20. No queue move produces duplicate tasks.
21. Both creation paths (sync direct-engine, async via `TaskFactory`) assign the bucket before insertion.
22. `ReorderTask`'s position clamp is computed per `employee + bucket`.

---

## 14. Out of scope

- Creating a new `Queue` table.
- Creating a new `QueueItem` table.
- Group queues.
- Department queues.
- A background job queue.
- A parallel mechanism to `WorkPriority`.
- Replacing `ProjectAssignment`.
- Changing workflow semantics.
- Changing `TaskLink` semantics.
- Duplicating tasks when moving between queues.
- Automatically determining the bucket by AI or task text.
- Smart scheduling or automatic daily planning.
- Adding structured bucket/priority **columns** to `ProjectAssignmentEvent`.

---

## 15. Dropped / cancelled / postponed

| Item | Status | Reason |
|---|---|---|
| Group queue | Postponed / not required | Requirement is personal employee queues only. |
| Department queue | Postponed / not required | Not needed for the current office workflow. |
| New `Queue` table | Not approved | Reuse existing `ProjectAssignment` + `WorkPriority`. |
| `QueueItem` table | Not approved | Would create a parallel queue model. |
| Background job queue | Out of scope | This is a user work-planning feature, not technical processing. |
| Automatic AI bucket classification | Postponed | First implementation uses `TaskType.DefaultWorkQueueBucket`. |
| Merging `WorkPriorityService` and `TaskPriorityEngine`/`TaskService` | Postponed | Existing overlap is documented (§11.2) and reviewed later, not removed now. |
| Structured bucket/priority audit columns | Postponed | First implementation uses `EventType` + `Note`; columns would require a schema change. |
| Preserving previous position after a bucket move | Postponed | Current rule: moves append to the end unless a future UI offers explicit positioning. |

---

## 16. Implementation principle for future work

Future implementation must **extend** the existing mechanism. Before writing new code or introducing a new queue concept, verify the behavior can be achieved by extending:

- `ProjectAssignment`
- `TaskType`
- `TaskPriorityEngine`
- `WorkPriorityService`
- `TaskService`
- `TaskFactory`
- existing task UI screens

The preferred direction is to integrate into the current task model, not to create a parallel system. If duplication is found (e.g., `WorkPriorityService` vs `TaskPriorityEngine`), document it as a candidate for future consolidation — do not remove or merge it as part of this feature.
