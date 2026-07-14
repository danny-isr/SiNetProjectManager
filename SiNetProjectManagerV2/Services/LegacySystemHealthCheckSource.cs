using SiNet.Application.Runtime;
using SiNetSQL.Services.Health;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Adapts legacy <see cref="ISystemHealthService"/> into Application <see cref="IExternalHealthCheckSource"/>.
/// </summary>
internal sealed class LegacySystemHealthCheckSource : IExternalHealthCheckSource, IDisposable
{
    private readonly ISystemHealthService _health;

    public LegacySystemHealthCheckSource(ISystemHealthService health)
    {
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _health.StatusChanged += OnStatusChanged;
    }

    public IReadOnlyList<ExternalHealthCheckSnapshot> Current =>
        _health.Current.Values
            .Select(Map)
            .OrderBy(s => s.DisplayNameHe, StringComparer.Ordinal)
            .ToList();

    public event EventHandler? Changed;

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        _health.RefreshAllAsync(cancellationToken);

    public void Dispose() => _health.StatusChanged -= OnStatusChanged;

    private void OnStatusChanged(ServiceHealthStatus _) =>
        Changed?.Invoke(this, EventArgs.Empty);

    private static ExternalHealthCheckSnapshot Map(ServiceHealthStatus s) =>
        new(
            s.Key,
            string.IsNullOrWhiteSpace(s.DisplayName) ? s.Key : s.DisplayName,
            MapState(s.State),
            string.IsNullOrWhiteSpace(s.Message) ? s.State.ToString() : s.Message,
            s.LastCheckedUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(s.LastCheckedUtc.Value, DateTimeKind.Utc))
                : null);

    private static SubsystemRuntimeState MapState(ServiceHealthState state) => state switch
    {
        ServiceHealthState.Online => SubsystemRuntimeState.Idle,
        ServiceHealthState.Checking => SubsystemRuntimeState.Running,
        ServiceHealthState.Warning => SubsystemRuntimeState.Degraded,
        ServiceHealthState.Offline => SubsystemRuntimeState.Stopped,
        ServiceHealthState.RequiresAuthorization => SubsystemRuntimeState.Stopped,
        ServiceHealthState.NotConfigured => SubsystemRuntimeState.NotConfigured,
        _ => SubsystemRuntimeState.NotConfigured,
    };
}
