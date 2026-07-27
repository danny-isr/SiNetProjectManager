using SiNet.Application.Workflow;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Adapts the native stalled-workflow watchdog to the Application recovery port.
/// </summary>
public sealed class SqlWorkflowRecoveryService(StalledWorkflowWatchdog watchdog) : IWorkflowRecoveryService
{
    private readonly StalledWorkflowWatchdog _watchdog =
        watchdog ?? throw new ArgumentNullException(nameof(watchdog));

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkflowRecoveryCandidate>> DetectStalledAsync(CancellationToken ct)
    {
        var stalled = await _watchdog.DetectStalledAsync(ct).ConfigureAwait(false);
        return stalled
            .Select(item => new WorkflowRecoveryCandidate(
                item.InstanceId,
                item.WorkflowDefinitionId,
                item.CurrentStageId,
                item.StageName,
                item.ProjectId,
                item.MostRecentClosedTaskId,
                item.TotalTasks,
                item.OpenTasks))
            .ToArray();
    }

    /// <inheritdoc />
    public ValueTask<int> AttemptRecoveryAsync(
        IReadOnlyList<WorkflowRecoveryCandidate> stalled,
        int systemUserId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stalled);

        return _watchdog.AttemptRecoveryAsync(
            stalled.Select(item => new StalledWorkflowInfo(
                    item.InstanceId,
                    item.DefinitionId,
                    item.StageId,
                    item.StageName,
                    item.ProjectId ?? 0,
                    item.MostRecentClosedTaskId,
                    item.TotalTasks,
                    item.OpenTasks))
                .ToList(),
            systemUserId,
            ct);
    }
}
