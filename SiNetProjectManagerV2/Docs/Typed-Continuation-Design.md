# Typed Continuation Design (Pre-Phase 2 Planning)

> **Status:** Documentation / design only. No code changes, no DB changes,
> no migrations, no refactor, no UI changes, no new types created.
> `ActionFollowUp` is **not** moved in this document.
>
> **Implementation status (as of Phase 5 pilot):** Typed Continuation is
> **implemented end-to-end for `ApproveOrClose` and `CloseOpinion`** through
> `WorkflowAdvanceDialog`. The path is:
> `EmailContextViewModel` → `IApproveOrCloseApplicationService` /
> `ICloseOpinionApplicationService` (`StartAsync`) → `IProcessActionDispatcher`
> → handler emits `WorkflowAdvanceContinuationRequest` →
> `IActionContinuationUiHost` (WPF: `WpfActionContinuationUiHost`) returns
> `WorkflowAdvanceContinuationResult` → application service `ContinueAsync`
> finalizes the outcome and lifecycle reporting.
>
> **TaskCreationDialog family (Phase 5 extension, no-fallback rule):**
> The TaskCreationDialog family is migrated to a typed-only path with **no
> legacy fallback**. The migrated `SuggestedActionType` values are listed in
> `SiNetSQL.Domain.Actions.Continuation.TaskCreationActionCatalog`. For those
> actions the only valid path is:
>
> `EmailContextViewModel` → `ITaskCreationContinuationApplicationService.StartAsync`
> → `TaskCreationContinuationRequest` → `IActionContinuationUiHost`
> (WPF: `TaskCreationDraftDialog`) → `TaskCreationContinuationResult(TaskDraft)`
> → `ITaskCreationContinuationApplicationService.ContinueAsync` →
> `TaskFactory.CreateAsync` → `ProjectAssignment` created.
>
> If the new path is missing required data, fails validation, or cannot open
> UI, the action **fails visibly** (clear error message, log entry) — it does
> **not** fall back to `ActionResult.RequiresUI(ActionFollowUp.TaskCreationDialog)`,
> `AssignActionDialog`, or `EmailManagementView.CreateActionTaskAsync`.
> `ActionExecutor` enforces this rule by returning `ActionResult.Failed(...)`
> via the `TaskCreationTypedRequired(...)` helper if any migrated action
> reaches its switch directly.
>
> Legacy `ActionFollowUp.TaskCreationDialog` / `AssignActionDialog` /
> `CreateActionTaskAsync(...)` code may remain physically present, but it is
> **dead code for migrated actions** and is only reachable by `SuggestedActionType`
> values not yet listed in `TaskCreationActionCatalog`.
>
> **FileImportDialog family (Phase 5 extension, no-fallback rule):**
> The FileImportDialog family is migrated to a typed-only path with **no
> legacy fallback**. The migrated `SuggestedActionType` values are listed in
> `SiNetSQL.Domain.Actions.Continuation.FileImportActionCatalog`
> (`UploadNewVersion`, `ReceiveSupplementaryMaterial`, `ReceiveMaterialForReview`,
> `ReceiveCorrectedVersion`, `ReceiveMaterialForOpinion`). `CollectMaterial`
> is intentionally not migrated (it is not a target-filing flow), and
> `AddMaterialToProject` continues to run through its canonical handler
> (`AddMaterialToProjectProcessActionHandler`) where `RequiresUI(FileImportDialog)`
> represents a legitimate "inputs missing" deferred outcome rather than a
> legacy fallback. For migrated actions the only valid path is:
>
> `EmailContextViewModel` → `IFileImportContinuationApplicationService.StartAsync`
> → `FileImportContinuationRequest` → `IActionContinuationUiHost`
> (WPF: `FileImportDialog` in `draftOnlyMode`) →
> `FileImportContinuationResult(FileImportDraft)` →
> `IFileImportContinuationApplicationService.ContinueAsync` → one
> `AddMaterialToProject` dispatch per `FileImportSelection` through
> `IProcessActionDispatcher` → `IProjectFileFilingService.FileAsync`.
>
> If the new path is missing required data, fails validation, or cannot open
> UI, the action **fails visibly** — it does **not** fall back to
> `ActionResult.RequiresUI(ActionFollowUp.FileImportDialog)`. `ActionExecutor`
> enforces this rule by returning `ActionResult.Failed(...)` via the
> `FileImportTypedRequired(...)` helper if any migrated FileImport action
> reaches its switch directly.
>
> Phase 3 had previously closed the `MoveToProject` duplicate-path issue via
> `IEmailMoveToProjectApplicationService`. The legacy
> `PrefilledData["ConfirmAdvance"]` bridge is intentionally **retained** so
> any code path that still calls `ActionExecutor.ExecuteAsync(...)` with a
> confirmation flag keeps working until all WPF callers are migrated. Only
> `ContinuationUiKind.WorkflowAdvanceDialog` is supported by the host in this
> pilot; other `RequiresUI` actions still flow through the legacy
> `ActionFollowUp` / `PrefilledData` path.
>
> **Companion documents:**
> - [`Action-Task-Workflow.md`](./Action-Task-Workflow.md) — architecture overview.
> - [`Action-Handler-Inventory.md`](./Action-Handler-Inventory.md) — Phase 1 mapping.
>
> **Repos in scope:** `danny-isr/SiNetProjectManager`, `danny-isr/SiNetSQL`.

