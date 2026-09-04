namespace SiNet.Application.Identity;

/// <summary>Outcome of Windows/runtime → SIUser resolution at startup (or refresh).</summary>
public enum WindowsUserAuthStatus
{
    /// <summary>Active SIUser with Role ≥ Employee — normal shell + identity coherence.</summary>
    Authorized = 0,

    /// <summary>Active SIUser with Role = Unauthorized (existing or just auto-created) — restricted shell.</summary>
    PendingApproval = 1,

    /// <summary>Inactive SIUser or hard failure — do not open the application.</summary>
    Blocked = 2,
}

/// <summary>Result of <see cref="IWindowsCurrentUserAuthenticator.AuthenticateAsync"/>.</summary>
public sealed record WindowsUserAuthenticationResult(
    WindowsUserAuthStatus Status,
    CurrentUserProfileDto? Profile,
    string? FailureReason = null);
