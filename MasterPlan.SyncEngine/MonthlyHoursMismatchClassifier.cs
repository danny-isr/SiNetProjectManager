using System.Globalization;

namespace MasterPlan.SyncEngine;

public enum MonthlyHoursComparePhase
{
    PreDrop,
    PostEtl
}

public sealed class SourceHoursCompareRow
{
    public int Id { get; set; }
    public DateTime? ReportDate { get; set; }
    public int? ProjectId { get; set; }
    public int? SubContractId { get; set; }
    public int? EmployeeId { get; set; }
    public double? RawMilliseconds { get; set; }
}

public sealed class ReplicaHoursCompareRow
{
    public int Id { get; set; }
    public DateTime? ReportDate { get; set; }
    public int? ProjectId { get; set; }
    public int? SubContractId { get; set; }
    public int? EmployeeId { get; set; }
    public decimal? Duration { get; set; }
    public DateTime? LastUpdated { get; set; }
}

public sealed record MonthlyHoursMismatch(
    int Id,
    string ClassName,
    string CauseCode,
    string Evidence);

public sealed class MonthlyHoursCompareSummary
{
    public int SourceCount { get; init; }
    public int ReplicaCount { get; init; }
    public int IdenticalCount { get; init; }
    public int DifferingCount { get; init; }
    public int BakOnlyCount { get; init; }
    public int ReplicaOnlyCount { get; init; }
    public int ZeroDurationCount { get; init; }
    public bool SkippedNoReplicaTable { get; init; }
    public bool AllReplicaLastUpdatedEqualsBackup { get; init; }
    public IReadOnlyList<MonthlyHoursMismatch> Mismatches { get; init; } = [];
    public IReadOnlyDictionary<string, int> CauseCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
}

/// <summary>
/// In-memory classify of restored <c>HoursReports</c> vs replica <c>MP_ProjectHoursExtended</c> by ID.
/// Cause codes match docs/DEV_PLAN_MASTERPLAN_MONTHLY_CAPTURE.md (July 2026 findings).
/// </summary>
public static class MonthlyHoursMismatchClassifier
{
    public const string ClassBakOnly = "BAK_ONLY";
    public const string ClassReplicaOnly = "REPLICA_ONLY";
    public const string ClassBothDiffering = "BOTH_DIFFERING";
    public const string ClassBothIdentical = "BOTH_IDENTICAL";

    public const string CauseAbsentReplica = "ABSENT_REPLICA";
    public const string CauseOrphanReplica = "ORPHAN_REPLICA";
    public const string CauseWatermarkLookbackGap = "WATERMARK_LOOKBACK_GAP";
    public const string CauseHoursUnitNull = "HOURS_UNIT_NULL";
    public const string CauseNullDurationZeroed = "NULL_DURATION_ZEROED";
    public const string CauseFieldDiff = "FIELD_DIFF";
    public const string CauseEtlRowcountMismatch = "ETL_ROWCOUNT_MISMATCH";
    public const string CauseEtlLastUpdatedSkip = "ETL_LASTUPDATED_SKIP";

    /// <summary>Milliseconds in a calendar day. Source Hours above this cannot be a valid daily duration.</summary>
    public const double MaxDailyMilliseconds = 24d * 3_600_000d;

    public static MonthlyHoursCompareSummary Classify(
        IReadOnlyList<SourceHoursCompareRow> source,
        IReadOnlyList<ReplicaHoursCompareRow> replica,
        MonthlyHoursComparePhase phase,
        DateTime? dailyFromDate,
        DateTime? backupFinishDate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replica);

        var sourceById = new Dictionary<int, SourceHoursCompareRow>();
        foreach (var row in source)
        {
            sourceById.TryAdd(row.Id, row);
        }

        var replicaById = new Dictionary<int, ReplicaHoursCompareRow>();
        foreach (var row in replica)
        {
            replicaById.TryAdd(row.Id, row);
        }

        var mismatches = new List<MonthlyHoursMismatch>();
        var causeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var identical = 0;
        var differing = 0;
        var bakOnly = 0;
        var replicaOnly = 0;
        var zeroDuration = 0;

        var ids = sourceById.Keys.Union(replicaById.Keys).OrderBy(id => id);
        foreach (var id in ids)
        {
            var hasSource = sourceById.TryGetValue(id, out var src);
            var hasReplica = replicaById.TryGetValue(id, out var rep);

            if (hasSource && !hasReplica)
            {
                bakOnly++;
                var cause = ClassifyBakOnly(src!, phase, dailyFromDate);
                AddMismatch(mismatches, causeCounts, id, ClassBakOnly, cause, BakOnlyEvidence(src!, dailyFromDate, phase));
                continue;
            }

            if (!hasSource && hasReplica)
            {
                replicaOnly++;
                var cause = phase == MonthlyHoursComparePhase.PostEtl
                    ? CauseEtlRowcountMismatch
                    : CauseOrphanReplica;
                AddMismatch(
                    mismatches,
                    causeCounts,
                    id,
                    ClassReplicaOnly,
                    cause,
                    $"ReportDate={FormatDate(rep!.ReportDate)}; Duration={FormatDecimal(rep.Duration)}");
                continue;
            }

            if (rep!.Duration == 0m)
            {
                zeroDuration++;
            }

            var (isDiff, causeCode, evidence) = ClassifyBoth(src!, rep, phase);
            if (isDiff)
            {
                differing++;
                AddMismatch(mismatches, causeCounts, id, ClassBothDiffering, causeCode, evidence);
            }
            else
            {
                identical++;
            }
        }

        var allStampMatch = replica.Count > 0
            && backupFinishDate.HasValue
            && replica.All(r => r.LastUpdated.HasValue && SameSecond(r.LastUpdated.Value, backupFinishDate.Value));

