using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// REV certification helper mirroring production
/// <c>InspectionReportTaskLinkService</c> + work-target completion via
/// <see cref="SiNet.Application.Tasks.CompleteTaskCommand.CompletedTaskLinkIds"/>.
/// </summary>
internal static class SystemCertificationRevInspectionProof
{
    private static readonly HashSet<string> InspectionReportTaskTypes = new(StringComparer.Ordinal)
    {
        TaskTypeCodes.PerformProfessionalReview,
        TaskTypeCodes.FixReportPerManager,
        TaskTypeCodes.RecheckPlan,
        TaskTypeCodes.ApproveReviewReport,
        TaskTypeCodes.ResubmitToManager,
    };

    internal static async Task<int> EnsureSharedCertReportAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int certProjectId,
        int operatorUserId,
        int? existingReportId,
        CancellationToken cancellationToken = default)
    {
        if (existingReportId is > 0)
        {
            return existingReportId.Value;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var nextNumber = await db.InspectionReports
            .Where(r => r.ProjectId == certProjectId && r.SeriesId == null)
            .Select(r => (int?)r.ReportNumber)
            .MaxAsync(cancellationToken) ?? 0;

        var report = new InspectionReport
        {
            ProjectId = certProjectId,
            ReportNumber = nextNumber + 1,
            InspectionDate = DateTime.UtcNow,
            InspectorId = operatorUserId,
            ReviewedVersion = "1",
        };

        db.InspectionReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);
        return report.ReportId;
    }

    internal static async Task<(int SharedReportId, IReadOnlyList<int> CompletedLinkIds)> ResolveCompletedWorkTargetLinkIdsAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int taskId,
        string taskTypeCode,
        int certProjectId,
        int operatorUserId,
        int sharedReportId,
        CancellationToken cancellationToken = default)
    {
        if (InspectionReportTaskTypes.Contains(taskTypeCode))
        {
            sharedReportId = await EnsureSharedCertReportAsync(
                dbFactory,
                certProjectId,
                operatorUserId,
                sharedReportId > 0 ? sharedReportId : null,
                cancellationToken);
            await EnsureReportWorkTargetLinkAsync(
                dbFactory,
                taskId,
                sharedReportId,
                operatorUserId,
                cancellationToken);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var linkIds = await db.TaskLinks.AsNoTracking()
            .Where(l => l.TaskId == taskId
                        && l.IsWorkTarget
                        && l.WorkStatus != WorkTargetStatus.Done
                        && l.WorkStatus != WorkTargetStatus.Skipped)
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);
        return (sharedReportId, linkIds);
    }

    private static async Task EnsureReportWorkTargetLinkAsync(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        int taskId,
        int reportId,
        int userId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var candidates = await db.TaskLinks
            .Where(l =>
                l.TaskId == taskId
                && l.LinkedEntityType == TaskLinkEntityType.InspectionReport
                && l.LinkedEntityId == reportId)
            .ToListAsync(cancellationToken);

        var existing = candidates.FirstOrDefault(l => l.Role == TaskLinkRole.Related)
                       ?? candidates.FirstOrDefault();

        if (existing is not null)
        {
            var changed = false;
            if (existing.Role != TaskLinkRole.Related)
            {
                existing.Role = TaskLinkRole.Related;
                changed = true;
            }

            if (!existing.IsWorkTarget)
            {
                existing.IsWorkTarget = true;
                existing.WorkStatus = WorkTargetStatus.Pending;
                changed = true;
            }

            if (changed)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        db.TaskLinks.Add(new TaskLink
        {
            TaskId = taskId,
            LinkedEntityType = TaskLinkEntityType.InspectionReport,
            LinkedEntityId = reportId,
            Role = TaskLinkRole.Related,
            IsWorkTarget = true,
            WorkStatus = WorkTargetStatus.Pending,
            Description = $"[SYS-CERT] inspection report work target #{reportId}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
