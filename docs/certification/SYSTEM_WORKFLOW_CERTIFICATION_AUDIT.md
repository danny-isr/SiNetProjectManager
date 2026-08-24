# Full System Workflow Certification — Phase 1 Audit

> **Status:** Audit complete — awaiting approval to build the harness (Phase 2)
> **Commit audited:** `a6bbb99555e710a40c94409dfd01c516086389d6` (`development`; HEAD confirmed unchanged at audit time)
> **Scope:** Read-only audit. No code, seed, schema or UI was changed.
> **Related:** [`../PILOT_CONTROLS.md`](../PILOT_CONTROLS.md), [`../TEST_STRATEGY.md`](../TEST_STRATEGY.md) §4W, [`../manual-tests/STANDALONE_WORKFLOW_PRODUCTION_GATE.md`](../manual-tests/STANDALONE_WORKFLOW_PRODUCTION_GATE.md)

This document is the Phase 1 deliverable: what exists today, what the certification tier must prove, and
every gap that blocks an end-to-end proof. It does **not** claim any certification result.

The existing L4W `Category=PilotSmoke` tier is **retained unchanged** as the fast smoke. Certification is a
new, separate tier.

---

## 1. What exists today

### 1.1 Seeded workflow definitions — 6, not 7

Source of truth is the seed baseline catalog, and it lists exactly six required codes:

| # | Code | Hebrew name | Entry stage | Terminal stage(s) |
| --- | --- | --- | --- | --- |
| 1 | `MaterialIntake` | תהליך קליטת חומר | `MAT.Receive` | `MAT.Complete` |
| 2 | `PlanningWorkflow` | תהליך תכנון פרויקט | `PLN.WorkOrder` | `PLN.Close` |
| 3 | `Review` | תהליך בדיקת תוכנית | `REV.AwaitingMunicipalityRequest` | `REV.Completed` |
| 4 | `Proposal` | תהליך הצעת מחיר | `PRP.Intake` | `PRP.Approved`, `PRP.Rejected` |
| 5 | `Opinion` | תהליך חוות דעת | `OPN.ReceiveMaterial` | `OPN.Close` |
| 6 | `Outsourcing` | תהליך מיקור חוץ | `OUT.ReceiveOffer` | `OUT.Complete` |

`SqlSeedBaselineVerifyService` already queries `WorkflowDefinitions` filtered by `IsActive` against this
list. **The certification tier must reuse this and must not hard-code a workflow list** — see §4.1.

### 1.2 Existing workflow test coverage — all non-live

Every file under `src/SiNet.App.Wpf.Tests/Workflow/` runs on EF InMemory or as a pure unit test. None runs
against SQL Server.

| File | Backing | Covers |
| --- | --- | --- |
| `WorkflowTaskIntegrityTests` | EF InMemory | delete/deactivate/reactivate guards; paused not stalled; no duplicate on sweep |
| `NativeWorkflowEngineTests` | EF InMemory | PRP start + auto-advance via `ITaskCompletionService` |
| `PilotStartGateTests` | EF InMemory | Pilot allowlist on root starts; child sub-workflow parent link |
| `ProposalSendQuoteStageTests` | EF InMemory | InternalApproval → SendQuote → FollowUp |
| `ProjectTypeContinuationStarterTests` | EF InMemory + fake commands | missing mapping fails; per-JobType starts; active track skipped |
| `WorkflowAssigneeReadinessEvaluateStageTests` | pure unit | assignee issue classification |
| `WorkflowOrphanTrackMarkersTests` | pure unit | `[ORPHAN-TRACK]` note handling |
| `WorkflowDebugTracePathTests` | pure unit | debug log path |

Consequence: no integrity rule is currently proven against a real database, and the SubWorkflow waiting
scenario (§2.1) is not covered anywhere.

### 1.3 Reusable production seams

These are the only write paths the certification tier may use. No new abstractions are needed.

| Purpose | Port | Registered by |
| --- | --- | --- |
| Start workflow | `IWorkflowCommandService.StartAsync` | `AddSiNetProcessBackbone()` |
| Complete task | `ITaskCompletionService.CompleteAsync` | `AddSiNetTaskServices()` |
| Auto-advance / reprovision | `IWorkflowCommandService.CheckAndAutoAdvance*`, `ReprovisionStalledStageTasksAsync` | `AddSiNetProcessBackbone()` |
| Query instances / stages / progress | `IWorkflowQueryService` | `AddSiNetWorkflowReads()` |
| Stall detect + recover | `IWorkflowRecoveryService.DetectStalledAsync` / `AttemptRecoveryAsync` | `AddSiNetProcessBackbone()` |
| Assignee config readiness | `IWorkflowAssigneeReadinessQueryService.GetIssuesAsync` | `AddSiNetProcessBackbone()` |
| Continuations | `IProjectTypeContinuationStarter.StartContinuationsAsync` | `AddSiNetProcessBackbone()` |
| Email action execution | `IEmailSuggestedActionExecutionService.ExecuteAsync` | `AddSiNetEmailDetailSql()` |
| Task delete/deactivate guards | `ITaskWorkbenchService` | `AddSiNetTaskServices()` |

