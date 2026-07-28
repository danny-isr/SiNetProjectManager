# DB baseline and restore rehearsal — checklist

> **Status: Not Run / Manual Pending.**
> No step below has been executed. Nothing in this round touched a live database.
> Audit findings #8 (baseline + rehearsal), #9 (Replica hours tables) and #10 (AUTO_CLOSE / RCSI)
> stay **open** until an operator fills in this document.

Related: [`../DATABASE_RECOVERY_BASELINE.md`](../DATABASE_RECOVERY_BASELINE.md).

## Run metadata

| Field | Value |
| --- | --- |
| Operator | _(not run)_ |
| Date (UTC) | _(not run)_ |
| Repository commit | _(not run)_ |
| SQL Server instance | _(not run)_ |
| Rehearsal instance (isolated) | _(not run)_ |

## Tooling

All scripts are **dry run by default** and print the exact T-SQL they would execute. Add `-Execute`
only when you intend to act.

| Script | Purpose |
| --- | --- |
| [`scripts/db/backup-baseline.ps1`](../../scripts/db/backup-baseline.ps1) | COPY_ONLY + CHECKSUM backup of SiData and Replica, `RESTORE VERIFYONLY`, writes a `.metadata.json` with table count and `__EFMigrationsHistory` |
| [`scripts/db/restore-rehearsal.ps1`](../../scripts/db/restore-rehearsal.ps1) | Restores a backup into `<Database>_Rehearsal` on an isolated instance and validates it |
| [`scripts/db/apply-replica-migrations.ps1`](../../scripts/db/apply-replica-migrations.ps1) | Creates `dbo.SchemaVersions` on Replica and applies pending `MasterPlan.SyncEngine/Migrations/*.sql` with checksums |
| [`scripts/db/check-database-settings.ps1`](../../scripts/db/check-database-settings.ps1) | Read-only report of AUTO_CLOSE / RCSI / snapshot isolation |

## 1. Baseline backup (SiData + Replica)

| # | Step | Command | Result |
| --- | --- | --- | --- |
| 1.1 | Dry run the backup plan | `pwsh scripts/db/backup-baseline.ps1 -Server <srv> -BackupDirectory <dir>` | Not Run |
| 1.2 | Take the backups | same command `-Execute` | Not Run |
| 1.3 | `RESTORE VERIFYONLY` passed for SiData | (part of 1.2) | Not Run |
| 1.4 | `RESTORE VERIFYONLY` passed for Replica | (part of 1.2) | Not Run |
| 1.5 | `.metadata.json` written and archived with the `.bak` | | Not Run |

## 2. Restore rehearsal on an isolated instance

| # | Step | Pass criteria | Result |
| --- | --- | --- | --- |
| 2.1 | Restore SiData backup into `SiData_Rehearsal` | Restore completes | Not Run |
| 2.2 | User table count | >= 85 (matches `ToTable` count in `SiNetSQLDbContextModelSnapshot`) | Not Run |
| 2.3 | `__EFMigrationsHistory` | Non-empty; latest id matches the deployed build | Not Run |
| 2.4 | AUTO_CLOSE on the restored DB | OFF | Not Run |
| 2.5 | Restore Replica backup into `Replica_DB_Rehearsal` | Restore completes | Not Run |
| 2.6 | `MP_TimeHourReports` present | Table exists | Not Run |
| 2.7 | `MP_ProjectHoursExtended` present | Table exists | Not Run |
| 2.8 | Application startup against the rehearsal DB | New System reaches the shell | Not Run |

## 3. Replica migration history

| # | Step | Pass criteria | Result |
| --- | --- | --- | --- |
| 3.1 | Dry run `apply-replica-migrations.ps1` | Lists `001_AddHoursEndpointTables.sql` as pending (or already applied) | Not Run |
| 3.2 | Apply with `-Execute` on Replica | `dbo.SchemaVersions` has one row per applied script | Not Run |
| 3.3 | Post-check | Both MP hours tables exist | Not Run |
| 3.4 | Re-run is a no-op | Second run reports `[skip]` for every script | Not Run |

## 4. Database settings

| # | Step | Pass criteria | Result |
| --- | --- | --- | --- |
| 4.1 | `check-database-settings.ps1` report captured | Output attached below | Not Run |
| 4.2 | AUTO_CLOSE OFF on the permanent server | `is_auto_close_on = 0` for SiData and Replica | Not Run |
| 4.3 | Blocking baseline collected before any RCSI decision | Wait-stats/blocking sample attached | Not Run |
| 4.4 | RCSI decision recorded (enable / do not enable) | Decision + rationale written here | Not Run |

## 5. Sign-off

| Field | Value |
| --- | --- |
| All sections Pass | No |
| Blocking issues found | _(not run)_ |
| Approved for production use as a recovery path | **No** |
