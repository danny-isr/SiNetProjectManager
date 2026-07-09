using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email.Detail;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Detail;

internal sealed class SqlEmailMoveToProjectEligibilityService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IEmailMoveToProjectEligibilityService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<EmailMoveToProjectEligibility> EvaluateAsync(
        EmailMoveToProjectEligibilityQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.InboxMessageId <= 0)
        {
            return Block("בחר מייל.");
        }

        if (query.ProjectId <= 0)
        {
            return Block("בחר פרויקט.");
        }

        if (!query.IsEmailFiledToProject)
        {
            return Block("המייל לא משויך לפרויקט.");
        }

        if (query.AttachmentCount <= 0)
        {
            return Allow();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var attachments = await db.EmailInboxAttachments
            .AsNoTracking()
            .Where(a => a.MessageId == query.InboxMessageId)
            .Select(a => new { a.ProjectFileId, a.AccItemId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (attachments.Count == 0)
        {
            return Allow();
        }

        var untagged = attachments.Count(a => a.ProjectFileId is null or <= 0);
        if (untagged > 0)
        {
            return Block($"נותרו {untagged} צרופות לא מתויגות.");
        }

        var unplaced = attachments.Count(a => string.IsNullOrWhiteSpace(a.AccItemId));
        if (unplaced == attachments.Count)
        {
            return Block("שירות ה-ACC (Ingestion) אינו זמין.");
        }

        if (attachments.All(a => !string.IsNullOrWhiteSpace(a.AccItemId)))
        {
            // All tagged and in ACC — move is allowed unless already placed (handled by move service).
        }

        return Allow();
    }

    private static EmailMoveToProjectEligibility Allow() => new(true, null);

    private static EmailMoveToProjectEligibility Block(string reason) => new(false, reason);
}
