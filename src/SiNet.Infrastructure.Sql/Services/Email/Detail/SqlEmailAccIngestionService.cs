using SiNet.Application.Email.Acc;
using SiNet.Application.Email.Detail;
using SiNet.Infrastructure.Sql.Services.Email.Acc;

namespace SiNet.Infrastructure.Sql.Services.Email.Detail;

/// <summary>
/// Native ACC ingest port for Email Detail — delegates to the host-configured ingestion executor.
/// </summary>
internal sealed class SqlEmailAccIngestionService(IEmailAccIngestionExecutor? ingestionExecutor = null)
    : IEmailAccIngestionService
{
    private readonly IEmailAccIngestionExecutor? _ingestionExecutor = ingestionExecutor;

    public async Task<EmailAccIngestionResult> IngestToInboxAsync(
        EmailAccIngestionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_ingestionExecutor is null)
        {
            var messageUniqueId = EmailAccStatusMapper.ResolveMessageUniqueId(
                command.InternetMessageId,
                command.GmailMessageId);
            return new EmailAccIngestionResult(
                false,
                messageUniqueId,
                null,
                "ACC ingest backend is not configured.",
                InProgress: false);
        }

        var upload = await _ingestionExecutor
            .IngestToInboxAsync(
                new EmailAccUploadCommand(
                    command.GmailMessageId,
                    GmailThreadId: string.Empty,
                    command.InternetMessageId,
                    command.ActingUserLogin),
                cancellationToken)
            .ConfigureAwait(false);

        return new EmailAccIngestionResult(
            upload.Succeeded,
            upload.MessageUniqueId,
            upload.InboxMessageId,
            upload.ErrorMessage,
            upload.Outcome == EmailAccUploadOutcome.InProgress);
    }
}
