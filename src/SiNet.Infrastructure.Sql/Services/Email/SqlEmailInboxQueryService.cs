using Microsoft.EntityFrameworkCore;
using SiNet.Application.Email;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Email;

/// <summary>
/// Native read-only lookup for <c>EmailInboxMessage</c> rows used by task-driven email navigation.
/// </summary>
public sealed class SqlEmailInboxQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IEmailInboxQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    public async Task<EmailInboxMessageDto?> GetByIdAsync(int inboxMessageId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await db.EmailInboxMessages
            .AsNoTracking()
            .Where(message => message.Id == inboxMessageId)
            .Select(message => new EmailInboxMessageDto(
                message.Id,
                message.ProjectId,
                message.Subject,
                message.FromAddress,
                message.ReceivedUtc,
                message.MessageUniqueId,
                message.InternetMessageId,
                message.InboxAccProjectId,
                message.InboxAccFolderId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row;
    }

    public async Task<IReadOnlyList<EmailInboxAttachmentViewDto>> GetAttachmentsAsync(
        int inboxMessageId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.EmailInboxAttachments
            .AsNoTracking()
            .Where(a => a.MessageId == inboxMessageId)
            .OrderBy(a => a.AttachmentIndex)
            .Select(a => new EmailInboxAttachmentViewDto(
                a.Id,
                a.OriginalFileName ?? a.SavedFileName ?? $"קובץ #{a.AttachmentIndex}",
                a.AttachmentIndex,
                a.AccItemId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
