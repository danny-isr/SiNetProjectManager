namespace MasterPlan.SyncEngine;

public sealed class OrphanReplicaRow
{
    public int Id { get; set; }
    public DateTime? ReportDate { get; set; }
    public int? ProjectId { get; set; }
    public int? EmployeeId { get; set; }
    public DateTime? LastUpdated { get; set; }
}

public sealed class OrphanPurgeEvaluation
{
    public bool Allowed { get; init; }
    public string? BlockReason { get; init; }
    /// <summary>Soft warning (G4 volume) — DELETE still proceeds under DEV-025.</summary>
    public string? WarningReason { get; init; }
    public int ReplicaRowCount { get; init; }
    public int FetchedCount { get; init; }
    public int OrphanCount { get; init; }
    public IReadOnlyList<OrphanReplicaRow> ToPurge { get; init; } = [];
    public IReadOnlyList<OrphanReplicaRow> DeferredAge { get; init; } = [];
    public IReadOnlyList<OrphanReplicaRow> DeferredFirstSighting { get; init; } = [];
    /// <summary>Unused under DEV-025 (G6 dropped). Kept so existing callers compile.</summary>
    public IReadOnlyList<int> SightingIdsToPersist { get; init; } = [];
    public bool PersistSightings { get; init; }
}

/// <summary>
/// Gate math for orphan DELETE after a successful full hours reconcile (DEV-025).
/// Hard blocks: G1 full-pull only, G2 min-fetch fail-closed. G3/G5/G6 are not blockers.
/// G4 (absolute cap) is a warning only.
/// </summary>
public static class OrphanPurgeGate
{
    public static OrphanPurgeEvaluation Evaluate(
        bool isFullReconcile,
        DateTime? fromDate,
        int fetchedCount,
        int replicaRowCount,
        IReadOnlyList<OrphanReplicaRow> orphans,
        OrphanPurgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(orphans);
        ArgumentNullException.ThrowIfNull(options);

        if (!isFullReconcile || fromDate.HasValue)
        {
            return Blocked("G1_FULL_PULL_ONLY", fetchedCount, replicaRowCount, orphans.Count);
        }

        var minRequired = Math.Max(
            options.MinAbsoluteFetch,
            (int)Math.Ceiling(replicaRowCount * options.MinFetchFractionOfReplica));

        if (fetchedCount < minRequired)
        {
            return Blocked(
                $"G2_MIN_FETCH fetched={fetchedCount} required>={minRequired}",
                fetchedCount,
                replicaRowCount,
                orphans.Count);
        }

        string? warning = null;
        if (replicaRowCount > 0
            && orphans.Count > replicaRowCount * options.MaxPurgeFraction)
        {
            warning =
                $"G3_MAX_FRACTION purge={orphans.Count} replica={replicaRowCount} maxFraction={options.MaxPurgeFraction:0.##} (warning only; DEV-025)";
        }

        if (orphans.Count > options.MaxAbsolutePurge)
        {
            var g4 =
                $"G4_MAX_ABSOLUTE purge={orphans.Count} max={options.MaxAbsolutePurge} (warning only; DEV-025)";
            warning = string.IsNullOrWhiteSpace(warning) ? g4 : warning + "; " + g4;
        }

        return new OrphanPurgeEvaluation
        {
            Allowed = true,
            BlockReason = null,
            WarningReason = warning,
            ReplicaRowCount = replicaRowCount,
            FetchedCount = fetchedCount,
            OrphanCount = orphans.Count,
            ToPurge = orphans,
            PersistSightings = false
        };
    }

    /// <summary>Kept for DEV-019 tests/history. Age deferral is not a DEV-025 hard block.</summary>
    public static bool IsInsideAgeWindow(DateTime? reportDate, DateTime ageCutoffInclusive)
    {
        if (!reportDate.HasValue)
            return false;

        return reportDate.Value.Date >= ageCutoffInclusive;
    }

    private static OrphanPurgeEvaluation Blocked(
        string reason,
        int fetchedCount,
        int replicaRowCount,
        int orphanCount)
        => new()
        {
            Allowed = false,
            BlockReason = reason,
            FetchedCount = fetchedCount,
            ReplicaRowCount = replicaRowCount,
            OrphanCount = orphanCount,
            PersistSightings = false
        };
}
