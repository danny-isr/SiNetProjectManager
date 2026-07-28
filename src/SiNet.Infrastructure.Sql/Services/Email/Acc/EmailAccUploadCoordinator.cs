using SiNet.Application.Email.Acc;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

/// <summary>
/// ACC inbox upload orchestration. <see cref="IEmailAccIngestionExecutor"/> is host-supplied
/// (V2 legacy bridge); when absent (standalone New System), uploads return BackendNotAvailable.
/// </summary>
internal sealed class EmailAccUploadCoordinator(
    IEmailAccStatusService statusService,
    EmailAccInboxQueryService inboxQuery,
    IEmailAccIngestionExecutor? ingestionExecutor = null)
    : IEmailAccUploadCoordinator
{
    private readonly IEmailAccIngestionExecutor? _ingestionExecutor = ingestionExecutor;
    private readonly IEmailAccStatusService _statusService =
        statusService ?? throw new ArgumentNullException(nameof(statusService));
    private readonly EmailAccInboxQueryService _inboxQuery =
        inboxQuery ?? throw new ArgumentNullException(nameof(inboxQuery));

    public async Task<EmailAccUploadResult> UploadToAccInboxAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_ingestionExecutor is null)
        {
            var messageUniqueId = EmailAccStatusMapper.ResolveMessageUniqueId(
                command.InternetMessageId,
                command.GmailMessageId);
            return EmailAccUploadResult.BackendNotAvailable(messageUniqueId);
        }

        return await _ingestionExecutor.IngestToInboxAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EmailAccInboxStatus?> WaitForCompletionAsync(
        string messageUniqueId,
        string? currentUserLogin,
        TimeSpan pollInterval,
        int maxAttempts,
        Func<bool> shouldContinue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shouldContinue);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!shouldContinue())
            {
                return null;
            }

            if (await _inboxQuery.HasUploadedAttachmentsAsync(messageUniqueId, cancellationToken)
                .ConfigureAwait(false))
            {
                var cache = await _inboxQuery.GetByMessageUniqueIdAsync(messageUniqueId, cancellationToken)
                    .ConfigureAwait(false);
                if (cache?.Id is int inboxMessageId and > 0)
                {
                    return await _statusService
                        .GetStatusByInboxMessageIdAsync(inboxMessageId, currentUserLogin, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }
}
