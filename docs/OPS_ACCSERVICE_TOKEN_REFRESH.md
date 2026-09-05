# AccService — refreshing the Autodesk 3-legged token (ops)

> **Title:** AccService Autodesk refresh-token refresh  
> **Date:** 03.08.2026  
> **Status:** Active  
> **Scope:** How an operator restores Autodesk OAuth for `SiOffice.AccService` on the PROD server when Jumbo→ACC / NativeAccIngest hang with HttpClient 100s timeouts. Documentation only for the procedure; no code changes in this round.

Related: [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md),
[`SYSTEM_HEALTH.md`](./SYSTEM_HEALTH.md),
[`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md),
[`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md),
[`SiOffice.AccService/README.md`](../SiOffice.AccService/README.md).

---

## DEV workstation AccService (local)

On the DEV machine, AccService typically runs under the interactive Windows user (`dannyisrael`).
The Admin refresh token is then:

`%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json` of **that** Windows user.

```powershell
cd D:\repos2026\SiNetProjectManager_GitHub
dotnet run --project SiOffice.AccService.AuthOnce\SiOffice.AccService.AuthOnce.csproj -c Release -- --force
# Browser: sign in specifically as AccService Admin = siad@si-eng.co.il
# (SystemSetting AccBootstrapAdminEmail; must NOT be SIUser / Tair / random Autodesk user)
# If the browser is already signed in as danny@ / Tair@ — sign out / choose another account first.
# Then restart AccService so it reloads the token.
```

**Steady-state AccService Admin identity:** `siad@si-eng.co.il`  
Configured via the **single** `dbo.SystemSettings` key **`AccBootstrapAdminEmail`** (bootstrap inserts default if missing; never overwrites an existing value). Changing the DB setting updates which token identity is **expected**; it does **not** replace the Autodesk refresh token.  
Do **not** require AccService Admin Autodesk email == current SIUser.Email (see [`IDENTITY_SIUSER_GATE.md`](./IDENTITY_SIUSER_GATE.md)).  
After AuthOnce, independently read AccService token profile (`GET /v1/acc/admin-identity`) and require Actual == `siad@si-eng.co.il` before Admin mutations. Then probe Admin APIs; 403 with matching identity means Account Admin rights, not wrong login.

---

## 1. What broke (symptom → cause)


| Symptom | Likely cause |
| --- | --- |
| JumboMail window opens, file downloads, then stuck on "מעלה ל-ACC…" | AccService cannot get a 3-legged Autodesk token |
| `[NativeAccIngest] Failed: HttpClient.Timeout of 100 seconds` | Same — AccService call to Autodesk never completes |
| AccService central log empty / quiet while client times out | Service is up (TCP/HTTPS) but Autodesk work hangs without Warning lines |
| Startup log: `refreshTokenFileExists=false` | Confirmed: no refresh token for the **service Windows user** |

**Important split:**

| Store | Path / location | Who must own it |
| --- | --- | --- |
| Autodesk **ClientId / ClientSecret** | Windows Credential Manager (`SiNet/…`) | Same Windows account that runs AccService (`SI-ENG\sieng`) |
| Autodesk **refresh token (3-legged)** | `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json` of that account | **`C:\Users\sieng\AppData\Local\SiNet\Autodesk\refresh_token.json`** |

A token under `Danny` on a workstation **does not** count for AccService. The service only sees **sieng**'s LocalAppData.

There is **no Device Auth** flow. Interactive browser OAuth (`TokenProvider` → localhost:8080) is the mechanism.

---

## 2. Procedure now (PROD server `SI-WIN-2K19`)

### 2.1 Confirm the gap

On the PROD workstation or RDP, read today's AccService log (or yesterday's startup if today is empty):

```powershell
$day = Get-Date -Format yyyyMMdd
Get-Content "\\si-win-2k19\AutoCAD Data\log\AccService\SI-WIN-2K19\sieng\AccService-$day.log" -Tail 80
# Look for: refreshTokenFileExists=false  OR  tokenStoragePath=C:\Users\sieng\AppData\Local\SiNet\Autodesk\...
```

Also confirm the service listens:

```powershell
# From any domain machine
Test-NetConnection SI-WIN-2K19 -Port 8443
# HTTPS without API key should return 401 on /v1/acc/health paths that require key;
# /v1/acc/health is documented as unauthenticated in AccService README.
```

### 2.2 Preferred when the server has no usable browser (drop + install)

Autodesk browser login must happen on a **workstation** (Explorer / your PC), signed in as the
**ACC Account Admin** Autodesk user (not "whoever Windows user you are"). Then copy the file
into `sieng`'s LocalAppData on the server.

| Step | Where | Double-click |
| --- | --- | --- |
| 1. Export (+ new Autodesk login) | Workstation | `\\SI-WIN-2K19\AppFolder\AppNet\Server\Export-AccAutodeskToken-ToShare.cmd` |
| 2. Install | `SI-WIN-2K19` as Administrator | `\\SI-WIN-2K19\AppFolder\AppNet\Server\Install-AccAutodeskToken-FromShare.cmd` |

