namespace MasterPlan.SyncEngine;

/// <summary>
/// Step 0 of monthly restore: the bak HEADERONLY <c>BackupFinishDate</c> must be later than
/// the last successful monthly stamp (<c>Sync_State.MonthlyRestore</c>). First run (no stamp) is allowed.
/// </summary>
public static class MonthlyRestoreGate
{
    public const string SyncStateEntityName = "MonthlyRestore";

    public static bool IsNewerThanLastRestore(DateTime backupFinishDate, DateTime? lastSuccessfulRestore)
    {
        if (!lastSuccessfulRestore.HasValue)
        {
            return true;
        }

        return backupFinishDate > lastSuccessfulRestore.Value;
    }
}
