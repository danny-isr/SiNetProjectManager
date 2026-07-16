namespace SiNet.Application.Email;

/// <summary>
/// Read port for inbox rows stored in SQL. Used when a task supplies
/// <c>PrimaryWorkTargetEntityId</c> as an <c>EmailInboxMessage</c> id.
/// </summary>
public interface IEmailInboxQueryService
{
    /// <summary>Loads one inbox message by primary key, or <see langword="null"/> when missing.</summary>
    Task<EmailInboxMessageDto?> GetByIdAsync(int inboxMessageId, CancellationToken cancellationToken = default);

    /// <summary>Loads inbox attachments for preview / ACC open (ordered by index).</summary>
    Task<IReadOnlyList<EmailInboxAttachmentViewDto>> GetAttachmentsAsync(
        int inboxMessageId,
        CancellationToken cancellationToken = default);
}
