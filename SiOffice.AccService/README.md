# SiOffice.AccService

Privileged-operations service that centralizes ACC API calls requiring
**Account Admin / Project Admin / Folder CONTROL** rights. Runs as a
**Windows Service** on the office Windows Server 2019. The WPF client
(`SiNetProjectManagerV2`) calls this service over HTTPS instead of holding
Autodesk admin credentials itself.

| Endpoint                 | Description                                  |
|--------------------------|----------------------------------------------|
| `GET /v1/acc/health`     | Health probe (no auth)                       |
| `GET /v1/acc/templates`  | List ACC project templates `[ {id, name} ]`  |
| `POST /v1/acc/inbox/ensure` | Ensure the Office Inbox ACC project/root `_Inbox` folder/member access through the central service path. |

All non-health endpoints require the header `X-AccService-Key: <key>`.

## Current ACC Inbox boundary

- `SiOffice.AccService` is the central service boundary for ACC operations when the WPF application runs in remote/service mode.
- When `AccService:BaseUrl` is configured, the WPF application uses the service instead of running local privileged ACC provisioning paths.
- Office Inbox ensure runs through `POST /v1/acc/inbox/ensure`; the service creates/ensures the configured Office Inbox project and root `_Inbox` folder and returns the ACC project/root/inbox folder identifiers.
- ACC remains the source of truth for physical file existence. SQL stores metadata/cache/helper identifiers only.
- ACC Inbox file layout is centralized in `AccInboxLayout` in the shared application code: message folder `MSG_{messageKey}`, message files `00_Email.pdf` and `manifest.json`, and regular attachments under the `Attachments` child folder.
- Viewer/open and MoveToProject flows must use ACC reconciliation/layout-aware lookup results; they must not open or move files from DB-only identifiers.
- Metadata/custom-attribute read failure is not proof that a file is missing if ACC listing verifies the physical item.
- The current shortened move-target alternative attribute name is `SiInbox.Move.TargetAltId`.

---

## Architecture (the part that matters for installation)

Secrets — including the AccService API key, connection strings, Autodesk
credentials, etc. — are stored in **Windows Credential Manager** (generic
credentials). The store is **scoped per Windows user**: a credential written
by user `A` is invisible to a process running as user `B`.

This has one critical consequence:

> **The Windows service must run under the same Windows account that wrote
> the secrets in the WPF client.**

If the service runs as `LocalSystem` (the default for Windows services) but
secrets were saved interactively by `DOMAIN\YourUser`, the service will log:

```
WARN  AccService API key is not configured (vault key 'SiNet/AccService/ApiKey' …).
      All non-health requests will be rejected with 401.
```

…even though the WPF "save" succeeded — different vault namespace.

The MSI exposes two install-time properties that solve this:

| Property          | Purpose                                                       |
|-------------------|---------------------------------------------------------------|
| `SERVICEACCOUNT`  | Windows account to run the service under. Default: LocalSystem.|
| `SERVICEPASSWORD` | Password for that account. Hidden in MSI logs.                |

When `SERVICEACCOUNT` is empty the service installs as `LocalSystem` (legacy
behavior). When set, the service is registered under that account and reads
from that account's credential vault.

---

## End-to-end install on the server

Do these once, in order, on the Windows Server.

### 1. Build the MSI on the developer machine

```powershell
cd D:\repos2026\SiNetProjectManager_GitHub\SiOffice.AccService
.\publish-service.ps1
```

This bumps `<Version>` in `SiOffice.AccService.csproj`, publishes the service,
builds `SiOfficeAccService.msi`, and copies it to:

```
\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi
```

### 2. Grant "Log on as a service" to your Windows user (one time)

RDP to the server with the account that will run the service, then in
**PowerShell as Administrator**:

```powershell
$tmp  = [IO.Path]::GetTempFileName()
secedit /export /cfg $tmp | Out-Null
$cfg  = Get-Content $tmp
$user = "$env:USERDOMAIN\$env:USERNAME"
if ($cfg -notmatch [regex]::Escape($user)) {
    $cfg = $cfg -replace '(SeServiceLogonRight\s*=.*)', "`$1,$user"
    $cfg | Set-Content $tmp
    secedit /configure /db secedit.sdb /cfg $tmp /areas USER_RIGHTS | Out-Null
    Write-Host "Added $user to 'Log on as a service'"
} else {
    Write-Host "$user already has 'Log on as a service'"
}
Remove-Item $tmp
```

Without this right Windows will refuse to start the service after we point it
at a user account.

### 3. Install (or upgrade) the MSI under your account

Still in **PowerShell as Administrator**:

```powershell
$msi  = "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi"
$user = "$env:USERDOMAIN\$env:USERNAME"
$sec  = Read-Host "Password for $user" -AsSecureString
$pwd  = [System.Net.NetworkCredential]::new('', $sec).Password

