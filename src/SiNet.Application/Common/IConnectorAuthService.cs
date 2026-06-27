namespace SiNet.Application.Common;

/// <summary>
/// Authentication port shared by external connectors (Google, Autodesk).
/// Implementations live in the relevant <c>SiNet.Infrastructure.*</c> project
/// or temporarily in <c>SiNet.LegacyBridge</c>.
/// </summary>
public interface IConnectorAuthService
{
    bool IsAuthenticated { get; }

    Task<bool> LoginAsync(CancellationToken cancellationToken = default);

    void Logout();

    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);

    event Action<bool>? AuthStateChanged;
}
