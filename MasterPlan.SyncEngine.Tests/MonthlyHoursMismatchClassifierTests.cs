using MasterPlan.SyncEngine;
using Xunit;

namespace MasterPlan.SyncEngine.Tests;

public sealed class MonthlyHoursMismatchClassifierTests
{
    [Fact]
    public void When_rows_match_then_identical()
    {
        var source = new List<SourceHoursCompareRow>
        {
            new()
            {
                Id = 1,
                ReportDate = new DateTime(2026, 7, 15),
                ProjectId = 10,
                SubContractId = 20,
                EmployeeId = 30,
                RawMinutes = 60
            }
        };
        var replica = new List<ReplicaHoursCompareRow>
        {
            new()
            {
                Id = 1,
                ReportDate = new DateTime(2026, 7, 15),
                ProjectId = 10,
                SubContractId = 20,
                EmployeeId = 30,
                Duration = 1m,
                LastUpdated = new DateTime(2026, 8, 1)
            }
        };

        var summary = MonthlyHoursMismatchClassifier.Classify(
            source,
            replica,
            MonthlyHoursComparePhase.PreDrop,
            dailyFromDate: new DateTime(2026, 7, 1),
            backupFinishDate: new DateTime(2026, 8, 1));

        Assert.Equal(1, summary.IdenticalCount);
        Assert.Empty(summary.Mismatches);
    }

    [Fact]
    public void When_source_only_and_report_before_fromdate_then_watermark_lookback_gap()
    {
        var source = new List<SourceHoursCompareRow>
        {
            new()
            {
                Id = 39,
                ReportDate = new DateTime(2026, 7, 2),
                RawMinutes = 30
            }
        };

        var summary = MonthlyHoursMismatchClassifier.Classify(
            source,
            replica: [],
            MonthlyHoursComparePhase.PreDrop,
            dailyFromDate: new DateTime(2026, 7, 18),
            backupFinishDate: new DateTime(2026, 8, 1));

        Assert.Equal(1, summary.BakOnlyCount);
        Assert.Equal(
            MonthlyHoursMismatchClassifier.CauseWatermarkLookbackGap,
            summary.Mismatches[0].CauseCode);
    }

    [Fact]
    public void When_source_only_inside_window_then_absent_replica()
    {
        var source = new List<SourceHoursCompareRow>
        {
            new()
            {
                Id = 40,
                ReportDate = new DateTime(2026, 7, 20),
                RawMinutes = 30
            }
        };

        var summary = MonthlyHoursMismatchClassifier.Classify(
            source,
            replica: [],
            MonthlyHoursComparePhase.PreDrop,
            dailyFromDate: new DateTime(2026, 7, 18),
            backupFinishDate: new DateTime(2026, 8, 1));

        Assert.Equal(
            MonthlyHoursMismatchClassifier.CauseAbsentReplica,
            summary.Mismatches[0].CauseCode);
    }

    [Fact]
    public void When_replica_only_pre_drop_then_orphan()
    {
        var replica = new List<ReplicaHoursCompareRow>
        {
            new() { Id = 99, ReportDate = new DateTime(2026, 7, 25), Duration = 1m }
        };

        var summary = MonthlyHoursMismatchClassifier.Classify(
            source: [],
            replica,
            MonthlyHoursComparePhase.PreDrop,
            dailyFromDate: new DateTime(2026, 7, 1),
            backupFinishDate: new DateTime(2026, 8, 1));

        Assert.Equal(
            MonthlyHoursMismatchClassifier.CauseOrphanReplica,
            summary.Mismatches[0].CauseCode);
    }

    [Fact]
    public void When_replica_only_post_etl_then_etl_rowcount_mismatch()
    {
        var replica = new List<ReplicaHoursCompareRow>
        {
            new() { Id = 99, Duration = 1m }
        };

        var summary = MonthlyHoursMismatchClassifier.Classify(
            source: [],
            replica,
            MonthlyHoursComparePhase.PostEtl,
            dailyFromDate: new DateTime(2026, 7, 1),
            backupFinishDate: new DateTime(2026, 8, 1));

        Assert.Equal(
            MonthlyHoursMismatchClassifier.CauseEtlRowcountMismatch,
            summary.Mismatches[0].CauseCode);
    }

