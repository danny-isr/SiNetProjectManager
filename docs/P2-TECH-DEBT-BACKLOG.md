# P2 technical-debt backlog

**Status date:** 2026-07-27  
**Source:** [AUDIT-Architecture-Migration-2026-07-27.md](./AUDIT-Architecture-Migration-2026-07-27.md)

All items below are **Pending** unless noted.

## UI / orchestration

| Item | Notes | Status |
| --- | --- | --- |
| Finish thinning `OpenQuoteProjectDecisionDialog.xaml.cs` | Partial extract done via `IOpenQuoteProjectDecisionService`; remaining filing/create-project orchestration still in dialog | Pending |
| Split large ViewModels | `InspectionWindowViewModel`, `SettingsViewModel`, `ProjectWorkTreeViewModel`, `WorkflowVisualCanvasViewModel`, `EmailDetailViewModel`, `TaskWorkbenchViewModel`, `EmailListViewModel` — one composition VM + use-case coordinators | Pending |
| Async shell construction | Remaining `RunSync` in `NewShellFactory` for profile display | Pending |

## Domain / namespaces

| Item | Notes | Status |
| --- | --- | --- |
| Expand Domain by stable invariants only | Do not clone EF entities; move pure rules when duplicated | Pending |
| Clean `SiNetSQL.*` namespaces inside Infrastructure.Sql | After consumers retire | Pending |
| Local ACC namespace cleanup in Sql | Implementations live under Sql after Autodesk→Sql decoupling | Pending |

## Tests / ops

| Item | Notes | Status |
| --- | --- | --- |
| Integration tests | See [manual-tests/INTEGRATION_TEST_PLAN-P2.md](./manual-tests/INTEGRATION_TEST_PLAN-P2.md) — SQL/migration restore, ACC TLS/API, MasterPlan sync locks | Pending |
| DBA: `AUTO_CLOSE OFF` | SiData + Replica_DB on permanent server | Pending |
| DBA: evaluate RCSI | Staging blocking analysis before enabling `READ_COMMITTED_SNAPSHOT` | Pending |
| Interactive New System smoke | [NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md) — currently Not Run | Pending |

## Completed in remediation S5–S6 (reference)

- Autodesk project no longer references Infrastructure.Sql (Local ACC SQL services + AccFileStore moved).
- Sync mutex uses `sp_getapplock` (session-scoped).
- MasterPlan.SyncEngine.csproj cleaned; Shared sources vendored.
- Central Package Management introduced (`Directory.Packages.props`).
