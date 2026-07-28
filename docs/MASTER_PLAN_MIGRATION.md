# Master Plan → Standalone New System

> Status: **Approved — S2 mapping implemented (awaiting operator smoke)**  
> Date: 2026-07-28  
> Approved by: operator (chat 2026-07-28)  

> Related: [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md),
> [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md),
> [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md),
> [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md),
> [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md),
> [`DATABASE_RECOVERY_BASELINE.md`](./DATABASE_RECOVERY_BASELINE.md)

## Problem

Standalone `SiNet.App.Wpf` already links **MasterPlan employees** to users (vault + SQL lookup).
Operators still need **company/contact mapping**, **hours/attendance reports (R01–R03)**, and a
healthy **SyncEngine** ops story — today those live in V2 / SiNetSQL / GoogleConnector / the
scheduled SyncEngine console.

Constraint (locked): App.Wpf has **no** `ProjectReference` to SiNetSQL or V2. Surfaces without a
native adapter stay **hidden** until reimplemented.

## Sources of truth (do not reopen)

| Concern | Source of truth |
| --- | --- |
| MasterPlan business data (API) | MasterPlan Web API → daily sync into **Replica** `MP_*` |
| Mapping flags on SiNet companies/contacts | SiData `MasterPlanCompanyId` / `MasterPlanContactId` + `MasterPlanSync` |
| User ↔ employee link | SiData `SIUser.MasterPlanEmployeeId` (already native) |
| Physical project files | ACC (unchanged; not MasterPlan) |

CrossSync (SyncEngine) copies mapped + `MasterPlanSync=1` rows from Replica into SiData helpers.
Mapping UI writes the mapping/flags; SyncEngine must not be folded into the WPF process.

## Already in New System (do not re-port)

| Capability | Where |
| --- | --- |
| Employee lookup for user admin | `IMasterPlanEmployeeLookupService` / `SqlMasterPlanEmployeeLookupService` |
| Vault CS (Replica + MasterPlan DB) + API key | `SecretCatalog` + `VaultMasterPlanEmployeeConnectionProvider` |
| User UI ComboBox | `Admin/Users/*` |
| Schema fields | `Company` / `Contact` / `SIUser` MasterPlan* columns |

## Target state (after slices)

```text
SiNet.App.Wpf
  ├─ Users: employee link          ✅ done
  ├─ MasterPlan mapping surface    ← S2 native WPF + Application ports
  ├─ Reports R01–R03               ← S3 (needs Google/Sheets boundary)
  └─ (no SyncEngine inside WPF)

MasterPlan.SyncEngine (Task Scheduler)
  └─ API → Replica → CrossSync     ← S4 hygiene (namespaces / logging); stay separate process
```

## Slice plan

### S1 — This document (approval gate)

**In:** Inventory, SoT, slice order, risks, acceptance.  
**Out:** Code.  
**Risk:** Low.

### S2 — Company/Contact mapping surface ✅ implemented

**In (delivered):**
- Port `IMasterPlanMappingService` + `SqlMasterPlanMappingService` (Replica `MP_*` + SiData EF)
- `MasterPlanAutoMatchEngine` (threshold ≥ 6)
- Native `MasterPlanMappingWindow` under App.Wpf; NewShell → מנהלה → מיפוי MasterPlan (`SystemSettingsWrite`)
- Commands: Load, AutoMatch, Clear, Apply, CompleteMissing, EnableFullSync, Export/Import JSON
- Vault `ReplicaDatabase` via `IMasterPlanEmployeeConnectionProvider`

**Out (still):**
- AiMatch / Gemini
- R01–R03, SyncEngine algorithm changes
- Wrapping SiNetSQL `MasterPlanMappingViewModel`

**DB/schema:** None.

### S3 — Reports R01 / R02 / R03

**In:** Native surfaces (or thin hosts) for portfolio/hours/attendance → Google Sheets where product requires; vault CS; align Google auth with native module.

**Out:** Changing Replica KPI schema; SyncEngine.

**Risk / effort:** High — blocked/deferred by [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md) Sheets ownership until approved. Prefer after S2.

### S4 — SyncEngine hygiene (parallel ops track)

**In:**
- De-vendor Shared off `SiNetSQL.*` namespaces toward Application / Infrastructure.Logging
- Keep **console host + Task Scheduler** (do not embed in App.Wpf)
- Ops: document schedule; complete MasterPlan API key rotation ([`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md) — still Not done)

**Out:** Rewriting daily/monthly sync algorithms unless a defect requires it.

**Risk / effort:** Medium — deploy path on `SI-WIN-2K19`; CrossSync lock already hardened.

## Suggested order

1. **Approve this doc (S1)**  
2. **S2 mapping** (highest operator value in App.Wpf)  
3. **S4 SyncEngine hygiene** in parallel with ops (key rotation)  
4. **S3 reports** after Google/Sheets boundary approval  

## Explicitly out of this migration program

- Folding SyncEngine into App.Wpf
- Re-enabling V2 New System host for MasterPlan
- ACC / Email N3 recovery (separate track)

## Approval gate

**Do not implement S2+ until this document is explicitly approved** (record scope tweaks below).

### Approval notes

- Approved by: operator
- Date: 2026-07-28
- Scope tweaks: none
- First code slice: **S2 mapping**
