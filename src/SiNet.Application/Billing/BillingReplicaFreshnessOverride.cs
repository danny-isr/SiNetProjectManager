namespace SiNet.Application.Billing;

/// <summary>
/// One-shot age-stale bypass for a single dashboard read. Does not mark Replica fresh
/// and does not apply to structural fatals (missing tables / Sync_State).
/// </summary>
public static class BillingReplicaFreshnessOverride
{
    public static bool CanBypassAgeBlock(
        BillingReplicaFreshnessDecision decision,
        bool allowStaleReplicaForCurrentCheck)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (!allowStaleReplicaForCurrentCheck || !decision.CandidatesBlocked)
            return false;
        if (decision.MissingRequiredTables.Count > 0)
            return false;
        if (decision.MissingSyncStateEntities.Count > 0)
            return false;
        if (decision.OldestSyncAge is null)
            return false;
        return string.Equals(
            decision.Code,
            BillingDataQualityWarningCodes.ReplicaFreshnessFatal,
            StringComparison.Ordinal);
    }
}
