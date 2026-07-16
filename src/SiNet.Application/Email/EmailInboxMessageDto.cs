namespace SiNet.Application.Email;

/// <summary>
/// Read-only projection of an <c>EmailInboxMessage</c> row for work-surface navigation.
/// </summary>
public sealed record EmailInboxMessageDto(
    int Id,
    int ProjectId,
    string? Subject,
    string? FromAddress,
    DateTime ReceivedUtc,
    string MessageUniqueId,
    string InternetMessageId,
    string? InboxAccProjectId = null,
    string? InboxAccFolderId = null);
