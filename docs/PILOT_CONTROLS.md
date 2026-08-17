# Controlled Production Pilot — runtime controls (P1)

> **Status:** Implemented (code) — **not** ops enablement  
> **Updated:** 2026-08-17  
> **Scope:** Fail-closed SystemSettings + root `StartAsync` gate + QuoteApproved pre-validation

## Purpose

Limit **new root** workflow starts to explicitly allowlisted users and workflow codes while a controlled internal pilot runs. Existing instances, task completion, and **child** starts (parent instance) are not killed by `Pilot.Enabled=false`.

## Settings (`dbo.SystemSettings`)

| Key | Absent / empty | Effect |
| --- | --- | --- |
| `Pilot.Enabled` | **false** | Block all new root starts |
| `Pilot.AllowedUserIds` | empty set | No user passes |
| `Pilot.AllowedWorkflowCodes` | empty set | No workflow code passes |

Keys are in `SystemSettingKeys` / `AllManaged`. Mapped on `WorkflowSystemSettingsDto`. Admin Settings UI round-trips Pilot fields without a dedicated editor (ops can set rows in SQL / future UI).

**Never** treat missing keys as unrestricted production starts.

## Gate location

**File:** `NativeWorkflowCommandService.StartAsync`  
**Policy:** `PilotStartPolicy` via `IPilotStartGate` / `SqlPilotStartGate`

Before orchestrator start: resolve `WorkflowDefinition.Code` from `command.DefinitionId`, then require:

`Pilot.Enabled ∧ UserId ∈ AllowedUserIds ∧ Code ∈ AllowedWorkflowCodes`

Deny → `WorkflowStartPreflightException` (Hebrew).

Covers Email, Ops Manual Start, System continuation starts that go through `IWorkflowCommandService.StartAsync`.

### Children

`WorkflowEngine.StartAsync(..., parentWorkflowInstanceId: …)` and `StartSubWorkflow` **do not** pass through the command-service gate. Cap / policy for children unchanged.

## QuoteApprovedByClient pre-validation

`SqlTaskCompletionService` calls `IProjectTypeContinuationStarter.ValidateBeforeQuoteApprovalAsync(projectId, command.UserId)` **before** mutating the FollowQuote task.

- Uses the **actual** completion `command.UserId` (not `actingUserId=0`).
- Same `PilotStartPolicy` / `IPilotStartGate.EvaluateAsync` as the Start gate.
- Only tracks that would need a **new** root start are checked (already Active/Paused tracks skipped).
- On deny: `TaskCompletionResultDto.Failure`, task stays open, no stage advance, no Start.

Post-commit `StartContinuationsAsync` remains defense-in-depth (gate on `StartAsync`).

## Explicitly out of this control

- Ops **Advance** / `ExecuteTransitionAsync` (known non-atomic risk — follow-up)
- PLN/REV seed edits / artificial Pilot Complete stages
- Production allowlist enablement (ops decision after review)

## Ops enablement (not done by P1)

1. Set `Pilot.Enabled=true`
2. CSV user ids and codes (e.g. `Proposal,Opinion`)
3. Live smoke on non-prod / replica before real pilot users

**Operational risk:** Save after a failed System Settings Load (before Pilot fields are applied) may write fail-closed defaults. Normal open→load→save preserves Pilot; automated regression covers the happy path. Not fixed in P1 production code.

See also: [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md), [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md).
