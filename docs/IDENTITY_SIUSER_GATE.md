# SIUser identity gate + pending auto-registration

> **Status:** Active  
> **Date:** 04.09.2026 (ACC membership fail-closed closed 04.09.2026)  
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
- ACC Data Management uses 2-legged application OAuth (not human). Human ACC check = ACC project membership email == `SIUser.Email` via `IAccHumanMembershipProbe` (Autodesk/AccService readback). SQL `ProjectAccMapping` only resolves AccProjectId.
- Project-specific ACC writes (`AccFileWrite`, MoveToProject, …) require `IdentityOperationContext` with `SiProjectId` and/or `AccProjectId`, and **`AccMembershipMatch == true`**. `false` / `null` / unavailable → **deny** (fail-closed).
- If email missing from membership: supported reconciler once, then fresh ACC readback; only readback `IsMember=true` may PASS.
- Autodesk 3-legged (when used): Autodesk profile email == `SIUser.Email`.

## Status bar

- Full **`זהות: תקינה`** only when authorized SIUser + Email + Google MATCH, and when a project is active also ACC membership MATCH.
- Active project without ACC verification → **`AccUnverified`** (`Google: תקין | ACC: טרם אומת`) — never overall MATCH.
- No active project → ACC may show as לא רלוונטי; Google-only MATCH is allowed.

## Ports

- `IIdentityCoherenceService` — evaluate/refresh coherence snapshot
- `IIdentityOperationGuard` + `IdentityOperationContext` — deny connector/business writes before side effects
- `IAccHumanMembershipProbe` / `IAccProjectIdResolver` — ACC human membership
- `CurrentUserProfileDto.Email` / `AccUserType` — shared resolved SIUser fields

## Schema hardening (manual; not blocking DEV E2E)

Prefer a unique index on `SIUser.LoginName`. Application uses `sp_getapplock` + re-read so concurrent starts cannot create two rows even before the migration is applied.

```powershell
dotnet ef migrations add SIUser_LoginName_Unique --context SiNetDbContext --project src\SiNet.Infrastructure.Sql\SiNet.Infrastructure.Sql.csproj --startup-project src\SiNet.App.Wpf\SiNet.App.Wpf.csproj
```

(Operator-owned; agent does not create/edit migration files.)
