# AccService ↔ SiNetSQL decoupling

> Status: **B3 implemented (DbContext + settings reads)**  
> Date: 2026-07-28  
> Branch: `SiWorkNet10`  
> Related: [`ACC_BOUNDARY.md`](./ACC_BOUNDARY.md), [`LOGGING.md`](./LOGGING.md),
> [`ACC_CONTROL_PLANE.md`](./ACC_CONTROL_PLANE.md), [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md)

## Goal

`SiOffice.AccService` must stop depending on the `SiNetSQL` assembly for host bootstrap
concerns, then for contracts/SQL/orchestration, until the `ProjectReference` can be removed
and AccService no longer pulls GoogleConnector/WPF packages transitively.

## Slice roadmap

| Slice | Content | Status |
| --- | --- | --- |
| **B1** | Vault + CentralLogging on clean modules; ProjectReference stays | **Done** |
| **B2** | Move `AccServiceContracts` / wire DTOs out of SiNetSQL | **Done** |
| **B3** | AccService → `Infrastructure.Sql` for DbContext / settings reads | **Done** |
| **B4** | Extract AccBootstrap + provisioning | Planned |
| **B5** | Drop SiNetSQL `ProjectReference`; shrink AccService dependency graph | Planned |

---

## B1 / B2 (done)

- **B1:** Vault (`CredentialVault` + `SecretCatalog`) + `CentralLogging` in clean modules;
  temporary `CredentialProvider.GetSecret` bridge until B4.
- **B2:** Wire contracts in `src/SiOffice.AccService.Contracts`; Autodesk mirror deleted.
- SyncEngine Shared `CentralLogging` still deferred (B1b).

---

## B3 (done) — DbContext + settings reads

### Locked decisions

1. **DbContext:** AccService registers via `AddSiNetSql(connectionString)`
   (`SiNetSQL.Data.SiNetSQLDbContext` in Infrastructure.Sql, compat-120).
2. **Direct ProjectReference** to `SiNet.Infrastructure.Sql`.
3. **AccService-owned settings reads** (`POST /inbox/ensure`): Application
   `ISystemSettingsQueryService` via `AddSiNetSystemSettingsSql` + `AddSiNetAuthorizationSql`
   (`NullCurrentUserContext`). Same string fallbacks as the previous
   `SystemSettingsService.GetOrDefaultAsync` path.
4. **Keep** SiNetSQL `SystemSettingsService` DI for `AccProjectProvisioningService` until B4.
5. **Keep** SiNetSQL `ProjectReference` for AccBootstrap / provisioning / `CredentialProvider`.
6. **Out of B3:** AccBootstrap/provisioning move (B4); drop SiNetSQL reference (B5);
   TFM change; DB schema; HTTP wire changes.

### Success criteria

1. AccService csproj references `SiNet.Infrastructure.Sql`.
2. `Program.cs` uses `AddSiNetSql` (not inline `AddDbContextFactory`).
3. `/inbox/ensure` uses `ISystemSettingsQueryService`, not `SiNetSQL.Services.SystemSettingsService`.
4. Provisioning still resolves via SiNetSQL types; build/tests green; **no** schema changes.

---

## Temporary dual ownership (logging)

- **Canonical:** `src/SiNet.Infrastructure.Logging`.
- **MasterPlan.SyncEngine Shared/Logging:** deferred until B1b.
