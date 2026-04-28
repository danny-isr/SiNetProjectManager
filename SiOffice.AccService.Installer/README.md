# SiOffice.AccService.Installer

WiX v5 MSI that **updates** an already-installed `SiOfficeAccService` Windows
service on the office Windows Server 2019.

## What it does

1. Stops `SiOfficeAccService`.
2. Replaces the contents of `C:\AccService` with the new published payload.
3. Starts `SiOfficeAccService` again.
4. Registers the package in *Programs and Features* (supports Major Upgrade).

## What it does **not** do

- It does **not** create or reconfigure the Windows service. The service is
  bootstrapped once with `sc.exe create` (see `SiOffice.AccService\README.md`).
  Recovery / ACL / Description set on the server are preserved across upgrades.
- It does **not** touch `appsettings.Production.json` or `cert.pfx` — those
  live next to the binaries on the server but are not part of the MSI payload.

## Build

The MSI is produced by `SiOffice.AccService\publish-service.ps1`, which:

1. Publishes the service payload to a **local intermediate folder**
   (`..\artifacts\AccService_Publish`, just scratch space — nothing runs from there).
2. Builds this `.wixproj` with `-p:PublishDir=...` and
   `-p:ProductVersion=<version from csproj>`.
3. Copies the final `SiOfficeAccService.msi` directly to the
   **network share** `\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full`
   so the server can run it without any extra file movement.

Manual build (without auto-deploy):

```powershell
.\publish-service.ps1 -SkipDeploy
```

## Deploy on the server

After running `publish-service.ps1`, log in to `SI-WIN-2K19` and execute:

```powershell
msiexec /i "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi" /qn /l*v upgrade.log
```

…or just double-click the MSI in File Explorer.

If the service is missing on the target machine the MSI will refuse to
install with a clear message — bootstrap the service first.