---

## 1. Overview

The Phase 1 inventory shows that **27 of the 39 `SuggestedActionType` rows**
end with `ActionExecutor` returning `ActionResult.RequiresUI(...)` and nothing
more. After the user closes the dialog, the system has no formal contract for
how the business flow resumes:

- Some actions are silently re-issued by the UI with extra keys written into
  `SuggestedAction.PrefilledData` (e.g. `AssociateToExistingProject` after the
  picker, `ApproveOrClose` with `ConfirmAdvance`).
- Some actions stop in the UI and never return to the backend at all (e.g.
  `CreateNewProject`, `ForwardToDecision`, the entire `Task-Creation-Dialog`
  family).
- `CloseOpinion` is declared `CanAdvanceWorkflow = true` in
  `ActionDefinitionRegistry`, yet there is no backend continuation that
  actually advances a workflow.

This document defines the **typed continuation contract** that will replace
the current `Dictionary<string, object?>` re-issue pattern as the primary
return path from the UI. No type is created here — names are proposals only.

---

## 2. Goals

1. Establish a **single, typed contract** between Handler / Application
   Service and the UI for "I need the user, then continue".
2. Replace the unsafe central use of `PrefilledData` (an open dictionary) with
   a typed Continuation Request → typed Continuation Result.
3. Make Handlers and Application Services **unit-testable** without WPF.
4. Allow `RequiresUI` actions to be migrated **one at a time**, with the
   legacy path still working until each one is converted.
5. Preserve the agreed canonical control flow:
   `ViewModel → Application Service → IProcessActionDispatcher → IProcessActionHandler`.
   The ViewModel does **not** know about `ActionCode`, the dispatcher, or
   workflow advance.

---

## 3. Non-goals

- **Not** implementing any type in this document.
- **Not** changing `ActionExecutor`, `ActionResult`, `SuggestedAction`,
  `PrefilledData`, or any handler.
- **Not** moving `ActionFollowUp` out of `SiNetSQL.Services.EmailContext`
  (that decision is parked — see §7).
- **Not** changing any UI, dialog, ViewModel, or XAML.
- **Not** changing the database, EF model, or migrations.
- **Not** retiring `ActionResult.RequiresUI(...)` overload yet.

---

## 4. Proposed Model

The target flow when a backend action needs user input:

```
ViewModel
	│ (1) raises business intent — by ActionCode + minimal context
	▼
Application Service                ◄────────────────────────────────┐
	│ (2) builds ActionExecutionContext                              │
	│ (3) calls IProcessActionDispatcher.DispatchAsync               │
	▼                                                                 │
IProcessActionHandler                                                │
	│ (4a) Completed       → returns ProcessActionResult.Completed   │
	│ (4b) NeedsUserInput  → returns ProcessActionResult.Deferred    │
	│      with a typed IActionContinuationRequest in Data           │
	▼                                                                 │
Application Service                                                  │
	│ (5) translates typed Continuation Request                      │
	│     to a ContinuationUiKind + payload                          │
	▼                                                                 │
UI continuation host                                                 │
	│ (6) opens the matching dialog / picker / wizard                │
	│ (7) returns a typed IActionContinuationResult                  │
	▼                                                                 │
Application Service                                                  │
	│ (8) calls dispatcher again (or directly                        │
	│     SmartTaskService / ITaskCompletionCoordinator /            │
	│     WorkflowTaskOrchestrator) with the typed result            │
	└────────────────────────────────────────────────────────────────┘
							(loop back to handler if needed)
```

Key constraints implied by this model:

- **Handlers never call the UI.** They return a typed Continuation Request.
- **The UI never decides business outcomes.** It returns a typed
  Continuation Result.
- **Application Services own the loop.** Only they decide whether to
  re-dispatch, call `SmartTaskService`, call `ITaskCompletionCoordinator`,
  or report `Cancelled`.
- **`PrefilledData` is no longer the contract.** It can still exist for
  legacy compatibility but is not the continuation channel.

---

## 5. Suggested base contracts (proposed names, not created here)

> These names are illustrative. Final names will be decided during the
> pilot's design review (see §9 and §12).

