using System.IO;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;
using SiNetSQL.Services;
using SiNetSQL.Services.EmailIngestion;
using SiOffice.GoogleConnector;
using SiOffice.GoogleConnector.Logging;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host bridge: delegates ACC inbox upload to legacy <see cref="EmailIngestionService"/>.
/// </summary>
internal sealed class LegacyEmailAccIngestionExecutor(
    GoogleService googleService,
    IEmailIngestionServiceFactory ingestionFactory,
    IGoogleIngestSessionEnsurer sessionEnsurer,
    IEmailPdfRenderer? pdfRenderer = null) : IEmailAccIngestionExecutor
{
    private const string AccIngestAuthFailureMessage = "לא ניתן להעלות ל-ACC — התחברות Gmail (legacy) נדרשת";

    private readonly GoogleService _googleService =
        googleService ?? throw new ArgumentNullException(nameof(googleService));
    private readonly IEmailIngestionServiceFactory _ingestionFactory =
        ingestionFactory ?? throw new ArgumentNullException(nameof(ingestionFactory));
    private readonly IGoogleIngestSessionEnsurer _sessionEnsurer =
        sessionEnsurer ?? throw new ArgumentNullException(nameof(sessionEnsurer));
    private readonly IEmailPdfRenderer? _pdfRenderer = pdfRenderer;

    public async Task<EmailAccUploadResult> IngestToInboxAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var messageUniqueId = EmailMessageIdentity.GetMessageUniqueId(
            command.InternetMessageId,
            command.GmailMessageId);

        if (!await _sessionEnsurer.EnsureAuthenticatedForAccIngestAsync(cancellationToken).ConfigureAwait(false))
        {
            return new EmailAccUploadResult(
                EmailAccUploadOutcome.Failed,
                messageUniqueId,
                null,
                0,
                0,
                AccIngestAuthFailureMessage,
                0);
        }

        await EnsurePdfRendererReadyAsync().ConfigureAwait(false);

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
                var authMessage = EmailAccIngestGates.MapAuthFailureMessage(ex.Message);
                return new EmailAccUploadResult(
                    EmailAccUploadOutcome.Failed,
                    messageUniqueId,
                    null,
                    0,
                    0,
                    authMessage ?? ex.Message,
                    0);
            }

            var result = await ingestion
                .IngestToInboxAsync(email, command.ActingUserLogin, cancellationToken)
                .ConfigureAwait(false);

            // #region agent log
            try
            {
                var dbg = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sessionId = "487a8a",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    location = "LegacyEmailAccIngestionExecutor.cs:IngestToInboxAsync",
                    message = "ingest completed",
                    hypothesisId = "H-E",
                    data = new { outcome = result.Status.ToString(), threadId = Environment.CurrentManagedThreadId },
                });
                File.AppendAllText(@"d:\repos2026\debug-487a8a.log", dbg + Environment.NewLine);
            }
            catch { }
            // #endregion

            return MapResult(result);
        }
    }

    private async Task EnsurePdfRendererReadyAsync()
    {
        if (_pdfRenderer is WebView2PdfRenderer webRenderer && !webRenderer.IsAvailable)
        {
            ReportLogger.Warn("[AccIngest] PDF renderer not ready — continuing attachment ingest without body PDF.");
            try
            {
                if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
                {
                    await dispatcher.InvokeAsync(webRenderer.InitializeAsync).Task.ConfigureAwait(false);
                }
                else
                {
                    await webRenderer.InitializeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ReportLogger.Warn($"[AccIngest] PDF renderer init failed (non-fatal): {ex.Message}");
            }
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
