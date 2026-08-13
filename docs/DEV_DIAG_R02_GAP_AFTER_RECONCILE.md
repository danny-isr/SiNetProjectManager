# DEV-024 — אבחון פער R02 מול Master Plan אחרי reconcile מוצלח

> **Title:** R02 vs Master Plan gap diagnosis (post-monthly + force reconcile)  
> **Date:** 13.08.2026  
> **Status:** Diagnosis measured on PROD — **fix directed by DEV-025** (Replica-first); see [`DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md`](./DEV_DIRECTIVE_REPLICA_SOT_AND_ORPHAN_ARCHIVE.md)  
> **Scope:** Why native R02 for July 2026 stays at **271 rows / ~612.93 h** while Master Plan export shows **349 / 878:25**, even after ETL identity and API reconcile Success on SyncEngine **1.0.22**. Read-only investigation on `development`.  
> **Branch:** `development` only — do not patch `release` until diagnosis + approved fix.

Related: [`DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md`](./DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md), [`DEV_CHECKLIST_MASTERPLAN_HOURS_DEV021.md`](./DEV_CHECKLIST_MASTERPLAN_HOURS_DEV021.md), [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md), [`DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md`](./DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md).

---

## 1. Proven facts (ops) vs code facts (repo)

### 1.1 Proven from ops (do not re-litigate)

| Fact | Value |
| --- | --- |
| SyncEngine | 1.0.22.0 |
| Monthly ETL PHE | 22374 rows; identical to restored source (differing=0) |
| Force reconcile PHE | Fetched=22564 Inserted=213 Updated=22351 Skipped=0 Success |
| R02 before reconcile | 271 / 612.93 |
| R02 after reconcile | **identical** 271 / 612.93 |
| Master Plan July export | 349 / 878:25 |
| Net missing vs MP (business key) | ~81 rows / ~265:30 h |
| Orphans on replica | 23 IDs (not deleted); some may appear in R02 |

### 1.2 Proven from code (`SqlR02ReportDataSource`)

| Fact | Evidence |
| --- | --- |
| Entry | `R02ReportViewModel.GenerateAsync` → `NativeR02ReportService` → `IR02ReportDataSource.GetMergedHoursAsync` |
| Implementation | `src/SiNet.Infrastructure.Sql/Services/MasterPlan/Reports/SqlR02ReportDataSource.cs` |
| Connection | Vault `SiNet/ConnectionStrings/ReplicaDatabase` + `MasterPlanDatabase` via `VaultMasterPlanEmployeeConnectionProvider` (names inside CS — typically Replica_DB / live MP on `SI-WIN-2K19\SIDATA`) |
| **Split source** | If `MAX(CAST(DateTime AS date))` on live `dbo.HoursReports` ≥ report `EndDate`, **entire range is read from live MasterPlan only** — Replica is **not queried** |
| Replica path | Prefer `MP_ProjectHoursExtended` if table exists; else `MP_ProjectHours`. **No row-level union** between Extended and basic |
| Extended JOINs | `LEFT JOIN` employees/projects (does not drop orphans) |
| MP path JOINs | `INNER JOIN` Employees + Projects (drops hours without matching dims) |
| Post-filter | `ExcludeZeroHours` default **true** → drop `Hours == 0` after `ConvertHoursRaw` |
| Cache | None for report result |
| Distinct/GroupBy on Data | None in native path |

### 1.3 Hypotheses (not yet proven on SQL)

| ID | Hypothesis | Why it fits |
| --- | --- | --- |
| **H1 (primary)** | July R02 reads **live MasterPlan `HoursReports`**, not reconciled Replica | Explains **identical R02 before/after reconcile** |
| H2 | Live MP has fewer July rows / INNER JOIN drops ~81 | Explains gap vs bak/export 349 |
| H3 | If somehow Replica path: API MERGE wiped lookup fields → still unlikely to drop with LEFT JOIN; ExcludeZeroHours / Duration CASE could drop some | Secondary |
| H4 | Orphans inflate R02 extras (3 surplus rows) | Partial; does not explain 81 missing |
| H5 | Wrong vault CS / wrong DB | Must verify with connection probe queries |

---

## 2. Exact R02 data path

```text
NewShellFactory.OpenNativeR02Report
  → R02ReportWindow / R02ReportViewModel.GenerateAsync
    → NativeR02ReportService.GenerateAsync  (Google Sheets: R02_Hours_All_*)
      → SqlR02ReportDataSource.GetMergedHoursAsync
```

### 2.1 Merge decision (critical)

