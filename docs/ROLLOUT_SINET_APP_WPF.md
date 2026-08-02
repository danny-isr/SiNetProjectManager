# Rollout — SiNet.App.Wpf (replaces V2 distribution)

> Related: [`DESKTOP_CUTOVER.md`](./DESKTOP_CUTOVER.md),  
> [`RELEASE_PROCESS.md`](./RELEASE_PROCESS.md) (publish gates),  
> [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md) (post-publish watch),  
> [`ENVIRONMENTS.md`](./ENVIRONMENTS.md) (PROD vs DEV),  
> [`manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md`](./manual-tests/SMOKE_CUTOVER_SINET_APP_WPF.md)

## Decision

- Production desktop app = **`SiNet.App.Wpf` only**.
- `SiNetProjectManagerV2` stays in the repo; **not** published.
- Safety net until sign-off = the **external legacy system** (outside this repo), not V2.

## Phases

### Phase 0 — Ops gates

- [ ] MasterPlan API key rotated + old key revoked ([`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md))
- [ ] DB backup + restore drill + AUTO_CLOSE/RCSI ([`OPS-P0-DB-BACKUP.md`](./OPS-P0-DB-BACKUP.md))
- [ ] CI green on push/PR
- [ ] Signed MSIX published to `\\SI-WIN-2K19\AppFolder\AppNet\SiNet.App.Wpf\`

### Phase 1 — Pilot (1–2 users)

- [ ] Install from `.appinstaller` on pilot machines
- [ ] Complete smoke checklist
- [ ] Pilots keep the external legacy system available
- [ ] Collect blockers for 2–3 working days

### Phase 2 — Expand

- [ ] Remaining users install App.Wpf
- [ ] Confirm no one depends on V2 MSIX share
- [ ] Owner sign-off to retire external legacy system (separate decision)

## Sign-off log

| Date | Phase | Owner | Result | Notes |
| --- | --- | --- | --- | --- |
| | | | | |
