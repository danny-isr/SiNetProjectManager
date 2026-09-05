using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Inspection;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Inspection;

/// <summary>
/// Native port of SiNetSQL <c>InspectionReportTaskLinkService</c> — idempotent task→report
/// work-target linking on the existing <see cref="TaskLink"/> table.
/// </summary>
public sealed class SqlInspectionReportTaskLinkService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IInspectionReportTaskLinkService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async ValueTask<int> EnsureReportWorkTargetLinkAsync(
        int taskId,
        int reportId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (taskId <= 0)
            throw new ArgumentOutOfRangeException(nameof(taskId));
        if (reportId <= 0)
            throw new ArgumentOutOfRangeException(nameof(reportId));

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var candidates = await db.TaskLinks
            .Where(l =>
                l.TaskId == taskId
                && l.LinkedEntityType == TaskLinkEntityType.InspectionReport
                && l.LinkedEntityId == reportId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return existing.Id;
        }

        var link = new TaskLink
        {
            TaskId = taskId,
            LinkedEntityType = TaskLinkEntityType.InspectionReport,
            LinkedEntityId = reportId,
            Role = TaskLinkRole.Related,
            IsWorkTarget = true,
            WorkStatus = WorkTargetStatus.Pending,
            Description = $"יעד עבודה: דוח בדיקה #{reportId}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId,
        };

        db.TaskLinks.Add(link);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return link.Id;
    }

    public async ValueTask<int?> TryGetReportWorkTargetLinkIdAsync(
        int taskId,
        int reportId,
        CancellationToken cancellationToken = default)
    {
        if (taskId <= 0 || reportId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.TaskLinks
            .AsNoTracking()
            .Where(l =>
                l.TaskId == taskId
                && l.LinkedEntityType == TaskLinkEntityType.InspectionReport
                && l.LinkedEntityId == reportId
                && l.IsWorkTarget)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