**What Export does now:** if the local token is missing/old, it **backs up and deletes** it, launches
`SiOffice.AccService.AuthOnce.exe --force` on the workstation (browser Autodesk login as ACC Admin),
waits until a **new** `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json` exists, then copies it to the drop folder.
Use `-SkipCreate` only to export an already-fresh file without opening the browser.

**Windows user vs Autodesk user:** copying Danny's file to `sieng` only changes the Windows path.
The Autodesk identity inside the JSON must still be an ACC Admin for the same ClientId as AccService.

### 2.3 Optional: AuthOnce on the server (often blocked)

On some locked-down servers the browser opens on the wrong session or not at all. Prefer §2.2.

On **`SI-WIN-2K19`** when interactive browser as `sieng` works:

1. Open `\\SI-WIN-2K19\AppFolder\AppNet\Server\`
2. Double-click **`Refresh-AccService-Token.cmd`** (UAC elevation → Administrator).
3. Enter the Windows password for **`SI-ENG\sieng`** in the **Windows credential dialog**.
4. Complete Autodesk login in the browser with an **ACC Account Admin** user.
5. When AuthOnce prints OK, press Enter in that window.
6. Read the final banner: **`RESULT: SUCCESS`** or **`RESULT: FAILED`**.

Kit files (AuthOnce path):

| File | Role |
| --- | --- |
| `Refresh-AccService-Token.cmd` | Double-click entry (self-elevates) |
| `Refresh-AccService-Token.ps1` | Stop service → AuthOnce as sieng → start service |
| `SiOffice.AccService.AuthOnce.exe` | Interactive `TokenProvider` |
| `Export-AccAutodeskToken-ToShare.cmd` | Workstation: copy token to drop folder |
| `Install-AccAutodeskToken-FromShare.cmd` | Server: install drop into sieng + restart service |

### 2.3.1 Manual interactive session (legacy fallback)

AccService is a **Windows Service** — it cannot open a browser by itself. Run a short interactive session as the service account:

1. RDP to `SI-WIN-2K19` as **Administrator**.
2. Start an elevated PowerShell **as `SI-ENG\sieng`** (e.g. `runas /user:SI-ENG\sieng powershell`).
3. Under that session, run AuthOnce from the Server kit, or temporarily run an interactive host that uses the same vault ClientId/Secret.
4. Complete the Autodesk browser login with an **ACC Account Admin** user.
5. Verify the file exists:

```powershell
# As sieng:
Test-Path "$env:LOCALAPPDATA\SiNet\Autodesk\refresh_token.json"
Get-Item "$env:LOCALAPPDATA\SiNet\Autodesk\refresh_token.json" | Format-List FullName, Length, LastWriteTime
```

6. If you stopped the Windows Service, start it again:

```powershell
Restart-Service SiOfficeAccService
```

7. Confirm startup diagnostics now show `refreshTokenFileExists=true` in the AccService log.
8. From the PROD client: retry JumboMail upload / open «מצב מערכת» and refresh ACC rows.

### 2.4 Alternate: manual copy (same as §2.2 without the scripts)

Prefer §2.2. Manual equivalent: only if the workstation token was issued for the **same Autodesk ClientId**
that AccService uses from the vault, and the Autodesk user has ACC admin rights:

1. On a working workstation, locate `%LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json`.
2. Copy it to `C:\Users\sieng\AppData\Local\SiNet\Autodesk\refresh_token.json` on the server (create the folder if missing). Ensure the file is readable by `sieng`.
3. Restart AccService and verify `refreshTokenFileExists=true`.

Do **not** commit this file to git or leave it on the UNC share.

### 2.5 If ClientId/Secret themselves are wrong

That is a vault problem, not the refresh file. Use `Install-OnServer.ps1` / Secret Setup export per [`SECRETS-MANAGEMENT.md`](../SECRETS-MANAGEMENT.md). Fixing secrets alone does **not** create `refresh_token.json`.

---

## 3. Aftercare checklist

- [ ] AccService log: `refreshTokenFileExists=true`
- [ ] Client Jumbo → stage reaches "הושלם" (not stuck on upload)
- [ ] No new `[NativeAccIngest] … Timeout of 100 seconds` in local Client log
- [ ] Optional: open «מצב מערכת» — ACC / AccService rows healthy

---

## 4. Out of Scope

- Implementing Device Auth or a dedicated "Re-auth AccService" button (see [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md))
- Changing where the refresh token is stored
- Rotating Autodesk ClientId/Secret in this procedure

## 5. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Device-code / DeviceAuth for AccService | Not available | `TokenProvider` has no device-code path |
| Manual-only token refresh without a kit tool | Superseded | Prefer `Server\Refresh-AccService-Token.cmd` + AuthOnce |
| Auto popup when token missing | Postponed to DEV | Tracked in [`OPS_STARTUP_ALERTS.md`](./OPS_STARTUP_ALERTS.md) |

## 6. Needs Review

- Exact UX when `runas` cannot show a browser on a locked / disconnected RDP session — operator must complete login on an interactive desktop.
- Exact Windows service name on installs that differ from `SiOfficeAccService` (`Get-Service *Acc*`).
