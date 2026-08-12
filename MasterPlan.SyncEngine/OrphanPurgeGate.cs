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
    public int ReplicaRowCount { get; init; }
    public int FetchedCount { get; init; }
    public int OrphanCount { get; init; }
    public IReadOnlyList<OrphanReplicaRow> ToPurge { get; init; } = [];
    public IReadOnlyList<OrphanReplicaRow> DeferredAge { get; init; } = [];
    public IReadOnlyList<OrphanReplicaRow> DeferredFirstSighting { get; init; } = [];
    /// <summary>All current orphan IDs to persist as the next sighting baseline (only when G2 passed).</summary>
    public IReadOnlyList<int> SightingIdsToPersist { get; init; } = [];
    public bool PersistSightings { get; init; }
}

/// <summary>
/// Pure gate math for DEV-019 (G1–G6). Side effects (CSV/DELETE/JSON) live in <see cref="OrphanPurgeRunner"/>.
/// </summary>
public static class OrphanPurgeGate
{
    public static OrphanPurgeEvaluation Evaluate(
        bool isFullReconcile,
        DateTime? fromDate,
        int fetchedCount,
        int replicaRowCount,
        IReadOnlyList<OrphanReplicaRow> orphans,
        IReadOnlySet<int> previousSightings,
        OrphanPurgeOptions options,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(orphans);
        ArgumentNullException.ThrowIfNull(previousSightings);
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

        var ageCutoff = utcNow.Date.AddMonths(-options.AgeWindowMonths);
        var deferredAge = new List<OrphanReplicaRow>();
        var ageOk = new List<OrphanReplicaRow>();

        foreach (var row in orphans)
        {
            if (options.IncludeLegacy || IsInsideAgeWindow(row.ReportDate, ageCutoff))
            {
                ageOk.Add(row);
            }
            else
            {
                deferredAge.Add(row);
            }
        }

        var deferredFirst = new List<OrphanReplicaRow>();
        var purgeCandidates = new List<OrphanReplicaRow>();
        foreach (var row in ageOk)
        {
            if (previousSightings.Contains(row.Id))
            {
                purgeCandidates.Add(row);
            }
            else
            {
                deferredFirst.Add(row);
            }
        }

        var sightingIds = orphans.Select(o => o.Id).OrderBy(id => id).ToArray();

        if (replicaRowCount > 0
            && purgeCandidates.Count > replicaRowCount * options.MaxPurgeFraction)
        {
            return new OrphanPurgeEvaluation
            {
                Allowed = false,
                BlockReason =
                    $"G3_MAX_FRACTION purge={purgeCandidates.Count} replica={replicaRowCount} maxFraction={options.MaxPurgeFraction:0.##}",
                ReplicaRowCount = replicaRowCount,
                FetchedCount = fetchedCount,
                OrphanCount = orphans.Count,
                ToPurge = [],
                DeferredAge = deferredAge,
                DeferredFirstSighting = deferredFirst.Concat(purgeCandidates).ToList(),
                SightingIdsToPersist = sightingIds,
                PersistSightings = true
            };
        }

        if (purgeCandidates.Count > options.MaxAbsolutePurge)
        {
            return new OrphanPurgeEvaluation
            {
                Allowed = false,
                BlockReason =
                    $"G4_MAX_ABSOLUTE purge={purgeCandidates.Count} max={options.MaxAbsolutePurge}",
                ReplicaRowCount = replicaRowCount,
                FetchedCount = fetchedCount,
                OrphanCount = orphans.Count,
                ToPurge = [],
                DeferredAge = deferredAge,
                DeferredFirstSighting = deferredFirst.Concat(purgeCandidates).ToList(),
                SightingIdsToPersist = sightingIds,
                PersistSightings = true
            };
        }

        return new OrphanPurgeEvaluation
        {
            Allowed = true,
            BlockReason = null,
            ReplicaRowCount = replicaRowCount,
            FetchedCount = fetchedCount,
            OrphanCount = orphans.Count,
            ToPurge = purgeCandidates,
            DeferredAge = deferredAge,
            DeferredFirstSighting = deferredFirst,
            SightingIdsToPersist = sightingIds,
            PersistSightings = true
        };
    }

    /// <summary>null ReportDate is treated as outside the window (deferred) unless IncludeLegacy.</summary>
    public static bool IsInsideAgeWindow(DateTime? reportDate, DateTime ageCutoffInclusive)
    {
        if (!reportDate.HasValue)
        {
            return false;
        }

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
