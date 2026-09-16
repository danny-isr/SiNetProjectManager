using Microsoft.EntityFrameworkCore;
using SiNet.Application.ProjectIntelligence;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.ProjectIntelligence;

/// <summary>
/// Confirmed filing cache only: inbox Subject where ProjectId equals ThreadStatusMapping Assigned
/// and is not the office-default project. No Gmail crawl. No backfill predictions.
/// </summary>
public sealed class SqlConfirmedProjectSubjectSource(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IConfirmedProjectSubjectSource
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<IReadOnlyList<ConfirmedProjectSubject>> LoadAsync(
        int officeDefaultProjectId,
        CancellationToken cancellationToken = default)
    {
        if (officeDefaultProjectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(officeDefaultProjectId));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (
                from inbox in db.EmailInboxMessages.AsNoTracking()
                join mapping in db.ThreadStatusMappings.AsNoTracking()
                    on inbox.ThreadUniqueId equals mapping.ThreadUniqueId
                where inbox.ProjectId != officeDefaultProjectId
                      && inbox.ProjectId == mapping.ProjectId
                      && mapping.Status == ThreadMappingStatus.Assigned
                      && inbox.Subject != null
                      && inbox.Subject != string.Empty
                select new ConfirmedProjectSubject(
                    inbox.ProjectId,
                    inbox.Subject!,
                    inbox.ReceivedUtc,
                    inbox.ThreadUniqueId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }
}