| Proposed name | Role |
|---|---|
| `IActionContinuationRequest` | Marker / base contract for any "the handler needs the user". Carries `ActionCode`, a `ContinuationUiKind`, and a typed payload (concrete request type, see §6). |
| `IActionContinuationResult` | Marker / base contract returned by the UI. Carries the originating `ActionCode`, a typed payload (concrete result type), and an `ActionContinuationOutcome`. |
| `ContinuationUiKind` (enum) | UI taxonomy: `ProjectPicker`, `TaskCreationDialog`, `FileImportDialog`, `WorkflowAdvanceDialog`, `DecisionDialog`, `NewProjectDialog`, `DisciplineDialog`. Initial values mirror the existing `ActionFollowUp` set so an adapter is trivial. |
| `ActionContinuationContext` | The application-service-owned bag that carries the original `ActionExecutionContext`, the `IActionContinuationRequest` returned by the handler, and a correlation id. |
| `ActionContinuationOutcome` (enum) | `Confirmed`, `Cancelled`, `Failed`. Mirrors / refines `ActionExecutionStatus` for the UI side. |

Notes:

- `IActionContinuationRequest` is the analogue of "the input shape the dialog
  needs". `IActionContinuationResult` is the analogue of "what the dialog
  produced". They are **paired** per `ContinuationUiKind`.
- The dispatcher does **not** need to know about continuation types. It only
  needs to be able to carry a `IActionContinuationRequest` inside the
  existing `ProcessActionResult.Deferred` shape (in its `Data`). No
  dispatcher contract change is required.

---

## 6. Suggested concrete continuations

Initial set (one per current `ActionFollowUp` value). Concrete payload
fields are proposals, not requirements:

| Continuation pair | Request carries | Result carries |
|---|---|---|
| `ProjectPickerContinuationRequest` / `Result` | Filter hints (`SuggestedProjectTypeCode`, `RequireOpenStatus`, `ExcludeProjectIds`). | `SelectedProjectId`, `Outcome`. |
| `TaskCreationContinuationRequest` / `Result` | Default `TaskTypeCode`, `SuggestedAssigneeId`, `SourceEntityRef` (email / file / review), `PrefilledTitle`. | `CreatedTaskId` (when the dialog creates it directly) **or** a `TaskDraft` (when the application service creates it through `SmartTaskService` afterwards), `Outcome`. |
| `FileImportContinuationRequest` / `Result` | `ProjectId`, `TargetFolderHint`, `AllowedKinds`, `Source` (email attachment, ACC, local). | `ImportedFileRefs` (file ids or temp paths), `Outcome`. |
| `WorkflowAdvanceContinuationRequest` / `Result` | `WorkflowInstanceId`, `CurrentStageCode`, `AvailableTransitions`. | `ChosenTransitionId` or `ConfirmAdvance=false`, `Outcome`. Replaces today's informal `PrefilledData["ConfirmAdvance"]`. |
| `DecisionContinuationRequest` / `Result` | `EmailMessageId`, `SuggestedRecipients`. | `DecisionId`, `Outcome`. |
| `NewProjectContinuationRequest` / `Result` | `SourceEmailMessageId`, `SuggestedProjectTypeCode`, prefilled values. | `CreatedProjectId`, `Outcome`. |
| `DisciplineContinuationRequest` / `Result` | `ProjectId`, available disciplines. | `SelectedDisciplineId`, `Outcome`. |

Open question: whether `TaskCreationContinuationResult` should always carry a
`CreatedTaskId` (dialog-creates) or always carry a `TaskDraft`
(service-creates). Recommended: **`TaskDraft` only**, so all task creation
flows through a single factory (aligns with Phase 2). See §12.

---

## 7. Relation to `ActionFollowUp`

Today:

- `ActionFollowUp` is a UI hint enum used by `ActionResult.RequiresUI(...)`.
- It lives under `SiNetSQL.Services.EmailContext`, which is a documented
  layering concession (`Domain/Actions` already references it through
  `ActionDefinitionRegistry.MapFromActionFollowUp`).

In the typed-continuation world:

- **`ContinuationUiKind` is the long-term home** of "which UI the user must
  see". Its initial values are intentionally a superset of `ActionFollowUp`
  so a trivial adapter exists:
  `ActionFollowUp.X` ↔ `ContinuationUiKind.X`.
- **`ActionFollowUp` is not moved or removed in this design.** It stays where
  it is, callers keep using it, and the inventory document still considers
  it the current concession.
- **Decision deferred:** whether to (a) keep `ActionFollowUp` as a thin alias
  for `ContinuationUiKind`, (b) replace its usages and delete it, or
  (c) move it physically into `Domain/Actions`. Decided after the pilot
  (§12, decision D-4).

---

## 8. Relation to `PrefilledData`

Today, `SuggestedAction.PrefilledData` (a `Dictionary<string, object?>`) is
used for three different purposes:

1. **Carrying initial context into a Handler** (e.g. `ProjectId` for
   `AssociateToExistingProject`).
2. **Carrying UI-returned answers back into a re-issued action** (e.g.
   `ConfirmAdvance` for `ApproveOrClose`).
