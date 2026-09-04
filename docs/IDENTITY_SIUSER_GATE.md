# SIUser identity gate + pending auto-registration

> **Status:** Active  
> **Date:** 04.09.2026  
> **Scope:** Standalone New System host (`SiNet.App.Wpf`)  
> **Related:** [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md), [`APP_SHELL.md`](./APP_SHELL.md), [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md)

## Authority

`dbo.SIUser` is the identity authority. Runtime/Windows login is used **only** to locate the row (`LoginName`). After resolution, external equality uses **`SIUser.Email` only** (never Windows username vs Google/ACC email).

## Startup outcomes

| SIUser state | Outcome |
| --- | --- |
| No row | Atomically create `Role=Unauthorized`, `Email=null`, `AccUserType=NoAccUser`, `IsActive=true` → **Pending** restricted shell |
| Active + Unauthorized | **Pending** restricted shell |
| `IsActive=false` | **Blocked** — never auto-reactivate |
| Active + Role ≥ Employee | Normal shell + identity coherence |

## External coherence

- Google (Gmail/Drive/Sheets share one credential): `SIUser.Email` == `IConnectorAuthService.ConnectedAccountEmail` (trim, case-insensitive). Mismatch → logout shared Google session + fail-closed.
- ACC Data Management uses 2-legged application OAuth (not human). Human ACC check = project membership email == `SIUser.Email` when a membership probe is available.
- Autodesk 3-legged (when used): Autodesk profile email == `SIUser.Email`.

## Ports

- `IIdentityCoherenceService` — evaluate/refresh coherence snapshot
- `IIdentityOperationGuard` — deny connector/business writes before side effects
- `CurrentUserProfileDto.Email` / `AccUserType` — shared resolved SIUser fields

## Schema note

Prefer a unique index on `SIUser.LoginName` for concurrent registration. Application also uses SQL `sp_getapplock` + re-read so two starts cannot create two rows even before the migration is applied.

```powershell
dotnet ef migrations add SIUser_LoginName_Unique --context SiNetDbContext --project src\SiNet.Infrastructure.Sql\SiNet.Infrastructure.Sql.csproj --startup-project src\SiNet.App.Wpf\SiNet.App.Wpf.csproj
```

(Operator-owned; agent does not create/edit migration files.)
