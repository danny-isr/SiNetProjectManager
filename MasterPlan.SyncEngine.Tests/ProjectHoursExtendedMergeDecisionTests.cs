using MasterPlan.SyncEngine;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class ProjectHoursExtendedMergeDecisionTests
{
    private static readonly DateTime Older = new(2026, 7, 1, 10, 0, 0);
    private static readonly DateTime Newer = new(2026, 8, 1, 10, 0, 0);

    [Fact]
    public void When_target_older_and_api_newer_then_update()
    {
        Assert.True(ProjectHoursExtendedMergeDecision.ShouldUpdate(
            targetLastUpdated: Older,
            sourceLastUpdated: Newer,
            targetDurationIsNull: false,
            sourceDurationHasValue: true,
            targetTotalHoursIsNull: false,
            sourceTotalHoursHasValue: true));
    }

    [Fact]
    public void When_target_newer_and_valid_then_skip()
    {
        Assert.False(ProjectHoursExtendedMergeDecision.ShouldUpdate(
            targetLastUpdated: Newer,
            sourceLastUpdated: Older,
            targetDurationIsNull: false,
            sourceDurationHasValue: true,
            targetTotalHoursIsNull: false,
            sourceTotalHoursHasValue: true));
    }

    [Fact]
    public void When_target_duration_null_and_api_older_with_duration_then_repair()
    {
        Assert.True(ProjectHoursExtendedMergeDecision.ShouldUpdate(
            targetLastUpdated: Newer,
            sourceLastUpdated: Older,
            targetDurationIsNull: true,
            sourceDurationHasValue: true,
            targetTotalHoursIsNull: false,
            sourceTotalHoursHasValue: true));
    }

    [Fact]
    public void When_target_duration_null_and_api_lastupdated_null_with_duration_then_repair()
    {
        Assert.True(ProjectHoursExtendedMergeDecision.ShouldUpdate(
            targetLastUpdated: Newer,
            sourceLastUpdated: null,
            targetDurationIsNull: true,
            sourceDurationHasValue: true,
            targetTotalHoursIsNull: false,
            sourceTotalHoursHasValue: false));
    }

    [Fact]
    public void When_target_valid_and_api_older_with_null_duration_then_skip()
    {
        Assert.False(ProjectHoursExtendedMergeDecision.ShouldUpdate(
            targetLastUpdated: Newer,
            sourceLastUpdated: Older,
            targetDurationIsNull: false,
            sourceDurationHasValue: false,
            targetTotalHoursIsNull: false,
            sourceTotalHoursHasValue: false));
    }

    [Fact]
    public void When_both_sides_identical_timestamps_and_values_then_skip()
    {
        Assert.False(ProjectHoursExtendedMergeDecision.ShouldUpdate(
            targetLastUpdated: Newer,
            sourceLastUpdated: Newer,
            targetDurationIsNull: false,
            sourceDurationHasValue: true,
            targetTotalHoursIsNull: false,
            sourceTotalHoursHasValue: true));
    }

    [Fact]
    public void When_target_totalhours_null_and_api_has_value_then_repair()
    {
        Assert.True(ProjectHoursExtendedMergeDecision.ShouldUpdate(
            targetLastUpdated: Newer,
            sourceLastUpdated: Older,
            targetDurationIsNull: false,
            sourceDurationHasValue: true,
            targetTotalHoursIsNull: true,
            sourceTotalHoursHasValue: true));
    }

    [Fact]
    public void CoalescePreserve_api_null_does_not_wipe_good_replica_values()
    {
        var (duration, totalHours, lastUpdated) = ProjectHoursExtendedMergeDecision.CoalescePreserve(
            sourceDuration: null,
            targetDuration: 2.0m,
            sourceTotalHours: null,
            targetTotalHours: TimeSpan.FromHours(2),
            sourceLastUpdated: null,
            targetLastUpdated: Newer);

        Assert.Equal(2.0m, duration);
        Assert.Equal(TimeSpan.FromHours(2), totalHours);
        Assert.Equal(Newer, lastUpdated);
    }

    [Fact]
    public void CoalescePreserve_api_value_fills_null_target()
    {
        var (duration, totalHours, lastUpdated) = ProjectHoursExtendedMergeDecision.CoalescePreserve(
            sourceDuration: 1.0m,
            targetDuration: null,
            sourceTotalHours: TimeSpan.FromHours(1),
            targetTotalHours: null,
            sourceLastUpdated: Older,
            targetLastUpdated: null);

        Assert.Equal(1.0m, duration);
        Assert.Equal(TimeSpan.FromHours(1), totalHours);
        Assert.Equal(Older, lastUpdated);
    }

    [Fact]
    public void When_target_lastupdated_null_and_api_has_stamp_then_update()
    {
        Assert.True(ProjectHoursExtendedMergeDecision.ShouldUpdate(
            targetLastUpdated: null,
            sourceLastUpdated: Newer,
            targetDurationIsNull: false,
            sourceDurationHasValue: true,
            targetTotalHoursIsNull: false,
            sourceTotalHoursHasValue: true));
    }
}
