# Controlled Production Pilot — runtime controls (P1)

> **Status:** Implemented (code) — **not** ops enablement  
> **Updated:** 2026-08-24  
> **Scope:** Fail-closed SystemSettings + root `StartAsync` gate + QuoteApproved pre-validation + automated P0 smoke tier

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

## P0 Live Smoke evidence (2026-08-17)

| Item | Result |
| --- | --- |
| Commit | `0ae3c906f8b591003d24869e8ee99ec6d37efe19` |
| Offline Release | Pass (3449) |
| Fail-closed / allowlisted Start / PRP→blocked-PLN / kill-switch live | **Not Run** — Environment Gate Blocked ([`ENVIRONMENTS.md`](./ENVIRONMENTS.md): DEV Gmail mailbox not provisioned; stop before production-impacting Gmail/ACC writes) |
| Replica `Pilot.Enabled` end-state | **Unchanged** (no SystemSettings writes this session) |
| Recommendation | **FIX BEFORE P3** until isolated DEV/replica session completes P0 steps 3–8 |

**Superseded by the automated tier below** (2026-08-24). The blocking reason no longer holds: the office shared mailbox is new and not yet in office use, so writing into its label tree is acceptable for a bounded window, and the DEV database is separate and restored from backup.

## Automated P0 Pilot smoke (`Category=PilotSmoke`)

> **Status:** Tier documented — implementation and first run pending  
> **Layer:** L4W in [`TEST_STRATEGY.md`](./TEST_STRATEGY.md) §4W (gates, ACC guard, cleanup)

The P1 controls above are proven by an ordered, agent-runnable scenario rather than a manual walk. Each proof maps to a specific control on this page:

| Step | Proves |
| --- | --- |
| S1 | Fail-closed: absent/false `Pilot.Enabled` and empty allowlists reject a root Start with no instance, task or partial mutation |
| S2 | Allowlisted user + `Proposal` Start succeeds |
| S3, S4 | Non-allowlisted `UserId` and non-allowlisted `WorkflowDefinition.Code` are both rejected |
| S6 | PRP corridor advances only through the email action and `ITaskCompletionService` — never Dashboard mutation, never Ops Advance |
| S7 | `ValidateBeforeQuoteApprovalAsync` blocks `QuoteApprovedByClient` while `PlanningWorkflow` is outside `Pilot.AllowedWorkflowCodes`, with the FollowQuote task left open and no PLN root instance |
| S8 | Kill-switch: flipping `Pilot.Enabled` to `false` blocks new root Starts immediately while an existing instance still completes its open task — no restart required |

Enforced constraints during the run:

- `Pilot.AllowedWorkflowCodes` is set to `Proposal` only. `PlanningWorkflow` staying out is exactly what makes S7 a real proof rather than a tautology.
- Pilot rows are written by direct EF upsert on `SystemSettings`, so no authenticated admin identity is needed, while the **read** path under test remains `SqlSystemSettingsService` — which has no DTO cache, so the kill-switch takes effect on the next read.
- The three `Pilot.*` values are snapshotted and restored in `finally`, and the report asserts a fresh read shows `Pilot.Enabled=false`.

Not covered by this tier: Ops **Advance**, PLN/REV seed edits, and production allowlist enablement — the same exclusions listed under "Explicitly out of this control".

## Live evidence (automated tier)

> **Latest corridor run:** 2026-08-24 on DEV (`danny\SQLEXPRESS` / `SiData`, operator `SIUser.Id=12`, Gmail corridor via `CreatePriceQuote`).  
> **Evidence file:** `%LOCALAPPDATA%\SiNet\pilot-smoke\p0-pilot-smoke-20260824-154135.md`

| Step | Result | Notes |
| --- | --- | --- |
| P1–P8 Preconditions | **Pass** | Place `SI` id=1315; PLN mapping on project type 17; seed baseline complete |
| S0 Smoke project | **Pass** | Project id=3157 `[P0-SMOKE] 0824-1541` |
| S1 Fail-closed | **Pass** | No instance/task mutation |
| S2 Narrow enable | **Pass** | `Pilot.AllowedWorkflowCodes=Proposal` only |
| S3 Allowlisted CreatePriceQuote | **Pass** | PRP instance id=4 at `PRP.ProjectSetup` via email action (`IsProjectBound=false`); inbox id=6 materialized from Gmail |
| S4 Non-allowlisted user | **Pass** | |
| S5 Non-allowlisted code (PLN) | **Pass** | |
| S6 PRP corridor | **Pass** | Reached `FollowQuoteApproval` (task id=32) after 7 completions through production seams (`OpenQuoteProject` + `FileQuoteMaterial` via corridor helpers) |
| S7a QuoteApproved pre-validation | **Pass** | `PlanningWorkflow` blocked by Pilot allowlist |
| S7b QuoteApprovedByClient completion | **Pass** | Refused before mutation — PLN continuation blocked; task 32 still open |
| S8a Kill-switch | **Pass** | New root Start blocked immediately after `Pilot.Enabled=false` |
| S8b Existing instance advances | **Not Run** | By design — only open task is `FollowQuoteApproval`, whose advancing result is covered by S7 |
| Gmail G1–G2 | **Pass** | 2026-08-24: silent restore, message located, label round-trip + unfile |
| ACC A1–A7 | **Pass** | 2026-08-24: disposable inbox `SI-SMOKE-INBOX`, ingest, tag, MoveToProject, mapping `SI-SI` — evidence `p0-pilot-smoke-20260824-150442.md` |
| `Pilot.Enabled` after restore | **Pass** | Fresh read = `false` |

**Recommendation:** **READY for L4W P0 sign-off** on SQL/Pilot controls (S1–S8a, S7b) and Gmail/ACC layers (G1–G2, A1–A7). S8b remains intentionally **Not Run** when the corridor stops at `FollowQuoteApproval`. Re-run Gmail+ACC tier separately from the SQL corridor test (parallel xUnit can collide on evidence file timestamps).

Rollout policy is unchanged by this tier: a green run raises confidence in the P1 safety controls, it does not by itself authorise production Pilot enablement, which stays an ops decision.

See also: [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md), [`ROLLOUT_SINET_APP_WPF.md`](./ROLLOUT_SINET_APP_WPF.md), [`manual-tests/STANDALONE_PILOT_SMOKE.md`](./manual-tests/STANDALONE_PILOT_SMOKE.md), [`TEST_STRATEGY.md`](./TEST_STRATEGY.md) §4W.
