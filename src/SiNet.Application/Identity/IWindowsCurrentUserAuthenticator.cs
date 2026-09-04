namespace SiNet.Application.Identity;

/// <summary>
/// Resolves the runtime/Windows identity to a <c>SIUser</c> row and binds the process session.
/// Unknown LoginName auto-registers as Pending (Unauthorized); inactive users stay Blocked.
/// </summary>
public interface IWindowsCurrentUserAuthenticator
{
    Task<WindowsUserAuthenticationResult> AuthenticateAsync(CancellationToken cancellationToken = default);
}
