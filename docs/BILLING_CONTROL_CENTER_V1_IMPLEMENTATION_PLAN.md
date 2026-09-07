# Billing Control Center V1 — Implementation Plan

> **Title:** Billing Control Center V1 — decision layer over MasterPlan Replica  
> **Date:** 06.09.2026  
> **Updated:** 07.09.2026 (B4.1 UI polish + Healthy visual fixture)  
> **Status:** Active (B0–B4 accepted; B4.1 polish; B5 not started)  
> **Scope:** New System WPF (`SiNet.App.Wpf`) operational Billing Control Center. Replica_DB is the primary current-fact store. Monthly `Db_Mp_SiEng` is enrichment only. Local SiNet rows are supplemental workflow notes after explicit human action.  
> **Target application:** New System WPF (`SiNet.App.Wpf`)  
> **Primary goal:** A reliable management screen that answers “Which projects should we review now for billing?” without creating a second financial system alongside MasterPlan.

Related: [`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md), [`DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md`](./DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md), [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md), [`APP_SHELL.md`](./APP_SHELL.md), [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md).

---

## Existing mechanisms (reuse — do not invent a parallel stack)

| Mechanism | Location / role |
| --- | --- |
| Replica vs live SQL selection | `MasterPlanReportSqlSourceResolver` — R01/R02 may last-resort to live MP if Replica is missing; **Billing must call `RequireReplica` only** |
| Hours normalization | `SqlR02ReportDataSource.ConvertHoursRaw` — Duration 0–24, else TotalHours, else Start/End; ticks/ms/minutes heuristics. **Extract to `MasterPlanHoursNormalizer` and share** |
| Replica table schemas | `MasterPlan.SyncEngine/Scripts/CreateReplicaTables.sql` + `MonthlyBackupRestoreService.CreateReplicaSchemaAsync` |
| `MP_ProjectHoursExtended` | Canonical hours + SubContract/Step/Description; SyncEngine entity `ProjectHoursExtended` |
| `Sync_State` | Entity watermarks: `Projects`, `Bills`, `Intakes`, `ProjectHours`, `ProjectHoursExtended` |
| Vault connection strings | `IMasterPlanEmployeeConnectionProvider` (`ReplicaDatabase`, `MasterPlanDatabase`) |
| SQL DI for MasterPlan reads | `AddSiNetUserManagementSql` currently registers R01/R02/R03 data sources |
| Feature-code authorization | `AppFeatureCodes` + `AppFeatureAuthorization` + coverage tests (Billing menu is **B4**, not B0/B1) |
| R01 portfolio | Replica `MP_Projects` for current facts; monthly `ProjectsExtraData` KPIs exist only on live MP — **do not copy R01 live-first history** |

---

## 1. Executive decision

The Billing Control Center is a **decision and control layer** over MasterPlan data.
It must not duplicate financial facts that already exist in MasterPlan.

The implementation must follow this data precedence:

1. **Replica_DB current facts — PRIMARY**  
   `MP_Projects`, `MP_Bills`, `MP_ProjectHoursExtended`, `MP_Intakes`
2. **Monthly restored MasterPlan snapshot — ENRICHMENT ONLY**  
   `ProjectsExtraData`, Contracts / SubContracts / fee type information, other fields not currently available through the Replica/API
3. **SiNet local workflow metadata — SUPPLEMENTAL ONLY**  
   reviewed / hold / reason / review-again date / responsible user

**Rule:** Replica fact > Monthly snapshot > SiNet supplemental workflow.  
A monthly snapshot value must never override a newer Replica fact.

---

## 2. Why Replica_DB is the primary source

Reality checks on the production-like data confirmed that Replica contains the complete monthly baseline plus newer rows:

| Entity | Monthly snapshot | Replica | Missing from Replica | Replica only |
| --- | ---: | ---: | ---: | ---: |
| Projects | 1,461 | 1,467 | 0 | 6 |
| Bills | 3,053 | 3,085 | 0 | 32 |
| Intakes | 2,773 | 2,807 | 0 | 34 |
| ProjectHoursExtended | 22,374 | 22,727 | 0 | 353 |

The daily sync updates Replica through the MasterPlan API. The monthly restore is the baseline, not the freshest operational store.

For project hours, parity testing showed:

- `OnlyInBasic = 0`
- `OnlyInExtended = 23`

Therefore **`MP_ProjectHoursExtended` is the canonical V1 hours source**. It currently contains all rows from `MP_ProjectHours` plus additional rows and also provides SubContract / Step / Description data.

Add a health check so that if `OnlyInBasic > 0` in the future, the system surfaces a sync warning.

---

## 3. Product scope for V1

### 3.1 Main question

The first screen must answer: **Which projects should management review now for submitting a bill?**  
The system recommends review, not an automatic bill amount.

### 3.2 V1 screens

Create a new top-level operational area named **מרכז חיובים**.

Recommended tabs:

- מועמדים לחשבון
- חשבונות בתהליך
- גבייה
- כל הפרויקטים

V1 may ship the first tab first, then add the other tabs incrementally.

### 3.3 V1 is not

Do not build any of the following in the first implementation:

- automatic bill creation in MasterPlan
- automatic suggested monetary bill amount
- a duplicate invoice/payment ledger in SiNet
- a generic 0–100 “AI score”
- dependency on PaymentsStep
- dependency on manually maintained `SubContractSteps.Progress`
- hard business logic based on stale monthly balance/progress values
- broad changes to existing R01/R02 behavior before the Billing Center is working

---

## 4. Business state model

### 4.1 MasterPlan financial facts

MasterPlan remains authoritative for financial lifecycle facts.

Known bill statuses:

| Id | Display |
| ---: | --- |
| 1 | ביצירה |
| 2 | הוגש |
| 3 | אושר |
| 4 | סגור |

Intake means money actually received.  
SiNet must not manually move a bill between these states.

### 4.2 Candidate states in SiNet

Use a small, explainable state resolver rather than a score.

```csharp
public enum BillingCandidateState
{
    ReviewNow,
    AccumulatedWork,
    CoveredByLatestBill,
    BillInPreparation,
    NotUrgent
}
```

| State | Display |
| --- | --- |
| ReviewNow | לבדוק עכשיו |
| AccumulatedWork | עבודה שהצטברה |
| CoveredByLatestBill | אין עבודה חדשה מאז החשבון |
| BillInPreparation | כבר יש חשבון ביצירה |
| NotUrgent | לא דחוף |

### 4.3 Initial resolver rules

Priority of rules matters.

```text
IF BillsInCreation > 0
    => BillInPreparation
ELSE IF HoursSinceLastBill <= 0 AND a real last bill exists (StatusID IN 2,3,4)
    => CoveredByLatestBill
ELSE IF Hours30 > 0
    => ReviewNow
ELSE IF HoursSinceLastBill > 0
    => AccumulatedWork
ELSE
    => NotUrgent
```

`CoveredByLatestBill` requires a real last bill. A project with no hours and no real bill is `NotUrgent`, not “covered”.

Do **not** use `BillsSubmitted > 0` as a hard stop. Historical data contains projects with one or more submitted bills that can still legitimately need another billing review.

### 4.4 Sorting inside a state

Do not create a synthetic score in V1.

For `ReviewNow` (and as a secondary key for other states), sort by:

1. `HoursSinceLastBill` DESC
2. `WorkDays30` DESC
3. `Hours30` DESC
4. `DaysSinceLastBill` DESC

This is transparent and easy to validate with management.

---

## 5. Required read model

Create an application-level DTO/read model independent of WPF.

Location: `src/SiNet.Application/Billing/`

### 5.1 Core row

`BillingCandidateRow` — see the type in Application. Snapshot monetary fields are present on the contract from B0 so B2 can fill them; B0/B1 leave them null.

### 5.2 Summary model

`BillingDashboardSummary`:

- `ReviewNowCount`
- `BillInPreparationCount`
- `SubmittedOpenAmount` — **unavailable in B0/B1** (null until Replica semantics are proven)
- `ReceivedThisMonth` — global sum from `MP_Intakes` by `OpenDate` in the as-of month (intakes have **no** project/bill linkage in Replica)
- `ReplicaLastSyncTime`
- `MonthlySnapshotDate` — null until B2

---

## 6. Application interfaces

`src/SiNet.Application/Billing/IBillingDashboardReadService.cs`

```csharp
public interface IBillingDashboardReadService
{
    Task<BillingDashboardResult> GetAsync(
        BillingDashboardRequest request,
        CancellationToken cancellationToken = default);
}
```

`BillingDashboardRequest` supports:

- `ActiveOnly` (default true)
- optional project IDs
- optional customer IDs
- optional candidate states
- `AsOfDate` (tests / deterministic windows; default local today)

The service returns summary, candidate rows, source freshness metadata, and warnings / data-quality flags.

---

## 7. Infrastructure design

Location: `src/SiNet.Infrastructure.Sql/Services/Billing/`

### 7.1 Components

| Component | Layer | B0/B1 |
| --- | --- | --- |
| `IBillingDashboardReadService` / DTOs / enum | Application | yes |
| `BillingCandidateStateResolver` | Application (pure rules; no SQL) | yes |
| `BillingCandidateEngine` | Application (aggregation + reasons + sort) | yes |
| `SqlBillingDashboardReadService` | Infrastructure | yes |
| `ReplicaBillingDataSource` | Infrastructure | yes |
| `MasterPlanHoursNormalizer` | Infrastructure (shared with R02) | yes |
| `MonthlyBillingEnrichmentDataSource` | Infrastructure | **B2** |
| local decision table | EF | **B5** |

Keep SQL access behind the Application interface.

### 7.2 Replica source — authoritative current facts

| Table | Use |
| --- | --- |
| `MP_Projects` | Project ID / number / name, current customer name when populated, current project status, `IsActive`, current `FeeSum` |
| `MP_ProjectHoursExtended` | last work date, Hours30/60/90, HoursSinceLastBill, WorkDays30, later drill-down by employee / SubContract / SubContractStep |
| `MP_Bills` | latest bill, last real bill, bill status, bills in creation, submitted/approved counts |
| `MP_Intakes` | current receipt activity / received amount at **customer/global** level |

Do not pretend that `MP_Intakes` currently has project/bill linkage if it does not.

**Billing never falls back to live `Db_Mp_SiEng` for current facts.** Missing Replica configuration is a hard failure (`RequireReplica`).

### 7.3 Last bill definition

For `HoursSinceLastBill`, the last bill that resets the work period is:

`StatusID IN (2, 3, 4)`

A bill in ביצירה does not reset the work-since-last-bill period.

**Timeline date (assumption, B1):** `COALESCE(SubmitDate, LastUpdated)`.  
**Hours included after that bill:** `ReportDate > lastRealBillDate` (work **on** the last bill date is treated as already covered).

`LatestBill*` on the row is the most recent bill of **any** status (including ביצירה). `LastBillDate` / `DaysSinceLastBill` / `HoursSinceLastBill` use the last **real** bill only.

### 7.4 Hours normalization

Do not invent a new hours conversion algorithm.

Reuse/extract the existing R02 normalization behavior so Billing and R02 cannot disagree about hours.

Preferred implementation (B1):

- Extract the normalization logic from `SqlR02ReportDataSource` into `MasterPlanHoursNormalizer`.
- Use the same helper from R02 and Billing.
- For Replica Extended: valid decimal `Duration` first (0–24), otherwise `TotalHours`, otherwise Start/End fallback.

Do not fork two independently maintained conversion implementations.

**Rolling windows (assumption, B1):** Hours30/60/90 include the as-of date and the previous N calendar days: `[asOf.Date.AddDays(-N), asOf.Date]` inclusive. `WorkDays30` is the distinct `ReportDate` count in the 30-day window where normalized hours `> 0`.

---

## 8. Monthly enrichment source

Monthly snapshot fields are useful but stale by definition. Use them only as enrichment and label them clearly. **B2.**

Source precedence is unchanged:

1. Replica current facts (`MP_Projects`, `MP_Bills`, `MP_ProjectHoursExtended`, `MP_Intakes`)
2. Monthly snapshot enrichment (`Db_Mp_SiEng` via vault `MasterPlanDatabase`)
3. SiNet supplemental / local decisions (B5 — not this round)

B2 does **not** weaken the Replica freshness guard. A stale Development Replica may still block a **current** dashboard. B2 is validated with fixtures, isolated enrichment tests, and historical `AsOfDate` (which does not bypass structural Replica failures).

### 8.1 Fields (explicit snapshot names)

From `Db_Mp_SiEng.dbo.ProjectsExtraData` (never presented as current):

| Snapshot field | Source column |
| --- | --- |
| `SnapshotBalance` | `Balance` |
| `SnapshotOpenBillSum` | `OpenBillSum` |
| `SnapshotApprovedBillSum` | `ApprovedBillSum` |
| `SnapshotBilledPercent` | `ProgressPercentage` |
| `SnapshotDate` / summary `MonthlySnapshotDate` | see §8.4 |

Do **not** expose `CurrentBalance` / `CurrentOpenBillSum`. Do not copy `ProjectsExtraData.FeeSum` onto `CurrentFeeSum` (Replica `MP_Projects.FeeSum` wins). `PaymentsStep` remains unused. No suggested billing amount.

SQL `NULL` stays `null`. Stored `0` stays `0`.

### 8.2 Customer fallback

If Replica `CustomerName` is null/empty, fill from monthly `Projects` → `Contacts` → `Companies.Name` (same join as R01 live). A non-empty Replica name is never overwritten.

### 8.3 Fee-type summary (snapshot)

Live MasterPlan stores `FeeTypeID` on **`SubContracts`**, not on `Contracts`. Join `SubContracts` → `Contracts.ProjectID`.

Known `FeeTypes.ID`:

| ID | Name |
| --- | --- |
| 1 | אחוז מעלות |
| 2 | יחידות |
| 3 | מחיר קבוע |
| 4 | שעות עבודה |

Return per project: distinct IDs, counts per type, and mix `FixedPrice` (only 3) / `Hourly` (only 4) / `Mixed` (2+ distinct IDs) / `Other` (only 1, 2, or unknown). No contracts → `SnapshotFeeTypes` null. **Do not** derive a billing amount from fee type. **Do not** let fee type or `ProgressPercentage` drive `CandidateState`.

### 8.4 Snapshot / reference date

Defensible date only: Replica `Sync_State.LastSyncTime` for entity **`MonthlyRestore`**. SyncEngine stamps that row with the `.bak` `BackupFinishDate` from `RESTORE HEADERONLY` after a successful monthly restore.

- That stamp is the backup’s finish time — not “now”, not `AsOfDate`, and not `MAX(ProjectsExtraData.*)`.
- If the row is missing, `SnapshotDate` / `MonthlySnapshotDate` are **null** and warning `SnapshotDateUnknown` is raised. Do not invent a date from table watermarks or `restorehistory`.

### 8.5 Presentation rule

The UI must not label a monthly field as “current”.

- Correct: `יתרה לפי snapshot 02/08: ₪1,289,020`
- Incorrect: `יתרה נוכחית: ₪1,289,020`

### 8.6 Missing enrichment

Missing `MasterPlanDatabase`, missing `ProjectsExtraData`, or a project without an extra-data row must **not** drop a valid Replica candidate. Monetary snapshot fields stay null; Replica identity/status/hours/bills unchanged.

---

## 9. Data freshness and quality

The dashboard must surface source freshness.

### 9.1 Replica freshness

Read `Sync_State` for at least: Projects, Bills, Intakes, ProjectHoursExtended, ProjectHours / relevant reconciliation stamp.

Display a compact status such as:

- Replica synced: 06/09 03:05
- Latest project hours: 02/09
- Monthly snapshot: 02/08 (B2)

### 9.2 Hours parity health check

Diagnostic: `OnlyInBasic` = IDs in `MP_ProjectHours` not in `MP_ProjectHoursExtended`.

Current verified state is 0. If it becomes greater than 0:

- do not silently ignore it
- show a data-quality warning
- log it to application diagnostics (`IAppLogger.Warn`)

V1 does **not** union the tables while parity remains one-directional and Extended is a superset. Missing `MP_ProjectHoursExtended` is a hard failure, not a silent fallback to basic hours or live MasterPlan.

---

## 10. Local SiNet workflow metadata

Do not persist a row simply because a project appears as a candidate. Candidates are a calculated read model. Only create local state after an explicit human action.

Recommended actions: PrepareBill, Hold, NotRelevant.

**Do not add this persistence table in B0/B1.** Read-only candidate screen first (B3), then B5 after validation. Manual EF migration only. Do not auto-run Update-Database.

---

## 11. WPF integration

**B3 (accepted):** read-only `src/SiNet.App.Wpf/Billing/` — `BillingDashboardWindow` + `BillingDashboardViewModel`. Registered in `AddSiNetNewSystemWpf`. The surface consumes `IBillingDashboardReadService` only. It does not query Replica, `Db_Mp_SiEng`, or R01/R02. It does not bypass the Replica freshness guard. A stale Development Replica shows the blocking panel. Opening the window is not a failure when the panel appears.

**B4 (accepted):** New Shell top-level group **כספים** → **מרכז חיובים**. Feature code `AppFeatureCodes.ShellOpenBillingCenter` = `Shell.OpenBillingCenter`, minimum role **Management** (Administrator inherits; Employee denied). Not under **דוחות**, not `ReportsManagement`. Read-only through B4 — no Hold / Prepare / Not Now persistence. Release data-parity validation remains deferred.

**B4.1 (this round):** Blocked/fatal UI polish + Healthy visual validation. When `CandidatesBlocked` (or fatal/recoverable error), show **one** primary blocking message, keep header freshness + Refresh, hide KPI cards and all candidate filters, and let the blocking panel fill the content area (no empty grid). Default grid shows the operational core; secondary financial fields stay in the details panel. DEBUG-only Healthy fixture launches the real `BillingDashboardWindow` over a fake `IBillingDashboardReadService` — not a production Replica bypass. B5 is not started.

---

## 12. V1 UI design (B3)

Primary question: **אילו פרויקטים צריך לבדוק עכשיו לחשבון?**

| Area | Behavior |
| --- | --- |
| Header | Title מרכז חיובים; as-of date; Replica freshness; last sync; monthly snapshot date; Refresh |
| Warning | Banner when freshness is Warning; candidates still shown |
| Blocked | One primary panel when `CandidatesBlocked`; **not** an empty-candidate zero-state; headline is not duplicated in the header status line |
| Blocked chrome | KPI cards, text search, CandidateState filter, ActiveOnly, and Clear filters are **hidden** (not disabled). The blocking panel fills the remaining content area |
| KPI | Shown only when candidates are available. Counts from `BillingDashboardSummary`: לבדוק עכשיו, עבודה שהצטברה, חשבון בהכנה, מכוסה בחשבון האחרון, תקבולים החודש (גלובלי) |
| Grid (default) | Operational core: מצב, מספר, שם פרויקט, לקוח, שעות 30, שעות מאז חשבון, חשבון אחרון, ימים מאז חשבון, סטטוס חשבון אחרון, יתרה לפי snapshot, סיבה |
| Details-only | WorkDays30, CurrentFeeSum, snapshot open/approved/% billed, fee classification, last work date, hours 60/90, bills in flight |
| Filters | Shown only when candidates are available. Text search and CandidateState are **client-side**. **ActiveOnly** is a **service/query** filter (`BillingDashboardRequest`) and re-calls `IBillingDashboardReadService`. Default client filter: actionable states |
| Details | Three sections: Replica current / monthly snapshot / why this row |
| Refresh | Re-calls the read service only — does not run MasterPlan sync |

`SubmittedOpenAmount` stays hidden. Snapshot amounts: null → "—", never a fake 0. Unknown snapshot date → "תאריך snapshot לא ידוע".

---

## 13. Drill-down — second slice

After the candidate table works. Do not block V1 (and do not block B0/B1) on this drill-down.

---

## 14. Current code that must not be copied blindly

### 14.1 R01 source precedence

Current R01 uses the shared Replica-first resolver and only last-resorts to live MasterPlan when Replica is not configured. Billing is stricter: **Replica required; no live-MP current-fact path.**

Billing must not treat monthly `ProjectsExtraData` as current.

### 14.2 R02 date splitting

Do **not** revive the old R02 strategy of splitting a requested date range between MasterPlan and Replica based on the max date in MasterPlan. Current R02 is Replica-first via the shared resolver; Billing still must not copy any live-MP hours merge. Hours come from `MP_ProjectHoursExtended` in Replica. R02 **normalization** is reused; R02 **source-selection** must not drive Billing.

---

## 15. Testing strategy

Create resolver and aggregation tests before wiring the menu.

### 15.1 Resolver unit tests

At minimum:

- bill in creation + many hours => `BillInPreparation`
- no hours since latest **real** bill => `CoveredByLatestBill`
- Hours30 > 0 and hours since bill > 0 => `ReviewNow`
- no Hours30 but hours since bill > 0 => `AccumulatedWork`
- no work => `NotUrgent`

### 15.2 SQL / data-source tests

B0/B1 cover these as **in-memory engine tests** (same rules the SQL facts feed), plus source-code guards that the data source is Replica-only:

- latest bill per project
- last “real” bill excludes status 1
- HoursSinceLastBill excludes work on/before last bill date
- Hours30/60/90 windows
- WorkDays30 distinct-date count
- projects without previous bills
- monthly enrichment does not override Replica values (**B2**)

Live Replica smoke IDs (not production logic): 5905, 6982, 5893, 3611.

### 15.3 Shell tests

B4: feature-code coverage, authorization mapping, menu gating.

---

## 16. Implementation phases and acceptance criteria

| Phase | Deliver | B0/B1? |
| --- | --- | --- |
| **B0** — Documentation + contracts | this document; Application DTOs/interface/enum; resolver unit tests | **accepted** |
| **B1** — Replica-first backend | Replica data source; shared hours normalization; aggregation; freshness; candidate resolver | **accepted** |
| **B1.5** — Replica connection & freshness guard | Diagnosable Replica target; 36h/72h sync gate; no silent stale candidates | **accepted** |
| **B2** — Monthly enrichment | snapshot fields, snapshot date, customer fallback, fee-type summary | **accepted** |
| **B3** — Read-only WPF screen | dashboard window/VM, table, filters, freshness, reasons | **accepted** |
| **B4** — Shell + permissions | `Shell.OpenBillingCenter`, כספים > מרכז חיובים, Management | **accepted** |
| **B4.1** — UI polish + Healthy visual fixture | Single blocked message; hide KPI/filters while blocked; core grid columns; DEBUG Healthy fixture | **this round** |
| **B5** — Human decisions | Hold / Prepare / Not relevant; manual EF migration | after user validation of read-only screen |
| **B6** — Later Replica enrichment | API/Replica entities to reduce monthly snapshot dependence | post-V1 |

B0 acceptance: builds with no UI and no DB schema changes.  
B1 acceptance: backend returns candidate rows matching the validated SQL behavior; Replica is required; Extended is canonical hours.  
**B1.5 acceptance:** current dashboard requests never return candidates from a stale/unidentifiable Replica as though they are current; diagnostics never include credentials; server identity is informational only (no hostname allow-list).  
**B2 acceptance:** monthly `ProjectsExtraData` / fee-type fields are labelled as snapshot, never override Replica current facts, never drive `CandidateState`, and missing enrichment leaves Replica candidates intact. Replica freshness guard is unchanged.  
**B3 acceptance:** read-only WPF surface over `IBillingDashboardReadService`; blocked freshness is not shown as an empty candidate list.  
**B4 acceptance:** New Shell exposes **כספים → מרכז חיובים** gated by `Shell.OpenBillingCenter` (Management); Employee does not see the group; opening a stale Replica shows the B3 blocking panel; no B5 persistence.  
**B4.1 acceptance:** Blocked/fatal UI shows one primary message, no KPI/filter chrome, no empty grid hole; default grid is the operational core; Healthy layout validated via DEBUG fixture (not a Replica bypass); no B5 persistence.

---

## B1.5 — Replica connection & freshness guard

Billing must not silently treat a stale or redirected Replica as current. This slice does **not** start B2/WPF/EF.

### Existing mechanism (reuse)

| Mechanism | Role |
| --- | --- |
| `IMasterPlanEmployeeConnectionProvider.ReplicaDatabase` | Vault connection string (do not hardcode hosts) |
| `MasterPlanReportSqlSourceResolver.RequireReplica` | Billing already refuses live-MP fallback |
| `Sync_State.LastSyncTime` | Daily sync stamp per entity (`GETUTCDATE()` in SyncEngine) |
| `BillingSourceFreshness` | Compact stamps already returned by B1 |

### Diagnostics (always returned)

Sanitized identity + source stamps, never password / user secret:

- Configured `DataSource`, `InitialCatalog` (from the connection string builder; `Password` cleared)
- Actual `@@SERVERNAME`, `SERVERPROPERTY('MachineName')`, `SERVERPROPERTY('InstanceName')`, `DB_NAME()`
- `Sync_State.LastSyncTime` for Projects, Bills, Intakes, ProjectHours, ProjectHoursExtended
- `MAX(MP_ProjectHoursExtended.ReportDate)`, `MAX(MP_Projects.LastUpdated)`, `MAX(MP_Bills.LastUpdated)`, `MAX(MP_Intakes.LastUpdated)`

Server / instance names are **informational**. Billing does **not** accept or reject because a name matches a workstation.

### Freshness clock

Thresholds live in `BillingReplicaFreshnessOptions` (defaults: warning after **36 hours**, fatal after **72 hours**). Not scattered literals.

Age uses the **oldest** required `LastSyncTime` versus **now** (UTC). `AsOfDate` is never the freshness clock.

| Status | When | Current dashboard (`AsOfDate` null or local today) | Historical `AsOfDate` (date &lt; local today) |
| --- | --- | --- | --- |
| Healthy | every required entity `LastSyncTime` age ≤ 36h | return candidates | return candidates |
| Warning | worst age &gt; 36h and ≤ 72h | return candidates + warning | return candidates + warning |
| Stale (age) | worst age &gt; 72h | **block candidates** | do **not** block solely for age; warn that the Replica is not a live dashboard |
| Fatal (structure) | missing `Sync_State`, missing required Sync_State entity row, or missing required table (`MP_Projects`, `MP_Bills`, `MP_ProjectHoursExtended`, `Sync_State`) | **block** | **block** |

Blocked response: empty `Candidates`, zero current KPI counts, `CandidatesBlocked = true`, explicit `ReplicaFreshnessFatal` / table-missing warning. No MasterPlan DB fallback. No `MP_ProjectHours` union.

Historical requests exist so a later replay of `AsOfDate = 2026-09-06` is not rejected *because that date is in the past*. They are still blocked if the Replica cannot be probed (missing tables / Sync_State).

### Out of scope (B1.5)

- Rewriting the vault secret automatically
- Hostname allow-lists
- B2 monthly enrichment, WPF, EF, menu/feature codes


---

## 17. Definition of Done for V1

V1 is done when:

- The screen reads current projects/bills/hours from Replica.
- `MP_ProjectHoursExtended` is the canonical hours source.
- Candidate classification is deterministic and explainable.
- A project with a bill in creation is never presented as a fresh actionable candidate.
- A project whose latest bill already covers all recorded work is clearly shown as covered.
- Monthly fields are visibly marked with snapshot freshness.
- No MasterPlan financial fact is duplicated as editable SiNet state.
- No automatic bill amount is suggested.
- Permission/menu tests pass.
- Existing R01/R02 behavior is not accidentally broken.

---

## 18. Cursor execution brief (B0/B1)

Implement Billing Control Center V1 incrementally according to this document. Start with Phase **B0 and B1 only**. Do not build the WPF UI until the application contracts, resolver tests, Replica-first SQL data source, hours normalization reuse, and freshness metadata are complete and tested. Do not introduce a new EF table or migration in B0/B1. Do not change the business meaning of R01/R02. Replica_DB is authoritative for current Projects/Bills/Hours/Intakes; monthly `Db_Mp_SiEng` is enrichment only. `MP_ProjectHoursExtended` is the canonical hours source. Report all assumptions and any schema mismatch before proceeding rather than silently falling back to MasterPlan-first behavior.

---

## 19. Known open questions — do not block B0/B1

These are intentionally deferred:

- Exact semantics for a current “open submitted amount” using Replica only.
- Current project/bill linkage for `MP_Intakes`.
- Whether Invoice entities can be added to Replica via the API.
- Whether Contracts/SubContracts can be made current via API rather than monthly enrichment.
- Final local decision table design after management validates the read-only candidate workflow.

None of these should block the candidate engine backend.

---

## 20. Architectural guardrail

The simplest test for every future feature is:

**Is this a fact MasterPlan owns, a calculation SiNet derives, or a workflow note only SiNet needs?**

- If MasterPlan owns it → read it from Replica whenever possible.
- If it is a derived management insight → calculate it in the Billing read model.
- If MasterPlan does not represent the human decision → store only that small supplemental decision in SiNet.

This prevents the Billing Center from becoming a second, inconsistent financial system.

---

## B0/B1 implementation notes (locked for this round)

### Assumptions

1. Last-bill / latest-bill timeline uses `COALESCE(SubmitDate, LastUpdated)`, then higher `BillId`.
2. `HoursSinceLastBill` uses `ReportDate > lastRealBillDate` (on-or-before is covered).
3. `CoveredByLatestBill` requires a real last bill (`StatusID IN (2,3,4)`). Zero hours and no real bill → `NotUrgent`.
4. Hours rolling windows are inclusive of as-of date: `[asOf − N days, asOf]`.
5. `ReceivedThisMonth` is a **global** Replica intake sum by `OpenDate`; not allocated to projects.
6. `SubmittedOpenAmount` is always null in B0–B2.
7. Snapshot monetary / fee-type fields are filled only by `BillingSnapshotEnrichmentApplier` from monthly `Db_Mp_SiEng`; the Replica engine still emits them as null.
8. Missing Replica connection throws. Missing required Replica tables or stale `Sync_State` (B1.5) returns a blocked dashboard result — no live-MP or basic-hours fallback.
9. `AsOfDate` on the request defaults to the clock's local today (`TimeProvider`). Freshness always uses now, never `AsOfDate`.
10. Resolver/engine/enrichment-applier live in Application because they have no SQL; Infrastructure loads Replica facts and monthly snapshot rows.
11. Effective bill date is `SubmitDate` when present; `LastUpdated` is fallback only. Real bills (status 2/3/4) that require the fallback raise `RealBillSubmitDateFallback` with a count.
12. Freshness for a **current** dashboard is Replica `Sync_State.LastSyncTime` vs **now**, not vs `AsOfDate`. Defaults: warn at 36h, block at 72h (`BillingReplicaFreshnessOptions`). Historical `AsOfDate` is not blocked solely because the as-of date is old.
13. `CandidateState` is Replica-only (hours + in-creation bills). Snapshot balance, open bills, billed percent, and fee mix never change it.
14. Snapshot date is `Sync_State.MonthlyRestore` (`BackupFinishDate`) or null + `SnapshotDateUnknown`. Never inferred from `MAX(LastUpdated)`.

### Schema notes (no mismatch found for B1)

Replica `MP_Bills` has `SubmitDate`, `StatusID`, `Status`, `Sum`, `ProjectID`, `BillNum`, `LastUpdated`.  
Replica `MP_Intakes` has `CustomerID` / `CustomerName` and **no** `ProjectID` / bill link.  
Replica `MP_ProjectHoursExtended` has `Duration`, `TotalHours`, `StartTime`, `EndTime`, `ReportDate`.

---

## Out of Scope (this document / B0–B4.1)

- Local Hold / Prepare / Not relevant persistence and EF migration (B5)
- Automatic bill creation or suggested bill amount
- Duplicate invoice/payment ledger
- Drill-down project details
- Changing R01/R02 business meaning or source-selection
- Running `dotnet ef` / `Update-Database`

---

## Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Generic 0–100 AI score | Cancelled for V1 | Unexplainable; management needs transparent sort keys |
| `BillsSubmitted > 0` as a hard stop | Cancelled | Historical submitted bills can still need another review |
| Union `MP_ProjectHours` + Extended | Postponed | Extended is currently a superset; parity warning instead |
| `SubmittedOpenAmount` KPI | Postponed | Semantics not proven from Replica-only data |
| Project-level intake allocation | Postponed | Replica intakes lack project/bill linkage |
| Live MasterPlan fallback for Billing | Cancelled | Would violate Replica-authoritative current facts |
| Local decision table in B0/B1 | Postponed to B5 | Validate read-only candidates first |
| WPF / permissions in B0/B1 | Postponed to B3/B4 | Backend contracts and Replica engine first |
| Hostname allow-list for Replica | Cancelled | Freshness is the gate; server identity is diagnostic only |
