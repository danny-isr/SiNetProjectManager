# SiOffice.AccService

Privileged-operations service that centralizes ACC API calls requiring
**Account Admin / Project Admin / Folder CONTROL** rights. Runs as a
**Windows Service** on the office Windows Server 2019. The WPF client
(`SiNetProjectManagerV2`) calls this service over HTTPS instead of holding
Autodesk admin credentials itself.

## Phase B scaffold

| Endpoint                 | Description                                  |
|--------------------------|----------------------------------------------|
| `GET /v1/acc/health`     | Health probe (no auth)                       |
| `GET /v1/acc/templates`  | List ACC project templates `[ {id, name} ]`  |

All non-health endpoints require the header `X-AccService-Key: <key>`.

## Configuration

`appsettings.json` ships with empty placeholders. Real values go in
`appsettings.Production.json` next to the published binaries (or via
environment variables using the `__` separator):

```jsonc
{
  "AccService": {
    "HttpsPort": 8443,
    "ApiKey": "<random 256-bit base64>",
    "Certificate": {
      "Path": "C:\\ProgramData\\SiOffice\\AccService\\cert.pfx",
      "Password": "<pfx password>"
    }
  },
  "ConnectionStrings": {
    "SiNetDatabase": "Server=...;Database=SiNet;..."
  },
  "Secrets": {
    "SiNet/Autodesk/ClientId":     "<3-legged app client id>",
    "SiNet/Autodesk/ClientSecret": "<3-legged app client secret>"
  }
}
```

## Install as a Windows Service

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
    -o C:\SiOffice\AccService

sc.exe create SiOfficeAccService `
    binPath= "C:\SiOffice\AccService\SiOffice.AccService.exe" `
    start= auto `
    DisplayName= "SiOffice ACC Service"

sc.exe failure SiOfficeAccService reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc.exe start SiOfficeAccService
```

Logs: `%ProgramData%\SiOffice\AccService\logs\accservice-YYYYMMDD.log`.

## Smoke test

```powershell
$key = "<your api key>"
Invoke-RestMethod https://localhost:8443/v1/acc/health -SkipCertificateCheck
Invoke-RestMethod https://localhost:8443/v1/acc/templates `
    -Headers @{ "X-AccService-Key" = $key } -SkipCertificateCheck
```
