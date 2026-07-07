namespace SiNet.Application.Email.Acc;

/// <summary>
/// Host-provided backend that performs ACC inbox ingestion using the legacy pipeline.
/// Registered by the V2 host — not by the clean composition root alone.
/// </summary>
public interface IEmailAccIngestionExecutor
{
    Task<EmailAccUploadResult> IngestToInboxAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken = default);
}
