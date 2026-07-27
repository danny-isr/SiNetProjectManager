# GitHub Remediation Board — Staging Plan S0–S7

**Created:** 2026-07-27  
**Source audit:** [AUDIT-Architecture-Migration-2026-07-27.md](./AUDIT-Architecture-Migration-2026-07-27.md)  
**Purpose:** Tracking board when `gh` CLI is unavailable locally. Create matching Milestones/Issues on GitHub when authenticated.

**Local status (2026-07-27):** Implementation for S0–S7 landed in the working tree. `git`/`gh` were not on PATH in the agent shell, so remote Milestones/Issues/PRs were not created automatically — run the bootstrap commands below on a machine with GitHub CLI authenticated.

## Milestones

| Milestone | Stages | Goal |
| --- | --- | --- |
| Production gates P0 | S1–S3 | Block production promotion until secrets/ACC, readiness/DB, CI pass |
| Architecture P1 | S4–S6 | HostMode composition, infra/UI ports, sync lock + build hygiene |
| Tech debt P2 | S7 | ViewModels, Domain invariants, integration tests, DBA |

## Issues

| ID (local) | Title | Milestone | Branch | Primary files |
| --- | --- | --- | --- | --- |
| S0 | Persist audit + board | — | `docs/audit-architecture-migration-2026-07-27` | `docs/AUDIT-Architecture-Migration-2026-07-27.md`, this file |
| S1 | P0 secrets + ACC TLS/diag | Production gates P0 | `security/p0-secrets-acc` | MasterPlan.SyncEngine, SiOffice.AccService, Autodesk TLS clients |
| S2 | P0 readiness + DB freeze | Production gates P0 | `docs/p0-readiness-db-freeze` | `NEW_SYSTEM_PRODUCTION_READINESS.md`, `DATABASE_RECOVERY_BASELINE.md`, smoke checklist |
| S3 | CI SiNet.sln gate | Production gates P0 | `ci/sinet-sln-gate` | `.github/workflows/ci.yml`, `SiNet.sln` |
| S4 | Composition HostMode | Architecture P1 | `arch/composition-hostmode` | `SiNetCompositionExtensions.cs`, V2 `App.xaml.cs` |
| S5 | Infra/UI ports | Architecture P1 | `arch/infra-ui-ports` | Autodesk↔Sql, NewShellFactory, DevToolsCoordinator |
| S6 | Sync lock + build hygiene | Architecture P1 | `fix/sync-lock-build-hygiene` | SyncEngine lock, csproj, CPM |
| S7 | P2 debt slices | Tech debt P2 | per-slice | ViewModels, Domain, integration tests, DBA notes |

## gh bootstrap (run when authenticated)

```powershell
gh milestone create "Production gates P0" --description "P0 production blockers S1-S3"
gh milestone create "Architecture P1" --description "Composition, ports, sync S4-S6"
gh milestone create "Tech debt P2" --description "P2 debt S7"

gh issue create --title "S1: P0 secrets and ACC security" --milestone "Production gates P0" --body "See docs/AUDIT-Architecture-Migration-2026-07-27.md §9 and docs/OPS-P0-SECRET-ROTATION.md"
gh issue create --title "S2: P0 readiness and DB recovery freeze" --milestone "Production gates P0" --body "See docs/NEW_SYSTEM_PRODUCTION_READINESS.md and docs/DATABASE_RECOVERY_BASELINE.md"
gh issue create --title "S3: CI gate on SiNet.sln" --milestone "Production gates P0" --body "Add GitHub Actions restore/build/test + secret scan"
gh issue create --title "S4: Composition HostMode" --milestone "Architecture P1" --body "Unify DI; LegacyBridge opt-in; schema gate; no legacy dialogs on New System"
gh issue create --title "S5: Remove Autodesk->Sql and UI infra leaks" --milestone "Architecture P1" --body "Ports for ACC local mode, watchdog, EF exceptions, Secrets"
gh issue create --title "S6: Sync sp_getapplock and build hygiene" --milestone "Architecture P1" --body "Fix Sync_Lock race; clean SyncEngine.csproj; CPM; sibling pins"
gh issue create --title "S7: P2 tech debt slices" --milestone "Tech debt P2" --body "ViewModels split, Domain invariants, integration tests, DBA AUTO_CLOSE/RCSI"
```
