using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Tests.Support;

/// <summary>
/// Minimal no-op / recording <see cref="IWorkflowCommandService"/> test double. Pause/Resume are
/// recorded (they are the only members the Task Workbench integrity paths use); the start/advance
/// members throw so accidental use in a test is loud.
/// </summary>
public sealed class StubWorkflowCommandService : IWorkflowCommandService
{
    public List<int> PausedInstanceIds { get; } = [];
    public List<int> ResumedInstanceIds { get; } = [];

    public int PauseCallCount => PausedInstanceIds.Count;
    public int ResumeCallCount => ResumedInstanceIds.Count;

    public ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct)
        => throw new NotSupportedException();

    public ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct)
        => throw new NotSupportedException();

    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct)
        => ValueTask.FromResult<StageCompletionResultDto?>(null);

    public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct)
        => ValueTask.FromResult<StageCompletionResultDto?>(null);

    public ValueTask<StageCompletionResultDto?> CheckAndAdvanceOnActionCompletedAsync(ActionCompletedCommand command, CancellationToken ct)
        => ValueTask.FromResult<StageCompletionResultDto?>(null);

    public ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct)
        => ValueTask.FromResult(0);

    public ValueTask PauseAsync(PauseWorkflowCommand command, CancellationToken ct)
    {
        PausedInstanceIds.Add(command.InstanceId);
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync(ResumeWorkflowCommand command, CancellationToken ct)
    {
        ResumedInstanceIds.Add(command.InstanceId);
        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteInstanceAsync(CompleteWorkflowCommand command, CancellationToken ct)
        => ValueTask.CompletedTask;

    public ValueTask CancelAsync(CancelWorkflowCommand command, CancellationToken ct)
        => ValueTask.CompletedTask;
}
