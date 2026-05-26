# Workflow Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Active — source of truth for tasks, actions, and runtime workflow.
- **Scope:** Tasks, Actions, RuntimeActions, action handlers, task completion, ReviewTask, FileQuoteMaterial, workflow advance, FloatingTasks, `PRP.MaterialCheck`.

## Purpose
Define how tasks and actions move the system forward, who is responsible for what, and what must not be duplicated.

## Source of truth
- `SiNetSQL\docs\WorkflowDecisions.md` — append-only workflow decision log.
- `SiNetSQL\docs\ProjectFileFiling.md` — filing pipeline architecture and `IProcessActionHandler` dispatcher.

## Core principles
1. Tasks drive the workflow. Actions are triggered through task completion and the action handler dispatcher.
2. Workflow stages use the `PLN.*` codes (e.g. `PLN.Approval.AuthorityApproved`). `ProjectStatus` is a separate broad list — do not use `Completed` as `ProjectStatus`.
3. `ProjectStatus` values are limited to: LeadReceived, QuotePreparation, WaitingForQuoteApproval, WaitingForWorkOrder, Active, WaitingForClient, WaitingForAuthority, WaitingForMaterial, BillingPending, Closed, ClosedLost, Cancelled.
4. Design stages and disciplines depend on `ProjectType`.
5. Gmail labels are not statuses; they represent responsible assignee only.
6. Task assignment to a UserGroup:
   - 1 member → auto-assign.
   - Multiple members → use group default assignee.
   - No default → user must pick. Each group should have a default assignee setting.
   - Empty group / no default → workflow notifies the user and does not proceed without an assignee.
7. New open tasks are appended to the **end** of the queue (Max+1). Reopened tasks also go to the end. On close, clear priority and re-rank.
8. Do not invent a new handler if a suitable mechanism (e.g. an existing `IProcessActionHandler`) already exists. Extend, do not duplicate.
9. Runtime action state snapshots (e.g. `RuntimeActions-CurrentState.md`) are informational; binding decisions live in `WorkflowDecisions.md`.

## What we do not do now
- Do not bypass the action handler dispatcher for new actions.
- Do not change workflow designer or migrations as part of documentation work.
- Do not represent statuses via Gmail labels.

## Dropped / cancelled / postponed
- `Completed` as a `ProjectStatus` value — dropped.
- Parallel ad-hoc handler chains — dropped.
- Deep per-handler documentation — postponed.

## Relevant terms / search terms
Task, Action, RuntimeAction, IProcessActionHandler, MoveToProject, AddMaterialToProject, ReviewTask, FileQuoteMaterial, FloatingTask, PRP.MaterialCheck, WorkflowStageDefinition, PLN.Approval.AuthorityApproved, ProjectStatus, UserGroup, default assignee.

## Relevant code areas (informational)
- `SiNetSQL\SiNetSQL\Domain\Actions\Handlers`
- `IProcessActionHandler` implementations
- `WorkflowDecisions.md`

## Extracted from archived documents (Round C, 26.05.2026)

Sources (archived): `Action-Task-Workflow.md`, `Typed-Continuation-Design.md`, `ProjectWork-Documentation.md`.

### Typed continuation paths — no legacy fallback
- Migrated continuation actions (TaskCreation, FileImport) run on a **typed-only path**: application service → `IActionContinuationUiHost` → typed dialog → typed result → application service `ContinueAsync`. No `RequiresUI(...)` enum fallback.
- Continuation requests and results are **typed models**, not an open `enum + props` bag.
- The UI host is abstracted (e.g. `IActionContinuationUiHost`); ViewModels do not bind to dialogs directly.
- Missing data, validation failure, or inability to open UI must **fail visibly** (clear error + log). No silent fallback.
- Legacy paths still physically present (e.g. `AssignActionDialog`, `EmailManagementView.CreateActionTaskAsync` for migrated actions) are **not active** and are candidates for removal.

### Project work and workflow boundary
- `WorkflowManagementWindow` is the single management surface for the workflow engine (Builder, Visual Designer, Policy, Dashboard, Behaviors, Help). No parallel Builder/Policy windows.
- The ProjectWork screen **does not** change `ProjectStatus` or `WorkflowStage` directly; such changes go only through workflow actions / engine transitions.