    [Fact]
    public void When_raw_minutes_exceed_day_and_duration_null_then_hours_unit_null()
    {
        var source = new List<SourceHoursCompareRow>
        {
            new() { Id = 7, ReportDate = new DateTime(2026, 7, 10), RawMinutes = 200_000 }
        };
        var replica = new List<ReplicaHoursCompareRow>
        {
            new()
            {
                Id = 7,
                ReportDate = new DateTime(2026, 7, 10),
                Duration = null
            }
        };

        var summary = MonthlyHoursMismatchClassifier.Classify(
            source,
            replica,
            MonthlyHoursComparePhase.PreDrop,
            dailyFromDate: new DateTime(2026, 7, 1),
            backupFinishDate: new DateTime(2026, 8, 1));

        Assert.Equal(1, summary.DifferingCount);
        Assert.Equal(
            MonthlyHoursMismatchClassifier.CauseHoursUnitNull,
            summary.Mismatches[0].CauseCode);
    }

    [Fact]
    public void When_duration_zero_but_expected_nonzero_then_null_duration_zeroed()
    {
        var source = new List<SourceHoursCompareRow>
        {
            new() { Id = 8, ReportDate = new DateTime(2026, 7, 10), RawMinutes = 60 }
        };
        var replica = new List<ReplicaHoursCompareRow>
        {
            new()
            {
                Id = 8,
                ReportDate = new DateTime(2026, 7, 10),
                Duration = 0m
            }
        };

        var summary = MonthlyHoursMismatchClassifier.Classify(
            source,
            replica,
            MonthlyHoursComparePhase.PreDrop,
            dailyFromDate: new DateTime(2026, 7, 1),
            backupFinishDate: new DateTime(2026, 8, 1));

        Assert.Equal(
            MonthlyHoursMismatchClassifier.CauseNullDurationZeroed,
            summary.Mismatches[0].CauseCode);
    }

    [Fact]
    public void When_project_id_differs_then_field_diff()
    {
        var source = new List<SourceHoursCompareRow>
        {
            new()
            {
                Id = 3,
                ReportDate = new DateTime(2026, 7, 10),
                ProjectId = 1,
                RawMinutes = 60
            }
        };
        var replica = new List<ReplicaHoursCompareRow>
        {
            new()
            {
                Id = 3,
                ReportDate = new DateTime(2026, 7, 10),
                ProjectId = 2,
                Duration = 1m
            }
        };

        var summary = MonthlyHoursMismatchClassifier.Classify(
            source,
            replica,
            MonthlyHoursComparePhase.PreDrop,
            dailyFromDate: new DateTime(2026, 7, 1),
            backupFinishDate: new DateTime(2026, 8, 1));

        Assert.Equal(
            MonthlyHoursMismatchClassifier.CauseFieldDiff,
            summary.Mismatches[0].CauseCode);
    }

    [Fact]
    public void When_all_lastupdated_equal_backup_post_etl_then_forward_warning_counted()
    {
        var stamp = new DateTime(2026, 8, 1, 12, 0, 0);
        var source = new List<SourceHoursCompareRow>
        {
            new() { Id = 1, ReportDate = stamp.Date, RawMinutes = 60 }
        };
        var replica = new List<ReplicaHoursCompareRow>
        {
            new()
            {
                Id = 1,
                ReportDate = stamp.Date,
                Duration = 1m,
                LastUpdated = stamp
            }
        };

        var summary = MonthlyHoursMismatchClassifier.Classify(
            source,
            replica,
            MonthlyHoursComparePhase.PostEtl,
            dailyFromDate: stamp.Date.AddDays(-14),
            backupFinishDate: stamp);

        Assert.True(summary.AllReplicaLastUpdatedEqualsBackup);
        Assert.True(summary.CauseCounts.ContainsKey(
            MonthlyHoursMismatchClassifier.CauseEtlLastUpdatedSkip));
    }

    [Fact]
    public void DurationMatches_treats_null_expected_with_replica_value_as_match()
    {
        Assert.True(MonthlyHoursMismatchClassifier.DurationMatches(null, 1.5m));
        Assert.False(MonthlyHoursMismatchClassifier.DurationMatches(1m, null));
        Assert.True(MonthlyHoursMismatchClassifier.DurationMatches(null, null));
        Assert.True(MonthlyHoursMismatchClassifier.DurationMatches(1m, 1m));
        Assert.False(MonthlyHoursMismatchClassifier.DurationMatches(1m, 0m));
    }
}
