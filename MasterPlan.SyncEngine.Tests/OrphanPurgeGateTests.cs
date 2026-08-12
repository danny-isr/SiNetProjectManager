using MasterPlan.SyncEngine;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class OrphanPurgeGateTests
{
    private static OrphanPurgeOptions Options(bool includeLegacy = false) => new()
    {
        Enabled = true,
        PurgeRequested = true,
        IncludeLegacy = includeLegacy,
        MinAbsoluteFetch = 1000,
        MinFetchFractionOfReplica = 0.5,
        MaxPurgeFraction = 0.10,
        MaxAbsolutePurge = 500,
        AgeWindowMonths = 24
    };

    private static OrphanReplicaRow Row(int id, DateTime? reportDate)
        => new() { Id = id, ReportDate = reportDate };

    [Fact]
    public void When_fromdate_set_then_g1_blocks()
    {
        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: new DateTime(2026, 8, 1),
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans: [Row(1, DateTime.UtcNow.Date)],
            previousSightings: new HashSet<int> { 1 },
            Options(),
            DateTime.UtcNow);

        Assert.False(eval.Allowed);
        Assert.Contains("G1", eval.BlockReason, StringComparison.Ordinal);
        Assert.False(eval.PersistSightings);
    }

    [Fact]
    public void When_not_reconcile_then_g1_blocks()
    {
        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: false,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans: [Row(1, DateTime.UtcNow.Date)],
            previousSightings: new HashSet<int> { 1 },
            Options(),
            DateTime.UtcNow);

        Assert.False(eval.Allowed);
        Assert.Contains("G1", eval.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public void When_fetch_too_small_then_g2_blocks_and_does_not_persist()
    {
        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 10,
            replicaRowCount: 5000,
            orphans: [Row(1, DateTime.UtcNow.Date)],
            previousSightings: new HashSet<int> { 1 },
            Options(),
            DateTime.UtcNow);

        Assert.False(eval.Allowed);
        Assert.Contains("G2", eval.BlockReason, StringComparison.Ordinal);
        Assert.False(eval.PersistSightings);
    }

    [Fact]
    public void When_purge_fraction_over_10_percent_then_g3_blocks_but_persists_sightings()
    {
        var orphans = Enumerable.Range(1, 600).Select(i => Row(i, DateTime.UtcNow.Date)).ToList();
        var previous = orphans.Select(o => o.Id).ToHashSet();

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans,
            previous,
            Options(),
            DateTime.UtcNow);

        Assert.False(eval.Allowed);
        Assert.Contains("G3", eval.BlockReason, StringComparison.Ordinal);
        Assert.True(eval.PersistSightings);
    }

    [Fact]
    public void When_purge_over_absolute_cap_then_g4_blocks()
    {
        var options = Options() with { MaxAbsolutePurge = 5, MaxPurgeFraction = 1.0 };
        var orphans = Enumerable.Range(1, 10).Select(i => Row(i, DateTime.UtcNow.Date)).ToList();
        var previous = orphans.Select(o => o.Id).ToHashSet();

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 100,
            orphans,
            previous,
            options,
            DateTime.UtcNow);

        Assert.False(eval.Allowed);
        Assert.Contains("G4", eval.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public void When_report_date_older_than_window_then_deferred_age()
    {
        var now = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);
        var old = Row(7, now.Date.AddMonths(-30));
        var previous = new HashSet<int> { 7 };

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans: [old],
            previous,
            Options(),
            now);

        Assert.True(eval.Allowed);
        Assert.Empty(eval.ToPurge);
        Assert.Single(eval.DeferredAge);
    }

    [Fact]
    public void When_include_legacy_then_old_rows_can_purge()
    {
        var now = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);
        var old = Row(7, now.Date.AddMonths(-30));
        var previous = new HashSet<int> { 7 };

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans: [old],
            previous,
            Options(includeLegacy: true),
            now);

        Assert.True(eval.Allowed);
        Assert.Single(eval.ToPurge);
        Assert.Empty(eval.DeferredAge);
    }

    [Fact]
    public void When_first_sighting_then_deferred_not_purged()
    {
        var now = DateTime.UtcNow;
        var row = Row(9, now.Date);

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans: [row],
            previousSightings: new HashSet<int>(),
            Options(),
            now);

        Assert.True(eval.Allowed);
        Assert.Empty(eval.ToPurge);
        Assert.Single(eval.DeferredFirstSighting);
        Assert.True(eval.PersistSightings);
        Assert.Contains(9, eval.SightingIdsToPersist);
    }

    [Fact]
    public void When_second_sighting_inside_window_then_allowed_to_purge()
    {
        var now = DateTime.UtcNow;
        var row = Row(9, now.Date);

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans: [row],
            previousSightings: new HashSet<int> { 9 },
            Options(),
            now);

        Assert.True(eval.Allowed);
        Assert.Single(eval.ToPurge);
        Assert.Equal(9, eval.ToPurge[0].Id);
    }

    [Fact]
    public void Null_report_date_is_outside_age_window()
    {
        Assert.False(OrphanPurgeGate.IsInsideAgeWindow(null, DateTime.UtcNow.Date.AddMonths(-24)));
    }
}
