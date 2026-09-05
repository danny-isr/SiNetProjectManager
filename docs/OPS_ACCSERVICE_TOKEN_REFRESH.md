# AccService — refreshing the Autodesk 3-legged token (ops)

> **Title:** AccService Autodesk refresh-token refresh  
> **Date:** 05.09.2026 (token-store isolation)  
> **Status:** Active  
> **Scope:** How an operator restores Autodesk OAuth for `SiOffice.AccService`. AccService owns a **dedicated** Autodesk token store, independent of the SiNet desktop user-context token.

Related: [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md),
[`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md),
[`IDENTITY_SIUSER_GATE.md`](./IDENTITY_SIUSER_GATE.md),
[`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md),
[`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md),
[`SiOffice.AccService/README.md`](../SiOffice.AccService/README.md).

---

## Token ownership model (source of truth)

Two independent Autodesk 3-legged identities:

| Role | Purpose | Default physical store |
| --- | --- | --- |
| **Desktop / user-context** | Interactive SiNet Autodesk session | `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json` |
| **AccService Admin** | AccService administrative APIs (`AccBootstrapAdminEmail`) | `%LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json` |

**Isolation mechanism = token store path**, not Windows username and not browser profile.

- Same Windows user on DEV is allowed **because the two files are different**.
- Browser auto-login is **not** isolation — AuthOnce must still sign in as the designated Admin.
- Proof of health = AccService diagnostics report **its** `tokenStoragePath` + userinfo `ActualAdminEmail` vs `AccBootstrapAdminEmail`.

Do **not** copy the desktop generic token into the AccService store automatically — it may belong to Danny/Tair/another desktop user.

Code: `AutodeskTokenStoreOptions` / `AutodeskTokenStorePurpose` in `SiOffice.AutodeskConnector`. AccService and AuthOnce always construct `TokenProvider` with `AccServiceAdmin`.

---

## DEV workstation AccService (local)

On DEV, AccService often runs under the same interactive Windows user as the desktop app. That is fine **only** because stores are separate:

```text
Desktop:    %LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json
AccService: %LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json
```

```powershell
cd D:\repos2026\SiNetProjectManager_GitHub
dotnet run --project SiOffice.AccService.AuthOnce\SiOffice.AccService.AuthOnce.csproj -c Release -- --force
# Browser: sign in specifically as AccService Admin = siad@si-eng.co.il
# (SystemSetting AccBootstrapAdminEmail; must NOT be SIUser / Tair / random Autodesk user)
# If the browser is already signed in as danny@ / Tair@ — sign out / choose another account first.
# AuthOnce writes ONLY the AccService store (never the desktop token file).
# Then restart AccService so it reloads the AccService store.
```

**Steady-state AccService Admin identity:** `siad@si-eng.co.il`  
Configured via **`AccBootstrapAdminEmail`**. Changing the DB setting updates expected identity only — it does **not** replace the refresh token.  
After AuthOnce, read `GET /v1/acc/admin-identity` and require:

- `tokenStoragePath` ends with `\SiNet\Autodesk\AccService\refresh_token.json`
- `ActualAdminEmail` == `siad@si-eng.co.il`
- `EmailMatch` = true

A valid desktop Autodesk token **never** satisfies AccService Admin requirements. If the AccService store is missing → `TokenMissing` even when the desktop store exists.

---

## 1. What broke (symptom → cause)

| Symptom | Likely cause |
| --- | --- |
| JumboMail stuck on "מעלה ל-ACC…" | AccService cannot get a 3-legged Autodesk token from **its** store |
| `[NativeAccIngest] Failed: HttpClient.Timeout of 100 seconds` | Same |
| Startup log: `refreshTokenFileExists=false` on AccService path | No AccService Admin refresh token yet |
| Desktop Autodesk login “works” but AccService Admin is unhealthy | Desktop and AccService stores are independent (expected) |

**Important split:**

| Store | Path / location | Who must own it |
| --- | --- | --- |
| Autodesk **ClientId / ClientSecret** | Windows Credential Manager (`SiNet/…`) | Windows account that runs AccService |
| Autodesk **AccService Admin refresh token** | `%LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json` | Same Windows account that runs AccService |
| Autodesk **desktop user-context token** | `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json` | Interactive desktop user (never used as AccService Admin) |

PROD example: `C:\Users\sieng\AppData\Local\SiNet\Autodesk\AccService\refresh_token.json`

There is **no Device Auth** flow. Interactive browser OAuth (`TokenProvider` → localhost:8080) is the mechanism.

---

## 2. Procedure now (PROD server `SI-WIN-2K19`)

> **Note:** This DEV slice does **not** migrate PROD. Before a future production rollout, plan a controlled migration: either re-run AuthOnce/install into the AccService store, or a one-time known-good copy of the existing service token into `\Autodesk\AccService\`. Do not silently copy a workstation desktop token.

### 2.1 Confirm the gap

```powershell
$day = Get-Date -Format yyyyMMdd
Get-Content "\\si-win-2k19\AutoCAD Data\log\AccService\SI-WIN-2K19\sieng\AccService-$day.log" -Tail 80
# Look for: tokenPurpose=AccServiceAdmin, tokenStoragePath=...\Autodesk\AccService\refresh_token.json,
#           refreshTokenFileExists=false|true
```

### 2.2 Preferred when the server has no usable browser (drop + install)

| Step | Where | Double-click |
| --- | --- | --- |
| 1. Export (+ new Autodesk login) | Workstation | `\\SI-WIN-2K19\AppFolder\AppNet\Server\Export-AccAutodeskToken-ToShare.cmd` |
| 2. Install | `SI-WIN-2K19` as Administrator | `\\SI-WIN-2K19\AppFolder\AppNet\Server\Install-AccAutodeskToken-FromShare.cmd` |

**What Export does:** AuthOnce writes `%LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json`, then copies **that** AccService file to the drop folder.

**What Install does:** places the drop file into `C:\Users\sieng\AppData\Local\SiNet\Autodesk\AccService\refresh_token.json` and restarts AccService.

### 2.3 Optional: AuthOnce on the server (often blocked)

Prefer §2.2. Kit files:

| File | Role |
| --- | --- |
| `Refresh-AccService-Token.cmd` | Double-click entry (self-elevates) |
| `Refresh-AccService-Token.ps1` | Stop service → AuthOnce as sieng → start service |
| `SiOffice.AccService.AuthOnce.exe` | Interactive AccService Admin `TokenProvider` |
| `Export-AccAutodeskToken-ToShare.cmd` | Workstation: export AccService store to drop |
| `Install-AccAutodeskToken-FromShare.cmd` | Server: install into AccService store + restart |

Verify as `sieng`:

```powershell
Test-Path "$env:LOCALAPPDATA\SiNet\Autodesk\AccService\refresh_token.json"
Get-Item "$env:LOCALAPPDATA\SiNet\Autodesk\AccService\refresh_token.json" | Format-List FullName, Length, LastWriteTime
```

### 2.4 Alternate: manual copy

Only if the source file is an **AccService Admin** token for the **same Autodesk ClientId**:

1. Source: `%LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json` on the AuthOnce workstation.
2. Dest: `C:\Users\sieng\AppData\Local\SiNet\Autodesk\AccService\refresh_token.json`.
3. Restart AccService; confirm diagnostics show AccService path + `refreshTokenFileExists=true`.

Do **not** commit this file to git or leave it on the UNC share.

### 2.5 If ClientId/Secret themselves are wrong

Vault problem — see [`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md). Fixing secrets alone does **not** create `refresh_token.json`.

---

## 3. Aftercare checklist

- [ ] AccService log: `tokenPurpose=AccServiceAdmin`, AccService `tokenStoragePath`, `refreshTokenFileExists=true`
- [ ] `admin-identity`: Expected/Actual match (`siad@si-eng.co.il`)
- [ ] Client Jumbo → stage reaches "הושלם"
- [ ] Optional: «מצב מערכת» — ACC Admin Identity row healthy

---

## 4. Out of Scope

- Implementing Device Auth or a dedicated "Re-auth AccService" button (see [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md))
- Performing the PROD server token migration in this DEV task
- Rotating Autodesk ClientId/Secret in this procedure
