namespace SiNet.Application.Identity;

/// <summary>Read-only active user lookup for dropdowns (Task Workbench scope selector, etc.).</summary>
public interface IUserLookupService
{
    /// <summary>Returns active users ordered by display name, then id.</summary>
    Task<IReadOnlyList<UserLookupDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default);
}
