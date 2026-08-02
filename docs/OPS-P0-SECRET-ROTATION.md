# OPS — P0 Secret Rotation Checklist (MasterPlan API Key)

Operational checklist for rotating the MasterPlan Web API key. **Do not record actual key values in this document or in Git.**

## Status — `Manual Pending` (as of 2026-08-02)

> Cutover blocker: rotation must be performed by a MasterPlan admin / server operator.
> Engineering prepared this checklist; the agent cannot rotate the live key.

| Item | Status | Notes |
| --- | --- | --- |
| Key removed from tracked files at HEAD | Done | Enforced on every CI run by `build/secret-scan.ps1` |
| Key loaded from vault / `MASTERPLAN_API_KEY` only | Done | `appsettings.json` fallback was removed from `MasterPlanApiClient` |
| Key fingerprints removed from central logs | Done | See "Logging policy" below |
| **Rotation performed** | **Not done** | Requires MasterPlan admin action |
| **Old key revoked** | **Not done** | Requires MasterPlan admin action |
| Git history rewrite | **Will not be done** | Explicit owner decision, 2026-07-28 (see below) |

Until rotation and revocation are actually performed and recorded in the log at the bottom of this
document, audit finding #2 stays **open**. Removing the value from HEAD is containment, not
remediation.

### Git history decision (2026-07-28)

The owner decided **not** to rewrite history: no `git filter-repo`, no BFG, no force-push, no
deletion of commits. The commit that contained the key therefore remains reachable in the
repository. The compensating controls are:

1. the key must be rotated and the old value revoked (still pending, above);
2. `build/secret-scan.ps1` runs in CI so the value cannot be reintroduced at HEAD;
3. the value is loaded only from the credential vault or an environment variable.

### Logging policy

Key **fingerprints** (length, SHA-256 prefix) are no longer written to the central log. They were
removed from `SiOffice.AccService/Program.cs`, `SiNetProjectManagerV2/App.xaml.cs`,
`RemoteAccProjectProvisioningService`, `RemoteAccInboxProvisioner`, and the `/v1/acc/diag` response.
Logs now record presence (`hasApiKey`) and source (`keySource`) only.

One deliberate exception remains: the ACC control-plane **status screen** still shows key length and
a hash prefix through `IAccServiceKeyDiagnostics`. That surface is operator-initiated, on-screen, and
not persisted to the central log; it is the tool used to diagnose client/server key mismatches. It
is kept intentionally and is not a logging path.

## Scope

| Item | Value |
|---|---|
| Vault key | `SiNet/MasterPlanApi/ApiKey` |
| Env var (server fallback) | `MASTERPLAN_API_KEY` |
| HTTP header | `X-API-Key` |
| Consumer | `MasterPlan.SyncEngine` (Task Scheduler on `SI-WIN-2K19`, user `SI-ENG\sieng`) |

## Pre-rotation

- [ ] Coordinate a maintenance window with whoever owns MasterPlan API access.
- [ ] Confirm the new key was issued by MasterPlan (vendor / admin portal).
- [ ] Verify Task Scheduler runs SyncEngine as `SI-ENG\sieng` (same user as vault import).
- [ ] Confirm `MasterPlan.SyncEngine/appsettings.json` on the server has **empty** `MasterPlanApi:ApiKey`.
- [ ] Export current `.secrets` backup from WPF (optional rollback).

## Rotation steps

### 1. Update vault on dev machine

1. Open WPF → `SecretSetupWindow`.
2. Paste the **new** MasterPlan API key → **Save** (green status).
3. Export updated `SiNet.secrets` if server import is needed.

### 2. Deploy to server (if using `.secrets` package)

```powershell
# On SI-WIN-2K19 as Administrator:
powershell -ExecutionPolicy Bypass -File "\\SI-WIN-2K19\AppFolder\AppNet\SiOffice.AccService\Install-OnServer.ps1" `
  -SecretsFile "\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNet.secrets" `
  -SkipService
```

Alternatively, set only the env var on the server (no WPF vault on that machine):

```powershell
[System.Environment]::SetEnvironmentVariable("MASTERPLAN_API_KEY", "<NEW_KEY>", "Machine")
```

Restart the scheduled task or wait for the next run after vault/env update.

### 3. Revoke old key

- [ ] Request revocation/disable of the **old** key in MasterPlan after successful verification.
- [ ] Remove old key from any local notes, chat logs, or temp files.

## Verification

- [ ] Run SyncEngine manually once on the server (or wait for scheduled run) — no `MasterPlan API key not found` in logs.
- [ ] Confirm HTTP 200 from a sample API call (check SyncEngine log / central log).
- [ ] Confirm `appsettings.json` on the deploy share still has empty `ApiKey`.
- [ ] Spot-check WPF SecretSetup: MasterPlan API Key status green.

## Rollback

If the new key fails:

1. Restore previous key in vault (re-import `.secrets` backup or re-enter in SecretSetupWindow).
2. Re-run Install-OnServer with `-SkipService`, or restore previous `MASTERPLAN_API_KEY` env var.
3. Re-enable old key with MasterPlan admin only if still valid.

## Post-rotation

- [ ] Document rotation date and operator (no key values).
- [ ] If a team member had the old key outside vault, confirm they received the new package or updated vault locally.
- [ ] Update the status table at the top of this document.

## Rotation log

| Date | Operator | Old key revoked? | Notes |
| --- | --- | --- | --- |
| _(empty)_ | | | No rotation has been performed yet. |