3. **Lifecycle reporting payload** (`WorkflowInstanceId` lookups in
   `BuildLifecycleContext`).

In the typed model:

- **Purpose (1) stays as is.** It is a fine carrier for *inbound* hints.
- **Purpose (2) is replaced.** UI answers travel as a typed
  `IActionContinuationResult`. The application service decides whether to
  re-dispatch the same `ActionCode` with the typed result attached, or to
  call a different service directly.
- **Purpose (3) stays as is.** Lifecycle context can still read scalar keys
  for telemetry without needing the typed result.
- **No `PrefilledData` removal is in scope for this design.** It can be
  deprecated later, action-by-action, as each `RequiresUI` flow is migrated.

---

## 9. Pilot recommendation

Three candidates were considered. Comparison:

| Candidate | Why a good pilot | Why it might be a poor pilot |
|---|---|---|
| `CloseOpinion` | Has a real gap (registry says `CanAdvanceWorkflow=true`, no backend). Forces the design to handle the **workflow-advance** continuation path. Isolated: only one suggested action, narrow blast radius. | Workflow advance is the most complex continuation; risk of over-designing for one action. |
| `CreateTask` | Already has a handler. Forces the design to handle the **task-creation** continuation, which is the most common `RequiresUI` payload in the inventory (≈14 of 27 `RequiresUiOnly` actions resolve to `TaskCreationDialog`). | Couples the pilot to **Phase 2** (unified task creation). If Phase 2 changes the factory shape, the pilot has to change with it. |
| `ApproveOrClose` | Already has a handler **and** an informal continuation contract (`ConfirmAdvance`). Replacing an existing informal contract with a typed one is the cleanest "before/after" demo. Forces the design to handle the **workflow-advance** continuation. | Highest user-visible risk: this is on the happy path of the Review workflow. A bug here is felt immediately. |

**Recommendation: pilot on `ApproveOrClose`.**

Rationale:

1. It is the only candidate where **a handler already exists**, so the pilot
   is purely about replacing the *contract*, not about adding a new code path.
2. The existing `PrefilledData["WorkflowInstanceId"]` + `PrefilledData["ConfirmAdvance"]`
   pair is the clearest example of an unsafe dictionary contract; replacing
   it with `WorkflowAdvanceContinuationRequest` / `Result` gives an
   unambiguous, demoable improvement.
3. It exercises the **hardest** continuation type (workflow advance) once,
   so the remaining 26 `RequiresUiOnly` migrations have an easier reference
   implementation to follow.
4. It is decoupled from Phase 2 (does not create or close tasks) and
   decoupled from Phase 3 (does not touch `MoveToProject`).

`CloseOpinion` is the **second pilot** after `ApproveOrClose`, because both
share the `WorkflowAdvanceDialog` continuation and the second one validates
that the first design generalised correctly.

`CreateTask` should be migrated **after Phase 2** completes, so the typed
`TaskCreationContinuationResult` lands on the unified task factory rather
than on the legacy `db.ProjectAssignments.Add` sites.

---

## 10. Migration strategy

Strict additive migration:

1. **Pilot only.** `ApproveOrClose` is converted to use the typed
   continuation pair. All other 26 `RequiresUiOnly` flows continue to use
   `ActionResult.RequiresUI(...)` exactly as today.
2. **Adapter layer.** A bidirectional adapter is added between
   `ActionFollowUp` and `ContinuationUiKind` so the UI continuation host
   can serve both worlds in parallel. The adapter lives at the application
   service layer, not in `Domain/Actions`.
3. **Service-owned loop.** Only application services hold the continuation
   loop. ViewModels keep their existing `ActionExecutor.ExecuteAsync(...)`
   call shape; the typed loop is invisible to them until the pilot service
   replaces a specific VM call.
4. **One action per change.** Each subsequent `RequiresUiOnly` action is
   migrated in its own change. After each migration the legacy
   `RequiresUI(...)` branch for that action is **left in place** until a
   second change deletes it (two-step retirement).
5. **Telemetry preserved.** Lifecycle reporting continues to emit Started /
   Completed / Failed / Cancelled / Deferred / NoOp at the same points; the
   continuation does not introduce new lifecycle states.

No mass conversion. No big-bang. No deletion of legacy paths until after the
last action in a family has been migrated.

---

