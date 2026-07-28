# SyncEngine Shared facilities

Formerly vendored from SiNetSQL under the `SiNetSQL.Services*` namespaces.
As of S4 (`docs/MASTER_PLAN_MIGRATION.md`) vault/credential helpers live under:

- `MasterPlan.SyncEngine.Shared`

Central logging is **not** vendored here: `Program.cs` uses
`SiNet.Infrastructure.Logging` via ProjectReference (same module as V2 / AccService).

Vault key names match `SiNet.Application.Configuration.SecretCatalog`
(e.g. `SiNet/MasterPlanApi/ApiKey`).
