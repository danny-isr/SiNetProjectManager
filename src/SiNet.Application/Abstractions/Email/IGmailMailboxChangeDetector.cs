namespace SiNet.Application.Abstractions.Email;

/// <summary>Result of a Gmail History poll (after paging all tokens).</summary>
public enum GmailHistoryCheckOutcome
{
    /// <summary>No relevant messagesAdded — checkpoint may be advanced immediately.</summary>
    NoRelevantChanges,

    /// <summary>Relevant messagesAdded — reload required; checkpoint stays pending until reload commits.</summary>
    ReloadRequired,

    /// <summary>History id expired (HTTP 404) — caller must run baseline-before-sync full reload.</summary>
    HistoryExpired,

    /// <summary>Transient failure — leave checkpoint unchanged; retry next cycle.</summary>
    TransientFailure,

    /// <summary>Detector not initialized / no checkpoint yet.</summary>
    NotReady,
}

/// <summary>Session-only Gmail mailbox change detector (History API, messageAdded v1).</summary>
public interface IGmailMailboxChangeDetector
{
    ulong? LastHistoryId { get; }

    ulong? PendingHistoryId { get; }

    /// <summary>
    /// Captures profile HistoryId as baseline <b>before</b> a full sync load.
    /// Call <see cref="CommitBaselineAsync"/> only after that load succeeds.
    /// </summary>
    Task<ulong?> CaptureBaselineAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits a previously captured baseline after successful mailbox load.</summary>
    void CommitBaseline(ulong baselineHistoryId);

    /// <summary>
    /// Polls History (all pages). Advances checkpoint immediately when no messagesAdded;
    /// otherwise stores PendingHistoryId and signals reload required.
    /// </summary>
    Task<GmailHistoryCheckOutcome> CheckForChangesAsync(
        EmailMailboxScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>Commits <see cref="PendingHistoryId"/> into <see cref="LastHistoryId"/> after a successful reload.</summary>
    void CommitPendingCheckpoint();

    void Reset();
}