## 11. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **Over-engineering.** Introducing 7 continuation pairs for a system that today uses 1 dictionary. | Medium | Pilot first with `ApproveOrClose` only. Do not introduce continuation types that are not yet needed by the pilot. |
| **Service / Handler duplication.** Continuation logic could end up split between the application service and the handler. | Medium | Single rule: handlers never re-enter the UI; services own the loop. Documented in §4. |
| **UI regressions.** Replacing the dictionary-based re-issue with typed results can break edge-cases where the dialog returns nothing or the user clicks the X. | Medium | `ActionContinuationOutcome.Cancelled` is mandatory; pilot tests exercise it explicitly. |
| **Mixed contract during migration.** Some flows are typed, some still `PrefilledData`. | High (by design) | Adapter layer (§10 step 2) plus per-action migration order so the UI host always handles both forms. |
| **Test coverage gap.** Handlers become testable, but the application services that own the loop are new ground. | Medium | Each pilot ships with at least two service tests: happy path and Cancelled path. |
| **`ConfirmAdvance` regressions.** The current `ApproveOrClose` flow is on the Review happy path. | High | Pilot ships behind a feature flag (DI swap), with the legacy `PrefilledData` path preserved and selectable. |
| **Scope creep into `ActionFollowUp` / `PrefilledData` removal.** | Medium | This document forbids both. Removal is decided per §12 after the pilot. |

---

## 12. Open decisions

To be answered **after** this document is reviewed, before any code change.

