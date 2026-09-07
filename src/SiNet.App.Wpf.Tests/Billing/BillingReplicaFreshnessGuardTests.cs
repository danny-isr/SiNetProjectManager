using SiNet.Application.Billing;
using SiNet.Infrastructure.Sql.Services.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingReplicaFreshnessGuardTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LocalToday = new(2026, 9, 6);
    private static readonly TimeProvider Clock = new FrozenUtcTimeProvider(UtcNow);

    [Fact]
    public async Task When_replica_is_fresh_then_candidates_are_returned()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-1));
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        var row = Assert.Single(result.Candidates);
        Assert.Equal(5905, row.ProjectId);
        Assert.False(result.CandidatesBlocked);
        Assert.Equal(BillingReplicaFreshnessStatus.Healthy, result.FreshnessStatus);
        Assert.Equal(1, replica.LoadFactsCalls);
        Assert.DoesNotContain(result.Warnings, w => w.Code == BillingDataQualityWarningCodes.ReplicaFreshnessFatal);
    }

    [Fact]
    public async Task When_replica_is_40_hours_stale_then_warning_and_candidates_returned()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-40));
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        Assert.False(result.CandidatesBlocked);
        Assert.Equal(BillingReplicaFreshnessStatus.Warning, result.FreshnessStatus);
        Assert.Single(result.Candidates);
        Assert.Equal(1, replica.LoadFactsCalls);
        Assert.Contains(result.Warnings, w => w.Code == BillingDataQualityWarningCodes.ReplicaFreshnessWarning);
    }

    [Fact]
    public async Task When_replica_is_over_72_hours_stale_then_current_candidates_are_blocked()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-80));
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        Assert.True(result.CandidatesBlocked);
        Assert.Equal(BillingReplicaFreshnessStatus.Stale, result.FreshnessStatus);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.Summary.ReviewNowCount);
        Assert.Null(result.Summary.ReceivedThisMonth);
        Assert.Equal(0, replica.LoadFactsCalls);
        Assert.Contains(result.Warnings, w => w.Code == BillingDataQualityWarningCodes.ReplicaFreshnessFatal);
    }

    [Fact]
    public async Task When_required_sync_state_entity_is_missing_then_blocked()
    {
        var times = BillingReplicaFreshnessEvaluatorTests.FreshSyncTimes(UtcNow.AddHours(-1));
        times.Remove("Bills");
        var replica = FakeReplica.WithSync(times);
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        Assert.True(result.CandidatesBlocked);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, replica.LoadFactsCalls);
        Assert.Contains(result.Warnings, w => w.Code == BillingDataQualityWarningCodes.ReplicaSyncStateMissing);
    }

    [Fact]
    public async Task When_extended_hours_table_is_missing_then_blocked()
    {
        var replica = FakeReplica.Fresh(UtcNow.AddHours(-1));
        replica.Probe = replica.Probe with
        {
            MissingRequiredTables = ["MP_ProjectHoursExtended"]
        };
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        Assert.True(result.CandidatesBlocked);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, replica.LoadFactsCalls);
        Assert.Contains(result.Warnings, w => w.Code == BillingDataQualityWarningCodes.ReplicaRequiredTableMissing);
        Assert.DoesNotContain("MasterPlan", result.Candidates.Select(c => c.ProjectName ?? string.Empty));
    }

    [Fact]
    public async Task Diagnostics_do_not_expose_password_or_credentials()
    {
        const string secret = "SuperSecret-Password-123!";
        var connectionString =
            $"Server=example-host;Database=Replica_DB;User ID=billing;Password={secret};TrustServerCertificate=True;";

        var (dataSource, catalog) = ReplicaConnectionStringSanitizer.ReadIdentity(connectionString);
        Assert.Equal("example-host", dataSource);
        Assert.Equal("Replica_DB", catalog);
        Assert.DoesNotContain(secret, dataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", dataSource, StringComparison.OrdinalIgnoreCase);

        var replica = FakeReplica.Fresh(UtcNow.AddHours(-1));
        replica.Probe = replica.Probe with
        {
            Diagnostics = replica.Probe.Diagnostics with
            {
                ConfiguredDataSource = dataSource,
                InitialCatalog = catalog
            }
        };
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock);
        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        var dumped = System.Text.Json.JsonSerializer.Serialize(result.Diagnostics);
        Assert.DoesNotContain(secret, dumped, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", dumped, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pwd=", dumped, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("example-host", result.Diagnostics.ConfiguredDataSource);
        Assert.Equal("Replica_DB", result.Diagnostics.InitialCatalog);
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

        public static FakeReplica Fresh(DateTime syncTime) =>
            WithSync(BillingReplicaFreshnessEvaluatorTests.FreshSyncTimes(syncTime));

        public static FakeReplica WithSync(IReadOnlyDictionary<string, DateTime?> times)
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
                    Array.Empty<BillingBillFact>(),
                    [new BillingHourFact(5905, LocalToday.AddDays(-2), 8m)],
                    ReceivedThisMonth: 10m,
                    HoursOnlyInBasicCount: 0)
            };
        }
    }
}
