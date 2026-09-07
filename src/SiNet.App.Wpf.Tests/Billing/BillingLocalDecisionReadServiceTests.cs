using SiNet.Application.Billing;
using SiNet.Infrastructure.Sql.Services.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingLocalDecisionReadServiceTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LocalToday = new(2026, 9, 7);
    private static readonly TimeProvider Clock = new FrozenUtcTimeProvider(UtcNow);

    [Fact]
    public async Task Local_decision_is_applied_after_candidate_state_without_changing_inputs()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-1));
        var store = new MemoryStore();
        store.Add(Decision(5905, BillingLocalDecisionType.PrepareBill, observedBillId: null));
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock, localDecisions: store);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        var row = Assert.Single(result.Candidates);
        Assert.Equal(BillingCandidateState.ReviewNow, row.CandidateState);
        Assert.Equal(8m, row.HoursSinceLastBill);
        Assert.Null(row.LatestBillId);
        Assert.NotNull(row.LocalDecision);
        Assert.Equal(BillingLocalDecisionType.PrepareBill, row.LocalDecision.DecisionType);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task Newer_replica_bill_supersedes_stored_PrepareBill()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-1), latestBillId: 88);
        var store = new MemoryStore();
        store.Add(Decision(5905, BillingLocalDecisionType.PrepareBill, observedBillId: 10));
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock, localDecisions: store);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        var row = Assert.Single(result.Candidates);
        Assert.Null(row.LocalDecision);
        Assert.Equal(88, row.LatestBillId);
        Assert.Equal(BillingCandidateState.CoveredByLatestBill, row.CandidateState);
    }

    [Fact]
    public async Task Stale_replica_blocks_before_local_decisions_are_loaded()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-80));
        var store = new MemoryStore();
        store.Add(Decision(5905, BillingLocalDecisionType.PrepareBill, observedBillId: null));
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock, localDecisions: store);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        Assert.True(result.CandidatesBlocked);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, replica.LoadFactsCalls);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Cancelled_decision_restores_computed_behavior()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-1));
        var store = new MemoryStore();
        store.Add(Decision(5905, BillingLocalDecisionType.NotNow, reason: "hold", reviewAgain: LocalToday.AddDays(10)) with
        {
            ClearedAtUtc = UtcNow
        });
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock, localDecisions: store);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        var row = Assert.Single(result.Candidates);
        Assert.Null(row.LocalDecision);
        Assert.Equal(BillingCandidateState.ReviewNow, row.CandidateState);
        Assert.True(BillingLocalDecisionApplier.IncludeInDefaultActionableView(
            row,
            BillingDashboardViewModelActionable.States,
            LocalToday));
    }

    private static BillingReviewDecisionRecord Decision(
        int projectId,
        BillingLocalDecisionType type,
        string? reason = "הערה",
        DateTime? reviewAgain = null,
        int? observedBillId = null) =>
        new(
            projectId,
            type,
            reason,
            reviewAgain,
            UtcNow.AddDays(-1),
            7,
            "manager",
            null,
            null,
            null,
            null,
            null,
            null,
            observedBillId,
            observedBillId is null ? null : 2,
            observedBillId is null ? null : new DateTime(2026, 6, 1),
            8m,
            LocalToday.AddDays(-2));

    private sealed class MemoryStore : IBillingReviewDecisionStore
    {
        private readonly List<BillingReviewDecisionRecord> _rows = [];
        public int Calls { get; private set; }

        public void Add(BillingReviewDecisionRecord row) => _rows.Add(row);

        public Task<IReadOnlyList<BillingReviewDecisionRecord>> GetByProjectIdsAsync(
            IReadOnlyList<int> projectIds,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            IReadOnlyList<BillingReviewDecisionRecord> match =
                _rows.Where(r => projectIds.Contains(r.ProjectId)).ToList();
            return Task.FromResult(match);
        }
    }

    private sealed class FrozenUtcTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeReplica : IReplicaBillingDataSource
    {
        public required ReplicaBillingProbe Probe { get; set; }
        public required ReplicaBillingSnapshot Facts { get; set; }
        public int LoadFactsCalls { get; private set; }

        public Task<ReplicaBillingProbe> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Probe);

        public Task<ReplicaBillingSnapshot> LoadFactsAsync(
            BillingDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            LoadFactsCalls++;
            return Task.FromResult(Facts);
        }

        public static FakeReplica Fresh(DateTime syncTime, int? latestBillId = null) =>
            WithSync(BillingReplicaFreshnessEvaluatorTests.FreshSyncTimes(syncTime), latestBillId);

        public static FakeReplica WithSync(IReadOnlyDictionary<string, DateTime?> times, int? latestBillId = null)
        {
            DateTime? Get(string name) => times.TryGetValue(name, out var value) ? value : null;
            var diagnostics = new ReplicaConnectionDiagnostics(
                ConfiguredDataSource: "example-host",
                InitialCatalog: "Replica_DB",
                SqlServerName: "example-host",
                SqlMachineName: "example-host",
                SqlInstanceName: "SIDATA",
                DatabaseName: "Replica_DB",
                ProjectsSyncTime: Get("Projects"),
                BillsSyncTime: Get("Bills"),
                IntakesSyncTime: Get("Intakes"),
                ProjectHoursSyncTime: Get("ProjectHours"),
                ProjectHoursExtendedSyncTime: Get("ProjectHoursExtended"),
                MaxHoursReportDate: LocalToday.AddDays(-1),
                MaxProjectsLastUpdated: LocalToday.AddDays(-1),
                MaxBillsLastUpdated: LocalToday.AddDays(-1),
                MaxIntakesLastUpdated: LocalToday.AddDays(-1));

            BillingBillFact[] bills = latestBillId is int billId
                ?
                [
                    new BillingBillFact(
                        billId,
                        5905,
                        billId.ToString(),
                        1000m,
                        MasterPlanBillStatusIds.Submitted,
                        "הוגש",
                        LocalToday.AddDays(-1),
                        LocalToday.AddDays(-1))
                ]
                : [];

            return new FakeReplica
            {
                Probe = new ReplicaBillingProbe(
                    diagnostics,
                    MissingRequiredTables: Array.Empty<string>(),
                    SyncStateTablePresent: true,
                    SyncTimesByEntity: times),
                Facts = new ReplicaBillingSnapshot(
                    [
                        new BillingProjectFact(
                            5905, "5905", "Test", "Customer", 1, "Active", true, 1m)
                    ],
                    bills,
                    [new BillingHourFact(5905, LocalToday.AddDays(-2), 8m)],
                    ReceivedThisMonth: 10m,
                    HoursOnlyInBasicCount: 0)
            };
        }
    }
}