        if (allStampMatch && phase == MonthlyHoursComparePhase.PostEtl)
        {
            causeCounts[CauseEtlLastUpdatedSkip] = replica.Count;
        }

        return new MonthlyHoursCompareSummary
        {
            SourceCount = sourceById.Count,
            ReplicaCount = replicaById.Count,
            IdenticalCount = identical,
            DifferingCount = differing,
            BakOnlyCount = bakOnly,
            ReplicaOnlyCount = replicaOnly,
            ZeroDurationCount = zeroDuration,
            AllReplicaLastUpdatedEqualsBackup = allStampMatch,
            Mismatches = mismatches,
            CauseCounts = causeCounts
        };
    }

    private static string ClassifyBakOnly(
        SourceHoursCompareRow source,
        MonthlyHoursComparePhase phase,
        DateTime? dailyFromDate)
    {
        if (phase == MonthlyHoursComparePhase.PostEtl)
        {
            return CauseEtlRowcountMismatch;
        }

        if (dailyFromDate.HasValue
            && source.ReportDate.HasValue
            && source.ReportDate.Value.Date < dailyFromDate.Value.Date)
        {
            return CauseWatermarkLookbackGap;
        }

        return CauseAbsentReplica;
    }

    private static (bool IsDiff, string Cause, string Evidence) ClassifyBoth(
        SourceHoursCompareRow source,
        ReplicaHoursCompareRow replica,
        MonthlyHoursComparePhase phase)
    {
        var expectedDuration = HoursNormalization.MillisecondsToDecimalHours(source.RawMilliseconds);

        if (IsHoursUnitNull(source.RawMilliseconds, replica.Duration, expectedDuration))
        {
            return (true, CauseHoursUnitNull,
                $"Duration=null; RawMilliseconds={FormatDouble(source.RawMilliseconds)}; expected=null (>{MaxDailyMilliseconds:0} ms/day)");
        }

        if (replica.Duration == 0m && expectedDuration != 0m)
        {
            return (true, CauseNullDurationZeroed,
                $"Duration=0; expected={FormatDecimal(expectedDuration)}; RawMilliseconds={FormatDouble(source.RawMilliseconds)}");
        }

        var diffs = new List<string>();
        if (!SameDate(source.ReportDate, replica.ReportDate))
        {
            diffs.Add($"ReportDate {FormatDate(source.ReportDate)} vs {FormatDate(replica.ReportDate)}");
        }

        if (source.ProjectId != replica.ProjectId)
        {
            diffs.Add($"ProjectId {source.ProjectId} vs {replica.ProjectId}");
        }

        if (source.SubContractId != replica.SubContractId)
        {
            diffs.Add($"SubContractId {source.SubContractId} vs {replica.SubContractId}");
        }

        if (source.EmployeeId != replica.EmployeeId)
        {
            diffs.Add($"EmployeeId {source.EmployeeId} vs {replica.EmployeeId}");
        }

        if (!DurationMatches(expectedDuration, replica.Duration))
        {
            diffs.Add($"Duration expected={FormatDecimal(expectedDuration)} replica={FormatDecimal(replica.Duration)}");
        }

        if (diffs.Count == 0)
        {
            return (false, string.Empty, string.Empty);
        }

        _ = phase;
        return (true, CauseFieldDiff, string.Join("; ", diffs));
    }

    internal static bool IsHoursUnitNull(double? rawMilliseconds, decimal? replicaDuration, decimal? expectedDuration)
    {
        if (replicaDuration.HasValue)
        {
            return false;
        }

        if (!rawMilliseconds.HasValue)
        {
            return false;
        }

        return !expectedDuration.HasValue && rawMilliseconds.Value > MaxDailyMilliseconds;
    }

    /// <summary>
    /// null ≠ 0. When source Hours cannot convert (null expected) but replica already has a Duration
    /// (ETL Start/End fallback), duration is not treated as a field diff.
    /// </summary>
    public static bool DurationMatches(decimal? expected, decimal? replica)
    {
        if (expected is null && replica.HasValue)
        {
            return true;
        }

        if (expected is null && replica is null)
        {
            return true;
        }

        if (expected.HasValue && replica.HasValue)
        {
            return expected.Value == replica.Value;
        }

        return false;
    }

    private static void AddMismatch(
        List<MonthlyHoursMismatch> mismatches,
        Dictionary<string, int> causeCounts,
        int id,
        string className,
        string cause,
        string evidence)
    {
        mismatches.Add(new MonthlyHoursMismatch(id, className, cause, evidence));
        causeCounts[cause] = causeCounts.TryGetValue(cause, out var n) ? n + 1 : 1;
    }

    private static string BakOnlyEvidence(
        SourceHoursCompareRow source,
        DateTime? dailyFromDate,
        MonthlyHoursComparePhase phase)
        => $"ReportDate={FormatDate(source.ReportDate)}; FromDate={FormatDate(dailyFromDate)}; RawMilliseconds={FormatDouble(source.RawMilliseconds)}; phase={phase}";

    private static bool SameDate(DateTime? left, DateTime? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Value.Date == right.Value.Date;
    }

    private static bool SameSecond(DateTime left, DateTime right)
        => new DateTime(left.Year, left.Month, left.Day, left.Hour, left.Minute, left.Second, DateTimeKind.Unspecified)
           == new DateTime(right.Year, right.Month, right.Day, right.Hour, right.Minute, right.Second, DateTimeKind.Unspecified);

    private static string FormatDate(DateTime? value)
        => value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "(null)";

    private static string FormatDecimal(decimal? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "(null)";

    private static string FormatDouble(double? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "(null)";
}