| Id | Decision | Default lean |
|---|---|---|
| D-1 | Is the continuation loop owned **only** by application services, or may any caller own it? | Application services only. |
| D-2 | May any `IProcessActionHandler` return a `IActionContinuationRequest`, or only handlers explicitly marked `RequiresUi=true` in `ActionDefinitionRegistry`? | Only handlers whose registry entry has `RequiresUi=true`. |
| D-3 | Does the continuation result return to the **same** application service that started the loop, or to a shared `ContinuationRouter`? | Same service. A shared router is added only when more than one service needs it. |
| D-4 | What happens to `ActionFollowUp`? Alias of `ContinuationUiKind`, replaced and deleted, or physically moved to `Domain/Actions`? | Alias for one release, then delete. Physical move not pursued. |
| D-5 | Which is the **first** pilot — `ApproveOrClose`, `CloseOpinion`, or `CreateTask`? | `ApproveOrClose` (this document's recommendation). |
| D-6 | Does `TaskCreationContinuationResult` carry a `CreatedTaskId` (dialog-creates) or a `TaskDraft` (service-creates)? | `TaskDraft` only. Aligns with Phase 2. |
| D-7 | How is `ActionContinuationOutcome.Cancelled` reported in lifecycle? Re-use `ReportCancelledAsync`, or a new lifecycle status? | Re-use `ReportCancelledAsync`. |
| D-8 | Where do `IActionContinuationRequest` / `IActionContinuationResult` live — `Domain.Actions` or a new `Domain.Actions.Continuation` namespace? | New `Domain.Actions.Continuation` namespace. |

---

## 13. Impact on phases

| Phase | Status after this design | Reason |
|---|---|---|
| **Phase 2 — Unify task creation** | **Closed without code changes** after Phase 2 reconnaissance. The three originally-named sites (`CreateReviewChildProjectTaskAsync`, `CreateAuthorityInvitationTrackingTaskAsync`, `TaskPanelViewModel.CreateTask`) already comply with the canonical task-creation rule. See [`Action-Handler-Inventory.md`](./Action-Handler-Inventory.md) §6 and §8. | The two `ActionExecutor` methods already call `TaskFactory.CreateAsync`; `TaskPanelViewModel` calls `TaskService`, not the `DbContext`. |
| **Phase 2B — `TaskService` / `TaskFactory` unification** | **Deferred.** Not behavior-neutral. Decisions are open per [`Action-Handler-Inventory.md`](./Action-Handler-Inventory.md) §7.3. | `TaskService.GetOrCreate*` duplicates priority insertion and event logging instead of delegating to `TaskFactory.CreateAsync`, but the divergences (idempotency, title generation, note content, Local vs UTC timestamps, status validation, unassigned-task priority insertion) require explicit decisions before any rewrite. |
| **Phase 3 — Unify `MoveToProject`** | **Not blocked.** `MoveToProject` does not use `RequiresUI` today (the VM calls a dialog directly). Phase 3 can later adopt the typed continuation for its project picker, but it does not require it. | Phase 3 is a duplicate-path decision, not a contract decision. |
| **Phase 5 — Migrate `RequiresUI` actions** | **Now well-defined.** Phase 5 becomes "one action at a time using the typed continuation contract", starting with the pilot. | The whole point of this document. |
| Phase 4 (coordinator bypasses) and Phase 6 (`ActionFollowUp` move) | **Unchanged.** Phase 4 is orthogonal. Phase 6 may collapse into D-4 of this document and disappear. | Each is independent. |

**Net effect:** Phase 2 closed with no code changes; Phase 2B deferred;
Phase 5 now has a concrete plan; Phase 6 may merge into the typed-continuation
rollout.

> **Note on D-6 (`TaskDraft`).** The default lean of D-6 (the eventual
> `TaskCreationContinuationResult` carries a `TaskDraft` rather than a
> `CreatedTaskId`) is unchanged. However, **no `TaskDraft` type is introduced
> as part of the Phase 2 closure** — it remains a Phase 5 / Phase 2B concern
> and will be designed when one of those phases actually starts.

---

## 14. Phase 5 pilot — implementation note

The pilot is implemented as of this revision and covers exactly two action codes:

- `ApproveOrClose` (`IApproveOrCloseApplicationService`)
- `CloseOpinion` (`ICloseOpinionApplicationService`)

### Wiring

1. Handler (`ApproveOrCloseProcessActionHandler` / `CloseOpinionProcessActionHandler`)
   returns `Deferred` and places a typed `WorkflowAdvanceContinuationRequest`
   in `ProcessActionResult.Data["ContinuationRequest"]`.
2. Application service `StartAsync(...)` extracts the typed request and
   surfaces it on the outcome as `NeedsUserInput`.
3. `EmailContextViewModel` calls `IActionContinuationUiHost.RequestAsync(...)`.
4. `WpfActionContinuationUiHost` (in `SiNetProjectManagerV2.Services`) shows a
   WPF confirmation dialog for `ContinuationUiKind.WorkflowAdvanceDialog` and
   returns a `WorkflowAdvanceContinuationResult` (`Confirmed` / `Cancelled` /
   `Failed`).
5. Application service `ContinueAsync(...)` consumes the typed result and
   produces the final outcome (`Completed` / `Cancelled` / `Failed`) plus
   lifecycle events.

### Scope guardrails (still in force)

- No DB schema or migration changes.
- Legacy `PrefilledData["ConfirmAdvance"]` bridge **not** removed.
- Only `WorkflowAdvanceDialog` is supported by the WPF host. Other
  `RequiresUI` action families (`CreateTask`, `FileImport`, project picker,
  etc.) continue to use the legacy `ActionFollowUp` / `PrefilledData` path
  until they are migrated one at a time.
- Handlers stay UI-free; the application service owns the continuation loop.

### Tests

- `SiNetSQL.Tests.Services.ApproveOrClose.ApproveOrCloseTypedContinuationTests`.
- `SiNetSQL.Tests.Services.CloseOpinion.CloseOpinionTypedContinuationTests`.
- `SiNetSQL.Tests.Services.Continuation.ContinuationUiHostLoopTests` exercises
  the full start → host → continue loop with a fake `IActionContinuationUiHost`
  so the UI host contract can be regression-tested without WPF.

### Manual WPF check (not automated)

1. Open an email tied to a project with an active workflow in a stage that
   exposes `ApproveOrClose` (or `CloseOpinion`).
2. Select the action from the email context UI.
3. The new typed path opens a confirmation prompt via
   `WpfActionContinuationUiHost`.
4. Confirm → workflow advances and lifecycle reports `Completed`.
5. Cancel → workflow does not advance and lifecycle reports `Cancelled`.
6. Trigger the same action via a code path that still sets
   `PrefilledData["ConfirmAdvance"] = true` to verify the legacy bridge keeps
   working unchanged.


---

## 15. Phase 5 expansion — migrated families to date

Beyond the WorkflowAdvance pilot, three additional `RequiresUI` families have
been migrated to the typed continuation pipeline. The pattern is identical for
all of them:

```
ViewModel
  └─ I<Family>ContinuationApplicationService.StartAsync(...)        // typed request
       └─ IActionContinuationUiHost.RequestAsync(...)               // WPF dialog
            └─ I<Family>ContinuationApplicationService.ContinueAsync(...)
                 └─ dispatch / re-dispatch through ActionExecutor / IProcessActionDispatcher
```

For every migrated family, **no legacy fallback remains**: if a migrated
`SuggestedActionType` reaches `ActionExecutor` directly, the executor returns
`ActionResult.Failed(...)` with a Hebrew error message instead of returning
the legacy `RequiresUI(ActionFollowUp.X)` follow-up. The catalogs are the
single source of truth for which action codes are on the typed path.

### 15.1 TaskCreation family

- Service: `ITaskCreationContinuationApplicationService` /
  `TaskCreationContinuationApplicationService`.
- Request/result: `TaskCreationContinuationRequest` /
  `TaskCreationContinuationResult` (carries `TaskDraft`).
- WPF dialog: `TaskCreationDraftDialog` (real draft-only dialog, no DB writes).
- Catalog: `TaskCreationActionCatalog.MigratedActions` (17 action codes).
- Executor enforcement: `ActionExecutor.TaskCreationTypedRequired(...)`.

### 15.2 FileImport family

- Service: `IFileImportContinuationApplicationService` /
  `FileImportContinuationApplicationService` — owns the loop and dispatches
  one `AddMaterialToProject` per selected file through
  `IProcessActionDispatcher` → `AddMaterialToProjectProcessActionHandler` →
  `IProjectFileFilingService`.
- Request/result: `FileImportContinuationRequest` /
  `FileImportContinuationResult` (carries `FileImportDraft` with per-file
  `FileImportSelection`s).
- WPF dialog: `FileImportDraftDialog` (draft-only mode).
- Catalog: `FileImportActionCatalog.MigratedActions` (`UploadNewVersion`,
  `ReceiveSupplementaryMaterial`, `ReceiveMaterialForReview`,
  `ReceiveCorrectedVersion`, `ReceiveMaterialForOpinion`). `CollectMaterial`
  and `AddMaterialToProject` are intentionally **not** migrated.
- Executor enforcement: `ActionExecutor.FileImportTypedRequired(...)`.

### 15.3 ProjectPicker family

- Service: `IProjectPickerContinuationApplicationService` /
  `ProjectPickerContinuationApplicationService` — emits a typed request and,
  on confirmed selection, **re-dispatches the original `SuggestedAction`**
  through `ActionExecutor.ExecuteAsync` with `PrefilledData["ProjectId"]`
  populated.
- Request/result: `ProjectPickerContinuationRequest` /
  `ProjectPickerContinuationResult` (carries `SelectedProjectId`).
- WPF dialog: **reuses the existing `ProjectSelectorDialog` unchanged** —
  there is **no UI redesign**. `WpfActionContinuationUiHost` adapts the
  existing dialog into a `ContinuationUiKind.ProjectPicker` branch.
- Catalog: `ProjectPickerActionCatalog.MigratedActions`
  (`AssociateToExistingProject`, `StartWorkflow`, `CreateNewReview`,
  `CreateOpinionProject`, `OpenReviewRound`). `CreatePriceQuote` (uses the
  configured Office-Management project, never the picker) and
  `CreateNewProject` (uses `NewProjectDialog`, not the picker) are **not**
  in scope.
- Executor enforcement: `ActionExecutor.ProjectPickerTypedRequired(...)`
  replaces every previous `ActionResult.RequiresUI("...",
  ActionFollowUp.ProjectPicker)` call site for migrated codes
  (`AssociateToExistingProject`, `CreateNewReview`, `StartWorkflow`,
  `StartWorkflowFromActionAsync` for `CreateOpinionProject` /
  `OpenReviewRound`).
- VM routing: `EmailContextViewModel.RunProjectPickerTypedAsync(...)` runs
  the typed path **before** any legacy execute call. The legacy
  `ActionFollowUp.ProjectPicker` retry branch in `ExecuteSelectedActionAsync`
  now refuses migrated actions and only services legacy callers.

### 15.4 NewProject family

- Service: `INewProjectContinuationApplicationService` /
  `NewProjectContinuationApplicationService` — emits a typed request and,
  on confirmed creation, **validates the `CreatedProjectId`** returned by the
  host. The service itself performs **no persistence and no re-dispatch**.
- Request/result: `NewProjectContinuationRequest` (with optional
  `SuggestedName` / `SuggestedCompanyId` / `SuggestedPlaceId` /
  `SuggestedProjectTypeCode` pulled from `PrefilledData`) /
  `NewProjectContinuationResult` (carries `CreatedProjectId` and
  `CreatedProjectName`).
- WPF dialog: **reuses the existing `CreateProjectUserControl` unchanged** —
  there is **no UI redesign**. `WpfActionContinuationUiHost` hosts the
  UserControl inside a modal `Window`, resolves `CreateProjectViewModel`
  via `DataContext`, and subscribes to `CreateProjectViewModel.ProjectCreated`
  to capture the new project id/name. The dialog still owns persistence,
  email-link, and workflow-advance side effects internally — the typed
  result only reports the lifecycle outcome and the
  `CreatedProjectId`. A `ProjectDraft` path would require a redesign of
  `CreateProjectViewModel` and is out of scope for this batch.
- Catalog: `NewProjectActionCatalog.MigratedActions` (only
  `CreateNewProject`).
- Executor enforcement: `ActionExecutor.NewProjectTypedRequired(...)`
  replaces the previous `ActionResult.RequiresUI("יצירת פרויקט חדש",
  ActionFollowUp.NewProjectDialog)` call site for `CreateNewProject`.
- VM routing: `EmailContextViewModel.RunNewProjectTypedAsync(...)` runs
  the typed path before any legacy execute call.

### 15.5 Still on the legacy `ActionFollowUp` path

The following `RequiresUI` families remain on the legacy
`ActionFollowUp` / `PrefilledData` path and have **not** been migrated yet:

| Family | Legacy follow-up | Triggering actions |
| --- | --- | --- |
| **DecisionDialog** | `ActionFollowUp.DecisionDialog` | `ForwardToDecision` |
| **DisciplineDialog** | `ActionFollowUp.DisciplineDialog` | `AddNewDiscipline` |

Each remaining family will follow the same template (catalog →
request/result DTOs → application service → WPF host adapter → executor
no-fallback helper → VM routing → tests + docs).

### 15.6 Tests for the new families

- `SiNetSQL.Tests.Services.Continuation.TaskCreationContinuationTests`
- `SiNetSQL.Tests.Services.Continuation.MigratedTaskCreationTypedRoutingTests`
- `SiNetSQL.Tests.Services.EmailContext.MigratedTaskCreationNoFallbackTests`
- `SiNetSQL.Tests.Services.Continuation.FileImportContinuationTests`
- `SiNetSQL.Tests.Services.EmailContext.MigratedFileImportNoFallbackTests`
- `SiNetSQL.Tests.Services.Continuation.ProjectPickerContinuationTests`
- `SiNetSQL.Tests.Services.EmailContext.MigratedProjectPickerNoFallbackTests`
- `SiNetSQL.Tests.Services.Continuation.NewProjectContinuationTests`
- `SiNetSQL.Tests.Services.EmailContext.MigratedNewProjectNoFallbackTests`

### 15.7 Manual WPF check — ProjectPicker (not automated)

1. Open an email that does **not** yet have a project association.
2. Select `AssociateToExistingProject` (or any other migrated ProjectPicker
   action: `StartWorkflow`, `CreateNewReview`, `CreateOpinionProject`,
   `OpenReviewRound`).
3. Confirm that the existing `ProjectSelectorDialog` opens through the typed
   host (it is the **same** dialog as before — no UI changes).
4. Pick a project → action re-dispatches with `ProjectId` set and completes
   the appropriate downstream effect (link to project / start workflow /
   create OpenReviewProject task).
5. Cancel the dialog → the VM surfaces a Hebrew "בוטל" message and does **not**
   re-issue the legacy `ActionFollowUp.ProjectPicker` retry.
6. As a sanity check, trigger one of the migrated actions through a code path
   that bypasses the VM (e.g. directly calling `ActionExecutor.ExecuteAsync`
   from a test or script) without a `ProjectId` → expect
   `ActionResult.Failed(...)` with the typed-required message, **not**
   `RequiresUI(ActionFollowUp.ProjectPicker)`.

### 15.8 Manual WPF check — NewProject (not automated)

1. Open an email and trigger `CreateNewProject`.
2. Confirm that the existing project-creation UI
   (`CreateProjectUserControl`) opens **inside a modal `Window`** through the
   typed host — the form itself is unchanged.
3. Fill in the form and click OK. `CreateProjectViewModel.SaveChanges()` runs
   as before (persistence, email-link, workflow-advance, `ProjectCreated`
   event); the host captures `ProjectCreated` and closes the modal.
4. The VM surfaces a Hebrew success message with the new project id
   (e.g. `✅ הפרויקט נוצר (מזהה …)`).
5. Cancel / close the dialog without creating → the VM surfaces a Hebrew
   "בוטל" message and does **not** re-issue the legacy
   `ActionFollowUp.NewProjectDialog` retry.
6. As a sanity check, trigger `CreateNewProject` through a code path that
   bypasses the VM (e.g. directly calling `ActionExecutor.ExecuteAsync`
   from a test or script) → expect `ActionResult.Failed(...)` with the
   `NewProjectTypedRequired` message, **not**
   `RequiresUI(ActionFollowUp.NewProjectDialog)`.

### 15.9 Manual WPF check — DecisionDialog / DisciplineDialog (not automated)

DecisionDialog (`ForwardToDecision`)

1. Open an email and trigger `ForwardToDecision`.
2. The typed host opens the existing `ProjectDecisionsWindow` modally — the
   window itself is unchanged and continues to persist decisions internally
   via `ProjectDecisionService` / `ProjectDecisionsViewModel`.
3. Add / edit decisions and close the window. The VM surfaces a Hebrew
   success message (e.g. `✅ החלטות הפרויקט עודכנו.`).
4. Cancel / close without changes → the VM surfaces a Hebrew "בוטל" message
   and does **not** re-issue the legacy `ActionFollowUp.DecisionDialog`
   retry.
5. As a sanity check, trigger `ForwardToDecision` through a code path that
   bypasses the VM (e.g. directly calling `ActionExecutor.ExecuteAsync`
   from a test or script) → expect `ActionResult.Failed(...)` with the
   `DecisionTypedRequired` message, **not**
   `RequiresUI(ActionFollowUp.DecisionDialog)`.

DisciplineDialog (`AddNewDiscipline`)

1. Open an email and trigger `AddNewDiscipline`.
2. There is **no dedicated WPF surface today**. The typed host shows a
   confirmation prompt mirroring the legacy default fall-through.
3. Confirm → the VM surfaces a Hebrew success message
   (e.g. `✅ בקשת הוספת תחום אושרה.`).
4. Cancel → the VM surfaces a Hebrew "בוטל" message and does **not**
   re-issue the legacy `ActionFollowUp.DisciplineDialog` retry.
5. As a sanity check, trigger `AddNewDiscipline` through a code path that
   bypasses the VM → expect `ActionResult.Failed(...)` with the
   `DisciplineTypedRequired` message, **not**
   `RequiresUI(ActionFollowUp.DisciplineDialog)`.

