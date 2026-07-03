using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Settings;

/// <summary>
/// Status color overrides and global defaults via existing tables (no schema changes).
/// </summary>
public sealed class SqlStatusColorSettingsService : IStatusColorSettingsService
{
    private const string HardFallbackColor = "#808080";

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IAuthorizationQueryService _authorization;

    public SqlStatusColorSettingsService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IAuthorizationQueryService authorization)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public async Task<IReadOnlyList<UserStatusColorEntryDto>> GetUserStatusColorsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var statuses = await context.ProjectAssignmentStatuses
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var overrides = await context.UserStatusPreferences
            .AsNoTracking()
            .Where(p => p.SiuserId == userId)
            .ToDictionaryAsync(p => p.StatusId, p => p.OverrideColorHex, cancellationToken)
            .ConfigureAwait(false);

        return statuses.Select(s =>
        {
            var hasOverride = overrides.TryGetValue(s.Id, out var overrideColor)
                              && !string.IsNullOrWhiteSpace(overrideColor);
            var defaultHex = s.DefaultColorHex ?? HardFallbackColor;
            return new UserStatusColorEntryDto(
                s.Id,
                s.Name ?? $"#{s.Id}",
                s.IsOpen,
                defaultHex,
                hasOverride ? overrideColor : null,
                hasOverride ? overrideColor! : defaultHex,
                hasOverride);
        }).ToList();
    }

    public async Task SetUserOverrideAsync(
        int userId,
        int statusId,
        string colorHex,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            return;
        }

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await context.UserStatusPreferences
            .FirstOrDefaultAsync(p => p.SiuserId == userId && p.StatusId == statusId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.UserStatusPreferences.Add(new UserStatusPreference
            {
                SiuserId = userId,
                StatusId = statusId,
                OverrideColorHex = colorHex.Trim(),
            });
        }
        else
        {
            existing.OverrideColorHex = colorHex.Trim();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveUserOverrideAsync(
        int userId,
        int statusId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await context.UserStatusPreferences
            .FirstOrDefaultAsync(p => p.SiuserId == userId && p.StatusId == statusId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            context.UserStatusPreferences.Remove(existing);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<GlobalStatusColorEntryDto>> GetGlobalStatusColorsAsync(
        CancellationToken cancellationToken = default)
    {
        await RequireSystemSettingsWriteAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await context.ProjectAssignmentStatuses
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .Select(s => new GlobalStatusColorEntryDto(
                s.Id,
                s.Name ?? $"#{s.Id}",
                s.IsOpen,
                s.DefaultColorHex ?? HardFallbackColor))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetGlobalDefaultColorAsync(
        int statusId,
        string? colorHex,
        CancellationToken cancellationToken = default)
    {
        await RequireSystemSettingsWriteAsync(cancellationToken).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var status = await context.ProjectAssignmentStatuses
            .FirstOrDefaultAsync(s => s.Id == statusId, cancellationToken)
            .ConfigureAwait(false);

        if (status is null)
        {
            return;
        }

        status.DefaultColorHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex.Trim();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireSystemSettingsWriteAsync(CancellationToken cancellationToken)
    {
        if (!await _authorization
                .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.SystemSettingsWrite, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException("Administrator access required for status color settings.");
        }
    }
}
