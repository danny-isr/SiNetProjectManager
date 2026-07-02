namespace SiNet.Application.Identity;

/// <summary>
/// User management port for the New System (see <c>docs/IDENTITY_AND_PERMISSIONS.md</c> §7.5).
/// Wraps legacy <c>UserService</c> semantics: Administrator-only writes, self-protection on update,
/// ACC reconciliation after changes. Does not expose Active Directory lookup — that stays in legacy UI.
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Returns all users ordered by display name, including open task counts per user.
    /// </summary>
    Task<IReadOnlyList<UserSummaryDto>> GetUsersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new user. Throws <see cref="UnauthorizedAccessException"/> when the current caller is not Administrator.
    /// </summary>
    Task AddUserAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates one or more users. Throws when caller is not Administrator or self-protection rules are violated.
    /// </summary>
    Task UpdateUsersAsync(
        IReadOnlyList<UpdateUserCommand> updates,
        CancellationToken cancellationToken = default);
}
