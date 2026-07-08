namespace SiNet.Application.Email.Acc;

/// <summary>
/// Bounded parallel queue for ACC inbox ingest (one slot per message; max concurrency across messages).
/// </summary>
public interface IEmailAccIngestQueue
{
    int ActiveCount { get; }

    event Action<int>? ActiveCountChanged;

    bool IsQueuedOrRunning(string messageUniqueId);

    Task<EmailAccUploadResult> EnqueueAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken = default);
}
