namespace SiNet.Application.Email;

/// <summary>
/// Read port for inbox rows stored in SQL. Used when a task supplies
/// <c>PrimaryWorkTargetEntityId</c> as an <c>EmailInboxMessage</c> id.
/// </summary>
public interface IEmailInboxQueryService
{
    /// <summary>Loads one inbox message by primary key, or <see langword="null"/> when missing.</summary>
    Task<EmailInboxMessageDto?> GetByIdAsync(int inboxMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one inbox message by RFC 2822 / Gmail-derived message identity, or
    /// <see langword="null"/> when missing. Used when a Gmail list row has no
    /// <c>InboxMessageId</c> yet but the SQL row already exists for that exact message.
    /// </summary>
    Task<EmailInboxMessageDto?> FindByMessageIdentityAsync(
        string? internetMessageId,
        string? gmailMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>Loads inbox attachments for preview / ACC open (ordered by index).</summary>
    Task<IReadOnlyList<EmailInboxAttachmentViewDto>> GetAttachmentsAsync(
        int inboxMessageId,
        CancellationToken cancellationToken = default);
}