```text
mpMax = MAX(CAST(DateTime AS date)) FROM MasterPlan.dbo.HoursReports

if no MasterPlan CS or mpMax null:
    → Replica only [Start, End]
else if EndDate <= mpMax:
    → MasterPlan only [Start, End]          ★ July 2026 almost certainly here
else if StartDate > mpMax:
    → Replica only
else:
    → MasterPlan [Start, mpMax] + Replica [mpMax+1, End]
```

Then: optional `ExcludeZeroHours`; OrderBy date/project/employee/id.

### 2.2 SQL shapes (abbreviated)

**MasterPlan (when selected):**

```sql
SELECT hr.ID, CAST(hr.[DateTime] AS date), ... hr.Hours, ...
FROM dbo.HoursReports hr
INNER JOIN dbo.Employees e ON hr.EmployeeID = e.ID
INNER JOIN dbo.Projects p ON hr.ProjectID = p.ID
LEFT JOIN dbo.Contacts ct ... LEFT JOIN dbo.Companies ...
LEFT JOIN dbo.SubContracts ... LEFT JOIN dbo.SubContractSteps ...
WHERE hr.[DateTime] >= @Start AND hr.[DateTime] < @EndExclusive
```

**Replica Extended (when selected):**

```sql
SELECT ph.ID, CAST(ph.ReportDate AS date),
  CASE WHEN ph.Duration BETWEEN 0 AND 24 THEN ph.Duration ELSE NULL END AS HoursRaw,
  ..., ph.TotalHours
FROM MP_ProjectHoursExtended ph
LEFT JOIN MP_Employees e ON ph.EmployeeID = e.ID
LEFT JOIN MP_Projects p ON ph.ProjectID = p.ID
WHERE ph.ReportDate >= @Start AND ph.ReportDate < @EndExclusive
```

C#: if HoursRaw null → use TotalHours → `ConvertHoursRaw` → then ExcludeZeroHours.

### 2.3 MERGE lookup wipe (reconcile)

PHE MERGE **COALESCE** only for `Duration` / `TotalHours` / `LastUpdated`.  
Lookup fields (`ProjectNumber`, `EmployeeName`, …) are **assigned from API** on UPDATE (`t.ProjectNumber = s.ProjectNumber`, etc.). API empty/null **can** clear rich ETL strings — relevant **only if** R02 uses Replica path; with LEFT JOIN rows still appear (possibly with blank ProjectNum).

---

## 3. Read-only SQL pack (run on SI-WIN-2K19\SIDATA)

Replace DB names if vault differs. **No DELETE / UPDATE / restore.**

### Q0 — Which source would R02 pick for July?

```sql
-- Live MasterPlan (vault MasterPlanDatabase)
SELECT MAX(CAST([DateTime] AS date)) AS MpMaxDate,
       COUNT(*) AS JulyHoursReports
FROM dbo.HoursReports
WHERE [DateTime] >= '2026-07-01' AND [DateTime] < '2026-08-01';

-- Interpretation:
-- If MpMaxDate >= '2026-07-31' → R02(2026-07-01..31) uses MasterPlan ONLY (H1 confirmed).
```

### Q1 — Replica July base counts

```sql
SELECT
    COUNT(*) AS RowCount,
    COUNT(DISTINCT ID) AS UniqueIds,
    SUM(CAST(Duration AS float)) AS TotalDuration,
    SUM(CASE WHEN Duration IS NULL THEN 1 ELSE 0 END) AS NullDuration,
    SUM(CASE WHEN TotalHours IS NULL THEN 1 ELSE 0 END) AS NullTotalHours
FROM Replica_DB.dbo.MP_ProjectHoursExtended
WHERE ReportDate >= '2026-07-01' AND ReportDate < '2026-08-01';

-- Parallel:
SELECT COUNT(*) AS RowCount, COUNT(DISTINCT ID) AS UniqueIds
FROM Replica_DB.dbo.MP_ProjectHours
WHERE ReportDate >= '2026-07-01' AND ReportDate < '2026-08-01';
```

Compare to R02 **271 / 612.93**. If Replica ≫ 271 but MP live ≈ 271 → H1+H2.

### Q2 — Live MP July after same JOINs as R02

```sql
SELECT COUNT(*) AS AfterInnerJoins,
       SUM(CAST(hr.Hours AS float)) AS SumHoursRaw  -- unit may be minutes/ms; convert offline
FROM dbo.HoursReports hr
INNER JOIN dbo.Employees e ON hr.EmployeeID = e.ID
INNER JOIN dbo.Projects p ON hr.ProjectID = p.ID
WHERE hr.[DateTime] >= '2026-07-01' AND hr.[DateTime] < '2026-08-01';

-- Lost to INNER JOIN:
SELECT COUNT(*) AS DroppedNoEmployeeOrProject
FROM dbo.HoursReports hr
WHERE hr.[DateTime] >= '2026-07-01' AND hr.[DateTime] < '2026-08-01'
  AND (
    NOT EXISTS (SELECT 1 FROM dbo.Employees e WHERE e.ID = hr.EmployeeID)
    OR NOT EXISTS (SELECT 1 FROM dbo.Projects p WHERE p.ID = hr.ProjectID)
  );
```

