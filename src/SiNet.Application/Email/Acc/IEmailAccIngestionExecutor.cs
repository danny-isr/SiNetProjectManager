namespace SiNet.Application.Email.Acc;

/// <summary>
/// Host-provided backend that performs ACC inbox ingestion.
/// Standalone registers <c>NativeEmailAccIngestionExecutor</c>; V2 may override with the legacy bridge.
/// </summary>
public interface IEmailAccIngestionExecutor
{
    Task<EmailAccUploadResult> IngestToInboxAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken = default);
}
