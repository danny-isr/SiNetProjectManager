namespace SiNet.Application.Email.Acc;

/// <summary>
/// Explicit user-initiated ACC inbox upload. Gmail ids are runtime mailbox identifiers only.
/// </summary>
public sealed record EmailAccUploadCommand(
    string GmailMessageId,
    string GmailThreadId,
    string? InternetMessageId,
    string ActingUserLogin);
