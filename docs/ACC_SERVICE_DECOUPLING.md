# AccService ↔ SiNetSQL decoupling

> Status: **B1 implemented (vault + central logging)**  
> Date: 2026-07-28  
> Branch: `SiWorkNet10`  
> Related: [`ACC_BOUNDARY.md`](./ACC_BOUNDARY.md), [`LOGGING.md`](./LOGGING.md), [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md)

## Goal

`SiOffice.AccService` must stop depending on the `SiNetSQL` assembly for host bootstrap
concerns, then for contracts/SQL/orchestration, until the `ProjectReference` can be removed
and AccService no longer pulls GoogleConnector/WPF packages transitively.

## Locked decisions (B1)

1. **Scope:** vault + central logging only. `ProjectReference` to SiNetSQL **remains**.
2. **Vault:** AccService uses `SiNet.Infrastructure.Secrets.CredentialVault` +
   `SiNet.Application.Configuration.SecretCatalog` (same `SiNet/*` target strings).
3. **Logging:** `CentralLogging*` lives in `SiNet.Infrastructure.Logging` (canonical).
4. **Temporary bridge:** `CredentialProvider.GetSecret` is still wired to the clean vault so
   SiNetSQL `AccBootstrap` / provisioning in-process keep working.
5. **Out of B1:** contracts/DTOs move, direct Infrastructure.Sql, AccBootstrap extraction,
   ProjectReference removal, SyncEngine Shared `CentralLogging` consolidation.

## Slice roadmap

| Slice | Content |
| --- | --- |
| **B1** (this) | Vault + CentralLogging on clean modules; ProjectReference stays |
| **B2** | Move `AccServiceContracts` / DTOs out of SiNetSQL |
| **B3** | AccService → `Infrastructure.Sql` for DbContext / settings reads |
| **B4** | Extract AccBootstrap + provisioning |
| **B5** | Drop SiNetSQL `ProjectReference`; shrink AccService dependency graph |

## B1 success criteria

1. AccService source does not call `CredentialVaultService`, `SecretKeys`, or
   `SiNetSQL.Services.Logging.*`.
2. `--import-secret`, TLS password, API key, and DB connection use the clean vault.
3. Central logging boots via `SiNet.Infrastructure.Logging`.
4. Provisioning / inbox ensure still work via SiNetSQL + `CredentialProvider` bridge.
5. Build/tests green; **no** DB schema changes.

## Temporary dual ownership

- **Canonical:** `src/SiNet.Infrastructure.Logging` (`CentralLogging*`).
- **SiNetSQL:** no longer owns the implementation; V2 host uses the clean module.
- **MasterPlan.SyncEngine `Shared/Logging/CentralLogging.cs`:** still a separate copy until
  a follow-up slice (B1b / B2). Marked deferred — do not treat as canonical.
