using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNetSQL.Services.Workflow;

/// <summary>
/// Read-only query service for workflow data.
/// Provides lookups for definitions, active instances, and transition history.
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
    public async ValueTask<List<WorkflowDefinition>> GetActiveDefinitionsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.WorkflowDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .Include(d => d.Stages.OrderBy(s => s.SortOrder))
            .OrderBy(d => d.Code)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns a single definition with its stages and transition rules.
    /// </summary>
    public async ValueTask<WorkflowDefinition?> GetDefinitionAsync(int definitionId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.WorkflowDefinitions
            .AsNoTracking()
            .Include(d => d.Stages.OrderBy(s => s.SortOrder))
            .Include(d => d.TransitionRules)
            .FirstOrDefaultAsync(d => d.Id == definitionId, ct);
    }

    /// <summary>
    /// Returns all workflow instances for a project, optionally filtered by status.
    /// </summary>
    public async ValueTask<List<WorkflowInstance>> GetByProjectAsync(
        int projectId,
        WorkflowStatus? statusFilter,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = db.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId);

        if (statusFilter.HasValue)
            query = query.Where(i => i.Status == statusFilter.Value);

        return await query
            .Include(i => i.WorkflowDefinition)
            .Include(i => i.CurrentStage)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns active workflows for a project (status = Active or Paused).
    /// </summary>
    public async ValueTask<List<WorkflowInstance>> GetActiveByProjectAsync(int projectId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.WorkflowInstances
            .AsNoTracking()
            .Where(i => i.ProjectId == projectId &&
                        (i.Status == WorkflowStatus.Active || i.Status == WorkflowStatus.Paused))
            .Include(i => i.WorkflowDefinition)
            .Include(i => i.CurrentStage)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns a single workflow instance with full details (definition, stages, transitions).
    /// </summary>
    public async ValueTask<WorkflowInstance?> GetInstanceDetailAsync(int instanceId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.WorkflowInstances
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
    }

    /// <summary>
    /// Returns the allowed target stages for the current stage of a workflow instance.
    /// Used to present valid transition options in the UI.
    /// </summary>
    public async ValueTask<List<WorkflowStageDefinition>> GetAllowedNextStagesAsync(
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

        return await db.WorkflowStageDefinitions
            .AsNoTracking()
            .Where(s => targetStageIds.Contains(s.Id))
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dashboard — cross-project overview
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns all non-ended projects LEFT-JOINed with their latest workflow instance.
    /// Each result contains project info, instance status, current stage, stage list, and
    /// the set of distinct stage IDs already transitioned through (for pipeline display).
    /// </summary>
    public async ValueTask<List<ProjectWorkflowSnapshot>> GetAllProjectWorkflowSnapshotsAsync(
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // 1. All active projects
        var projects = await db.Projects
            .AsNoTracking()
            .Where(p => p.EndOfProject != true)
            .OrderBy(p => p.Title)
            .ToListAsync(ct);

        // 2. Latest instance per project (Active > Paused > Draft > rest, then newest)
        var latestInstances = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.WorkflowDefinition)
                .ThenInclude(d => d.Stages.OrderBy(s => s.SortOrder))
            .Include(i => i.CurrentStage)
            .Include(i => i.StageTransitions)
            .Where(i => projects.Select(p => p.Id).Contains(i.ProjectId))
            .ToListAsync(ct);

        // Group by project and pick the "most relevant" instance
        var bestByProject = latestInstances
            .GroupBy(i => i.ProjectId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(i => StatusPriority(i.Status))
                      .ThenByDescending(i => i.CreatedAtUtc)
                      .First());

        var results = new List<ProjectWorkflowSnapshot>(projects.Count);

        foreach (var project in projects)
        {
            if (bestByProject.TryGetValue(project.Id, out var inst))
            {
                var stages = inst.WorkflowDefinition.Stages.OrderBy(s => s.SortOrder).ToList();
                var visitedStageIds = inst.StageTransitions
                    .Select(t => t.ToStageId)
                    .ToHashSet();

                results.Add(new ProjectWorkflowSnapshot
                {
                    Project = project,
                    Instance = inst,
                    AllStages = stages,
                    VisitedStageIds = visitedStageIds
                });
            }
            else
            {
                results.Add(new ProjectWorkflowSnapshot { Project = project });
            }
        }

        return results;
    }

    /// <summary>
    /// Returns ALL workflow instances (regardless of project) with full stage info.
    /// Used by the floating Workflow Status Monitor window.
    /// </summary>
    public async ValueTask<List<WorkflowInstanceSnapshot>> GetAllWorkflowInstanceSnapshotsAsync(
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var instances = await db.WorkflowInstances
            .AsNoTracking()
            .Include(i => i.WorkflowDefinition)
                .ThenInclude(d => d.Stages.OrderBy(s => s.SortOrder))
            .Include(i => i.CurrentStage)
            .Include(i => i.Project)
            .Include(i => i.StageTransitions)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(ct);

        return instances.Select(inst =>
        {
            var stages = inst.WorkflowDefinition.Stages.OrderBy(s => s.SortOrder).ToList();
            var visited = inst.StageTransitions.Select(t => t.ToStageId).ToHashSet();
            return new WorkflowInstanceSnapshot
            {
                Instance = inst,
                AllStages = stages,
                VisitedStageIds = visited
            };
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

    /// <summary>Priority for picking the "best" instance: Active first, then Paused, etc.</summary>
    private static int StatusPriority(WorkflowStatus s) => s switch
    {
        WorkflowStatus.Active => 4,
        WorkflowStatus.Paused => 3,
        WorkflowStatus.Draft => 2,
        _ => 0
    };
}

/// <summary>
/// Lightweight projection for the cross-project workflow dashboard.
/// </summary>
public class ProjectWorkflowSnapshot
{
    public required Project Project { get; init; }
    public WorkflowInstance? Instance { get; init; }
    public List<WorkflowStageDefinition> AllStages { get; init; } = [];
    public HashSet<int> VisitedStageIds { get; init; } = [];
}

/// <summary>
/// Instance-centric snapshot for the floating workflow monitor.
/// </summary>
public class WorkflowInstanceSnapshot
{
    public required WorkflowInstance Instance { get; init; }
    public List<WorkflowStageDefinition> AllStages { get; init; } = [];
    public HashSet<int> VisitedStageIds { get; init; } = [];
}
