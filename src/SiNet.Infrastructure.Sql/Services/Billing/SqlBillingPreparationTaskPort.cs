using Microsoft.EntityFrameworkCore;
using SiNet.Application.Billing;
using SiNet.Application.Identity;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Billing;

public sealed class SqlBillingPreparationTaskPort(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    ITaskWorkbenchService workbench,
    ICurrentUserContext currentUser) : IBillingPreparationTaskPort
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ITaskWorkbenchService _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
    private readonly ICurrentUserContext _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    public async Task<int> CreatePrepareBillTaskAsync(
        int siNetProjectId,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (siNetProjectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(siNetProjectId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("נדרש משתמש מחובר ליצירת משימת הכנת חשבון.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var taskType = await db.TaskTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == TaskTypeCodes.PrepareBill, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("סוג משימה PrepareBill אינו מוגדר.");

        var open = await db.ProjectAssignmentStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == TaskStatusCodes.Open, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"סטטוס '{TaskStatusCodes.Open}' אינו מוגדר.");

        var group = await db.UserGroups
            .Include(g => g.Memberships)
                .ThenInclude(m => m.Siuser)
            .FirstOrDefaultAsync(g => g.Code == UserGroupCodes.OfficeManagement, cancellationToken)
            .ConfigureAwait(false);

        var (assigneeId, _) = WorkflowStageTaskProvisioningService.TryResolveAssigneeFromGroup(group);
        if (assigneeId is not int assignedTo)
            throw new InvalidOperationException("לא ניתן לשייך את משימת הכנת החשבון לקבוצת ניהול משרד.");

        var bucket = taskType.DefaultWorkQueueBucket is int b && WorkQueueBucketCodes.IsValid(b)
            ? b
            : WorkQueueBucketCodes.Medium;

        var result = await _workbench.CreateTaskAsync(
                new CreateTaskRequest(
                    siNetProjectId,
                    assignedTo,
                    taskType.Id,
                    open.Id,
                    title,
                    bucket,
                    Body: body),
                userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded || result.TaskId is not int taskId)
            throw new InvalidOperationException(result.Message ?? "יצירת משימת הכנת חשבון נכשלה.");

        return taskId;
    }

    public async Task<bool> HasOpenPrepareBillTaskAsync(
        int siNetProjectId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ProjectAssignments
            .AsNoTracking()
            .AnyAsync(
                t => t.ProjectId == siNetProjectId
                     && t.TaskType != null
                     && t.TaskType.Code == TaskTypeCodes.PrepareBill
                     && t.AssignmentStatus != null
                     && t.AssignmentStatus.IsOpen,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int?> FindOpenPrepareBillTaskForRequestAsync(
        int siNetProjectId,
        int billingPreparationRequestId,
        CancellationToken cancellationToken = default)
    {
        if (siNetProjectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(siNetProjectId));
        if (billingPreparationRequestId <= 0)
            throw new ArgumentOutOfRangeException(nameof(billingPreparationRequestId));

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var open = await db.ProjectAssignments
            .AsNoTracking()
            .Where(t => t.ProjectId == siNetProjectId
                        && t.TaskType != null
                        && t.TaskType.Code == TaskTypeCodes.PrepareBill
                        && t.AssignmentStatus != null
                        && t.AssignmentStatus.IsOpen)
            .Select(t => new { t.Id, t.Body })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return open
            .Where(t => BillingPreparationTaskInstructions.BodyIdentifiesRequest(
                t.Body, billingPreparationRequestId))
            .OrderBy(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefault();
    }
}
