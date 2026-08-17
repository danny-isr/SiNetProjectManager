using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Identity;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email;

public sealed class SqlUserMailViewPreferencesService(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    ICurrentUserContext currentUser,
    IAppLogger logger) : IUserMailViewPreferencesService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly ICurrentUserContext _currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly IAppLogger _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<UserMailViewPreferences> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not int userId || userId <= 0)
            return UserMailViewPreferences.Default;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SiuserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return UserMailViewPreferences.Default;

        return UserMailViewPreferencesMapper.FromStored(
            row.GmailMailScope,
            row.GmailMailCategory,
            row.GmailUnreadOnly);
    }

    public async Task SaveAsync(UserMailViewPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (_currentUser.UserId is not int userId || userId <= 0)
        {
            _logger.Warn("[GmailMailPrefs] Save skipped — no current user id.");
            return;
        }

        var (scope, category, unreadOnly) = UserMailViewPreferencesMapper.ToStored(preferences);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.UserSettings
            .FirstOrDefaultAsync(s => s.SiuserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new UserSetting
            {
                SiuserId = userId,
                AutoOpenTasksPanelAfterFiling = true,
            };
            db.UserSettings.Add(row);
        }

        row.GmailMailScope = scope;
        row.GmailMailCategory = category;
        row.GmailUnreadOnly = unreadOnly;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
