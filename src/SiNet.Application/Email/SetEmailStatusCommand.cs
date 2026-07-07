using SiNet.Application.Abstractions.Email;

namespace SiNet.Application.Email;

public sealed record SetEmailStatusCommand(
    string GmailMessageId,
    string? GmailThreadId,
    EmailTriageStatus Status,
    int ActingUserId,
    int? InboxMessageId = null,
    string? ThreadUniqueId = null);

public sealed record EmailStatusResult(
    bool Succeeded,
    string? ErrorMessage = null);