### Q3 — Stage counts (fill the A–H table)

| Stage | Query intent |
| --- | --- |
| A | `HoursReports` July raw count |
| B | After INNER Employees |
| C | After INNER Projects |
| D | After all LEFT joins (should = C) |
| E | WHERE date only (same as A) |
| F | N/A (no GROUP) |
| G | = C if H1; or PHE July if Replica path |
| H | Sheets row count from generation status (`RowCount`) |

```sql
-- A
SELECT COUNT(*) FROM dbo.HoursReports
WHERE [DateTime] >= '2026-07-01' AND [DateTime] < '2026-08-01';
-- B
SELECT COUNT(*) FROM dbo.HoursReports hr
INNER JOIN dbo.Employees e ON hr.EmployeeID = e.ID
WHERE hr.[DateTime] >= '2026-07-01' AND hr.[DateTime] < '2026-08-01';
-- C
SELECT COUNT(*) FROM dbo.HoursReports hr
INNER JOIN dbo.Employees e ON hr.EmployeeID = e.ID
INNER JOIN dbo.Projects p ON hr.ProjectID = p.ID
WHERE hr.[DateTime] >= '2026-07-01' AND hr.[DateTime] < '2026-08-01';
```

### Q4 — Missing business rows: resolve Replica IDs

```sql
SELECT ID, EmployeeID, EmployeeName, ProjectID, ProjectNumber, ProjectName,
       ReportDate, Duration, TotalHours, StartTime, EndTime, LastUpdated
FROM Replica_DB.dbo.MP_ProjectHoursExtended
WHERE ReportDate >= '2026-07-01' AND ReportDate < '2026-08-01'
  AND LTRIM(RTRIM(ProjectNumber)) IN
      ('1341','1972','2173','2498','2525','2576','2633','2962','57','413','1844','2644')
ORDER BY ProjectNumber, ReportDate, EmployeeID, ID;
```

Cross-check one missing example (e.g. 02/07/2026 + 1341 + 1h) on **both** Replica PHE and live HoursReports+Projects.ProjectNum.

### Q5 — Orphans in July R02 surface

```sql
SELECT ID, EmployeeID, ProjectID, ProjectNumber, ReportDate, Duration, TotalHours, LastUpdated
FROM Replica_DB.dbo.MP_ProjectHoursExtended
WHERE ID IN (
    20727,
    57661,57662,57668,57669,57670,57671,
    57677,57678,57679,57680,57681,57682,
    57899,58030,58076,58077,58078,
    58181,58182,58183,58184,58185
);
```

### Q6 — Lookup wipe sample (Replica only; after reconcile)

```sql
SELECT
  SUM(CASE WHEN ProjectNumber IS NULL OR LTRIM(RTRIM(ProjectNumber)) = '' THEN 1 ELSE 0 END) AS BlankProjectNumber,
  SUM(CASE WHEN EmployeeName IS NULL OR LTRIM(RTRIM(EmployeeName)) = '' THEN 1 ELSE 0 END) AS BlankEmployeeName,
  SUM(CASE WHEN ProjectID IS NULL THEN 1 ELSE 0 END) AS NullProjectId,
  COUNT(*) AS JulyRows
FROM Replica_DB.dbo.MP_ProjectHoursExtended
WHERE ReportDate >= '2026-07-01' AND ReportDate < '2026-08-01';
```

### Q7 — Connection identity (no secrets)

```sql
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;
-- Run once on each vault target (Replica + MasterPlan).
```

---

## 4. Five representative “in replica / not in R02” candidates

Pick after Q0–Q4. Expected pattern if **H1**:

| Sample | In PHE July | In live HoursReports+INNER | In R02 | Drop reason |
| --- | --- | --- | --- | --- |
| TBD ID | Yes | No or Hours≠ | No | R02 never read Replica / missing on live MP |
| … | | | | |

Until Q0 runs, **do not claim specific IDs** — claim the **mechanism**.

---

## 5. Analysis of 213 inserts / 23 orphans

| Topic | Finding |
| --- | --- |
| 213 inserts | Landed on Replica; **cannot change R02** while H1 holds |
| 23 orphans | May appear **only if** Replica path; under H1 they are invisible to R02. May explain surplus **if** a later month mix or if mpMax forces split |
| R02 extras (3) | Check 06/07 1341 8h, 13/07 1972 0.5h, 08/07 2644 vs MP 413 — project-number remapping |

