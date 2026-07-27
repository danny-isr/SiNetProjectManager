using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>EF/SQL read port for <see cref="UserGroup"/> admin surfaces.</summary>
public sealed class SqlUserGroupQueryService : IUserGroupQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlUserGroupQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<IReadOnlyList<UserGroupSummaryDto>> GetActiveGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var groups = await db.UserGroups
            .AsNoTracking()
            .Include(g => g.Memberships).ThenInclude(m => m.Siuser)
            .Include(g => g.DefaultAssignee)
            .Where(g => g.IsActive)
            .OrderBy(g => g.Name)
            .ThenBy(g => g.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return groups.Select(MapSummary).ToList();
    }

    public async Task<UserGroupDetailDto?> GetGroupDetailAsync(
        int groupId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var group = await db.UserGroups
            .AsNoTracking()
            .Include(g => g.Memberships).ThenInclude(m => m.Siuser)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (group is null)
            return null;

        var members = group.Memberships
            .Where(m => m.Siuser is { IsActive: true })
            .Select(m => new UserGroupMemberDto(
                m.Siuser.Id,
                DisplayName(m.Siuser),
                m.Siuser.IsActive))
            .OrderBy(m => m.DisplayName)
            .ThenBy(m => m.UserId)
            .ToList();

        var stages = await LoadDependentStagesAsync(db, groupId, cancellationToken).ConfigureAwait(false);

        return new UserGroupDetailDto(
            group.Id,
            group.Code,
            group.Name,
            group.Description,
            group.DefaultAssigneeId,
            members,
            stages);
    }

    public async Task<IReadOnlyList<WorkflowStageGroupDependencyDto>> GetStagesUsingGroupAsync(
        int groupId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await LoadDependentStagesAsync(db, groupId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<WorkflowStageGroupDependencyDto>> LoadDependentStagesAsync(
        SiNetSQLDbContext db,
        int groupId,
        CancellationToken cancellationToken)
    {
        return await db.WorkflowStageDefinitions
            .AsNoTracking()
            .Where(s => s.AssignedGroupId == groupId
                        && !s.IsFinal
                        && s.WorkflowDefinition.IsActive)
            .OrderBy(s => s.WorkflowDefinition.Code)
            .ThenBy(s => s.SortOrder)
            .Select(s => new WorkflowStageGroupDependencyDto(
                s.WorkflowDefinition.Code,
                s.Code,
                s.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static UserGroupSummaryDto MapSummary(UserGroup group)
    {
        var activeCount = group.Memberships.Count(m => m.Siuser is { IsActive: true });
        return new UserGroupSummaryDto(
            group.Id,
            group.Code,
            group.Name,
            group.Description,
            group.DefaultAssigneeId,
            group.DefaultAssignee is null ? null : DisplayName(group.DefaultAssignee),
            activeCount);
    }

    private static string DisplayName(Siuser user)
        => user.Name ?? user.LoginName ?? $"User {user.Id}";
}
