namespace SiNet.Application.Runtime;

/// <summary>
/// Aggregates New System runtime subsystem status (health checks, ACC, Gmail, background work, startup tasks).
/// </summary>
public interface IRuntimeSubsystemStatusService
{
    IReadOnlyList<SubsystemRuntimeStatus> Current { get; }

    event EventHandler? Changed;

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
