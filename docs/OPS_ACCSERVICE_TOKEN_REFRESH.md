# AccService — refreshing the Autodesk 3-legged token (ops)

> **Title:** AccService Autodesk refresh-token refresh  
> **Date:** 05.09.2026 (token distribution finalize)  
> **Status:** Active  
> **Scope:** How an operator restores Autodesk OAuth for `SiOffice.AccService`. AccService owns a **dedicated** Autodesk token store, independent of the SiNet desktop user-context token. PROD uses **workstation AuthOnce → export → server install** because the server has no interactive browser.

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
- Interactive OAuth authorization URL includes **`prompt=login`** so Autodesk does not silently reuse an existing browser session (e.g. danny@) when AuthOnce needs AccBootstrapAdminEmail (siad@).
- Proof of health = AccService diagnostics report **its** `tokenStoragePath` + userinfo `ActualAdminEmail` vs `AccBootstrapAdminEmail`.
- **Office Inbox Project Admin:** `POST /v1/acc/inbox/ensure` must assign **`AccBootstrapAdminEmail`** (not a hardcoded personal mailbox) as Project Admin on the Office Inbox ACC project. AccService metadata (custom attributes / MoveToProject) returns **403** on the Inbox project when that Admin is missing from project membership.
- **`body.AdminEmail` is not a second source of truth:** empty → use `SystemSettings.AccBootstrapAdminEmail`; equal to configured → accept; different → fail closed.

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

## 2. Production operating model (server has no browser)

**PROD server does NOT require or assume a usable browser.** Interactive AuthOnce on the server is only an optional fallback.

### Preferred production procedure

1. **Workstation** — `SiOffice.AccService.AuthOnce` into dedicated AccService store  
2. **Verify** Actual Autodesk identity == `AccBootstrapAdminEmail` (Export refuses mismatch)  
3. **Export** validated token + non-secret `export_meta.txt` to drop share  
4. **Secure transfer** (UNC drop)  
5. **Server install** into the **Windows service account** dedicated store  
   (`…\SiNet\Autodesk\AccService\refresh_token.json`, never the desktop UserContext path)  
6. **Restart** AccService  
7. **Runtime proof** — `GET /v1/acc/admin-identity` (authoritative)  
8. **System Health** — ACC Admin Identity green (store + identity; Admin API separate)

Metadata fields (no secrets): `TokenPurpose`, `ExpectedAdminEmail`, `ActualAdminEmail`, `ExportedUtc`, `SourceMachine`, optional `AutodeskUserId`.

After successful install **and** runtime `/v1/acc/admin-identity` verification (Healthy), the drop **`refresh_token.json` is deleted** from the share. `export_meta.txt` may be archived under `used\` for audit. If runtime verification fails, the drop token is **not** deleted (controlled recovery).

> **Note:** This DEV slice does **not** perform PROD installation. Before a future production rollout, migrate the existing server token into `\Autodesk\AccService\` via this export/install flow (or a controlled one-time copy of a known-good **AccService** token). Never copy a workstation desktop UserContext token.

**Expected Admin email:** always `dbo.SystemSettings.AccBootstrapAdminEmail` (DB). Export/AuthOnce/Install read the DB; they do not hardcode `siad@…` as SoT. `--expected-email` / `-ExpectedAdminEmail` may only match the DB value (diagnostic); a CLI≠DB mismatch fails closed.

### 2.1 Confirm the gap

```powershell
$day = Get-Date -Format yyyyMMdd
Get-Content "\\si-win-2k19\AutoCAD Data\log\AccService\SI-WIN-2K19\sieng\AccService-$day.log" -Tail 80
# Look for: tokenPurpose=AccServiceAdmin, tokenStoragePath=...\Autodesk\AccService\refresh_token.json,
#           refreshTokenFileExists=false|true
```

### 2.2 Preferred: workstation Export → server Install

| Step | Where | Double-click |
| --- | --- | --- |
| 1. Export (+ AuthOnce + identity gate) | Workstation | `Export-AccAutodeskToken-ToShare.cmd` |
| 2. Install (metadata pre-check + AccService store) | `SI-WIN-2K19` as Administrator | `Install-AccAutodeskToken-FromShare.cmd` |

**Export:** only `%LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json`.  
Refuses the desktop path `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json`.  
Refuses export when ActualAdminEmail ≠ ExpectedAdminEmail.

**Install:** resolves the AccService Windows service account when possible; installs into that account’s AccService store; does not touch the desktop UserContext file. Metadata must say `TokenPurpose=AccServiceAdmin` and Actual == configured Expected.

**Authoritative proof after restart:** AccService runtime `/v1/acc/admin-identity` + System Health — not the export metadata alone.

### 2.3 Optional: AuthOnce on the server (fallback only)

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
- [ ] `admin-identity`: Expected/Actual match (`siad@si-eng.co.il`), EmailMatch=true
- [ ] System Health «ACC Admin Identity»: מחסן AccService + זהות תקינה; Admin API separately תקין / 403
- [ ] Client Jumbo → stage reaches "הושלם"
- [ ] Drop folder: live `refresh_token.json` removed (moved under `used\`)

### System Health classifications (permanent)

| Condition | Display |
| --- | --- |
| Store + identity + Admin API 200 | `ACC Admin` / חשבון מוגדר+מחובר / מחסן: AccService / זהות: תקינה / Admin API: תקין |
| Wrong Autodesk user | `ACC Admin — שגיאת זהות` |
| Wrong token store/purpose | `ACC Admin — מחסן טוקן שגוי` |
| Identity OK, Admin API 403 | `ACC Admin — החשבון נכון, אך חסרות הרשאות Account Admin` |

Admin API probe (authoritative for Healthy):  
`GET https://developer.api.autodesk.com/construction/admin/v1/accounts/{accountId}/projects?limit=1`  
using the AccService Admin 3-legged token (`accountId` from default `AccHub`).

Runtime `/v1/acc/admin-identity` is authoritative over export metadata.

---

## 4. Out of Scope

- Implementing Device Auth or a dedicated "Re-auth AccService" button (see [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md))
- Performing the PROD server token migration in this DEV task
- Rotating Autodesk ClientId/Secret in this procedure
