# DEV plan — Monthly MasterPlan restore: pre-ETL replica mismatch report (DEV-018)

> **Title:** Monthly `--monthly` restore + pre-replace Replica mismatch log (DEV-018)
> **Date:** 12.08.2026
> **Updated:** 12.08.2026 (DEV-020: bak staging move + SQL server path map + retain 10)
> **Status:** DEV-018/019/020 on release tip — ops verify Needs Review
> **Scope:** Extend the **existing** monthly backup/restore ETL (`MasterPlan.SyncEngine --monthly`) that already restores the configured `Db_Mp_SiEng` from a `.bak` and then rebuilds `Replica_DB` `MP_*` from that database. **New:** BackupFinishDate gate; compare replica vs restored `HoursReports` before DROP and again after ETL; log classified mismatches to **existing SyncEngine sinks**; App.Wpf **מנהלה** launcher for Management+. **DEV-020:** before HEADERONLY/RESTORE, **move** (not copy) the chosen `.bak` into a shared staging folder visible to SQL Server.

Related: [`MASTERPLAN_SYNC_WATERMARKS.md`](./MASTERPLAN_SYNC_WATERMARKS.md), [`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md), [`DEV_BACKLOG.md`](./DEV_BACKLOG.md), [`IDENTITY_AND_PERMISSIONS.md`](./IDENTITY_AND_PERMISSIONS.md), [`APP_SHELL.md`](./APP_SHELL.md).

**PROD July 2026 evidence (what the new log should be able to explain):** `artifacts/mp-compare/2026-08-12/08-findings-and-fix-proposal.md`.

---

## 0. Correction vs the first draft of this file

The first draft invented an **isolated extra database** (`Db_Mp_SiEng_Capture_*`) and a separate `--capture` that must not call `--monthly`. **That was wrong.**

Operator intent (locked 12.08.2026):

- There is already a MasterPlan DB (`Db_Mp_SiEng`) and a Replica (`Replica_DB`), both registered in the system.
- The monthly process **already exists**: restore the `.bak` onto that same MasterPlan DB, then update replica from it so replica matches the backup.
- **Gate (before any restore):** `RESTORE HEADERONLY` `BackupFinishDate` (not file mtime) must be **later than** the last successful monthly restore stamp in replica `Sync_State` entity `MonthlyRestore`. If not later → stop, **no DB changes**. First run with no stamp → allow. If HEADERONLY cannot be read → fail closed.
- **Step 1b (pre-DROP):** compare current `Replica_DB.MP_ProjectHoursExtended` vs restored `HoursReports` by `ID`. Log drift. If compare **throws** → **fail closed before DROP**.
- **Logs** go to **existing SyncEngine sinks** (central `{Logging.CentralLogPath}\SyncEngine\...`, local `%ProgramData%\SiOffice\MasterPlanSync\logs\`). Hebrew summary + mismatch details there. **No** new `%ProgramData%\SiNet\mp-monthly\` folder.
- Then existing DROP/CREATE `MP_*` + full ETL. Post-bak replica-only rows disappearing is **accepted**; next `--daily` should INSERT new IDs since backup.
- **Step 3b (post-ETL):** second compare after reload. Remaining `BAK_ONLY` / `REPLICA_ONLY` after a successful ETL is an ETL bug.
- Daily watermarks after monthly already set to `BackupFinishDate` in `InitializeWatermarksAsync` — **verify, don’t rewrite** daily lookback / MERGE / hours-unit in this ID.
- **No additional database.**

---

## 1. Purpose

When a new monthly `.bak` arrives:

1. Office manager / admin selects the backup (UI) or ops runs the existing CLI.
1b. **DEV-020 staging:** SyncEngine **moves** the file into the client staging folder (`N:\MasterPlanBakup` by default), keeps at most **10** `.bak` files there (configurable `MaxRetainedBackups`), and passes the **server** path (`D:\SharedFolder\ProjectsData\MasterPlanBakup\…`) to SQL `RESTORE` / `HEADERONLY`. No copy — avoids accumulating duplicate bak files on the share.
2. **Gate:** HEADERONLY `BackupFinishDate` must be later than `Sync_State.MonthlyRestore` (or first run).
3. SyncEngine restores it onto the **configured** `Db_Mp_SiEng` (same as today).
4. **Step 1b:** while replica still holds daily-sync data, compare it to the restored `HoursReports`, log classified mismatches.
5. **Existing step:** rebuild replica `MP_*` from the restored MasterPlan DB (DROP/CREATE + full ETL) so replica is known-correct.
6. **Step 3b:** compare again after ETL; stamp `MonthlyRestore = BackupFinishDate`.

If everything matches → log says aligned. If not → classified mismatches + evidence so we can fix daily sync later. Mismatches **do not block** the replace (except: compare **throw** before DROP fails closed). UI confirmation names `Db_Mp_SiEng` + `Replica_DB`.

---

## 1b. DEV-020 — SQL staging path map (operator lock 12.08.2026)

| Role | Path |
| --- | --- |
| Client staging (SyncEngine / workstation) | `N:\MasterPlanBakup` |
| SQL Server view of the same folder | `D:\SharedFolder\ProjectsData\MasterPlanBakup` |
| Retention | `MaxRetainedBackups` (default **10**); delete oldest `.bak` beyond the limit; always keep the file about to be restored |
| Transfer | **`File.Move`** from the chosen path into client staging — **not** copy |

Config section: `MasterPlanMonthlyBackup` in SyncEngine `appsettings.json` / template.

**Why:** `RESTORE HEADERONLY` / `RESTORE DATABASE` run on the SQL host. A workstation path such as `N:\MasterPlanGS\…` returns OS error 3 on the server even when `File.Exists` succeeds on the client.

**Out of this slice:** App.Wpf Settings UI for these keys (appsettings is enough for SyncEngine publish); SystemSettings DB rows (optional later).

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
Step 0  HEADERONLY BackupFinishDate > last MonthlyRestore  (NEW gate; no DB writes)
Step 1  Restore .bak → Db_Mp_SiEng          (existing)
Step 1b Compare Replica (still old) vs HoursReports (new) → SyncEngine logs   (NEW)
Step 2  DROP/CREATE MP_* on Replica_DB      (existing)
Step 3  Full ETL from Db_Mp_SiEng           (existing)
Step 3b Compare again after ETL; stamp Sync_State.MonthlyRestore            (NEW)
```

After Step 1, replica still has “what daily updates accumulated”; MasterPlan DB already has “truth from backup”. That is the only window to see daily-sync drift. After Step 2 the old replica hours are gone. Step 3b is the extra check after replica refresh (July findings).

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
- Menu group **מנהלה** (locked — this **overwrites** both configured MP DB and replica `MP_*`).
- Hebrew: **שחזור חודשי MasterPlan**
- Tooltip: `שחזור הגיבוי ל-Db_Mp_SiEng, דוח אי-התאמות מול הרפליקה בלוג SyncEngine, ואז עדכון הרפליקה מהגיבוי`.
- `OpenFileDialog` filter `Backup (*.bak)|*.bak`.
- Strong confirmation: names the **existing** DBs (`Db_Mp_SiEng`, `Replica_DB`), states replica `MP_*` will be replaced after the compare log, SQL must be able to read the file path.
- WPF **launches** `MasterPlan.SyncEngine.exe --monthly --backup "<path>"`. Do **not** fold SMO into the WPF process ([`MASTER_PLAN_MIGRATION.md`](./MASTER_PLAN_MIGRATION.md)).
- Published path: `\\SI-WIN-2K19\AppFolder\AppNet\MasterPlan.SyncEngine\MasterPlan.SyncEngine.exe`. DEV fallback: repo `MasterPlan.SyncEngine\bin\{Debug|Release}\net10.0\`.
- Progress from redirected stdout; after run, the Hebrew summary is in the window **and** in SyncEngine logs.

CLI without UI must still produce the same log (ops / Task Scheduler).

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

Write **before** Step 2 (and again after Step 3) to **existing SyncEngine `ILogger` sinks** — not a new report folder:

| Sink | Path |
| --- | --- |
| Central | `{Logging.CentralLogPath}\SyncEngine\...` |
| Local | `%ProgramData%\SiOffice\MasterPlanSync\logs\` |

Hebrew summary (Warning, so it reaches the central share) + mismatch lines (capped) with Class + CauseCode + Evidence. Zero mismatches still logs «הכול תואם».

Then **always** continue Step 2–3 unless Step 1b **threw** (fail closed before DROP) or the user cancelled in the UI.

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

1. Keep this document as SoT; do not re-introduce an isolated extra DB or a new report folder.
2. Confirm `--monthly` path still matches §2 (read `RunMonthlyBackupRestoreAsync`).
3. Step 0 gate: HEADERONLY then compare to `Sync_State.MonthlyRestore`; stamp only after successful ETL.
4. Step 1b in `MonthlyBackupRestoreService` — SELECT-only on replica; log; **then** existing Step 2–3. Fail closed if compare throws.
5. Step 3b after ETL; remaining BAK_ONLY/ORPHAN = ETL bug in the log.
6. WPF: feature code + confirmation + launch SyncEngine `--monthly --backup`.
7. Tests: classification fixtures; gate; monthly order restore→compare→drop→etl→compare; Employee denied the menu.
8. Build: App.Wpf + App.Wpf.Tests + MasterPlan.SyncEngine.
9. Update `APP_SHELL.md` when the menu exists. No EF migrations.

---

## 6. Complexity / risk

| Topic | Assessment |
| --- | --- |
| Complexity | Medium: insert compare into a destructive pipeline; UI is a launcher |
| Effort | Step 1b + files (1 day); UI (0.5–1 day); tests (0.5) |
| Breaking | Monthly already overwrites MP DB + replica `MP_*`. New step must **not** skip ETL if mismatches are found. If compare **throws**, **fail closed before DROP**. |
| Disk | Full-table compare in memory; log lines capped (not CSV dump). |
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
| New `%ProgramData%\SiNet\mp-monthly\` report folder + CSV files | **Dropped** | Operator lock: existing SyncEngine central/local log sinks only |
| Copying bak into staging (duplicate files on share) | **Dropped** | Operator lock 12.08.2026: **move** only |
| Unlimited bak retention in staging | **Dropped** | Cap at `MaxRetainedBackups` (default 10) |

---

## 9. Needs Review

1. Whether Management Windows accounts can run restore, or only the SyncEngine task identity (`SI-ENG\sieng`).
2. Hebrew mismatch volume on the central share (cap is 100 lines per compare phase).

Locked (no longer Needs Review): menu **מנהלה**; fail closed before DROP if compare throws; published SyncEngine path `\\SI-WIN-2K19\AppFolder\AppNet\MasterPlan.SyncEngine\MasterPlan.SyncEngine.exe`.

---

## 10. Acceptance

- `--monthly --backup` still restores **configured** `Db_Mp_SiEng` and then reloads replica `MP_*` from it. **No third database.**
- Gate refuses a bak whose `BackupFinishDate` is not later than `Sync_State.MonthlyRestore` (no DB writes).
- After restore and **before** DROP, SyncEngine logs contain a Hebrew summary + mismatch causes even when counts are zero («הכול תואם»).
- After ETL, a second compare is logged; `MonthlyRestore` is stamped only on success.
- Employee cannot open the UI; Management/Administrator can.
- Build gate as in §5. **DB/schema: none** (uses existing `Sync_State` row, not a new table / not EF).

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
