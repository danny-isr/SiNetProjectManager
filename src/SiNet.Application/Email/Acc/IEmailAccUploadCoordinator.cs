namespace SiNet.Application.Email.Acc;

/// <summary>
/// Orchestrates explicit ACC inbox upload for the Email Workbench. Delegates to the host-provided
/// ingestion backend (legacy <see cref="SiNetSQL.Services.EmailIngestionService"/> via executor).
/// </summary>
public interface IEmailAccUploadCoordinator
{
    Task<EmailAccUploadResult> UploadToAccInboxAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls DB/reconciliation until upload completes or timeout. Used when another worker holds the lease.
    /// </summary>
    Task<EmailAccInboxStatus?> WaitForCompletionAsync(
        string messageUniqueId,
        string? currentUserLogin,
        TimeSpan pollInterval,
        int maxAttempts,
        Func<bool> shouldContinue,
        CancellationToken cancellationToken = default);
}
