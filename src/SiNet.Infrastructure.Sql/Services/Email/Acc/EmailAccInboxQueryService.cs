using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

internal sealed class EmailAccInboxQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<EmailInboxAccCacheRow?> GetByMessageUniqueIdAsync(
        string messageUniqueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageUniqueId))
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.EmailInboxMessages
            .AsNoTracking()
            .Where(message => message.MessageUniqueId == messageUniqueId)
            .Select(message => new EmailInboxAccCacheRow(
                message.Id,
                message.MessageUniqueId,
                message.Status,
                message.ProcessingByLogin,
                message.ProcessingStartedAtUtc,
                message.InboxAccFolderId,
                message.Attachments.Count))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EmailInboxAccCacheRow?> GetByInboxMessageIdAsync(
        int inboxMessageId,
        CancellationToken cancellationToken = default)
    {
        if (inboxMessageId <= 0)
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.EmailInboxMessages
            .AsNoTracking()
            .Where(message => message.Id == inboxMessageId)
            .Select(message => new EmailInboxAccCacheRow(
                message.Id,
                message.MessageUniqueId,
                message.Status,
                message.ProcessingByLogin,
                message.ProcessingStartedAtUtc,
                message.InboxAccFolderId,
                message.Attachments.Count))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> HasUploadedAttachmentsAsync(
        string messageUniqueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageUniqueId))
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await (
                from attachment in db.EmailInboxAttachments.AsNoTracking()
                join message in db.EmailInboxMessages.AsNoTracking() on attachment.MessageId equals message.Id
                where message.MessageUniqueId == messageUniqueId
                      && attachment.AccItemId != null
                      && attachment.AccItemId != ""
                select attachment.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAttachmentsAsync(
        string messageUniqueId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageUniqueId))
        {
            return 0;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await (
                from attachment in db.EmailInboxAttachments.AsNoTracking()
                join message in db.EmailInboxMessages.AsNoTracking() on attachment.MessageId equals message.Id
                where message.MessageUniqueId == messageUniqueId
                select attachment.Id)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EmailExternalDownloadItem>> ListExternalDownloadsAsync(
        string? internetMessageId,
        string gmailMessageId,
        CancellationToken cancellationToken = default)
    {
        var messageUniqueId = EmailAccStatusMapper.ResolveMessageUniqueId(internetMessageId, gmailMessageId);
        if (string.IsNullOrWhiteSpace(messageUniqueId))
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await (
                from attachment in db.EmailInboxAttachments.AsNoTracking()
                join message in db.EmailInboxMessages.AsNoTracking() on attachment.MessageId equals message.Id
                where message.MessageUniqueId == messageUniqueId && attachment.IsExternalDownload
                orderby attachment.AttachmentIndex
                select new EmailExternalDownloadItem(
                    attachment.OriginalFileName ?? attachment.SavedFileName ?? "קובץ",
                    attachment.AccItemId,
                    message.InboxAccFolderId,
                    attachment.IsExternalDownload))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
