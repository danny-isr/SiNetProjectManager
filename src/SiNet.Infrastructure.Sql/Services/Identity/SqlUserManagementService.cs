using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;
using SiNet.Infrastructure.Sql.Entities;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Native New System implementation of <see cref="IUserManagementService"/> — EF/SQL only, no WPF,
/// no SiNetSQL project references (see <c>docs/NEW_SYSTEM_BOUNDARY.md</c>).
/// </summary>
public sealed class SqlUserManagementService : IUserManagementService
{
    private readonly IDbContextFactory<SiNetDbContext> _dbFactory;
    private readonly IAuthorizationQueryService _authorization;

    public SqlUserManagementService(
        IDbContextFactory<SiNetDbContext> dbFactory,
        IAuthorizationQueryService authorization)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummaryDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await RequireUsersManageAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var users = await context.Users
            .AsNoTracking()
            .OrderBy(u => u.Name)
            .Select(u => new UserSummaryDto(
                UserId: u.Id,
                DisplayName: u.Name ?? string.Empty,
                Email: u.Email ?? string.Empty,
                LoginName: u.LoginName ?? string.Empty,
                IsDomainGroup: u.IsDomainGroup,
                IsActive: u.IsActive,
                AccUserType: (AppAccUserType)u.AccUserType,
                Role: (AppRole)u.Role,
                OpenTaskCount: context.ProjectAssignments.Count(pa =>
                    pa.AssignedToId == u.Id
                    && pa.StatusId != null
                    && pa.AssignmentStatus!.IsOpen),
                MasterPlanEmployeeId: u.MasterPlanEmployeeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return users;
    }

    /// <inheritdoc />
    public async Task AddUserAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireUsersManageAsync(cancellationToken).ConfigureAwait(false);

        var loginName = command.LoginName?.Trim();
        if (string.IsNullOrWhiteSpace(loginName))
        {
            throw new ArgumentException("LoginName is required.", nameof(command));
        }

        if (await CheckDuplicateLoginNameAsync(loginName, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Login name '{loginName}' already exists.");
        }

        var user = new SiUserEntity
        {
            LoginName = loginName,
            Name = string.IsNullOrWhiteSpace(command.DisplayName) ? null : command.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim(),
            Role = (int)command.Role,
            AccUserType = (int)command.AccUserType,
            IsActive = command.IsActive,
            IsDomainGroup = command.IsDomainGroup,
            MasterPlanEmployeeId = command.MasterPlanEmployeeId,
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim(),
        };

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UpdateUsersAsync(
        IReadOnlyList<UpdateUserCommand> updates,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "Native user updates are not implemented in this slice. " +
            "Use the Legacy startup path for inline editing until Infrastructure.Sql gains UpdateUsersAsync.");
    }

    /// <inheritdoc />
    public async Task<bool> CheckDuplicateLoginNameAsync(
        string loginName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loginName);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var normalized = loginName.Trim().ToLowerInvariant();
        return await context.Users
            .AsNoTracking()
            .AnyAsync(
                u => u.LoginName != null && u.LoginName.ToLower() == normalized,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetExistingLoginNamesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var logins = await context.Users
            .AsNoTracking()
            .Where(u => u.LoginName != null)
            .Select(u => u.LoginName!.ToLower())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new HashSet<string>(logins, StringComparer.OrdinalIgnoreCase);
    }

    private async Task RequireUsersManageAsync(CancellationToken cancellationToken)
    {
        var allowed = await _authorization
            .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.UsersManage, cancellationToken)
            .ConfigureAwait(false);

        if (!allowed)
        {
            throw new UnauthorizedAccessException(
                "Users.Manage (Administrator) is required for this operation.");
        }
    }
}
