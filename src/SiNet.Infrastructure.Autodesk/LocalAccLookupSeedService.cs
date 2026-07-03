using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class LocalAccLookupSeedService(IDbContextFactory<SiNetSQLDbContext> dbContextFactory) : IAccLookupSeedService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory = dbContextFactory;

    public async Task<IReadOnlyList<AccDocumentLookupSeed>> GetRecentSeedsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = await (
            from message in db.EmailInboxMessages.AsNoTracking()
            join attachment in db.EmailInboxAttachments.AsNoTracking() on message.Id equals attachment.MessageId
            where message.InboxAccProjectId != null
                  && message.InboxAccProjectId != string.Empty
                  && message.InboxAccFolderId != null
                  && message.InboxAccFolderId != string.Empty
                  && attachment.AccItemId != null
                  && attachment.AccItemId != string.Empty
                  && ((attachment.SavedFileName != null && attachment.SavedFileName != string.Empty)
                      || (attachment.OriginalFileName != null && attachment.OriginalFileName != string.Empty))
            orderby message.ReceivedUtc descending, attachment.Id descending
            select new
            {
                message.InboxAccProjectId,
                message.InboxAccFolderId,
                attachment.SavedFileName,
                attachment.OriginalFileName,
                attachment.AccItemId,
                message.ReceivedUtc
            })
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(static row => new AccDocumentLookupSeed(
                row.InboxAccProjectId!.Trim(),
                row.InboxAccFolderId!.Trim(),
                ResolveFileName(row.SavedFileName, row.OriginalFileName),
                string.IsNullOrWhiteSpace(row.AccItemId) ? null : row.AccItemId.Trim(),
                $"EmailInboxAttachment {row.ReceivedUtc:yyyy-MM-dd HH:mm}"))
            .Where(static seed =>
                seed.ProjectId.Length > 0
                && seed.FolderId.Length > 0
                && seed.FileName.Length > 0)
            .GroupBy(static seed => $"{seed.ProjectId}\n{seed.FolderId}\n{seed.FileName}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(20)
            .ToArray();
    }

    private static string ResolveFileName(string? savedFileName, string? originalFileName)
    {
        if (!string.IsNullOrWhiteSpace(savedFileName))
        {
            return savedFileName.Trim();
        }

        return originalFileName?.Trim() ?? string.Empty;
    }
}