---

## 2. Defects and design holes found

### 2.1 DEFECT — the watchdog cannot distinguish "waiting for child" from "stalled"

**Severity: high.** This is the single most likely way a real workflow gets mishandled in production.

Three verified facts combine:

1. `WorkflowStatus` has exactly five values — `Draft`, `Active`, `Paused`, `Completed`, `Cancelled`.
   There is **no `Waiting`**. A parent waiting on a child therefore sits in `Active`.
2. `StalledWorkflowWatchdog.DetectStalledAsync` excludes only `IsFinal`, `NodeType == "End"` and
   `NodeType == "Start"`. `NodeType == "SubWorkflow"` is **not** excluded, and there is no check for an
   existing active child instance.
3. A SubWorkflow host stage deliberately has **no tasks** — `CreateStageTasksAsync` returns `[]` for it.

So a parent parked at `REV.MaterialIntake` or `PLN.Execution.MaterialCheck` while a legitimate child runs
is reported as stalled. Because the trigger-link query filters by instance and **not** by current stage,
both recovery branches are reachable:

| Parent history | Branch taken | Risk |
| --- | --- | --- |
| Has closed tasks from earlier stages (normal for REV/PLN, which pass ProjectSetup/WorkOrder first) | `CheckAndAutoAdvanceAsync` with a long-closed task | re-evaluates an already-fired transition while the child is still running |
| No trigger links at all | `ReprovisionStalledStageTasksAsync` returns 0 (same `return []`) → `NotifyOrphanAsync` | **false** "workflow orphaned — manual intervention needed" alert |

**Not yet determined:** whether the first branch actually starts a second child or advances the parent past
the SubWorkflow stage. A `subExists` guard exists in `EnsureInitialStageTasksAsync`, but it is not
necessarily on the path a transition action takes. This is a primary question for the live tier to answer
empirically — it must not be asserted either way from reading alone.

### 2.2 DEFECT — two email actions are offered to users and always fail

`BuildAssociated` offers `SetProjectStatus` **unconditionally** for every project-associated email, and
`RecordTaskResult` whenever `ActiveWorkflowCount > 0`. But the email service dispatches
`ActionExecutionCommand` with only `ActionCode`, `UserId` and `Data["InboxMessageId"]` — no
`WorkflowInstanceId`.

Both handlers begin with `command.WorkflowInstanceId ?? 0` and fail immediately:

- `SetProjectStatusProcessActionHandler` → `"ProjectStatusCode is required."`
- `RecordTaskResultProcessActionHandler` → `"WorkflowInstanceId is required."`

The user sees an untranslated technical error. This is a product gap, not a test gap.

### 2.3 GAP — ACC write results carry no ACC identifiers, so "success" is self-reported

None of the three result DTOs returns an ACC item, version or folder id:

| DTO | Fields | Verification value |
| --- | --- | --- |
| `EmailAttachmentTagResult` | `Succeeded`, `ErrorMessage` | none |
| `EmailAccUploadResult` | counts, `MessageUniqueId`, `InboxMessageId` | no ACC id |
| `EmailMoveToProjectResult` | counts only | no ACC id |

`EmailMoveToProjectResult.AllFilesTransferred` is derived purely from counts the service reported about
itself. Treating it as proof is circular. Read-back must therefore resolve the target folder independently
and list its contents.

Read interfaces are available in **both** Local and Remote modes (`IAccFolderBrowserService`,
`IAccFolderPathService`, `IAccDocumentService`, `IAccProjectTreeSearchService`, `IAccItemService`,
`IAccItemMetadataService`), so external read-back is achievable — it simply is not being done today.

### 2.4 GAP — the existing smoke verifies ACC internally only

In `P0PilotGmailAccLiveSmokeTests`, only the Gmail steps re-read from the external system (label
round-trip and message metadata). **ACC steps A4–A7 assert the service result and the SQL mirror only.**
No ACC step re-reads from ACC. The green ACC result recorded on 2026-08-24 therefore does not prove any
file physically exists at its destination.

### 2.5 GAP — evidence can be red while the test is green

`PilotSmokeEvidence.Fail`, `.Skipped` and `.NotRun` only append a row and rewrite the file. There is no
finalizer and no assertion. A run can end `Passed` with `Fail` rows in the report. In the 15:39 run the
test did fail — but only incidentally, via an `Assert.NotNull` at the call site, not because of the
evidence. `evidence.NotRun(...)` in `PilotSmokeCorridorSupport` returns `false` and fails nothing.

