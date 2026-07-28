# Database recovery baseline — SQL freeze (2026-07-27)

> **Status:** Active  
> **Scope:** Documents why historical SQL scripts are **not** valid recovery paths for current
> **SiData** (primary) and **Replica** databases, and defines the approved recovery procedure.

Related: [`NEW_SYSTEM_PRODUCTION_READINESS.md`](./NEW_SYSTEM_PRODUCTION_READINESS.md),
[`NEW_SYSTEM_BOUNDARY.md`](./NEW_SYSTEM_BOUNDARY.md).

---

## 1. Frozen scripts — do not use for recovery

The following scripts may exist outside the repo or in archives. They reflect an **older** schema
snapshot and **must not** be used to rebuild or recover production-like databases:

| Script | What it actually is | Why invalid as a recovery path |
| --- | --- | --- |
| `01-scriptSiEng.sql` | Legacy **SiData** baseline | **35** tables — far below the current EF model (85) |
| `02-script.sql` | Dump of **`Db_Mp_SiEng`** (the MasterPlan source database, ~305 tables) | Not a SiData script at all. An earlier revision of this document described it as a "SiData companion / delta"; that was wrong. |
| `03-scriptReplica.sql` | Legacy **Replica_DB** baseline (13 tables) | Missing tables present in live Replica (see §2) |

**Rule:** Treat these scripts as **historical artifacts only**. Do not run them against SiData or
Replica expecting a working application.

---

## 2. Schema drift evidence

### 2.1 EF ModelSnapshot vs old SiEng script

Current authoritative schema for the New System SQL stack is captured in:

`src/SiNet.Infrastructure.Sql/Migrations/SiNetSQLDbContextModelSnapshot.cs`

- **85** mapped tables — 85 `ToTable(...)` calls, all distinct (counted at commit `22e7458`).
- The `SiNetSQLDbContext` exposes **89** `DbSet<>` properties. The difference is owned/derived types
  that map onto an existing table, not four extra tables. Do not use 89 as a table count.
- Old `01-scriptSiEng.sql` baseline: **35** tables.

Gap: workflow, email/ACC cache, MasterPlan sync, planning taxonomy, native user admin entities, and
many other slices added via EF migrations since the SiEng script era.

### 2.2 Replica script gaps

When `03-scriptReplica.sql` (or equivalent archived replica baseline) is compared to live Replica:

| Missing from old replica script | Used by |
| --- | --- |
| `MP_TimeHourReports` | MasterPlan sync — attendance / time-hour reports |
| `MP_ProjectHoursExtended` | MasterPlan sync — project hours extended |

Live Replica also carries additional MP_* tables maintained by `MasterPlan.SyncEngine`. An old
replica script produces a **partial** replica unusable for sync or reporting without manual repair.

Both tables are created by `MasterPlan.SyncEngine/Migrations/001_AddHoursEndpointTables.sql`. Until
now nothing recorded whether that script had been applied to a given Replica. Use
[`scripts/db/apply-replica-migrations.ps1`](../scripts/db/apply-replica-migrations.ps1), which
creates a `dbo.SchemaVersions` table and records script name, SHA-256 and apply time. It is dry-run
by default.

### 2.3 AUTO_CLOSE ON warning

Databases created or restored from older scripts often have **`AUTO_CLOSE ON`**. This causes:

- Connection churn and first-query latency after idle periods.
- Unexpected behavior under connection pooling and EF `DbContext` factory patterns.

When validating any restored database, check and prefer **`AUTO_CLOSE OFF`**:

```sql
SELECT name, is_auto_close_on FROM sys.databases WHERE name IN (N'SiData', N'Replica');
-- If ON: ALTER DATABASE [SiData] SET AUTO_CLOSE OFF;
```

---

## 3. Approved recovery path

Use **one** of these — never the frozen scripts in §1:

### Option A — Verified backup (preferred)

1. Restore from a **known-good backup** taken from live SiData / Replica (same major app version).
2. Confirm `__EFMigrationsHistory` rows match the deployed application build.
3. Run restore **rehearsal** on an isolated instance before touching production.

### Option B — Fresh baseline from live schema

1. Script schema from **live** SiData (and Replica if needed) using SSMS / `sqlpackage` / equivalent.
2. Export matching `__EFMigrationsHistory` contents.
3. Apply to a clean server; verify table count aligns with `SiNetSQLDbContextModelSnapshot`.
4. Rehearse application startup (`RunNewSystemStartup`) against the baseline DB before cutover.

### Reconciliation after Replica restore

Replica is **not** the business source of truth. After any Replica rebuild:

- Run MasterPlan sync reconciliation checks.
- Compare row counts / watermarks for critical MP_* tables (including `MP_TimeHourReports`,
  `MP_ProjectHoursExtended`).
- Treat SiData as authoritative for workflow, email filing state, and ACC cache helpers.

---

## 4. EF migrations — immutable

Per project rules (`.cursor/rules/ef-migrations-immutable.mdc`):

- **Never** edit historical EF migration files or `SiNetSQLDbContextModelSnapshot.cs` for recovery.
- **Never** hand-write migration SQL as a substitute for `dotnet ef migrations add`.
- Schema fixes go through: model/configuration change → user runs `Add-Migration` → user applies.

Recovery uses **backups** or **live schema export**, not migration rewrites.

---

## 5. Operator checklist (recovery validation)

> **No baseline backup and no restore rehearsal have been performed yet.** Track them in
> [`manual-tests/DB_RESTORE_REHEARSAL_CHECKLIST.md`](./manual-tests/DB_RESTORE_REHEARSAL_CHECKLIST.md),
> which is currently `Not Run`. The scripts under [`scripts/db`](../scripts/db) automate the steps
> below and are dry-run by default.

| Step | Pass criteria |
| --- | --- |
| Table count vs ModelSnapshot | >= 85 user tables |
| `__EFMigrationsHistory` | Matches deployed build |
| `MP_TimeHourReports` / `MP_ProjectHoursExtended` | Present on Replica when sync engine is used |
| `AUTO_CLOSE` | OFF for SiData and Replica |
| App smoke | New System starts; DB connect succeeds — see [`manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md`](./manual-tests/NEW_SYSTEM_SMOKE_CHECKLIST-2026-07-27.md) |

---

## 6. Summary

| Do | Don't |
| --- | --- |
| Restore verified backups | Run `01-scriptSiEng.sql` / `02-script.sql` / `03-scriptReplica.sql` for recovery |
| Baseline from **live** schema + migrations history | Edit historical EF migrations for recovery |
| Rehearse restore before production | Assume Replica script = full Replica |
| Reconcile Replica after rebuild | Treat Replica as source of truth for business data |
