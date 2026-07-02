using SiNet.Application.Identity;
using SiNetSQL.Models;
using SiNetSQL.Services.Users;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host adapter: maps legacy <see cref="IUserService"/> to the clean <see cref="IUserManagementService"/>
/// port. WPF and NewShell must not reference <see cref="IUserService"/> or EF types directly.
/// </summary>
internal sealed class LegacyUserManagementService : IUserManagementService
{
    private readonly IUserService _userService;

    public LegacyUserManagementService(IUserService userService)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummaryDto>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var users = await _userService
            .GetUsersWithOpenTaskCountsAsync(cancellationToken)
            .ConfigureAwait(false);

        return users
            .Select(MapSummary)
            .ToList();
    }

    /// <inheritdoc />
    public Task AddUserAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(command);

        var user = new Siuser
        {
            LoginName = command.LoginName,
            Name = command.DisplayName,
            Email = command.Email,
            Role = MapRole(command.Role),
            AccUserType = MapAccUserType(command.AccUserType),
            IsActive = command.IsActive,
            IsDomainGroup = command.IsDomainGroup,
            MasterPlanEmployeeId = command.MasterPlanEmployeeId,
        };

        return _userService.AddUserAsync(user, cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateUsersAsync(
        IReadOnlyList<UpdateUserCommand> updates,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
        {
            return Task.CompletedTask;
        }

        var legacyUpdates = updates
            .Select(u => new UserUpdateDto
            {
                Id = u.UserId,
                Name = u.DisplayName,
                Email = u.Email,
                LoginName = u.LoginName,
                AccUserType = MapAccUserType(u.AccUserType),
                Role = MapRole(u.Role),
                IsActive = u.IsActive,
                MasterPlanEmployeeId = u.MasterPlanEmployeeId,
            })
            .ToList();

        return _userService.UpdateUsersAsync(legacyUpdates, cancellationToken);
    }

    private static UserSummaryDto MapSummary(UserWithTaskCountDto user) =>
        new(
            UserId: user.Id,
            DisplayName: user.Name,
            Email: user.Email,
            LoginName: user.LoginName,
            IsDomainGroup: user.IsDomainGroup,
            IsActive: user.IsActive,
            AccUserType: MapAccUserType(user.AccUserType),
            Role: MapRole(user.Role),
            OpenTaskCount: user.OpenTaskCount,
            MasterPlanEmployeeId: user.MasterPlanEmployeeId);

    private static AppRole MapRole(AppUserRole role) => (AppRole)(int)role;

    private static AppUserRole MapRole(AppRole role) => (AppUserRole)(int)role;

    private static AppAccUserType MapAccUserType(AccUserType accUserType) => (AppAccUserType)(int)accUserType;

    private static AccUserType MapAccUserType(AppAccUserType accUserType) => (AccUserType)(int)accUserType;
}
