using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;

namespace SiNet.Infrastructure.Google;

/// <summary>Session-only Gmail History change detector (messageAdded v1).</summary>
public sealed class GmailMailboxChangeDetector(
    IGmailHistoryApi historyApi,
    IAppLogger logger) : IGmailMailboxChangeDetector
{
    private static readonly string[] MessageAddedOnly = ["messageAdded"];

    private readonly IGmailHistoryApi _historyApi =
        historyApi ?? throw new ArgumentNullException(nameof(historyApi));

    private readonly IAppLogger _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly object _gate = new();
    private ulong? _lastHistoryId;
    private ulong? _pendingHistoryId;
    private ulong? _capturedBaseline;

    public ulong? LastHistoryId
    {
        get { lock (_gate) return _lastHistoryId; }
    }

    public ulong? PendingHistoryId
    {
        get { lock (_gate) return _pendingHistoryId; }
    }

    public async Task<ulong?> CaptureBaselineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var baseline = await _historyApi.GetProfileHistoryIdAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _capturedBaseline = baseline;
            }

            _logger.Info($"[GmailHistory] Captured baseline historyId={baseline?.ToString() ?? "null"} before full sync.");
            return baseline;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[GmailHistory] Baseline capture failed (transient): {ex.Message}");
            return null;
        }
    }

    public void CommitBaseline(ulong baselineHistoryId)
    {
        lock (_gate)
        {
            _lastHistoryId = baselineHistoryId;
            _pendingHistoryId = null;
            _capturedBaseline = null;
        }

        _logger.Info($"[GmailHistory] Committed baseline historyId={baselineHistoryId}.");
    }

    public async Task<GmailHistoryCheckOutcome> CheckForChangesAsync(
        EmailMailboxScope scope,
        CancellationToken cancellationToken = default)
    {
        ulong startId;
        lock (_gate)
        {
            if (_lastHistoryId is not ulong last)
                return GmailHistoryCheckOutcome.NotReady;
            startId = last;
        }

        var normalized = EmailMailboxQueryComposer.Normalize(new EmailMailboxQuery { MailboxScope = scope });
        string? labelId = normalized.MailboxScope == EmailMailboxScope.Inbox ? "INBOX" : null;

        try
        {
            var (latestHistoryId, hasMessagesAdded) = await ListAllPagesAsync(
                    startId,
                    labelId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!hasMessagesAdded)
            {
                lock (_gate)
                {
                    _lastHistoryId = latestHistoryId;
                    _pendingHistoryId = null;
                }

                _logger.Info($"[GmailHistory] No messagesAdded; advanced historyId={latestHistoryId}.");
                return GmailHistoryCheckOutcome.NoRelevantChanges;
            }

            lock (_gate)
            {
                _pendingHistoryId = _pendingHistoryId is ulong pending
                    ? Math.Max(pending, latestHistoryId)
                    : latestHistoryId;
            }

            _logger.Info(
                $"[GmailHistory] messagesAdded detected; pendingHistoryId={PendingHistoryId}; reload required.");
            return GmailHistoryCheckOutcome.ReloadRequired;
        }
        catch (GmailHistoryExpiredException ex)
        {
            _logger.Warn($"[GmailHistory] History expired (404): {ex.Message}");
            return GmailHistoryCheckOutcome.HistoryExpired;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[GmailHistory] Transient poll failure; checkpoint unchanged: {ex.Message}");
            return GmailHistoryCheckOutcome.TransientFailure;
        }
    }

    public void CommitPendingCheckpoint()
    {
        lock (_gate)
        {
            if (_pendingHistoryId is ulong pending)
            {
                _lastHistoryId = pending;
                _pendingHistoryId = null;
                _logger.Info($"[GmailHistory] Committed pending historyId={pending} after successful reload.");
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastHistoryId = null;
            _pendingHistoryId = null;
            _capturedBaseline = null;
        }
    }

    private async Task<(ulong LatestHistoryId, bool HasMessagesAdded)> ListAllPagesAsync(
        ulong startHistoryId,
        string? labelId,
        CancellationToken cancellationToken)
    {
        string? pageToken = null;
        var hasAdded = false;
        ulong latest = startHistoryId;

        do
        {
            var page = await _historyApi.ListHistoryPageAsync(
                    startHistoryId,
                    labelId,
                    MessageAddedOnly,
                    pageToken,
                    cancellationToken)
                .ConfigureAwait(false);

            if (page.HasMessagesAdded)
                hasAdded = true;

            latest = page.HistoryId;
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return (latest, hasAdded);
    }
}
