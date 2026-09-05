# SIUser identity gate + pending auto-registration

> **Status:** Active  
> **Date:** 04.09.2026 (AccService Admin vs operator clarified 04.09.2026)  
> **Scope:** Standalone New System host (`SiNet.App.Wpf`)  
> **Related:** [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md), [`APP_SHELL.md`](./APP_SHELL.md), [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md), [`OPS_ACCSERVICE_TOKEN_REFRESH.md`](./OPS_ACCSERVICE_TOKEN_REFRESH.md)

## Authority

`dbo.SIUser` is the identity authority. Runtime/Windows login is used **only** to locate the row (`LoginName`). After resolution, external equality uses **`SIUser.Email` only** (never Windows username vs Google/ACC email).

## Two distinct Autodesk identities

| Role | Email authority | Used for |
| --- | --- | --- |
| **Current operator** | `SIUser.Email` ↔ Google ↔ ACC **project membership** | Human work: file ACC, MoveToProject, status bar MATCH |
| **AccService Admin principal** | Configured Autodesk **ACC Account Admin** email | Admin APIs: list/add members, industry roles, EnsureProjectMapping, custom-attribute defs |

**Designated AccService Admin (DEV/office steady-state):** `siad@si-eng.co.il`  
Stored as SystemSetting **`AccService.ExpectedAdminEmail`** (not inferred from `SIUser`).

**Never require** AccService Account Admin email == current `SIUser.Email`.  
**Always require** operator ACC project membership email == `SIUser.Email` before project-bound ACC writes.

Credential purpose enum (code): `AutodeskCredentialPurpose.UserContext` vs `AutodeskCredentialPurpose.AccServiceAdmin`.

- **UserContext** 3-legged: Autodesk profile email **must** equal `SIUser.Email` (`AutodeskThreeLeggedWrite`).
- **AccServiceAdmin** 3-legged: used by AccService/bootstrap; **not** compared to `SIUser.Email`; must match **`AccService.ExpectedAdminEmail`** (fail-closed when configured and mismatched). Does not flip operator MATCH/Mismatch.

### AccService Admin identity check

On AccService startup / health diagnostics (no tokens logged):

| Field | Meaning |
| --- | --- |
| Connected Autodesk profile email | From userinfo of the 3-legged Admin token |
| Expected admin email | `AccService.ExpectedAdminEmail` |
| Match / Mismatch | Case-insensitive equality |

Mismatch warning (ops):

```text
AccService Autodesk admin account mismatch.
Expected: siad@si-eng.co.il
Connected: <actual email>
```

Wrong connected identity → Admin API operations **fail closed**. If the connected email **is** the expected admin but Admin APIs still return 403, classify as ACC Account Admin / hub / app permission configuration — not “wrong token user”.

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
- If email missing from membership: supported reconciler once (AccService Admin credential), then fresh ACC readback; only readback `IsMember=true` may PASS.
- Autodesk Admin APIs require a **3-legged ACC Account Admin** token (`AccServiceAdmin`). HTTP 403 on list-members must surface as **`ProbeSucceeded=false`** (unavailable), never as empty membership / “not a member”.
- Ops: restore Admin refresh token per [`OPS_ACCSERVICE_TOKEN_REFRESH.md`](./OPS_ACCSERVICE_TOKEN_REFRESH.md) (`AuthOnce --force` as ACC Account Admin). Windows username need not match Autodesk Admin email.

## Status bar

- Full **`זהות: תקינה`** only when authorized SIUser + Email + Google MATCH, and when a project is active also ACC **membership** MATCH (operator). AccService Admin email is **not** part of this footer.
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
