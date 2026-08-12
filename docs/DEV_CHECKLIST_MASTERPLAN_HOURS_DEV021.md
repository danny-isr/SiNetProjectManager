# DEV-021 verification checklist — MasterPlan hours (test replica only)

> **Date:** 12.08.2026  
> **Branch:** `development` only — do not run against production without explicit approval.  
> **Related:** [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md) §1c

## Preconditions

- Use a **test** `Db_Mp_SiEng` / `Replica_DB` copy (or DEV SQL), never production UNC/SQL without approval.
- Same `.bak` as the July investigation: finish date **2026-08-02** (or the bak used then).

## Steps

1. Build/publish SyncEngine from this branch (or run from bin) with DEV connection strings.
2. Run `--monthly --backup <path-to-2026-08-02.bak>`.
3. After ETL, run:

```sql
SELECT
    COUNT(*) AS TotalRows,
    SUM(CASE WHEN Duration IS NULL THEN 1 ELSE 0 END) AS NullDuration,
    SUM(CASE WHEN TotalHours IS NULL THEN 1 ELSE 0 END) AS NullTotalHours
FROM MP_ProjectHoursExtended;

SELECT ID, Duration, TotalHours, LastUpdated
FROM MP_ProjectHoursExtended
WHERE ID IN (11062, 11068, 11073);
```

**Expected samples:** `11062` → Duration `1.0000`; `11068` → `2.0000`; `11073` → `7.0000`.  
`LastUpdated` should be **NULL** after monthly (DEV-021).  
`NullDuration` must drop dramatically vs ~8195 (may be non-zero for true empty Hours).

4. Run `--daily` (or reconcile) on the **same test** replica. Record Inserted / Updated / Skipped from the log.
5. Regenerate R02 for `2026-07-01` .. `2026-07-31` and compare to Master Plan by report ID (rows, total hours, missing, extra, value diffs, business duplicates).

## Delivery fields

- NullDuration before / after  
- Sample IDs above  
- Sync Inserted/Updated/Skipped  
- R02 vs Master Plan summary + remaining mismatches  
- Recommendation: safe to merge to `release`? (only after data verify)
