using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using SiNet.Infrastructure.Sql.Services.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingStaleReplicaOverrideTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LocalToday = new(2026, 9, 6);
    private static readonly TimeProvider Clock = new FrozenUtcTimeProvider(UtcNow);

    [Fact]
    public async Task Fresh_replica_loads_without_override()
    {
        var replica = GuardReplica.Fresh(UtcNow.AddHours(-1));
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock);

        var result = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));

        Assert.False(result.CandidatesBlocked);
        Assert.Equal(BillingReplicaFreshnessStatus.Healthy, result.FreshnessStatus);
        Assert.NotEmpty(result.Candidates);
        Assert.Equal(1, replica.LoadFactsCalls);
        Assert.DoesNotContain(result.Warnings, w => w.Code == BillingDataQualityWarningCodes.ReplicaFreshnessFatal);
    }

    [Fact]
    public async Task Stale_replica_development_starts_blocked_with_check_anyway()
    {
        var fake = new SwitchableStaleFake();
        var vm = new BillingDashboardViewModel(fake, staleReplicaOverrideAvailable: true);

        await vm.LoadAsync().ConfigureAwait(true);

        Assert.Equal(BillingDashboardUiState.FreshnessBlocked, vm.UiState);
        Assert.True(vm.ShowBlockedPanel);
        Assert.True(vm.ShowStaleReplicaWarning);
        Assert.True(vm.ShowCheckAnywayButton);
        Assert.Equal(BillingDashboardFormatters.StaleReplicaWarning, vm.StaleReplicaWarningText);
        Assert.Equal(BillingDashboardFormatters.CheckAnywayLabel, vm.CheckAnywayLabel);
        Assert.False(vm.ShowCandidatesArea);
        Assert.False(vm.ShowStaleOverrideBanner);
        Assert.False(fake.LastRequest!.AllowStaleReplicaForCurrentCheck);
        Assert.Equal("לא עדכני", vm.FreshnessStatusText);
    }

    [Fact]
    public async Task Stale_replica_development_override_loads_and_keeps_red_warning()
    {
        var fake = new SwitchableStaleFake();
        var vm = new BillingDashboardViewModel(fake, staleReplicaOverrideAvailable: true);
        await vm.LoadAsync().ConfigureAwait(true);

        await vm.CheckAnywayAsync().ConfigureAwait(true);

        Assert.Equal(2, fake.CallCount);
        Assert.True(fake.LastRequest!.AllowStaleReplicaForCurrentCheck);
        Assert.Equal(BillingDashboardUiState.Loaded, vm.UiState);
        Assert.True(vm.ShowCandidatesArea);
        Assert.True(vm.ShowStaleOverrideBanner);
        Assert.Equal(BillingDashboardFormatters.StaleOverrideBanner, vm.StaleOverrideBannerText);
        Assert.False(vm.ShowBlockedPanel);
        Assert.False(vm.ShowWarningBanner);
        Assert.Equal("לא עדכני", vm.FreshnessStatusText);
        Assert.NotEmpty(vm.Rows);
    }

    [Fact]
    public async Task Reload_resets_override_and_blocks_again()
    {
        var fake = new SwitchableStaleFake();
        var vm = new BillingDashboardViewModel(fake, staleReplicaOverrideAvailable: true);
        await vm.LoadAsync().ConfigureAwait(true);
        await vm.CheckAnywayAsync().ConfigureAwait(true);
        Assert.True(vm.ShowCandidatesArea);

        await vm.RefreshAsync().ConfigureAwait(true);

        Assert.Equal(3, fake.CallCount);
        Assert.False(fake.LastRequest!.AllowStaleReplicaForCurrentCheck);
        Assert.Equal(BillingDashboardUiState.FreshnessBlocked, vm.UiState);
        Assert.True(vm.ShowBlockedPanel);
        Assert.True(vm.ShowCheckAnywayButton);
        Assert.False(vm.ShowStaleOverrideBanner);
        Assert.False(vm.ShowCandidatesArea);
    }

    [Fact]
    public async Task Stale_replica_production_keeps_button_hidden_and_stays_blocked()
    {
        var fake = new SwitchableStaleFake();
        var vm = new BillingDashboardViewModel(fake, staleReplicaOverrideAvailable: false);

        await vm.LoadAsync().ConfigureAwait(true);
        Assert.True(vm.ShowBlockedPanel);
        Assert.False(vm.ShowCheckAnywayButton);

        await vm.CheckAnywayAsync().ConfigureAwait(true);

        Assert.Equal(1, fake.CallCount);
        Assert.False(fake.LastRequest!.AllowStaleReplicaForCurrentCheck);
        Assert.Equal(BillingDashboardUiState.FreshnessBlocked, vm.UiState);
        Assert.False(vm.ShowCandidatesArea);
    }

    [Fact]
    public async Task Service_override_loads_age_stale_without_changing_freshness_or_sync_stamps()
    {
        var replica = GuardReplica.Fresh(UtcNow.AddHours(-80));
        var before = replica.Probe.Diagnostics;
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock);

        var blocked = await service.GetAsync(new BillingDashboardRequest(AsOfDate: LocalToday));
        Assert.True(blocked.CandidatesBlocked);
        Assert.Equal(0, replica.LoadFactsCalls);

        var loaded = await service.GetAsync(new BillingDashboardRequest(
            AsOfDate: LocalToday,
            AllowStaleReplicaForCurrentCheck: true));

        Assert.False(loaded.CandidatesBlocked);
        Assert.Equal(BillingReplicaFreshnessStatus.Stale, loaded.FreshnessStatus);
        Assert.NotEmpty(loaded.Candidates);
        Assert.Equal(1, replica.LoadFactsCalls);
        Assert.Equal(before.ProjectsSyncTime, loaded.Diagnostics.ProjectsSyncTime);
        Assert.Equal(before.BillsSyncTime, loaded.Diagnostics.BillsSyncTime);
        Assert.Equal(before.ToSourceFreshness().ReplicaLastSyncTime, loaded.Freshness.ReplicaLastSyncTime);
        Assert.Contains(loaded.Warnings, w => w.Code == BillingDataQualityWarningCodes.ReplicaFreshnessFatal);
        Assert.Equal(before.ProjectsSyncTime, replica.Probe.Diagnostics.ProjectsSyncTime);
    }

    [Fact]
    public async Task Service_override_does_not_bypass_structural_fatal()
    {
        var replica = GuardReplica.Fresh(UtcNow.AddHours(-1));
        replica.Probe = replica.Probe with { MissingRequiredTables = ["MP_ProjectHoursExtended"] };
        var service = new SqlBillingDashboardReadService(replica, timeProvider: Clock);

        var result = await service.GetAsync(new BillingDashboardRequest(
            AsOfDate: LocalToday,
            AllowStaleReplicaForCurrentCheck: true));

        Assert.True(result.CandidatesBlocked);
        Assert.Equal(0, replica.LoadFactsCalls);
        Assert.Contains(result.Warnings, w => w.Code == BillingDataQualityWarningCodes.ReplicaRequiredTableMissing);
    }

    [Fact]
    public void Age_block_bypass_helper_rejects_structural_and_missing_token()
    {
        var ageStale = BillingReplicaFreshnessEvaluator.Evaluate(
            [],
            true,
            BillingReplicaFreshnessEvaluatorTests.FreshSyncTimes(UtcNow.AddHours(-80)),
            UtcNow,
            LocalToday,
            LocalToday);

        Assert.True(ageStale.CandidatesBlocked);
        Assert.False(BillingReplicaFreshnessOverride.CanBypassAgeBlock(ageStale, allowStaleReplicaForCurrentCheck: false));
        Assert.True(BillingReplicaFreshnessOverride.CanBypassAgeBlock(ageStale, allowStaleReplicaForCurrentCheck: true));

        var missingTable = BillingReplicaFreshnessEvaluator.Evaluate(
            ["MP_Bills"],
            true,
            BillingReplicaFreshnessEvaluatorTests.FreshSyncTimes(UtcNow.AddHours(-1)),
            UtcNow,
            LocalToday,
            LocalToday);
        Assert.False(BillingReplicaFreshnessOverride.CanBypassAgeBlock(missingTable, allowStaleReplicaForCurrentCheck: true));
    }

    [Fact]
    public async Task Fresh_dashboard_does_not_require_override_button()
    {
        var fake = new SwitchableStaleFake { ForceHealthy = true };
        var vm = new BillingDashboardViewModel(fake, staleReplicaOverrideAvailable: true);

        await vm.LoadAsync().ConfigureAwait(true);

        Assert.Equal(BillingDashboardUiState.Loaded, vm.UiState);
        Assert.True(vm.ShowCandidatesArea);
        Assert.False(vm.ShowCheckAnywayButton);
        Assert.False(vm.ShowBlockedPanel);
        Assert.False(vm.ShowStaleOverrideBanner);
        Assert.False(fake.LastRequest!.AllowStaleReplicaForCurrentCheck);
    }

    private sealed class SwitchableStaleFake : IBillingDashboardReadService
    {
        public int CallCount { get; private set; }
        public BillingDashboardRequest? LastRequest { get; private set; }
        public bool ForceHealthy { get; set; }

        public Task<BillingDashboardResult> GetAsync(
            BillingDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            if (ForceHealthy)
                return Task.FromResult(Healthy());
            return Task.FromResult(
                request.AllowStaleReplicaForCurrentCheck ? StaleLoaded() : StaleBlocked());
        }

        private static BillingDashboardResult StaleBlocked() =>
            new(
                new BillingDashboardSummary(0, 0, null, null, UtcNow.AddHours(-80), null),
                [],
                new BillingSourceFreshness(
                    UtcNow.AddHours(-80), UtcNow.AddHours(-80), UtcNow.AddHours(-80),
                    UtcNow.AddHours(-80), UtcNow.AddHours(-80), UtcNow.AddHours(-80),
                    UtcNow.AddHours(-80), null),
                [
                    new BillingDataQualityWarning(
                        BillingDataQualityWarningCodes.ReplicaFreshnessFatal,
                        "Replica לא מעודכן.")
                ],
                ReplicaConnectionDiagnostics.Empty,
                BillingReplicaFreshnessStatus.Stale,
                CandidatesBlocked: true);

        private static BillingDashboardResult StaleLoaded() =>
            Healthy() with
            {
                FreshnessStatus = BillingReplicaFreshnessStatus.Stale,
                CandidatesBlocked = false,
                Warnings =
                [
                    new BillingDataQualityWarning(
                        BillingDataQualityWarningCodes.ReplicaFreshnessFatal,
                        "Replica לא מעודכן.")
                ]
            };

        private static BillingDashboardResult Healthy() =>
            new(
                new BillingDashboardSummary(1, 0, null, 0m, UtcNow.AddHours(-1), null, 0, 0, 0),
                [
                    new BillingCandidateRow(
                        5905,
                        "5905",
                        "גשר",
                        "לקוח",
                        "פעיל",
                        1m,
                        UtcNow.Date,
                        8m,
                        8m,
                        8m,
                        8m,
                        1,
                        UtcNow.Date,
                        1,
                        11,
                        "B-11",
                        2,
                        "פתוח",
                        10m,
                        0,
                        0,
                        0,
                        0,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        BillingCandidateState.ReviewNow,
                        "שעות")
                ],
                new BillingSourceFreshness(
                    UtcNow.AddHours(-1), UtcNow.AddHours(-1), UtcNow.AddHours(-1),
                    UtcNow.AddHours(-1), UtcNow.AddHours(-1), UtcNow.AddHours(-1),
                    UtcNow.AddHours(-1), null),
                [],
                ReplicaConnectionDiagnostics.Empty,
                BillingReplicaFreshnessStatus.Healthy,
                CandidatesBlocked: false);
    }

    private sealed class FrozenUtcTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class GuardReplica : IReplicaBillingDataSource
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

        public static GuardReplica Fresh(DateTime syncTime)
        {
            var times = BillingReplicaFreshnessEvaluatorTests.FreshSyncTimes(syncTime);
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

            return new GuardReplica
            {
                Probe = new ReplicaBillingProbe(
                    diagnostics,
                    MissingRequiredTables: Array.Empty<string>(),
                    SyncStateTablePresent: true,
                    SyncTimesByEntity: times),
                Facts = new ReplicaBillingSnapshot(
                    [new BillingProjectFact(5905, "5905", "Test", "Customer", 1, "Active", true, 1m)],
                    Array.Empty<BillingBillFact>(),
                    [new BillingHourFact(5905, LocalToday.AddDays(-2), 8m)],
                    ReceivedThisMonth: 10m,
                    HoursOnlyInBasicCount: 0)
            };
        }
    }
}