### 2.6 GAP — the DEV guard proves nothing about the target

`PilotSmokeEnvironment.TryResolveSqlTier` requires `SINET_PILOT_SMOKE_DB_CONFIRM` to equal the database
name parsed from `SINET_PILOT_SMOKE_SQL`. Both values come from the same operator in the same shell — it
proves the name was typed twice, not that the target is DEV. There is no server allowlist, no database
allowlist, and no marker inside the database.

Searches for `Environment=DEV`, `EnvironmentName`, `IsDevelopment` across `src` return **no matches**, and
none of the 40 keys in `SystemSettingKeys.AllManaged` describes an environment.

`SystemSettings` is a key/value table keyed by `SettingKey varchar(128)`, so a marker row is **data, not
schema** — no EF migration required.

---

## 3. Seed gaps that limit what can be certified

These decide whether a workflow is certifiable end-to-end at all.

| Workflow | Blocking gap | Effect on certification |
| --- | --- | --- |
| `Review` | `CreateNewReview` is not mapped in `TryResolveWorkflowStart`; `REV.Intake` not seeded | no real email-driven start |
| `Review` | not mapped in `ProjectTypeWorkflowDefinition` — JobTypes `בדיקת`/`בדיקה` route to `PlanningWorkflow` | no continuation into REV |
| `Review` | optional police stages declared, but no `ProjectTypeWorkflowStage` profile seeded for REV | optional-path activation unproven |
| `Outsourcing` | no TaskResult codes and no interaction-registry entries | transitions rely solely on `AllTasksComplete`; no branch exists to test |
| `PlanningWorkflow` | design/approval transitions are `Manual`/`Manual` with no TaskResult binding | advancing may need an interaction contract that does not exist |
| `PlanningWorkflow` | `ProjectTypeWorkflowStageSeedData` references removed `PLN.Quote.*` stages | stage profile may not match the active graph |
| `PlanningWorkflow` | `PLN.Close` has no stage task and no group | closure happens only via transition actions |

Email actions, by certifiability:

| Tier | Actions |
| --- | --- |
| End-to-end certifiable | `CreatePriceQuote`, `RejectPriceQuote`, `CreateOpinionProject` |
| Certifiable only via toolbar, not the suggested-action button | `AssociateToExistingProject`, `FileOnly` |
| Product gap — stub returns "עדיין לא מחוברת" | `CreateNewReview`, `RequestAuthorityInvitation`, `CollectMaterial`, `ForwardToDecision` |
| Handler exists but email dispatch is mis-wired | `RecordTaskResult`, `SetProjectStatus` (see §2.2); `SendNotification` logs only |

---

## 4. Design of the certification tier

### 4.1 Coverage denominator is computed at runtime, never documented

Even this audit could not be trusted on transition counts — the source inventory reported totals that
disagreed with its own tables. Therefore the harness **queries** `WorkflowDefinitions`, `WorkflowStageDefinitions`
and `WorkflowTransitionRules` from the target database and derives the denominator itself.

Rule: every active `WorkflowDefinition` must map to a certification scenario **or** to an explicit
`N/A` with a written reason. Anything unclassified **fails the run**. Adding a workflow to the seed without
a scenario therefore breaks the build, which is the requested property.

### 4.2 Integrity validator — what to reuse, what to write

Reuse: `IWorkflowRecoveryService`, `IWorkflowAssigneeReadinessQueryService`, and the preventive guards
already in the engine and provisioning service.

Must be written as live SQL audit assertions (no production equivalent exists):

1. Orphan `TaskLink` rows and dangling `WorkflowInstance` references.
2. `Completed`/`Cancelled` instances that still have open workflow-driving tasks.
3. Duplicate trigger links per `(instance, stageTag)`, and duplicate active tracks per
   `(Project, WorkflowDefinition, JobType)` — as **detection**, not only prevention.
4. Open workflow-driving tasks whose assignee is null, inactive, or unresolvable from the stage group —
   distinct from the existing stage-definition readiness check.
5. Parent/child integrity: child with missing parent; parent `Active` at a SubWorkflow stage whose child is
   terminal or missing.
6. Every active instance has a way forward: an open driving task, or an active child, or a recognised
   waiting state. Given §2.1 there is no waiting status, so "active child" must be evaluated explicitly.

### 4.3 Fail-closed DEV protection — three independent conditions (operator decision, 2026-08-24)

All three must hold before the first write. Failure of any one aborts before any mutation:

1. **DB marker** — a pre-existing `SystemSettings` row `Certification.Environment = DEV` inside the target
   database. **The harness never creates it**; a marker it could write proves nothing. The key is
   deliberately *not* added to `SystemSettingKeys.AllManaged`, so it never becomes an editable field in the
   settings UI.
