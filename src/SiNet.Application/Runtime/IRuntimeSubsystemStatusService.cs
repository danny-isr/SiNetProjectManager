namespace SiNet.Application.Runtime;

/// <summary>
/// Aggregates New System runtime subsystem status (health checks, ACC, Gmail, background work, startup tasks).
/// </summary>
public interface IRuntimeSubsystemStatusService
{
    IReadOnlyList<SubsystemRuntimeStatus> Current { get; }

    event EventHandler? Changed;

    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the background startup + periodic full probe (see <c>docs/SYSTEM_HEALTH.md</c> §2.6).
    /// Idempotent. No-op for stubs that do not schedule work.
    /// </summary>
    void StartPeriodicRefresh();
}
