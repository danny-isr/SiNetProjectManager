# DEV plan — Monthly MasterPlan restore: pre-ETL replica mismatch report (DEV-018)

> **Title:** Monthly `--monthly` restore + pre-replace Replica mismatch log (DEV-018)
> **Date:** 12.08.2026
> **Updated:** 12.08.2026 (corrected: same existing monthly pipeline; no extra database)
> **Status:** Planning (implement on `development`; not on PROD)
> **Scope:** Extend the **existing** monthly backup/restore ETL (`MasterPlan.SyncEngine --monthly`) that already restores the configured `Db_Mp_SiEng` from a `.bak` and then rebuilds `Replica_DB` `MP_*` from that database. **New:** before replica tables are dropped/reloaded, compare current replica (daily/weekly sync state) to the restored MasterPlan source, write an analysis report of mismatches, then continue the existing replace. Optional App.Wpf entry for **מנהל משרד** / Administrator to pick the `.bak` and run that same process.

Related: [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md), [`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md), [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md), [`APP_SHELL.md`](./APP_SHELL.md).

**PROD July 2026 evidence (what the new log should be able to explain):** `artifacts/mp-compare/2026-08-12/08-findings-and-fix-proposal.md`.

---

## 0. Correction vs the first draft of this file

The first draft invented an **isolated extra database** (`Db_Mp_SiEng_Capture_*`) and a separate `--capture` that must not call `--monthly`. **That was wrong.**

Operator intent (locked):

- There is already a MasterPlan DB (`Db_Mp_SiEng`) and a Replica (`Replica_DB`), both registered in the system.
- The monthly process **already exists**: restore the `.bak` onto that same MasterPlan DB, then update replica from it so replica matches the backup.
- What was once done by hand (pick backup, restore once a month) is that CLI pipeline.
- **The only addition:** before the process **replaces** replica rows, it must look at **what replica had before**, detect mismatches vs the restored source, and write a report/log for analysis of **why daily sync drifted**. Then it continues the existing replace so replica becomes correct.
- **No additional database.**

---

## 1. Purpose

When a new monthly `.bak` arrives:

1. Office manager / admin selects the backup (UI) or ops runs the existing CLI.
2. SyncEngine restores it onto the **configured** `Db_Mp_SiEng` (same as today).
3. **New step:** while replica still holds daily-sync data, compare it to the restored `HoursReports` (and related source), write a mismatch report.
4. **Existing step:** rebuild replica `MP_*` from the restored MasterPlan DB (DROP/CREATE + full ETL) so replica is known-correct.

If everything matches → report says aligned. If not → classified mismatches + evidence so we can fix daily sync later. Mismatches **do not block** the replace (the monthly job’s job is still to make replica match the backup). A confirmation in the UI is allowed (“N אי-התאמות — להמשיך בשחזור הרפליקה?”).

---

## 2. Existing mechanism (verify it still exists — it does)

| Step | Code | What it does |
| --- | --- | --- |
| CLI | `MasterPlan.SyncEngine/Program.cs` `--monthly` / `-m` + `--backup` / `-b` | Entry. Default bak path `C:\Backups\MasterPlan.bak` if omitted. |
| Restore MP | `MonthlyBackupRestoreService.RestoreBackupAsync` | SMO restore onto **`Db_Mp_SiEng`** (`SourceDatabaseName`), `ReplaceDatabase = true`, SINGLE_USER, MOVE to instance default data/log paths. |
| Stamp | `GetBackupFinishDateAsync` | `LastUpdated` baseline for all ETL rows. |
| Wipe replica hours/dims | `CreateReplicaSchemaAsync` | **DROP TABLE** `MP_ProjectHoursExtended`, `MP_ProjectHours`, `MP_TimeHourReports`, and the other `MP_*` listed there, then CREATE. |
| Reload | `RunEtlPipelineAsync` / `EtlProjectHoursExtendedAsync` | Full load from restored `HoursReports` (+ joins). Duration via `HoursNormalization.MinutesToDecimalHours` (assumes **minutes**). `LastUpdated = _backupFinishDate`. |

There is **no** App.Wpf menu for this today — only the console. Daily/weekly API sync (`ApiDailySyncService`) is a **different** path that upserts into replica between monthlies.

**Verify on develop:** do not rewrite restore/ETL algorithms unless a defect in this path is found. Confirm `--monthly` still: restore configured MP DB → drop `MP_*` → full ETL. The new work is an **inserted compare+report** between restore and drop.

Insertion point (locked):

```text
Step 1  Restore .bak → Db_Mp_SiEng          (existing)
Step 1b Compare Replica (still old) vs HoursReports (new) → write report   (NEW)
Step 2  DROP/CREATE MP_* on Replica_DB      (existing)
Step 3  Full ETL from Db_Mp_SiEng           (existing)
```

After Step 1, replica still has “what daily updates accumulated”; MasterPlan DB already has “truth from backup”. That is the only window to see drift. After Step 2 the old replica hours are gone.

---

## 3. Target behavior

### 3.1 Who / UI (optional but requested)

| Role | Access |
| --- | --- |
| `AppRole.Management` (מנהל משרד) | Yes |
| `AppRole.Administrator` | Yes (`>= Management`) |
| `AppRole.Employee` | No |

- Feature code: `AppFeatureCodes.ShellOpenMasterPlanMonthlyRestore = "Shell.OpenMasterPlanMonthlyRestore"`
- Min role: `AppRole.Management` (same floor as `Reports.Management`).
- Menu group **דוחות** (or **מנהלה** if restore feels too destructive next to R02 — **Needs Review**; default **מנהלה** because this **overwrites** both configured MP DB and replica `MP_*`).
- Hebrew: **שחזור חודשי MasterPlan**
- Tooltip: `שחזור הגיבוי ל-Db_Mp_SiEng, דוח אי-התאמות מול הרפליקה, ואז עדכון הרפליקה מהגיבוי`.
- `OpenFileDialog` filter `Backup (*.bak)|*.bak`.
- Strong confirmation: names the **existing** DBs (`Db_Mp_SiEng`, `Replica_DB`), states replica `MP_*` will be replaced after the report, SQL must be able to read the file path.
- WPF **launches** `MasterPlan.SyncEngine.exe --monthly --backup "<path>"` (plus any new flag for report folder). Do **not** fold SMO into the WPF process ([`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md)).
- Progress + cancel; after run, show the mismatch summary from the report folder.

