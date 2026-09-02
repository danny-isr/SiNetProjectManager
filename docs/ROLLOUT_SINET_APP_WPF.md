# Rollout -- SiNet.App.Wpf (replaces V2 distribution)

> **Title:** Rollout checklist -- SiNet.App.Wpf
> **Date:** 02.08.2026
> **Updated:** 07.08.2026 (tip `127dc0e` / 1.0.23; checkboxes remain unchecked)
> **Status:** Active / Operational checklist (phases not signed off)
> **Classification:** Active checklist -- **unchecked does not mean Done**
> **Scope:** Pilot / expand phases and sign-off log for App.Wpf. Distinguishes repo tip from ops install evidence.
> **Reconciliation:** [`DOCUMENTATION_RECONCILIATION_2026-08-07.md`](./DOCUMENTATION_RECONCILIATION_2026-08-07.md)
>
> Related: [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md),
> [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) (publish gates),
> [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) (post-publish watch),
> [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) (PROD vs DEV),
> [`manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md`](./manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md)

## Decision

- Production desktop app = **`SiNet.App.Wpf` only**.
- `SiNetProjectManagerV2` stays in the repo; **not** published.
- Safety net until sign-off = the **external legacy system** (outside this repo), not V2.

## Repo vs ops evidence (do not infer Done)

| Evidence | Status (2026-08-07) | Notes |
| --- | --- | --- |
| Repo tip `origin/release` == `origin/development` | Verified | Commit `127dc0e` -- ship App.Wpf **1.0.23** |
| Ship commits / publish scripts target App.Wpf | Verified in repo | Channel 3 = `SiNet.App.Wpf` |
| UNC share install on pilot PCs | **Needs Review** | Not proven by docs alone |
| Interactive smoke checklist | **Not Run** / unchecked below | Operator must sign |
| Phase checkboxes below | **Unchecked** | Keep unchecked until ops completes each item |

## Phases

### Phase 0 -- Ops gates

- [ ] MasterPlan API key rotated + old key revoked ([`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md))
- [ ] DB backup + restore drill + AUTO_CLOSE/RCSI ([`OPS-P0-DB-BACKUP.md`](./OPS-P0-DB-BACKUP.md))
- [ ] Local full `SiNet.sln` gate green on DEV (siblings, Debug+Release, tests, secret-scan) — **not** GitHub Actions
- [ ] Signed MSIX published to `\\SI-WIN-2K19\AppFolder\AppNet\SiNet.App.Wpf\`

### Phase 1 -- Pilot (1-2 users)

- [ ] Install from `.appinstaller` on pilot machines
- [ ] Complete smoke checklist
- [ ] Pilots keep the external legacy system available
- [ ] Collect blockers for 2-3 working days

### Phase 2 -- Expand

- [ ] Remaining users install App.Wpf
- [ ] Confirm no one depends on V2 MSIX share
- [ ] Owner sign-off to retire external legacy system (separate decision)

## Sign-off log

| Date | Phase | Owner | Result | Notes |
| --- | --- | --- | --- | --- |
| | | | | |
