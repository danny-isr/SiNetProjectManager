namespace SiNet.Application.Email.Detail;

public interface IEmailAccIngestionService
{
    Task<EmailAccIngestionResult> IngestToInboxAsync(
        EmailAccIngestionCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record EmailAccIngestionCommand(
    string GmailMessageId,
    string? InternetMessageId,
    string ActingUserLogin,
    int AttachmentCount);

public sealed record EmailAccIngestionResult(
    bool Succeeded,
    string MessageUniqueId,
    int? InboxMessageId,
    string? ErrorMessage,
    bool InProgress);