CLI without UI must still produce the same report (ops / Task Scheduler).

### 3.2 Compare (Step 1b)

**No extra database.** Read:

- Source: restored `Db_Mp_SiEng.dbo.HoursReports` (and joins already used by `EtlProjectHoursExtendedAsync`).
- Target snapshot: current `Replica_DB.dbo.MP_ProjectHoursExtended` (and `MP_ProjectHours` if useful).

Primary key: `HoursReports.ID` ↔ `MP_ProjectHoursExtended.ID`.

Default window: last full calendar month present in restored `HoursReports.DateTime`, half-open `[Start, EndExclusive)`. Also emit **full-table** class counts (not only last month) in the summary, because ETL replaces **all** hours rows.

Classes:

| Class | Meaning |
| --- | --- |
| `BOTH_IDENTICAL` | Same ID; compared fields match (null ≠ 0) |
| `BOTH_DIFFERING` | Same ID; field-diff mask |
| `BAK_ONLY` | In restored HoursReports, not in replica (`ABSENT_REPLICA`) |
| `REPLICA_ONLY` | In replica, not in restored HoursReports (`ORPHAN_REPLICA`) |

Minimum fields: ID, ReportDate/`DateTime`, ProjectID, SubContract IDs, EmployeeID, Duration vs normalized Hours, Start/End, LastUpdated.

Cause codes (for the analysis log):

