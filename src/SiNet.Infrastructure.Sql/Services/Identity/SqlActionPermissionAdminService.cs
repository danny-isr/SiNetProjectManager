using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;
using SiNet.Infrastructure.Sql.Entities;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Native New System implementation of <see cref="IActionPermissionAdminService"/>.
/// </summary>
public sealed class SqlActionPermissionAdminService : IActionPermissionAdminService
{
    private readonly IDbContextFactory<SiNetDbContext> _dbFactory;
    private readonly IAuthorizationQueryService _authorization;

    public SqlActionPermissionAdminService(
        IDbContextFactory<SiNetDbContext> dbFactory,
        IAuthorizationQueryService authorization)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActionPermissionAssigneeDto>> GetAssignableUsersAsync(
        CancellationToken cancellationToken = default)
    {
        await RequireActionPermissionsManageAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await context.Users
            .AsNoTracking()
            .Where(u => u.IsActive
                        && u.Email != null
                        && u.Email != string.Empty
                        && u.Role >= (int)AppRole.Employee)
            .OrderBy(u => u.Name)
            .Select(u => new ActionPermissionAssigneeDto(
                u.Id,
                u.Name ?? "(ללא שם)",
                u.Email))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlySet<int>>> GetActivePermissionsByActionAsync(
        CancellationToken cancellationToken = default)
    {
        await RequireActionPermissionsManageAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await context.ActionPermissions
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.ActionCode, p.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var map = new Dictionary<string, IReadOnlySet<int>>(StringComparer.Ordinal);
        foreach (var entry in ActionPermissionCatalog.All)
        {
            map[entry.ActionCode] = new HashSet<int>();
        }

        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.ActionCode, out var set))
            {
                set = new HashSet<int>();
                map[row.ActionCode] = set;
            }

            if (set is HashSet<int> mutable)
            {
                mutable.Add(row.UserId);
            }
        }

        return map;
    }

    /// <inheritdoc />
    public Task SaveActionPermissionsAsync(
        string actionCode,
        IReadOnlySet<int> authorizedUserIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionCode);
        ArgumentNullException.ThrowIfNull(authorizedUserIds);

        return SaveAllActionPermissionsAsync(
            new Dictionary<string, IReadOnlySet<int>>(StringComparer.Ordinal)
            {
                [actionCode] = authorizedUserIds,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAllActionPermissionsAsync(
        IReadOnlyDictionary<string, IReadOnlySet<int>> permissionsByActionCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissionsByActionCode);
        await RequireActionPermissionsManageAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (actionCode, desiredUserIds) in permissionsByActionCode)
        {
            if (!ActionPermissionCodes.IsKnownActionCode(actionCode))
            {
                throw new ArgumentException($"Unknown action code '{actionCode}'.", nameof(permissionsByActionCode));
            }

            await ValidateDesiredUsersAsync(context, desiredUserIds, cancellationToken).ConfigureAwait(false);

            var displayName = ActionPermissionCatalog.GetDisplayName(actionCode) ?? actionCode;

            var existingRows = await context.ActionPermissions
                .Where(p => p.ActionCode == actionCode)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var currentActiveUserIds = existingRows
                .Where(r => r.IsActive)
                .Select(r => r.UserId)
                .ToHashSet();

            var desiredSet = desiredUserIds.ToHashSet();
            var toAdd = desiredSet.Except(currentActiveUserIds).ToList();
            var toRemove = currentActiveUserIds.Except(desiredSet).ToList();

            foreach (var userId in toAdd)
            {
                var existingInactive = existingRows.FirstOrDefault(r => r.UserId == userId && !r.IsActive);
                if (existingInactive != null)
                {
                    existingInactive.IsActive = true;
                    existingInactive.ActionDisplayName = displayName;
                }
                else
                {
                    context.ActionPermissions.Add(new ActionPermissionEntity
                    {
                        ActionCode = actionCode,
                        ActionDisplayName = displayName,
                        UserId = userId,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                    });
                }
            }

            foreach (var userId in toRemove)
            {
                var row = existingRows.First(r => r.UserId == userId && r.IsActive);
                row.IsActive = false;
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateDesiredUsersAsync(
        SiNetDbContext context,
        IReadOnlySet<int> desiredUserIds,
        CancellationToken cancellationToken)
    {
        if (desiredUserIds.Count == 0)
        {
            return;
        }

        var validUserIds = await context.Users
            .AsNoTracking()
            .Where(u => desiredUserIds.Contains(u.Id)
                        && u.IsActive
                        && u.Role >= (int)AppRole.Employee)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var invalidIds = desiredUserIds.Except(validUserIds).ToList();
        if (invalidIds.Count > 0)
        {
            throw new ArgumentException(
                $"Cannot grant permissions to invalid users (IDs: {string.Join(", ", invalidIds)}). " +
                "Users must exist, be active, and have Role >= Employee.");
        }
    }

    private async Task RequireActionPermissionsManageAsync(CancellationToken cancellationToken)
    {
        var allowed = await _authorization
            .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.ActionPermissionsManage, cancellationToken)
            .ConfigureAwait(false);

        if (!allowed)
        {
            throw new UnauthorizedAccessException(
                "ActionPermissions.Manage (Administrator) is required for this operation.");
        }
    }
}
