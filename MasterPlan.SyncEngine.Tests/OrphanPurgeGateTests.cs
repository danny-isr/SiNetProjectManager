using MasterPlan.SyncEngine;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class OrphanPurgeGateTests
{
    private static OrphanPurgeOptions Options() => new()
    {
        Enabled = true,
        PurgeRequested = true,
        MinAbsoluteFetch = 1000,
        MinFetchFractionOfReplica = 0.5,
        MaxPurgeFraction = 0.10,
        MaxAbsolutePurge = 500
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
            Options());

        Assert.False(eval.Allowed);
        Assert.Contains("G1", eval.BlockReason, StringComparison.Ordinal);
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
            Options());

        Assert.False(eval.Allowed);
        Assert.Contains("G1", eval.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public void When_fetch_too_small_then_g2_blocks()
    {
        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 10,
            replicaRowCount: 5000,
            orphans: [Row(1, DateTime.UtcNow.Date)],
            Options());

        Assert.False(eval.Allowed);
        Assert.Contains("G2", eval.BlockReason, StringComparison.Ordinal);
        Assert.Empty(eval.ToPurge);
    }

    [Fact]
    public void When_purge_fraction_over_10_percent_then_g3_warns_but_allows()
    {
        var orphans = Enumerable.Range(1, 600).Select(i => Row(i, DateTime.UtcNow.Date)).ToList();

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans,
            Options());

        Assert.True(eval.Allowed);
        Assert.Equal(600, eval.ToPurge.Count);
        Assert.Contains("G3", eval.WarningReason, StringComparison.Ordinal);
    }

    [Fact]
    public void When_purge_over_absolute_cap_then_g4_warns_but_allows()
    {
        var options = Options() with { MaxAbsolutePurge = 5, MaxPurgeFraction = 1.0 };
        var orphans = Enumerable.Range(1, 10).Select(i => Row(i, DateTime.UtcNow.Date)).ToList();

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 100,
            orphans,
            options);

        Assert.True(eval.Allowed);
        Assert.Equal(10, eval.ToPurge.Count);
        Assert.Contains("G4", eval.WarningReason, StringComparison.Ordinal);
    }

    [Fact]
    public void When_report_date_older_than_window_then_still_purged()
    {
        var now = new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);
        var old = Row(7, now.Date.AddMonths(-30));

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans: [old],
            Options());

        Assert.True(eval.Allowed);
        Assert.Single(eval.ToPurge);
        Assert.Equal(7, eval.ToPurge[0].Id);
        Assert.Empty(eval.DeferredAge);
    }

    [Fact]
    public void When_first_sighting_then_still_purged()
    {
        var now = DateTime.UtcNow;
        var row = Row(9, now.Date);

        var eval = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount: 5000,
            replicaRowCount: 5000,
            orphans: [row],
            Options());

        Assert.True(eval.Allowed);
        Assert.Single(eval.ToPurge);
        Assert.Equal(9, eval.ToPurge[0].Id);
        Assert.Empty(eval.DeferredFirstSighting);
        Assert.False(eval.PersistSightings);
    }

    [Fact]
    public void Null_report_date_is_outside_age_window()
    {
        Assert.False(OrphanPurgeGate.IsInsideAgeWindow(null, DateTime.UtcNow.Date.AddMonths(-24)));
    }
}
