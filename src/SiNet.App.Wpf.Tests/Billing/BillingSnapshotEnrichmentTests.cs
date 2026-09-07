using SiNet.Application.Billing;
using SiNet.Infrastructure.Sql.Services.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingSnapshotEnrichmentTests
{
    private static readonly DateTime AsOf = new(2026, 8, 1);
    private static readonly DateTime SnapshotDate = new(2026, 2, 1);
    private static readonly DateTime UtcNow = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LocalToday = new(2026, 9, 7);
    private static readonly TimeProvider Clock = new FrozenUtcTimeProvider(UtcNow);

    [Fact]
    public void Replica_customer_name_wins_over_snapshot()
    {
        var row = ReviewNowRow(5905, "Replica Customer", 100m);
        var extra = Enrichment(5905, customer: "Snapshot Customer", balance: 50m);

        var applied = Assert.Single(BillingSnapshotEnrichmentApplier.Apply(
            [row], extra, SnapshotDate));

        Assert.Equal("Replica Customer", applied.CustomerName);
        Assert.Equal(50m, applied.SnapshotBalance);
        Assert.Equal(100m, applied.CurrentFeeSum);
        Assert.Equal(row.CandidateState, applied.CandidateState);
    }

    [Fact]
    public void Blank_replica_customer_name_is_enriched_from_snapshot()
    {
        var row = ReviewNowRow(5905, null, 100m);
        var extra = Enrichment(5905, customer: "Snapshot Customer");

        var applied = Assert.Single(BillingSnapshotEnrichmentApplier.Apply(
            [row], extra, SnapshotDate));

        Assert.Equal("Snapshot Customer", applied.CustomerName);
    }

    [Fact]
    public void Nonblank_replica_customer_name_is_not_overwritten()
    {
        var row = ReviewNowRow(5905, "  Keep Me  ", 1m);
        var extra = Enrichment(5905, customer: "Other");

        var applied = Assert.Single(BillingSnapshotEnrichmentApplier.Apply(
            [row], extra, SnapshotDate));

        Assert.Equal("Keep Me", applied.CustomerName);
    }

    [Fact]
    public void Missing_ProjectsExtraData_leaves_candidate_intact()
    {
        var row = ReviewNowRow(5905, "Acme", 12m);
        var applied = Assert.Single(BillingSnapshotEnrichmentApplier.Apply(
            [row],
            new Dictionary<int, BillingSnapshotProjectEnrichment>(),
            SnapshotDate));

        Assert.Equal(5905, applied.ProjectId);
        Assert.Equal(row.CandidateState, applied.CandidateState);
        Assert.Equal(row.Hours30, applied.Hours30);
        Assert.Equal("Acme", applied.CustomerName);
        Assert.Null(applied.SnapshotBalance);
        Assert.Null(applied.SnapshotOpenBillSum);
        Assert.Null(applied.SnapshotFeeTypes);
        Assert.Equal(SnapshotDate, applied.SnapshotDate);
    }

    [Fact]
    public void Null_snapshot_values_remain_null_not_zero()
    {
        var row = ReviewNowRow(3611, "Acme", 1m);
        var extra = new Dictionary<int, BillingSnapshotProjectEnrichment>
        {
            [3611] = new(
                3611,
                Balance: null,
                OpenBillSum: null,
                ApprovedBillSum: null,
                ProgressPercentage: null,
                CustomerName: null,
                SubContractFeeTypeIds: Array.Empty<int>())
        };

        var applied = Assert.Single(BillingSnapshotEnrichmentApplier.Apply([row], extra, SnapshotDate));

        Assert.Null(applied.SnapshotBalance);
        Assert.Null(applied.SnapshotOpenBillSum);
        Assert.Null(applied.SnapshotApprovedBillSum);
        Assert.Null(applied.SnapshotBilledPercent);
        Assert.Null(applied.SnapshotFeeTypes);
    }

    [Fact]
    public void Progress_does_not_alter_candidate_state()
    {
        var row = ReviewNowRow(5905, "Acme", 1m);
        Assert.Equal(BillingCandidateState.ReviewNow, row.CandidateState);

        var extra = Enrichment(5905, progress: 100m, balance: 9_999m, openBill: 8_888m);
        var applied = Assert.Single(BillingSnapshotEnrichmentApplier.Apply([row], extra, SnapshotDate));

        Assert.Equal(BillingCandidateState.ReviewNow, applied.CandidateState);
        Assert.Equal(row.CandidateReason, applied.CandidateReason);
        Assert.Equal(100m, applied.SnapshotBilledPercent);
        Assert.Equal(9_999m, applied.SnapshotBalance);
        Assert.Equal(8_888m, applied.SnapshotOpenBillSum);
    }

    [Fact]
    public void Stale_snapshot_is_labelled_as_snapshot_not_current()
    {
        var row = ReviewNowRow(5905, "Acme", 1m);
        var extra = Enrichment(5905, balance: 1_289_020m);
        var applied = Assert.Single(BillingSnapshotEnrichmentApplier.Apply([row], extra, SnapshotDate));

        Assert.Equal(SnapshotDate, applied.SnapshotDate);
        Assert.Equal(1_289_020m, applied.SnapshotBalance);
        Assert.Null(applied.GetType().GetProperty("CurrentBalance"));
        Assert.Null(applied.GetType().GetProperty("CurrentOpenBillSum"));
        Assert.NotNull(applied.GetType().GetProperty("SnapshotBalance"));
    }

    [Fact]
    public void Mixed_fee_types_are_represented()
    {
        var summary = BillingFeeTypeClassifier.Classify(
        [
            MasterPlanSnapshotFeeTypeIds.FixedPrice,
            MasterPlanSnapshotFeeTypeIds.FixedPrice,
            MasterPlanSnapshotFeeTypeIds.WorkingHours
        ]);

        Assert.NotNull(summary);
        Assert.Equal(BillingSnapshotFeeMix.Mixed, summary.Mix);
        Assert.Equal([3, 4], summary.DistinctFeeTypeIds);
        Assert.Equal(2, Assert.Single(summary.Counts, c => c.FeeTypeId == 3).Count);
        Assert.Equal(1, Assert.Single(summary.Counts, c => c.FeeTypeId == 4).Count);

        var row = ReviewNowRow(1, "Acme", 1m);
        var extra = Enrichment(1, feeTypeIds: [3, 4]);
        var applied = Assert.Single(BillingSnapshotEnrichmentApplier.Apply([row], extra, SnapshotDate));
        Assert.Equal(BillingSnapshotFeeMix.Mixed, applied.SnapshotFeeTypes!.Mix);
        Assert.Equal(BillingCandidateState.ReviewNow, applied.CandidateState);
    }

    [Fact]
    public void Hourly_and_fixed_price_are_distinct_mixes()
    {
        Assert.Equal(
            BillingSnapshotFeeMix.Hourly,
            BillingFeeTypeClassifier.Classify([4, 4])!.Mix);
        Assert.Equal(
            BillingSnapshotFeeMix.FixedPrice,
            BillingFeeTypeClassifier.Classify([3])!.Mix);
        Assert.Equal(
            BillingSnapshotFeeMix.Other,
            BillingFeeTypeClassifier.Classify([1, 1])!.Mix);
        Assert.Null(BillingFeeTypeClassifier.Classify(Array.Empty<int>()));
    }

    [Fact]
    public async Task Missing_enrichment_on_the_service_leaves_replica_candidate()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-1), 5905);
        var monthly = new FakeMonthly
        {
            Load = new MonthlyBillingEnrichmentLoad(
                true,
                Array.Empty<string>(),
                new Dictionary<int, BillingSnapshotProjectEnrichment>())
        };
        var times = replica.Probe.SyncTimesByEntity.ToDictionary(
            kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        times[BillingReplicaRequirements.MonthlyRestoreSyncStateEntity] = SnapshotDate;
        replica.Probe = replica.Probe with { SyncTimesByEntity = times };

        var service = new SqlBillingDashboardReadService(replica, monthly, Clock);
        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        var row = Assert.Single(result.Candidates);
        Assert.Equal(5905, row.ProjectId);
        Assert.False(result.CandidatesBlocked);
        Assert.Null(row.SnapshotBalance);
        Assert.Equal(SnapshotDate, row.SnapshotDate);
        Assert.Equal(SnapshotDate, result.Summary.MonthlySnapshotDate);
        Assert.Equal(1, monthly.Calls);
    }

    [Fact]
    public async Task Stale_current_replica_does_not_load_monthly_enrichment()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-80), 5905);
        var monthly = new FakeMonthly { Load = MonthlyBillingEnrichmentLoad.Unavailable };
        var service = new SqlBillingDashboardReadService(replica, monthly, Clock);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        Assert.True(result.CandidatesBlocked);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, monthly.Calls);
        Assert.Null(result.Summary.MonthlySnapshotDate);
    }

    [Fact]
    public async Task Historical_as_of_can_enrich_when_replica_age_would_block_current()
    {
        var historicalAsOf = LocalToday.AddDays(-30);
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-80), 5905, hourDate: historicalAsOf.AddDays(-2));
        var monthly = new FakeMonthly
        {
            Load = new MonthlyBillingEnrichmentLoad(
                true,
                Array.Empty<string>(),
                Enrichment(5905, balance: 42m, progress: 80m))
        };
        var service = new SqlBillingDashboardReadService(replica, monthly, Clock);

        var result = await service.GetAsync(
            new BillingDashboardRequest(AsOfDate: historicalAsOf));

        Assert.False(result.CandidatesBlocked);
        var row = Assert.Single(result.Candidates);
        Assert.Equal(42m, row.SnapshotBalance);
        Assert.Equal(80m, row.SnapshotBilledPercent);
        Assert.Equal(BillingCandidateState.ReviewNow, row.CandidateState);
        Assert.Equal(1, monthly.Calls);
    }

    [Fact]
    public async Task Unconfigured_snapshot_warns_and_keeps_replica_candidate()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-1), 5905);
        var monthly = new FakeMonthly { Load = MonthlyBillingEnrichmentLoad.Unavailable };
        var service = new SqlBillingDashboardReadService(replica, monthly, Clock);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        Assert.False(result.CandidatesBlocked);
        Assert.Single(result.Candidates);
        Assert.Contains(result.Warnings, w => w.Code == BillingDataQualityWarningCodes.SnapshotEnrichmentUnavailable);
        Assert.DoesNotContain(result.Warnings, w => w.Code == BillingDataQualityWarningCodes.SnapshotDateUnknown);
    }

    [Fact]
    public void Configured_snapshot_without_MonthlyRestore_stamp_warns_date_unknown()
    {
        var warnings = BillingSnapshotWarningBuilder.Build(
            snapshotDatabaseConfigured: true,
            missingSnapshotTables: Array.Empty<string>(),
            snapshotDate: null);

        Assert.Equal(BillingDataQualityWarningCodes.SnapshotDateUnknown, Assert.Single(warnings).Code);
    }

    private static BillingCandidateRow ReviewNowRow(int projectId, string? customer, decimal feeSum)
    {
        var rows = BillingCandidateEngine.BuildRows(
            [new BillingProjectFact(projectId, projectId.ToString(), "P", customer, 1, "Active", true, feeSum)],
            Array.Empty<BillingBillFact>(),
            [new BillingHourFact(projectId, AsOf.AddDays(-2), 8m)],
            AsOf);
        return Assert.Single(rows);
    }

    private static Dictionary<int, BillingSnapshotProjectEnrichment> Enrichment(
        int projectId,
        string? customer = null,
        decimal? balance = null,
        decimal? openBill = null,
        decimal? approved = null,
        decimal? progress = null,
        IReadOnlyList<int>? feeTypeIds = null) =>
        new()
        {
            [projectId] = new BillingSnapshotProjectEnrichment(
                projectId,
                balance,
                openBill,
                approved,
                progress,
                customer,
                feeTypeIds ?? Array.Empty<int>())
        };

    private sealed class FrozenUtcTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeMonthly : IMonthlyBillingEnrichmentDataSource
    {
        public required MonthlyBillingEnrichmentLoad Load { get; set; }
        public int Calls { get; private set; }

        public Task<MonthlyBillingEnrichmentLoad> LoadAsync(
            IReadOnlyList<int> projectIds,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Load);
        }
    }

    private sealed class FakeReplica : IReplicaBillingDataSource
    {
        public required ReplicaBillingProbe Probe { get; set; }
        public required ReplicaBillingSnapshot Facts { get; set; }

        public Task<ReplicaBillingProbe> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Probe);

        public Task<ReplicaBillingSnapshot> LoadFactsAsync(
            BillingDashboardRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Facts);

        public static FakeReplica Fresh(DateTime syncTime, int projectId, DateTime? hourDate = null)
        {
            var times = BillingReplicaFreshnessEvaluatorTests.FreshSyncTimes(syncTime);
            DateTime? Get(string name) => times.TryGetValue(name, out var value) ? value : null;
            var diagnostics = new ReplicaConnectionDiagnostics(
                "example-host", "Replica_DB", "example-host", "example-host", "SIDATA", "Replica_DB",
                Get("Projects"), Get("Bills"), Get("Intakes"), Get("ProjectHours"), Get("ProjectHoursExtended"),
                LocalToday.AddDays(-1), LocalToday.AddDays(-1), LocalToday.AddDays(-1), LocalToday.AddDays(-1));
            var reported = hourDate ?? LocalToday.AddDays(-2);

            return new FakeReplica
            {
                Probe = new ReplicaBillingProbe(
                    diagnostics,
                    Array.Empty<string>(),
                    true,
                    times),
                Facts = new ReplicaBillingSnapshot(
                    [new BillingProjectFact(projectId, projectId.ToString(), "P", "Acme", 1, "Active", true, 1m)],
                    Array.Empty<BillingBillFact>(),
                    [new BillingHourFact(projectId, reported, 8m)],
                    0m,
                    0)
            };
        }
    }
}
