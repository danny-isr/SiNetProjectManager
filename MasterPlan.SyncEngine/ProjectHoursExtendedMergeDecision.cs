namespace MasterPlan.SyncEngine;

/// <summary>
/// Pure decision for daily UPSERT of <c>MP_ProjectHoursExtended</c> (mirrors MERGE WHEN MATCHED).
/// Repair of null Duration/TotalHours does not require source LastUpdated.
/// </summary>
public static class ProjectHoursExtendedMergeDecision
{
    /// <summary>
    /// Whether a matched API row should UPDATE the replica target.
    /// </summary>
    public static bool ShouldUpdate(
        DateTime? targetLastUpdated,
        DateTime? sourceLastUpdated,
        bool targetDurationIsNull,
        bool sourceDurationHasValue,
        bool targetTotalHoursIsNull,
        bool sourceTotalHoursHasValue)
    {
        var newerByLastUpdated = sourceLastUpdated.HasValue
            && (!targetLastUpdated.HasValue || sourceLastUpdated.Value > targetLastUpdated.Value);

        var repairDuration = targetDurationIsNull && sourceDurationHasValue;
        var repairTotalHours = targetTotalHoursIsNull && sourceTotalHoursHasValue;

        return newerByLastUpdated || repairDuration || repairTotalHours;
    }

    /// <summary>
    /// COALESCE semantics for Duration / TotalHours / LastUpdated so API null never wipes a good replica value.
    /// </summary>
    public static (decimal? Duration, TimeSpan? TotalHours, DateTime? LastUpdated) CoalescePreserve(
        decimal? sourceDuration,
        decimal? targetDuration,
        TimeSpan? sourceTotalHours,
        TimeSpan? targetTotalHours,
        DateTime? sourceLastUpdated,
        DateTime? targetLastUpdated)
        => (
            sourceDuration ?? targetDuration,
            sourceTotalHours ?? targetTotalHours,
            sourceLastUpdated ?? targetLastUpdated);
}
