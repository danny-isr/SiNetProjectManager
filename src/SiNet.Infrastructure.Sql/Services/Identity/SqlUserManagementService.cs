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
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserProfileService? _currentUserProfile;

    public SqlUserManagementService(
        IDbContextFactory<SiNetDbContext> dbFactory,
        IAuthorizationQueryService authorization,
        ICurrentUserContext currentUser,
        ICurrentUserProfileService? currentUserProfile = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _currentUserProfile = currentUserProfile;
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
                MasterPlanEmployeeId: u.MasterPlanEmployeeId,
                Notes: u.Notes))
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
    public async Task UpdateUsersAsync(
        IReadOnlyList<UpdateUserCommand> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            return;
        }

        await RequireUsersManageAsync(cancellationToken).ConfigureAwait(false);
        await EnforceSelfProtectionAsync(updates, cancellationToken).ConfigureAwait(false);
        await EnforceUniqueLoginNamesAsync(updates, cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var ids = updates.Select(u => u.UserId).ToList();
        var entities = await context.Users
            .Where(u => ids.Contains(u.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var missingIds = ids.Except(entities.Select(e => e.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot update missing users (IDs: {string.Join(", ", missingIds)}).");
        }

        var changed = false;
        foreach (var entity in entities)
        {
            var update = updates.First(u => u.UserId == entity.Id);
            var itemChanged = entity.Name != NormalizeOptional(update.DisplayName)
                || entity.Email != NormalizeOptional(update.Email)
                || entity.LoginName != NormalizeOptional(update.LoginName)
                || entity.AccUserType != (int)update.AccUserType
                || entity.Role != (int)update.Role
                || entity.IsActive != update.IsActive
                || entity.MasterPlanEmployeeId != update.MasterPlanEmployeeId
                || entity.Notes != NormalizeOptional(update.Notes);

            if (!itemChanged)
            {
                continue;
            }

            entity.Name = NormalizeOptional(update.DisplayName);
            entity.Email = NormalizeOptional(update.Email);
            entity.LoginName = NormalizeOptional(update.LoginName);
            entity.AccUserType = (int)update.AccUserType;
            entity.Role = (int)update.Role;
            entity.IsActive = update.IsActive;
            entity.MasterPlanEmployeeId = update.MasterPlanEmployeeId;
            entity.Notes = NormalizeOptional(update.Notes);
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
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

    private async Task EnforceSelfProtectionAsync(
        IReadOnlyList<UpdateUserCommand> updates,
        CancellationToken cancellationToken)
    {
        var currentUserId = _currentUser.UserId;
        if (currentUserId is null)
        {
            // Preview hosts without ICurrentUserContext binding cannot enforce self-protection.
            // See IUserManagementService remarks — callers must bind a real user in production hosts.
            return;
        }

        var selfUpdate = updates.FirstOrDefault(u => u.UserId == currentUserId.Value);
        if (selfUpdate is null)
        {
            return;
        }

        if (!selfUpdate.IsActive)
        {
            throw new InvalidOperationException(
                "Cannot deactivate your own account. Another administrator must perform this action.");
        }

        if (selfUpdate.Role < AppRole.Administrator)
        {
            throw new InvalidOperationException(
                "Cannot demote your own role below Administrator. Another administrator must perform this action.");
        }

        var currentLoginName = await ResolveCurrentLoginNameAsync(cancellationToken).ConfigureAwait(false);
        if (selfUpdate.LoginName != null
            && currentLoginName != null
            && !string.Equals(selfUpdate.LoginName, currentLoginName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Cannot change your own LoginName. This would break your next login. " +
                "Another administrator must perform this action.");
        }
    }

    private async Task<string?> ResolveCurrentLoginNameAsync(CancellationToken cancellationToken)
    {
        if (_currentUserProfile is not null)
        {
            var profile = await _currentUserProfile
                .GetCurrentUserAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(profile?.LoginName))
            {
                return profile!.LoginName;
            }
        }

        if (_currentUser.UserId is not int userId)
        {
            return null;
        }

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.LoginName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnforceUniqueLoginNamesAsync(
        IReadOnlyList<UpdateUserCommand> updates,
        CancellationToken cancellationToken)
    {
        var pending = updates
            .Where(u => !string.IsNullOrWhiteSpace(u.LoginName))
            .Select(u => new { u.UserId, LoginName = u.LoginName!.Trim() })
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await context.Users
            .AsNoTracking()
            .Where(u => u.LoginName != null)
            .Select(u => new { u.Id, u.LoginName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var update in pending)
        {
            var duplicate = existing.Any(u =>
                u.Id != update.UserId
                && u.LoginName != null
                && string.Equals(u.LoginName, update.LoginName, StringComparison.OrdinalIgnoreCase));

            if (duplicate)
            {
                throw new InvalidOperationException($"Login name '{update.LoginName}' already exists.");
            }
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
