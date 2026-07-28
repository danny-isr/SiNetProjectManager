# P2 technical-debt backlog

**Status date:** 2026-07-28  
**Source:** [AUDIT-Architecture-Migration-2026-07-27.md](./AUDIT-Architecture-Migration-2026-07-27.md),
updated by the round-2 remediation ([AUDIT-REMEDIATION-MATRIX-2026-07-28.md](./AUDIT-REMEDIATION-MATRIX-2026-07-28.md)).

All items below are **Pending** unless noted.

## UI / orchestration

| Item | Notes | Status |
| --- | --- | --- |
| Finish thinning `OpenQuoteProjectDecisionDialog.xaml.cs` | Partial extract done via `IOpenQuoteProjectDecisionService`; remaining filing/create-project orchestration still in dialog | Pending |
| Split large ViewModels | `InspectionWindowViewModel`, `SettingsViewModel`, `ProjectWorkTreeViewModel`, `WorkflowVisualCanvasViewModel`, `EmailDetailViewModel`, `TaskWorkbenchViewModel`, `EmailListViewModel` — one composition VM + use-case coordinators | Pending |
| Async shell construction | `NewShellFactory.RunSync` removed; `CreateShellAsync` + `BuildMigratedOnlyMenuAsync` await the authorization and profile ports | **Done (2026-07-28)** |
| Async Legacy startup path | `RunLegacyStartup` is still synchronous, so `LoadManagementSettingsFromDbAsync` and the default-project recovery in `HandleDefaultProjectFailure` are still bridged with `Task.Run(...).GetAwaiter().GetResult()`. The New System path is fully awaited. | Pending |
| Native Secret Setup at startup | The New System startup still opens the legacy `WPF_Window.SecretSetupWindow` for vault/DB repair. Blocked on ordering, not on a missing screen — see [APP_SHELL.md](./APP_SHELL.md) §3 "Legacy dialogs in the New System startup path". The provisioning password prompt already uses the native window. | Pending (decision needed) |

## Domain / namespaces

| Item | Notes | Status |
| --- | --- | --- |
| Expand Domain by stable invariants only | Do not clone EF entities; move pure rules when duplicated | Pending |
| Clean `SiNetSQL.*` namespaces inside Infrastructure.Sql | After consumers retire. Two files currently fail `InfrastructureSqlBoundaryTests`: `Services/Identity/SqlUserGroupQueryService.cs` and `SqlUserGroupCommandService.cs` (both use `SiNetSQL.Data`) | Pending |
| Local ACC namespace cleanup in Sql | Implementations live under Sql after Autodesk→Sql decoupling | Pending |
| `SiNet.App.Wpf` → `SiNet.Infrastructure.Secrets` reference | Cannot be removed as planned: the Secrets module targets `net10.0-windows` (Windows Credential Manager) and `SiNet.App.Composition` is platform-neutral `net10.0`. Constrained instead by `WpfSecretsBoundaryTests` to this project's own composition root (`App.xaml.cs`). Revisit if Composition is ever split per-platform. | Pending (constrained) |

## Build determinism

| Item | Notes | Status |
| --- | --- | --- |
| SDK pin | `global.json` pins SDK `10.0.301` with `rollForward: latestFeature` | **Done (2026-07-28)** |
| Package source pin | `NuGet.config` clears inherited feeds and restores from nuget.org only | **Done (2026-07-28)** |
| Central Package Management | `Directory.Packages.props` — all versions exact, `CentralPackageTransitivePinningEnabled` on | **Done** |
| `packages.lock.json` | **Not enabled, deliberately.** CPM with exact versions plus transitive pinning already makes the resolved graph deterministic; lock files would add hash verification at the cost of a regenerated lock file per project on every dependency change. Enable only together with a CI `--locked-mode` restore, otherwise the files rot silently. | Pending (decision recorded) |
| Sibling repository pins | `build/sibling-pins.json` + `build/fetch-siblings.ps1` — see [BUILD_SIBLING_PINS.md](./BUILD_SIBLING_PINS.md) | **Done (2026-07-28)** |

## Tests / ops

| Item | Notes | Status |
| --- | --- | --- |
| Integration tests | See [manual-tests/INTEGRATION_TEST_PLAN-P2.md](./manual-tests/INTEGRATION_TEST_PLAN-P2.md) — SQL/migration restore, ACC TLS/API, MasterPlan sync locks | Pending |
| DBA: `AUTO_CLOSE OFF` | SiData + Replica_DB on permanent server. Report script ready: `scripts/db/check-database-settings.ps1` (not run) | Pending |
| DBA: evaluate RCSI | Staging blocking analysis before enabling `READ_COMMITTED_SNAPSHOT` | Pending |
| DB restore rehearsal | Scripts ready (`scripts/db/backup-baseline.ps1`, `restore-rehearsal.ps1`, `apply-replica-migrations.ps1`); [checklist](./manual-tests/DB_RESTORE_REHEARSAL_CHECKLIST.md) is Not Run | Pending |
| MasterPlan API key rotation | Manual Pending — see [OPS-P0-SECRET-ROTATION.md](./OPS-P0-SECRET-ROTATION.md). No Git history rewrite by decision. | Pending |
| Interactive New System smoke | [NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md) — currently Not Run | Pending |
| Pre-existing failing tests | 7 tests fail on HEAD, unrelated to this round: 2 `InfrastructureSqlBoundaryTests` (row above), 3 shell-menu tests, 1 email-list XAML assertion, 1 workflow-viewer assertion | Pending |

## Completed in remediation S5–S6 (reference)

- Autodesk project no longer references Infrastructure.Sql (Local ACC SQL services + AccFileStore moved).
- Sync mutex uses `sp_getapplock` (session-scoped).
- MasterPlan.SyncEngine.csproj cleaned; Shared sources vendored.
- Central Package Management introduced (`Directory.Packages.props`).

## Completed in remediation round 2 (2026-07-28)

- `SiNet.Infrastructure.Sql` no longer references `SiOffice.AutodeskConnector` (port `IAccProjectRootFolderIdReader`).
- ACC control-plane TLS pins are bound from configuration in both composition roots.
- API key fingerprints (`keyLength` / `keyHashPrefix`) removed from all startup and request logs.
- `SiNetProjectManagerV2` composes through `AddSiNet(SiNetHostMode.V2Hybrid, ...)`.
- Enumerable DI registrations made idempotent (`IProcessActionHandler`, `IFileStore`, ProjectWork resolvers).