msiexec.exe /i $msi `
    SERVICEACCOUNT="$user" `
    SERVICEPASSWORD="$pwd" `
    /qb /l*v "$env:TEMP\AccService-install.log"
```

The MSI will:

1. Detect the existing install and run a major upgrade.
2. Stop the running service.
3. Replace files in `C:\AccService`.
4. Re-register `SiOfficeAccService` to log on as your account.
5. Open the firewall rule for HTTPS 8443 if missing.
6. Start the service.

### 4. Verify the service is running under the right account

```powershell
sc.exe qc SiOfficeAccService | Select-String "SERVICE_START_NAME"
Get-Service SiOfficeAccService
```

Expected:

```
SERVICE_START_NAME : DOMAIN\YourUser
Status             : Running
```

If `SERVICE_START_NAME` is `LocalSystem` the property pass-through didn't
land — re-run step 3 inside an elevated shell.

### 5. Save the secrets from the WPF client

RDP to the server **as the same account** the service now runs under, launch
`SiNetProjectManagerV2`, open **"הגדרות הרשאות מערכת"**, and:

1. Fill the AccService API key (or click **צור חדש** to generate one — copy
   it to clipboard so you can paste the same value on every client machine).
2. Fill the rest of the secrets you actually need (Autodesk Client Id /
   Secret, connection strings, etc.).
3. Click **שמור** (the regular save button). That's it.

`Save` writes everything to your user's Credential Manager. Because the
service is now running under that same account, it sees the keys immediately
on its next request.

### 6. Smoke test

```powershell
Invoke-RestMethod https://localhost:8443/v1/acc/health -SkipCertificateCheck
# expect: { "status": "ok", ... }

$key = "<paste the same AccService API key>"
Invoke-RestMethod https://localhost:8443/v1/acc/templates `
    -Headers @{ "X-AccService-Key" = $key } -SkipCertificateCheck
```

A 200 response with the templates list means the service read your vault
correctly.

---

## Day-2: refreshing or rotating a secret

You only need to install once. To change a secret afterwards:

1. RDP to the server **as the service account**.
2. Open the WPF client → **"הגדרות הרשאות מערכת"** → edit → **שמור**.
3. Restart the service so it re-reads the vault on its next startup:

   ```powershell
   Restart-Service SiOfficeAccService
   ```

No reinstall required.

---

## Troubleshooting

| Symptom in `accservice-YYYYMMDD.log`                                            | Cause                                                                                  | Fix                                                              |
|---------------------------------------------------------------------------------|----------------------------------------------------------------------------------------|------------------------------------------------------------------|
| `AccService API key is not configured (vault key 'SiNet/AccService/ApiKey' …)`  | The service's logon account differs from the user that saved the secret in the WPF.    | Steps 2–4 above. Verify `sc.exe qc SiOfficeAccService`.          |
| `Rejected request to /v1/acc/… : invalid or missing X-AccService-Key`           | Client is sending the wrong key.                                                       | Re-paste the key from the WPF "save" message into the client.    |
| `Service did not start in a timely fashion` after install                       | The account is missing the **Log on as a service** right, or the password is wrong.    | Re-run step 2; re-run step 3 with the correct password.          |
| MSI install fails with `1603` and the log shows `Service cannot be started`     | Same as above.                                                                         | Same as above.                                                   |

Logs: `%ProgramData%\SiOffice\AccService\logs\accservice-YYYYMMDD.log`.

---

## Manual install (without the MSI)

For development boxes only — production should always use the MSI.

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
    -o C:\AccService

sc.exe create SiOfficeAccService `
    binPath= "C:\AccService\SiOffice.AccService.exe" `
    start= auto `
    obj= "DOMAIN\YourUser" password= "..." `
    DisplayName= "SiOffice ACC Service"

sc.exe failure SiOfficeAccService reset= 86400 `
    actions= restart/5000/restart/5000/restart/5000
sc.exe start SiOfficeAccService
```
