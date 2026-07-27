using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>EF/SQL command port for <see cref="UserGroup"/> admin surfaces.</summary>
public sealed class SqlUserGroupCommandService : IUserGroupCommandService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlUserGroupCommandService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<int> CreateGroupAsync(
        string code,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        code = RequireCode(code);
        name = RequireName(name);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (await db.UserGroups.AnyAsync(g => g.Code == code, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"קבוצה עם קוד '{code}' כבר קיימת.");

        var group = new UserGroup
        {
            Code = code,
            Name = name,
            Description = NormalizeDescription(description),
            IsActive = true,
        };

        db.UserGroups.Add(group);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return group.Id;
    }

    public async Task UpdateGroupMetadataAsync(
        int groupId,
        string code,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        code = RequireCode(code);
        name = RequireName(name);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await RequireActiveGroupAsync(db, groupId, cancellationToken).ConfigureAwait(false);

        if (await db.UserGroups.AnyAsync(g => g.Code == code && g.Id != groupId, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"קבוצה עם קוד '{code}' כבר קיימת.");

        group.Code = code;
        group.Name = name;
        group.Description = NormalizeDescription(description);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SoftDeleteGroupAsync(int groupId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await RequireActiveGroupAsync(db, groupId, cancellationToken).ConfigureAwait(false);
        group.IsActive = false;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddMemberAsync(int groupId, int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        _ = await RequireActiveGroupAsync(db, groupId, cancellationToken).ConfigureAwait(false);

        var user = await db.Siusers
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"משתמש פעיל id={userId} לא נמצא.");

        var exists = await db.UserGroupMemberships
            .AnyAsync(m => m.UserGroupId == groupId && m.SiuserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
            return;

        db.UserGroupMemberships.Add(new UserGroupMembership
        {
            UserGroupId = groupId,
            SiuserId = user.Id,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveMemberAsync(int groupId, int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.UserGroups
            .Include(g => g.Memberships)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.IsActive, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"קבוצה פעילה id={groupId} לא נמצאה.");

        var membership = group.Memberships.FirstOrDefault(m => m.SiuserId == userId);
        if (membership is null)
            return;

        db.UserGroupMemberships.Remove(membership);
        if (group.DefaultAssigneeId == userId)
            group.DefaultAssigneeId = null;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetDefaultAssigneeAsync(
        int groupId,
        int? userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var group = await db.UserGroups
            .Include(g => g.Memberships).ThenInclude(m => m.Siuser)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.IsActive, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"קבוצה פעילה id={groupId} לא נמצאה.");

        if (userId is null)
        {
            group.DefaultAssigneeId = null;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var isActiveMember = group.Memberships.Any(m =>
            m.SiuserId == userId.Value && m.Siuser is { IsActive: true });
        if (!isActiveMember)
            throw new InvalidOperationException("ברירת מחדל חייבת להיות חבר פעיל בקבוצה.");

        group.DefaultAssigneeId = userId.Value;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<UserGroup> RequireActiveGroupAsync(
        SiNetSQLDbContext db,
        int groupId,
        CancellationToken cancellationToken)
    {
        return await db.UserGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.IsActive, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"קבוצה פעילה id={groupId} לא נמצאה.");
    }

    private static string RequireCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("קוד קבוצה נדרש.", nameof(code));
        return code.Trim();
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("שם קבוצה נדרש.", nameof(name));
        return name.Trim();
    }

    private static string? NormalizeDescription(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
