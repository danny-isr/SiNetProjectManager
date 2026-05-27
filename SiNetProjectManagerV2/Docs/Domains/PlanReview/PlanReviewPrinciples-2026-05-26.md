# Plan Review Principles

- **Decision date / Updated date:** 26.05.2026
- **Status:** Skeleton — to be expanded as the Plan Review feature lands.
- **Scope:** Plan review as a stand-alone domain: business `Workflow` for reviewing plans, reusable `Inspection` / `Review` work component, dedicated UI window, reviewed files, results, optional AI assistance (advisory only), and links back to project / task / file.

## Purpose
Define Plan Review as its own domain rather than as a side effect of filing or tasks.

## Source of truth
- This document for principles. Detailed implementation docs will be added under this folder when the feature lands.

## PlanReview / Inspection / Review / AI boundaries (added 26.05.2026)

This section is the authoritative split between `PlanReview`,
`Inspection` / `Review`, the dedicated review UI, and `AI`. It
complements
[`Domains\Workflow\WorkflowPrinciples-2026-05-26.md`](../Workflow/WorkflowPrinciples-2026-05-26.md)
§ *Workflow / Task / Action handler boundaries* and
[`Domains\AI\AiSystemPrinciples-2026-05-26.md`](../AI/AiSystemPrinciples-2026-05-26.md).

### PlanReview

- **`PlanReview` is a business `Workflow` for reviewing plans.** It is
  not a side mechanism and not a generic automatic action.
- `PlanReview` must use the system's regular `Workflow` / `Task` /
  `Action Handler` mechanisms. It must **not** create a parallel
  workflow engine.
- `PlanReview` is **not** performed as a silent side effect of filing a
  file. If filing a file should trigger a plan review, that trigger
  goes through the agreed `Task` / `Workflow` / `Action Handler` path.

### Inspection / Review

- `Inspection` / `Review` is a **reusable work component inside a
  `Workflow`**, with a dedicated UI window.
- Inside `PlanReview` it can serve as a **stage**, an **action**, or a
  central **Task**.
- In the future, the same component may be used by other `Workflow`s,
  not only by `PlanReview`.
- `Inspection` / `Review` is **not** a stand-alone workflow engine.
- `Inspection` / `Review` is **not** "just UI". It is also not always
  only a `Task` and not always only an `Action`. It is a review-domain
  work component, represented per context:
  - **As a `Task`** when assignment, responsibility, due date, tracking,
    or completion are needed.
  - **As an `Action`** when a concrete operation is needed (opening the
    UI, recording a result, generating a report, saving notes, advancing
    a stage) — executed through the agreed `Action Handler` / `Service`.

### Dedicated UI

- `Inspection` / `Review` has a **dedicated UI window**.
- The window is **not** a source of truth.
- The window is **not** a workflow engine.
- The window **shows state**, lets the user work, and **invokes the
  agreed Services / Handlers**.
- It does **not** change business state directly without going through
  the appropriate mechanism (see `WorkflowPrinciples` § *UI / ViewModel
  boundary*).

### AI inside PlanReview / Inspection / Review

- `AI` is an **advisory tool only**. See
  [`Domains\AI\AiSystemPrinciples-2026-05-26.md`](../AI/AiSystemPrinciples-2026-05-26.md).
- `AI` may assist with: issue detection, suggested notes / comments,
  content summarisation, initial analysis, recommended checks.
- `AI` must **not**:
  - approve a plan on its own,
  - reject a plan on its own,
  - close a `Task` on its own,
  - advance a `Workflow` stage on its own,
  - file a file on its own,
  - change business metadata on its own,
  - write back to DB / ACC / Storage Destination without explicit user
    confirmation or an agreed `Action Handler`.
- When `AI` output becomes a business action, it must go through:
  - **explicit user confirmation**, or
  - an **agreed `Action Handler`**, or
  - an **approved `Workflow` / `Task` path**.

### Source of truth

- **DB** is the source of truth for the business process:
  `PlanReview` state, tasks, workflow, review results, reviewer,
  timestamps, and links to project / file / task.
- **Storage Destination** is the source of truth for the physical
  existence of the file under review (ACC / File Server / Google Drive;
  Gmail is read-only ingestion only).
- **`AI` is not a source of truth.**
- **UI is not a source of truth.**

## Core principles
1. **Plan Review is its own business `Workflow`.** It has a dedicated
   UI window and is not embedded as a side effect of generic file
   filing.
2. **Reviewed files** are drawings / plans from ACC (or external) that
   have been filed into the project. The review references the file by
   its authoritative identifier (ACC URN where possible, otherwise the
   `ProjectFileInstance` runtime projection for the selected project).
3. **Review results** are stored as first-class data, with timestamp
   and reviewer. The most recent result is authoritative.
4. **Link back to project / task / file** is explicit. A review row
   references the project, the task that initiated it (if any), and the
   file under review.
5. **`Inspection` / `Review` is a reusable workflow activity** with a
   dedicated UI; inside `PlanReview` it may be a **stage**, a **`Task`**,
   or an **`Action`** depending on context (see § *PlanReview /
   Inspection / Review / AI boundaries*).
6. **AI assistance, if used, is advisory only** (see `AiSystemPrinciples`).
   AI never auto-approves, auto-rejects, auto-completes a task,
   auto-advances a workflow stage, or writes business state without an
   approved user / action-handler path.
7. **Nothing is performed automatically without explicit user
   confirmation** — including marking a plan as approved/rejected,
   advancing workflow, or writing back to ACC.
8. Plan Review must integrate with existing tasks / actions through the
   agreed dispatcher (e.g. `IProcessActionHandler`); it must **not**
   create a parallel workflow engine.

## What we do not do now
- Do not auto-approve or auto-reject plans.
- Do not store review results outside the proper domain tables.
- Do not bypass the workflow dispatcher for plan-related actions.
- Do not run `PlanReview` as a silent side effect of file filing; route through `Task` / `Workflow` / `Action Handler`.
- Do not let the Inspection / Review UI change `Review` / `Workflow` /
  `Task` state directly; UI calls Services / Handlers.
- Do not treat `Inspection` / `Review` as a stand-alone workflow engine.
- Do not let `AI` approve, reject, close, advance, or write state on
  its own.
- Do not store `AI` output as business truth without explicit user
  confirmation or an agreed handler.

## Dropped / cancelled / postponed
- Embedding plan review inside generic filing UI — dropped.
- AI auto-decision on plan review — postponed (requires explicit decision).
- Full review schema definition — postponed.
- `Inspection` / `Review` as a stand-alone workflow engine — **not approved**.
- `PlanReview` as a silent side effect of file filing — **not approved**.
- UI that changes `Review` / `Workflow` / `Task` state directly — **not approved**.
- `AI` as an autonomous decision maker — **not approved**.
- `AI` auto-approve / auto-reject / auto-complete — **not approved**.
- A parallel mechanism alongside `Workflow` / `Task` / `Action Handler`
  for `PlanReview` / `Inspection` / `Review` — **not approved**.

## Relevant terms / search terms
Plan Review, PlanReview Workflow, Inspection, Review, drawing review, reviewer, review result, advisory AI, ACC URN, ProjectFileInstance, task linkage, Inspection/Review UI window, reusable workflow activity, Action Handler, IProcessActionHandler.
