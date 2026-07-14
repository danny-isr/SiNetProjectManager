using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services.Workflow;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Native workflow engine managing the lifecycle of <see cref="WorkflowInstance"/>:
/// Start, Advance, Pause, Resume, Complete, Cancel. Re-homed from the legacy
/// <c>SiNetSQL.Services.Workflow.WorkflowEngine</c>. Uses short-lived DB contexts per operation.
/// </summary>
internal sealed class WorkflowEngine
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly ProjectWorkflowPolicyService _policyService;

    public WorkflowEngine(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        ProjectWorkflowPolicyService policyService)
    {
        _dbFactory = dbFactory;
        _policyService = policyService;
    }

    /// <summary>
    /// Creates and starts a new workflow instance from a definition, entering the initial stage.
    /// Validates that the workflow is allowed for the project (skipped when not project-bound).
    /// </summary>
    public async ValueTask<WorkflowInstance> StartAsync(
        int definitionId,
        int projectId,
        WorkflowTriggerType triggerType,
        int? triggerEntityId,
        int userId,
        string? notes,
        CancellationToken ct,
        bool isProjectBound = true,
        int? parentWorkflowInstanceId = null,
        string? initialStageCode = null)
    {
        if (isProjectBound &&
            !await _policyService.IsWorkflowAllowedAsync(projectId, definitionId, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Workflow definition {definitionId} is not allowed for project {projectId}.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var definition = await db.WorkflowDefinitions
            .Include(d => d.Stages)
            .FirstOrDefaultAsync(d => d.Id == definitionId && d.IsActive, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow definition {definitionId} not found or inactive.");

        var initialStage = (string.IsNullOrEmpty(initialStageCode)
            ? definition.Stages.Where(s => s.IsInitial).OrderBy(s => s.SortOrder).FirstOrDefault()
            : definition.Stages.FirstOrDefault(s => s.Code == initialStageCode))
            ?? throw new InvalidOperationException(
                $"Workflow definition '{definition.Code}' has no stage matching initial stage criteria.");

        if (parentWorkflowInstanceId is int parentId &&
            !await db.WorkflowInstances.AnyAsync(p => p.Id == parentId, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Parent workflow instance {parentId} not found.");
        }

        var instance = new WorkflowInstance
        {
            WorkflowDefinitionId = definitionId,
            ProjectId = projectId,
            IsProjectBound = isProjectBound,
            Status = WorkflowStatus.Active,
            CurrentStageId = initialStage.Id,
            TriggerType = triggerType,
            TriggerEntityId = triggerEntityId,
            ParentWorkflowInstanceId = parentWorkflowInstanceId,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            Notes = notes,
        };

        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        db.WorkflowStageTransitions.Add(new WorkflowStageTransition
        {
            WorkflowInstanceId = instance.Id,
            ToStageId = initialStage.Id,
            FromStageId = null,
            TransitionedByUserId = userId,
            TransitionedAtUtc = DateTime.UtcNow,
            Notes = "Workflow started.",
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return instance;
    }

    /// <summary>
    /// Advances a workflow instance to a new stage. Validates that the transition is allowed by a
    /// <see cref="WorkflowTransitionRule"/>. If the target stage is final, the instance is completed.
    /// </summary>
    public async ValueTask<WorkflowInstance> AdvanceStageAsync(
        int instanceId,
        int targetStageId,
        int userId,
        string? notes,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await AdvanceStageAsync(db, instanceId, targetStageId, userId, notes, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared-context overload used by the atomic task-close + auto-advance path. Performs the advance
    /// against the caller-provided <paramref name="db"/> so it enlists in the caller's transaction.
    /// </summary>
    public async ValueTask<WorkflowInstance> AdvanceStageAsync(
        SiNetSQLDbContext db,
        int instanceId,
        int targetStageId,
        int userId,
        string? notes,
        CancellationToken ct)
    {
        var instance = await db.WorkflowInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        if (instance.Status != WorkflowStatus.Active)
            throw new InvalidOperationException($"Cannot advance workflow {instanceId}: status is {instance.Status}.");

        if (instance.CurrentStageId is null)
            throw new InvalidOperationException($"Workflow instance {instanceId} has no current stage.");

        var ruleExists = await db.WorkflowTransitionRules
            .AnyAsync(r =>
                r.WorkflowDefinitionId == instance.WorkflowDefinitionId &&
                r.FromStageId == instance.CurrentStageId.Value &&
                r.ToStageId == targetStageId, ct)
            .ConfigureAwait(false);

        if (!ruleExists)
            throw new InvalidOperationException(
                $"Transition from stage {instance.CurrentStageId} to {targetStageId} is not allowed.");

        var targetStage = await db.WorkflowStageDefinitions
            .FirstOrDefaultAsync(s => s.Id == targetStageId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Target stage {targetStageId} not found.");

        var previousStageId = instance.CurrentStageId;

        db.WorkflowStageTransitions.Add(new WorkflowStageTransition
        {
            WorkflowInstanceId = instanceId,
            ToStageId = targetStageId,
            FromStageId = previousStageId,
            TransitionedByUserId = userId,
            TransitionedAtUtc = DateTime.UtcNow,
            Notes = notes,
        });

        instance.CurrentStageId = targetStageId;

        if (targetStage.IsFinal)
        {
            instance.Status = WorkflowStatus.Completed;
            instance.CompletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return instance;
    }

    public async ValueTask<WorkflowInstance> PauseAsync(
        int instanceId, int userId, string? notes, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var instance = await db.WorkflowInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        if (instance.Status != WorkflowStatus.Active)
            throw new InvalidOperationException($"Cannot pause workflow {instanceId}: status is {instance.Status}.");

        instance.Status = WorkflowStatus.Paused;
        instance.Notes = notes ?? instance.Notes;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return instance;
    }

    public async ValueTask<WorkflowInstance> ResumeAsync(
        int instanceId, int userId, string? notes, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var instance = await db.WorkflowInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        if (instance.Status != WorkflowStatus.Paused)
            throw new InvalidOperationException($"Cannot resume workflow {instanceId}: status is {instance.Status}.");

        instance.Status = WorkflowStatus.Active;
        instance.Notes = notes ?? instance.Notes;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return instance;
    }

    public async ValueTask<WorkflowInstance> CompleteAsync(
        int instanceId, int userId, string? notes, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var instance = await db.WorkflowInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        if (instance.Status is WorkflowStatus.Completed or WorkflowStatus.Cancelled)
            throw new InvalidOperationException($"Workflow {instanceId} is already {instance.Status}.");

        instance.Status = WorkflowStatus.Completed;
        instance.CompletedAtUtc = DateTime.UtcNow;
        instance.Notes = notes ?? instance.Notes;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return instance;
    }

    public async ValueTask<WorkflowInstance> CancelAsync(
        int instanceId, int userId, string? notes, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var instance = await db.WorkflowInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workflow instance {instanceId} not found.");

        if (instance.Status is WorkflowStatus.Completed or WorkflowStatus.Cancelled)
            throw new InvalidOperationException($"Workflow {instanceId} is already {instance.Status}.");

        instance.Status = WorkflowStatus.Cancelled;
        instance.CompletedAtUtc = DateTime.UtcNow;
        instance.Notes = notes ?? instance.Notes;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return instance;
    }
}
