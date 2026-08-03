# MasterPlan SyncEngine — watermarks, lookback window & weekly reconciliation

> **Title:** MasterPlan SyncEngine — incremental sync correctness for hour reporting
> **Date:** 02.08.2026
> **Status:** Active
> **Scope:** `MasterPlan.SyncEngine` daily API sync (`--daily`): how the per-entity watermark is
> stored and used, why the hour-reporting entities lost rows, and the corrective design
> (lookback window + weekly full reconciliation). Applies to `Replica_DB` `MP_*` tables only.

> Related: [`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md),
> [`DATABASE_RECOVERY_BASELINE.md`](./DATABASE_RECOVERY_BASELINE.md),
> [`PRODUCTION_MONITORING.md`](./PRODUCTION_MONITORING.md)

---

## 1. Existing mechanism (before this round)

`ApiDailySyncService.RunDailySyncAsync` syncs 12 entities. For each entity it:

1. reads `Replica_DB.dbo.Sync_State.LastWatermark` for that `EntityName`;
2. sends that value to the MasterPlan Web API as a query filter;
3. upserts the response by `ID` (no bulk delete, ever);
4. writes back `MAX(<field>)` of the batch as the new watermark — but only when the batch was
   non-empty, and only when the new value is strictly greater than the previous one.

Watermark storage table (created by `EnsureSyncStateTableAsync`):

```sql
CREATE TABLE Sync_State (
    EntityName    NVARCHAR(100) PRIMARY KEY,
    LastWatermark DATETIME2,
    LastSyncTime  DATETIME2,
    UpdatedAt     DATETIME2 DEFAULT GETUTCDATE()
)
```

The three hour-related entities:

| Entity | Endpoint | Query filter | Server filters on | Target table |
| --- | --- | --- | --- | --- |
| `ProjectHours` | `ProjectHours/` | `?fromDate=` | `ReportDate` | `MP_ProjectHours` |
| `ProjectHoursExtended` | `projecthours/GetProjectHoursExtended` | `?FromDate=` | `ReportDate` | `MP_ProjectHoursExtended` |
| `TimeHourReports` | `projecthours/GetTimeHourReports` | `?FromDate=` | *(ignored by server)* | `MP_TimeHourReports` |

The scheduled task runs twice a day (≈03:05 and ≈09:09 UTC / 06:05 and 12:09 local).

---

## 2. Defects found (2026-08-02 investigation)

### 2.1 Wrong watermark field for `ProjectHoursExtended`

The engine stored the watermark as `MAX(LastUpdated)` — an **edit timestamp**, in practice around
17:00–18:00 — and sent it as `FromDate`, which the server applies to **`ReportDate`** (always
midnight). After the first successful run of a day the watermark is therefore already *later* than
any report date that day can produce, so every later run returns nothing.

Evidence from `Sync_RunHistory` RunId 176 (2026-08-02 09:09), both endpoints reading the same
underlying `HoursReports` data:

| Entity | `FromDate` sent | Fetched | Inserted |
| --- | --- | --- | --- |
| `ProjectHours` | `2026-07-30T00:00:00` | 11 | **10** |
| `ProjectHoursExtended` | `2026-07-30T17:33:42` | **0** | 0 |

The ten hour reports for 30/07 entered late reached `MP_ProjectHours` and never reached
`MP_ProjectHoursExtended`. Across the whole run history the 09:09 run fetched **0** rows for
`ProjectHoursExtended` on every single day.

### 2.2 Future-dated report poisons a date watermark

Because the watermark advances to `MAX(ReportDate)` of the batch, one report dated in the future
moves it past everything in between. On 2026-07-07 a report with `ReportDate = 2026-07-23` entered
the batch; the `ProjectHours` watermark jumped to `2026-07-23T00:00:00` and stayed there until
2026-07-26. `MP_ProjectHours` has **zero rows for 07/07–22/07** — 16 working days — while June
averaged 15–23 reports per day.

### 2.3 Consequence for the application

`SqlR02ReportDataSource` and `SqlR03ReportDataSource` read `MP_ProjectHoursExtended` in preference
to `MP_ProjectHours`, so the reports users see are built on the table that loses the most rows.
Measured on 2026-08-02: 76 report IDs present in `MP_ProjectHours` and absent from
`MP_ProjectHoursExtended`, 32 in the opposite direction. Rows missing from *both* tables (the
07/07–22/07 window) are not detectable by comparing the two tables against each other.

### 2.4 `TimeHourReports` — not a data-loss defect

The server ignores `FromDate` on this endpoint and returns the full table (12,295 rows) on every
run. No rows are lost; the cost is two full 12k MERGE passes per day. Left as-is.

### 2.5 `LastSyncTime` cannot distinguish an idle entity from a dead one

`UpdateWatermarkAsync` — the only writer of `Sync_State.LastSyncTime` — is called from inside the
`if (batch.Count > 0)` block of each entity method. An entity whose endpoint returns nothing is
therefore never stamped, and its `LastSyncTime` freezes at the last day data happened to arrive.

Observed on 2026-08-03: `Bids` and `Conversations` both showed `LastSyncTime = 2026-07-04`, which
reads as "this entity stopped syncing a month ago". Both were in fact being fetched twice a day and
returning zero rows. The two situations — *not running* and *running but empty* — are
indistinguishable from the table, which makes the column useless for alerting.

---

## 3. Target state

### 3.1 Principle

Hour reporting is **mutable and back-datable**: a report for an earlier date can be created or
edited at any time. A monotonic high-water mark is the wrong instrument for such data — it can only
move forward, so anything written behind it is lost permanently. Because every upsert is keyed on
`ID` (MERGE / upsert-by-ID), re-fetching the same rows is idempotent and safe.

### 3.2 Rules

1. **The stored watermark must match the field the server filters on.** For
   `ProjectHoursExtended` the watermark is derived from `MAX(ReportDate)`, not `MAX(LastUpdated)`.
2. **The watermark never moves into the future.** It is clamped to the end of the current day, so a
   future-dated report cannot skip the days in between.
3. **Every request uses a lookback window.** The request date is
   `fromDate = watermark − LookbackDays`, with `LookbackDays = 14` by default
   (`MasterPlanApi:HoursLookbackDays`). This absorbs normal late and retroactive reporting.
4. **A full reconciliation runs weekly.** At most once every `ReconcileIntervalDays` (default 7) the
   hour entities are fetched **without any date filter**, compared against the replica, and any
   missing or changed row is written. This is the safety net for anything the window missed.
5. **Reconciliation never deletes.** Rows present in the replica but absent from the API are
   reported as `OrphanCandidates` in the log and in `Sync_RunHistory`, and left in place.

### 3.3 Reconciliation bookkeeping

Reconciliation state reuses `Sync_State` rather than introducing a table: the row
`EntityName = '<Entity>:Reconcile'` holds `LastSyncTime` of the last full pass. A run is a
reconciliation run when that row is missing or older than `ReconcileIntervalDays`. `--reconcile`
forces one; `--no-reconcile` suppresses it.

Because reconciliation has never run, the first execution after this change performs a full pull of
all hour entities and reports the gap it closes.

### 3.4 Configuration

| Key | Default | Meaning |
| --- | --- | --- |
| `MasterPlanApi:HoursLookbackDays` | `14` | Days subtracted from the watermark on every hours request |
| `MasterPlanApi:ReconcileIntervalDays` | `7` | Minimum days between full reconciliation passes |

### 3.5 Freshness stamp and staleness warning

Separating the two meanings that `LastSyncTime` was carrying (§2.5):

6. **`LastSyncTime` records the last successful pass, not the last non-empty pass.** Every entity
   stamps it at the end of a successful sync, whether or not rows came back. It answers one
   question only: *did this entity complete a pass?*
7. **An entity that returns nothing for a long time warns.** When a batch is empty and the stored
   watermark is more than `StaleEntityWarningDays` (14) old, the engine logs a `[STALE]` warning.
   Central logging runs at `Warning`, so a silently dead endpoint becomes visible without lowering
   the log level.

No schema change: both rules use the existing `Sync_State` columns.

### 3.6 Watermark semantics per entity (audited 2026-08-03)

`LastUpdated` is written by the MasterPlan server at save time, so it only ever moves forward and a
record can never appear behind the stored watermark. `ReportDate` is a business date the user
chooses and can be back-dated — which is why only the hour entities lost rows.

| Entity | Watermark field | Server filters on | Match | Verdict |
| --- | --- | --- | --- | --- |
| `Projects`, `Companies`, `Contacts`, `Employees`, `Bills`, `Intakes` | `LastUpdated` | `LastUpdated` | yes | Correct — row counts grow against the 30/04 restore baseline |
| `Bids` | `LastUpdated` | `LastUpdated` | yes | Correct; zero rows returned since 04/07 — see Needs Review |
| `Conversations` | `CreatedDate` | `CreatedDate` | yes | Correct; zero rows returned since 04/07 — see Needs Review |
| `Tasks` | `LastUpdated` | *(ignored — server echoes only `dueDate`)* | no | Full pull of 784 rows every run; no loss, wasteful |
| `TimeHourReports` | `ReportDateTime` | *(ignored)* | no | Full pull of ~12.3k rows every run; no loss (§2.4) |
| `ProjectHours` | `ReportDate` | `ReportDate` | yes | Back-datable — covered by §3.2 |
| `ProjectHoursExtended` | `ReportDate` (was `LastUpdated`) | `ReportDate` | fixed | Was §2.1 |

Deletes are not propagated for any entity; this is the standing "no bulk delete" rule.

---

## 4. Verification

1. Run `MasterPlan.SyncEngine.exe --daily` once manually.
2. In the log, `[WATERMARK]` lines for the hour entities must show a `FromDate` at least 14 days
   behind the stored watermark, and `[RECONCILE]` must report the first full pass.
3. In SQL, per-day counts for `MP_ProjectHoursExtended` and `MP_ProjectHours` must converge:

```sql
SELECT COALESCE(a.D, b.D) AS ReportDay, ISNULL(a.Cnt,0) AS InProjectHours, ISNULL(b.Cnt,0) AS InExtended
FROM      (SELECT CAST(ReportDate AS date) D, COUNT(*) Cnt FROM MP_ProjectHours          WHERE ReportDate >= '2026-07-01' GROUP BY CAST(ReportDate AS date)) a
FULL JOIN (SELECT CAST(ReportDate AS date) D, COUNT(*) Cnt FROM MP_ProjectHoursExtended  WHERE ReportDate >= '2026-07-01' GROUP BY CAST(ReportDate AS date)) b
  ON a.D = b.D
ORDER BY ReportDay;
```

4. Every entity must carry a fresh `LastSyncTime` after a successful run, including the ones that
   returned nothing:

```sql
SELECT EntityName, LastWatermark, LastSyncTime FROM Sync_State ORDER BY LastSyncTime;
```

---

## 5. Out of Scope

- Any change to the watermark **field** of the nine non-hour entities (§3.6 audits them; the
  freshness stamp in §3.5 applies to all twelve but changes no watermark).
- Deleting replica rows that no longer exist in MasterPlan (reported only).
- The monthly backup/restore ETL (`--monthly`) and its watermark initialisation.
- Schema changes to `MP_*` tables, `Sync_State`, or any EF migration.
- Making `TimeHourReports` incremental (the server-side filter would have to be fixed first).
- Changing the Task Scheduler run times.

## 6. Dropped / Cancelled / Postponed (דברים שירדו / בוטלו / הושהו)

| Item | Status | Why |
| --- | --- | --- |
| Repairing history via a `--monthly` backup/restore | Postponed | The weekly reconciliation closes the same gap without restoring a database |
| One-off manual watermark reset in `Sync_State` | Dropped | Superseded by the first reconciliation pass, which is automatic and repeatable |
| Deleting replica rows absent from the API | Not implemented | The engine's "no bulk delete" rule stands; orphans are reported for manual review |
| Making the lookback window per-entity configurable | Postponed | One shared value for the hour entities is sufficient; revisit if reporting patterns diverge |
| Adding `LastRecordsFetched` / `LastNonEmptySyncTime` columns to `Sync_State` | Dropped | Would need a schema change; the `[STALE]` warning in §3.5 covers the same alerting need |
| Making `Tasks` genuinely incremental | Postponed | The server ignores `lastUpdated` on that endpoint; a full pull of 784 rows is cheap enough to leave alone |

## 7. Needs Review

- The exact server-side semantics of `FromDate` on `GetProjectHoursExtended` are inferred from
  observed behaviour, not from MasterPlan API documentation. The lookback window makes the fix
  correct under either interpretation, but the inference should be confirmed with the vendor.
- `Bids` (10 replica rows) and `Conversations` (115 rows, newest `CreatedDate` 12/04/2026) have
  returned zero rows from the API since 04/07/2026. Their replica contents match the 30/04 restore
  baseline exactly, so nothing has been lost — but whether these features are genuinely unused or
  the endpoints are broken has not been established. The `[STALE]` warning will surface them daily.
