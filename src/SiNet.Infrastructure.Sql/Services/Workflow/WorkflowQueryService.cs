using Microsoft.EntityFrameworkCore;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using DomainWorkflowStatus = SiNet.Domain.Workflow.WorkflowStatus;

namespace SiNetSQL.Services.Workflow;

/// <summary>
/// Read-only query service for workflow data.
/// Provides lookups for definitions, active instances, and transition history.
/// <para>
/// EF entities are queried internally and mapped to clean
/// <see cref="SiNet.Application.Workflow"/> DTOs at the boundary via
/// <see cref="WorkflowDtoMappings"/>; entities never leak past this service.
/// </para>
/// </summary>
public class WorkflowQueryService : IWorkflowQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public WorkflowQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Returns all active workflow definitions.
    /// </summary>
    public async ValueTask<List<WorkflowDefinitionDto>> GetActiveDefinitionsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var definitions = await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .Include(d => d.Stages.OrderBy(s => s.SortOrder))
            .OrderBy(d => d.Code)
            .ToListAsync(ct);

        return definitions.ToDtoList();
    }

    /// <summary>
    /// Returns a single definition with its stages and transition rules.
    /// </summary>
    public async ValueTask<WorkflowDefinitionDto?> GetDefinitionAsync(int definitionId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var definition = await db.WorkflowDefinitions
            .AsNoTracking()
            .Include(d => d.Stages.OrderBy(s => s.SortOrder))
            .Include(d => d.TransitionRules)
            .FirstOrDefaultAsync(d => d.Id == definitionId, ct);

        return definition?.ToDto();
    }

    /// <summary>
    /// Returns all workflow instances for a project, optionally filtered by status.
    /// </summary>
    public async ValueTask<List<WorkflowInstanceDto>> GetByProjectAsync(
        int projectId,
        DomainWorkflowStatus? statusFilter,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId);

        if (statusFilter.HasValue)
        {
            var legacyStatus = statusFilter.Value.ToLegacy();
            query = query.Where(i => i.Status == legacyStatus);
        }

        var instances = await query
            .Include(i => i.WorkflowDefinition)
            .Include(i => i.CurrentStage)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(ct);

        return instances.ToDtoList();
    }

    /// <summary>
    /// Returns active workflows for a project (status = Active or Paused).
    /// </summary>
    public async ValueTask<List<WorkflowInstanceDto>> GetActiveByProjectAsync(int projectId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var instances = await db.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId &&
                        (i.Status == WorkflowStatus.Active || i.Status == WorkflowStatus.Paused))
            .Include(i => i.WorkflowDefinition)
            .Include(i => i.CurrentStage)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(ct);

        return instances.ToDtoList();
    }

    /// <summary>
    /// Returns a single workflow instance with full details (definition, stages, transitions).
    /// </summary>
    public async ValueTask<WorkflowInstanceDto?> GetInstanceDetailAsync(int instanceId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.WorkflowDefinition)
                .ThenInclude(d => d.Stages.OrderBy(s => s.SortOrder))
            .Include(i => i.CurrentStage)
            .Include(i => i.StageTransitions.OrderBy(t => t.TransitionedAtUtc))
                .ThenInclude(t => t.ToStage)
            .Include(i => i.StageTransitions)
                .ThenInclude(t => t.TransitionedByUser)
            .Include(i => i.CreatedByUser)
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct);

        return instance?.ToDto();
    }

    /// <summary>
    /// Returns the allowed target stages for the current stage of a workflow instance.
    /// Used to present valid transition options in the UI.
    /// </summary>
    public async ValueTask<List<WorkflowStageDefinitionDto>> GetAllowedNextStagesAsync(
        int instanceId,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct);

        if (instance is null || instance.CurrentStageId is null)
            return [];

        var targetStageIds = await db.WorkflowTransitionRules
            .AsNoTracking()
            .Where(r =>
                r.WorkflowDefinitionId == instance.WorkflowDefinitionId &&
                r.FromStageId == instance.CurrentStageId.Value)
            .Select(r => r.ToStageId)
            .ToListAsync(ct);

        if (targetStageIds.Count == 0)
            return [];

        var stages = await db.WorkflowStageDefinitions
            .AsNoTracking()
            .Where(s => targetStageIds.Contains(s.Id))
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        return stages.ToDtoList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dashboard — cross-project overview
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns all non-ended projects LEFT-JOINed with their latest workflow instance.
    /// Each result contains project info, instance status, current stage, stage list, and
    /// the set of distinct stage IDs already transitioned through (for pipeline display).
    /// </summary>
    public async ValueTask<List<ProjectWorkflowSnapshotDto>> GetAllProjectWorkflowSnapshotsAsync(
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // 1. All active projects
        var projects = await db.Projects
            .AsNoTracking()
            .Where(p => p.EndOfProject != true)
            .OrderBy(p => p.Title)
            .ToListAsync(ct);

        // 2. All instances for those projects (include JobType for B2 track display)
        var latestInstances = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.WorkflowDefinition)
                .ThenInclude(d => d.Stages.OrderBy(s => s.SortOrder))
            .Include(i => i.CurrentStage)
            .Include(i => i.JobType)
            .Include(i => i.StageTransitions)
            .Where(i => projects.Select(p => p.Id).Contains(i.ProjectId))
            .ToListAsync(ct);

        var byProject = latestInstances.GroupBy(i => i.ProjectId).ToDictionary(g => g.Key, g => g.ToList());

        var results = new List<ProjectWorkflowSnapshotDto>(projects.Count);

        foreach (var project in projects)
        {
            if (byProject.TryGetValue(project.Id, out var projectInstances) && projectInstances.Count > 0)
            {
                var tracks = projectInstances
                    .Where(i => i.Status is WorkflowStatus.Active or WorkflowStatus.Paused)
                    .OrderBy(i => i.JobTypeId ?? int.MaxValue)
                    .ThenByDescending(i => i.CreatedAtUtc)
                    .Select(i => i.ToDto())
                    .ToList();

                var inst = projectInstances
                    .OrderByDescending(i => StatusPriority(i.Status))
                    .ThenByDescending(i => i.CreatedAtUtc)
                    .First();

                var stages = inst.WorkflowDefinition.Stages.OrderBy(s => s.SortOrder).ToDtoList();
                var visitedStageIds = inst.StageTransitions
                    .Select(t => t.ToStageId)
                    .ToHashSet();

                results.Add(new ProjectWorkflowSnapshotDto(
                    project.ToDto(),
                    inst.ToDto(),
                    stages,
                    visitedStageIds,
                    tracks));
            }
            else
            {
                results.Add(new ProjectWorkflowSnapshotDto(
                    project.ToDto(),
                    Instance: null,
                    AllStages: [],
                    VisitedStageIds: new HashSet<int>(),
                    TrackInstances: []));
            }
        }

        return results;
    }

    /// <summary>
    /// Returns ALL workflow instances (regardless of project) with full stage info.
    /// Used by the floating Workflow Status Monitor window.
    /// </summary>
    public async ValueTask<List<WorkflowInstanceSnapshotDto>> GetAllWorkflowInstanceSnapshotsAsync(
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var instances = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.WorkflowDefinition)
                .ThenInclude(d => d.Stages.OrderBy(s => s.SortOrder))
            .Include(i => i.CurrentStage)
            .Include(i => i.JobType)
            .Include(i => i.Project)
            .Include(i => i.StageTransitions)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(ct);

        return instances.Select(inst =>
        {
            var stages = inst.WorkflowDefinition.Stages.OrderBy(s => s.SortOrder).ToDtoList();
            var visited = inst.StageTransitions.Select(t => t.ToStageId).ToHashSet();
            return new WorkflowInstanceSnapshotDto(inst.ToDto(), stages, visited);
        }).ToList();
    }

    /// <summary>
    /// Returns the distinct workflow definition names that have at least one instance.
    /// Used to populate the "workflow type" filter.
    /// </summary>
    public async ValueTask<List<string>> GetDistinctWorkflowNamesAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.WorkflowInstances
            .AsNoTracking()
            .Select(i => i.WorkflowDefinition.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns the task-completion progress for a workflow instance's current stage
    /// (required/optional/created/closed counts). Read-only projection; mirrors the
    /// orchestrator's stage-progress query but returns a clean value DTO.
    /// </summary>
    public async ValueTask<StageTaskProgressDto> GetStageTaskProgressAsync(int instanceId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var instance = await db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        if (instance.CurrentStageId is null)
            return StageTaskProgressDto.Empty;

        var currentStageId = instance.CurrentStageId.Value;
        // Canonical stage tag stored in TaskLink.Description (mirrors WorkflowConstants.BuildStageTag,
        // which lives in the SiNetSQL assembly that this infrastructure module does not reference).
        var stageTag = $"Stage:{currentStageId}";

        var templates = await db.WorkflowStageTasks
            .AsNoTracking()
            .Where(st => st.StageDefinitionId == currentStageId && st.IsActive)
            .Select(st => new { st.TaskTypeId, st.IsRequired })
            .ToListAsync(ct);

        var totalRequired = templates.Count(t => t.IsRequired);
        var totalOptional = templates.Count(t => !t.IsRequired);

        var linkedTaskStatuses = await (
            from link in db.TaskLinks.AsNoTracking()
            join task in db.ProjectAssignments.AsNoTracking()
                on link.TaskId equals task.Id
            join status in db.ProjectAssignmentStatuses.AsNoTracking()
                on task.StatusId equals status.Id
            where link.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
               && link.LinkedEntityId == instanceId
               && link.Role == TaskLinkRole.Trigger
               && link.Description == stageTag
            select new { task.TaskTypeId, status.IsOpen }
        ).ToListAsync(ct);

        var requiredTaskTypeIds = templates
            .Where(t => t.IsRequired)
            .Select(t => t.TaskTypeId)
            .ToHashSet();

        var completedRequired = linkedTaskStatuses
            .Where(t => t.TaskTypeId.HasValue && requiredTaskTypeIds.Contains(t.TaskTypeId.Value) && !t.IsOpen)
            .Select(t => t.TaskTypeId!.Value)
            .Distinct()
            .Count();

        var totalCreated = linkedTaskStatuses.Count;
        var totalClosed = linkedTaskStatuses.Count(t => !t.IsOpen);

        return new StageTaskProgressDto(totalRequired, completedRequired, totalOptional, totalCreated, totalClosed);
    }

    /// <summary>Priority for picking the "best" instance: Active first, then Paused, etc.</summary>
    private static int StatusPriority(WorkflowStatus s) => s switch
    {
        WorkflowStatus.Active => 4,
        WorkflowStatus.Paused => 3,
        WorkflowStatus.Draft => 2,
        _ => 0
    };
}
