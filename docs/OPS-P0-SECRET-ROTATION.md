# OPS — P0 Secret Rotation Checklist (MasterPlan API Key)

Operational checklist for rotating the MasterPlan Web API key. **Do not record actual key values in this document or in Git.**

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
