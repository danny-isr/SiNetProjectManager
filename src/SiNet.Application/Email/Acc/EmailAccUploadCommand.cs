namespace SiNet.Application.Email.Acc;

/// <summary>
/// Explicit user-initiated ACC inbox upload. Gmail ids are runtime mailbox identifiers only.
/// </summary>
/// <param name="AllowZeroAttachmentIngest">
/// N4.3: when true, messages with no Gmail attachments may still create ACC Inbox + body PDF
/// (mailbox-filed to a project, recovery, or explicit post-File ingest).
/// </param>
public sealed record EmailAccUploadCommand(
    string GmailMessageId,
    string GmailThreadId,
    string? InternetMessageId,
    string ActingUserLogin,
    bool AllowZeroAttachmentIngest = false);
