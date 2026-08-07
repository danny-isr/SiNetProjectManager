# AccService ↔ SiNetSQL decoupling

> Status: **B4 done -- SiNetSQL `ProjectReference` dropped from AccService (B5 goal reached early)**
> Date: 2026-07-28
> Updated: 07.08.2026
> Scope: AccService decoupling from SiNetSQL assembly / ProjectReference; host bootstrap and logging notes.
> Working branches: `release` + `development` -- see [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) §3. `SiWorkNet10` deprecated.

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
| **B4** | Extract AccBootstrap + provisioning; close `CredentialProvider` bridge | **Done** |
| **B5** | Drop SiNetSQL `ProjectReference`; shrink AccService dependency graph | **Done (achieved as part of B4)** |

---

## B1–B3 (done)

- **B1:** Vault + CentralLogging; temporary `CredentialProvider.GetSecret` bridge until B4.
- **B2:** Wire contracts in `SiOffice.AccService.Contracts`.
- **B3:** `AddSiNetSql` + `ISystemSettingsQueryService` for AccService-owned reads.
- SyncEngine Shared `CentralLogging` still deferred (B1b).

---

## B4 — implemented (AccBootstrap + provisioning extract)

### Why not “ports-only”

Clean `IAccInboxBootstrapService` / Autodesk remotes are **clients of AccService**. They do not
replace the server-side orchestration that AccService constructs (`AccBootstrapService`,
`AccProjectProvisioningService`) — that orchestration was extracted into its own project
instead of being ported to a client abstraction.

### What shipped

1. **New project:** `src/SiNet.Infrastructure.AccBootstrap` (`net10.0`). Moved files **kept**
   their original `SiNetSQL.Services.AccBootstrap` namespace (same pattern as `SiNetSQL.Models`
   living in `SiNet.Infrastructure.Sql`) to avoid using-directive churn in SiNetSQL/V2.
   References: `SiNet.Infrastructure.Sql`, `SiNet.Application`, `SiOffice.AutodeskConnector`.
2. **Moved (canonical, deleted from SiNetSQL):** `AccBootstrapService`, `IAccBootstrapService`,
   `AccProjectProvisioningService`, `IAccProjectProvisioningService`, `OfficeInboxTargets`,
   `ProjectAccTargets`, `CreateProjectPlatform`, `AccConstants`, `LocalAccInboxProvisioner`,
   `IAccInboxProvisioner`, `AccUserBootstrapService`, `IAccUserBootstrapService`,
   `AccMembershipReconciler`, `IAccMembershipReconciler`.
3. **Adaptations to compile without SiNetSQL:**
   - `AppLogger.Info/Warn/Error` → new `AccBootstrapLog` static Serilog facade.
   - `SiNetSQL.FileIndex.SidecarMetadata` → `SiNet.Infrastructure.Sql.Services.Email.Acc.SidecarMetadata`.
   - New `IAccMetadataStatusReporter` / `AccMetadataIssue` / `AccMetadataOperation` types owned
     by AccBootstrap. SiNetSQL's `AccMetadataStatusReporter` singleton now implements **both**
     its own `SiNetSQL.FileIndex.IAccMetadataStatusReporter` and the new AccBootstrap contract
     (explicit interface impl mapping between the two structurally-identical shapes), so the UI
     status badge and the decoupled provisioning service share one reporter instance.
   - `AccProjectProvisioningService` / `LocalAccInboxProvisioner`: `SystemSettingsService?` →
     `ISystemSettingsQueryService?`, reading `settings.Acc.AccProjectTemplateName` /
     `settings.Acc.AccBootstrapAdminEmail` / `settings.EmailOffice.Inbox*` from
     `GetSystemSettingsAsync`.
   - `FixDirectoryName` inlined as `DirectoryNameExtensions` (was `SiNetSQL.MyExtensions.DataFunc`).
   - `EmailIngestionServiceFactory`'s legacy in-process fallback (only path still holding a
     `SystemSettingsService` reference) uses a small `LegacySystemSettingsQueryAdapter` to bridge
     into `ISystemSettingsQueryService` for `LocalAccInboxProvisioner` — DI-driven call sites
     (V2 `AddTransient<IAccProjectProvisioningService, ...>`, etc.) already resolve
     `ISystemSettingsQueryService` from the container and needed no changes.
4. **SiNetSQL:** deleted the moved files, added a `ProjectReference` to the new AccBootstrap
   project.
5. **AccService host wire (`SiOffice.AccService`):**
   - Removed the `CredentialProvider.GetSecret = CredentialVault.GetSecret;` bridge and every
     `CredentialProvider.Autodesk*` read; replaced with direct
     `CredentialVault.GetSecret(SecretCatalog.AutodeskClientId/AutodeskClientSecret)` reads
     (`Program.cs` TokenProvider registration + diagnostics, `AccEndpoints.cs` `/diag` and
     `/inbox/ensure`).
   - Removed `builder.Services.AddSingleton<SystemSettingsService>()` — nothing in AccService
     needs the legacy type anymore (`AccProjectProvisioningService` gets
     `ISystemSettingsQueryService` from `AddSiNetSystemSettingsSql`, already registered).
   - **Dropped the `SiNetSQL.csproj` ProjectReference entirely** (B5 goal) — AccService now
     references `SiNet.Infrastructure.AccBootstrap` instead; `SiNetSQLDbContext` /
     `SiNetSQL.Data` / `SiNetSQL.Models` still resolve via the existing direct
     `SiNet.Infrastructure.Sql` reference (unchanged from B3).
6. **V2 / SiNetSQL consumers:** no DI/constructor changes needed — `AccProjectProvisioningService`
   and `LocalAccInboxProvisioner` are registered via `AddTransient<TInterface, TImpl>()`, so the
   container already resolves the new `ISystemSettingsQueryService` constructor parameter.
   `LegacyHostLocalAccInboxBootstrapExecutor` (V2) already used `ISystemSettingsQueryService`
   directly and needed no change.

### Out of scope (unchanged)

Rewriting AccService onto Application client ports; DB schema; HTTP wire breaks; SyncEngine
CentralLogging (B1b).

### Success criteria (B4) — met

1. AccBootstrap/provisioning types live under `SiNet.Infrastructure.AccBootstrap`. ✅
2. AccService does not use `CredentialProvider` / SiNetSQL `SystemSettingsService` /
   SiNetSQL `AppLogger`. ✅
3. AccService builds with **zero** `SiNetSQL.csproj` ProjectReference (B5 achieved early). ✅
4. V2 / SiNetSQL consumers compile against the new project (no DI changes required). ✅
5. Build/tests green; **no** schema changes; HTTP JSON shapes unchanged. ✅ (see
   `AccServiceDecouplingB4BoundaryTests`).

---

## Temporary dual ownership (logging)

- **Canonical:** `src/SiNet.Infrastructure.Logging`.
- **MasterPlan.SyncEngine Shared/Logging:** deferred until B1b.
