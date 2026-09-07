using SiNet.Application.Billing;
using SiNet.Infrastructure.Sql.Services.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingReplicaFreshnessEvaluatorTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LocalToday = new(2026, 9, 6);

    [Fact]
    public void When_all_required_sync_times_are_fresh_then_healthy()
    {
        var decision = BillingReplicaFreshnessEvaluator.Evaluate(
            Array.Empty<string>(),
            syncStateTablePresent: true,
            FreshSyncTimes(UtcNow.AddHours(-1)),
            UtcNow,
            LocalToday,
            asOfDate: LocalToday);

        Assert.Equal(BillingReplicaFreshnessStatus.Healthy, decision.Status);
        Assert.False(decision.CandidatesBlocked);
        Assert.True(decision.IsCurrentDashboardRequest);
        Assert.Equal(string.Empty, decision.Code);
    }

    [Fact]
    public void When_oldest_sync_is_40_hours_then_warning_and_not_blocked()
    {
        var decision = BillingReplicaFreshnessEvaluator.Evaluate(
            Array.Empty<string>(),
            syncStateTablePresent: true,
            FreshSyncTimes(UtcNow.AddHours(-40)),
            UtcNow,
            LocalToday,
            asOfDate: LocalToday);

        Assert.Equal(BillingReplicaFreshnessStatus.Warning, decision.Status);
        Assert.False(decision.CandidatesBlocked);
        Assert.Equal(BillingDataQualityWarningCodes.ReplicaFreshnessWarning, decision.Code);
        Assert.True(decision.OldestSyncAge > BillingReplicaFreshnessOptions.Default.WarningAfter);
        Assert.True(decision.OldestSyncAge <= BillingReplicaFreshnessOptions.Default.FatalAfter);
    }

    [Fact]
    public void When_oldest_sync_is_over_72_hours_on_current_dashboard_then_blocked()
    {
        var decision = BillingReplicaFreshnessEvaluator.Evaluate(
            Array.Empty<string>(),
            syncStateTablePresent: true,
            FreshSyncTimes(UtcNow.AddHours(-73)),
            UtcNow,
            LocalToday,
            asOfDate: LocalToday);

        Assert.Equal(BillingReplicaFreshnessStatus.Stale, decision.Status);
        Assert.True(decision.CandidatesBlocked);
        Assert.Equal(BillingDataQualityWarningCodes.ReplicaFreshnessFatal, decision.Code);
    }

    [Fact]
    public void When_oldest_sync_is_over_72_hours_on_historical_as_of_then_not_blocked_for_age()
    {
        var decision = BillingReplicaFreshnessEvaluator.Evaluate(
            Array.Empty<string>(),
            syncStateTablePresent: true,
            FreshSyncTimes(UtcNow.AddHours(-73)),
            UtcNow,
            LocalToday,
            asOfDate: LocalToday.AddDays(-30));

        Assert.Equal(BillingReplicaFreshnessStatus.Stale, decision.Status);
        Assert.False(decision.CandidatesBlocked);
        Assert.False(decision.IsCurrentDashboardRequest);
        Assert.Equal(BillingDataQualityWarningCodes.ReplicaFreshnessWarning, decision.Code);
        Assert.Contains("היסטורית", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void When_required_sync_state_entity_is_missing_then_blocked()
    {
        var times = FreshSyncTimes(UtcNow.AddHours(-1));
        times.Remove("Intakes");

        var decision = BillingReplicaFreshnessEvaluator.Evaluate(
            Array.Empty<string>(),
            syncStateTablePresent: true,
            times,
            UtcNow,
            LocalToday,
            asOfDate: LocalToday);

        Assert.Equal(BillingReplicaFreshnessStatus.Stale, decision.Status);
        Assert.True(decision.CandidatesBlocked);
        Assert.Equal(BillingDataQualityWarningCodes.ReplicaSyncStateMissing, decision.Code);
        Assert.Contains("Intakes", decision.MissingSyncStateEntities);
    }

    [Fact]
    public void When_extended_hours_table_is_missing_then_blocked()
    {
        var decision = BillingReplicaFreshnessEvaluator.Evaluate(
            ["MP_ProjectHoursExtended"],
            syncStateTablePresent: true,
            FreshSyncTimes(UtcNow.AddHours(-1)),
            UtcNow,
            LocalToday,
            asOfDate: LocalToday);

        Assert.Equal(BillingReplicaFreshnessStatus.Stale, decision.Status);
        Assert.True(decision.CandidatesBlocked);
        Assert.Equal(BillingDataQualityWarningCodes.ReplicaRequiredTableMissing, decision.Code);
        Assert.Contains("MP_ProjectHoursExtended", decision.MissingRequiredTables);
        Assert.Contains("אין נפילה", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Thresholds_come_from_options_not_literals_in_evaluator()
    {
        var options = new BillingReplicaFreshnessOptions(
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(4));

        var warning = BillingReplicaFreshnessEvaluator.Evaluate(
            Array.Empty<string>(),
            true,
            FreshSyncTimes(UtcNow.AddHours(-3)),
            UtcNow,
            LocalToday,
            LocalToday,
            options);
        Assert.Equal(BillingReplicaFreshnessStatus.Warning, warning.Status);

        var fatal = BillingReplicaFreshnessEvaluator.Evaluate(
            Array.Empty<string>(),
            true,
            FreshSyncTimes(UtcNow.AddHours(-5)),
            UtcNow,
            LocalToday,
            LocalToday,
            options);
        Assert.True(fatal.CandidatesBlocked);
    }

    internal static Dictionary<string, DateTime?> FreshSyncTimes(DateTime stamp)
    {
        var times = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in BillingReplicaRequirements.RequiredSyncStateEntities)
            times[entity] = stamp;
        return times;
    }
}
