namespace SiNet.Application.Email;

/// <summary>Kind of Gmail label mutation recorded in the local change journal (DEV-009 §4.2).</summary>
public enum GmailLabelJournalAction
{
    Renamed = 0,
    Deleted = 1,
}

/// <summary>Why SiNet mutated the label.</summary>
public enum GmailLabelJournalSource
{
    AutoSync = 0,
    ManualSync = 1,
    DuplicateResolve = 2,
}

/// <summary>One rename/delete recorded for a mailbox (retained ≤ 30 days).</summary>
public sealed record GmailLabelJournalEntry(
    string LabelId,
    GmailLabelJournalAction Action,
    string OldFullPath,
    string? NewFullPath,
    int? ProjectNumber,
    DateTime ChangedAtUtc,
    GmailLabelJournalSource Source,
    IReadOnlyList<string> MessageIds);

/// <summary>On-disk journal file shape (one file per mailbox email).</summary>
public sealed record GmailLabelJournalFile(
    string MailboxEmail,
    IReadOnlyList<GmailLabelJournalEntry> Entries);

/// <summary>
/// Per-mailbox local journal of SiNet-driven Gmail label renames/deletes.
/// See <c>docs/DEV_PLAN_PROJECT_EDIT_AND_RENAME.md</c> §4.2.
/// </summary>
public interface IGmailLabelChangeJournal
{
    /// <summary>Retention hard cap (days). Entries older than this are pruned on write.</summary>
    const int RetentionDays = 30;

    /// <summary>
    /// Appends an entry for <paramref name="mailboxEmail"/> and prunes entries older than
    /// <see cref="RetentionDays"/>. Must not throw for routine I/O after the caller already
    /// mutated Gmail — implementations log and swallow; callers that need fail-closed
    /// (pre-delete) should catch and abort themselves.
    /// </summary>
    Task AppendAsync(
        string mailboxEmail,
        GmailLabelJournalEntry entry,
        CancellationToken cancellationToken = default);
}
