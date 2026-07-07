namespace SiNet.Application.Common;

/// <summary>
/// Authentication port shared by external connectors (Google, Autodesk).
/// Implementations live in the relevant <c>SiNet.Infrastructure.*</c> project
/// or temporarily in <c>SiNet.LegacyBridge</c>.
/// </summary>
public interface IConnectorAuthService
{
    bool IsAuthenticated { get; }

    /// <summary>Best-effort connected account email; null when unknown or not authenticated.</summary>
    string? ConnectedAccountEmail { get; }

    Task<bool> LoginAsync(ConnectorLoginOptions? options = null, CancellationToken cancellationToken = default);

    void Logout();

    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Refreshes <see cref="ConnectedAccountEmail"/> after login or silent restore.</summary>
    Task RefreshAccountProfileAsync(CancellationToken cancellationToken = default);

    event Action<bool>? AuthStateChanged;
}
