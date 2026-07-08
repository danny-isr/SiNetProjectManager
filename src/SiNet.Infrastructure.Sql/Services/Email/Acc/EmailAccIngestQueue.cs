using System.Collections.Concurrent;
using SiNet.Application.Email;
using SiNet.Application.Email.Acc;

namespace SiNet.Infrastructure.Sql.Services.Email.Acc;

internal sealed class EmailAccIngestQueue(
    IEmailAccUploadCoordinator uploadCoordinator,
    IEmailAccBackgroundWorkTracker backgroundWorkTracker) : IEmailAccIngestQueue
{
    public const int DefaultMaxConcurrency = 5;

    private readonly IEmailAccUploadCoordinator _uploadCoordinator =
        uploadCoordinator ?? throw new ArgumentNullException(nameof(uploadCoordinator));
    private readonly IEmailAccBackgroundWorkTracker _backgroundWorkTracker =
        backgroundWorkTracker ?? throw new ArgumentNullException(nameof(backgroundWorkTracker));
    private readonly SemaphoreSlim _concurrency = new(DefaultMaxConcurrency, DefaultMaxConcurrency);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    public int ActiveCount => _inFlight.Count;

    public event Action<int>? ActiveCountChanged;

    public bool IsQueuedOrRunning(string messageUniqueId) =>
        !string.IsNullOrWhiteSpace(messageUniqueId) && _inFlight.ContainsKey(messageUniqueId);

    public async Task<EmailAccUploadResult> EnqueueAsync(
        EmailAccUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var messageUniqueId = EmailAccStatusMapper.ResolveMessageUniqueId(
            command.InternetMessageId,
            command.GmailMessageId);

        if (string.IsNullOrWhiteSpace(messageUniqueId))
        {
            return new EmailAccUploadResult(
                EmailAccUploadOutcome.Failed,
                null,
                null,
                0,
                0,
                "חסר מזהה מייל.",
                0);
        }

        if (!_inFlight.TryAdd(messageUniqueId, 0))
        {
            return new EmailAccUploadResult(
                EmailAccUploadOutcome.InProgress,
                messageUniqueId,
                null,
                0,
                0,
                "המייל כבר בתור העלאה.",
                0);
        }

        NotifyActiveCountChanged();

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var workScope = _backgroundWorkTracker.BeginWork();
        try
        {
            return await _uploadCoordinator
                .UploadToAccInboxAsync(command, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(messageUniqueId, out _);
            _concurrency.Release();
            NotifyActiveCountChanged();
        }
    }

    private void NotifyActiveCountChanged() => ActiveCountChanged?.Invoke(ActiveCount);
}
