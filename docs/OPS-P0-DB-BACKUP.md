# OPS — P0 Database backup & restore drill

Operational checklist before production cutover of `SiNet.App.Wpf`.
**Do not store connection strings or credentials in this document.**

## Status — `Manual Pending` (as of 2026-08-02)

| Item | Status |
| --- | --- |
| Full backup of production SQL database | **Not done** — requires DBA / server admin |
| Restore drill to a scratch database | **Not done** |
| Verify `AUTO_CLOSE = OFF` | **Not done** |
| Verify RCSI (`READ_COMMITTED_SNAPSHOT`) | **Not done** |
| Document RPO/RTO with owner | **Not done** |

## Checks (run on SQL Server as admin)

```sql
-- Database options
SELECT name, is_auto_close_on, is_read_committed_snapshot_on, recovery_model_desc
FROM sys.databases
WHERE name = N'<SiNetDbName>';

-- Prefer:
-- is_auto_close_on = 0
-- is_read_committed_snapshot_on = 1
-- recovery_model_desc = FULL (if log backups exist) or as per ops policy
```

If AUTO_CLOSE is on:

```sql
ALTER DATABASE [<SiNetDbName>] SET AUTO_CLOSE OFF;
```

If RCSI is off (coordinate a maintenance window — can block briefly):

```sql
ALTER DATABASE [<SiNetDbName>] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
```

## Backup

```sql
BACKUP DATABASE [<SiNetDbName>]
TO DISK = N'\\<backup-share>\SiNet_<yyyyMMdd_HHmm>.bak'
WITH COPY_ONLY, COMPRESSION, STATS = 10;
```

## Restore drill

1. Restore to `[SiNet_RestoreDrill]` on a non-production instance (or same instance with a new name).
2. Point a **dev** vault / connection string at the drill DB.
3. Launch `SiNet.App.Wpf` Debug, confirm login + project list + one workflow query.
4. Drop the drill DB after sign-off.

## Sign-off log

| Date | Operator | Backup file | Restore OK | AUTO_CLOSE | RCSI | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| | | | | | | |
