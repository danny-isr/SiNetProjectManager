using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// B2: for JobType-bound instances, <c>ProjectTypeWorkflowStage</c> selects which
/// stages of the definition may become active / provision tasks.
/// </summary>
internal static class ProjectTypeWorkflowStagePolicy
{
    /// <summary>
    /// When a JobType has any stage-profile rows for the definition, the target stage
    /// must appear as <c>IsActive</c>. Missing profile for the JobType+definition is a
    /// configuration error (no silent generic path).
    /// </summary>
    public static async Task EnsureStageAllowedOrThrowAsync(
        SiNetSQLDbContext db,
        int jobTypeId,
        int workflowDefinitionId,
        int stageDefinitionId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var profile = await db.ProjectTypeWorkflowStages.AsNoTracking()
            .Where(p =>
                p.ProjectTypeId == jobTypeId
                && p.WorkflowStageDefinition.WorkflowDefinitionId == workflowDefinitionId)
            .Select(p => new { p.WorkflowStageDefinitionId, p.IsActive })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (profile.Count == 0)
        {
            throw new InvalidOperationException(
                $"חסר פרופיל שלבים (ProjectTypeWorkflowStage) לסוג פרויקט {jobTypeId} בתהליך {workflowDefinitionId}. הרץ Seed או הגדר במנהלה.");
        }

        var row = profile.FirstOrDefault(p => p.WorkflowStageDefinitionId == stageDefinitionId);
        if (row is null || !row.IsActive)
        {
            throw new InvalidOperationException(
                $"השלב {stageDefinitionId} אינו פעיל לסוג פרויקט {jobTypeId} בתהליך {workflowDefinitionId}.");
        }
    }

    public static async Task<bool> IsStageAllowedAsync(
        SiNetSQLDbContext db,
        int? jobTypeId,
        int workflowDefinitionId,
        int stageDefinitionId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (jobTypeId is null)
            return true;

        try
        {
            await EnsureStageAllowedOrThrowAsync(
                    db, jobTypeId.Value, workflowDefinitionId, stageDefinitionId, ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
