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

    public NativeWorkflowCommandService(WorkflowTaskOrchestrator orchestrator, WorkflowEngine engine)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct) =>
        _orchestrator.StartWorkflowAsync(
            command.DefinitionId,
            command.ProjectId,
            ToModel(command.TriggerType),
            command.TriggerEntityId,
            command.UserId,
            command.Notes,
            ct,
            command.IsProjectBound,
            command.InitialStageCode);

    public ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct) =>
        _orchestrator.AdvanceWithTasksAsync(
            command.InstanceId,
            command.TargetStageId,
            command.UserId,
            command.Notes,
            ct);

    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct) =>
        _orchestrator.CheckAndAutoAdvanceAsync(command.TaskId, command.UserId, ct);

    /// <summary>
    /// Atomic (shared-context) auto-advance entry point used by <c>SqlTaskCompletionService</c> to run
    /// the workflow advance inside the same <see cref="SiNetSQLDbContext"/> and transaction as the
    /// task-close writes (Phase 1d). Not part of the <see cref="IWorkflowCommandService"/> port because
    /// the shared context is an infrastructure concern; callers use it only when the concrete native
    /// service is in effect. Throws on failure so the caller's transaction rolls back.
    /// </summary>
    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceSharedAsync(
        SiNetSQLDbContext db, TaskClosedCommand command, CancellationToken ct) =>
        _orchestrator.CheckAndAutoAdvanceSharedAsync(db, command.TaskId, command.UserId, ct);

    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct) =>
        _orchestrator.CheckAndAutoAdvanceStalledWorkflowAsync(command.InstanceId, command.UserId, ct);

    public ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct) =>
        _orchestrator.ReprovisionCurrentStageTasksAsync(command.InstanceId, command.UserId, ct);

    public async ValueTask PauseAsync(PauseWorkflowCommand command, CancellationToken ct)
    {
        await _engine.PauseAsync(command.InstanceId, command.UserId, command.Notes, ct).ConfigureAwait(false);
    }

    public async ValueTask ResumeAsync(ResumeWorkflowCommand command, CancellationToken ct)
    {
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
