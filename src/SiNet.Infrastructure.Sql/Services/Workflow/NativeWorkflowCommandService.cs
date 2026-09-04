using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Identity;
using SiNet.Application.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Native <see cref="IWorkflowCommandService"/> implementation for the New System backbone.
/// Composes the re-homed <see cref="WorkflowTaskOrchestrator"/> + <see cref="WorkflowEngine"/>
/// so hosts no longer need the legacy <c>SiNetSQL</c> adapter. Replaces
/// <see cref="UnboundWorkflowCommandService"/> in <c>AddSiNetProcessBackbone</c>.
/// </summary>
internal sealed class NativeWorkflowCommandService : IWorkflowCommandService
{
    private readonly WorkflowTaskOrchestrator _orchestrator;
    private readonly WorkflowEngine _engine;
    private readonly IPilotStartGate _pilotStartGate;
    private readonly IIdentityOperationGuard? _identityGuard;

    public NativeWorkflowCommandService(
        WorkflowTaskOrchestrator orchestrator,
        WorkflowEngine engine,
        IPilotStartGate pilotStartGate,
        IIdentityOperationGuard? identityGuard = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _pilotStartGate = pilotStartGate ?? throw new ArgumentNullException(nameof(pilotStartGate));
        _identityGuard = identityGuard;
    }

    public async ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct)
    {
        await EnsureIdentityAsync(
                command.ProjectId is > 0
                    ? IdentityOperationContext.ForSiProject(command.ProjectId)
                    : null,
                ct)
            .ConfigureAwait(false);

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Engine.Command.Start",
            $"def={command.DefinitionId} project={command.ProjectId} trigger={command.TriggerType} entity={command.TriggerEntityId} user={command.UserId} bound={command.IsProjectBound} initialStage={command.InitialStageCode ?? "(default)"} jobType={command.JobTypeId?.ToString() ?? "(none)"}");

        await _pilotStartGate
            .EnsureRootStartAllowedAsync(command.UserId, command.DefinitionId, ct)
            .ConfigureAwait(false);

        return await _orchestrator.StartWorkflowAsync(
            command.DefinitionId,
            command.ProjectId,
            ToModel(command.TriggerType),
            command.TriggerEntityId,
            command.UserId,
            command.Notes,
            ct,
            command.IsProjectBound,
            command.InitialStageCode,
            command.JobTypeId).ConfigureAwait(false);
    }

    public async ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct)
    {
        await EnsureIdentityAsync(context: null, ct).ConfigureAwait(false);
        return await _orchestrator.AdvanceWithTasksAsync(
            command.InstanceId,
            command.TargetStageId,
            command.UserId,
            command.Notes,
            ct).ConfigureAwait(false);
    }

    private async Task EnsureIdentityAsync(IdentityOperationContext? context, CancellationToken ct)
    {
        if (_identityGuard is null)
        {
            return;
        }

        await _identityGuard.EnsureAllowedAsync(IdentityOperationKind.WorkflowMutate, context, ct).ConfigureAwait(false);
    }

    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct)
    {
        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Engine.Command.AutoAdvance", $"task={command.TaskId} user={command.UserId} (non-atomic)");
        return _orchestrator.CheckAndAutoAdvanceAsync(command.TaskId, command.UserId, ct);
    }

    /// <summary>
    /// Atomic (shared-context) auto-advance entry point used by <c>SqlTaskCompletionService</c> to run
    /// the workflow advance inside the same <see cref="SiNetSQLDbContext"/> and transaction as the
    /// task-close writes (Phase 1d). Not part of the <see cref="IWorkflowCommandService"/> port because
    /// the shared context is an infrastructure concern; callers use it only when the concrete native
    /// service is in effect. Throws on failure so the caller's transaction rolls back.
    /// </summary>
    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceSharedAsync(
        SiNetSQLDbContext db, TaskClosedCommand command, CancellationToken ct)
    {
        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Engine.Command.AutoAdvance", $"task={command.TaskId} user={command.UserId} (atomic/shared-tx)");
        return _orchestrator.CheckAndAutoAdvanceSharedAsync(db, command.TaskId, command.UserId, ct);
    }

    /// <summary>
    /// Post-commit hook: when a shared auto-advance completed a child instance, advance the parent
    /// on <c>SubWorkflowCompleted</c> outside the child's transaction.
    /// </summary>
    public ValueTask NotifyParentOfCompletedChildAsync(
        int childInstanceId,
        int userId,
        CancellationToken ct)
        => _orchestrator.NotifyParentOfCompletedChildAsync(childInstanceId, userId, ct);

    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct) =>
        _orchestrator.CheckAndAutoAdvanceStalledWorkflowAsync(command.InstanceId, command.UserId, ct);

    public ValueTask<StageCompletionResultDto?> CheckAndAdvanceOnActionCompletedAsync(ActionCompletedCommand command, CancellationToken ct)
    {
        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Engine.Command.ActionCompleted",
            $"instance={command.InstanceId} action={command.ActionCode} outcome={command.ActionOutcome ?? "(none)"} user={command.UserId}");
        return _orchestrator.CheckAndAdvanceOnActionCompletedAsync(command.InstanceId, command.ActionCode, command.ActionOutcome, command.UserId, ct);
    }

    public ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct) =>
        _orchestrator.ReprovisionCurrentStageTasksAsync(command.InstanceId, command.UserId, ct);

    public async ValueTask PauseAsync(PauseWorkflowCommand command, CancellationToken ct)
    {
        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Engine.Command.Pause", $"instance={command.InstanceId} user={command.UserId} notes={command.Notes}");
        await _engine.PauseAsync(command.InstanceId, command.UserId, command.Notes, ct).ConfigureAwait(false);
    }

    public async ValueTask ResumeAsync(ResumeWorkflowCommand command, CancellationToken ct)
    {
        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Engine.Command.Resume", $"instance={command.InstanceId} user={command.UserId} notes={command.Notes}");
        await _engine.ResumeAsync(command.InstanceId, command.UserId, command.Notes, ct).ConfigureAwait(false);
    }

    public async ValueTask CompleteInstanceAsync(CompleteWorkflowCommand command, CancellationToken ct)
    {
        await _engine.CompleteAsync(command.InstanceId, command.UserId, command.Notes, ct).ConfigureAwait(false);
    }

    public async ValueTask CancelAsync(CancelWorkflowCommand command, CancellationToken ct)
    {
        await _engine.CancelAsync(command.InstanceId, command.UserId, command.Notes, ct).ConfigureAwait(false);
    }

    private static WorkflowTriggerType ToModel(WorkflowTriggerTypeDto trigger) => trigger switch
    {
        WorkflowTriggerTypeDto.Manual => WorkflowTriggerType.Manual,
        WorkflowTriggerTypeDto.Email => WorkflowTriggerType.Email,
        WorkflowTriggerTypeDto.System => WorkflowTriggerType.System,
        _ => WorkflowTriggerType.Manual,
    };
}
