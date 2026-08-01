using System.Diagnostics;

using Microsoft.EntityFrameworkCore;

using SiNet.Application.Identity;

using SiNet.Application.Tasks;

using SiNet.Application.WorkSurfaces;

using SiNet.Infrastructure.Sql.Constants;

using SiNetSQL.Data;

using SiNetSQL.Models;



namespace SiNet.Infrastructure.Sql.Services.Tasks;



/// <summary>

/// Native Infrastructure.Sql implementation of <see cref="ITaskNavigationService"/>. Ports the

/// read-only logic from legacy <c>TaskNavigationResolver</c> and maps directly to

/// <see cref="WorkSurfaceContext"/> without crossing through LegacyBridge.

/// </summary>

public sealed class SqlTaskNavigationService : ITaskNavigationService

{

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    private readonly ICurrentUserContext? _currentUser;



    public SqlTaskNavigationService(

        IDbContextFactory<SiNetSQLDbContext> dbFactory,

        ICurrentUserContext? currentUser = null)

    {

        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

        _currentUser = currentUser;

    }



    public async ValueTask<WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct)

    {

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);



        var task = await db.ProjectAssignments

            .AsNoTracking()

            .Include(t => t.TaskType)

            .Include(t => t.TaskLinks)

            .FirstOrDefaultAsync(t => t.Id == taskId, ct)

            .ConfigureAwait(false);



        if (task is null)

            return null;



        if (task.TaskType is null || string.IsNullOrEmpty(task.TaskType.Code))

            return null;



        var taskTypeCode = task.TaskType.Code;

        var interaction = ReviewTaskInteractionRegistry.TryGet(taskTypeCode);

        if (interaction is null)

            return null;



        int? workflowInstanceId = null;

        int? stageGroupId = null;

        string? processDisplayName = null;

        string? jobTypeDisplayName = null;

        string? currentStageDisplayName = null;



        // B2: prefer exact Trigger link; never guess newest-on-project when a link exists.

        var triggerLinkId = task.TaskLinks

            .Where(l => l.Role == TaskLinkRole.Trigger

                        && l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance)

            .OrderByDescending(l => l.CreatedAtUtc)

            .Select(l => (long?)l.LinkedEntityId)

            .FirstOrDefault();



        if (triggerLinkId is long linkedLong && linkedLong > 0 && linkedLong <= int.MaxValue)
        {
            var linkedInstanceId = (int)linkedLong;
            var instance = await db.WorkflowInstances
                .AsNoTracking()
                .Include(i => i.CurrentStage)
                .Include(i => i.WorkflowDefinition)
                .Include(i => i.JobType)
                .FirstOrDefaultAsync(i => i.Id == linkedInstanceId, ct)
                .ConfigureAwait(false);

            if (instance is not null)
            {
                workflowInstanceId = instance.Id;
                stageGroupId = instance.CurrentStage?.AssignedGroupId;
                processDisplayName = instance.WorkflowDefinition?.Name ?? instance.WorkflowDefinition?.Code;
                jobTypeDisplayName = instance.JobType?.Title;
                currentStageDisplayName = instance.CurrentStage?.Name ?? instance.CurrentStage?.Code;
            }
        }
        else if (task.ProjectId is int projectId)

        {

            // Fallback only when the task has no Trigger link (ad-hoc / legacy rows).

            var instance = await db.WorkflowInstances

                .AsNoTracking()

                .Include(i => i.CurrentStage)

                .Include(i => i.WorkflowDefinition)

                .Include(i => i.JobType)

                .Where(i => i.ProjectId == projectId

                            && i.Status != WorkflowStatus.Completed

                            && i.Status != WorkflowStatus.Cancelled)

                .OrderByDescending(i => i.CreatedAtUtc)

                .FirstOrDefaultAsync(ct)

                .ConfigureAwait(false);



            if (instance is not null)

            {

                workflowInstanceId = instance.Id;

                stageGroupId = instance.CurrentStage?.AssignedGroupId;

                processDisplayName = instance.WorkflowDefinition?.Name ?? instance.WorkflowDefinition?.Code;

                jobTypeDisplayName = instance.JobType?.Title;

                currentStageDisplayName = instance.CurrentStage?.Name ?? instance.CurrentStage?.Code;

            }

        }



        var assignedUserId = task.AssignedToId;

        var assignedGroupId = task.TaskGroupId ?? stageGroupId;



        if (assignedUserId is null && assignedGroupId is null)

            return null;



        var (resolved, primaryId, _, _) = ResolveWorkTargets(task, interaction);

        if (!resolved)

        {

            Trace.TraceWarning(

                "[SqlTaskNavigationService] Task {0} has ambiguous work targets; navigation blocked.",

                taskId);

            return null;

        }



        var completionEventCode =

            ReviewCompletionEventBehavior.TryResolveUniqueEventCodeForTaskType(taskTypeCode);



        return new WorkSurfaceContext(

            TaskId: task.Id,

            ProjectId: task.ProjectId ?? 0,

            WorkflowInstanceId: workflowInstanceId,

            ComponentKey: interaction.ComponentKey,

            PrimaryWorkTargetEntityId: ToInt32(primaryId),

            AllowedResultCodes: interaction.AllowedTaskResultCodes,

            CompletionEventCode: completionEventCode,

            ActingUserId: _currentUser?.UserId,

            TaskTypeCode: taskTypeCode,

            ProcessDisplayName: processDisplayName,

            JobTypeDisplayName: jobTypeDisplayName,

            CurrentStageDisplayName: currentStageDisplayName);

    }



    private static (bool Resolved, long? PrimaryId, IReadOnlyList<long> All, IReadOnlyList<long> Pending)

        ResolveWorkTargets(ProjectAssignment task, TaskInteractionDefinition interaction)

    {

        var targetEntityLinkType = MapWorkTargetToLinkedEntityType(interaction.PrimaryWorkTargetEntityType);



        var candidates = task.TaskLinks

            .Where(l => l.Role == interaction.RequiredTaskLinkRole)

            .Where(l => targetEntityLinkType is null || l.LinkedEntityType == targetEntityLinkType)

            .ToList();



        var workTargets = candidates.Where(l => l.IsWorkTarget).ToList();

        if (workTargets.Count > 0)

        {

            var allIds = workTargets.Select(l => l.LinkedEntityId).ToArray();

            var pendingIds = workTargets

                .Where(l => l.WorkStatus != WorkTargetStatus.Done && l.WorkStatus != WorkTargetStatus.Skipped)

                .Select(l => l.LinkedEntityId)

                .ToArray();



            if (pendingIds.Length > 1)

                return (false, null, allIds, pendingIds);



            if (pendingIds.Length == 1)

                return (true, pendingIds[0], allIds, pendingIds);



            if (allIds.Length > 1)

                return (false, null, allIds, pendingIds);



            if (allIds.Length == 1)

                return (true, allIds[0], allIds, pendingIds);



            return (false, null, allIds, pendingIds);

        }



        if (candidates.Count == 0)

            return (true, null, Array.Empty<long>(), Array.Empty<long>());



        if (candidates.Count > 1)

            return (false, null, candidates.Select(l => l.LinkedEntityId).ToArray(), Array.Empty<long>());



        var id = candidates[0].LinkedEntityId;

        return (true, id, [id], Array.Empty<long>());

    }



    private static TaskLinkEntityType? MapWorkTargetToLinkedEntityType(TaskWorkTargetEntityType target) => target switch

    {

        TaskWorkTargetEntityType.EmailInboxMessage => TaskLinkEntityType.EmailInboxMessage,

        TaskWorkTargetEntityType.EmailInboxAttachment => TaskLinkEntityType.EmailInboxMessage,

        TaskWorkTargetEntityType.EmailThread => TaskLinkEntityType.EmailInboxMessage,

        TaskWorkTargetEntityType.ProjectFile => TaskLinkEntityType.ProjectFile,

        TaskWorkTargetEntityType.InspectionReport => TaskLinkEntityType.InspectionReport,

        TaskWorkTargetEntityType.InspectionNote => TaskLinkEntityType.InspectionNote,

        _ => null,

    };



    private static int? ToInt32(long? value)

        => value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;

}