2. **Server and database allowlist** — `SINET_SYSTEM_CERT_ALLOWED_SERVERS` and
   `SINET_SYSTEM_CERT_ALLOWED_DATABASES`, supplied separately from the connection string. Re-stating the
   connection string proves nothing.
3. **Windows identity allowlist** — `WindowsIdentity.GetCurrent().Name` must appear in
   `SINET_SYSTEM_CERT_ALLOWED_WINDOWS_USERS` (currently `AzureAD\dannyisrael`). Configurable by
   environment variable, never hard-coded in a test.

**Exact match only.** No partial matching such as `Contains("danny")`. This mirrors the existing SIUser
mechanism, which compares `LoginName` by equality in `PilotSmokeSeed.EnsureOperatorLoginAsync`; a
normalisation variant is acceptable only if that mechanism already supports it.

The Windows identity is **defence in depth, never proof of DEV on its own** — the same operator can
connect to a different database.

Additionally: exact-match Gmail account, exact-match ACC Place `SI`, an explicit certification write flag
separate from the smoke flags, and no vault fallback for the connection string under any condition.

**Skip versus fail.** When the tier is not switched on the run *skips*, so CI and the offline suite are
unaffected. When it *is* switched on but the target cannot be proven approved, the test still runs and
**fails** on the guard. A skip there would be indistinguishable from a clean run.

### 4.4 Evidence with a real gate

Result vocabulary becomes `PASS`, `FAIL`, `BLOCKED`, `N/A`, `OPTIONAL` — `Skipped` is removed as a way to
hide a failure. Each step declares up front whether it is required. `FinalizeCertification()` fails the test
if any required step is not `PASS`, and must be unavoidable rather than a courtesy call at the end.

### 4.5 Naming, isolation and cleanup

`[SYS-CERT]` prefix on every created entity. Evidence records ids for project, workflow instance, task,
inbox message, ACC project/folder/item and Gmail message. Settings changed are snapshotted including
whether the row existed at all, and restore is verified by re-read. The `PilotSmoke` xUnit collection
serialization introduced in `a6bbb99` is extended to certification so runs cannot collide.

---

## 5. Phase plan

| Phase | Content | Status |
| --- | --- | --- |
| 1 | Audit, coverage matrix, gaps (this document) | **Complete** |
| 2 | Harness, integrity validator, evidence gate, DEV protection | **Primitives complete; DI host outstanding** |
| 3 | PRP full incl. continuation into PLN | Not started |
| 4 | OPN full | Not started |
| 5 | MAT + PLN | Not started |
| 6 | REV | Not started |
| 7 | OUT | Not started |
| 8 | Email actions that are not workflow starts | Not started |
| 9 | Failure / retry / restart / watchdog scenarios | Not started |
| 10 | Full clean run + final report | Not started |

Known blockers to a `PASS` verdict, independent of harness quality: §2.1 (watchdog/SubWorkflow), §2.2
(mis-wired email actions), and the `Review` and `Outsourcing` seed gaps in §3. These are product gaps and
will be reported as `BLOCKED / PRODUCT GAP` rather than worked around.

### Operator decisions, 2026-08-24

| Question | Decision |
| --- | --- |
| DEV protection | Three independent conditions, per §4.3 |
| The two defects in §2.1 and §2.2 | **Prove first, then decide.** Write a certification scenario that demonstrates each on a real database; no product code changes in this round |
| `SendQuoteToClient` proof | **`BLOCKED BY POLICY`.** No real email is sent, and the internal `QuoteSendProofStore` is not accepted as a substitute for delivery |

### Phase 2 delivered so far

| File | Role |
| --- | --- |
| `Certification/SystemCertificationEnvironment.cs` | env-side guard: enabled flag, connection parsing, server/database/Windows-identity allowlists, Gmail and ACC layers |
| `Certification/SystemCertificationDatabaseMarker.cs` | read-only verification of the in-database DEV marker; no write path exists |
| `Certification/SystemCertificationEvidence.cs` | `PASS`/`FAIL`/`BLOCKED`/`N/A` + required-versus-optional, declared up front so an unreached step shows as `NOT RUN`; `FinalizeCertification()` throws |
| `Certification/SystemCertificationEvidenceTests.cs` | 8 offline tests proving the gate actually fails — including that a required step left unrun fails the run |
| `Certification/WorkflowCoverageInventory.cs` | derives the coverage denominator from the live graph; reports unclassified definitions |
| `Certification/SystemCertificationIntegrityValidator.cs` | global integrity checks with a pre-write baseline so only new violations fail |
| `Certification/SystemCertificationFactAttribute.cs` | `Category=SystemCertification`, separate from `Category=PilotSmoke` |
| `Certification/SystemCertificationTestCollection.cs` | serialises the tier |

Outstanding in Phase 2: the DI host that composes the production write ports for the scenarios.
