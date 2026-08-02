# Master Plan → Standalone New System

> Status: **Approved — S2 mapping done · S3 reports native · S4 hygiene done**  
> Date: 2026-07-28  
> Approved by: operator (chat 2026-07-28)  

> Related: [`STANDALONE_NEW_SYSTEM_HOST.md`](./STANDALONE_NEW_SYSTEM_HOST.md),
> [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md),
> [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md),
> [`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md),
> [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md),
> [`DATABASE_RECOVERY_BASELINE.md`](./DATABASE_RECOVERY_BASELINE.md),
> [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md) — SyncEngine watermark
> semantics, hours lookback window and weekly reconciliation

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
  ├─ Reports R01–R03               ← S3 native (User OAuth + Spreadsheets)
  └─ (no SyncEngine inside WPF)

MasterPlan.SyncEngine (Task Scheduler)
  └─ API → Replica → CrossSync     ← S4 done (namespaces + Infrastructure.Logging); stay separate process
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

**Boundary (approved):** Shared User OAuth on `GmailClientProvider` + `Spreadsheets` scope
(see [`GOOGLE_BOUNDARY.md`](./GOOGLE_BOUNDARY.md)). No service account in this slice. No App.Wpf →
V2 / SiNetSQL / GoogleConnector ProjectReference.

**Phases:**
- **S3a:** Sheets/Drive helpers + native R03 (Replica only)
- **S3b:** Native R01 (Replica/MasterPlan + template)
- **S3c:** Native R02 — one row per hour report (not aggregated); MasterPlan
  `HoursReports` + Replica `MP_ProjectHoursExtended` (Description + SubContract /
  תת-חוזה); fallback `MP_ProjectHours` when Extended missing
- **S3d:** Native R03 in-app DataGrid preview (**הצג נתונים**) via `PreviewAsync` — same Replica
  build as Sheets, no Google required for preview. Non-management users see **self only**
  (`MasterPlanEmployeeId`); management gets checklist multi-select (select all / clear / partial).
  NewShell exposes R03 to every authenticated user; Sheets generate stays management-only.

**In:** Application ports, Sql repos over vault Replica/MasterPlan CS, NewShell **דוחות** menu
(`ReportsManagement`), GoogleReports folder/template ids from config.

**Out:** Changing Replica KPI schema; SyncEngine; deleting V2 dialogs until soak; Inspection Sheets.

**SyncEngine vs App.Wpf (locked):** Daily/monthly MasterPlan → Replica → SiData CrossSync remains
`MasterPlan.SyncEngine.exe` under Task Scheduler. New System already hosts **מיפוי MasterPlan**
(mapping + Full Sync flags) and R01–R03. Embedding SyncEngine inside the WPF process is **out of
scope** unless product explicitly re-opens that decision.

**DB/schema:** None.

### S4 — SyncEngine hygiene (parallel ops track)

**In (delivered):**
- Renamed Shared off `SiNetSQL.Services*` → `MasterPlan.SyncEngine.Shared` (vault/credentials)
- Cut over central logging to `SiNet.Infrastructure.Logging` ProjectReference; removed Shared/Logging copy
- Console host + Task Scheduler unchanged (not embedded in App.Wpf)
- Vault key strings unchanged (`SecretCatalog` parity for MasterPlan API key)

**Still open:**
- Ops: MasterPlan API key rotation ([`OPS-P0-SECRET-ROTATION.md`](./OPS-P0-SECRET-ROTATION.md) — still Not done)

**Out:** Rewriting daily/monthly sync algorithms unless a defect requires it.

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