| Code | Meaning |
| --- | --- |
| `ABSENT_REPLICA` | In bak/source, missing from replica (daily never inserted — e.g. watermark lookback) |
| `ORPHAN_REPLICA` | In replica, not in bak (engine never deletes; or bak older than API edits) |
| `ETL_LASTUPDATED_SKIP` | Would have been skipped by daily MERGE (API LastUpdated ≤ replica) — if API pull is available |
| `WATERMARK_LOOKBACK_GAP` | ReportDate older than daily FromDate (watermark − 14d) |
| `HOURS_UNIT_NULL` | Replica Duration NULL while source Hours looks like ms not minutes |
| `NULL_DURATION_ZEROED` | Duration 0 / would drop from R02 `ExcludeZeroHours` |
| `FIELD_DIFF` | Present both; business fields differ |

### 3.3 Report / log (analysis artifact)

Write **before** Step 2. Folder e.g. next to SyncEngine logs or `%ProgramData%\SiNet\mp-monthly\<yyyy-MM-dd-HHmmss>\`:

| File | Content |
| --- | --- |
| `00-environment.md` | Instance, `Db_Mp_SiEng` / `Replica_DB`, bak path, BackupFinishDate, app/engine version |
| `id-classification-matrix.csv` | Full outer by ID + Class + diff flags (at least last month; full table OK if size allows) |
| `row-disposition.csv` | Mismatch-only + CauseCode + Evidence |
| `summary.md` | Hebrew: counts, top causes, “האם נראה באג בעדכון היומי?” |

Also structured log (`ILogger`) so the same facts appear in the existing SyncEngine log share.

Then **always** continue Step 2–3 unless the user cancelled in the UI.

### 3.4 What this is not

- Not a second MasterPlan database.
- Not a dry-run that skips ETL.
- Not a rewrite of daily sync (lookback / MERGE / Hours unit). Those remain follow-up IDs **after** we have this log from a real monthly.

---

## 4. July 2026 — what the log should have shown

If this Step 1b had run on a July-complete bak vs then-current replica:

- ~39 `BAK_ONLY` / `ABSENT_REPLICA` (or API-only) with early-July `ReportDate` and post-reconcile `LastUpdated` → `WATERMARK_LOOKBACK_GAP`.
- Replica-only IDs → `ORPHAN_REPLICA`.
- Historical NULL Duration + ETL stamp → `HOURS_UNIT_NULL` / `ETL_LASTUPDATED_SKIP`.

The monthly ETL would then have **replaced** replica hours from the bak (and today would also stamp `LastUpdated = BackupFinishDate`, which daily MERGE may later refuse to correct — known; out of scope to change in this ID unless develop finds it blocks the report).

---

## 5. Implementation steps (develop)

1. Keep this document as SoT; do not re-introduce an isolated extra DB.
2. Confirm `--monthly` path still matches §2 (read `RunMonthlyBackupRestoreAsync`).
3. Add Step 1b in `MonthlyBackupRestoreService` (or a helper called from there) — SELECT-only on replica; write files + logs; **then** existing Step 2–3.
4. Optional `--report-dir` CLI; default a dated folder if omitted.
5. WPF: feature code + confirmation + launch SyncEngine `--monthly --backup` + show `summary.md`.
6. Tests: classification fixtures; monthly still calls restore then compare then schema then ETL (order); Employee denied the menu.
7. Build: App.Wpf + App.Wpf.Tests + MasterPlan.SyncEngine.
8. Update `APP_SHELL.md` when the menu exists. No EF migrations.

---

## 6. Complexity / risk

| Topic | Assessment |
| --- | --- |
| Complexity | Medium: insert compare into a destructive pipeline; UI is a launcher |
| Effort | Step 1b + files (1 day); UI (0.5–1 day); tests (0.5) |
| Breaking | Monthly already overwrites MP DB + replica `MP_*`. New step must **not** skip ETL if the report fails — log the compare error and still ETL, or fail closed **before** Step 2 if compare cannot run (Needs Review: prefer **fail closed before DROP** if compare throws, so old replica is not destroyed without a report). |
| Disk | Report CSVs for full hours history can be large — last-month matrix required; full-table optional/summary-only. |
| Path | `.bak` path as seen by SQL Server. |
| Hours unit | Compare must not assume ETL Duration equals source Hours/60 without flagging `HOURS_UNIT_NULL` (July finding). |

---

## 7. Out of Scope

- Creating `Db_Mp_SiEng_Capture_*` or any extra database.
- A `--capture` mode that avoids `--monthly`.
- Changing daily lookback, MERGE skip, or `MinutesToDecimalHours` in this ID.
- Deleting replica orphans outside the existing DROP/reload.
- Folding SyncEngine into WPF.
- EF migrations.
- Changing R02 `ExcludeZeroHours`.

---

## 8. Dropped / Cancelled / Postponed (דברים שירדו / בוטלו / הושהו)

| Item | Status | Why |
| --- | --- | --- |
| Isolated extra DB `Db_Mp_SiEng_Capture_*` + `--capture` | **Dropped** | Operator clarification 12.08.2026: same configured MP DB + existing monthly replace |
| Blocking ETL until mismatches are “resolved” | Dropped | Monthly must still make replica match the backup; report is for later daily-sync analysis |
| Mandatory API three-way in v1 | Postponed | Bak vs current replica is enough; API optional if vault key already in SyncEngine |
| Rewriting restore/ETL | Dropped unless defect found | Verify existing process; extend it |

---

## 9. Needs Review

1. Menu group: **מנהלה** vs **דוחות**.
2. If Step 1b throws: abort before DROP (recommended) vs log and continue ETL.
3. Published path of `MasterPlan.SyncEngine.exe` for WPF to launch.
4. Whether Management Windows accounts can run restore, or only the SyncEngine task identity.

---

## 10. Acceptance

- `--monthly --backup` still restores **configured** `Db_Mp_SiEng` and then reloads replica `MP_*` from it. **No third database.**
- After restore and **before** DROP, a report folder exists with summary + mismatch CSV even when counts are zero (“הכול תואם”).
- Replica rowcounts/`MAX(LastUpdated)` after a successful run match a full ETL from the bak (same as today).
- Employee cannot open the UI; Management/Administrator can (when UI is in the slice).
- Build gate as in §5. **DB/schema: none.**

---

## 11. Copy-paste prompt for the `development` agent

```
Implement DEV-018 from docs/DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md (read the whole file; §0 corrects an earlier wrong design).

