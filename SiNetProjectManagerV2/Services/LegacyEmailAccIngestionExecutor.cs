using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiOffice.GoogleConnector;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailIngestion;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host bridge: delegates ACC inbox upload to legacy <see cref="EmailIngestionService"/>.
/// </summary>
internal sealed class LegacyEmailAccIngestionExecutor(
    GoogleService googleService,
    IEmailIngestionServiceFactory ingestionFactory,
    IEmailPdfRenderer? pdfRenderer = null) : IEmailAccIngestionExecutor
{
    private readonly GoogleService _googleService =
        googleService ?? throw new ArgumentNullException(nameof(googleService));
    private readonly IEmailIngestionServiceFactory _ingestionFactory =
        ingestionFactory ?? throw new ArgumentNullException(nameof(ingestionFactory));
    private readonly IEmailPdfRenderer? _pdfRenderer = pdfRenderer;

    public async Task<EmailAccUploadResult> IngestToInboxAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var messageUniqueId = EmailMessageIdentity.GetMessageUniqueId(
            command.InternetMessageId,
            command.GmailMessageId);

        if (_pdfRenderer is not null)
        {
            _ingestionFactory.SetPdfRenderer(_pdfRenderer);
        }

        var ingestion = await _ingestionFactory
            .CreateAsync(_googleService)
            .ConfigureAwait(false);

        if (ingestion is null)
        {
            return EmailAccUploadResult.BackendNotAvailable(messageUniqueId);
        }

        using (ingestion)
        {
            EmailInfo email;
            try
            {
                email = await _googleService
                    .LoadFullEmailBodyAsync(command.GmailMessageId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new EmailAccUploadResult(
                    EmailAccUploadOutcome.Failed,
                    messageUniqueId,
                    null,
                    0,
                    0,
                    ex.Message,
                    0);
            }

            var result = await ingestion
                .IngestToInboxAsync(email, command.ActingUserLogin, cancellationToken)
                .ConfigureAwait(false);

            return MapResult(result);
        }
    }

    private static EmailAccUploadResult MapResult(IngestionResult result) =>
        new(
            MapOutcome(result.Status),
            result.MessageUniqueId,
            result.MessageDbId,
            result.AttachmentsUploaded,
            result.TotalAttachments,
            result.ErrorMessage,
            result.DurationMs);

    private static EmailAccUploadOutcome MapOutcome(IngestionResultStatus status) => status switch
    {
        IngestionResultStatus.Success => EmailAccUploadOutcome.Succeeded,
        IngestionResultStatus.AlreadyProcessed => EmailAccUploadOutcome.AlreadyProcessed,
        IngestionResultStatus.InProgress => EmailAccUploadOutcome.InProgress,
        IngestionResultStatus.SkippedNoAttachments => EmailAccUploadOutcome.SkippedNoAttachments,
        IngestionResultStatus.SkippedNotRelevant => EmailAccUploadOutcome.SkippedNotRelevant,
        _ => EmailAccUploadOutcome.Failed,
    };
}
