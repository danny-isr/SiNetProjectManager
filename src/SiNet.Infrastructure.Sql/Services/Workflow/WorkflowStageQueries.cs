using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Shared query helpers for the native workflow engine.
/// Eliminates duplicate queries across <see cref="WorkflowTaskOrchestrator"/>,
/// <see cref="WorkflowTransitionEvaluator"/>, and the transition action handlers.
/// </summary>
internal static class WorkflowStageQueries
{
    /// <summary>
    /// Checks whether all required task types for a stage are closed (completed).
    /// A stage with no required templates is considered complete.
    /// </summary>
    public static async ValueTask<bool> AreAllRequiredTasksCompleteAsync(
        SiNetSQLDbContext db,
        int instanceId,
        int stageId,
        CancellationToken ct)
    {
        var requiredTaskTypeIds = await db.WorkflowStageTasks
            .AsNoTracking()
            .Where(st => st.StageDefinitionId == stageId && st.IsActive && st.IsRequired)
            .Select(st => st.TaskTypeId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (requiredTaskTypeIds.Count == 0)
            return true;

        var stageTag = WorkflowConstants.BuildStageTag(stageId);

        var linkedTaskIds = await db.TaskLinks
            .AsNoTracking()
            .Where(l => l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                     && l.LinkedEntityId == instanceId
                     && l.Role == TaskLinkRole.Trigger
                     && l.Description == stageTag)
            .Select(l => l.TaskId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (linkedTaskIds.Count == 0)
            return false;

        var closedTaskTypeIds = await db.ProjectAssignments
            .AsNoTracking()
            .Where(pa => linkedTaskIds.Contains(pa.Id)
                      && pa.TaskTypeId.HasValue
                      && pa.AssignmentStatus != null
                      && !pa.AssignmentStatus.IsOpen)
            .Select(pa => pa.TaskTypeId!.Value)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return requiredTaskTypeIds.All(rtId => closedTaskTypeIds.Contains(rtId));
    }
}
