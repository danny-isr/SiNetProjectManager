# AccService ↔ SiNetSQL decoupling

> Status: **B2 implemented (contracts extraction)**  
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
| **B3** | AccService → `Infrastructure.Sql` for DbContext / settings reads | Planned |
| **B4** | Extract AccBootstrap + provisioning | Planned |
| **B5** | Drop SiNetSQL `ProjectReference`; shrink AccService dependency graph | Planned |

---

## B1 (done)

Vault via `CredentialVault` + `SecretCatalog`; logging via `SiNet.Infrastructure.Logging`.
Temporary `CredentialProvider.GetSecret` bridge remains until B4.
SyncEngine Shared `CentralLogging` copy deferred (B1b).

---

## B2 (done) — contracts extraction

### Locked decisions

1. **Project:** `src/SiOffice.AccService.Contracts` (`net10.0`), namespace
   `SiOffice.AccService.Contracts`.
2. **Moved as-is:** API constants + wire DTOs (headers/JSON unchanged; no `/v1` bump).
3. **Consumers:** AccService, V2 remotes/health/secret-setup, Infrastructure.Autodesk.
4. **Deleted:** `AccServiceContractConstants` mirror.
5. **SiNetSQL:** Contracts file removed; AccService still references SiNetSQL for bootstrap/EF.
6. **Out of B2:** AccBootstrap services (B4); unifying Autodesk private `Remote*` DTOs;
   dropping AccService→SiNetSQL `ProjectReference` (B5).

### Success criteria

1. No AccService / V2 / Autodesk source depends on `SiNetSQL.Services.AccBootstrap.Contracts`.
2. `AccServiceContractConstants` gone; Autodesk uses `SiOffice.AccService.Contracts`.
3. AccService still references SiNetSQL for bootstrap/provisioning/EF only.
4. Build/tests green; **no** DB schema changes; HTTP wire unchanged.

---

## Temporary dual ownership (logging)

- **Canonical:** `src/SiNet.Infrastructure.Logging` (`CentralLogging*`).
- **MasterPlan.SyncEngine `Shared/Logging`:** deferred until B1b — not canonical.
