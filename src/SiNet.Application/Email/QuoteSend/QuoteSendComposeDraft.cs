namespace SiNet.Application.Email.QuoteSend;

public enum QuoteSendComposeMode
{
    ReplyAll = 0,
    NewCompose = 1,
}

/// <summary>Editable draft shown in the internal SendQuote compose window.</summary>
public sealed record QuoteSendComposeDraft(
    QuoteSendComposeMode Mode,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string Subject,
    string Body,
    string? ThreadId,
    string? InReplyToMessageId,
    string? SourceGmailMessageId,
    int? SourceInboxMessageId,
    string Marker,
    int ProjectId);

/// <summary>SQL-side reference to the email that opened a Proposal workflow.</summary>
public sealed record ProposalSourceEmailRef(
    int InboxMessageId,
    string? Subject,
    string? FromAddress,
    string InternetMessageId,
    string? GmailThreadId);

/// <summary>Persisted proof that a quote email was sent for a task.</summary>
public sealed record QuoteSendProof(
    int TaskId,
    string GmailMessageId,
    string? GmailThreadId,
    DateTime CreatedAtUtc);