---

## 6. Facts vs hypotheses

| Statement | Class |
| --- | --- |
| Reconcile updated Replica; R02 unchanged | **Fact** (ops) |
| Native R02 can ignore Replica when `EndDate <= mpMax` | **Fact** (code) |
| July R02 currently uses MasterPlan-only | **Hypothesis H1** — confirm with Q0 |
| 81 missing = INNER JOIN / incomplete live HoursReports | **Hypothesis H2** |
| Reconcile NULL lookup caused the 81 drop | **Unlikely under H1**; test Q6 only if Replica path |

---

## 7. Minimal fix plan (after diagnosis — not implementing now)

Prefer the **smallest** change that reuses existing merge:

1. **Confirm H1** with Q0 + stage counts matching 271.
2. Options (pick after review):
   - **A.** For closed months already captured on Replica, prefer Replica (or bak-aligned) over live MP when monthly restore watermark says month is sealed.
   - **B.** Always read Replica for ranges fully covered by `MP_ProjectHoursExtended` after successful monthly+reconcile (feature flag).
   - **C.** Keep merge but fix live MP dimension data / HoursReports gaps (ops, not app) — only if live is intended SoT.
3. Do **not** dual-read Extended+basic blindly (duplicate risk).
4. Keep ExcludeZeroHours behavior explicit in UI when comparing to MP export.
5. Orphan purge remains separate (DEV-019); don’t delete during diagnosis.

### Regression

- July 2026 R02 row count / hours vs agreed SoT (349 / 878:25 ± documented business diffs).
- Open month (current) still merges MP tip + Replica tail.
- Zero-hour exclusion toggle.
- Unit tests around merge branch selection (new).
- SyncEngine monthly + reconcile still Success; R02 smoke on pilot.

### Acceptance proof

Document which DB/source R02 used (`Source` field already on `R02HoursRow` — expose count by Source in status or log). Target: July sheet ≈ Master Plan export, with a short list of intentional diffs only.

---

## 8. Out of Scope

- Code/SQL writes, orphan DELETE, second restore, changing `release` before approval.
- Replacing Google Sheets pipeline.
- Treating SyncEngine as R02 runner (it is not).

## 9. Dropped / Cancelled / Postponed

| Item | Status | Why |
| --- | --- | --- |
| Implementing fix in this round | Postponed | Diagnosis-first per request |
| Assuming Replica is always R02 SoT | Dropped as assumption | Code proves conditional MasterPlan preference |

## 10. Needs Review

- Exact vault DB names on PROD (`Replica_DB` vs other).
- Unit of `HoursReports.Hours` on live MP for July (minutes vs hours vs ms) when summing vs 612.93.
- Whether Master Plan “349” export used the same filters as R02 (company/employee/exclude zero).
- Next ID: **DEV-024**.

---

## 12. Measured on PROD (13.08.2026) — `SI-WIN-2K19\SIDATA`

Read-only `sqlcmd -E`. Databases: `Db_Mp_SiEng`, `Replica_DB`.

| Check | Result |
| --- | --- |
| **Q0** `MAX(DateTime)` HoursReports | **2026-07-30** |
| Live July HoursReports | **273** |
| After INNER emp+proj | **273** (0 dropped) |
| Hours = 0 or NULL | **2** → NonZero **271** |
| **R02 reported** | **271 / ~612.93** |
| Replica PHE July | **371** rows, Duration sum **~904.43 h** |
| Replica July 31 | **0** |
| Project 1341 | Live **16** · Replica **35** (Δ+19) |
| Orphan IDs in live HoursReports | **23 / 23** (including `57899` = 1341 / 06-07 / 8h — matches a known R02 *extra*) |

### Verdict (proven)

1. **H1 confirmed in effect:** for July 1–31, `mpMax=2026-07-30` → merge uses **live `Db_Mp_SiEng.HoursReports` through 30-Jul** + Replica from 31-Jul. July 31 is empty on both → R02 ≈ live July only.
2. **271 = live NonZeroHours** exactly → `ExcludeZeroHours` explains live 273→271. Reconcile on Replica **cannot** change this path.
3. Gap vs Master Plan export (349 / 878:25) and vs Replica (371 / 904) is **missing rows on live HoursReports**, not R02 JOIN/cache.
4. Orphans listed are still present on **live** HoursReports; some surface in R02 (e.g. 57899).

### Next (fix — still not implementing)

Prefer Replica (or sealed monthly snapshot) for closed months when generating R02, **or** refresh live HoursReports to match bak/API. Decision Needs Review.