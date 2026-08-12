using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace MasterPlan.SyncEngine;

/// <summary>
/// SELECT-only compare of restored HoursReports vs replica MP_ProjectHoursExtended, then log
/// to existing SyncEngine sinks (Warning so the central share records the Hebrew summary).
/// </summary>
public sealed class MonthlyHoursCompareRunner
{
    public const int MaxLoggedMismatches = 100;

    private readonly ILogger _logger;

    public MonthlyHoursCompareRunner(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task<MonthlyHoursCompareSummary> RunAsync(
        SqlConnection source,
        SqlConnection replica,
        MonthlyHoursComparePhase phase,
        DateTime? dailyFromDate,
        DateTime? backupFinishDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replica);
        cancellationToken.ThrowIfCancellationRequested();

        var replicaTableId = await replica.ExecuteScalarAsync<int?>(
            "SELECT OBJECT_ID(N'dbo.MP_ProjectHoursExtended', N'U')");
        if (replicaTableId is null)
        {
            _logger.LogWarning(
                "שחזור חודשי [{Phase}]: אין טבלת MP_ProjectHoursExtended ברפליקה — דילוג על השוואת שעות (ריצה ראשונה?).",
                phase);
            Console.WriteLine($"    [COMPARE {phase}] skipped — no MP_ProjectHoursExtended");
            return new MonthlyHoursCompareSummary { SkippedNoReplicaTable = true };
        }

        var sourceRows = (await source.QueryAsync<SourceHoursCompareRow>(@"
            SELECT
                hr.ID AS Id,
                hr.[DateTime] AS ReportDate,
                hr.ProjectID AS ProjectId,
                hr.SubContractID AS SubContractId,
                hr.EmployeeID AS EmployeeId,
                CAST(hr.Hours AS FLOAT) AS RawMinutes
            FROM HoursReports hr WITH (NOLOCK)
            WHERE hr.ID IS NOT NULL")).AsList();

        var replicaRows = (await replica.QueryAsync<ReplicaHoursCompareRow>(@"
            SELECT
                ID AS Id,
                ReportDate,
                ProjectID AS ProjectId,
                SubContractID AS SubContractId,
                EmployeeID AS EmployeeId,
                Duration,
                LastUpdated
            FROM MP_ProjectHoursExtended WITH (NOLOCK)")).AsList();

        var summary = MonthlyHoursMismatchClassifier.Classify(
            sourceRows,
            replicaRows,
            phase,
            dailyFromDate,
            backupFinishDate);

        LogSummary(phase, summary, dailyFromDate, backupFinishDate);
        return summary;
    }

    private void LogSummary(
        MonthlyHoursComparePhase phase,
        MonthlyHoursCompareSummary summary,
        DateTime? dailyFromDate,
        DateTime? backupFinishDate)
    {
        var causes = summary.CauseCounts.Count == 0
            ? "אין"
            : string.Join(", ", summary.CauseCounts.Select(kv => $"{kv.Key}={kv.Value}"));

        var aligned = summary.BakOnlyCount == 0
            && summary.ReplicaOnlyCount == 0
            && summary.DifferingCount == 0;

        var hebrew = aligned
            ? "הכול תואם"
            : "נמצאו אי-התאמות — לפירוט ראו שורות mismatch למטה";

        _logger.LogWarning(
            "שחזור חודשי השוואת שעות [{Phase}]: {Hebrew}. מקור={SourceCount}, רפליקה={ReplicaCount}, זהים={Identical}, שונים={Differing}, רק-גיבוי={BakOnly}, רק-רפליקה={ReplicaOnly}, Duration=0={ZeroDuration}. FromDate={FromDate:yyyy-MM-dd}, BackupFinishDate={BackupFinishDate:yyyy-MM-dd HH:mm:ss}. סיבות: {Causes}",
            phase,
            hebrew,
            summary.SourceCount,
            summary.ReplicaCount,
            summary.IdenticalCount,
            summary.DifferingCount,
            summary.BakOnlyCount,
            summary.ReplicaOnlyCount,
            summary.ZeroDurationCount,
            dailyFromDate,
            backupFinishDate,
            causes);

        Console.WriteLine($"    [COMPARE {phase}] {hebrew}");
        Console.WriteLine(
            $"        source={summary.SourceCount} replica={summary.ReplicaCount} identical={summary.IdenticalCount} differing={summary.DifferingCount} bakOnly={summary.BakOnlyCount} replicaOnly={summary.ReplicaOnlyCount}");
        Console.WriteLine($"        causes: {causes}");

        if (summary.AllReplicaLastUpdatedEqualsBackup && phase == MonthlyHoursComparePhase.PostEtl)
        {
            _logger.LogWarning(
                "שחזור חודשי [{Phase}] {Cause}: כל שורות הרפליקה עם LastUpdated=BackupFinishDate. סנכרון יומי (MERGE) ידלג על רשומות API עם LastUpdated קטן או שווה לחותמת הזו.",
                phase,
                MonthlyHoursMismatchClassifier.CauseEtlLastUpdatedSkip);
        }

        var logged = 0;
        foreach (var mismatch in summary.Mismatches)
        {
            if (logged >= MaxLoggedMismatches)
            {
                var remaining = summary.Mismatches.Count - MaxLoggedMismatches;
                _logger.LogWarning(
                    "שחזור חודשי [{Phase}]: ועוד {Remaining} אי-התאמות שלא נרשמו (תקרה {Cap}).",
                    phase,
                    remaining,
                    MaxLoggedMismatches);
                Console.WriteLine($"        ... and {remaining} more mismatches (cap {MaxLoggedMismatches})");
                break;
            }

            _logger.LogWarning(
                "שחזור חודשי mismatch [{Phase}] ID={Id} Class={Class} Cause={Cause} {Evidence}",
                phase,
                mismatch.Id,
                mismatch.ClassName,
                mismatch.CauseCode,
                mismatch.Evidence);
            logged++;
        }
    }
}
