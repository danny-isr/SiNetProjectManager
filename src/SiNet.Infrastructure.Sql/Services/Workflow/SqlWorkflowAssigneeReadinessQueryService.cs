using Microsoft.EntityFrameworkCore;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Reports non-final stages on active workflows whose assigned group cannot resolve an assignee
/// using the same rules as <see cref="WorkflowStageTaskProvisioningService.TryResolveAssigneeFromGroup"/>.
/// </summary>
public sealed class SqlWorkflowAssigneeReadinessQueryService : IWorkflowAssigneeReadinessQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlWorkflowAssigneeReadinessQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<IReadOnlyList<WorkflowAssigneeReadinessIssueDto>> GetIssuesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var definitions = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .Include(d => d.Stages)
            .OrderBy(d => d.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var allGroups = await db.UserGroups
            .AsNoTracking()
            .Include(g => g.Memberships).ThenInclude(m => m.Siuser)
            .ToDictionaryAsync(g => g.Id, cancellationToken)
            .ConfigureAwait(false);

        var issues = new List<WorkflowAssigneeReadinessIssueDto>();

        foreach (var def in definitions)
        {
            foreach (var stage in def.Stages.Where(s => !s.IsFinal).OrderBy(s => s.SortOrder))
            {
                var issue = EvaluateStage(def.Code, stage, allGroups);
                if (issue is not null)
                    issues.Add(issue);
            }
        }

        return issues;
    }

    /// <summary>
    /// Pure mapping used by unit tests and by <see cref="GetIssuesAsync"/>.
    /// Mirrors runtime assignee resolution failure modes.
    /// </summary>
    internal static WorkflowAssigneeReadinessIssueDto? EvaluateStage(
        string workflowCode,
        WorkflowStageDefinition stage,
        IReadOnlyDictionary<int, UserGroup> groupsById)
    {
        if (stage.AssignedGroupId is null)
        {
            return new WorkflowAssigneeReadinessIssueDto(
                workflowCode,
                stage.Code,
                stage.Name,
                GroupCode: null,
                WorkflowAssigneeIssueKind.MissingAssignedGroup,
                $"לשלב {workflowCode}.{stage.Code} אין קבוצת מבצעים (AssignedGroupId).");
        }

        if (!groupsById.TryGetValue(stage.AssignedGroupId.Value, out var group) || group is null)
        {
            return new WorkflowAssigneeReadinessIssueDto(
                workflowCode,
                stage.Code,
                stage.Name,
                GroupCode: null,
                WorkflowAssigneeIssueKind.GroupMissing,
                $"לשלב {workflowCode}.{stage.Code} משויכת קבוצה חסרה (id={stage.AssignedGroupId}).");
        }

        var (assigneeId, activeMemberCount) =
            WorkflowStageTaskProvisioningService.TryResolveAssigneeFromGroup(group);

        if (assigneeId.HasValue)
            return null;

        if (activeMemberCount == 0)
        {
            return new WorkflowAssigneeReadinessIssueDto(
                workflowCode,
                stage.Code,
                stage.Name,
                group.Code,
                WorkflowAssigneeIssueKind.NoActiveMembers,
                $"קבוצה '{group.Code}' בשלב {workflowCode}.{stage.Code} ללא חברים פעילים — לא ניתן ליצור משימה.");
        }

        return new WorkflowAssigneeReadinessIssueDto(
            workflowCode,
            stage.Code,
            stage.Name,
            group.Code,
            WorkflowAssigneeIssueKind.MultipleMembersWithoutDefault,
            $"קבוצה '{group.Code}' בשלב {workflowCode}.{stage.Code} עם {activeMemberCount} חברים פעילים בלי DefaultAssigneeId תקף.");
    }
}