This is the EXISTING monthly pipeline, not a new database:
- MasterPlan.SyncEngine --monthly --backup restores the configured Db_Mp_SiEng (ReplaceDatabase=true).
- Then it DROP/CREATE Replica_DB MP_* and full-ETL from HoursReports (LastUpdated=BackupFinishDate).
- VERIFY that path still exists (MonthlyBackupRestoreService.RunMonthlyBackupRestoreAsync). Do not rewrite it.

NEW: insert Step 1b AFTER restore and BEFORE CreateReplicaSchemaAsync/DROP:
- SELECT-compare current Replica_DB.MP_ProjectHoursExtended vs restored Db_Mp_SiEng.HoursReports by ID.
- Write analysis report (summary.md Hebrew + classification CSV + disposition CSV with cause codes in the plan).
- Then continue existing Step 2–3 so replica is replaced from the backup as today.
- No extra/isolated MasterPlan database. No --capture mode.

Optional WPF: AppFeatureCodes.ShellOpenMasterPlanMonthlyRestore, min AppRole.Management, confirmation naming Db_Mp_SiEng + Replica_DB, OpenFileDialog *.bak, launch SyncEngine --monthly --backup (do not fold SMO into WPF).

Do not implement daily-sync lookback/MERGE/Hours-unit fixes in this ID.
No EF migrations. If compare throws, fail closed BEFORE dropping MP_* (recommended in the plan).

Tests: compare order restore→report→drop→etl; classification fixtures; authorization.
Build: App.Wpf + App.Wpf.Tests + MasterPlan.SyncEngine. Update APP_SHELL.md when the menu exists.
```
