using SiNet.Application.Workflow;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Fail-fast placeholder registered by <c>AddSiNetProcessBackbone</c> when the host has not bound
/// a real <see cref="IWorkflowCommandService"/> (e.g. standalone New System without SiNetSQL adapter).
/// V2 replaces this via <c>AddSiNetWorkflowCommands()</c>.
/// </summary>
public sealed class UnboundWorkflowCommandService : IWorkflowCommandService
{
    private static InvalidOperationException Unbound() =>
        new("IWorkflowCommandService is not bound. The host must call AddSiNetWorkflowCommands() " +
            "(WorkflowCommandServiceAdapter) before task completion auto-advance can run.");

    public ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct)
        => throw Unbound();

    public ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct)
        => throw Unbound();

    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct)
        => throw Unbound();

    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct)
        => throw Unbound();

    public ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct)
        => throw Unbound();

    public ValueTask PauseAsync(PauseWorkflowCommand command, CancellationToken ct)
        => throw Unbound();

    public ValueTask ResumeAsync(ResumeWorkflowCommand command, CancellationToken ct)
        => throw Unbound();

    public ValueTask CompleteInstanceAsync(CompleteWorkflowCommand command, CancellationToken ct)
        => throw Unbound();

    public ValueTask CancelAsync(CancelWorkflowCommand command, CancellationToken ct)
        => throw Unbound();
}
