# SyncEngine Shared facilities

Formerly vendored from SiNetSQL under the `SiNetSQL.Services*` namespaces.
As of S4 (`docs/MASTER_PLAN_MIGRATION.md`) these types live under:

- `MasterPlan.SyncEngine.Shared`
- `MasterPlan.SyncEngine.Shared.Logging`

They remain **local copies** for the console host (Task Scheduler). Vault key names
match `SiNet.Application.Configuration.SecretCatalog` (e.g. `SiNet/MasterPlanApi/ApiKey`).

Follow-up (not this slice): cut over logging to `SiNet.Infrastructure.Logging` package
reference and delete the Shared logging copy.
